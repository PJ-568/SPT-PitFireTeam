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
    internal partial class SquadControlMenuUi : MonoBehaviour
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
        private const float RaidOverlayBackButtonYOffset = 50f;

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
        private static readonly FieldInfo MatchmakerBackButtonField = AccessTools.Field(typeof(MatchMakerSideSelectionScreen), "_backButton");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private const string TeammatesRoute = "/singleplayer/friendlysain/teammates";
        private static Sprite squadIconSprite;
        private static Sprite rosterTileDiagonalSprite;
        private static Sprite autoJoinBadgeSprite;
        private static Sprite groupBadgeSprite;
        private const float RaidSettingsButtonGap = 10f;

        private MenuScreen menuScreen;
        private DefaultUIButton playerButton;
        private DefaultUIButton tradeButton;
        private DefaultUIButton hideScreenButton;
        private DefaultUIButton squadButton;
        private DefaultUIButton raidSettingsButton;
        private GameObject screenRoot;
        private TextMeshProUGUI titleLabel;
        private DefaultUIButton standaloneCloseButton;
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
        private readonly HashSet<string> groupRequestsInFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder shortcutBuilder = new StringBuilder();
        private int rosterBuildVersion;
        private static bool forceRosterRefreshOnNextInject;
        private static readonly HashSet<string> pendingTileRefreshAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private bool raidSettingsOverlayActive;
        private MatchmakerPlayerControllerClass subscribedGroupBadgeLogController;
        private Action unsubscribeGroupBadgeLog;

        internal static void RequestRosterRefreshOnNextInject()
        {
            forceRosterRefreshOnNextInject = true;
            pendingTileRefreshAccountIds.Clear();
        }

        internal static void RequestRosterTileRefreshOnNextInject(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return;
            }

            if (forceRosterRefreshOnNextInject)
            {
                return;
            }

            pendingTileRefreshAccountIds.Add(accountId);
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
            private TextMeshProUGUI nameLabel;
            private Color normalColor;
            private Color hoverColor;
            private Color pressedColor;
            private Color normalTextColor;
            private Color hoverTextColor;
            private bool isHovered;

            public void Configure(Image target, TextMeshProUGUI label, Color normal, Color hover, Color pressed)
            {
                background = target;
                nameLabel = label;
                normalColor = normal;
                hoverColor = hover;
                pressedColor = pressed;
                normalTextColor = nameLabel != null ? nameLabel.color : default;
                hoverTextColor = new Color(1f - normalTextColor.r, 1f - normalTextColor.g, 1f - normalTextColor.b, normalTextColor.a);
                Apply(normalColor);
                ApplyText(normalTextColor);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (isHovered)
                {
                    return;
                }

                isHovered = true;
                GUISounds guiSounds = Singleton<GUISounds>.Instance;
                if (guiSounds != null)
                {
                    guiSounds.PlayUISound(EUISoundType.ButtonOver);
                }

                Apply(hoverColor);
                ApplyText(hoverTextColor);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                isHovered = false;
                Apply(normalColor);
                ApplyText(normalTextColor);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                Apply(isHovered ? hoverColor : normalColor);
                ApplyText(isHovered ? hoverTextColor : normalTextColor);
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                Apply(isHovered ? hoverColor : normalColor);
                ApplyText(isHovered ? hoverTextColor : normalTextColor);
            }

            private void Apply(Color color)
            {
                if (background != null)
                {
                    background.color = color;
                }
            }

            private void ApplyText(Color color)
            {
                if (nameLabel != null)
                {
                    nameLabel.color = color;
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

        private sealed class TooltipHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private string tooltipText;
            private Vector2 tooltipOffset = new Vector2(5f, 7f);

            public void Configure(string text, Vector2? offset = null)
            {
                tooltipText = text ?? string.Empty;
                tooltipOffset = offset ?? new Vector2(5f, 7f);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (string.IsNullOrWhiteSpace(tooltipText))
                {
                    return;
                }

                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Show(tooltipText, tooltipOffset, 0f, null);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Close();
            }

            private void OnDisable()
            {
                ItemUiContext instance = ItemUiContext.Instance;
                instance?.Tooltip?.Close();
            }
        }

        public void Initialize(MenuScreen screen, DefaultUIButton sourcePlayerButton, DefaultUIButton sourceTradeButton, DefaultUIButton sourceHideScreenButton)
        {
            DetachGroupBadgeEventLogging();

            menuScreen = screen;
            playerButton = sourcePlayerButton;
            tradeButton = sourceTradeButton;
            hideScreenButton = sourceHideScreenButton;

            if (playerButton == null)
            {
                return;
            }

            EnsureSquadButton();
            EnsureRaidSettingsButton();
            EnsureScreen();
            raidSettingsOverlayActive = false;
            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.gameObject.SetActive(false);
            }

            EnsureGroupBadgeEventLogging();
            SyncButtonVisibility();
        }

        private void OnDisable()
        {
            DetachGroupBadgeEventLogging();
        }

        private void OnDestroy()
        {
            DetachGroupBadgeEventLogging();
        }

        public void SyncButtonVisibility()
        {
            if (playerButton != null && squadButton != null)
            {
                bool visible = playerButton.gameObject.activeSelf;
                squadButton.gameObject.SetActive(visible);
            }

            if (raidSettingsButton != null)
            {
                bool showRaidSettingsButton = hideScreenButton != null
                    && hideScreenButton.gameObject.activeSelf
                    && !raidSettingsOverlayActive;
                raidSettingsButton.gameObject.SetActive(showRaidSettingsButton);
            }
        }

        private void Update()
        {
            if (raidSettingsOverlayActive && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                HideRaidSettingsOverlay();
                return;
            }

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
                squadRect.anchoredPosition = playerRect.anchoredPosition + new Vector2(0f, -verticalStep - 10f);

                ShiftButtonsBelowPlayer(playerRect, verticalStep);

                if (tradeRect != null)
                {
                    tradeRect.anchoredPosition -= new Vector2(0f, 4f);
                }
            }

            squadButton.SetRawText(GetSocialUiText("SquadControlButton", "Squad Control"), playerButton.HeaderSize);
            squadButton.SetIcon(LoadSquadIcon());
            squadButton.OnClick.RemoveAllListeners();
            squadButton.OnClick.AddListener(Modules.SquadSideSelectionFlow.Open);
        }

        private void EnsureRaidSettingsButton()
        {
            if (hideScreenButton == null)
            {
                if (raidSettingsButton != null)
                {
                    raidSettingsButton.gameObject.SetActive(false);
                }

                return;
            }

            if (raidSettingsButton == null)
            {
                Transform existing = hideScreenButton.transform.parent.Find("friendlySAIN_RaidSettingsButton");
                if (existing != null)
                {
                    raidSettingsButton = existing.GetComponent<DefaultUIButton>();
                }
            }

            if (raidSettingsButton == null)
            {
                raidSettingsButton = Instantiate(hideScreenButton, hideScreenButton.transform.parent, false);
                raidSettingsButton.name = "friendlySAIN_RaidSettingsButton";
            }

            if (hideScreenButton.transform is RectTransform resumeRect
                && raidSettingsButton.transform is RectTransform raidRect)
            {
                raidRect.anchorMin = resumeRect.anchorMin;
                raidRect.anchorMax = resumeRect.anchorMax;
                raidRect.pivot = resumeRect.pivot;
                raidRect.sizeDelta = resumeRect.sizeDelta;
                raidRect.anchoredPosition = resumeRect.anchoredPosition - new Vector2(0f, resumeRect.rect.height + RaidSettingsButtonGap - 5f);
                raidRect.localScale = resumeRect.localScale;
            }

            raidSettingsButton.transform.SetSiblingIndex(hideScreenButton.transform.GetSiblingIndex() + 1);
            raidSettingsButton.SetRawText(GetSocialUiText("SquadControlRaidSettingsButton", "Squad Settings"), hideScreenButton.HeaderSize);
            raidSettingsButton.SetIcon(null);
            raidSettingsButton.OnClick.RemoveAllListeners();
            raidSettingsButton.OnClick.AddListener(ShowRaidSettingsOverlay);
        }

        internal static SquadControlMenuUi FindInstance()
        {
            return Resources.FindObjectsOfTypeAll<SquadControlMenuUi>().FirstOrDefault(ui => ui != null);
        }

        internal void InjectPanelsIntoScreen(Transform newParent)
        {
            EnsureScreen();
            EnsureGroupBadgeEventLogging();
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
            List<string> pendingTileRefreshIds = pendingTileRefreshAccountIds.ToList();
            pendingTileRefreshAccountIds.Clear();
            bool needsRoster = rosterGridRoot != null && (rosterGridRoot.childCount == 0 || shouldForceRosterRefresh);
            bool needsSettings = settingsContentRoot != null && settingsContentRoot.childCount == 0;

            if (!needsRoster && rosterGridRoot != null)
            {
                SyncGroupBadgesFromKnownState();

                if (pendingTileRefreshIds.Count > 0)
                {
                    RefreshRosterTiles(pendingTileRefreshIds);
                }
            }

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
                rosterPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlRosterPanel", GetSocialUiText("SquadControlRosterTab", "Roster"));
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

            titleLabel = titleObject.GetComponent<TextMeshProUGUI>();
            EnsureStandaloneCloseButton();
        }

        private void EnsureStandaloneCloseButton()
        {
            if (standaloneCloseButton != null && standaloneCloseButton.transform.parent == screenRoot.transform)
            {
                return;
            }

            DefaultUIButton closeButton = ResolveOverlayBackButtonTemplate();
            if (closeButton == null)
            {
                return;
            }

            closeButton.transform.SetParent(screenRoot.transform, false);
            closeButton.name = "friendlySAIN_SquadControlStandaloneClose";
            closeButton.gameObject.SetActive(false);
            standaloneCloseButton = closeButton;
        }

        private DefaultUIButton ResolveOverlayBackButtonTemplate()
        {
            MatchMakerSideSelectionScreen sideSelectionScreen = Resources.FindObjectsOfTypeAll<MatchMakerSideSelectionScreen>()
                .FirstOrDefault(screen => screen != null);
            DefaultUIButton template = sideSelectionScreen?.transform.Find("ScreenDefaultButtons/BackButton")?.GetComponent<DefaultUIButton>()
                ?? MatchmakerBackButtonField?.GetValue(sideSelectionScreen) as DefaultUIButton;
            if (template == null)
            {
                return null;
            }

            DefaultUIButton clone = Instantiate(template, screenRoot.transform, false);
            clone.gameObject.SetActive(true);
            clone.Interactable = true;

            if (template.transform is RectTransform templateRect && clone.transform is RectTransform cloneRect)
            {
                cloneRect.anchorMin = new Vector2(0.5f, 0f);
                cloneRect.anchorMax = new Vector2(0.5f, 0f);
                cloneRect.pivot = new Vector2(0.5f, 0f);
                cloneRect.sizeDelta = templateRect.sizeDelta;
                cloneRect.anchoredPosition = new Vector2(0f, RaidOverlayBackButtonYOffset);
                cloneRect.localScale = templateRect.localScale;
            }

            return clone;
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

        internal void ShowRaidSettingsOverlay()
        {
            EnsureScreen();
            if (screenRoot == null)
            {
                return;
            }

            EnsureStandaloneCloseButton();

            RetractPanels();
            raidSettingsOverlayActive = true;
            screenRoot.SetActive(true);
            SetStandaloneTitle(GetSocialUiText("SquadControlRaidSettingsTitle", "My Squad Settings"));
            ShowTab(false);

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.SetRawText(GetSocialUiText("SquadControlBack", "Back"), standaloneCloseButton.HeaderSize);
                standaloneCloseButton.SetIcon(null);
                standaloneCloseButton.OnClick.RemoveAllListeners();
                standaloneCloseButton.OnClick.AddListener(HideRaidSettingsOverlay);
                standaloneCloseButton.Interactable = true;
                standaloneCloseButton.transform.SetAsLastSibling();
                standaloneCloseButton.gameObject.SetActive(true);
            }

            if (settingsContentRoot == null || settingsContentRoot.childCount == 0)
            {
                RebuildSettingsEntries();
            }

            SyncButtonVisibility();
        }

        internal void HideRaidSettingsOverlay()
        {
            raidSettingsOverlayActive = false;

            if (screenRoot != null)
            {
                screenRoot.SetActive(false);
            }

            if (standaloneCloseButton != null)
            {
                standaloneCloseButton.gameObject.SetActive(false);
            }

            SetStandaloneTitle(GetSocialUiText("SquadControlTitle", "My Squad"));
            CancelShortcutCapture(false);
            SyncButtonVisibility();
        }

        private void SetStandaloneTitle(string title)
        {
            if (titleLabel == null)
            {
                return;
            }

            titleLabel.text = title;
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

    }
}
