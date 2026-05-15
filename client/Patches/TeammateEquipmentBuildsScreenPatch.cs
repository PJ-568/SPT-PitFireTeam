using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Builds;
using EFT.UI.Health;
using EFT.UI.Screens;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ResultProfile = GClass1416;

namespace pitTeam.Patches
{
    internal static class TeammateEquipmentBuildsScreenFlow
    {
        private static readonly FieldInfo ButtonRawTextField = AccessTools.Field(typeof(DefaultUIButton), "_rawText");
        private static readonly FieldInfo BuildListWeightField = AccessTools.Field(typeof(EquipmentBuildListView), "_weight");
        private static readonly FieldInfo ScreenSelectedBuildField = AccessTools.Field(typeof(EquipmentBuildsScreen), "gclass3953_0");
        private static readonly FieldInfo ScreenWeightField = AccessTools.Field(typeof(EquipmentBuildsScreen), "_weight");
        private static readonly FieldInfo EditBuildOnlyAvailableToggleField = AccessTools.Field(typeof(EditBuildScreen), "_onlyAvailableToggle");
        private const string BuyKitRoute = "/singleplayer/pitfireteam/teammate/profile/buy-kit";
        private const float MarketPricesRefreshIntervalSeconds = 300f;

        private static string _accountId;
        private static ResultProfile _profile;
        private static ISession _session;
        private static GClass3387 _backendInventoryController;
        private static EquipmentBuildsScreen _activeScreen;
        private static Action _profileBackOverrideAction;
        private static bool _openingFromProfile;
        private static bool _returningToProfile;
        private static bool _customizingBuildsScreen;
        private static bool _marketPricesRequestInFlight;
        private static bool _excludeExistingItems;
        private static float _marketPricesUpdatedAt = -MarketPricesRefreshIntervalSeconds;
        private static Dictionary<string, float> _marketPrices;
        private static Dictionary<string, int> _selectedBuildMissingCounts = new Dictionary<string, int>();
        private static List<Item> _selectedBuildMissingItems = new List<Item>();
        private static Sprite _roublesSprite;
        private static ScreenChromeState _screenChromeState;
        private static GameObject _buyConfirmOverlay;
        private static readonly MarketPriceSource BuildMarketPriceSource = new MarketPriceSource();
        private static readonly Dictionary<int, BuildRowState> BuildRowStates = new Dictionary<int, BuildRowState>();

        public static bool IsActive => !string.IsNullOrWhiteSpace(_accountId);
        public static bool ShouldCustomizeBuildsScreen => _customizingBuildsScreen && IsActive;

        public static bool ShouldSuppressSearchInteraction(EItemInfoButton button)
        {
            return ShouldCustomizeBuildsScreen
                && (button == EItemInfoButton.EditBuild
                    || button == EItemInfoButton.FilterSearch
                    || button == EItemInfoButton.LinkedSearch
                    || button == EItemInfoButton.NeededSearch);
        }

        private static string GetSocialUiText(string key, string fallback)
        {
            if (pitFireTeam.optionsLang?.socialUi != null
                && pitFireTeam.optionsLang.socialUi.TryGetValue(key, out string value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }

        public static void CaptureAndSuppressMissingItems(ref IEnumerable<Item> notFoundItems)
        {
            if (!ShouldCustomizeBuildsScreen || notFoundItems == null)
            {
                return;
            }

            List<Item> missingItems = new List<Item>(notFoundItems);
            _selectedBuildMissingItems = missingItems;
            _selectedBuildMissingCounts = SummarizeMissingItems(missingItems);

            // Stock build preview paints missing build items red by feeding this list
            // into GClass3452. The teammate buy screen needs the same availability
            // data for later purchase math, but should not present the stock "you
            // cannot equip this now" warning state.
            notFoundItems = Array.Empty<Item>();
        }

        public static void Open(ResultProfile profile, ISession session, InventoryController inventoryController)
        {
            if (profile == null || session == null || inventoryController == null)
            {
                NotificationManagerClass.DisplayWarningNotification(GetSocialUiText("KitLoadoutsOpenFailed", "Unable to open teammate kit loadouts."), ENotificationDurationType.Default);
                pitFireTeam.Log.LogWarning("[UI] Buy loadout screen aborted: missing teammate profile, session, or inventory controller.");
                return;
            }

            if (!TryResolveBackendController(session, inventoryController, out GClass3387 backendController))
            {
                NotificationManagerClass.DisplayWarningNotification(GetSocialUiText("KitLoadoutsOpenFailed", "Unable to open teammate kit loadouts."), ENotificationDurationType.Default);
                pitFireTeam.Log.LogWarning($"[UI] Buy loadout screen aborted: expected backend inventory controller, got '{inventoryController.GetType().Name}'.");
                return;
            }

            _accountId = profile.AccountId;
            _profile = profile;
            _session = session;
            _profileBackOverrideAction = OtherPlayerProfileScreenPatch.ActiveBackOverrideAction;
            _openingFromProfile = true;
            _returningToProfile = false;
            _customizingBuildsScreen = true;
            _excludeExistingItems = false;

            // Buy mode is a scoped overlay on top of EFT's stock builds screen. The
            // later patches use this state to decide whether they should behave like
            // stock EFT or like the teammate shop.
            OtherPlayerProfileScreenPatch.DisableMenuTaskBarForReturnOverride();
            RequestMarketPrices(session, forceRefresh: false);

            IHealthController healthController = OtherPlayerProfileScreenPatch.CreateProfileHealthController(session.Profile?.Health);
            new EquipmentBuildsScreen.GClass3870(session, backendController, healthController, backendController.Inventory.Equipment)
                .ShowScreen(EScreenState.Queued);

            pitFireTeam.Log.LogInfo($"[UI] Opening teammate equipment builds screen for '{profile.AccountId}'.");
        }

        public static bool ConsumeProfileTransition(Action profileBackOverrideAction)
        {
            if (!_openingFromProfile)
            {
                return false;
            }

            _openingFromProfile = false;
            _profileBackOverrideAction = profileBackOverrideAction ?? _profileBackOverrideAction;
            return true;
        }

        public static bool TryBuildTeammateVisual(EquipmentBuildsScreen screen, LastPlayerStateClass currentVisual, out LastPlayerStateClass teammateVisual)
        {
            teammateVisual = null;
            if (!IsActive || screen == null || _profile == null || currentVisual == null)
            {
                return false;
            }

            teammateVisual = new LastPlayerStateClass(new GClass1410
            {
                Level = InfoClass.GetLevel(_profile.Info.Experience),
                MemberCategory = _profile.Info.MemberCategory,
                Nickname = _profile.Info.Nickname,
                PrestigeLevel = _profile.Info.PrestigeLevel,
                SelectedMemberCategory = _profile.Info.SelectedMemberCategory,
                Side = _profile.Info.Side
            }, _profile.Customization, currentVisual.Equipment);
            return true;
        }

        public static void ApplyScreenChrome(EquipmentBuildsScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            if (!ShouldCustomizeBuildsScreen)
            {
                // The player character screen also uses EquipmentBuildsScreen. Restore
                // only captured teammate-buy state and otherwise leave stock screens
                // untouched.
                RestoreScreenChrome(screen);
                return;
            }

            _activeScreen = screen;
            CaptureScreenChrome(screen);
            HideScreenBottomPanel(screen.transform);
            HideCurrentWeightValue(screen.transform);
            EnsureExcludeExistingItemsToggle(screen.transform);
            ApplyActionButtonText(screen.transform);
            HideCanEquipIcon(screen.transform);
            ApplySelectedBuildPrice(screen);
        }

        public static void ApplyBuildListRow(EquipmentBuildListView view, GClass3749<GClass3953> buildWrapper)
        {
            if (view == null)
            {
                return;
            }

            if (!ShouldCustomizeBuildsScreen)
            {
                RestoreBuildListRow(view);
                return;
            }

            CaptureBuildListRow(view);
            RememberBuildListRow(view, buildWrapper?.Build);
            HideChild(view.transform, "DeleteHolder");
            ApplyRoublesIcon(view.transform);
            ApplyBuildListPrice(view, buildWrapper);
        }

        public static bool HandleBackToProfile()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return false;
            }

            ReturnToProfile();
            return true;
        }

