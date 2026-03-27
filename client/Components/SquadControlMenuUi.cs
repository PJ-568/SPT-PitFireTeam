using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Settings;
using friendlySAIN.Modules;
using friendlySAIN.Patches;
using HarmonyLib;
using Newtonsoft.Json;
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
        private const float SettingsRowHeight = 104f;
        private const float SettingsHeaderHeight = 36f;
        private const float SettingsSpacing = 12f;
        private const float SettingsControlRightInset = 52f;
        private const float SettingsShortcutRightInset = 128f;
        private const float SettingsSliderVerticalOffset = 36f;

        private static readonly FieldInfo HeaderLabelField = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static readonly FieldInfo TraderCardsContainerField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardsContainer");
        private static readonly FieldInfo TradingPlayerPanelIconField = AccessTools.Field(typeof(TradingPlayerPanel), "_playerIconImage");
        private static readonly FieldInfo SettingsScreenGameTabField = AccessTools.Field(typeof(SettingsScreen), "_gameSettingsScreen");
        private static readonly FieldInfo GameSettingsToggleTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_enableBlockInvites");
        private static readonly FieldInfo GameSettingsSliderTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_fov");
        private static readonly FieldInfo NumberSliderValueInputField = AccessTools.Field(typeof(NumberSlider), "_valueInput");
        private static readonly FieldInfo PlayersInviteWindowContextMenuField = AccessTools.Field(typeof(PlayersInviteWindow), "_contextMenu");
        private static readonly FieldInfo SimpleContextMenuButtonsContainerField = AccessTools.Field(typeof(SimpleContextMenu), "_interactionButtonsContainer");
        private static readonly FieldInfo InteractionButtonsContainerTemplateField = AccessTools.Field(typeof(InteractionButtonsContainer), "_buttonTemplate");
        private static readonly FieldInfo InteractionButtonsContainerButtonsRootField = AccessTools.Field(typeof(InteractionButtonsContainer), "_buttonsContainer");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private const string TeammatesRoute = "/singleplayer/friendlysain/teammates";
        private static Sprite squadIconSprite;
        private static Sprite rosterTileDiagonalSprite;
        private static Sprite autoJoinBadgeSprite;

        private MenuScreen menuScreen;
        private DefaultUIButton playerButton;
        private DefaultUIButton tradeButton;
        private DefaultUIButton squadButton;
        private GameObject screenRoot;
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
        private GameObject portraitContextMenuOverlay;
        private float currentRosterShellHeight;
        private Button activeShortcutCaptureButton;
        private TextMeshProUGUI activeShortcutCaptureLabel;
        private ConfigEntry<KeyboardShortcut> activeShortcutCaptureEntry;
        private readonly Dictionary<RectTransform, Vector2> originalButtonPositions = new Dictionary<RectTransform, Vector2>();
        private readonly Dictionary<string, GameObject> portraitLoadingIndicators = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly HashSet<string> autoJoinRequestsInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder shortcutBuilder = new StringBuilder();
        private int rosterBuildVersion;
        private static bool forceRosterRefreshOnNextInject;

        internal static void RequestRosterRefreshOnNextInject()
        {
            forceRosterRefreshOnNextInject = true;
        }

        private sealed class SquadRosterEntry
        {
            public string AccountId { get; set; } = string.Empty;
            public string SocialMemberId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public int Level { get; set; }
            public bool AutoJoinEnabled { get; set; }
        }

        private sealed class SquadSettingEntry
        {
            public string SectionTitle { get; set; } = string.Empty;
            public ConfigEntryBase Entry { get; set; }
        }

        private sealed class BackendBodyResponse
        {
            public int err;
            public string errmsg;
            public object data;
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

        internal sealed class TabHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
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
            SquadSideSelectionFlow.Open();
        }

        private sealed class PortraitContextClickController : MonoBehaviour, IPointerClickHandler
        {
            public Action<PointerEventData> OnLeftClick { get; set; }
            public Action<PointerEventData> OnRightClick { get; set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    OnLeftClick?.Invoke(eventData);
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    OnRightClick?.Invoke(eventData);
                }
            }
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
            squadButton.OnClick.AddListener(Modules.SquadSideSelectionFlow.Open);
        }

        internal static SquadControlMenuUi FindInstance()
        {
            return Resources.FindObjectsOfTypeAll<SquadControlMenuUi>().FirstOrDefault(ui => ui != null);
        }

        internal void InjectPanelsIntoScreen(Transform newParent)
        {
            EnsureScreen();
            if (screenRoot == null)
            {
                return;
            }

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            if (rosterPanel != null && rosterPanel.transform.parent == rootRect)
            {
                rosterPanel.transform.SetParent(newParent, false);
            }

            if (settingsPanel != null && settingsPanel.transform.parent == rootRect)
            {
                settingsPanel.transform.SetParent(newParent, false);
            }

            ShowTab(true);

            // Rebuild roster only on first open or when explicitly requested by add-teammate flow.
            bool shouldForceRosterRefresh = forceRosterRefreshOnNextInject;
            forceRosterRefreshOnNextInject = false;
            bool needsRoster = rosterGridRoot != null && (rosterGridRoot.childCount == 0 || shouldForceRosterRefresh);
            bool needsSettings = settingsContentRoot != null && settingsContentRoot.childCount == 0;

            if (shouldForceRosterRefresh && needsRoster)
            {
                RebuildRosterTiles();
                needsRoster = false;
            }

            if (needsRoster || needsSettings)
            {
                friendlySAIN.Instance.StartCoroutine(RebuildAfterTransitionCoroutine(needsRoster, needsSettings));
            }
        }

        private IEnumerator RebuildAfterTransitionCoroutine(bool roster, bool settings)
        {
            // Wait for the matchmaker screen open animation to finish before doing any heavy work.
            // Time-based is more reliable than frame-counting across different frame rates.
            yield return new WaitForSeconds(1.2f);

            if (settings) RebuildSettingsEntries();

            if (roster) RebuildRosterTiles();
        }

        internal void RetractPanels()
        {
            if (screenRoot == null)
            {
                return;
            }

            RectTransform rootRect = screenRoot.GetComponent<RectTransform>();
            if (rosterPanel != null && rosterPanel.transform.parent != rootRect)
            {
                rosterPanel.transform.SetParent(rootRect, false);
            }

            if (settingsPanel != null && settingsPanel.transform.parent != rootRect)
            {
                settingsPanel.transform.SetParent(rootRect, false);
            }
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
            CreateHeader(rootRect);

            if (!TryCreateStockTraderChrome(rootRect))
            {
                rosterPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlRosterPanel", GetSocialUiText("SquadControlRosterTab", "Roaster"));
                settingsPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab", "Settings"));
                BuildSettingsPanel();
            }

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

            Transform cardsContainerTemplate = TraderCardsContainerField?.GetValue(templateGroup) as Transform;

            if (cardsContainerTemplate == null)
            {
                return false;
            }

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

        internal void ShowTab(bool showRoster)
        {
            if (rosterPanel != null)
            {
                rosterPanel.SetActive(showRoster);
            }

            if (settingsPanel != null)
            {
                settingsPanel.SetActive(!showRoster);
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
                friendlySAIN.badGuy))
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
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(0f, -6f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.color = new Color(0.92f, 0.82f, 0.63f, 1f);
            label.fontWeight = FontWeight.Bold;
            label.fontSize = 20f;

            GameObject dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(headerObject.transform, false);
            RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.offsetMin = new Vector2(6f, 0f);
            dividerRect.offsetMax = new Vector2(-6f, 1f);

            Image divider = dividerObject.GetComponent<Image>();
            divider.color = new Color(0.54f, 0.45f, 0.29f, 0.45f);
            divider.raycastTarget = false;
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
            background.color = new Color(0.07f, 0.07f, 0.07f, 0.84f);
            background.raycastTarget = true;

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, SettingsRowHeight);

            CreateSettingsRowChrome(rowObject.transform);

            GameObject nameObject = CreateText("Name", GetSettingDisplayName(entry), 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(rowObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(22f, -34f);
            nameRect.offsetMax = new Vector2(-418f, -8f);

            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;

            GameObject descriptionObject = CreateText("Description", entry.Description?.Description ?? string.Empty, 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(rowObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(22f, 16f);
            descriptionRect.offsetMax = new Vector2(-418f, -38f);

            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = new Color(0.72f, 0.72f, 0.72f, 1f);
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

        private static void CreateSettingsRowChrome(Transform rowTransform)
        {
            GameObject topLineObject = new GameObject("TopLine", typeof(RectTransform), typeof(Image));
            topLineObject.transform.SetParent(rowTransform, false);

            RectTransform topLineRect = topLineObject.GetComponent<RectTransform>();
            topLineRect.anchorMin = new Vector2(0f, 1f);
            topLineRect.anchorMax = new Vector2(1f, 1f);
            topLineRect.pivot = new Vector2(0.5f, 1f);
            topLineRect.offsetMin = new Vector2(12f, -1f);
            topLineRect.offsetMax = new Vector2(-12f, 0f);

            Image topLine = topLineObject.GetComponent<Image>();
            topLine.color = new Color(0.68f, 0.58f, 0.38f, 0.14f);
            topLine.raycastTarget = false;

            GameObject bottomLineObject = new GameObject("BottomLine", typeof(RectTransform), typeof(Image));
            bottomLineObject.transform.SetParent(rowTransform, false);

            RectTransform bottomLineRect = bottomLineObject.GetComponent<RectTransform>();
            bottomLineRect.anchorMin = new Vector2(0f, 0f);
            bottomLineRect.anchorMax = new Vector2(1f, 0f);
            bottomLineRect.pivot = new Vector2(0.5f, 0f);
            bottomLineRect.offsetMin = new Vector2(12f, 0f);
            bottomLineRect.offsetMax = new Vector2(-12f, 1f);

            Image bottomLine = bottomLineObject.GetComponent<Image>();
            bottomLine.color = new Color(0f, 0f, 0f, 0.42f);
            bottomLine.raycastTarget = false;
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
            buttonRect.sizeDelta = new Vector2(232f, 36f);
            buttonRect.anchoredPosition = Vector2.zero;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.52f, 0.43f, 0.27f, 0.88f);
            background.raycastTarget = true;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(buttonObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);

            Image fill = fillObject.GetComponent<Image>();
            fill.color = new Color(0.12f, 0.12f, 0.12f, 0.96f);
            fill.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = fill;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.92f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            button.colors = colors;

            GameObject labelObject = CreateText("Label", string.Empty, 18f, TextAlignmentOptions.Center);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 17f;
            label.fontWeight = FontWeight.SemiBold;
            label.color = new Color(0.93f, 0.88f, 0.77f, 1f);
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

        private void RebuildRosterTiles()
        {
            if (rosterGridRoot == null)
            {
                return;
            }

            CloseRemoveConfirmOverlay();
            ClosePortraitContextMenu();
            CancelPortraitQueue();
            portraitLoadingIndicators.Clear();
            autoJoinRequestsInFlight.Clear();
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
                                Level = level,
                                AutoJoinEnabled = ParseBool(teammate?["AutoJoinEnabled"]?.ToString() ?? teammate?["autoJoinEnabled"]?.ToString())
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

        private static bool ParseBool(string value)
        {
            return bool.TryParse(value, out bool parsed) && parsed;
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
            AddTeammateCreationFlow.Start(SquadSideSelectionFlow.Open);
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
            Color normalColor = new Color(0.045f, 0.045f, 0.045f, 0.97f);
            Color hoverColor = new Color(0.10f, 0.10f, 0.10f, 0.98f);
            Color pressedColor = new Color(0.16f, 0.16f, 0.16f, 0.99f);
            tileBackground.color = normalColor;

            GameObject tileOverlayObject = new GameObject("BackgroundOverlay", typeof(RectTransform), typeof(Image));
            tileOverlayObject.transform.SetParent(tileRect, false);
            RectTransform tileOverlayRect = tileOverlayObject.GetComponent<RectTransform>();
            tileOverlayRect.anchorMin = new Vector2(0f, 1f);
            tileOverlayRect.anchorMax = new Vector2(0f, 1f);
            tileOverlayRect.pivot = new Vector2(0f, 1f);
            tileOverlayRect.sizeDelta = new Vector2(58f, 58f);
            tileOverlayRect.anchoredPosition = Vector2.zero;

            Image tileOverlay = tileOverlayObject.GetComponent<Image>();
            Sprite diagonalSprite = LoadRosterTileDiagonalSprite();
            if (diagonalSprite != null)
            {
                tileOverlay.sprite = diagonalSprite;
                tileOverlay.type = Image.Type.Simple;
                tileOverlay.color = Color.white;
            }
            else
            {
                tileOverlay.color = new Color(1f, 1f, 1f, 0.06f);
            }

            tileOverlay.raycastTarget = false;

            Button button = tileObject.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = tileBackground;
            button.onClick.AddListener(() => OpenProfile(entry.AccountId));

            RosterTileHoverController hoverController = tileObject.AddComponent<RosterTileHoverController>();
            hoverController.Configure(tileBackground, normalColor, hoverColor, pressedColor);

            PortraitContextClickController tileContextController = tileObject.AddComponent<PortraitContextClickController>();
            tileContextController.OnRightClick = eventData => ShowPortraitContextMenu(entry, eventData);

            RectTransform portraitHost = CreatePortraitHost(tileRect, entry.Level, out PlayerIconImage iconImage);
            RegisterPortraitLoadingIndicator(entry.AccountId, iconImage?._progress);
            AttachPortraitProfileTrigger(portraitHost, entry);
            CreateRemoveButton(tileRect, entry);
            CreateAutoJoinBadge(tileRect, entry);
            CreateRosterNameLabel(tileRect, entry.Nickname);
            EnqueuePortrait(entry.AccountId, iconImage, portraitHost, buildVersion);
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

        private void CreateAutoJoinBadge(RectTransform tileRect, SquadRosterEntry entry)
        {
            if (tileRect == null || entry == null)
            {
                return;
            }

            Sprite badgeSprite = LoadAutoJoinBadgeSprite();
            if (badgeSprite == null)
            {
                return;
            }

            GameObject badgeObject = new GameObject("friendlySAIN_AutoJoinBadge", typeof(RectTransform), typeof(Image));
            badgeObject.transform.SetParent(tileRect, false);

            RectTransform rect = badgeObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(22f, 22f);
            rect.localPosition = new Vector3(95f, -107f, 0f);

            Image badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = badgeSprite;
            badgeImage.type = Image.Type.Simple;
            badgeImage.preserveAspect = true;
            badgeImage.color = new Color(0.92f, 0.92f, 0.9f, 0.95f);
            badgeImage.raycastTarget = false;
            badgeObject.SetActive(entry.AutoJoinEnabled);
        }

        private void UpdateAutoJoinBadge(string accountId, bool enabled)
        {
            if (rosterGridRoot == null || string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }

            Transform tile = rosterGridRoot.Find($"friendlySAIN_RosterTile_{accountId}");
            if (tile == null)
            {
                return;
            }

            Transform badge = tile.Find("friendlySAIN_AutoJoinBadge");
            if (badge != null)
            {
                badge.gameObject.SetActive(enabled);
            }
        }

        private void ShowRemoveConfirmOverlay(SquadRosterEntry entry)
        {
            CloseRemoveConfirmOverlay();

            Transform? overlayParent = rosterPanel?.transform.parent;
            if (overlayParent == null || entry == null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("friendlySAIN_RemoveOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
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
                background.gameObject.SetActive(true);
            }

            SetNamedPortraitChildActive(clonedRoot, "Shadow_L", false);
            SetNamedPortraitChildActive(clonedRoot, "Shadow_R", false);

            Transform levelRoot = clonedRoot.Find("Level");
            if (levelRoot != null)
            {
                //
                TextMeshProUGUI? label = levelRoot.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = level.ToString("D2");
                }
                else
                {
                    levelRoot.gameObject.SetActive(false);

                    CreatePortraitLevelBadge(clonedRoot, level);
                }
            }

            RemoveStockPortraitBorderChrome(clonedRoot);
            CreatePortraitBackground(clonedRoot);
        }

        private static void SetNamedPortraitChildActive(Transform root, string childName, bool isActive)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                Transform child = descendants[i];
                if (child != null && string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(isActive);
                }
            }
        }

        private static void RemoveStockPortraitBorderChrome(Transform clonedRoot)
        {
            if (clonedRoot == null)
            {
                return;
            }

            Image[] images = clonedRoot.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null)
                {
                    continue;
                }

                string name = image.name;
                if (name.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("border", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.gameObject.SetActive(false);
                }
            }
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

        private void AttachPortraitProfileTrigger(RectTransform portraitHost, SquadRosterEntry entry)
        {
            if (portraitHost == null || entry == null)
            {
                return;
            }

            GameObject triggerObject = new GameObject("friendlySAIN_PortraitProfileTrigger", typeof(RectTransform), typeof(Image));
            triggerObject.transform.SetParent(portraitHost, false);

            RectTransform triggerRect = triggerObject.GetComponent<RectTransform>();
            Stretch(triggerRect);
            triggerRect.SetAsLastSibling();

            Image triggerImage = triggerObject.GetComponent<Image>();
            triggerImage.color = new Color(0f, 0f, 0f, 0.001f);
            triggerImage.raycastTarget = true;

            PortraitContextClickController controller = triggerObject.AddComponent<PortraitContextClickController>();
            controller.OnLeftClick = _ => OpenProfile(entry.AccountId);
            controller.OnRightClick = eventData => ShowPortraitContextMenu(entry, eventData);
        }

        private void ShowPortraitContextMenu(SquadRosterEntry entry, PointerEventData eventData)
        {
            ClosePortraitContextMenu();

            if (entry == null)
            {
                return;
            }

            Transform overlayParent = rosterPanel?.transform.parent;
            if (overlayParent == null)
            {
                return;
            }

            GameObject overlayRoot = new GameObject("friendlySAIN_PortraitContextMenuOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.001f);
            overlayImage.raycastTarget = true;

            Button overlayButton = overlayRoot.GetComponent<Button>();
            overlayButton.transition = Selectable.Transition.None;
            overlayButton.onClick.AddListener(ClosePortraitContextMenu);

            if (TryCreateStockPortraitContextMenu(overlayRoot.transform, overlayRect, entry, eventData))
            {
                portraitContextMenuOverlay = overlayRoot;
                return;
            }

            GameObject menuObject = new GameObject("friendlySAIN_PortraitContextMenu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            menuObject.transform.SetParent(overlayRoot.transform, false);
            RectTransform menuRect = menuObject.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0.5f, 0.5f);
            menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.sizeDelta = new Vector2(228f, 0f);

            Vector2 localPoint = eventData.position - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            menuRect.anchoredPosition = ClampContextMenuPosition(overlayRect.rect, localPoint, 228f, 126f);

            Image menuBackground = menuObject.GetComponent<Image>();
            menuBackground.color = new Color(0.045f, 0.045f, 0.045f, 0.98f);
            menuBackground.raycastTarget = true;

            VerticalLayoutGroup layout = menuObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = menuObject.GetComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateContextMenuButton(
                menuObject.transform,
                GetSocialUiText("SquadControlInviteToGroup", "Invite to group"),
                () => InviteTeammateToGroup(entry));
            CreateContextMenuButton(
                menuObject.transform,
                GetSocialUiText("SquadControlViewProfile", "View profile"),
                () =>
                {
                    ClosePortraitContextMenu();
                    OpenProfile(entry.AccountId);
                });
            CreateContextMenuButton(
                menuObject.transform,
                GetSocialUiText(
                    entry.AutoJoinEnabled ? "SquadControlAutoJoinOn" : "SquadControlAutoJoinOff",
                    entry.AutoJoinEnabled ? "Auto join: On" : "Auto join: Off"),
                () => ToggleTeammateAutoJoin(entry));

            portraitContextMenuOverlay = overlayRoot;
        }

        private bool TryCreateStockPortraitContextMenu(Transform overlayParent, RectTransform overlayRect, SquadRosterEntry entry, PointerEventData eventData)
        {
            if (overlayParent == null || overlayRect == null || entry == null)
            {
                return false;
            }

            SimpleContextMenu template = ResolveMatchmakerContextMenuTemplate();
            if (template == null)
            {
                return false;
            }

            SimpleContextMenu menu = Instantiate(template, overlayParent, false);
            menu.name = "friendlySAIN_PortraitContextMenu";
            menu.gameObject.SetActive(true);
            menu.enabled = false;

            RectTransform menuRect = menu.transform as RectTransform;
            if (menuRect == null)
            {
                Destroy(menu.gameObject);
                return false;
            }

            menuRect.anchorMin = new Vector2(0.5f, 0.5f);
            menuRect.anchorMax = new Vector2(0.5f, 0.5f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.SetAsLastSibling();

            InteractionButtonsContainer container = SimpleContextMenuButtonsContainerField?.GetValue(menu) as InteractionButtonsContainer;
            SimpleContextMenuButton buttonTemplate = InteractionButtonsContainerTemplateField?.GetValue(container) as SimpleContextMenuButton;
            RectTransform buttonsRoot = InteractionButtonsContainerButtonsRootField?.GetValue(container) as RectTransform;
            if (container == null || buttonTemplate == null || buttonsRoot == null)
            {
                Destroy(menu.gameObject);
                return false;
            }

            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "InviteInGroup",
                GetSocialUiText("SquadControlInviteToGroup", "Invite to group"),
                () =>
                {
                    ClosePortraitContextMenu();
                    InviteTeammateToGroup(entry);
                });
            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "WatchProfile",
                GetSocialUiText("SquadControlViewProfile", "View profile"),
                () =>
                {
                    ClosePortraitContextMenu();
                    OpenProfile(entry.AccountId);
                });
            CreateStockContextMenuButton(
                container,
                buttonTemplate,
                buttonsRoot,
                "AutoJoin",
                GetSocialUiText(
                    entry.AutoJoinEnabled ? "SquadControlAutoJoinOn" : "SquadControlAutoJoinOff",
                    entry.AutoJoinEnabled ? "Auto join: On" : "Auto join: Off"),
                () =>
                {
                    ClosePortraitContextMenu();
                    ToggleTeammateAutoJoin(entry);
                });

            LayoutRebuilder.ForceRebuildLayoutImmediate(menuRect);

            Vector2 localPoint = eventData.position - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 menuSize = menuRect.rect.size;
            menuRect.anchoredPosition = ClampContextMenuPosition(
                overlayRect.rect,
                localPoint,
                Mathf.Max(menuSize.x, 228f),
                Mathf.Max(menuSize.y, 36f * 3f));

            return true;
        }

        private Vector2 ClampContextMenuPosition(Rect overlayRect, Vector2 desiredPosition, float menuWidth, float menuHeight)
        {
            float minX = -overlayRect.width * 0.5f;
            float maxX = overlayRect.width * 0.5f - menuWidth;
            float minY = -overlayRect.height * 0.5f + menuHeight;
            float maxY = overlayRect.height * 0.5f;

            return new Vector2(
                Mathf.Clamp(desiredPosition.x, minX, maxX),
                Mathf.Clamp(desiredPosition.y, minY, maxY));
        }

        private void CreateStockContextMenuButton(
            InteractionButtonsContainer container,
            SimpleContextMenuButton template,
            RectTransform buttonsRoot,
            string key,
            string label,
            Action onClick)
        {
            if (container == null || template == null || buttonsRoot == null)
            {
                return;
            }

            container.method_1(
                key,
                label,
                template,
                buttonsRoot,
                null,
                onClick,
                null,
                false,
                false);
        }

        private void CreateContextMenuButton(Transform parent, string label, Action onClick)
        {
            GameObject buttonObject = new GameObject("friendlySAIN_ContextMenuButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 36f;
            layout.flexibleWidth = 1f;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.11f, 0.11f, 0.11f, 1f);
            background.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            GameObject labelObject = CreateText("Label", label, 18f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            Stretch(labelRect);
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            text.color = new Color(0.92f, 0.92f, 0.90f, 1f);
            text.raycastTarget = false;
        }

        private void InviteTeammateToGroup(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
            {
                friendlySAIN.Log.LogWarning($"[UI] Could not invite teammate '{entry.AccountId}': matchmaker controller unavailable.");
                return;
            }

            TeammateAutoJoinRuntime.ClearSuppression(entry.AccountId);

            if (controller.GroupPlayers != null && controller.GroupPlayers.Any(player => player?.AccountId == entry.AccountId))
            {
                return;
            }

            controller.SendInvite(entry.AccountId, true, null);
        }

        private void ToggleTeammateAutoJoin(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            bool nextState = !entry.AutoJoinEnabled;
            if (!autoJoinRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            SetPortraitLoading(entry.AccountId, true);
            friendlySAIN.Instance.StartCoroutine(ToggleTeammateAutoJoinCoroutine(entry, nextState));
        }

        private IEnumerator ToggleTeammateAutoJoinCoroutine(SquadRosterEntry entry, bool nextState)
        {
            string accountId = entry?.AccountId ?? string.Empty;
            string nickname = entry?.Nickname ?? string.Empty;
            Task<string> requestTask = null;
            string requestBody = JsonConvert.SerializeObject(new
            {
                aid = accountId,
                enabled = nextState
            });

            try
            {
                requestTask = Task.Run(() => RequestHandler.PostJson("/singleplayer/friendlysain/teammate/autojoin", requestBody));
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to start auto join toggle request for teammate '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
            }

            if (requestTask != null)
            {
                yield return new WaitUntil(() => requestTask.IsCompleted);
            }

            bool success = false;
            string backendError = null;

            if (requestTask == null)
            {
                backendError = "Request did not start.";
            }
            else if (requestTask.IsFaulted)
            {
                backendError = requestTask.Exception?.GetBaseException().Message;
                friendlySAIN.Log.LogError($"[UI] Failed to toggle auto join for teammate '{accountId}'.");
                friendlySAIN.Log.LogError(requestTask.Exception);
            }
            else
            {
                success = TryValidateBackendSuccess(requestTask.Result, out backendError);
                if (!success && !string.IsNullOrWhiteSpace(backendError))
                {
                    friendlySAIN.Log.LogWarning($"[UI] Auto join toggle rejected for teammate '{accountId}': {backendError}");
                }
            }

            SetPortraitLoading(accountId, false);
            autoJoinRequestsInFlight.Remove(accountId);

            if (success)
            {
                if (nextState)
                {
                    TeammateAutoJoinRuntime.ClearSuppression(accountId);
                }

                entry.AutoJoinEnabled = nextState;
                UpdateAutoJoinBadge(accountId, nextState);
                string successTemplate = GetSocialUiText(
                    nextState ? "SquadControlAutoJoinEnabledToast" : "SquadControlAutoJoinDisabledToast",
                    nextState ? "Enabled PMC raid auto-join for {0}." : "Disabled PMC raid auto-join for {0}.");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            string failureTemplate = GetSocialUiText(
                nextState ? "SquadControlAutoJoinEnableFailedToast" : "SquadControlAutoJoinDisableFailedToast",
                nextState ? "Failed to enable auto-join for {0}" : "Failed to disable auto-join for {0}");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private static bool TryValidateBackendSuccess(string responseJson, out string backendError)
        {
            backendError = null;

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return true;
            }

            try
            {
                BackendBodyResponse response = JsonConvert.DeserializeObject<BackendBodyResponse>(responseJson);
                if (response == null)
                {
                    backendError = "Backend returned an empty response body.";
                    return false;
                }

                if (response.err != 0)
                {
                    backendError = string.IsNullOrWhiteSpace(response.errmsg)
                        ? $"Backend returned err={response.err}."
                        : response.errmsg;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                backendError = $"Failed to parse backend response: {ex.Message}";
                return false;
            }
        }

        private void RegisterPortraitLoadingIndicator(string accountId, GameObject progressObject)
        {
            if (string.IsNullOrWhiteSpace(accountId) || progressObject == null)
            {
                return;
            }

            portraitLoadingIndicators[accountId] = progressObject;
            progressObject.SetActive(false);
        }

        private void SetPortraitLoading(string accountId, bool loading)
        {
            if (TryGetPortraitLoadingIndicator(accountId, out GameObject progressObject))
            {
                progressObject.SetActive(loading);
            }
        }

        private bool TryGetPortraitLoadingIndicator(string accountId, out GameObject progressObject)
        {
            progressObject = null;

            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            if (portraitLoadingIndicators.TryGetValue(accountId, out progressObject) && progressObject != null)
            {
                return true;
            }

            Transform tile = rosterGridRoot?.Find($"friendlySAIN_RosterTile_{accountId}");
            Transform progressTransform = FindChildRecursive(tile, "Progress");
            if (progressTransform == null)
            {
                return false;
            }

            progressObject = progressTransform.gameObject;
            portraitLoadingIndicators[accountId] = progressObject;
            return true;
        }

        private static Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform descendant = FindChildRecursive(child, childName);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private bool TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller)
        {
            controller = null;

            if (friendlySAIN.application == null)
            {
                try
                {
                    friendlySAIN.application = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                }
                catch (Exception ex)
                {
                    friendlySAIN.Log.LogError("[UI] Failed to resolve main application for teammate invite.");
                    friendlySAIN.Log.LogError(ex);
                }
            }

            controller = friendlySAIN.application?.MatchmakerPlayerControllerClass;
            return controller != null;
        }

        private void ClosePortraitContextMenu()
        {
            if (portraitContextMenuOverlay == null)
            {
                return;
            }

            Destroy(portraitContextMenuOverlay);
            portraitContextMenuOverlay = null;
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

        private SimpleContextMenu ResolveMatchmakerContextMenuTemplate()
        {
            return Resources.FindObjectsOfTypeAll<PlayersInviteWindow>()
                .Select(window => PlayersInviteWindowContextMenuField?.GetValue(window) as SimpleContextMenu)
                .FirstOrDefault(menu => menu != null && SimpleContextMenuButtonsContainerField?.GetValue(menu) is InteractionButtonsContainer);
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

        // Sequential portrait load queue — one SetPresetIcon per entry, processed in order.
        private static readonly Queue<(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)> _portraitQueue
            = new Queue<(string, PlayerIconImage, RectTransform, int)>();
        private static Coroutine _portraitQueueCoroutine;

        private void EnqueuePortrait(string accountId, PlayerIconImage iconImage, RectTransform portraitRoot, int buildVersion)
        {
            _portraitQueue.Enqueue((accountId, iconImage, portraitRoot, buildVersion));

            if (_portraitQueueCoroutine == null)
            {
                _portraitQueueCoroutine = friendlySAIN.Instance.StartCoroutine(DrainPortraitQueueCoroutine());
            }
        }

        private void CancelPortraitQueue()
        {
            _portraitQueue.Clear();

            if (_portraitQueueCoroutine != null)
            {
                friendlySAIN.Instance.StopCoroutine(_portraitQueueCoroutine);
                _portraitQueueCoroutine = null;
            }
        }

        private IEnumerator DrainPortraitQueueCoroutine()
        {
            while (_portraitQueue.Count > 0)
            {
                var (accountId, iconImage, portraitRoot, buildVersion) = _portraitQueue.Dequeue();
                yield return LoadTeammatePortraitCoroutine(accountId, iconImage, portraitRoot, buildVersion);
                yield return new WaitForSeconds(0.3f);
            }

            _portraitQueueCoroutine = null;
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

        private Sprite LoadRosterTileDiagonalSprite()
        {
            if (rosterTileDiagonalSprite != null)
            {
                return rosterTileDiagonalSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "diagonal_lines.png"),
                Path.Combine(PluginDirectory, "resources", "diagonal_lines.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "diagonal_lines.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                friendlySAIN.Log.LogWarning("[UI] Roster tile diagonal overlay image could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                friendlySAIN.Log.LogWarning($"[UI] Failed to decode roster tile diagonal overlay '{iconPath}'.");
                return null;
            }

            texture.name = "friendlySAIN_RosterTileDiagonal";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            rosterTileDiagonalSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                200f,
                0u,
                SpriteMeshType.FullRect);
            rosterTileDiagonalSprite.name = "friendlySAIN_RosterTileDiagonal";
            return rosterTileDiagonalSprite;
        }

        private Sprite LoadAutoJoinBadgeSprite()
        {
            if (autoJoinBadgeSprite != null)
            {
                return autoJoinBadgeSprite;
            }

            string[] candidates =
            {
                Path.Combine(PluginDirectory, "auto-join.png"),
                Path.Combine(PluginDirectory, "resources", "auto-join.png"),
                Path.Combine(Directory.GetParent(PluginDirectory)?.FullName ?? PluginDirectory, "resources", "auto-join.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "friendlySAIN", "auto-join.png"),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "friendlySAIN", "resources", "auto-join.png")
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                friendlySAIN.Log.LogWarning("[UI] Auto-join badge icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                friendlySAIN.Log.LogWarning($"[UI] Failed to decode auto-join badge icon '{iconPath}'.");
                return null;
            }

            texture.name = "friendlySAIN_AutoJoinBadge";
            autoJoinBadgeSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            autoJoinBadgeSprite.name = "friendlySAIN_AutoJoinBadge";
            return autoJoinBadgeSprite;
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
