using EFT.UI;
using HarmonyLib;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace friendlySAIN.Components
{
    internal class SquadControlMenuUi : MonoBehaviour
    {
        private const string SquadButtonName = "friendlySAIN_SquadControlButton";
        private const string ScreenRootName = "friendlySAIN_SquadControlScreen";

        private static readonly FieldInfo HeaderLabelField = AccessTools.Field(typeof(DefaultUIButton), "_headerLabel");
        private static readonly FieldInfo TraderCloseButtonField = AccessTools.Field(typeof(TraderScreensGroup), "_closeButton");
        private static readonly FieldInfo TraderCardsContainerField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardsContainer");
        private static readonly FieldInfo TraderCardPrefabField = AccessTools.Field(typeof(TraderScreensGroup), "_traderCardPrefab");
        private static readonly FieldInfo TraderCardAvatarField = AccessTools.Field(typeof(TraderCard), "_traderAvatar");
        private static readonly FieldInfo TraderCardRankPanelField = AccessTools.Field(typeof(TraderCard), "_rankPanel");
        private static readonly FieldInfo TraderCardQuestionMarkField = AccessTools.Field(typeof(TraderCard), "_questionMark");
        private static readonly FieldInfo TraderCardStandingField = AccessTools.Field(typeof(TraderCard), "_standing");
        private static readonly FieldInfo TraderCardTimeLeftField = AccessTools.Field(typeof(TraderCard), "_timeLeft");
        private static readonly FieldInfo TraderCardNickNameField = AccessTools.Field(typeof(TraderCard), "_nickName");
        private static readonly FieldInfo RagfairAllOffersToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_allOffersToggle");
        private static readonly FieldInfo RagfairWishListToggleField = AccessTools.Field(typeof(EFT.UI.Ragfair.RagfairScreen), "_wishListToggle");
        private static readonly FieldInfo SpawnableToggleHeaderLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_headerLabel");
        private static readonly FieldInfo SpawnableToggleSizeLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_sizeLabel");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
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
        private GameObject rosterPanel;
        private GameObject settingsPanel;
        private readonly Dictionary<RectTransform, Vector2> originalButtonPositions = new Dictionary<RectTransform, Vector2>();

        public static SquadControlMenuUi GetOrCreate(MenuScreen menuScreen)
        {
            SquadControlMenuUi ui = menuScreen.GetComponent<SquadControlMenuUi>();
            if (ui == null)
            {
                ui = menuScreen.gameObject.AddComponent<SquadControlMenuUi>();
            }

            return ui;
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

                ShiftMenuColumn(playerRect, 26f);

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
            background.color = new Color(0.04f, 0.05f, 0.06f, 0.96f);
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
            TraderCard traderCardTemplate = TraderCardPrefabField?.GetValue(templateGroup) as TraderCard;
            UIAnimatedToggleSpawner rosterTabTemplate = ResolveRagfairToggleTemplate(true);
            UIAnimatedToggleSpawner settingsTabTemplate = ResolveRagfairToggleTemplate(false);

            if (closeTemplate == null || rosterTabTemplate == null || settingsTabTemplate == null || cardsContainerTemplate == null || traderCardTemplate == null)
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
            stockCardsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            stockCardsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            stockCardsContainer.pivot = new Vector2(0.5f, 0.5f);
            stockCardsContainer.sizeDelta = new Vector2(1180f, 230f);
            stockCardsContainer.anchoredPosition = new Vector2(0f, -12f);
            CenterRosterContainer(stockCardsContainer);

            for (int i = stockCardsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(stockCardsContainer.GetChild(i).gameObject);
            }

            rosterPanel = new GameObject("friendlySAIN_SquadControlRosterPanel", typeof(RectTransform));
            rosterPanel.transform.SetParent(rootRect, false);
            RectTransform rosterRect = rosterPanel.GetComponent<RectTransform>();
            rosterRect.anchorMin = new Vector2(0.5f, 0.5f);
            rosterRect.anchorMax = new Vector2(0.5f, 0.5f);
            rosterRect.pivot = new Vector2(0.5f, 0.5f);
            rosterRect.sizeDelta = new Vector2(1260f, 360f);
            rosterRect.anchoredPosition = new Vector2(0f, -8f);

            stockCardsContainer.SetParent(rosterRect, false);
            CreateRosterPlaceholderCards(traderCardTemplate, stockCardsContainer);

            settingsPanel = CreateFallbackContentPanel("friendlySAIN_SquadControlSettingsPanel", GetSocialUiText("SquadControlSettingsTab", "Settings"));
            return true;
        }

        private void OpenScreen()
        {
            if (screenRoot == null)
            {
                return;
            }

            screenRoot.SetActive(true);
            SyncButtonVisibility();
        }

        private void CloseScreen()
        {
            if (screenRoot == null)
            {
                return;
            }

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

        private void CreateRosterPlaceholderCards(TraderCard traderCardTemplate, RectTransform parent)
        {
            CreateRosterCard(traderCardTemplate, parent, GetSocialUiText("SquadControlRosterPlayer", "PLAYER"), GetSocialUiText("SquadControlRosterLeader", "Leader"), true);
            CreateRosterCard(traderCardTemplate, parent, GetSocialUiText("SquadControlRosterMemberA", "TEAMMATE 01"), GetSocialUiText("SquadControlRosterRole", "Squad Member"), false);
            CreateRosterCard(traderCardTemplate, parent, GetSocialUiText("SquadControlRosterMemberB", "TEAMMATE 02"), GetSocialUiText("SquadControlRosterRole", "Squad Member"), false);
        }

        private void CreateRosterCard(TraderCard template, RectTransform parent, string title, string subtitle, bool selected)
        {
            TraderCard card = Instantiate(template, parent, false);
            card.name = $"friendlySAIN_{title.Replace(' ', '_')}";
            card.ApplyState(selected ? TraderCard.ETraderCardState.Selected : TraderCard.ETraderCardState.Available);

            if (TraderCardAvatarField?.GetValue(card) is Component avatar)
            {
                avatar.gameObject.SetActive(false);
            }

            if (TraderCardRankPanelField?.GetValue(card) is Component rankPanel)
            {
                rankPanel.gameObject.SetActive(false);
            }

            if (TraderCardQuestionMarkField?.GetValue(card) is GameObject questionMark)
            {
                questionMark.SetActive(false);
            }

            if (TraderCardNickNameField?.GetValue(card) is LocalizedText nickname)
            {
                nickname.enabled = false;
                nickname.method_2(title);
            }

            if (TraderCardStandingField?.GetValue(card) is TMP_Text standing)
            {
                standing.text = subtitle;
            }

            if (TraderCardTimeLeftField?.GetValue(card) is TMP_Text timeLeft)
            {
                timeLeft.text = string.Empty;
            }

            AddCardIcon(card.transform as RectTransform);
        }

        private void CenterRosterContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            if (container.GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup horizontalLayout)
            {
                horizontalLayout.childAlignment = TextAnchor.MiddleCenter;
                horizontalLayout.padding = new RectOffset(0, 0, 0, 0);
            }

            if (container.GetComponent<GridLayoutGroup>() is GridLayoutGroup gridLayout)
            {
                gridLayout.childAlignment = TextAnchor.MiddleCenter;
                gridLayout.padding = new RectOffset(0, 0, 0, 0);
            }
        }

        private void AddCardIcon(RectTransform cardRect)
        {
            if (cardRect == null)
            {
                return;
            }

            GameObject iconObject = new GameObject("friendlySAIN_CardIcon", typeof(RectTransform), typeof(Image));
            iconObject.transform.SetParent(cardRect, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.58f);
            iconRect.anchorMax = new Vector2(0.5f, 0.58f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(52f, 52f);
            iconRect.anchoredPosition = Vector2.zero;

            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = LoadSquadIcon();
            icon.preserveAspect = true;
            icon.color = new Color(0.92f, 0.92f, 0.9f, 0.92f);
        }

        private TraderScreensGroup ResolveTraderScreensGroupTemplate()
        {
            return Resources.FindObjectsOfTypeAll<TraderScreensGroup>()
                .FirstOrDefault(group =>
                    group != null &&
                    TraderCloseButtonField?.GetValue(group) is DefaultUIButton &&
                    TraderCardsContainerField?.GetValue(group) is Transform &&
                    TraderCardPrefabField?.GetValue(group) is TraderCard);
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

        private void ShiftMenuColumn(RectTransform playerRect, float amount)
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

                sibling.anchoredPosition += new Vector2(0f, amount);
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