        public static bool HandleBuyRequest()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return false;
            }

            if (!TryCreateBuyQuote(out EquipmentBuildBuyQuote quote))
            {
                NotificationManagerClass.DisplayWarningNotification(GetSocialUiText("KitLoadoutPriceFailed", "Unable to price selected teammate kit."), ENotificationDurationType.Default);
                return true;
            }

            if (!CanAffordBuyQuote(quote))
            {
                ShowNotEnoughResourcesOverlay(quote);
                return true;
            }

            ShowBuyConfirmOverlay(quote);
            return true;
        }

        public static void FinishReturnIfMatches(string accountId)
        {
            if (!_returningToProfile || string.IsNullOrWhiteSpace(accountId) || !string.Equals(accountId, _accountId, StringComparison.Ordinal))
            {
                return;
            }

            ClearReturnState();
        }

        private static bool TryResolveBackendController(ISession session, InventoryController inventoryController, out GClass3387 backendController)
        {
            backendController = inventoryController as GClass3387;
            if (backendController != null)
            {
                _backendInventoryController = backendController;
                return true;
            }

            if (_backendInventoryController?.Profile != null
                && session?.Profile != null
                && ReferenceEquals(_backendInventoryController.Profile, session.Profile))
            {
                backendController = _backendInventoryController;
                return true;
            }

            return false;
        }

        private static void ReturnToProfile()
        {
            if (_returningToProfile)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_accountId))
            {
                Clear();
                OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
                return;
            }

            _returningToProfile = true;
            OtherPlayerProfileScreenPatch.PrepareReturnOverride(_profileBackOverrideAction);
            ItemUiContext.Instance.ShowPlayerProfileScreen(_accountId, EItemViewType.OtherPlayerProfile)
                .ContinueWith(task =>
                {
                    if (task.IsFaulted || task.IsCanceled || task.Result == null)
                    {
                        pitFireTeam.Log.LogError("[UI] Failed to return from teammate equipment builds screen to teammate profile.");
                        if (task.Exception != null)
                        {
                            pitFireTeam.Log.LogError(task.Exception);
                        }

                        Clear();
                        OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
                    }
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext())
                .HandleExceptions();
        }

        public static void HandleScreenClosed()
        {
            CloseBuyConfirmOverlay();
            RestoreScreenChrome();
            RestoreBuildListRows();

            if (!ShouldCustomizeBuildsScreen)
            {
                return;
            }

            _customizingBuildsScreen = false;
            if (!_returningToProfile)
            {
                ClearReturnState();
                OtherPlayerProfileScreenPatch.RestoreMenuTaskBarForReturnOverride();
            }
        }

        public static void HideInspectInteractionButtons(ItemSpecificationPanel panel)
        {
            if (!ShouldCustomizeBuildsScreen || panel == null)
            {
                return;
            }

            Transform buttonsPanel = FindChildRecursive(panel.transform, "InteractionButtonsPanel");
            if (buttonsPanel != null)
            {
                buttonsPanel.gameObject.SetActive(false);
            }
        }

        private static void Clear()
        {
            ClearReturnState();
            _customizingBuildsScreen = false;
            _activeScreen = null;
        }

        public static void ClearIfNotOpeningOrReturning()
        {
            if (_openingFromProfile || _returningToProfile)
            {
                return;
            }

            Clear();
        }

        private static void ClearReturnState()
        {
            _accountId = null;
            _profile = null;
            _session = null;
            _profileBackOverrideAction = null;
            _openingFromProfile = false;
            _returningToProfile = false;
            _excludeExistingItems = false;
            _selectedBuildMissingCounts = new Dictionary<string, int>();
            _selectedBuildMissingItems = new List<Item>();
        }

        private static void CaptureScreenChrome(EquipmentBuildsScreen screen)
        {
            if (_screenChromeState?.Screen == screen)
            {
                return;
            }

            // Capture before hiding/changing anything. EFT reuses this screen for the
            // normal player build flow, so every buy-mode mutation needs a stock value
            // to restore on exit.
            Transform equipButtonTransform = FindEquipButtonTransform(screen.transform);
            DefaultUIButton equipButton = equipButtonTransform?.GetComponent<DefaultUIButton>();
            Transform canEquip = FindCanEquipTransform(screen.transform);

            _screenChromeState = new ScreenChromeState
            {
                Screen = screen,
                EquipButton = equipButton,
                EquipButtonText = equipButton?.HeaderText,
                EquipButtonFontSize = equipButton?.HeaderSize ?? 18,
                EquipButtonRawText = equipButton != null && ButtonRawTextField?.GetValue(equipButton) is bool rawText && rawText,
                EquipButtonLabel = equipButtonTransform?.GetComponentInChildren<TMP_Text>(true),
                EquipButtonLabelText = equipButtonTransform?.GetComponentInChildren<TMP_Text>(true)?.text,
                CanEquip = canEquip,
                CanEquipActive = canEquip?.gameObject.activeSelf,
                ScreenObjects = FindScreenChromeObjects(screen.transform)
            };
        }

        private static void RestoreScreenChrome(EquipmentBuildsScreen screen = null)
        {
            if (_screenChromeState == null)
            {
                return;
            }

            if (screen != null && _screenChromeState.Screen != null && _screenChromeState.Screen != screen)
            {
                return;
            }

            if (_screenChromeState.EquipButton != null)
            {
                _screenChromeState.EquipButton.method_1(
                    _screenChromeState.EquipButtonText ?? "EQUIP",
                    _screenChromeState.EquipButtonFontSize,
                    _screenChromeState.EquipButtonRawText);
            }
            else if (_screenChromeState.EquipButtonLabel != null)
            {
                _screenChromeState.EquipButtonLabel.text = _screenChromeState.EquipButtonLabelText ?? "EQUIP";
            }

            if (_screenChromeState.CanEquip != null && _screenChromeState.CanEquipActive != null)
            {
                _screenChromeState.CanEquip.gameObject.SetActive(_screenChromeState.CanEquipActive.Value);
            }

            if (_screenChromeState.ExcludeExistingItemsToggleRoot != null)
            {
                UnityEngine.Object.Destroy(_screenChromeState.ExcludeExistingItemsToggleRoot);
            }

            if (_screenChromeState.Screen == _activeScreen)
            {
                _activeScreen = null;
            }

            if (_screenChromeState.ScreenObjects != null)
            {
                foreach (KeyValuePair<GameObject, bool> pair in _screenChromeState.ScreenObjects)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetActive(pair.Value);
                    }
                }
            }

            _screenChromeState = null;
        }

        private static void CaptureBuildListRow(EquipmentBuildListView view)
        {
            int instanceId = view.GetInstanceID();
            if (BuildRowStates.ContainsKey(instanceId))
            {
                return;
            }

            Transform deleteHolder = FindChildRecursive(view.transform, "DeleteHolder");
            Image weightIcon = FindChildRecursive(view.transform, "WeightIcon")?.GetComponent<Image>();
            BuildRowStates[instanceId] = new BuildRowState
            {
                DeleteHolder = deleteHolder,
                DeleteHolderActive = deleteHolder?.gameObject.activeSelf,
                WeightIcon = weightIcon,
                WeightIconSprite = weightIcon?.sprite,
                WeightIconEnabled = weightIcon?.enabled,
                WeightIconPreserveAspect = weightIcon?.preserveAspect
            };
        }

        private static void RememberBuildListRow(EquipmentBuildListView view, GClass3953 build)
        {
            if (view == null)
            {
                return;
            }

            int instanceId = view.GetInstanceID();
            if (BuildRowStates.TryGetValue(instanceId, out BuildRowState state))
            {
                state.View = view;
                state.Build = build;
            }
        }

        private static void RestoreBuildListRow(EquipmentBuildListView view)
        {
            int instanceId = view.GetInstanceID();
            if (!BuildRowStates.TryGetValue(instanceId, out BuildRowState state))
            {
                return;
            }

            RestoreBuildRowState(state);
            BuildRowStates.Remove(instanceId);
        }

        private static void RestoreBuildListRows()
        {
            foreach (BuildRowState state in BuildRowStates.Values)
            {
                RestoreBuildRowState(state);
            }

            BuildRowStates.Clear();
        }

        private static void RestoreBuildRowState(BuildRowState state)
        {
            if (state.DeleteHolder != null && state.DeleteHolderActive != null)
            {
                state.DeleteHolder.gameObject.SetActive(state.DeleteHolderActive.Value);
            }

            if (state.WeightIcon != null)
            {
                state.WeightIcon.sprite = state.WeightIconSprite;
                if (state.WeightIconEnabled != null)
                {
                    state.WeightIcon.enabled = state.WeightIconEnabled.Value;
                }

                if (state.WeightIconPreserveAspect != null)
                {
                    state.WeightIcon.preserveAspect = state.WeightIconPreserveAspect.Value;
                }
            }
        }

        private static void HideScreenBottomPanel(Transform screen)
        {
            HideChild(screen, "BottomPanel");
            HideChild(screen, "Bottom Panel");
            HideChild(screen, "Bottom");
        }

        private static void HideCurrentWeightValue(Transform screen)
        {
            Transform currentWeight = FindCurrentWeightTransform(screen);
            if (currentWeight != null)
            {
                currentWeight.gameObject.SetActive(false);
            }
        }

        private static void EnsureExcludeExistingItemsToggle(Transform screen)
        {
            if (_screenChromeState?.ExcludeExistingItemsToggleRoot != null)
            {
                return;
            }

            Transform buttonTransform = FindEquipButtonTransform(screen);
            RectTransform buttonRect = buttonTransform as RectTransform;
            RectTransform parent = buttonTransform?.parent as RectTransform;
            if (buttonTransform == null || parent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Could not add teammate buy exclude-existing checkbox: missing equip button parent.");
                return;
            }

            Toggle sourceToggle = ResolveEditBuildOnlyAvailableToggleTemplate();
            if (sourceToggle == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Could not add teammate buy exclude-existing checkbox: EditBuildScreen OnlyAvailable toggle template was unavailable.");
                return;
            }

            Toggle clonedToggle = UnityEngine.Object.Instantiate(sourceToggle, parent, false);
            GameObject root = clonedToggle.gameObject;
            root.name = "pitFireTeam_ExcludeExistingItemsToggle";
            root.transform.SetParent(parent, false);
            root.transform.SetSiblingIndex(buttonTransform.GetSiblingIndex() + 1);
            root.SetActive(true);

            RectTransform rootRect = root.GetComponent<RectTransform>() ?? root.AddComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(245f, buttonRect != null && buttonRect.rect.height > 1f ? buttonRect.rect.height : 42f);

            HorizontalOrVerticalLayoutGroup layoutGroup = parent.GetComponent<HorizontalOrVerticalLayoutGroup>();
            LayoutElement layout = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
            layout.minWidth = 225f;
            layout.preferredWidth = 245f;
            layout.preferredHeight = rootRect.sizeDelta.y;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            if (layoutGroup == null && buttonRect != null)
            {
                rootRect.anchorMin = buttonRect.anchorMin;
                rootRect.anchorMax = buttonRect.anchorMax;
                rootRect.pivot = buttonRect.pivot;
                rootRect.anchoredPosition = buttonRect.anchoredPosition + new Vector2(265f, 0f);
            }

            ConfigureClonedExcludeExistingToggle(clonedToggle);
            clonedToggle.SetIsOnWithoutNotify(false);
            clonedToggle.onValueChanged.AddListener(isOn =>
            {
                _excludeExistingItems = isOn;
                pitFireTeam.Log.LogInfo($"[UI] Teammate buy exclude existing items changed: {isOn}");
                ApplyActionButtonText(screen);
            });

            _screenChromeState.ExcludeExistingItemsToggleRoot = root;
        }

        private static bool TryCreateBuyQuote(out EquipmentBuildBuyQuote quote)
        {
            quote = null;
            GClass3953 selectedBuild = ScreenSelectedBuildField?.GetValue(_activeScreen) as GClass3953;
            if (selectedBuild?.Equipment == null)
            {
                return false;
            }

            int fullKitPrice = CalculateBuildMarketRoublePrice(selectedBuild.Equipment);
            StashOnlyExclusionPlan exclusionPlan = _excludeExistingItems
                ? CreateStashOnlyExclusionPlan(selectedBuild.Equipment, fullKitPrice)
                : StashOnlyExclusionPlan.Empty(fullKitPrice);

            quote = new EquipmentBuildBuyQuote
            {
                Build = selectedBuild,
                BuildName = string.IsNullOrWhiteSpace(selectedBuild.Name) ? "Selected loadout" : selectedBuild.Name,
                FullKitPrice = fullKitPrice,
                MissingOnlyPrice = exclusionPlan.FinalPrice,
                FinalPrice = exclusionPlan.FinalPrice,
                ExcludeExistingItems = _excludeExistingItems,
                CanEquipFromStash = _excludeExistingItems && exclusionPlan.HasAllRequiredItems,
                MissingTemplateCounts = new Dictionary<string, int>(_selectedBuildMissingCounts),
                UsedStashItems = exclusionPlan.UsedItems,
                PurchasedItems = exclusionPlan.PurchasedItems
            };
            return true;
        }

        private static bool CanAffordBuyQuote(EquipmentBuildBuyQuote quote)
        {
            if (quote == null)
            {
                return false;
            }

            if (quote.FinalPrice > GetAvailableStashRoubles())
            {
                return false;
            }

            return !quote.ExcludeExistingItems || HasRequiredStashItems(quote.UsedStashItems);
        }

        private static int GetAvailableStashRoubles()
        {
            Inventory inventory = _backendInventoryController?.Inventory;
            CompoundItem stash = inventory?.Stash;
            if (stash == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Item item in CollectDeepItemTree(stash))
            {
                if (item == null || ReferenceEquals(item, stash))
                {
                    continue;
                }

                if (GClass3130.TryGetCurrencyType(new MongoID?(item.TemplateId), out ECurrencyType currencyType)
                    && currencyType == ECurrencyType.RUB
                    && item.PinLockState != EItemPinLockState.Locked)
                {
                    total += Mathf.Max(1, item.StackObjectsCount);
                }
            }

            return total;
        }

        private static bool HasRequiredStashItems(IEnumerable<UsedStashItemSummary> requiredItems)
        {
            if (requiredItems == null)
            {
                return true;
            }

            Dictionary<string, int> stashCounts = CreateStashOnlyTemplateCounts();
            foreach (UsedStashItemSummary requiredItem in requiredItems)
            {
                if (requiredItem == null || string.IsNullOrWhiteSpace(requiredItem.TemplateId) || requiredItem.Count <= 0)
                {
                    continue;
                }

                if (!stashCounts.TryGetValue(requiredItem.TemplateId, out int available) || available < requiredItem.Count)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ShowBuyConfirmOverlay(EquipmentBuildBuyQuote quote)
        {
            CloseBuyConfirmOverlay();

            Transform overlayParent = _activeScreen?.transform;
            if (overlayParent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Buy confirmation overlay could not open: no overlay parent was available.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_BuyLoadoutConfirmOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_BuyLoadoutConfirmPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            bool hasItemizedQuote = quote.ExcludeExistingItems && (quote.UsedStashItems.Count > 0 || quote.PurchasedItems.Count > 0);
            int itemizedLineCount = CountOverlayTextLines(CreateBuyConfirmBodyText(quote));
            float panelHeight = hasItemizedQuote ? Mathf.Clamp(250f + itemizedLineCount * 22f, 340f, 640f) : 224f;
            panelRect.sizeDelta = hasItemizedQuote ? new Vector2(900f, panelHeight) : new Vector2(700f, panelHeight);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_BuyLoadoutConfirmHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutConfirmTitle",
                GetSocialUiText("BuyKitTitle", "BUY KIT"),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateOverlayCloseButton(header.transform, "pitFireTeam_BuyLoadoutConfirmCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(() =>
            {
                pitFireTeam.Log.LogInfo("[UI] Teammate equipment build buy confirmation closed.");
                CloseBuyConfirmOverlay();
            });

            string bodyText = CreateBuyConfirmBodyText(quote);

            if (hasItemizedQuote)
            {
                CreateScrollableOverlayText(panel.transform, "pitFireTeam_BuyLoadoutConfirmBodyScroll", bodyText, 18f, new Vector2(28f, 72f), new Vector2(-28f, -42f), itemizedLineCount);
            }
            else
            {
                GameObject bodyObject = CreateOverlayText(
                    "pitFireTeam_BuyLoadoutConfirmBody",
                    bodyText,
                    23f,
                    TextAlignmentOptions.Center);
                RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
                bodyRect.SetParent(panel.transform, false);
                bodyRect.anchorMin = new Vector2(0f, 0f);
                bodyRect.anchorMax = new Vector2(1f, 1f);
                bodyRect.offsetMin = new Vector2(28f, 72f);
                bodyRect.offsetMax = new Vector2(-28f, -42f);

                TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
                bodyLabel.enableWordWrapping = true;
                bodyLabel.overflowMode = TextOverflowModes.Ellipsis;
            }

            DefaultUIButton buyButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            if (buyButton != null)
            {
                buyButton.SetRawText(GetQuoteActionButtonText(quote), 22);
                buyButton.OnClick.RemoveAllListeners();
                buyButton.OnClick.AddListener(() => ConfirmBuyQuote(quote));
            }
            else
            {
                Button fallbackButton = CreateFallbackOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f), GetQuoteActionButtonText(quote));
                fallbackButton.onClick.AddListener(() => ConfirmBuyQuote(quote));
            }

            _buyConfirmOverlay = overlayRoot;
            pitFireTeam.Log.LogInfo($"[UI] Teammate equipment build buy confirmation opened: build='{quote.BuildName}', price={quote.FinalPrice}, excludeExistingItems={quote.ExcludeExistingItems}, stashItemsUsed={quote.UsedStashItems.Count}, missingTemplates={quote.MissingTemplateCounts.Count}.");
        }

        private static void ShowNotEnoughResourcesOverlay(EquipmentBuildBuyQuote quote)
        {
            CloseBuyConfirmOverlay();

            Transform overlayParent = _activeScreen?.transform;
            if (overlayParent == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Not-enough-resources overlay could not open: no overlay parent was available.");
                return;
            }

            GameObject overlayRoot = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(overlayParent, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            Stretch(overlayRect);
            overlayRect.SetAsLastSibling();

            Image overlayImage = overlayRoot.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.62f);
            overlayImage.raycastTarget = true;

            GameObject panel = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(700f, 224f);

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("pitFireTeam_BuyLoadoutNotEnoughResourcesHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutNotEnoughResourcesTitle",
                GetSocialUiText("BuyKitTitle", "BUY KIT"),
                18f,
                TextAlignmentOptions.MidlineLeft);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.SetParent(header.transform, false);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            Button closeButton = CreateOverlayCloseButton(header.transform, "pitFireTeam_BuyLoadoutNotEnoughResourcesCloseButton");
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(CloseBuyConfirmOverlay);

            string bodyText = string.Format(
                GetSocialUiText("NotEnoughResourcesKitPrompt", "Not enough resources to purchase {0} kit"),
                quote?.BuildName ?? "selected");
            GameObject bodyObject = CreateOverlayText(
                "pitFireTeam_BuyLoadoutNotEnoughResourcesBody",
                bodyText,
                23f,
                TextAlignmentOptions.Center);
            RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
            bodyRect.SetParent(panel.transform, false);
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(28f, 72f);
            bodyRect.offsetMax = new Vector2(-28f, -42f);

            TextMeshProUGUI bodyLabel = bodyObject.GetComponent<TextMeshProUGUI>();
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Ellipsis;

            string okText = GetSocialUiText("Ok", "OK");
            DefaultUIButton okButton = CreateOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f));
            if (okButton != null)
            {
                okButton.SetRawText(okText, 22);
                okButton.OnClick.RemoveAllListeners();
                okButton.OnClick.AddListener(CloseBuyConfirmOverlay);
            }
            else
            {
                Button fallbackButton = CreateFallbackOverlayActionButton(panel.transform, new Vector2(0f, 10f), new Vector2(180f, 36f), okText);
                fallbackButton.onClick.AddListener(CloseBuyConfirmOverlay);
            }

            _buyConfirmOverlay = overlayRoot;
            pitFireTeam.Log.LogInfo($"[UI] Teammate equipment build purchase blocked by resources: build='{quote?.BuildName}', price={quote?.FinalPrice ?? 0}, availableRoubles={GetAvailableStashRoubles()}, excludeExistingItems={quote?.ExcludeExistingItems ?? false}.");
        }

        private static void ConfirmBuyQuote(EquipmentBuildBuyQuote quote)
        {
            ConfirmBuyQuoteAsync(quote).HandleExceptions();
        }

        private static async Task ConfirmBuyQuoteAsync(EquipmentBuildBuyQuote quote)
        {
            if (quote?.Build?.Equipment == null || string.IsNullOrWhiteSpace(_accountId))
            {
                NotificationManagerClass.DisplayWarningNotification(GetSocialUiText("KitLoadoutPurchaseFailed", "Unable to purchase teammate kit."), ENotificationDurationType.Default);
                return;
            }

            SetBuyScreenBusy(true);
            try
            {
                FlatItemsDataClass[] serializedEquipment = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { quote.Build.Equipment });
                if (serializedEquipment == null || serializedEquipment.Length == 0)
                {
                    throw new InvalidOperationException("Selected teammate kit equipment was unavailable for purchase.");
                }

                string responseJson = await Task.Run(() => RequestHandler.PostJson(
                    BuyKitRoute,
                    SerializeBody(new FriendlyTeammateBuyKitRequest
                    {
                        aid = _accountId,
                        items = serializedEquipment,
                        price = quote.FinalPrice,
                        useItemsInStash = quote.ExcludeExistingItems,
                        usedItems = quote.UsedStashItems
                            .Select(item => new FriendlyTeammateBuyKitUsedItem
                            {
                                templateId = item.TemplateId,
                                count = item.Count
                            })
                            .ToArray()
                    })));

                FriendlyTeammateBodyResponse<FriendlyTeammateBuyKitResponse> response =
                    DeserializeBodySuccess<FriendlyTeammateBuyKitResponse>(responseJson);

                try
                {
                    OtherPlayerProfileScreenPatch.ApplyServerSavedPlayerStash(
                        _session?.Profile,
                        _backendInventoryController,
                        _session?.RagFair,
                        response?.data?.playerStashItems);
                }
                catch (Exception ex)
                {
                    pitFireTeam.Log.LogError("[UI] Failed to refresh live player stash after teammate kit purchase.");
                    pitFireTeam.Log.LogError(ex);
                    NotificationManagerClass.DisplayWarningNotification(
                        GetSocialUiText("LoadoutEditorRealCommitRestartRequired", "Loadout saved. Restart the game to refresh the player stash view."),
                        ENotificationDurationType.Default);
                }

                pitFireTeam.Log.LogInfo($"[UI] Teammate equipment build {GetQuoteActionButtonText(quote)} completed: build='{quote.BuildName}', price={quote.FinalPrice}, useItemsInStash={quote.ExcludeExistingItems}.");

                // The server has already saved the teammate's new Default gear here, so the
                // roster portrait can be regenerated from the updated profile when My Squad
                // is injected again.
                Components.SquadControlMenuUi.RequestRosterTileRefreshOnNextInject(_accountId);

                CloseBuyConfirmOverlay();
                ReturnToProfile();
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError("[UI] Failed to purchase teammate equipment build.");
                pitFireTeam.Log.LogError(ex);
                NotificationManagerClass.DisplayWarningNotification(ex.Message ?? GetSocialUiText("KitLoadoutPurchaseFailed", "Unable to purchase teammate kit."), ENotificationDurationType.Default);
            }
            finally
            {
                SetBuyScreenBusy(false);
            }
        }

        private static void CloseBuyConfirmOverlay()
        {
            if (_buyConfirmOverlay != null)
            {
                UnityEngine.Object.Destroy(_buyConfirmOverlay);
                _buyConfirmOverlay = null;
            }
        }

        private static void SetBuyScreenBusy(bool busy)
        {
            try
            {
                if (_buyConfirmOverlay != null)
                {
                    CanvasGroup canvasGroup = _buyConfirmOverlay.GetComponent<CanvasGroup>();
                    if (canvasGroup == null)
                    {
                        canvasGroup = _buyConfirmOverlay.AddComponent<CanvasGroup>();
                    }

                    canvasGroup.interactable = !busy;
                    canvasGroup.blocksRaycasts = true;
                }

                if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                {
                    MonoBehaviourSingleton<PreloaderUI>.Instance.SetLoaderStatus(busy);
                }
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to set teammate kit purchase busy state: {ex.Message}");
            }
        }

        private static string SerializeBody(object body)
        {
            return body.ToJson(GetDefaultJsonConverters());
        }

        private static FriendlyTeammateBodyResponse<T> DeserializeBodySuccess<T>(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            FriendlyTeammateBodyResponse<T> body = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<T>>(responseJson);
            if (body != null && body.err != 0)
            {
                throw new InvalidOperationException(body.errmsg ?? "Unknown teammate backend error");
            }

            return body;
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            Type converterClass = typeof(AbstractGame).Assembly
                .GetTypes()
                .First(type => type.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            return Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
        }

        private static DefaultUIButton CreateOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton template = FindEquipButtonTransform(_activeScreen?.transform)?.GetComponent<DefaultUIButton>();
            if (template == null)
            {
                return null;
            }

            DefaultUIButton button = UnityEngine.Object.Instantiate(template, parent, false);
            button.name = "pitFireTeam_BuyLoadoutConfirmActionButton";
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

        private static Button CreateFallbackOverlayActionButton(Transform parent, Vector2 anchoredPosition, Vector2 size, string text)
        {
            GameObject buttonObject = new GameObject("pitFireTeam_BuyLoadoutConfirmFallbackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.28f, 0.28f, 0.28f, 1f);
            image.raycastTarget = true;

            GameObject labelObject = CreateOverlayText("Label", text, 22f, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonObject.transform, false);
            Stretch(labelRect);

            return buttonObject.GetComponent<Button>();
        }

        private static Button CreateOverlayCloseButton(Transform parent, string name)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = CreateOverlayText($"{name}_Label", "X", 16f, TextAlignmentOptions.Center);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(buttonObject.transform, false);
            Stretch(labelRect);

            return buttonObject.GetComponent<Button>();
        }

        private static GameObject CreateOverlayText(string name, string text, float size, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = new Color(0.86f, 0.86f, 0.86f, 1f);
            label.raycastTarget = false;
            return textObject;
        }

        private static void CreateScrollableOverlayText(Transform parent, string name, string text, float size, Vector2 offsetMin, Vector2 offsetMax, int lineCount)
        {
            GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 0f);
            scrollRectTransform.anchorMax = new Vector2(1f, 1f);
            scrollRectTransform.offsetMin = offsetMin;
            scrollRectTransform.offsetMax = offsetMax;

            GameObject viewportObject = new GameObject($"{name}_Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            Mask mask = viewportObject.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject contentObject = CreateOverlayText($"{name}_Content", text, size, TextAlignmentOptions.TopLeft);
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, 0f);
            contentRect.offsetMax = new Vector2(0f, 0f);
            contentRect.sizeDelta = new Vector2(0f, Mathf.Max(160f, lineCount * 25f));

            TextMeshProUGUI label = contentObject.GetComponent<TextMeshProUGUI>();
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 35f;
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
            rect.localScale = Vector3.one;
        }

        private static string FormatRoubles(int amount)
        {
            return string.Format(GetSocialUiText("CurrencyRoubles", "{0:N0} RUB"), Mathf.Max(0, amount));
        }

        private static string CreateBuyConfirmBodyText(EquipmentBuildBuyQuote quote)
        {
            string deliveryText = GetSocialUiText(
                "KitCurrentGearDeliveryNotice",
                "Teammate's current kit will be returned to you via delivery service.");
            if (quote.CanEquipFromStash)
            {
                return $"{CreateUsedStashItemsText(quote)}\n\n{deliveryText}";
            }

            string text = string.Format(
                GetSocialUiText("PurchaseKitPrompt", "Purchase {0} Kit for {1}?"),
                quote.BuildName,
                FormatRoubles(quote.FinalPrice));
            text = $"{text}\n\n{deliveryText}";
            if (!quote.ExcludeExistingItems || quote.UsedStashItems.Count == 0)
            {
                return quote.ExcludeExistingItems && quote.PurchasedItems.Count > 0
                    ? $"{text}\n\n{CreatePurchasedItemsText(quote)}"
                    : text;
            }

            string usedText = CreateUsedStashItemsText(quote);
            if (quote.PurchasedItems.Count == 0)
            {
                return $"{text}\n\n{usedText}";
            }

            return $"{text}\n\n{usedText}\n\n{CreatePurchasedItemsText(quote)}";
        }

        private static string CreateUsedStashItemsText(EquipmentBuildBuyQuote quote)
        {
            IEnumerable<string> lines = CreateDisplayItemSummaries(quote.UsedStashItems)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatUsedStashItemLine);

            return $"{GetSocialUiText("KitItemsTakenFromStash", "The following items will be taken from stash:")}\n{string.Join("\n", lines)}";
        }

        private static string CreatePurchasedItemsText(EquipmentBuildBuyQuote quote)
        {
            IEnumerable<string> lines = CreateDisplayItemSummaries(quote.PurchasedItems)
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatUsedStashItemLine);

            return $"{GetSocialUiText("KitItemsPurchased", "The following items will be purchased:")}\n{string.Join("\n", lines)}";
        }

        private static List<UsedStashItemSummary> CreateDisplayItemSummaries(IEnumerable<UsedStashItemSummary> items)
        {
            if (items == null)
            {
                return new List<UsedStashItemSummary>();
            }

            return items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                .GroupBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new UsedStashItemSummary
                {
                    DisplayName = group.First().DisplayName,
                    Count = group.Sum(item => item.Count)
                })
                .ToList();
        }

        private static int CountOverlayTextLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            return text.Count(character => character == '\n') + 1;
        }

        private static string GetQuoteActionButtonText(EquipmentBuildBuyQuote quote)
        {
            return quote?.CanEquipFromStash == true
                ? GetSocialUiText("EquipKitAction", "EQUIP")
                : GetSocialUiText("PurchaseKitAction", "Purchase");
        }

        private static string FormatUsedStashItemLine(UsedStashItemSummary item)
        {
            return item.Count > 1
                ? $"{item.Count} x {item.DisplayName}"
                : item.DisplayName;
        }

        private static Toggle ResolveEditBuildOnlyAvailableToggleTemplate()
        {
            return EditBuildOnlyAvailableToggleField?.GetValue(CommonUI.Instance?.EditBuildScreen) as Toggle
                ?? CommonUI.Instance?.EditBuildScreen?.transform.Find("Toggle Group/OnlyAvailable")?.GetComponent<Toggle>()
                ?? FindChildRecursive(CommonUI.Instance?.EditBuildScreen?.transform, "OnlyAvailable")?.GetComponent<Toggle>();
        }

        private static void ConfigureClonedExcludeExistingToggle(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            // The stock EditBuildScreen toggle may already carry runtime listeners
            // from its original screen. Clear them so the clone only changes the
            // teammate-buy option and never drives weapon-build filtering.
            toggle.onValueChanged.RemoveAllListeners();
            toggle.group = null;
            toggle.interactable = true;

            foreach (Selectable selectable in toggle.GetComponentsInChildren<Selectable>(true))
            {
                selectable.interactable = true;
            }

            foreach (TMP_Text label in toggle.GetComponentsInChildren<TMP_Text>(true))
            {
                label.text = GetSocialUiText("UseItemsInStash", "Use items in stash");
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        private static void ApplySelectedBuildPrice(EquipmentBuildsScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            GClass3953 selectedBuild = ScreenSelectedBuildField?.GetValue(screen) as GClass3953;
            HealthParameterPanel weightPanel = ScreenWeightField?.GetValue(screen) as HealthParameterPanel;
            if (selectedBuild == null || weightPanel == null)
            {
                return;
            }

            int price = CalculateBuildMarketRoublePrice(selectedBuild.Equipment);
            weightPanel.SetParameterValue(new ValueStruct
            {
                Current = price,
                Maximum = 0f
            }, -1f, 0, false, true);
            weightPanel.SetWarningColor(false, false);
        }

        private static void ApplyBuildListPrice(EquipmentBuildListView view, GClass3749<GClass3953> buildWrapper)
        {
            ApplyBuildListPrice(view, buildWrapper?.Build);
        }

        private static void ApplyBuildListPrice(EquipmentBuildListView view, GClass3953 build)
        {
            HealthParameterPanel weightPanel = BuildListWeightField?.GetValue(view) as HealthParameterPanel;
            if (weightPanel == null || build == null)
            {
                return;
            }

            int price = CalculateBuildMarketRoublePrice(build.Equipment);
            weightPanel.SetParameterValue(new ValueStruct
            {
                Current = price,
                Maximum = 0f
            }, -1f, 0, false, true);
            weightPanel.SetWarningColor(false, false);
        }

        private static int CalculateBuildMarketRoublePrice(InventoryEquipment equipment)
        {
            if (equipment == null)
            {
                return 0;
            }

            double total = 0.0;
            foreach (Item item in CollectEquipmentBuildItems(equipment))
            {
                if (IsIgnoredKitRequirementItem(item))
                {
                    continue;
                }

                total += CalculateSingleItemMarketRoublePrice(item);
            }

            return Mathf.Max(0, Convert.ToInt32(Math.Floor(total)));
        }

        private static StashOnlyExclusionPlan CreateStashOnlyExclusionPlan(InventoryEquipment equipment, int fullKitPrice)
        {
            List<BuildItemRequirement> requirements = CreateBuildItemRequirements(equipment);
            Dictionary<string, int> stashCounts = CreateStashOnlyTemplateCounts();
            Dictionary<string, int> missingCounts = new Dictionary<string, int>(_selectedBuildMissingCounts, StringComparer.Ordinal);
            List<UsedStashItemSummary> usedItems = new List<UsedStashItemSummary>();
            List<UsedStashItemSummary> purchasedItems = new List<UsedStashItemSummary>();
            double usedValue = 0.0;

            foreach (BuildItemRequirement requirement in requirements)
            {
                if (requirement.RequiredCount <= 0)
                {
                    continue;
                }

                missingCounts.TryGetValue(requirement.TemplateId, out int missingCount);
                int stockAvailableCount = Mathf.Max(0, requirement.RequiredCount - Mathf.Min(requirement.RequiredCount, missingCount));
                stashCounts.TryGetValue(requirement.TemplateId, out int stashAvailableCount);
                int usedCount = Mathf.Min(stockAvailableCount, stashAvailableCount);
                if (usedCount > 0)
                {
                    stashCounts[requirement.TemplateId] = stashAvailableCount - usedCount;
                    usedValue += requirement.TotalPrice * ((double)usedCount / requirement.RequiredCount);
                    usedItems.Add(new UsedStashItemSummary
                    {
                        TemplateId = requirement.TemplateId,
                        DisplayName = requirement.DisplayName,
                        Count = usedCount
                    });
                }

                int remainingCount = requirement.RequiredCount - usedCount;
                if (remainingCount > 0)
                {
                    purchasedItems.Add(new UsedStashItemSummary
                    {
                        TemplateId = requirement.TemplateId,
                        DisplayName = requirement.DisplayName,
                        Count = remainingCount
                    });
                }
            }

            int finalPrice = Mathf.Max(0, fullKitPrice - Convert.ToInt32(Math.Floor(usedValue)));
            int requiredUnits = requirements.Sum(requirement => requirement.RequiredCount);
            int usedUnits = usedItems.Sum(item => item.Count);
            int purchasedUnits = purchasedItems.Sum(item => item.Count);
            pitFireTeam.Log.LogInfo($"[UI] Teammate kit stash-use quote scanned {requirements.Count} template(s), {requiredUnits} required item unit(s), {usedUnits} stash-matched unit(s), {purchasedUnits} purchase unit(s), finalPrice={finalPrice}.");
            return new StashOnlyExclusionPlan
            {
                FinalPrice = finalPrice,
                UsedItems = usedItems,
                PurchasedItems = purchasedItems,
                HasAllRequiredItems = requirements.Count > 0 && purchasedItems.Count == 0
            };
        }

        private static List<BuildItemRequirement> CreateBuildItemRequirements(InventoryEquipment equipment)
        {
            Dictionary<string, BuildItemRequirement> requirements = new Dictionary<string, BuildItemRequirement>(StringComparer.Ordinal);
            if (equipment == null)
            {
                return new List<BuildItemRequirement>();
            }

            foreach (Item item in CollectEquipmentBuildItems(equipment))
            {
                AddRequirement(requirements, item, Mathf.Max(1, item.StackObjectsCount), CalculateSingleItemMarketRoublePrice(item));
            }

            return requirements.Values.ToList();
        }

        private static void AddRequirement(Dictionary<string, BuildItemRequirement> requirements, Item item, int count, double totalPrice)
        {
            if (IsIgnoredKitRequirementItem(item) || string.IsNullOrWhiteSpace(item.TemplateId))
            {
                return;
            }

            if (!requirements.TryGetValue(item.TemplateId, out BuildItemRequirement requirement))
            {
                requirement = new BuildItemRequirement
                {
                    TemplateId = item.TemplateId,
                    DisplayName = GetItemDisplayName(item)
                };
                requirements[item.TemplateId] = requirement;
            }

            requirement.RequiredCount += count;
            requirement.TotalPrice += totalPrice;
        }

        private static bool IsIgnoredEquipmentBuildSlot(Slot slot)
        {
            if (slot == null)
            {
                return false;
            }

            if (string.Equals(slot.ID, EquipmentSlot.Dogtag.ToStringNoBox<EquipmentSlot>(), StringComparison.Ordinal))
            {
                return true;
            }

            return !pitFireTeam.IsFollowerLoadoutRealisticMode()
                && string.Equals(slot.ID, EquipmentSlot.SecuredContainer.ToStringNoBox<EquipmentSlot>(), StringComparison.Ordinal);
        }

        private static bool IsIgnoredKitRequirementItem(Item item)
        {
            // These are structural roots inside EFT's equipment-build tree. Their
            // children are real kit requirements, but the roots themselves are not
            // purchasable gear and should not appear in the teammate kit quote.
            return item == null
                || item is InventoryEquipment
                || item is PocketsItemClass;
        }

        private static Dictionary<string, int> CreateStashOnlyTemplateCounts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            Inventory inventory = _backendInventoryController?.Inventory;
            CompoundItem stash = inventory?.Stash;
            if (stash == null)
            {
                return counts;
            }

            foreach (Item item in CollectDeepItemTree(stash))
            {
                if (item == null
                    || ReferenceEquals(item, stash)
                    || IsIgnoredKitRequirementItem(item)
                    || string.IsNullOrWhiteSpace(item.TemplateId))
                {
                    continue;
                }

                int count = Mathf.Max(1, item.StackObjectsCount);
                if (counts.TryGetValue(item.TemplateId, out int existing))
                {
                    counts[item.TemplateId] = existing + count;
                }
                else
                {
                    counts[item.TemplateId] = count;
                }
            }

            return counts;
        }

        private static int CalculateItemsMarketRoublePrice(IEnumerable<Item> items)
        {
            if (items == null)
            {
                return 0;
            }

            double total = 0.0;
            HashSet<string> pricedItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in items)
            {
                if (item == null)
                {
                    continue;
                }

                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem)
                        || string.IsNullOrWhiteSpace(subItem.Id)
                        || !pricedItemIds.Add(subItem.Id))
                    {
                        continue;
                    }

                    total += CalculateSingleItemMarketRoublePrice(subItem);
                }
            }

            return Mathf.Max(0, Convert.ToInt32(Math.Floor(total)));
        }

        private static double CalculateItemTreeMarketRoublePrice(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            try
            {
                double total = 0.0;
                // Walk each sub-item explicitly instead of pricing the root tree with
                // CalculateBasePriceForAllItems. EFT's tree helper can return zero for
                // nested containers, which is correct for buyout validation but wrong
                // for our visible "what would this kit cost" estimate.
                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem))
                    {
                        continue;
                    }

                    total += CalculateSingleItemMarketRoublePrice(subItem);
                }

                return total;
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogWarning($"[UI] Failed to calculate market build item price for '{item.Id}': {ex.Message}");
                return 0.0;
            }
        }

        private static double CalculateSingleItemMarketRoublePrice(Item item)
        {
            if (item == null)
            {
                return 0.0;
            }

            double price = FleaTaxCalculatorAbstractClass.CalculateBuyoutBasePriceForSingleItem(
                item,
                0,
                BuildMarketPriceSource,
                true);

            return price > 0.0
                ? FleaTaxCalculatorAbstractClass.ApplyCustomPriceIfNeeded(item, price)
                : 0.0;
        }

        private static List<Item> CollectDeepItemTree(Item item)
        {
            List<Item> items = new List<Item>();
            if (item == null)
            {
                return items;
            }

            // Equipment builds are full trees: weapons carry mods, rigs carry
            // contents, and armor can carry plate inserts. Keep price, requirement,
            // and stash-match math on the same explicit recursive traversal so the
            // confirmation quote cannot silently ignore nested gear.
            item.GetAllItemsNonAlloc(items, false, true);
            return items;
        }

        private static List<Item> CollectEquipmentBuildItems(InventoryEquipment equipment)
        {
            List<Item> allItems = new List<Item>();
            if (equipment == null)
            {
                return allItems;
            }

            HashSet<string> ignoredItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Slot slot in equipment.Slots)
            {
                if (!IsIgnoredEquipmentBuildSlot(slot) || slot.ContainedItem == null)
                {
                    continue;
                }

                foreach (Item item in CollectDeepItemTree(slot.ContainedItem))
                {
                    if (!string.IsNullOrWhiteSpace(item?.Id))
                    {
                        ignoredItemIds.Add(item.Id);
                    }
                }
            }

            equipment.GetAllItemsFromCollectionNonAlloc(allItems);

            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            List<Item> result = new List<Item>();
            foreach (Item item in allItems)
            {
                if (item == null
                    || ReferenceEquals(item, equipment)
                    || string.IsNullOrWhiteSpace(item.Id)
                    || ignoredItemIds.Contains(item.Id)
                    || !seenItemIds.Add(item.Id))
                {
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private static string GetItemDisplayName(Item item)
        {
            if (item == null)
            {
                return GetSocialUiText("UnknownItem", "Unknown item");
            }

            string name = item.Name?.Localized(null);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            string shortName = item.ShortName?.Localized(null);
            return !string.IsNullOrWhiteSpace(shortName) ? shortName : item.TemplateId;
        }

        private static void RequestMarketPrices(ISession session, bool forceRefresh)
        {
            if (session == null || _marketPricesRequestInFlight)
            {
                return;
            }

            bool shouldRefresh = forceRefresh
                || _marketPrices == null
                || Time.realtimeSinceStartup - _marketPricesUpdatedAt > MarketPricesRefreshIntervalSeconds;
            if (!shouldRefresh)
            {
                return;
            }

            _marketPricesRequestInFlight = true;
            // This is the same market-facing price table Ragfair uses to update
            // handbook node prices. It is a better visible estimate than Prapor's
            // sell-to-trader value and is cheap enough to cache per screen session.
            session.RagfairGetPrices(new Callback<Dictionary<string, float>>(HandleMarketPricesReceived));
        }

        private static void HandleMarketPricesReceived(Result<Dictionary<string, float>> result)
        {
            _marketPricesRequestInFlight = false;
            if (!result.Succeed || result.Value == null)
            {
                pitFireTeam.Log.LogWarning("[UI] Failed to load Ragfair market prices for teammate build pricing.");
                return;
            }

            _marketPrices = new Dictionary<string, float>(result.Value);
            _marketPricesUpdatedAt = Time.realtimeSinceStartup;
            UpdateHandbookPrices(_marketPrices);
            RefreshActivePriceLabels();
        }

        private static void UpdateHandbookPrices(Dictionary<string, float> prices)
        {
            if (prices == null || !Singleton<HandbookClass>.Instantiated)
            {
                return;
            }

            HandbookClass handbook = Singleton<HandbookClass>.Instance;
            foreach (KeyValuePair<string, float> pair in prices)
            {
                EntityNodeClass node = handbook[pair.Key];
                if (node != null)
                {
                    node.Data.Price = pair.Value;
                }
            }
        }

        private static void RefreshActivePriceLabels()
        {
            if (!ShouldCustomizeBuildsScreen)
            {
                return;
            }

            if (_activeScreen != null)
            {
                ApplySelectedBuildPrice(_activeScreen);
            }

            foreach (BuildRowState state in BuildRowStates.Values)
            {
                if (state.View != null && state.Build != null)
                {
                    ApplyBuildListPrice(state.View, state.Build);
                }
            }
        }

        private static Dictionary<string, int> SummarizeMissingItems(IEnumerable<Item> missingItems)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in missingItems)
            {
                foreach (Item subItem in CollectDeepItemTree(item))
                {
                    if (IsIgnoredKitRequirementItem(subItem)
                        || string.IsNullOrWhiteSpace(subItem.TemplateId)
                        || !string.IsNullOrWhiteSpace(subItem.Id) && !seenItemIds.Add(subItem.Id))
                    {
                        continue;
                    }

                    int count = Mathf.Max(1, subItem.StackObjectsCount);
                    if (counts.TryGetValue(subItem.TemplateId, out int existing))
                    {
                        counts[subItem.TemplateId] = existing + count;
                    }
                    else
                    {
                        counts[subItem.TemplateId] = count;
                    }
                }
            }

            return counts;
        }

        private static void ApplyActionButtonText(Transform screen)
        {
            string label = GetSocialUiText("PurchaseKitAction", "Purchase");
            if (_excludeExistingItems && TryCreateBuyQuote(out EquipmentBuildBuyQuote quote) && quote.CanEquipFromStash)
            {
                label = GetSocialUiText("EquipKitAction", "EQUIP");
            }

            Transform buttonTransform = FindEquipButtonTransform(screen);
            DefaultUIButton button = buttonTransform?.GetComponent<DefaultUIButton>();
            if (button != null)
            {
                button.SetRawText(label, 18);
            }

            TMP_Text text = buttonTransform?.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private static void HideCanEquipIcon(Transform screen)
        {
            Transform canEquip = FindCanEquipTransform(screen);
            if (canEquip != null)
            {
                canEquip.gameObject.SetActive(false);
            }
        }

        private static Transform FindEquipButtonTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/ButtonsPanel/EquipButtonGroup/EquipButton")
                ?? FindChildRecursive(screen, "EquipButton");
        }

        private static Transform FindCanEquipTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/ButtonsPanel/EquipButtonGroup/CanEquip")
                ?? FindChildRecursive(screen, "CanEquip");
        }

        private static Transform FindCurrentWeightTransform(Transform screen)
        {
            return screen?.Find("Panels/Gear Panel/AdditionalInfoPanel/WeightPanel/CurrentWeight");
        }

        private static Dictionary<GameObject, bool> FindScreenChromeObjects(Transform screen)
        {
            Dictionary<GameObject, bool> objects = new Dictionary<GameObject, bool>();
            AddOriginalActive(objects, FindChildRecursive(screen, "BottomPanel"));
            AddOriginalActive(objects, FindChildRecursive(screen, "Bottom Panel"));
            AddOriginalActive(objects, FindChildRecursive(screen, "Bottom"));
            AddOriginalActive(objects, FindCurrentWeightTransform(screen));
            return objects;
        }

        private static void AddOriginalActive(Dictionary<GameObject, bool> states, Transform target)
        {
            if (target?.gameObject != null && !states.ContainsKey(target.gameObject))
            {
                states[target.gameObject] = target.gameObject.activeSelf;
            }
        }

        private static void ApplyRoublesIcon(Transform row)
        {
            Transform iconTransform = FindChildRecursive(row, "WeightIcon");
            Image image = iconTransform?.GetComponent<Image>();
            Sprite sprite = LoadRoublesSprite();
            if (image == null || sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.enabled = true;
            image.preserveAspect = true;
        }

        private static Sprite LoadRoublesSprite()
        {
            if (_roublesSprite != null)
            {
                return _roublesSprite;
            }

            string pluginDirectory = Path.GetDirectoryName(typeof(pitFireTeam).Assembly.Location) ?? string.Empty;
            string[] candidates =
            {
                Path.Combine(pluginDirectory, "icon_info_money_roubles_big.png"),
                Path.Combine(pluginDirectory, "resources", "icon_info_money_roubles_big.png"),
                Path.Combine(Directory.GetParent(pluginDirectory)?.FullName ?? pluginDirectory, "resources", "icon_info_money_roubles_big.png")
            };

            string iconPath = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    iconPath = candidates[i];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(iconPath))
            {
                pitFireTeam.Log.LogWarning("[UI] Teammate equipment builds roubles icon could not be found.");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                UnityEngine.Object.Destroy(texture);
                pitFireTeam.Log.LogWarning($"[UI] Failed to decode teammate equipment builds roubles icon '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_BuildListRoublesIcon";
            _roublesSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            _roublesSprite.name = "pitFireTeam_BuildListRoublesIcon";
            return _roublesSprite;
        }

        private static void HideChild(Transform root, string childName)
        {
            Transform child = FindChildRecursive(root, childName);
            if (child != null)
            {
                child.gameObject.SetActive(false);
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private sealed class ScreenChromeState
        {
            public EquipmentBuildsScreen Screen;
            public DefaultUIButton EquipButton;
            public string EquipButtonText;
            public int EquipButtonFontSize;
            public bool EquipButtonRawText;
            public TMP_Text EquipButtonLabel;
            public string EquipButtonLabelText;
            public Transform CanEquip;
            public bool? CanEquipActive;
            public Dictionary<GameObject, bool> ScreenObjects;
            public GameObject ExcludeExistingItemsToggleRoot;
        }

        private sealed class EquipmentBuildBuyQuote
        {
            public GClass3953 Build;
            public string BuildName;
            public int FullKitPrice;
            public int MissingOnlyPrice;
            public int FinalPrice;
            public bool ExcludeExistingItems;
            public bool CanEquipFromStash;
            public Dictionary<string, int> MissingTemplateCounts;
            public List<UsedStashItemSummary> UsedStashItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> PurchasedItems = new List<UsedStashItemSummary>();
        }

        private sealed class FriendlyTeammateBuyKitRequest
        {
            public string aid { get; set; }
            public FlatItemsDataClass[] items { get; set; }
            public int price { get; set; }
            public bool useItemsInStash { get; set; }
            public FriendlyTeammateBuyKitUsedItem[] usedItems { get; set; }
        }

        private sealed class FriendlyTeammateBuyKitResponse
        {
            public FlatItemsDataClass[] playerStashItems { get; set; }
        }

        private sealed class FriendlyTeammateBuyKitUsedItem
        {
            public string templateId { get; set; }
            public int count { get; set; }
        }

        private sealed class StashOnlyExclusionPlan
        {
            public int FinalPrice;
            public bool HasAllRequiredItems;
            public List<UsedStashItemSummary> UsedItems = new List<UsedStashItemSummary>();
            public List<UsedStashItemSummary> PurchasedItems = new List<UsedStashItemSummary>();

            public static StashOnlyExclusionPlan Empty(int fullKitPrice)
            {
                return new StashOnlyExclusionPlan
                {
                    FinalPrice = fullKitPrice
                };
            }
        }

        private sealed class BuildItemRequirement
        {
            public string TemplateId;
            public string DisplayName;
            public int RequiredCount;
            public double TotalPrice;
        }

        private sealed class UsedStashItemSummary
        {
            public string TemplateId;
            public string DisplayName;
            public int Count;
        }

        private sealed class BuildRowState
        {
            public EquipmentBuildListView View;
            public GClass3953 Build;
            public Transform DeleteHolder;
            public bool? DeleteHolderActive;
            public Image WeightIcon;
            public Sprite WeightIconSprite;
            public bool? WeightIconEnabled;
            public bool? WeightIconPreserveAspect;
        }

        private sealed class MarketPriceSource : IBasePriceSource
        {
            public double GetBasePrice(MongoID itemId)
            {
                // Prefer the live /client/items/prices table; handbook remains a safe
                // fallback for templates absent from the returned market map.
                if (_marketPrices != null && _marketPrices.TryGetValue(itemId, out float marketPrice) && marketPrice > 0f)
                {
                    return marketPrice;
                }

                if (Singleton<HandbookClass>.Instantiated)
                {
                    return Singleton<HandbookClass>.Instance.GetBasePrice(itemId);
                }

                return 0.0;
            }
        }
    }

    internal class TeammateEquipmentBuildsScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "Show", new[] { typeof(EquipmentBuildsScreen.GClass3870) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyScreenChrome(__instance);
        }
    }

    internal class TeammateEquipmentBuildsScreenVisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_12");
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance, ref LastPlayerStateClass __result)
        {
            if (TeammateEquipmentBuildsScreenFlow.TryBuildTeammateVisual(__instance, __result, out LastPlayerStateClass teammateVisual))
            {
                __result = teammateVisual;
            }
        }
    }

    internal class TeammateEquipmentBuildsScreenSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_9");
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildsScreen __instance)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyScreenChrome(__instance);
        }
    }

    internal class TeammateEquipmentBuildsMissingItemsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_10");
        }

        [PatchPrefix]
        private static void PatchPrefix(ref IEnumerable<Item> notFoundItems)
        {
            TeammateEquipmentBuildsScreenFlow.CaptureAndSuppressMissingItems(ref notFoundItems);
        }
    }

    internal class TeammateEquipmentBuildsScreenBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_22");
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile();
        }
    }

    internal class TeammateEquipmentBuildsScreenAltBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_23");
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile();
        }
    }

    internal class TeammateEquipmentBuildsScreenEscapePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "TranslateCommand");
        }

        [PatchPrefix]
        private static bool PatchPrefix(ECommand command, ref InputNode.ETranslateResult __result)
        {
            if (!command.IsCommand(ECommand.Escape) || !TeammateEquipmentBuildsScreenFlow.HandleBackToProfile())
            {
                return true;
            }

            __result = InputNode.ETranslateResult.BlockAll;
            return false;
        }
    }

    internal class TeammateEquipmentBuildsScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            TeammateEquipmentBuildsScreenFlow.HandleScreenClosed();
        }
    }

    internal class TeammateEquipmentBuildsScreenBuyButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentBuildsScreen), "method_15");
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TeammateEquipmentBuildsScreenFlow.HandleBuyRequest();
        }
    }

    internal class TeammateEquipmentBuildsSearchContextPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsAbstractClass), nameof(ContextInteractionsAbstractClass.IsActive));
        }

        [PatchPostfix]
        private static void PatchPostfix(EItemInfoButton button, ref bool __result)
        {
            if (TeammateEquipmentBuildsScreenFlow.ShouldSuppressSearchInteraction(button))
            {
                __result = false;
            }
        }
    }

    internal class TeammateEquipmentBuildsInspectButtonsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemSpecificationPanel), "method_4");
        }

        [PatchPostfix]
        private static void PatchPostfix(ItemSpecificationPanel __instance)
        {
            TeammateEquipmentBuildsScreenFlow.HideInspectInteractionButtons(__instance);
        }
    }

    internal class TeammateEquipmentBuildsListViewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(EquipmentBuildListView),
                "Show",
                new[] { typeof(GClass3749<GClass3953>), typeof(ValueTuple<float, float, float>?) });
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentBuildListView __instance, GClass3749<GClass3953> buildWrapper)
        {
            TeammateEquipmentBuildsScreenFlow.ApplyBuildListRow(__instance, buildWrapper);
        }
    }
}
