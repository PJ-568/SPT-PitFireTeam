using ChatShared;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
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

        private static readonly FieldInfo HeaderLabelField = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static readonly FieldInfo TraderCloseButtonField = AccessTools.Field(typeof(TraderScreensGroup), "_closeButton");
        private static readonly FieldInfo TraderCardsContainerField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardsContainer");
        private static readonly FieldInfo RagfairAllOffersToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_allOffersToggle");
        private static readonly FieldInfo RagfairWishListToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_wishListToggle");
        private static readonly FieldInfo SpawnableToggleHeaderLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_headerLabel");
        private static readonly FieldInfo SpawnableToggleSizeLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_sizeLabel");
        private static readonly FieldInfo TradingPlayerPanelIconField = AccessTools.Field(typeof(TradingPlayerPanel), "_playerIconImage");
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
        private RectTransform stockCardsContainer;
        private RectTransform rosterPanelRect;
        private ScrollRectNoDrag rosterScrollRect;
        private RectTransform rosterViewport;
        private RectTransform rosterContentRoot;
        private RectTransform rosterGridRoot;
        private GridLayoutGroup rosterGridLayout;
        private Scrollbar rosterScrollbar;
        private GameObject rosterPanel;
        private GameObject settingsPanel;
        private DefaultUIButton addTeammateButton;
        private TextMeshProUGUI emptyRosterLabel;
        private GameObject removeConfirmOverlay;
        private float currentRosterShellHeight;
        private readonly Dictionary<RectTransform, Vector2> originalButtonPositions = new Dictionary<RectTransform, Vector2>();
        private int rosterBuildVersion;

        private sealed class SquadRosterEntry
        {
            public string AccountId { get; set; } = string.Empty;
            public string SocialMemberId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public int Level { get; set; }
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
            CreateHeader(rootRect);

            if (!TryCreateStockTraderChrome(rootRect))
            {
                CreateFallbackTabs(rootRect);
                rosterPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlRosterPanel", GetSocialUiText("SquadControlRosterTab", "Roaster"));
                settingsPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab", "Settings"));
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
            SyncButtonVisibility();
        }

        private void CloseScreen()
        {
            if (screenRoot == null)
            {
                return;
            }

            CloseRemoveConfirmOverlay();
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

        private DefaultUIButton CloneMenuButton(string name, string text)
        {
            DefaultUIButton button = Instantiate(playerButton, screenRoot.transform, false);
            button.name = name;
            button.SetRawText(text, playerButton.HeaderSize);
            button.SetIcon(null);
            button.Interactable = true;
            return button;
        }

        private Tab CreateSimpleFallbackTab(string name, RectTransform parent, Vector2 anchoredPosition, string label)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Tab));
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
