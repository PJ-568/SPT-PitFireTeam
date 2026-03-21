using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using friendlySAIN.Utils;
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
using UnityEngine.EventSystems;
using UnityEngine.Events;
using dropDownItem = GClass3682;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace friendlySAIN.Patches
{
    internal class FriendlyProfileDropdownItem : dropDownItem
    {
    }

    internal class FriendlyTeammateBodyResponse<T>
    {
        public int err { get; set; }
        public string errmsg { get; set; }
        public T data { get; set; }
    }

    internal class FriendlyTeammateLoadoutOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class FriendlyTeammateProfileOptions
    {
        public string CurrentLoadoutId { get; set; }
        public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; }
    }

    internal class FriendlyTeammateSuitRequest
    {
        public string aid { get; set; }
        public string[] suit { get; set; }
    }

    internal class FriendlyTeammateLoadoutRequest
    {
        public string aid { get; set; }
        public string loadoutId { get; set; }
    }

    internal class FriendlyTeammateRenameRequest
    {
        public string aid { get; set; }
        public string nickname { get; set; }
    }

    internal class FriendlyDropdownNamePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(GClass3672), "NameLocalizationKey");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3672 __instance, ref string __result)
        {
            if (OtherPlayerProfileScreenPatch.CustomDropdownIds.Contains(__instance.Id))
            {
                __result = __instance.Name;
                return false;
            }

            return true;
        }
    }

    internal class OtherPlayerProfileScreenPatch : ModulePatch
    {
        private const string OptionsRoute = "/singleplayer/friendlysain/teammate/profile/options";
        private const string SuitRoute = "/singleplayer/friendlysain/teammate/profile/suit";
        private const string RenameRoute = "/singleplayer/friendlysain/teammate/profile/rename";
        private const string LoadoutRoute = "/singleplayer/friendlysain/teammate/profile/loadout";

        private static readonly FieldInfo PlayerModelWindowField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
        private static readonly FieldInfo ClothingPanelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_clothingPanel");
        private static readonly FieldInfo NicknameLabelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_nicknameLabel");
        private static readonly FieldInfo NicknameFieldInputField = AccessTools.Field(typeof(NicknameField), "_inputField");
        private static readonly FieldInfo NicknameFieldStatusLabelField = AccessTools.Field(typeof(NicknameField), "_statusLabel");
        private static readonly FieldInfo NicknameFieldUsedSymbolsField = AccessTools.Field(typeof(NicknameField), "_usedSymbolsCount");
        private static readonly FieldInfo BackButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_backButton");
        private static readonly FieldInfo HideoutButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_hideoutButton");
        private static readonly FieldInfo ReportPanelField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_reportPanel");
        private static readonly FieldInfo OverallStatsPanelField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_overallStatsPanel");
        private static readonly FieldInfo AchievementsProgressBlockField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_achievementsProgressBlock");
        private static readonly FieldInfo AchievementsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_achievementsBlockPlaceholder");
        private static readonly FieldInfo WeaponsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_weaponsBlockPlaceholder");
        private static readonly FieldInfo NonWeaponItemsBlockPlaceholderField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_nonWeaponItemsBlockPlaceholder");
        private static readonly FieldInfo WeaponsGridLayoutGroupField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_weaponsGridLayoutGroup");
        private static readonly FieldInfo NonWeaponItemsGridLayoutGroupField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_nonWeaponItemsGridLayoutGroup");
        private static readonly FieldInfo UiField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "UI");
        private static readonly FieldInfo UpperDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_upperButtonDropDown");
        private static readonly FieldInfo LowerDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_lowerButtonDropDown");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private static readonly string GearIconPath = Path.Combine(PluginDirectory, "gear.png");

        public static ResultProfile ViewedProfile { get; set; }
        public static Transform LoadoutSelector { get; set; }
        public static CustomTextMeshProUGUI OriginalNicknameLabel { get; set; }
        public static DefaultUIButton NicknameRenameButton { get; set; }
        public static GameObject RenameOverlayRoot { get; set; }
        public static NicknameField RenameOverlayField { get; set; }
        public static Vector2? OriginalNicknameAnchoredPosition { get; set; }
        public static List<MongoID> CustomDropdownIds { get; } = new List<MongoID>();
        private static List<GameObject> HiddenRenameButtonDecorations { get; } = new List<GameObject>();
        private static Dictionary<GameObject, bool> HiddenRightSideRoots { get; } = new Dictionary<GameObject, bool>();
        internal static Action PendingBackOverrideAction { get; set; }
        internal static Action ActiveBackOverrideAction { get; set; }

        internal static void PrepareReturnOverride(Action callback)
        {
            PendingBackOverrideAction = callback;
        }

        internal static void ClearPendingReturnOverride()
        {
            PendingBackOverrideAction = null;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(OtherPlayerProfileScreen),
                "Show",
                new Type[] { typeof(ResultProfile), typeof(InventoryController), typeof(EItemViewType), typeof(ISession) });
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance, ResultProfile profile, InventoryController inventoryController, EItemViewType viewType, ISession session)
        {
            ConfigureBackOverride(__instance);

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                PlayerModelWindowField?.GetValue(__instance) as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow == null)
            {
                return;
            }

            RestoreProfileRightSideContent(__instance);

            if (session?.Profile != null
                && session.Profile.AccountId == profile.AccountId)
            {
                RestoreHideoutButtonVisuals(__instance);
                return;
            }

            RestoreHideoutButtonVisuals(__instance);

            FriendlyTeammateProfileOptions options = TryLoadProfileOptions(profile.AccountId);
            if (options == null || options.Loadouts == null || options.Loadouts.Count == 0)
            {
                friendlySAIN.Log.LogWarning($"[UI] Teammate profile patch aborted: no profile options for accountId '{profile.AccountId}'.");
                return;
            }

            if (!TryGetClothingPanel(__instance, playerModelWindow, out RectTransform clothingPanel, out InventoryClothingSelectionPanel clothingSelectionPanel, out Transform parent))
            {
                friendlySAIN.Log.LogWarning("[UI] Teammate profile patch aborted: clothing panel not found on profile screen.");
                return;
            }

            friendlySAIN.Log.LogInfo($"[UI] Applying teammate profile customization UI for '{profile.AccountId}'.");
            ViewedProfile = profile;
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            playerModelWindow.OnCustomizationChanged += PlayerModelWithStatsWindow_OnCustomizationChanged;

            HideProfileActions(__instance);
            ClearProfileRightSideContent(__instance);
            ConfigureNicknameRenameUi(__instance, playerModelWindow, profile);

            clothingPanel.gameObject.SetActive(true);
            DisplayClothingOptions(profile.PlayerVisualRepresentation, playerModelWindow, inventoryController, clothingSelectionPanel);

            AddViewListClass ui = UiField?.GetValue(__instance) as AddViewListClass;
            if (ui == null)
            {
                return;
            }

            if (LoadoutSelector != null)
            {
                GameObject.Destroy(LoadoutSelector.gameObject);
                LoadoutSelector = null;
            }

            RectTransform clone = GameObject.Instantiate(clothingPanel, parent, true);
            clone.name = "friendlySAIN_LoadoutSelector";
            clone.anchoredPosition = clothingPanel.anchoredPosition + new Vector2(0f, -72f);

            InventoryClothingSelectionPanel loadoutPanel = clone.GetComponent<InventoryClothingSelectionPanel>();
            if (loadoutPanel == null)
            {
                GameObject.Destroy(clone.gameObject);
                return;
            }

            ui.AddDisposable(loadoutPanel);
            LoadoutSelector = clone;
            ConfigureLoadoutPanel(loadoutPanel, clothingSelectionPanel);
            DisplayLoadoutOptions(profile, inventoryController, session, loadoutPanel, playerModelWindow, options);
            ApplyLoadoutPanelLayout(loadoutPanel, clothingSelectionPanel);
        }

        private static void ConfigureBackOverride(OtherPlayerProfileScreen screen)
        {
            if (screen == null)
            {
                PendingBackOverrideAction = null;
                ActiveBackOverrideAction = null;
                return;
            }

            Action callback = PendingBackOverrideAction;
            PendingBackOverrideAction = null;
            ActiveBackOverrideAction = callback;

            if (callback == null)
            {
                return;
            }
        }

        private static bool TryGetClothingPanel(
            OtherPlayerProfileScreen screen,
            InventoryPlayerModelWithStatsWindow playerModelWindow,
            out RectTransform clothingPanel,
            out InventoryClothingSelectionPanel clothingSelectionPanel,
            out Transform parent)
        {
            clothingPanel = null;
            clothingSelectionPanel = null;
            parent = null;

            clothingSelectionPanel = ClothingPanelField?.GetValue(playerModelWindow) as InventoryClothingSelectionPanel;
            if (clothingSelectionPanel == null)
            {
                Transform playerModelTransform = playerModelWindow.transform;
                Transform hierarchyPanel = playerModelTransform.Find("ClothingPanel")
                    ?? playerModelTransform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen.transform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen.transform.Find("ClothingPanel");

                if (hierarchyPanel == null)
                {
                    hierarchyPanel = FindChildRecursive(playerModelTransform, "ClothingPanel")
                        ?? FindChildRecursive(screen.transform, "ClothingPanel");
                }

                clothingSelectionPanel = hierarchyPanel?.GetComponent<InventoryClothingSelectionPanel>();
            }

            clothingPanel = clothingSelectionPanel?.transform as RectTransform;
            parent = clothingPanel?.parent;
            return clothingPanel != null && clothingSelectionPanel != null && parent != null;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
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

        public static void PlayerModelWithStatsWindow_OnCustomizationChanged(dropDownItem suit)
        {
            if (ViewedProfile == null)
            {
                return;
            }

            try
            {
                string body = ViewedProfile.Customization[EBodyModelPart.Body];
                string feet = ViewedProfile.Customization[EBodyModelPart.Feet];
                RequestHandler.PostJson(SuitRoute, SerializeBody(new FriendlyTeammateSuitRequest
                {
                    aid = ViewedProfile.AccountId,
                    suit = new string[] { body, feet }
                }));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate suit change.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void ConfigureNicknameRenameUi(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow window, ResultProfile profile)
        {
            if (NicknameRenameButton != null && NicknameRenameButton.name == "friendlySAIN_NicknameRenameButton")
            {
                GameObject.Destroy(NicknameRenameButton.gameObject);
                NicknameRenameButton = null;
            }

            OriginalNicknameLabel = NicknameLabelField?.GetValue(window) as CustomTextMeshProUGUI;
            if (OriginalNicknameLabel == null)
            {
                return;
            }

            RectTransform sourceRect = OriginalNicknameLabel.transform as RectTransform;
            if (sourceRect == null)
            {
                return;
            }

            if (OriginalNicknameAnchoredPosition == null)
            {
                OriginalNicknameAnchoredPosition = sourceRect.anchoredPosition;
            }

            sourceRect.anchoredPosition = OriginalNicknameAnchoredPosition.Value;

            DefaultUIButton hideoutButton = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (hideoutButton == null)
            {
                return;
            }

            hideoutButton.gameObject.SetActive(true);
            hideoutButton.SetRawText(GetSocialUiText("RenameChange", "EDIT NAME"), 18);
            HideHideoutButtonDecorations(hideoutButton);
            hideoutButton.OnClick.RemoveAllListeners();
            hideoutButton.OnClick.AddListener(() => ShowRenameOverlay(screen, profile));
            NicknameRenameButton = hideoutButton;
        }

        private static void HideHideoutButtonDecorations(DefaultUIButton button)
        {
            HiddenRenameButtonDecorations.Clear();

            foreach (Image image in button.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.gameObject == button.gameObject)
                {
                    continue;
                }

                RectTransform rect = image.transform as RectTransform;
                if (rect == null)
                {
                    continue;
                }

                bool looksLikeSmallIcon = rect.rect.width <= 40f && rect.rect.height <= 40f;
                if (!looksLikeSmallIcon)
                {
                    continue;
                }

                HiddenRenameButtonDecorations.Add(image.gameObject);
                image.gameObject.SetActive(false);
            }
        }

        internal static void RestoreHideoutButtonVisuals(OtherPlayerProfileScreen screen)
        {
            foreach (GameObject decoration in HiddenRenameButtonDecorations)
            {
                if (decoration != null)
                {
                    decoration.SetActive(true);
                }
            }

            HiddenRenameButtonDecorations.Clear();
        }

        internal static void RestoreProfileRightSideContent(OtherPlayerProfileScreen screen)
        {
            foreach (KeyValuePair<GameObject, bool> pair in HiddenRightSideRoots)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }

            HiddenRightSideRoots.Clear();
        }

        private static void ShowRenameOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseRenameOverlay();

            if (profile == null || OriginalNicknameLabel == null)
            {
                return;
            }

            DefaultUIButton buttonTemplate = BackButtonField?.GetValue(screen) as DefaultUIButton;
            NicknameField nicknameTemplate = Resources.FindObjectsOfTypeAll<NicknameField>()
                .FirstOrDefault(field =>
                    field != null &&
                    field.gameObject != null &&
                    field.gameObject.scene.IsValid() &&
                    NicknameFieldInputField?.GetValue(field) is TMP_InputField);
            if (buttonTemplate == null || nicknameTemplate == null)
            {
                friendlySAIN.Log.LogWarning("[UI] Teammate rename overlay aborted: template button or NicknameField not found.");
                return;
            }

            GameObject overlayRoot = new GameObject("friendlySAIN_RenameOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screen.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.08f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("friendlySAIN_RenamePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(620f, 188f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.98f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("friendlySAIN_RenameHeader", typeof(RectTransform), typeof(Image));
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

            GameObject titleObject = new GameObject("friendlySAIN_RenameTitle", typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            titleObject.transform.SetParent(header.transform, false);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.offsetMin = new Vector2(16f, 0f);
            titleRect.offsetMax = new Vector2(-42f, 0f);

            CustomTextMeshProUGUI title = titleObject.GetComponent<CustomTextMeshProUGUI>();
            title.text = "\u270E " + GetSocialUiText("RenameTeammateTitle", "Rename teammate").ToUpperInvariant();
            title.fontSize = 18f;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.color = new Color(0.87f, 0.87f, 0.84f, 1f);

            Button closeButton = CreateWindowCloseButton(header.transform);
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-4f, 0f);
            }

            closeButton.onClick.AddListener(new UnityAction(CloseRenameOverlay));

            GameObject fieldRoot = GameObject.Instantiate(nicknameTemplate.gameObject, panel.transform, false);
            fieldRoot.name = "friendlySAIN_RenameNicknameField";
            RectTransform fieldRect = fieldRoot.transform as RectTransform;
            if (fieldRect != null)
            {
                fieldRect.anchorMin = new Vector2(0f, 1f);
                fieldRect.anchorMax = new Vector2(1f, 1f);
                fieldRect.pivot = new Vector2(0.5f, 1f);
                fieldRect.offsetMin = new Vector2(26f, -118f);
                fieldRect.offsetMax = new Vector2(-26f, -54f);
                fieldRect.localScale = Vector3.one;
            }

            NicknameField nicknameField = fieldRoot.GetComponent<NicknameField>();
            TMP_InputField inputField = NicknameFieldInputField?.GetValue(nicknameField) as TMP_InputField;
            if (nicknameField == null || inputField == null)
            {
                GameObject.Destroy(overlayRoot);
                return;
            }

            string currentNickname = OriginalNicknameLabel.text?.Trim() ?? string.Empty;
            nicknameField.Init(string.Empty);
            inputField.SetTextWithoutNotify(currentNickname);
            inputField.text = currentNickname;
            inputField.interactable = true;
            nicknameField.method_3(currentNickname);
            inputField.textViewport.offsetMin = Vector2.zero;
            inputField.textViewport.offsetMax = Vector2.zero;
            AddTeammateNicknameFieldUi.SetStatusLabelText(
                nicknameField,
                GetSocialUiText("EnterNickname", "Enter player nickname"));

            TMP_Text stockStatusLabel = NicknameFieldStatusLabelField?.GetValue(nicknameField) as TMP_Text;
            if (stockStatusLabel != null)
            {
                stockStatusLabel.gameObject.SetActive(true);
                stockStatusLabel.text = string.Empty;
                stockStatusLabel.color = new Color(0.88f, 0.39f, 0.35f, 1f);
            }

            LocalizedText usedSymbolsLabel = NicknameFieldUsedSymbolsField?.GetValue(nicknameField) as LocalizedText;
            if (usedSymbolsLabel != null)
            {
                usedSymbolsLabel.gameObject.SetActive(false);
            }

            if (inputField.transform is RectTransform inputRect)
            {
                inputRect.offsetMin = Vector2.zero;
                inputRect.offsetMax = Vector2.zero;
            }

            inputField.pointSize = 28f;

            DefaultUIButton saveButton = CreateOverlayButton(buttonTemplate, panel.transform, new Vector2(0f, 12f), new Vector2(180f, 36f));
            saveButton.SetRawText(GetSocialUiText("RenameSave", "Save"), 22);
            saveButton.OnClick.RemoveAllListeners();
            saveButton.OnClick.AddListener(() => TryPersistNickname(inputField.text, profile, nicknameField, stockStatusLabel));
            if (saveButton.transform is RectTransform saveRect)
            {
                saveRect.anchorMin = new Vector2(0.5f, 0f);
                saveRect.anchorMax = new Vector2(0.5f, 0f);
                saveRect.pivot = new Vector2(0.5f, 0f);
                saveRect.anchoredPosition = new Vector2(0f, 10f);
                saveRect.localScale = Vector3.one * 0.9f;
            }

            RenameOverlayRoot = overlayRoot;
            RenameOverlayField = nicknameField;

            inputField.ActivateInputField();
            inputField.Select();
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(inputField.gameObject);
            }
        }

        private static DefaultUIButton CreateOverlayButton(DefaultUIButton template, Transform parent, Vector2 anchoredPosition, Vector2 size)
        {
            DefaultUIButton button = GameObject.Instantiate(template, parent, false);
            button.name = $"friendlySAIN_{template.name}";
            RectTransform rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one;
            }

            button.gameObject.SetActive(true);
            button.Interactable = true;
            return button;
        }

        private static Button CreateWindowCloseButton(Transform parent)
        {
            GameObject buttonObject = new GameObject("friendlySAIN_RenameCloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(28f, 22f);
            rect.localScale = Vector3.one;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.43f, 0.12f, 0.12f, 1f);
            background.raycastTarget = true;

            GameObject labelObject = new GameObject("friendlySAIN_RenameCloseLabel", typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            CustomTextMeshProUGUI label = labelObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = GetSocialUiText("RenameClose", "x");
            label.fontSize = 16f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.95f, 0.95f, 1f);

            return buttonObject.GetComponent<Button>();
        }

        private static void TryPersistNickname(string value, ResultProfile profile, NicknameField nicknameField, TMP_Text statusLabel)
        {
            if (ViewedProfile == null || profile == null || nicknameField == null)
            {
                return;
            }

            TMP_InputField inputField = NicknameFieldInputField?.GetValue(nicknameField) as TMP_InputField;
            string normalized = value?.Trim() ?? string.Empty;
            string current = OriginalNicknameLabel?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                AddTeammateNicknameFieldUi.SetStatusLabelText(
                    nicknameField,
                    GetSocialUiText("NicknameTooShort", "Nickname too short"));
                SetRenameStatus(statusLabel, GetSocialUiText("NicknameTooShort", "Nickname too short"), true);
                inputField?.ActivateInputField();
                return;
            }

            ENicknameError validationError = nicknameField.method_5(normalized);
            if (validationError != ENicknameError.ValidNickname)
            {
                nicknameField.method_6(validationError, false);
                string message = validationError == ENicknameError.TooShort
                    ? GetSocialUiText("NicknameTooShort", "Nickname too short")
                    : validationError.Localized(EStringCase.None);
                if (validationError == ENicknameError.TooShort)
                {
                    AddTeammateNicknameFieldUi.SetStatusLabelText(nicknameField, message);
                }
                SetRenameStatus(statusLabel, message, true);
                inputField?.ActivateInputField();
                return;
            }

            AddTeammateNicknameFieldUi.SetStatusLabelText(
                nicknameField,
                GetSocialUiText("EnterNickname", "Enter player nickname"));

            if (string.Equals(normalized, current, StringComparison.Ordinal))
            {
                CloseRenameOverlay();
                return;
            }

            try
            {
                string responseJson = RequestHandler.PostJson(RenameRoute, SerializeBody(new FriendlyTeammateRenameRequest
                {
                    aid = profile.AccountId,
                    nickname = normalized
                }));
                EnsureBodySuccess(responseJson);

                if (OriginalNicknameLabel != null)
                {
                    OriginalNicknameLabel.text = normalized;
                }

                SocialNetworkClassPatch.RefreshFriendsList();
                CloseRenameOverlay();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate nickname change.");
                Modules.Logger.LogError(ex);

                if (inputField != null)
                {
                    inputField.SetTextWithoutNotify(current);
                    inputField.ActivateInputField();
                }

                SetRenameStatus(statusLabel, GetSocialUiText("RenameFailed", "Could not rename teammate"), true);
            }
        }

        private static void SetRenameStatus(TMP_Text statusLabel, string message, bool isError)
        {
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = message;
            statusLabel.color = isError
                ? new Color(0.88f, 0.39f, 0.35f, 1f)
                : new Color(0.62f, 0.62f, 0.6f, 1f);
        }

        internal static void CloseRenameOverlay()
        {
            if (RenameOverlayRoot != null)
            {
                GameObject.Destroy(RenameOverlayRoot);
                RenameOverlayRoot = null;
            }

            RenameOverlayField = null;
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

        private static void EnsureBodySuccess(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return;
            }

            FriendlyTeammateBodyResponse<object> body = null;
            try
            {
                body = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<object>>(responseJson);
            }
            catch
            {
                return;
            }

            if (body != null && body.err != 0)
            {
                throw new InvalidOperationException(body.errmsg ?? "Unknown teammate backend error");
            }
        }

        private static FriendlyTeammateProfileOptions TryLoadProfileOptions(string accountId)
        {
            try
            {
                string responseJson = RequestHandler.PostJson(OptionsRoute, SerializeBody(new { aid = accountId }));
                FriendlyTeammateBodyResponse<FriendlyTeammateProfileOptions> body =
                    JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<FriendlyTeammateProfileOptions>>(responseJson);

                if (body?.data != null)
                {
                    return body.data;
                }

                return JsonConvert.DeserializeObject<FriendlyTeammateProfileOptions>(responseJson);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to load teammate profile options for '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
                return null;
            }
        }

        private static string SerializeBody(object body)
        {
            return body.ToJson(GetDefaultJsonConverters());
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            Type converterClass = typeof(AbstractGame).Assembly
                .GetTypes()
                .First(type => type.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            return Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
        }

        private static void HideProfileActions(OtherPlayerProfileScreen screen)
        {
            ReportPanel reportPanel = ReportPanelField?.GetValue(screen) as ReportPanel;
            if (reportPanel != null)
            {
                reportPanel.Close();
                reportPanel.gameObject.SetActive(false);
            }
        }

        private static void ClearProfileRightSideContent(OtherPlayerProfileScreen screen)
        {
            HideProfileRightSideRoot(screen, OverallStatsPanelField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, AchievementsProgressBlockField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, AchievementsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, WeaponsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, NonWeaponItemsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, WeaponsGridLayoutGroupField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, NonWeaponItemsGridLayoutGroupField?.GetValue(screen) as Component);
        }

        private static void HideProfileRightSideRoot(OtherPlayerProfileScreen screen, Component component)
        {
            HideProfileRightSideRoot(screen, component?.gameObject);
        }

        private static void HideProfileRightSideRoot(OtherPlayerProfileScreen screen, GameObject target)
        {
            if (screen == null || target == null)
            {
                return;
            }

            GameObject root = ResolveProfileSectionRoot(screen.transform, target.transform);
            if (root == null || HiddenRightSideRoots.ContainsKey(root))
            {
                return;
            }

            HiddenRightSideRoots[root] = root.activeSelf;
            root.SetActive(false);
        }

        private static GameObject ResolveProfileSectionRoot(Transform screenRoot, Transform target)
        {
            if (screenRoot == null || target == null)
            {
                return null;
            }

            Transform current = target;
            while (current.parent != null && current.parent != screenRoot)
            {
                current = current.parent;
            }

            return current == screenRoot ? null : current.gameObject;
        }

        private static void ConfigureLoadoutPanel(InventoryClothingSelectionPanel panel, InventoryClothingSelectionPanel sourcePanel)
        {
            DropDownBox upperDropdown = UpperDropdownField?.GetValue(panel) as DropDownBox;
            DropDownBox lowerDropdown = LowerDropdownField?.GetValue(panel) as DropDownBox;

            if (upperDropdown != null && lowerDropdown != null)
            {
                ReplaceDropdownIcon(panel.transform, "Upper/Icon", GearIconPath);
                HideDropdownIcon(panel.transform, "Lower/Icon");

                lowerDropdown.gameObject.SetActive(false);
            }
        }

        private static void ApplyLoadoutPanelLayout(InventoryClothingSelectionPanel panel, InventoryClothingSelectionPanel sourcePanel)
        {
            DropDownBox upperDropdown = UpperDropdownField?.GetValue(panel) as DropDownBox;
            DropDownBox sourceUpperDropdown = UpperDropdownField?.GetValue(sourcePanel) as DropDownBox;
            if (upperDropdown?.transform is not RectTransform upperRect || sourceUpperDropdown?.transform is not RectTransform sourceUpperRect)
            {
                return;
            }

            upperRect.anchorMin = sourceUpperRect.anchorMin;
            upperRect.anchorMax = sourceUpperRect.anchorMax;
            upperRect.pivot = sourceUpperRect.pivot;
            upperRect.anchoredPosition = sourceUpperRect.anchoredPosition;
            upperRect.sizeDelta = sourceUpperRect.sizeDelta;
            upperRect.offsetMin = sourceUpperRect.offsetMin;
            upperRect.offsetMax = sourceUpperRect.offsetMax;
            upperRect.localScale = sourceUpperRect.localScale;
        }

        private static void ReplaceDropdownIcon(Transform parent, string childPath, string filePath)
        {
            Transform iconTransform = parent.Find(childPath);
            if (iconTransform == null)
            {
                return;
            }

            Image image = iconTransform.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                image.enabled = false;
                return;
            }

            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                image.enabled = false;
                UnityEngine.Object.Destroy(texture);
                return;
            }

            image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            image.enabled = true;
            image.rectTransform.sizeDelta = new Vector2(25f, 30f);
        }

        private static void HideDropdownIcon(Transform parent, string childPath)
        {
            Transform iconTransform = parent.Find(childPath);
            if (iconTransform == null)
            {
                return;
            }

            Image image = iconTransform.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
            }
        }

        private static void DisplayClothingOptions(
            LastPlayerStateClass playerVisualRepresentation,
            InventoryPlayerModelWithStatsWindow window,
            InventoryController inventoryController,
            InventoryClothingSelectionPanel panel
        )
        {
            InventoryPlayerModelWithStatsWindow.Class3160 state = new InventoryPlayerModelWithStatsWindow.Class3160
            {
                playerVisualRepresentation = playerVisualRepresentation,
                inventoryPlayerModelWithStatsWindow_0 = window,
                inventoryController = inventoryController
            };

            IEnumerable<dropDownItem> availableSuites =
                Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Bear)
                .Concat(Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Usec))
                .Concat(Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Savage));

            List<dropDownItem> upper = new List<dropDownItem>();
            List<dropDownItem> lower = new List<dropDownItem>();
            MongoID selectedBody = state.playerVisualRepresentation.Customization[EBodyModelPart.Body];
            MongoID selectedFeet = state.playerVisualRepresentation.Customization[EBodyModelPart.Feet];
            dropDownItem currentUpper = null;
            dropDownItem currentLower = null;

            foreach (dropDownItem suite in availableSuites)
            {
                if (suite is GClass3683 upperSuite)
                {
                    upper.Add(upperSuite);
                }
                else if (suite is GClass3684 lowerSuite)
                {
                    lower.Add(lowerSuite);
                }

                if (selectedBody == suite.MainBodyPartItem)
                {
                    currentUpper = suite;
                }
                else if (selectedFeet == suite.MainBodyPartItem)
                {
                    currentLower = suite;
                }
            }

            panel.Show(upper, currentUpper, lower, currentLower, false, state.method_0);
        }

        private static void DisplayLoadoutOptions(
            ResultProfile profile,
            InventoryController inventoryController,
            ISession session,
            InventoryClothingSelectionPanel panel,
            InventoryPlayerModelWithStatsWindow window,
            FriendlyTeammateProfileOptions options
        )
        {
            CustomDropdownIds.Clear();

            List<dropDownItem> loadoutItems = [];
            dropDownItem currentLoadout = null;
            foreach (FriendlyTeammateLoadoutOption option in options.Loadouts)
            {
                FriendlyProfileDropdownItem item = new FriendlyProfileDropdownItem
                {
                    Id = option.Id,
                    Name = option.Name
                };

                CustomDropdownIds.Add(item.Id);
                loadoutItems.Add(item);

                if (string.Equals(option.Id, options.CurrentLoadoutId, StringComparison.OrdinalIgnoreCase))
                {
                    currentLoadout = item;
                }
            }

            currentLoadout ??= loadoutItems.FirstOrDefault();
            if (currentLoadout == null)
            {
                return;
            }

            panel.Show(loadoutItems, currentLoadout, new List<dropDownItem> { currentLoadout }, currentLoadout, false, selected =>
            {
                try
                {
                    RequestHandler.PostJson(LoadoutRoute, SerializeBody(new FriendlyTeammateLoadoutRequest
                    {
                        aid = profile.AccountId,
                        loadoutId = selected.Id
                    }));

                    RefreshPlayerVisualization(profile, inventoryController, session, window);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("[UI] Failed to persist teammate loadout change.");
                    Modules.Logger.LogError(ex);
                }
            });
        }

        private static void RefreshPlayerVisualization(ResultProfile profile, InventoryController inventoryController, ISession session, InventoryPlayerModelWithStatsWindow window)
        {
            try
            {
                Task.Run(async () =>
                {
                    Result<OtherProfileResult> result = await session.GetOtherPlayerProfile(profile.AccountId);
                    return result;
                }).ContinueWith(task =>
                {
                    Result<OtherProfileResult> result = task.Result;
                    if (result.Failed)
                    {
                        Modules.Logger.LogError(result.Error);
                        return;
                    }

                    profile.PlayerVisualRepresentation.Info.Nickname = result.Value.Info?.Nickname ?? profile.PlayerVisualRepresentation.Info.Nickname;
                    profile.PlayerVisualRepresentation.Info.Side = result.Value.Info?.Side ?? profile.PlayerVisualRepresentation.Info.Side;
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Head] = result.Value.Customization[EBodyModelPart.Head];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Body] = result.Value.Customization[EBodyModelPart.Body];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Feet] = result.Value.Customization[EBodyModelPart.Feet];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Hands] = result.Value.Customization[EBodyModelPart.Hands];

                    FieldInfo equipmentField = profile.PlayerVisualRepresentation.GetType()
                        .GetField("Equipment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    equipmentField?.SetValue(profile.PlayerVisualRepresentation, result.Value.Equipment.ToEquipment());

                    FieldInfo playerModelInfo = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_playerModelView");
                    PlayerModelView playerModelView = playerModelInfo?.GetValue(window) as PlayerModelView;
                    if (playerModelView?.gameObject.activeSelf == true)
                    {
                        playerModelView.Close();
                    }

                    window.method_3(profile.PlayerVisualRepresentation, inventoryController);
                }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal class OtherPlayerProfileScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(OtherPlayerProfileScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance)
        {
            Action callback = OtherPlayerProfileScreenPatch.ActiveBackOverrideAction;
            OtherPlayerProfileScreenPatch.ActiveBackOverrideAction = null;
            OtherPlayerProfileScreenPatch.PendingBackOverrideAction = null;

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow")?.GetValue(__instance)
                as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow != null)
            {
                playerModelWindow.OnCustomizationChanged -= OtherPlayerProfileScreenPatch.PlayerModelWithStatsWindow_OnCustomizationChanged;
            }

            OtherPlayerProfileScreenPatch.ViewedProfile = null;
            OtherPlayerProfileScreenPatch.CustomDropdownIds.Clear();
            OtherPlayerProfileScreenPatch.CloseRenameOverlay();

            if (OtherPlayerProfileScreenPatch.OriginalNicknameLabel != null)
            {
                if (OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition != null
                    && OtherPlayerProfileScreenPatch.OriginalNicknameLabel.transform is RectTransform labelRect)
                {
                    labelRect.anchoredPosition = OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition.Value;
                }

                OtherPlayerProfileScreenPatch.OriginalNicknameLabel = null;
                OtherPlayerProfileScreenPatch.OriginalNicknameAnchoredPosition = null;
            }

            if (OtherPlayerProfileScreenPatch.NicknameRenameButton != null)
            {
                if (OtherPlayerProfileScreenPatch.NicknameRenameButton.name == "friendlySAIN_NicknameRenameButton")
                {
                    GameObject.Destroy(OtherPlayerProfileScreenPatch.NicknameRenameButton.gameObject);
                }

                OtherPlayerProfileScreenPatch.NicknameRenameButton = null;
            }

            OtherPlayerProfileScreenPatch.RestoreHideoutButtonVisuals(__instance);
            OtherPlayerProfileScreenPatch.RestoreProfileRightSideContent(__instance);

            if (OtherPlayerProfileScreenPatch.LoadoutSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.LoadoutSelector.gameObject);
                OtherPlayerProfileScreenPatch.LoadoutSelector = null;
            }

            if (callback == null)
            {
                return;
            }

            if (friendlySAIN.Instance == null)
            {
                callback();
                return;
            }

            friendlySAIN.Instance.StartCoroutine(InvokeBackOverrideNextFrame(callback));
        }

        private static System.Collections.IEnumerator InvokeBackOverrideNextFrame(Action callback)
        {
            yield return null;
            callback?.Invoke();
        }
    }
}
