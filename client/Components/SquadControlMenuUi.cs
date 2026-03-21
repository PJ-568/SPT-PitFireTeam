using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Settings;
using friendlySAIN.Modules;
using friendlySAIN.Patches;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using PlayerIcons;
using SPT.Common.Http;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace friendlySAIN.Components
{
    internal class SquadControlMenuUi : MonoBehaviour
    {
        private const string SquadButtonName = "friendlySAIN_SquadControlButton";
        private const string ScreenRootName = "friendlySAIN_SquadControlScreen";
        private const float RosterTileWidth = 190f;
        private const float RosterTileHeight = 214f;
        private const float RosterTileSpacing = 18f;
        private const float RosterViewportPadding = 20f;
        private const float RosterViewportTopInset = 44f;
        private const float RosterViewportBottomInset = 26f;
        private const float RosterHeightScreenRatio = 0.54f;
        private const float RosterShellToButtonGap = -14f;
        private const float RosterBlockVerticalOffset = -24f;
        private const float EmptyRosterButtonCenterY = -36f;
        private const float EmptyRosterLabelSpacing = 92f;
        private const float SettingsViewportTopInset = 34f;
        private const float SettingsViewportBottomInset = 28f;
        private const float SettingsViewportSideInset = 24f;
        private const float SettingsRowHeight = 112f;
        private const float SettingsHeaderHeight = 42f;
        private const float SettingsSpacing = 16f;
        private const float SettingsControlRightInset = 50f;
        private const float SettingsShortcutRightInset = 140f;
        private const float SettingsSliderVerticalOffset = 36f;

        private static readonly FieldInfo HeaderLabelField = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static readonly FieldInfo TraderCloseButtonField = AccessTools.Field(typeof(TraderScreensGroup), "_closeButton");
        private static readonly FieldInfo TraderCardsContainerField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardsContainer");
        private static readonly FieldInfo RagfairAllOffersToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_allOffersToggle");
        private static readonly FieldInfo RagfairWishListToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_wishListToggle");
        private static readonly FieldInfo SpawnableToggleHeaderLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_headerLabel");
        private static readonly FieldInfo SpawnableToggleSizeLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_sizeLabel");
        private static readonly FieldInfo TradingPlayerPanelIconField = AccessTools.Field(typeof(TradingPlayerPanel), "_playerIconImage");
        private static readonly FieldInfo SettingsScreenGameTabField = AccessTools.Field(typeof(SettingsScreen), "_gameSettingsScreen");
        private static readonly FieldInfo GameSettingsToggleTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_enableBlockInvites");
        private static readonly FieldInfo GameSettingsSliderTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_fov");
        private static readonly FieldInfo NumberSliderValueInputField = AccessTools.Field(typeof(NumberSlider), "_valueInput");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private const string TeammatesRoute = "/singleplayer/friendlysain/teammates";
        private static Sprite squadIconSprite;

        private MenuScreen menuScreen;
        private DefaultUIButton playerButton;
        private DefaultUIButton tradeButton;
        private DefaultUIButton squadButton;
        private GameObject screenRoot;
        private DefaultUIButton backButton;
        private Tab rosterTab;
        private Tab settingsTab;
        private UIAnimatedToggleSpawner rosterAnimatedTab;
        private UIAnimatedToggleSpawner settingsAnimatedTab;
        private ToggleGroup tabToggleGroup;
        private RectTransform stockCardsContainer;
        private RectTransform rosterPanelRect;
        private ScrollRectNoDrag rosterScrollRect;
        private RectTransform rosterViewport;
        private RectTransform rosterContentRoot;
        private RectTransform rosterGridRoot;
        private GridLayoutGroup rosterGridLayout;
        private Scrollbar rosterScrollbar;
        private ScrollRectNoDrag settingsScrollRect;
        private RectTransform settingsViewport;
        private RectTransform settingsContentRoot;
        private VerticalLayoutGroup settingsLayoutGroup;
        private Scrollbar settingsScrollbar;
        private GameObject rosterPanel;
        private GameObject settingsPanel;
        private DefaultUIButton addTeammateButton;
        private TextMeshProUGUI emptyRosterLabel;
        private GameObject removeConfirmOverlay;
        private float currentRosterShellHeight;
        private Button activeShortcutCaptureButton;
        private TextMeshProUGUI activeShortcutCaptureLabel;
        private ConfigEntry<KeyboardShortcut> activeShortcutCaptureEntry;
        private readonly Dictionary<RectTransform, Vector2> originalButtonPositions = new Dictionary<RectTransform, Vector2>();
        private readonly StringBuilder shortcutBuilder = new StringBuilder();
        private int rosterBuildVersion;

        private sealed class SquadRosterEntry
        {
            public string AccountId { get; set; } = string.Empty;
            public string SocialMemberId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public int Level { get; set; }
        }

        private sealed class SquadSettingEntry
        {
            public string SectionTitle { get; set; } = string.Empty;
            public ConfigEntryBase Entry { get; set; }
        }

        private sealed class RosterTileHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private Image background;
            private Color normalColor;
            private Color hoverColor;
            private Color pressedColor;
            private bool isHovered;

            public void Configure(Image target, Color normal, Color hover, Color pressed)
            {
                background = target;
                normalColor = normal;
                hoverColor = hover;
                pressedColor = pressed;
                Apply(normalColor);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                isHovered = true;
                Apply(hoverColor);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                Apply(normalColor);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(pressedColor);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverColor : normalColor);
            }

            private void Apply(Color color)
            {
                if (background != null)
                {
                    background.color = color;
                }
            }
        }

        private sealed class TabHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
        {
            private RectTransform rectTransform;
            private Vector3 normalScale = Vector3.one;
            private Vector3 hoverScale = new Vector3(1.025f, 1.025f, 1f);
            private Vector3 pressedScale = new Vector3(0.99f, 0.99f, 1f);
            private bool isHovered;

            public void Configure(RectTransform target)
            {
                rectTransform = target;
                if (rectTransform != null)
                {
                    normalScale = rectTransform.localScale;
                }
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                isHovered = true;
                Apply(hoverScale);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                Apply(normalScale);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(pressedScale);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverScale : normalScale);
            }

            private void Apply(Vector3 scale)
            {
                if (rectTransform != null)
                {
                    rectTransform.localScale = scale;
                }
            }
        }

        public static SquadControlMenuUi GetOrCreate(MenuScreen menuScreen)
        {
            SquadControlMenuUi ui = menuScreen.GetComponent<SquadControlMenuUi>();
            if (ui == null)
            {
                ui = menuScreen.gameObject.AddComponent<SquadControlMenuUi>();
            }

            return ui;
        }

        internal static void ReturnFromProfileToSquadControl()
        {
            if (friendlySAIN.Instance == null)
            {
                return;
            }

            friendlySAIN.Instance.StartCoroutine(ReturnFromProfileCoroutine());
        }

        private static IEnumerator ReturnFromProfileCoroutine()
        {
            const int maxFrames = 180;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                MenuScreen menu = Resources.FindObjectsOfTypeAll<MenuScreen>()
                    .FirstOrDefault(candidate => candidate != null && candidate.gameObject != null);

                if (menu != null && menu.gameObject.activeInHierarchy)
                {
                    SquadControlMenuUi ui = GetOrCreate(menu);
                    ui.OpenScreen();
                    yield break;
                }

                yield return null;
            }

            friendlySAIN.Log.LogWarning("[UI] Timed out waiting for MenuScreen to reactivate before returning to Squad Control.");
        }

        public void Initialize(MenuScreen screen, DefaultUIButton sourcePlayerButton, DefaultUIButton sourceTradeButton)
        {
            menuScreen = screen;
            playerButton = sourcePlayerButton;
            tradeButton = sourceTradeButton;

            if (playerButton == null)
            {
                return;
            }

            EnsureSquadButton();
            EnsureScreen();
            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            SyncButtonVisibility();
        }

        public void SyncButtonVisibility()
        {
            if (squadButton == null || playerButton == null)
            {
                return;
            }

            bool visible = playerButton.gameObject.activeSelf;
            squadButton.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            if (screenRoot == null || !screenRoot.activeInHierarchy || settingsPanel == null || !settingsPanel.activeInHierarchy)
            {
                CancelShortcutCapture(false);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CancelShortcutCapture(true);
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Backspace) || UnityEngine.Input.GetKeyDown(KeyCode.Delete))
            {
                ApplyShortcutCapture(new KeyboardShortcut(KeyCode.None));
                return;
            }

            KeyCode mainKey = FindPressedMainKey();
            if (mainKey == KeyCode.None)
            {
                return;
            }

            ApplyShortcutCapture(new KeyboardShortcut(mainKey, GetPressedModifiers()));
        }

        private void EnsureSquadButton()
        {
            if (squadButton == null)
            {
                Transform existing = playerButton.transform.parent.Find(SquadButtonName);
                if (existing != null)
                {
                    squadButton = existing.GetComponent<DefaultUIButton>();
                }
            }

            if (squadButton == null)
            {
                squadButton = Instantiate(playerButton, playerButton.transform.parent, false);
                squadButton.name = SquadButtonName;
                squadButton.transform.SetSiblingIndex(playerButton.transform.GetSiblingIndex() + 1);
            }

            RectTransform playerRect = playerButton.transform as RectTransform;
            RectTransform tradeRect = tradeButton != null ? tradeButton.transform as RectTransform : null;
            RectTransform squadRect = squadButton.transform as RectTransform;

            if (playerRect != null && squadRect != null)
            {
                CaptureOriginalButtonPositions(playerRect);
                RestoreOriginalButtonPositions();

                float verticalStep = ResolveVerticalStep(playerRect, tradeRect) * 0.82f;
                squadRect.anchorMin = playerRect.anchorMin;
                squadRect.anchorMax = playerRect.anchorMax;
                squadRect.pivot = playerRect.pivot;
                squadRect.sizeDelta = playerRect.sizeDelta;
                squadRect.anchoredPosition = playerRect.anchoredPosition + new Vector2(0f, -verticalStep);

                ShiftButtonsBelowPlayer(playerRect, verticalStep);
            }

            squadButton.SetRawText(GetSocialUiText("SquadControlButton", "Squad Control"), playerButton.HeaderSize);
            squadButton.SetIcon(LoadSquadIcon());
            squadButton.OnClick.RemoveAllListeners();
            squadButton.OnClick.AddListener(OpenScreen);
        }

        private void EnsureScreen()
        {
            if (screenRoot == null)
            {
                Transform existing = menuScreen.transform.Find(ScreenRootName);
                if (existing != null)
                {
                    screenRoot = existing.gameObject;
                }
            }

            if (screenRoot != null)
            {
                return;
            }

            screenRoot = new GameObject(ScreenRootName, typeof(RectTransform), typeof(Image));
            screenRoot.transform.SetParent(menuScreen.transform, false);

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image background = screenRoot.GetComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.06f, 1f);
            background.raycastTarget = true;

            CreateScreenChrome();
            screenRoot.SetActive(false);
        }

        private void CreateScreenChrome()
        {
            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            EnsureTabToggleGroup();
            CreateHeader(rootRect);

            if (!TryCreateStockTraderChrome(rootRect))
            {
                CreateFallbackTabs(rootRect);
                rosterPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlRosterPanel", GetSocialUiText("SquadControlRosterTab", "Roaster"));
                settingsPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab", "Settings"));
                BuildSettingsPanel();
            }

            BringInteractiveChromeToFront();
            ShowTab(true);
        }

        private void CreateHeader(RectTransform rootRect)
        {
            GameObject titleObject = CreateText(
                "friendlySAIN_SquadControlTitle",
                GetSocialUiText("SquadControlTitle", "Squad Control"),
                44,
                TextAlignmentOptions.Center);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(rootRect, false);
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(600f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, -48f);
        }

        private bool TryCreateStockTraderChrome(RectTransform rootRect)
        {
            TraderScreensGroup templateGroup = ResolveTraderScreensGroupTemplate();
            if (templateGroup == null)
            {
                return false;
            }

            DefaultUIButton closeTemplate = TraderCloseButtonField?.GetValue(templateGroup) as DefaultUIButton;
            Transform cardsContainerTemplate = TraderCardsContainerField?.GetValue(templateGroup) as Transform;
            UIAnimatedToggleSpawner rosterTabTemplate = ResolveRagfairToggleTemplate(true);
            UIAnimatedToggleSpawner settingsTabTemplate = ResolveRagfairToggleTemplate(false);

            if (closeTemplate == null || rosterTabTemplate == null || settingsTabTemplate == null || cardsContainerTemplate == null)
            {
                return false;
            }

            backButton = Instantiate(closeTemplate, rootRect, false);
            backButton.name = "friendlySAIN_SquadControlBackButton";
            RectTransform backRect = backButton.transform as RectTransform;
            if (backRect != null)
            {
                backRect.anchorMin = new Vector2(1f, 1f);
                backRect.anchorMax = new Vector2(1f, 1f);
                backRect.pivot = new Vector2(1f, 1f);
                backRect.anchoredPosition = new Vector2(-48f, -34f);
            }

            backButton.OnClick.RemoveAllListeners();
            backButton.OnClick.AddListener(CloseScreen);

            rosterAnimatedTab = Instantiate(rosterTabTemplate, rootRect, false);
            rosterAnimatedTab.name = "friendlySAIN_SquadControlRosterTab";
            settingsAnimatedTab = Instantiate(settingsTabTemplate, rootRect, false);
            settingsAnimatedTab.name = "friendlySAIN_SquadControlSettingsTab";

            ConfigureAnimatedTab(rosterAnimatedTab, new Vector2(-135f, -112f), GetSocialUiText("SquadControlRosterTab", "Roaster"), true, () => ShowTab(true));
            ConfigureAnimatedTab(settingsAnimatedTab, new Vector2(135f, -112f), GetSocialUiText("SquadControlSettingsTab", "Settings"), false, () => ShowTab(false));

            stockCardsContainer = Instantiate(cardsContainerTemplate as RectTransform, rootRect, false);
            stockCardsContainer.name = "friendlySAIN_SquadControlCardsContainer";
            float rosterShellHeight = CalculateRosterShellHeight();
            currentRosterShellHeight = rosterShellHeight;
            stockCardsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            stockCardsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            stockCardsContainer.pivot = new Vector2(0.5f, 0.5f);
            stockCardsContainer.sizeDelta = new Vector2(1180f, rosterShellHeight);
            stockCardsContainer.anchoredPosition = new Vector2(0f, -12f);
            PrepareRosterShellContainer(stockCardsContainer);

            for (int i = stockCardsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(stockCardsContainer.GetChild(i).gameObject);
            }

            rosterPanel = new GameObject("friendlySAIN_SquadControlRosterPanel", typeof(RectTransform));
            rosterPanel.transform.SetParent(rootRect, false);
            RectTransform rosterRect = rosterPanel.GetComponent<RectTransform>();
            rosterPanelRect = rosterRect;
            rosterRect.anchorMin = new Vector2(0.5f, 0.5f);
            rosterRect.anchorMax = new Vector2(0.5f, 0.5f);
            rosterRect.pivot = new Vector2(0.5f, 0.5f);
            rosterRect.sizeDelta = new Vector2(1260f, rosterShellHeight + 180f);
            rosterRect.anchoredPosition = new Vector2(0f, 8f);

            stockCardsContainer.SetParent(rosterRect, false);
            stockCardsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            stockCardsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            stockCardsContainer.pivot = new Vector2(0.5f, 0.5f);
            stockCardsContainer.sizeDelta = new Vector2(1180f, rosterShellHeight);
            CreateScrollableRosterArea(stockCardsContainer);
            CreateEmptyRosterLabel(rosterRect);
            CreateAddTeammateButton(rosterRect);
            UpdateRosterPanelLayout(false);
            RebuildRosterTiles();

            settingsPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab", "Settings"));
            BuildSettingsPanel();
            return true;
        }

        private void OpenScreen()
        {
            if (screenRoot == null)
            {
                return;
            }

            screenRoot.transform.SetAsLastSibling();
            screenRoot.SetActive(true);
            RebuildRosterTiles();
            RebuildSettingsEntries();
            SyncButtonVisibility();
        }

        private void CloseScreen()
        {
            if (screenRoot == null)
            {
                return;
            }

            CloseRemoveConfirmOverlay();
            CancelShortcutCapture(false);
            screenRoot.SetActive(false);
            SyncButtonVisibility();
        }

        private void ShowTab(bool showRoster)
        {
            if (rosterPanel != null)
            {
                rosterPanel.SetActive(showRoster);
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(!showRoster);
            }

            if (rosterTab != null)
            {
                rosterTab.UpdateVisual(showRoster, false);
            }

            if (settingsTab != null)
            {
                settingsTab.UpdateVisual(!showRoster, false);
            }

            if (rosterAnimatedTab != null)
            {
                rosterAnimatedTab.ToggleSilently(showRoster);
            }

            if (settingsAnimatedTab != null)
            {
                settingsAnimatedTab.ToggleSilently(!showRoster);
            }

            if (showRoster)
            {
                CancelShortcutCapture(false);
            }
        }

        private GameObject CreateFallbackContentPanel(string name, string label)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(screenRoot.transform, false);

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1180f, 320f);
            panelRect.anchoredPosition = Vector2.zero;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.09f, 0.11f, 0.12f, 0.94f);

            GameObject labelObject = CreateText($"{name}_Label", label, 30, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(panelRect, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(420f, 48f);
            labelRect.anchoredPosition = Vector2.zero;

            return panel;
        }

        private void CreateFallbackTabs(RectTransform rootRect)
        {
            backButton = CloneMenuButton("friendlySAIN_SquadControlBackButton", GetSocialUiText("SquadControlBack", "Back"));
            RectTransform backRect = backButton.transform as RectTransform;
            backRect.SetParent(rootRect, false);
            backRect.anchorMin = new Vector2(1f, 1f);
            backRect.anchorMax = new Vector2(1f, 1f);
            backRect.pivot = new Vector2(1f, 1f);
            backRect.anchoredPosition = new Vector2(-48f, -36f);
            backButton.OnClick.RemoveAllListeners();
            backButton.OnClick.AddListener(CloseScreen);

            Tab fallbackRosterTab = CreateSimpleFallbackTab("friendlySAIN_SquadControlRosterTab", rootRect, new Vector2(-190f, -132f), GetSocialUiText("SquadControlRosterTab", "Roaster"));
            Tab fallbackSettingsTab = CreateSimpleFallbackTab("friendlySAIN_SquadControlSettingsTab", rootRect, new Vector2(30f, -132f), GetSocialUiText("SquadControlSettingsTab", "Settings"));
            rosterTab = fallbackRosterTab;
            settingsTab = fallbackSettingsTab;

            rosterTab.OnSelectionChanged += (_, selected) =>
            {
                if (selected)
                {
                    ShowTab(true);
                }
            };
            settingsTab.OnSelectionChanged += (_, selected) =>
            {
                if (selected)
                {
                    ShowTab(false);
                }
            };
        }

        private GameObject CreateText(string name, string text, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            TextMeshProUGUI textLabel = textObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                textLabel.font = templateLabel.font;
                textLabel.fontSharedMaterial = templateLabel.fontSharedMaterial;
                textLabel.color = templateLabel.color;
            }
            else
            {
                textLabel.color = Color.white;
            }

            textLabel.text = text;
            textLabel.fontSize = fontSize;
            textLabel.alignment = alignment;
            textLabel.enableWordWrapping = false;
            return textObject;
        }

        private void BuildSettingsPanel()
        {
            if (settingsPanel == null)
            {
                return;
            }

            RectTransform settingsRect = settingsPanel.GetComponent<RectTransform>();
            if (settingsRect == null)
            {
                return;
            }

            float settingsShellHeight = currentRosterShellHeight > 1f
                ? currentRosterShellHeight
                : CalculateRosterShellHeight();

            settingsRect.anchorMin = new Vector2(0.5f, 0.5f);
            settingsRect.anchorMax = new Vector2(0.5f, 0.5f);
            settingsRect.pivot = new Vector2(0.5f, 0.5f);
            settingsRect.sizeDelta = new Vector2(1180f, settingsShellHeight);
            settingsRect.anchoredPosition = new Vector2(0f, -12f);

            for (int index = settingsRect.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsRect.GetChild(index).gameObject);
            }

            settingsScrollRect = null;
            settingsViewport = null;
            settingsContentRoot = null;
            settingsLayoutGroup = null;
            settingsScrollbar = null;

            CreateScrollableSettingsArea(settingsRect);
            RebuildSettingsEntries();
        }

        private void CreateScrollableSettingsArea(RectTransform panelRect)
        {
            if (TryCreateStockSettingsArea(panelRect))
            {
                return;
            }

            GameObject scrollRootObject = new GameObject("friendlySAIN_SquadControlSettingsScroll", typeof(RectTransform), typeof(ScrollRectNoDrag));
            scrollRootObject.transform.SetParent(panelRect, false);
            RectTransform scrollRoot = scrollRootObject.GetComponent<RectTransform>();
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
            scrollRoot.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRoot, false);
            settingsViewport = viewportObject.GetComponent<RectTransform>();
            settingsViewport.anchorMin = Vector2.zero;
            settingsViewport.anchorMax = Vector2.one;
            settingsViewport.offsetMin = Vector2.zero;
            settingsViewport.offsetMax = new Vector2(-22f, 0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            settingsScrollbar = CreateRosterScrollbar(scrollRoot);
            settingsScrollbar.name = "friendlySAIN_SquadControlSettingsScrollbar";

            GameObject contentObject = new GameObject("friendlySAIN_SquadControlSettingsContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(settingsViewport, false);
            settingsContentRoot = contentObject.GetComponent<RectTransform>();
            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = new Vector2(0f, 0f);

            settingsLayoutGroup = contentObject.GetComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = contentObject.GetComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsScrollRect = scrollRootObject.GetComponent<ScrollRectNoDrag>();
            settingsScrollRect.horizontal = false;
            settingsScrollRect.vertical = true;
            settingsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            settingsScrollRect.scrollSensitivity = 30f;
            settingsScrollRect.inertia = true;
            settingsScrollRect.viewport = settingsViewport;
            settingsScrollRect.content = settingsContentRoot;
            settingsScrollRect.verticalScrollbar = settingsScrollbar;
            settingsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.verticalScrollbarSpacing = 6f;
            settingsScrollRect.AutoZeroing = true;
            settingsScrollRect.Alignment = TextAnchor.UpperLeft;
        }

        private void RebuildSettingsEntries()
        {
            if (settingsContentRoot == null)
            {
                return;
            }

            CancelShortcutCapture(false);

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsContentRoot.GetChild(index).gameObject);
            }

            string currentSection = null;
            foreach (SquadSettingEntry setting in BuildSquadSettingsEntries())
            {
                if (!string.Equals(currentSection, setting.SectionTitle, StringComparison.Ordinal))
                {
                    currentSection = setting.SectionTitle;
                    CreateSettingsSectionHeader(currentSection);
                }

                CreateSettingsEntryRow(setting.Entry);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsContentRoot);
            if (settingsScrollRect != null)
            {
                settingsScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private IEnumerable<SquadSettingEntry> BuildSquadSettingsEntries()
        {
            foreach (ConfigEntryBase entry in GetSettingsEntries(
                friendlySAIN.spawnPoint,
                friendlySAIN.botGrenades,
                friendlySAIN.enemyMarker,
                friendlySAIN.statusSound,
                friendlySAIN.enemyRemember,
                friendlySAIN.scanDistance,
                friendlySAIN.patrolRadius,
                friendlySAIN.botTalk,
                friendlySAIN.englishBear,
                friendlySAIN.pingRadioVolume,
                friendlySAIN.pingTime))
            {
                yield return new SquadSettingEntry
                {
                    SectionTitle = friendlySAIN.optionsLang?.baseSettings ?? "Base Settings",
                    Entry = entry
                };
            }

            foreach (ConfigEntryBase entry in GetSettingsEntries(
                friendlySAIN.pingKey,
                friendlySAIN.contactKey,
                friendlySAIN.overThereKey))
            {
                yield return new SquadSettingEntry
                {
                    SectionTitle = friendlySAIN.optionsLang?.inputSettings ?? "Input Settings",
                    Entry = entry
                };
            }

            foreach (ConfigEntryBase entry in GetSettingsEntries(
                friendlySAIN.pickupEnabled,
                friendlySAIN.tieredPickup,
                friendlySAIN.maximumPickup,
                friendlySAIN.recruitPickup,
                friendlySAIN.npcSendMessage,
                friendlySAIN.friendlySAINFLAG,
                friendlySAIN.badGuy,
                friendlySAIN.pmcArmbands))
            {
                yield return new SquadSettingEntry
                {
                    SectionTitle = friendlySAIN.optionsLang?.raidSettings ?? "Raid Settings",
                    Entry = entry
                };
            }

            foreach (ConfigEntryBase entry in GetSettingsEntries(
                friendlySAIN.teleportKey,
                friendlySAIN.healKey,
                friendlySAIN.heatlhMultiplier,
                friendlySAIN.botPrefetch))
            {
                yield return new SquadSettingEntry
                {
                    SectionTitle = friendlySAIN.optionsLang?.miscSettings ?? "Miscellaneous",
                    Entry = entry
                };
            }
        }

        private static IEnumerable<ConfigEntryBase> GetSettingsEntries(params ConfigEntryBase[] entries)
        {
            return entries.Where(entry => entry != null);
        }

        private void CreateSettingsSectionHeader(string title)
        {
            GameObject headerObject = new GameObject($"friendlySAIN_SettingsHeader_{title}", typeof(RectTransform), typeof(LayoutElement));
            headerObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = headerObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsHeaderHeight;
            layout.flexibleWidth = 1f;

            GameObject labelObject = CreateText("Label", title.ToUpperInvariant(), 22f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(headerObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.color = new Color(0.92f, 0.82f, 0.63f, 1f);
            label.fontWeight = FontWeight.Bold;
        }

        private void CreateSettingsEntryRow(ConfigEntryBase entry)
        {
            if (entry == null)
            {
                return;
            }

            GameObject rowObject = new GameObject(
                $"friendlySAIN_Setting_{SanitizeName(entry.Definition.Key)}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            rowObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = rowObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsRowHeight;
            layout.flexibleWidth = 1f;

            Image background = rowObject.GetComponent<Image>();
            background.color = new Color(0.08f, 0.08f, 0.08f, 0.94f);
            background.raycastTarget = true;

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, SettingsRowHeight);

            GameObject nameObject = CreateText("Name", GetSettingDisplayName(entry), 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(rowObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(18f, -38f);
            nameRect.offsetMax = new Vector2(-402f, -8f);

            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;

            GameObject descriptionObject = CreateText("Description", entry.Description?.Description ?? string.Empty, 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(rowObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(18f, 14f);
            descriptionRect.offsetMax = new Vector2(-402f, -42f);

            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 15f;
            descriptionLabel.color = new Color(0.74f, 0.74f, 0.74f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(rowObject.transform, false);
            controlRect.sizeDelta = new Vector2(360f, 72f);
            controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset + controlRect.sizeDelta.x), 0f);

            if (entry.SettingType == typeof(bool))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.sizeDelta = new Vector2(120f, 72f);
                controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset - 20f), 0f);
                CreateBoolSettingControl(controlRect, entry);
                return;
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, SettingsSliderVerticalOffset);
                CreateIntSliderSettingControl(controlRect, entry, acceptableRange);
                return;
            }

            if (entry.SettingType == typeof(KeyboardShortcut))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.anchoredPosition = new Vector2(-SettingsShortcutRightInset, 0f);
                CreateShortcutSettingControl(controlRect, entry as ConfigEntry<KeyboardShortcut>);
                return;
            }

            CreateReadOnlySettingControl(controlRect, entry.BoxedValue?.ToString() ?? string.Empty);
        }

        private void CreateBoolSettingControl(RectTransform parent, ConfigEntryBase entry)
        {
            UpdatableToggle toggle = CloneStockToggle(parent);
            if (toggle == null)
            {
                Toggle fallbackToggle = CreateBasicToggle(parent);
                bool fallbackValue = entry.BoxedValue is bool fallbackBool && fallbackBool;
                fallbackToggle.isOn = fallbackValue;
                fallbackToggle.onValueChanged.AddListener(isOn =>
                {
                    entry.BoxedValue = isOn;
                    friendlySAIN.Instance?.Config.Save();
                });
                return;
            }

            bool currentValue = entry.BoxedValue is bool boolValue && boolValue;
            toggle.UpdateValue(currentValue, false, null, null);
            toggle.Bind(isOn =>
            {
                entry.BoxedValue = isOn;
                friendlySAIN.Instance?.Config.Save();
            });
        }

        private void CreateIntSliderSettingControl(RectTransform parent, ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange)
        {
            NumberSlider slider = CloneStockNumberSlider(parent);
            if (slider == null)
            {
                RectTransform sliderRoot = new GameObject("SliderRoot", typeof(RectTransform)).GetComponent<RectTransform>();
                sliderRoot.SetParent(parent, false);
                Stretch(sliderRoot);

                Slider fallbackSlider = CreateBasicSlider(sliderRoot, out TextMeshProUGUI valueLabel);
                fallbackSlider.minValue = acceptableRange.MinValue;
                fallbackSlider.maxValue = acceptableRange.MaxValue;
                fallbackSlider.wholeNumbers = true;
                fallbackSlider.value = Convert.ToSingle(entry.BoxedValue);
                valueLabel.text = entry.BoxedValue?.ToString() ?? "0";

                fallbackSlider.onValueChanged.AddListener(value =>
                {
                    int boxed = Mathf.RoundToInt(value);
                    if (Equals(entry.BoxedValue, boxed))
                    {
                        return;
                    }

                    entry.BoxedValue = boxed;
                    valueLabel.text = boxed.ToString();
                    friendlySAIN.Instance?.Config.Save();
                });
                return;
            }

            slider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            slider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            slider.Bind(value =>
            {
                int boxed = Mathf.RoundToInt(value);
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                friendlySAIN.Instance?.Config.Save();
            });
        }

        private void CreateShortcutSettingControl(RectTransform parent, ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
            {
                CreateReadOnlySettingControl(parent, string.Empty);
                return;
            }

            Button button = CreateActionButton(parent, out TextMeshProUGUI label);
            label.text = FormatShortcut(entry.Value);
            button.onClick.AddListener(() =>
            {
                if (ReferenceEquals(activeShortcutCaptureEntry, entry))
                {
                    CancelShortcutCapture(true);
                    return;
                }

                BeginShortcutCapture(entry, button, label);
            });
        }

        private void CreateReadOnlySettingControl(RectTransform parent, string value)
        {
            GameObject valueObject = CreateText("Value", value, 18f, TextAlignmentOptions.MidlineRight);
            valueObject.transform.SetParent(parent, false);
            RectTransform valueRect = valueObject.GetComponent<RectTransform>();
            Stretch(valueRect);
        }

        private bool TryCreateStockSettingsArea(RectTransform panelRect)
        {
            ScrollRectNoDrag template = ResolveSettingsScrollRectTemplate();
            if (template == null || template.content == null || template.viewport == null)
            {
                return false;
            }

            settingsScrollRect = Instantiate(template, panelRect, false);
            settingsScrollRect.name = "friendlySAIN_SquadControlSettingsScroll";

            RectTransform scrollRect = settingsScrollRect.transform as RectTransform;
            if (scrollRect != null)
            {
                Stretch(scrollRect);
                scrollRect.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
                scrollRect.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);
                scrollRect.localScale = Vector3.one;
            }

            settingsViewport = settingsScrollRect.viewport;
            settingsContentRoot = settingsScrollRect.content;
            settingsScrollbar = settingsScrollRect.verticalScrollbar;

            if (settingsContentRoot == null)
            {
                return false;
            }

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsContentRoot.GetChild(index).gameObject);
            }

            settingsLayoutGroup = settingsContentRoot.GetComponent<VerticalLayoutGroup>() ?? settingsContentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = settingsContentRoot.GetComponent<ContentSizeFitter>() ?? settingsContentRoot.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = Vector2.zero;
            settingsContentRoot.localScale = Vector3.one;

            return true;
        }

        private bool CreateStockSettingsEntryRow(ConfigEntryBase entry)
        {
            if (settingsContentRoot == null)
            {
                return false;
            }

            if (entry.SettingType == typeof(bool))
            {
                return CreateStockToggleRow(entry);
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                return CreateStockSliderRow(entry, acceptableRange);
            }

            return false;
        }

        private bool CreateStockToggleRow(ConfigEntryBase entry)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"friendlySAIN_StockToggleRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            UpdatableToggle clonedToggle = FindComponentByPath<UpdatableToggle>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedToggle == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            clonedToggle.group = null;
            clonedToggle.UpdateValue(entry.BoxedValue is bool boolValue && boolValue, false, null, null);
            clonedToggle.Bind(isOn =>
            {
                entry.BoxedValue = isOn;
                friendlySAIN.Instance?.Config.Save();
            });

            return true;
        }

        private bool CreateStockSliderRow(ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange)
        {
            NumberSlider template = ResolveSettingsSliderTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"friendlySAIN_StockSliderRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            NumberSlider clonedSlider = FindComponentByPath<NumberSlider>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedSlider == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            clonedSlider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            clonedSlider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            clonedSlider.Bind(value =>
            {
                int boxed = Mathf.RoundToInt(value);
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                friendlySAIN.Instance?.Config.Save();
            });

            return true;
        }

        private void ConfigureStockSettingsRow(RectTransform rowRect, RectTransform templateRect)
        {
            if (rowRect == null)
            {
                return;
            }

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.localScale = Vector3.one;

            LayoutElement layout = rowRect.GetComponent<LayoutElement>() ?? rowRect.gameObject.AddComponent<LayoutElement>();
            float preferredHeight = templateRect != null && templateRect.rect.height > 1f ? templateRect.rect.height : rowRect.rect.height;
            if (preferredHeight < 36f)
            {
                preferredHeight = 48f;
            }

            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = 1f;
        }

        private Transform ResolveSettingsRowTemplateRoot(Component control)
        {
            if (control == null)
            {
                return null;
            }

            Transform controlTransform = control.transform;
            Type controlType = control.GetType();
            Transform candidate = controlTransform;

            for (Transform current = controlTransform.parent; current != null; current = current.parent)
            {
                if (current.GetComponentsInChildren(controlType, true).Length != 1)
                {
                    break;
                }

                candidate = current;
            }

            return candidate;
        }

        private void ApplyStockSettingsLabelText(Transform rowTemplate, Transform clonedRow, Transform controlTransform, string text)
        {
            if (rowTemplate == null || clonedRow == null || controlTransform == null)
            {
                return;
            }

            TextMeshProUGUI[] templateLabels = rowTemplate
                .GetComponentsInChildren<TextMeshProUGUI>(true)
                .Where(label =>
                    label != null &&
                    !string.IsNullOrWhiteSpace(label.text) &&
                    !IsAncestorOf(controlTransform, label.transform))
                .ToArray();

            foreach (TextMeshProUGUI templateLabel in templateLabels)
            {
                TextMeshProUGUI clonedLabel = FindComponentByPath<TextMeshProUGUI>(rowTemplate, clonedRow, templateLabel.transform);
                if (clonedLabel != null)
                {
                    clonedLabel.text = text;
                }
            }
        }

        private UpdatableToggle CloneStockToggle(RectTransform parent)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return null;
            }

            UpdatableToggle toggle = Instantiate(template, parent, false);
            toggle.name = "friendlySAIN_StockToggle";
            RectTransform rect = toggle.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            toggle.gameObject.SetActive(true);
            toggle.group = null;
            HideStockLabelContainers(toggle.transform, null);
            return toggle;
        }

        private NumberSlider CloneStockNumberSlider(RectTransform parent)
        {
            RectTransform templateRoot = ResolveSettingsSliderContainerTemplate();
            if (templateRoot == null)
            {
                return null;
            }

            RectTransform sliderRoot = Instantiate(templateRoot, parent, false);
            NumberSlider slider = sliderRoot.GetComponent<NumberSlider>() ?? sliderRoot.GetComponentInChildren<NumberSlider>(true);
            if (slider == null)
            {
                Destroy(sliderRoot.gameObject);
                return null;
            }

            slider.name = "friendlySAIN_StockNumberSlider";
            if (sliderRoot != null)
            {
                Stretch(sliderRoot);
                sliderRoot.localScale = Vector3.one;
            }

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            HideStockLabelContainers(sliderRoot, valueInput?.transform);

            slider.gameObject.SetActive(true);
            return slider;
        }

        private void HideStockLabelContainers(Transform root, Transform exemptRoot)
        {
            if (root == null)
            {
                return;
            }

            HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }
        }

        private void HideStockLabelContainer(Transform root, Transform exemptRoot, Transform labelTransform, HashSet<GameObject> hiddenObjects)
        {
            if (root == null || labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && IsAncestorOf(exemptRoot, labelTransform))
            {
                return;
            }

            Transform directChild = ResolveDirectChildContainer(root, labelTransform);
            if (directChild == null || directChild == root || directChild == exemptRoot || (exemptRoot != null && IsAncestorOf(directChild, exemptRoot)))
            {
                return;
            }

            if (hiddenObjects.Add(directChild.gameObject))
            {
                directChild.gameObject.SetActive(false);
            }
        }

        private static Transform ResolveDirectChildContainer(Transform root, Transform descendant)
        {
            if (root == null || descendant == null)
            {
                return null;
            }

            if (ReferenceEquals(root, descendant))
            {
                return root;
            }

            Transform current = descendant;
            while (current != null && current.parent != root)
            {
                current = current.parent;
            }

            return current == null ? descendant : current;
        }

        private Toggle CreateBasicToggle(RectTransform parent)
        {
            GameObject root = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0.5f);
            rootRect.anchorMax = new Vector2(1f, 0.5f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.sizeDelta = new Vector2(92f, 38f);
            rootRect.anchoredPosition = Vector2.zero;

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(root.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            GameObject checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform checkRect = checkmarkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(76f, 24f);
            checkRect.anchoredPosition = Vector2.zero;
            Image checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            toggle.colors = colors;

            return toggle;
        }

        private Slider CreateBasicSlider(RectTransform parent, out TextMeshProUGUI valueLabel)
        {
            valueLabel = CreateText("Value", "0", 18f, TextAlignmentOptions.MidlineRight).GetComponent<TextMeshProUGUI>();
            valueLabel.transform.SetParent(parent, false);
            RectTransform valueRect = valueLabel.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(52f, 26f);
            valueRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject sliderRoot = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderRoot.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderRoot.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.offsetMin = new Vector2(0f, -10f);
            sliderRect.offsetMax = new Vector2(-64f, 10f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 8f);
            backgroundRect.anchoredPosition = Vector2.zero;
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 1f);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            Stretch(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(0f, 0f);
            fillAreaRect.offsetMax = new Vector2(-18f, 0f);

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 8f);
            fillRect.anchoredPosition = Vector2.zero;
            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            GameObject handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleSlideArea.transform.SetParent(sliderRoot.transform, false);
            RectTransform slideAreaRect = handleSlideArea.GetComponent<RectTransform>();
            Stretch(slideAreaRect);
            slideAreaRect.offsetMin = new Vector2(8f, -6f);
            slideAreaRect.offsetMax = new Vector2(-8f, 6f);

            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(handleSlideArea.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.96f, 0.89f, 0.74f, 1f);

            Slider slider = sliderRoot.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            slider.colors = colors;

            return slider;
        }

        private Button CreateActionButton(RectTransform parent, out TextMeshProUGUI label)
        {
            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(244f, 42f);
            buttonRect.anchoredPosition = Vector2.zero;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            background.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            button.colors = colors;

            GameObject labelObject = CreateText("Label", string.Empty, 18f, TextAlignmentOptions.Center);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        private void BeginShortcutCapture(ConfigEntry<KeyboardShortcut> entry, Button button, TextMeshProUGUI label)
        {
            CancelShortcutCapture(false);
            activeShortcutCaptureEntry = entry;
            activeShortcutCaptureButton = button;
            activeShortcutCaptureLabel = label;
            if (activeShortcutCaptureLabel != null)
            {
                activeShortcutCaptureLabel.text = GetSocialUiText("SettingsPressKey", "Press key...");
            }
        }

        private void CancelShortcutCapture(bool refreshLabel)
        {
            ConfigEntry<KeyboardShortcut> capturedEntry = activeShortcutCaptureEntry;
            TextMeshProUGUI capturedLabel = activeShortcutCaptureLabel;

            activeShortcutCaptureEntry = null;
            activeShortcutCaptureButton = null;
            activeShortcutCaptureLabel = null;

            if (refreshLabel && capturedEntry != null && capturedLabel != null)
            {
                capturedLabel.text = FormatShortcut(capturedEntry.Value);
            }
        }

        private void ApplyShortcutCapture(KeyboardShortcut shortcut)
        {
            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            activeShortcutCaptureEntry.Value = shortcut;
            friendlySAIN.Instance?.Config.Save();
            CancelShortcutCapture(true);
        }

        private KeyCode FindPressedMainKey()
        {
            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (IsModifierKey(keyCode))
                {
                    continue;
                }

                if (UnityEngine.Input.GetKeyDown(keyCode))
                {
                    return keyCode;
                }
            }

            return KeyCode.None;
        }

        private static bool IsModifierKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return true;
                default:
                    return false;
            }
        }

        private static KeyCode[] GetPressedModifiers()
        {
            List<KeyCode> modifiers = new List<KeyCode>(3);
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            {
                modifiers.Add(KeyCode.LeftControl);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
            {
                modifiers.Add(KeyCode.LeftShift);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt))
            {
                modifiers.Add(KeyCode.LeftAlt);
            }

            return modifiers.ToArray();
        }

        private string FormatShortcut(KeyboardShortcut shortcut)
        {
            bool hasModifiers = shortcut.Modifiers != null && shortcut.Modifiers.Any();
            if (shortcut.MainKey == KeyCode.None && !hasModifiers)
            {
                return GetSocialUiText("SettingsNotBound", "Not Bound");
            }

            shortcutBuilder.Clear();
            if (hasModifiers)
            {
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    if (shortcutBuilder.Length > 0)
                    {
                        shortcutBuilder.Append(" + ");
                    }

                    shortcutBuilder.Append(FormatKeyCode(modifier));
                }
            }

            if (shortcut.MainKey != KeyCode.None)
            {
                if (shortcutBuilder.Length > 0)
                {
                    shortcutBuilder.Append(" + ");
                }

                shortcutBuilder.Append(FormatKeyCode(shortcut.MainKey));
            }

            return shortcutBuilder.ToString();
        }

        private static string FormatKeyCode(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.None:
                    return "None";
                default:
                    return keyCode.ToString();
            }
        }

        private static string GetSettingDisplayName(ConfigEntryBase entry)
        {
            string key = entry.Definition.Key ?? string.Empty;
            int index = 0;
            while (index < key.Length && (char.IsDigit(key[index]) || char.IsWhiteSpace(key[index])))
            {
                index++;
            }

            string trimmed = key.Substring(index).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? key : trimmed;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Setting";
            }

            char[] buffer = value.ToCharArray();
            for (int index = 0; index < buffer.Length; index++)
            {
                if (!char.IsLetterOrDigit(buffer[index]))
                {
                    buffer[index] = '_';
                }
            }

            return new string(buffer);
        }

        private DefaultUIButton CloneMenuButton(string name, string text)
        {
            DefaultUIButton button = Instantiate(playerButton, screenRoot.transform, false);
            button.name = name;
            button.SetRawText(text, playerButton.HeaderSize);
            button.SetIcon(null);
            button.Interactable = true;
            return button;
        }

        private void BringInteractiveChromeToFront()
        {
            backButton?.transform.SetAsLastSibling();
            rosterAnimatedTab?.transform.SetAsLastSibling();
            settingsAnimatedTab?.transform.SetAsLastSibling();
            rosterTab?.transform.SetAsLastSibling();
            settingsTab?.transform.SetAsLastSibling();
        }

        private Tab CreateSimpleFallbackTab(string name, RectTransform parent, Vector2 anchoredPosition, string label)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Tab));
            // Add CanvasGroup component for stock UI compatibility (use reflection to avoid assembly reference)
            try
            {
                Type canvasGroupType = Type.GetType("UnityEngine.CanvasGroup, UnityEngine.UIModule");
                if (canvasGroupType != null)
                {
                    root.AddComponent(canvasGroupType);
                }
            }
            catch { }

            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(160f, 40f);

            Image image = root.GetComponent<Image>();
            image.color = new Color(0.14f, 0.14f, 0.14f, 1f);

            GameObject normal = new GameObject("Normal", typeof(RectTransform));
            normal.transform.SetParent(root.transform, false);
            Stretch(normal.GetComponent<RectTransform>());

            GameObject selected = new GameObject("Selected", typeof(RectTransform), typeof(Image));
            selected.transform.SetParent(root.transform, false);
            Stretch(selected.GetComponent<RectTransform>());
            selected.GetComponent<Image>().color = new Color(0.35f, 0.21f, 0.08f, 1f);

            TextMeshProUGUI normalText = CreateTabText(normal.transform, label);
            TextMeshProUGUI selectedText = CreateTabText(selected.transform, label);
            selectedText.color = new Color(0.95f, 0.88f, 0.74f, 1f);

            Traverse.Create(root.GetComponent<Tab>()).Field("_normalVersion").SetValue(normal);
            Traverse.Create(root.GetComponent<Tab>()).Field("_selectedVersion").SetValue(selected);
            Traverse.Create(root.GetComponent<Tab>()).Field("_targetImage").SetValue(image);
            root.GetComponent<Tab>().OnAwake();
            return root.GetComponent<Tab>();
        }

        private TextMeshProUGUI CreateTabText(Transform parent, string label)
        {
            GameObject textObject = CreateText("Label", label.ToUpperInvariant(), 18f, TextAlignmentOptions.Center);
            textObject.transform.SetParent(parent, false);
            Stretch(textObject.GetComponent<RectTransform>());
            return textObject.GetComponent<TextMeshProUGUI>();
        }

        private void ConfigureAnimatedTab(UIAnimatedToggleSpawner tab, Vector2 anchoredPosition, string label, bool selected, Action onSelected)
        {
            RectTransform rect = tab.transform as RectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;

            UISpawnableToggle spawnableToggle = tab.SpawnableToggle;
            if (tabToggleGroup != null)
            {
                spawnableToggle.method_1(tabToggleGroup);
            }

            if (SpawnableToggleHeaderLabelField?.GetValue(spawnableToggle) is TextMeshProUGUI headerLabel)
            {
                headerLabel.text = label.ToUpperInvariant();
            }

            if (SpawnableToggleSizeLabelField?.GetValue(spawnableToggle) is TextMeshProUGUI sizeLabel)
            {
                sizeLabel.text = label.ToUpperInvariant();
            }

            foreach (TextMeshProUGUI text in tab.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = label.ToUpperInvariant();
            }

            tab.SetActive(true);
            spawnableToggle.Interactable = true;
            if (tab.SpawnedObject != null)
            {
                tab.SpawnedObject.group = tabToggleGroup;
                tab.SpawnedObject.interactable = true;
            }

            TabHoverController hoverController = tab.gameObject.GetComponent<TabHoverController>();
            if (hoverController == null)
            {
                hoverController = tab.gameObject.AddComponent<TabHoverController>();
            }

            hoverController.Configure(rect);

            tab.SpawnedObject.onValueChanged.RemoveAllListeners();
            tab.SpawnedObject.onValueChanged.AddListener(isSelected =>
            {
                if (isSelected)
                {
                    onSelected();
                }
            });

            tab.ToggleSilently(selected);
        }

        private void EnsureTabToggleGroup()
        {
            if (screenRoot == null)
            {
                return;
            }

            tabToggleGroup = screenRoot.GetComponent<ToggleGroup>();
            if (tabToggleGroup == null)
            {
                tabToggleGroup = screenRoot.AddComponent<ToggleGroup>();
            }

            tabToggleGroup.allowSwitchOff = false;
        }

        private UIAnimatedToggleSpawner ResolveRagfairToggleTemplate(bool primary)
        {
            return Resources.FindObjectsOfTypeAll<EFT.UI.Ragfair.RagfairScreen>()
                .Select(screen => primary
                    ? RagfairAllOffersToggleField?.GetValue(screen) as UIAnimatedToggleSpawner
                    : RagfairWishListToggleField?.GetValue(screen) as UIAnimatedToggleSpawner)
                .FirstOrDefault(toggle => toggle != null);
        }

        private void RebuildRosterTiles()
        {
            if (rosterGridRoot == null)
            {
                return;
            }

            CloseRemoveConfirmOverlay();
            rosterBuildVersion++;

            for (int i = rosterGridRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(rosterGridRoot.GetChild(i).gameObject);
            }

            List<SquadRosterEntry> entries = BuildRosterEntries().ToList();
            UpdateRosterGridLayout(entries.Count);

            foreach (SquadRosterEntry entry in entries)
            {
                CreateRosterTile(rosterGridRoot, entry, rosterBuildVersion);
            }

            UpdateEmptyRosterState(entries.Count == 0);

            if (rosterGridRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterGridRoot);
            }

            if (rosterContentRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterContentRoot);
            }

            if (rosterPanel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterPanel.GetComponent<RectTransform>());
            }

            if (rosterScrollRect != null)
            {
                rosterScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private IEnumerable<SquadRosterEntry> BuildRosterEntries()
        {
            List<SquadRosterEntry> entries = new List<SquadRosterEntry>();

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    JToken root = JToken.Parse(response);
                    JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                    if (dataToken is JArray teammates)
                    {
                        foreach (JToken teammate in teammates)
                        {
                            string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString() ?? string.Empty;
                            string socialMemberId = teammate?["Id"]?.ToString() ?? teammate?["id"]?.ToString() ?? string.Empty;
                            string nickname = teammate?["Info"]?["Nickname"]?.ToString() ?? teammate?["info"]?["nickname"]?.ToString() ?? string.Empty;
                            int level = ParseLevel(teammate?["Info"]?["Level"]?.ToString() ?? teammate?["info"]?["level"]?.ToString());
                            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(nickname))
                            {
                                continue;
                            }

                            entries.Add(new SquadRosterEntry
                            {
                                AccountId = accountId,
                                SocialMemberId = socialMemberId,
                                Nickname = nickname,
                                Level = level
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI] Failed to build Squad Control roster.");
                friendlySAIN.Log.LogError(ex);
            }

            return entries
                .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
                .Select(group => group.First());
        }

        private static int ParseLevel(string value)
        {
            return int.TryParse(value, out int level) ? Mathf.Max(1, level) : 1;
        }

        private void CreateAddTeammateButton(RectTransform rosterRect)
        {
            if (rosterRect == null || playerButton == null)
            {
                return;
            }

            if (addTeammateButton != null)
            {
                Destroy(addTeammateButton.gameObject);
                addTeammateButton = null;
            }

            addTeammateButton = Instantiate(playerButton, rosterRect, false);
            addTeammateButton.name = "friendlySAIN_SquadControlAddTeammateButton";
            addTeammateButton.SetRawText(GetSocialUiText("AddTeammate", "+ Add teammate"), playerButton.HeaderSize);
            addTeammateButton.SetIcon(null);
            addTeammateButton.Interactable = true;
            addTeammateButton.OnClick.RemoveAllListeners();
            addTeammateButton.OnClick.AddListener(OnAddTeammateClicked);

            RectTransform buttonRect = addTeammateButton.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.sizeDelta = new Vector2(320f, playerButton.transform is RectTransform playerRect ? playerRect.sizeDelta.y : 54f);
                buttonRect.anchoredPosition = Vector2.zero;
            }
        }

        private void OnAddTeammateClicked()
        {
            CloseScreen();
            AddTeammateCreationFlow.Start(OpenScreen);
        }

        private void CreateEmptyRosterLabel(RectTransform rosterRect)
        {
            if (rosterRect == null)
            {
                return;
            }

            if (emptyRosterLabel != null)
            {
                Destroy(emptyRosterLabel.gameObject);
                emptyRosterLabel = null;
            }

            GameObject textObject = CreateText(
                "friendlySAIN_EmptyRosterLabel",
                GetSocialUiText("SquadControlEmptyRoster", "You have not created any team members yet, press the add button below to get started"),
                24f,
                TextAlignmentOptions.Center);
            textObject.transform.SetParent(rosterRect, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(760f, 90f);
            textRect.anchoredPosition = Vector2.zero;

            emptyRosterLabel = textObject.GetComponent<TextMeshProUGUI>();
            emptyRosterLabel.enableWordWrapping = true;
            emptyRosterLabel.overflowMode = TextOverflowModes.Ellipsis;
            emptyRosterLabel.gameObject.SetActive(false);
        }

        private void UpdateEmptyRosterState(bool isEmpty)
        {
            if (emptyRosterLabel == null)
            {
                return;
            }

            emptyRosterLabel.text = GetSocialUiText(
                "SquadControlEmptyRoster",
                "You have not created any team members yet, press the add button below to get started");
            emptyRosterLabel.gameObject.SetActive(isEmpty);

            UpdateRosterPanelLayout(isEmpty);
        }

        private void CreateRosterTile(RectTransform parent, SquadRosterEntry entry, int buildVersion)
        {
            GameObject tileObject = new GameObject(
                $"friendlySAIN_RosterTile_{entry.AccountId}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement),
                typeof(Button));
            tileObject.transform.SetParent(parent, false);

            RectTransform tileRect = tileObject.GetComponent<RectTransform>();
            tileRect.sizeDelta = new Vector2(RosterTileWidth, RosterTileHeight);

            LayoutElement layoutElement = tileObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = RosterTileWidth;
            layoutElement.preferredHeight = RosterTileHeight;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            Image tileBackground = tileObject.GetComponent<Image>();
            Color normalColor = new Color(0.08f, 0.08f, 0.08f, 0.96f);
            Color hoverColor = new Color(0.16f, 0.16f, 0.16f, 0.98f);
            Color pressedColor = new Color(0.24f, 0.24f, 0.24f, 0.98f);
            tileBackground.color = normalColor;

            Button button = tileObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = tileBackground;
            button.onClick.AddListener(() => OpenProfile(entry.AccountId));

            RosterTileHoverController hoverController = tileObject.AddComponent<RosterTileHoverController>();
            hoverController.Configure(tileBackground, normalColor, hoverColor, pressedColor);

            RectTransform portraitHost = CreatePortraitHost(tileRect, entry.Level, out PlayerIconImage iconImage);
            CreateRemoveButton(tileRect, entry);
            CreateRosterNameLabel(tileRect, entry.Nickname);
            StartCoroutine(LoadTeammatePortraitCoroutine(entry.AccountId, iconImage, portraitHost, buildVersion));
        }

        private void CreateRemoveButton(RectTransform tileRect, SquadRosterEntry entry)
        {
            GameObject buttonObject = new GameObject("friendlySAIN_RemoveButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(tileRect, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(24f, 22f);
            rect.localPosition = new Vector3(95f, 107f, 0f);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.88f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(() => ShowRemoveConfirmOverlay(entry));

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.localPosition = new Vector3(-12f, -10f, 0f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = GetSocialUiText("RenameClose", "x");
            label.fontSize = 16f;
            label.fontWeight = FontWeight.Medium;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            label.raycastTarget = false;
        }

        private void ShowRemoveConfirmOverlay(SquadRosterEntry entry)
        {
            CloseRemoveConfirmOverlay();

            if (screenRoot == null || entry == null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("friendlySAIN_RemoveOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screenRoot.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.12f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("friendlySAIN_RemovePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 188f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("friendlySAIN_RemoveHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -28f);
            headerRect.offsetMax = new Vector2(0f, 0f);

            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.06f, 0.06f, 0.06f, 1f);
            headerImage.raycastTarget = true;

            GameObject titleObject = CreateText(
                "friendlySAIN_RemoveTitle",
                GetSocialUiText("RemoveTeammateTitle", "Remove teammate").ToUpperInvariant(),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateWindowCloseButton(header.transform, "friendlySAIN_RemoveCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(CloseRemoveConfirmOverlay);

            GameObject bodyObject = CreateText(
                "friendlySAIN_RemoveBody",
                string.Format(
                    GetSocialUiText("RemoveTeammatePrompt", "Are you sure you want to delete member {0}? Process cannot be undone."),
                    entry.Nickname),
                24f,
                TextAlignmentOptions.Center);
            RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.SetParent(panel.transform, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(28f, 68f);
            bodyRect.offsetMax = new Vector2(-28f, -42f);

            TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

            DefaultUIButton removeButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            removeButton.SetRawText(GetSocialUiText("RemoveTeammateConfirm", "Remove"), 22);
            removeButton.OnClick.RemoveAllListeners();
            removeButton.OnClick.AddListener(() => RemoveTeammate(entry));

            removeConfirmOverlay = overlayRoot;
        }

        private void RemoveTeammate(SquadRosterEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                IChatInteractions chatInteractions = ItemUiContext.Instance?.Session;
                SocialNetworkClass socialNetwork = chatInteractions?.SocialNetwork;
                if (socialNetwork?.FriendsList == null)
                {
                    friendlySAIN.Log.LogError("[UI] Failed to delete teammate: social network is unavailable.");
                    return;
                }

                UpdatableChatMember member = socialNetwork.FriendsList
                    .FirstOrDefault(candidate =>
                        candidate != null &&
                        (!string.IsNullOrWhiteSpace(candidate.AccountId)
                            ? string.Equals(candidate.AccountId, entry.AccountId, StringComparison.Ordinal)
                            : string.Equals(candidate.Id, entry.SocialMemberId, StringComparison.Ordinal)));

                if (member == null)
                {
                    friendlySAIN.Log.LogError($"[UI] Failed to delete teammate '{entry.AccountId}': social member was not found.");
                    return;
                }

                socialNetwork.RemoveFromFriendsList(member, new Callback(_ =>
                {
                    CloseRemoveConfirmOverlay();
                    SocialNetworkClassPatch.RefreshFriendsList();
                    RebuildRosterTiles();
                }));
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to delete teammate '{entry.AccountId}'.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void CloseRemoveConfirmOverlay()
        {
            if (removeConfirmOverlay == null)
            {
                return;
            }

            Destroy(removeConfirmOverlay);
            removeConfirmOverlay = null;
        }

        private RectTransform CreatePortraitHost(RectTransform tileRect, int level, out PlayerIconImage iconImage)
        {
            RectTransform portraitRoot = new GameObject("PortraitRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            portraitRoot.SetParent(tileRect, false);
            portraitRoot.anchorMin = new Vector2(0.5f, 1f);
            portraitRoot.anchorMax = new Vector2(0.5f, 1f);
            portraitRoot.pivot = new Vector2(0.5f, 1f);
            portraitRoot.sizeDelta = new Vector2(142f, 142f);
            portraitRoot.anchoredPosition = new Vector2(0f, -14f);

            if (TryCreateStockPortrait(portraitRoot, level, out iconImage))
            {
                return portraitRoot;
            }

            RectTransform fallbackRoot = new GameObject("PortraitFallback", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            fallbackRoot.SetParent(portraitRoot, false);
            Stretch(fallbackRoot);

            Image preview = fallbackRoot.GetComponent<Image>();
            preview.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            preview.preserveAspect = true;
            preview.sprite = LoadSquadIcon();

            GameObject progress = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            progress.transform.SetParent(fallbackRoot, false);
            RectTransform progressRect = progress.GetComponent<RectTransform>();
            progressRect.anchorMin = new Vector2(0.5f, 0.5f);
            progressRect.anchorMax = new Vector2(0.5f, 0.5f);
            progressRect.pivot = new Vector2(0.5f, 0.5f);
            progressRect.sizeDelta = new Vector2(28f, 28f);
            progress.SetActive(false);

            Image progressImage = progress.GetComponent<Image>();
            progressImage.color = new Color(0.9f, 0.9f, 0.88f, 0.85f);
            progressImage.sprite = LoadSquadIcon();

            iconImage = new PlayerIconImage
            {
                _pmcPreview = preview,
                _progress = progress
            };

            return portraitRoot;
        }

        private bool TryCreateStockPortrait(RectTransform parent, int level, out PlayerIconImage iconImage)
        {
            iconImage = null;

            TradingPlayerPanel template = ResolveTradingPlayerPanelTemplate();
            PlayerIconImage templateIcon = TradingPlayerPanelIconField?.GetValue(template) as PlayerIconImage;
            if (templateIcon?._pmcPreview == null || templateIcon._progress == null)
            {
                return false;
            }

            Transform templateRoot = FindPortraitTemplateRoot(templateIcon);
            if (templateRoot == null)
            {
                return false;
            }

            GameObject clonedRoot = Instantiate(templateRoot.gameObject, parent, false);
            clonedRoot.name = "Portrait";
            RectTransform clonedRect = clonedRoot.transform as RectTransform;
            if (clonedRect != null)
            {
                Stretch(clonedRect);
            }

            Image previewClone = FindComponentByPath<Image>(templateRoot, clonedRoot.transform, templateIcon._pmcPreview.transform);
            GameObject progressClone = FindTransformByPath(templateRoot, clonedRoot.transform, templateIcon._progress.transform)?.gameObject;
            if (previewClone == null || progressClone == null)
            {
                Destroy(clonedRoot);
                return false;
            }

            iconImage = new PlayerIconImage
            {
                _pmcPreview = previewClone,
                _progress = progressClone
            };

            ReplaceStockPortraitChrome(clonedRoot.transform, level);

            return true;
        }

        private void ReplaceStockPortraitChrome(Transform clonedRoot, int level)
        {
            if (clonedRoot == null)
            {
                return;
            }

            Transform background = clonedRoot.Find("Background");
            if (background != null)
            {
                background.gameObject.SetActive(false);
            }

            Transform levelRoot = clonedRoot.Find("Level");
            if (levelRoot != null)
            {
                levelRoot.gameObject.SetActive(false);
            }

            CreatePortraitBackground(clonedRoot);
            CreatePortraitLevelBadge(clonedRoot, level);
        }

        private static void CreatePortraitBackground(Transform clonedRoot)
        {
            GameObject backgroundObject = new GameObject("friendlySAIN_PortraitBackground", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(clonedRoot, false);
            backgroundObject.transform.SetAsFirstSibling();

            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);

            Image backgroundImage = backgroundObject.GetComponent<Image>();
            backgroundImage.color = new Color(0.14f, 0.15f, 0.16f, 1f);
            backgroundImage.raycastTarget = false;
        }

        private void CreatePortraitLevelBadge(Transform clonedRoot, int level)
        {
            GameObject badgeObject = new GameObject("friendlySAIN_LevelBadge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(clonedRoot, false);

            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.sizeDelta = new Vector2(44f, 36f);
            badgeRect.anchoredPosition = new Vector2(0f, 0f);

            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.color = new Color(0f, 0f, 0f, 0f);
            badgeImage.raycastTarget = false;

            GameObject labelObject = new GameObject("LevelLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(badgeObject.transform, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.localPosition = new Vector3(17f, -14f, 0f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = level.ToString("D2");
            label.fontSize = 18f;
            label.fontWeight = FontWeight.SemiBold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            label.raycastTarget = false;
        }

        private Transform FindPortraitTemplateRoot(PlayerIconImage templateIcon)
        {
            Transform previewTransform = templateIcon._pmcPreview.transform;
            Transform progressTransform = templateIcon._progress.transform;
            if (previewTransform == null || progressTransform == null)
            {
                return null;
            }

            HashSet<Transform> previewAncestors = new HashSet<Transform>();
            for (Transform current = previewTransform; current != null; current = current.parent)
            {
                previewAncestors.Add(current);
            }

            for (Transform current = progressTransform; current != null; current = current.parent)
            {
                if (previewAncestors.Contains(current))
                {
                    return current;
                }
            }

            return previewTransform.parent;
        }

        private static T FindComponentByPath<T>(Transform templateRoot, Transform clonedRoot, Transform target) where T : Component
        {
            Transform clone = FindTransformByPath(templateRoot, clonedRoot, target);
            return clone != null ? clone.GetComponent<T>() : null;
        }

        private static Transform FindTransformByPath(Transform templateRoot, Transform clonedRoot, Transform target)
        {
            if (templateRoot == null || clonedRoot == null || target == null)
            {
                return null;
            }

            List<int> path = new List<int>();
            for (Transform current = target; current != null && current != templateRoot; current = current.parent)
            {
                path.Add(current.GetSiblingIndex());
            }

            if (target != templateRoot && (target.parent == null || !IsAncestorOf(templateRoot, target)))
            {
                return null;
            }

            Transform resolved = clonedRoot;
            for (int i = path.Count - 1; i >= 0; i--)
            {
                int index = path[i];
                if (index < 0 || index >= resolved.childCount)
                {
                    return null;
                }

                resolved = resolved.GetChild(index);
            }

            return resolved;
        }

        private static bool IsAncestorOf(Transform ancestor, Transform target)
        {
            for (Transform current = target; current != null; current = current.parent)
            {
                if (current == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateRosterNameLabel(RectTransform tileRect, string nickname)
        {
            GameObject textObject = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(tileRect, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 0f);
            textRect.pivot = new Vector2(0.5f, 0f);
            textRect.offsetMin = new Vector2(16f, 14f);
            textRect.offsetMax = new Vector2(-16f, 66f);

            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = nickname;
            label.fontSize = 24f;
            label.alignment = TextAlignmentOptions.Bottom;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.color = new Color(0.86f, 0.86f, 0.84f, 1f);
        }

        private DefaultUIButton CreateOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton button = Instantiate(playerButton, parent, false);
            button.name = "friendlySAIN_OverlayActionButton";
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one * 0.9f;
            }

            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetIcon(null);
            return button;
        }

        private Button CreateWindowCloseButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = new GameObject($"{name}_Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI templateLabel = ResolveTemplateLabel();
            if (templateLabel != null)
            {
                label.font = templateLabel.font;
                label.fontSharedMaterial = templateLabel.fontSharedMaterial;
            }

            label.text = GetSocialUiText("RenameClose", "x");
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);
            label.raycastTarget = false;

            return buttonObject.GetComponent<Button>();
        }

        private IEnumerator LoadTeammatePortraitCoroutine(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)
        {
            if (string.IsNullOrWhiteSpace(accountId) || iconImage == null || ItemUiContext.Instance?.Session == null)
            {
                yield break;
            }

            var profileTask = ItemUiContext.Instance.Session.GetOtherPlayerProfile(accountId);
            while (!profileTask.IsCompleted)
            {
                yield return null;
            }

            if (buildVersion != rosterBuildVersion || portraitRoot == null || iconImage._pmcPreview == null)
            {
                yield break;
            }

            if (profileTask.IsFaulted || profileTask.Result.Failed || profileTask.Result.Value == null)
            {
                yield break;
            }

            try
            {
                GClass1416 profile = new GClass1416(profileTask.Result.Value);
                iconImage.SetPresetIcon(profile.Customization, profile.Equipment);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to load teammate portrait for '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void OpenProfile(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId) || ItemUiContext.Instance == null)
            {
                return;
            }

            try
            {
                OtherPlayerProfileScreenPatch.PrepareReturnOverride(ReturnFromProfileToSquadControl);
                Task<OtherPlayerProfileScreen.GClass3883> task = ItemUiContext.Instance.ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile);
                task.ContinueWith(
                    continuation =>
                    {
                        if (continuation.IsFaulted)
                        {
                            OtherPlayerProfileScreenPatch.ClearPendingReturnOverride();
                            friendlySAIN.Log.LogError($"[UI] Failed to open profile for '{accountId}'.");
                            friendlySAIN.Log.LogError(continuation.Exception);
                        }
                    },
                    TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                OtherPlayerProfileScreenPatch.ClearPendingReturnOverride();
                friendlySAIN.Log.LogError($"[UI] Failed to start profile open for '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void PrepareRosterShellContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            if (container.GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup horizontalLayout)
            {
                horizontalLayout.enabled = false;
            }

            if (container.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup verticalLayout)
            {
                verticalLayout.enabled = false;
            }

            if (container.GetComponent<GridLayoutGroup>() is GridLayoutGroup gridLayout)
            {
                gridLayout.enabled = false;
            }

            if (container.GetComponent<ContentSizeFitter>() is ContentSizeFitter sizeFitter)
            {
                sizeFitter.enabled = false;
            }
        }

        private void ConfigureRosterContentContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            rosterGridLayout = container.GetComponent<GridLayoutGroup>() ?? container.gameObject.AddComponent<GridLayoutGroup>();
            rosterGridLayout.enabled = true;
            rosterGridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            rosterGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            rosterGridLayout.cellSize = new Vector2(RosterTileWidth, RosterTileHeight);
            rosterGridLayout.spacing = new Vector2(RosterTileSpacing, RosterTileSpacing);
            rosterGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rosterGridLayout.constraintCount = 5;
            rosterGridLayout.childAlignment = TextAnchor.UpperCenter;
            rosterGridLayout.padding = new RectOffset(0, 0, 0, 0);

            ContentSizeFitter sizeFitter = container.GetComponent<ContentSizeFitter>() ?? container.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            container.anchorMin = new Vector2(0.5f, 1f);
            container.anchorMax = new Vector2(0.5f, 1f);
            container.pivot = new Vector2(0.5f, 1f);
            container.anchoredPosition = Vector2.zero;
            container.sizeDelta = Vector2.zero;
            container.localScale = Vector3.one;
        }

        private TraderScreensGroup ResolveTraderScreensGroupTemplate()
        {
            return Resources.FindObjectsOfTypeAll<TraderScreensGroup>()
                .FirstOrDefault(group =>
                    group != null &&
                    TraderCloseButtonField?.GetValue(group) is DefaultUIButton &&
                    TraderCardsContainerField?.GetValue(group) is Transform);
        }

        private SettingsScreen ResolveSettingsScreenTemplate()
        {
            return Resources.FindObjectsOfTypeAll<SettingsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    SettingsScreenGameTabField?.GetValue(screen) is GameSettingsTab);
        }

        private ScrollRectNoDrag ResolveSettingsScrollRectTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            if (gameSettingsTab == null)
            {
                return null;
            }

            return gameSettingsTab.GetComponentsInChildren<ScrollRectNoDrag>(true)
                .FirstOrDefault(scrollRect =>
                    scrollRect != null &&
                    scrollRect.content != null &&
                    scrollRect.viewport != null);
        }

        private GameSettingsTab ResolveGameSettingsTabTemplate()
        {
            SettingsScreen settingsScreen = ResolveSettingsScreenTemplate();
            return SettingsScreenGameTabField?.GetValue(settingsScreen) as GameSettingsTab;
        }

        private UpdatableToggle ResolveSettingsToggleTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            return GameSettingsToggleTemplateField?.GetValue(gameSettingsTab) as UpdatableToggle;
        }

        private RectTransform ResolveSettingsSliderContainerTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            if (gameSettingsTab == null)
            {
                return null;
            }

            Transform sliderRoot = gameSettingsTab.transform.Find("Image/Scroll View/Viewport/Other Settings/Scrolls/FOV");
            if (sliderRoot is RectTransform sliderRect)
            {
                return sliderRect;
            }

            NumberSlider fallbackSlider = ResolveSettingsSliderTemplate();
            return fallbackSlider?.transform as RectTransform;
        }

        private NumberSlider ResolveSettingsSliderTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            return GameSettingsSliderTemplateField?.GetValue(gameSettingsTab) as NumberSlider;
        }

        private TradingPlayerPanel ResolveTradingPlayerPanelTemplate()
        {
            return Resources.FindObjectsOfTypeAll<TradingPlayerPanel>()
                .FirstOrDefault(panel =>
                    panel != null &&
                    TradingPlayerPanelIconField?.GetValue(panel) is PlayerIconImage playerIcon &&
                    playerIcon._pmcPreview != null &&
                    playerIcon._progress != null);
        }

        private Scrollbar ResolveVerticalScrollbarTemplate()
        {
            Scrollbar template = Resources.FindObjectsOfTypeAll<ScrollRectNoDrag>()
                .Select(scrollRect => scrollRect?.verticalScrollbar)
                .FirstOrDefault(scrollbar => scrollbar != null && scrollbar.transform is RectTransform);

            if (template != null)
            {
                return template;
            }

            return Resources.FindObjectsOfTypeAll<Scrollbar>()
                .FirstOrDefault(scrollbar => scrollbar != null && scrollbar.direction == Scrollbar.Direction.BottomToTop);
        }

        private void CreateScrollableRosterArea(RectTransform shellRect)
        {
            if (shellRect == null)
            {
                return;
            }

            GameObject scrollRootObject = new GameObject("friendlySAIN_SquadControlRosterScroll", typeof(RectTransform), typeof(ScrollRectNoDrag));
            scrollRootObject.transform.SetParent(shellRect, false);
            RectTransform scrollRoot = scrollRootObject.GetComponent<RectTransform>();
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(18f, RosterViewportBottomInset);
            scrollRoot.offsetMax = new Vector2(-18f, -RosterViewportTopInset);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRoot, false);
            rosterViewport = viewportObject.GetComponent<RectTransform>();
            rosterViewport.anchorMin = Vector2.zero;
            rosterViewport.anchorMax = Vector2.one;
            rosterViewport.offsetMin = Vector2.zero;
            rosterViewport.offsetMax = new Vector2(-22f, 0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            rosterScrollbar = CreateRosterScrollbar(scrollRoot);

            GameObject contentObject = new GameObject("friendlySAIN_SquadControlRosterContent", typeof(RectTransform));
            contentObject.transform.SetParent(rosterViewport, false);
            rosterContentRoot = contentObject.GetComponent<RectTransform>();
            rosterContentRoot.anchorMin = new Vector2(0f, 1f);
            rosterContentRoot.anchorMax = new Vector2(1f, 1f);
            rosterContentRoot.pivot = new Vector2(0.5f, 1f);
            rosterContentRoot.anchoredPosition = Vector2.zero;
            rosterContentRoot.sizeDelta = new Vector2(0f, 0f);

            GameObject gridObject = new GameObject("friendlySAIN_SquadControlRosterGrid", typeof(RectTransform));
            gridObject.transform.SetParent(rosterContentRoot, false);
            rosterGridRoot = gridObject.GetComponent<RectTransform>();
            ConfigureRosterContentContainer(rosterGridRoot);

            rosterScrollRect = scrollRootObject.GetComponent<ScrollRectNoDrag>();
            rosterScrollRect.horizontal = false;
            rosterScrollRect.vertical = true;
            rosterScrollRect.movementType = ScrollRect.MovementType.Clamped;
            rosterScrollRect.scrollSensitivity = 30f;
            rosterScrollRect.inertia = true;
            rosterScrollRect.viewport = rosterViewport;
            rosterScrollRect.content = rosterContentRoot;
            rosterScrollRect.verticalScrollbar = rosterScrollbar;
            rosterScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            rosterScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            rosterScrollRect.verticalScrollbarSpacing = 6f;
            rosterScrollRect.AutoZeroing = true;
            rosterScrollRect.Alignment = TextAnchor.UpperCenter;
        }

        private Scrollbar CreateRosterScrollbar(RectTransform parent)
        {
            Scrollbar template = ResolveVerticalScrollbarTemplate();
            Scrollbar scrollbar;

            if (template != null)
            {
                scrollbar = Instantiate(template, parent, false);
                scrollbar.name = "friendlySAIN_SquadControlScrollbar";
            }
            else
            {
                scrollbar = CreateFallbackScrollbar(parent);
            }

            if (scrollbar.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(12f, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            scrollbar.gameObject.SetActive(true);
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            return scrollbar;
        }

        private static Scrollbar CreateFallbackScrollbar(RectTransform parent)
        {
            GameObject root = new GameObject("friendlySAIN_SquadControlScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(parent, false);

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.sizeDelta = new Vector2(12f, 0f);
            rootRect.anchoredPosition = Vector2.zero;

            Image background = root.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.12f);

            GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(root.transform, false);
            RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
            Stretch(slidingRect);
            slidingRect.offsetMin = new Vector2(0f, 4f);
            slidingRect.offsetMax = new Vector2(0f, -4f);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;

            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.88f, 0.88f, 0.88f, 0.9f);

            Scrollbar scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private void UpdateRosterGridLayout(int entryCount)
        {
            if (rosterGridLayout == null)
            {
                return;
            }

            int columnCount = Mathf.Clamp(entryCount, 1, 5);
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(entryCount / 5f));
            float contentHeight = CalculateRowsHeight(rowCount) - RosterViewportPadding;
            float viewportHeight = Mathf.Max(0f, rosterViewport != null && rosterViewport.rect.height > 1f
                ? rosterViewport.rect.height
                : currentRosterShellHeight - 20f);
            float wrapperHeight = Mathf.Max(viewportHeight, contentHeight);
            float gridOffsetY = contentHeight < wrapperHeight ? (wrapperHeight - contentHeight) * 0.5f : 0f;

            rosterGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            rosterGridLayout.constraintCount = columnCount;

            if (rosterContentRoot != null)
            {
                rosterContentRoot.sizeDelta = new Vector2(0f, wrapperHeight);
            }

            if (rosterGridRoot != null)
            {
                rosterGridRoot.sizeDelta = new Vector2(0f, contentHeight);
                rosterGridRoot.anchoredPosition = new Vector2(0f, -gridOffsetY);
            }
        }

        private void UpdateRosterPanelLayout(bool isEmpty)
        {
            float buttonHeight = addTeammateButton?.transform is RectTransform addButtonSource
                ? addButtonSource.sizeDelta.y
                : 54f;

            if (stockCardsContainer != null)
            {
                float shellCenterY = isEmpty ? 54f : (RosterShellToButtonGap + buttonHeight) * 0.5f;
                stockCardsContainer.anchoredPosition = new Vector2(0f, shellCenterY + RosterBlockVerticalOffset);
            }

            if (addTeammateButton?.transform is RectTransform addButtonRect)
            {
                float buttonCenterY = isEmpty
                    ? EmptyRosterButtonCenterY
                    : -(currentRosterShellHeight + RosterShellToButtonGap) * 0.5f;
                addButtonRect.anchoredPosition = new Vector2(0f, buttonCenterY + RosterBlockVerticalOffset);
            }

            if (emptyRosterLabel?.transform is RectTransform emptyLabelRect)
            {
                float emptyLabelY = EmptyRosterButtonCenterY + EmptyRosterLabelSpacing;
                emptyLabelRect.anchoredPosition = new Vector2(0f, emptyLabelY + RosterBlockVerticalOffset);
            }

            if (rosterPanelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rosterPanelRect);
            }
        }

        private static float CalculateRosterShellHeight()
        {
            float screenHeight = Mathf.Max(720f, Screen.height);
            float targetHeight = screenHeight * RosterHeightScreenRatio;
            int visibleRowCount = CalculateVisibleRosterRowCount(targetHeight);
            return CalculateRowsHeight(visibleRowCount) + RosterViewportTopInset + RosterViewportBottomInset;
        }

        private static int CalculateVisibleRosterRowCount(float targetHeight)
        {
            float rowUnit = RosterTileHeight + RosterTileSpacing;
            float usableHeight = Mathf.Max(0f, targetHeight - RosterViewportTopInset - RosterViewportBottomInset - RosterViewportPadding + RosterTileSpacing);
            int visibleRows = Mathf.FloorToInt(usableHeight / rowUnit);
            return Mathf.Max(2, visibleRows);
        }

        private static float CalculateRowsHeight(int rowCount)
        {
            int rows = Mathf.Max(1, rowCount);
            return RosterTileHeight * rows + RosterTileSpacing * (rows - 1) + RosterViewportPadding;
        }

        private static void Stretch(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private TextMeshProUGUI ResolveTemplateLabel()
        {
            return HeaderLabelField?.GetValue(playerButton) as TextMeshProUGUI;
        }

        private void ShiftButtonsBelowPlayer(RectTransform playerRect, float verticalStep)
        {
            foreach (RectTransform sibling in playerButton.transform.parent.Cast<Transform>().Select(t => t as RectTransform).Where(t => t != null))
            {
                if (sibling == playerRect || sibling == squadButton.transform)
                {
                    continue;
                }

                if (Mathf.Abs(sibling.anchoredPosition.x - playerRect.anchoredPosition.x) > 8f)
                {
                    continue;
                }

                if (sibling.anchoredPosition.y >= playerRect.anchoredPosition.y - verticalStep * 0.5f)
                {
                    continue;
                }

                if (sibling.name.StartsWith("friendlySAIN_", StringComparison.Ordinal))
                {
                    continue;
                }

                sibling.anchoredPosition -= new Vector2(0f, verticalStep);
            }
        }

        private void CaptureOriginalButtonPositions(RectTransform playerRect)
        {
            foreach (RectTransform sibling in playerButton.transform.parent.Cast<Transform>().Select(t => t as RectTransform).Where(t => t != null))
            {
                if (sibling == null || sibling == squadButton.transform)
                {
                    continue;
                }

                if (Mathf.Abs(sibling.anchoredPosition.x - playerRect.anchoredPosition.x) > 8f)
                {
                    continue;
                }

                if (sibling.name.StartsWith("friendlySAIN_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!originalButtonPositions.ContainsKey(sibling))
                {
                    originalButtonPositions[sibling] = sibling.anchoredPosition;
                }
            }
        }

        private void RestoreOriginalButtonPositions()
        {
            foreach (KeyValuePair<RectTransform, Vector2> pair in originalButtonPositions)
            {
                if (pair.Key != null)
                {
                    pair.Key.anchoredPosition = pair.Value;
                }
            }
        }

        private float ResolveVerticalStep(RectTransform playerRect, RectTransform tradeRect)
        {
            if (tradeRect != null)
            {
                float delta = playerRect.anchoredPosition.y - tradeRect.anchoredPosition.y;
                if (Mathf.Abs(delta) > 1f)
                {
                    return Mathf.Abs(delta);
                }
            }

            return Mathf.Max(80f, playerRect.rect.height + 10f);
        }

        private Sprite LoadSquadIcon()
        {
            if (squadIconSprite != null)
            {
                return squadIconSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "squad.png"),
                Path.Combine(PluginDirectory, "resources", "squad.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "squad.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                friendlySAIN.Log.LogWarning("[UI] Squad Control icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                friendlySAIN.Log.LogWarning($"[UI] Failed to decode Squad Control icon '{iconPath}'.");
                return null;
            }

            texture.name = "friendlySAIN_SquadControlIcon";
            squadIconSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            squadIconSprite.name = "friendlySAIN_SquadControlIcon";
            return squadIconSprite;
        }

        private static string GetSocialUiText(string key, string fallback)
        {
            if (friendlySAIN.optionsLang?.socialUi != null
                && friendlySAIN.optionsLang.socialUi.TryGetValue(key, out string value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }
    }
}
