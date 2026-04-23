using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.Builds;
using EFT.Communications;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace friendlySAIN.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void ShowLoadoutEditorOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseLoadoutEditorOverlay();

            if (screen == null || profile == null || ActiveProfileSession?.Profile?.Inventory?.Stash == null)
            {
                return;
            }

            LoadoutEditorSourceLoadoutId = ActiveTeammateLoadoutId;
            LoadoutEditorSourceLoadoutName = ActiveTeammateLoadoutName;

            DefaultUIButton buttonTemplate = BackButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                friendlySAIN.Log.LogWarning("[UI] Loadout editor overlay aborted: template button not found.");
                return;
            }

            GameObject overlayRoot = new GameObject("friendlySAIN_LoadoutEditorOverlay", typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(screen.transform, false);
            RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.localScale = Vector3.one;
            overlayRect.SetAsLastSibling();

            Image backdrop = overlayRoot.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.2f);
            backdrop.raycastTarget = true;

            GameObject panel = new GameObject("friendlySAIN_LoadoutEditorPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlayRoot.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = new Vector2(235f, 100f);
            panelRect.offsetMax = new Vector2(-235f, -100f);
            panelRect.localScale = Vector3.one;

            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.985f);
            panelImage.raycastTarget = true;

            GameObject header = new GameObject("friendlySAIN_LoadoutEditorHeader", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(panel.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -36f);
            headerRect.offsetMax = Vector2.zero;

            Image headerImage = header.GetComponent<Image>();
            headerImage.color = new Color(0.06f, 0.06f, 0.06f, 1f);
            headerImage.raycastTarget = true;

            LoadoutEditorHeaderDragHandle dragHandle = header.AddComponent<LoadoutEditorHeaderDragHandle>();
            dragHandle.Target = panelRect;

            CreateOverlayText(
                "friendlySAIN_LoadoutEditorTitle",
                header.transform,
                new Vector2(18f, 0f),
                new Vector2(-54f, 0f),
                TextAlignmentOptions.MidlineLeft,
                GetSocialUiText("EditLoadoutTitle", "Edit Loadout").ToUpperInvariant(),
                20f,
                new Color(0.87f, 0.87f, 0.84f, 1f));

            Button closeButton = CreateWindowCloseButton(header.transform);
            if (closeButton.transform is RectTransform closeRect)
            {
                closeRect.anchorMin = new Vector2(1f, 0.5f);
                closeRect.anchorMax = new Vector2(1f, 0.5f);
                closeRect.pivot = new Vector2(1f, 0.5f);
                closeRect.anchoredPosition = new Vector2(-6f, 0f);
            }

            closeButton.onClick.AddListener(new UnityAction(CloseLoadoutEditorOverlay));

            CreateOverlayText(
                "friendlySAIN_LoadoutEditorSubtitle",
                panel.transform,
                new Vector2(28f, -62f),
                new Vector2(-28f, -98f),
                TextAlignmentOptions.MidlineLeft,
                string.Format(
                    GetSocialUiText("EditLoadoutSubtitle", "Edit cloned items for {0}. Changes here do not touch the real stash yet."),
                    profile.Info?.Nickname ?? "teammate"),
                17f,
                new Color(0.67f, 0.67f, 0.64f, 1f));

            RectTransform leftSection = CreateLoadoutEditorSection(
                panel.transform,
                "friendlySAIN_PlayerStashSection",
                GetSocialUiText("PlayerStash", "Player Stash"),
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(16f, 64f),
                new Vector2(-2f, -104f));

            RectTransform rightSection = CreateLoadoutEditorSection(
                panel.transform,
                "friendlySAIN_BotInventorySection",
                GetSocialUiText("BotInventory", "Follower Inventory"),
                new Vector2(0.5f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 64f),
                new Vector2(-10f, -104f));

            TryBuildLoadoutEditorPanels(profile, leftSection, rightSection);

            DefaultUIButton cancelButton = CreateOverlayButton(buttonTemplate, panel.transform, Vector2.zero, new Vector2(180f, 36f));
            cancelButton.name = "friendlySAIN_LoadoutEditorCancelButton";
            cancelButton.SetRawText(GetSocialUiText("Cancel", "Cancel"), 20);
            cancelButton.OnClick.RemoveAllListeners();
            cancelButton.OnClick.AddListener(CloseLoadoutEditorOverlay);
            if (cancelButton.transform is RectTransform cancelRect)
            {
                cancelRect.anchorMin = new Vector2(1f, 0f);
                cancelRect.anchorMax = new Vector2(1f, 0f);
                cancelRect.pivot = new Vector2(1f, 0f);
                cancelRect.anchoredPosition = new Vector2(-212f, 18f);
                cancelRect.localScale = Vector3.one * 0.9f;
            }

            DefaultUIButton doneButton = CreateOverlayButton(buttonTemplate, panel.transform, Vector2.zero, new Vector2(180f, 36f));
            doneButton.name = "friendlySAIN_LoadoutEditorDoneButton";
            doneButton.SetRawText(GetSocialUiText("Done", "Done"), 20);
            doneButton.OnClick.RemoveAllListeners();
            doneButton.OnClick.AddListener(async () =>
            {
                try
                {
                    await SaveLoadoutEditorPresetAsync(profile);
                }
                catch (Exception ex)
                {
                    friendlySAIN.Log.LogError("[UI] Failed to commit teammate loadout editor changes.");
                    friendlySAIN.Log.LogError(ex);
                }
            });
            if (doneButton.transform is RectTransform doneRect)
            {
                doneRect.anchorMin = new Vector2(1f, 0f);
                doneRect.anchorMax = new Vector2(1f, 0f);
                doneRect.pivot = new Vector2(1f, 0f);
                doneRect.anchoredPosition = new Vector2(-24f, 18f);
                doneRect.localScale = Vector3.one * 0.9f;
            }

            LoadoutEditorOverlayRoot = overlayRoot;
        }

        private static RectTransform CreateLoadoutEditorSection(
            Transform parent,
            string name,
            string title,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            GameObject section = new GameObject(name, typeof(RectTransform), typeof(Image));
            section.transform.SetParent(parent, false);
            RectTransform sectionRect = section.GetComponent<RectTransform>();
            sectionRect.anchorMin = anchorMin;
            sectionRect.anchorMax = anchorMax;
            sectionRect.offsetMin = offsetMin;
            sectionRect.offsetMax = offsetMax;
            sectionRect.localScale = Vector3.one;

            Image sectionImage = section.GetComponent<Image>();
            sectionImage.color = new Color(0.09f, 0.09f, 0.09f, 1f);
            sectionImage.raycastTarget = true;

            GameObject contentRoot = new GameObject($"{name}_Content", typeof(RectTransform));
            contentRoot.transform.SetParent(section.transform, false);
            RectTransform contentRect = contentRoot.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = new Vector2(8f, 10f);
            contentRect.offsetMax = new Vector2(-8f, -10f);
            contentRect.localScale = Vector3.one;
            return contentRect;
        }

        private static void CreateLoadoutEditorFallbackText(Transform parent, string name, string body)
        {
            CreateOverlayText(
                name,
                parent,
                new Vector2(12f, 12f),
                new Vector2(-12f, -12f),
                TextAlignmentOptions.Center,
                body,
                19f,
                new Color(0.58f, 0.58f, 0.56f, 1f));
        }

        private static void TryBuildLoadoutEditorPanels(ResultProfile profile, RectTransform leftSection, RectTransform rightSection)
        {
            if (profile?.Equipment == null
                || ActiveProfileSession?.Profile?.Inventory?.Stash == null
                || ActiveProfileInventoryController == null)
            {
                const string missingReason = "missing profile equipment, stash, or inventory controller";
                friendlySAIN.Log.LogWarning("[UI] Loadout editor inventory build aborted: missing profile equipment, stash, or inventory controller.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "friendlySAIN_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder", "Failed to load cloned stash view.\n{0}"), missingReason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "friendlySAIN_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder", "Failed to load cloned follower inventory.\n{0}"), missingReason));
                return;
            }

            ItemUiContext itemUiContext = ItemUiContext.Instance;
            if (itemUiContext == null)
            {
                const string reason = "ItemUiContext.Instance is null";
                friendlySAIN.Log.LogWarning("[UI] Loadout editor inventory build aborted: ItemUiContext.Instance is null.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "friendlySAIN_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder", "Failed to load cloned stash view.\n{0}"), reason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "friendlySAIN_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder", "Failed to load cloned follower inventory.\n{0}"), reason));
                return;
            }

            if (!TryCreateLoadoutEditorProfile(profile, out Profile editorProfile, out InventoryController editorInventoryController))
            {
                const string reason = "failed to create local editor inventory";
                friendlySAIN.Log.LogWarning("[UI] Loadout editor inventory build aborted: failed to create local editor inventory.");
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "friendlySAIN_PlayerStashFallback",
                    string.Format(GetSocialUiText("PlayerStashPlaceholder", "Failed to load cloned stash view.\n{0}"), reason));
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "friendlySAIN_BotInventoryFallback",
                    string.Format(GetSocialUiText("BotInventoryPlaceholder", "Failed to load cloned follower inventory.\n{0}"), reason));
                return;
            }

            LoadoutEditorProfile = editorProfile;
            LoadoutEditorInventoryController = editorInventoryController;

            itemUiContext.Configure(
                editorInventoryController,
                editorProfile,
                ActiveProfileSession,
                ActiveProfileSession?.InsuranceCompany,
                null,
                null,
                new CompoundItem[] { editorProfile.Inventory.Stash },
                EItemUiContextType.TransferItemsScreen,
                ECursorResult.ShowCursor,
                null,
                editorProfile.Inventory.Equipment,
                null);

            TryBuildLoadoutEditorStashPanel(leftSection, editorProfile, editorInventoryController);
            TryBuildLoadoutEditorFollowerPanel(profile, rightSection, itemUiContext, editorProfile, editorInventoryController);
        }

        private static bool TryCreateLoadoutEditorProfile(ResultProfile profile, out Profile editorProfile, out InventoryController editorInventoryController)
        {
            editorProfile = null;
            editorInventoryController = null;

            try
            {
                Profile baseProfile = ActiveProfileSession?.Profile?.Clone();
                InventoryEquipment editorEquipment = ResolveLoadoutEditorSourceEquipment(profile);
                if (baseProfile?.Inventory == null || editorEquipment == null)
                {
                    return false;
                }

                EFTInventoryClass inventoryDescriptor = new EFTInventoryClass(baseProfile.Inventory, GClass2240.Instance)
                {
                    Equipment = EFTItemSerializerClass.SerializeItem(editorEquipment, null),
                    Stash = EFTItemSerializerClass.SerializeItem(baseProfile.Inventory.Stash.CloneItem(null), null)
                };

                baseProfile.Inventory = inventoryDescriptor.ToInventory();
                editorProfile = baseProfile;
                editorInventoryController = new InventoryController(editorProfile, false);
                return editorProfile.Inventory?.Equipment != null && editorProfile.Inventory.Stash != null;
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI] Failed to create local teammate loadout editor profile.");
                friendlySAIN.Log.LogError(ex);
                editorProfile = null;
                editorInventoryController = null;
                return false;
            }
        }

        private static InventoryEquipment ResolveLoadoutEditorSourceEquipment(ResultProfile profile)
        {
            InventoryEquipment sourceEquipment = TryGetCustomBuildById(LoadoutEditorSourceLoadoutId)?.Equipment ?? profile?.Equipment;
            InventoryEquipment clonedEquipment = sourceEquipment?.CloneItem(null) as InventoryEquipment;
            if (clonedEquipment == null)
            {
                return null;
            }

            SanitizeLoadoutEditorEquipment(clonedEquipment);
            return clonedEquipment;
        }

        private static void SanitizeLoadoutEditorEquipment(InventoryEquipment equipment)
        {
            if (equipment == null)
            {
                return;
            }

            RemoveLoadoutEditorSlotItem(equipment, EquipmentSlot.SecuredContainer);
        }

        private static void RemoveLoadoutEditorSlotItem(InventoryEquipment equipment, EquipmentSlot slot)
        {
            try
            {
                equipment.GetSlot(slot)?.RemoveItemWithoutRestrictions();
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to sanitize loadout editor slot '{slot}'.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private static void TryBuildLoadoutEditorStashPanel(
            RectTransform leftSection,
            Profile editorProfile,
            InventoryController editorInventoryController)
        {
            try
            {
                SimpleStashPanel stashTemplate = ResolveLoadoutEditorStashTemplate();
                StashItemClass fakeStash = editorProfile?.Inventory?.Stash;
                if (stashTemplate == null || fakeStash == null)
                {
                    throw new InvalidOperationException($"stashTemplate={(stashTemplate != null)}, fakeStash={(fakeStash != null)}");
                }

                ItemContextAbstractClass stashContext = new GClass3459(
                    fakeStash,
                    GClass3459.EItemType.Inventory,
                    editorInventoryController.Inventory.FavoriteItemsStorage,
                    false);

                SimpleStashPanel stashPanel = GameObject.Instantiate(stashTemplate, leftSection, false);
                stashPanel.name = "friendlySAIN_LoadoutEditorStashPanel";
                if (stashPanel.transform is RectTransform stashRect)
                {
                    StretchToFillParent(stashRect);
                }

                stashPanel.Show(
                    fakeStash,
                    editorInventoryController,
                    stashContext,
                    false,
                    null,
                    SimpleStashPanel.EStashSearchAvailability.Unavailable,
                    null,
                    ItemsPanel.EItemsTab.Gear);

                LoadoutEditorStashPanel = stashPanel;
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI] Failed to build cloned stash panel.");
                friendlySAIN.Log.LogError(ex);
                CreateLoadoutEditorFallbackText(
                    leftSection,
                    "friendlySAIN_PlayerStashFallback",
                    string.Format(
                        GetSocialUiText("PlayerStashPlaceholder", "Failed to load cloned stash view.\n{0}"),
                        ex.GetType().Name + ": " + ex.Message));
            }
        }

        private static void TryBuildLoadoutEditorFollowerPanel(
            ResultProfile profile,
            RectTransform rightSection,
            ItemUiContext itemUiContext,
            Profile editorProfile,
            InventoryController editorInventoryController)
        {
            try
            {
                ComplexStashPanel equipmentTemplate = ResolveLoadoutEditorEquipmentTemplate();
                InventoryEquipment equipmentView = editorProfile?.Inventory?.Equipment;
                if (equipmentTemplate == null || equipmentView == null || editorInventoryController == null)
                {
                    throw new InvalidOperationException($"equipmentTemplate={(equipmentTemplate != null)}, equipmentView={(equipmentView != null)}, followerController={(editorInventoryController != null)}");
                }

                ItemContextAbstractClass equipmentContext = new GClass3450(EItemViewType.InventoryDuringMatching);
                LoadoutEditorEquipmentContext = equipmentContext;

                ComplexStashPanel equipmentPanelRoot = GameObject.Instantiate(equipmentTemplate, rightSection, false);
                equipmentPanelRoot.name = "friendlySAIN_LoadoutEditorEquipmentPanel";
                if (equipmentPanelRoot.transform is RectTransform equipmentRect)
                {
                    StretchToFillParent(equipmentRect);
                }

                ShowLoadoutEditorEquipmentPanel(
                    equipmentPanelRoot,
                    equipmentContext,
                    equipmentView,
                    editorInventoryController,
                    profile.Info?.Nickname ?? "teammate",
                    profile.Skills ?? ActiveProfileSession.Profile.Skills,
                    ActiveProfileSession.InsuranceCompany,
                    itemUiContext);

                LoadoutEditorEquipmentPanel = equipmentPanelRoot;
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI] Failed to build cloned follower inventory.");
                friendlySAIN.Log.LogError(ex);
                CreateLoadoutEditorFallbackText(
                    rightSection,
                    "friendlySAIN_BotInventoryFallback",
                    string.Format(
                        GetSocialUiText("BotInventoryPlaceholder", "Failed to load cloned follower inventory.\n{0}"),
                        ex.GetType().Name + ": " + ex.Message));
            }
        }

        private static SimpleStashPanel ResolveLoadoutEditorStashTemplate()
        {
            SimpleStashPanel screenTemplate = TransferItemsScreenStashPanelField?.GetValue(CommonUI.Instance?.TransferItemsScreen) as SimpleStashPanel;
            if (screenTemplate != null)
            {
                return screenTemplate;
            }

            return Resources.FindObjectsOfTypeAll<SimpleStashPanel>()
                .FirstOrDefault(panel => panel != null && !panel.name.StartsWith("friendlySAIN_", StringComparison.Ordinal));
        }

        private static ComplexStashPanel ResolveLoadoutEditorEquipmentTemplate()
        {
            ItemsPanel itemsPanel = InventoryScreenItemsPanelField?.GetValue(CommonUI.Instance?.InventoryScreen) as ItemsPanel;
            ComplexStashPanel screenTemplate = ItemsPanelComplexStashPanelField?.GetValue(itemsPanel) as ComplexStashPanel;
            if (screenTemplate != null)
            {
                return screenTemplate;
            }

            return Resources.FindObjectsOfTypeAll<ComplexStashPanel>()
                .FirstOrDefault(panel => panel != null && !panel.name.StartsWith("friendlySAIN_", StringComparison.Ordinal));
        }

        private static void ShowLoadoutEditorEquipmentPanel(
            ComplexStashPanel panelRoot,
            ItemContextAbstractClass equipmentContext,
            InventoryEquipment equipmentView,
            InventoryController followerInventoryController,
            string followerName,
            SkillManager skills,
            InsuranceCompanyClass insurance,
            ItemUiContext itemUiContext)
        {
            if (panelRoot == null || equipmentView == null || followerInventoryController == null)
            {
                throw new InvalidOperationException("equipment panel root, equipment view, or follower inventory controller was null");
            }

            panelRoot.Show(
                followerInventoryController,
                equipmentContext,
                equipmentView,
                skills,
                insurance,
                itemUiContext);

            string headerTitle = !string.IsNullOrWhiteSpace(LoadoutEditorSourceLoadoutName)
                ? LoadoutEditorSourceLoadoutName
                : followerName;

            HideLoadoutEditorContainerSlot(panelRoot, EquipmentSlot.SecuredContainer);
            SetLoadoutEditorEquipmentHeader(panelRoot.transform, headerTitle);
            HideLoadoutEditorEquipmentHeaderIcon(panelRoot.transform);
            HideLoadoutEditorCharacterGearImage(panelRoot.transform);
        }

        private static void HideLoadoutEditorContainerSlot(ComplexStashPanel panelRoot, EquipmentSlot slot)
        {
            if (panelRoot == null)
            {
                return;
            }

            try
            {
                ContainersPanel containersPanel = ComplexStashPanelContainersPanelField?.GetValue(panelRoot) as ContainersPanel;
                if (containersPanel == null)
                {
                    return;
                }

                IDictionary<EquipmentSlot, SlotView> slotViews =
                    ContainersPanelDictionaryField?.GetValue(containersPanel) as IDictionary<EquipmentSlot, SlotView>;
                if (slotViews == null || !slotViews.TryGetValue(slot, out SlotView slotView) || slotView == null)
                {
                    return;
                }

                slotView.Close();
                GameObject.Destroy(slotView.gameObject);
                slotViews.Remove(slot);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to hide loadout editor container slot '{slot}'.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private static void SetLoadoutEditorEquipmentHeader(Transform panelRoot, string headerTitle)
        {
            if (panelRoot == null || string.IsNullOrWhiteSpace(headerTitle))
            {
                return;
            }

            Transform disabledTextTransform = panelRoot.Find("Header/Text/DisabledText");
            if (disabledTextTransform == null)
            {
                disabledTextTransform = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "DisabledText", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Text", StringComparison.Ordinal));
            }

            Transform headerTextTransform = panelRoot.Find("Header/Text");
            if (headerTextTransform == null)
            {
                headerTextTransform = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "Text", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Header", StringComparison.Ordinal));
            }

            if (headerTextTransform != null)
            {
                LocalizedText headerLocalizedText = headerTextTransform.GetComponent<LocalizedText>();
                if (headerLocalizedText != null)
                {
                    headerLocalizedText.enabled = false;
                }
            }

            if (disabledTextTransform != null)
            {
                LocalizedText disabledLocalizedText = disabledTextTransform.GetComponent<LocalizedText>();
                if (disabledLocalizedText != null)
                {
                    disabledLocalizedText.enabled = false;
                }

                TMP_Text disabledText = disabledTextTransform.GetComponent<TMP_Text>();
                if (disabledText != null)
                {
                    disabledText.text = headerTitle;
                    return;
                }
            }

            if (headerTextTransform != null)
            {
                TMP_Text headerText = headerTextTransform.GetComponent<TMP_Text>();
                if (headerText != null)
                {
                    headerText.text = headerTitle;
                }
            }
        }

        private static void HideLoadoutEditorEquipmentHeaderIcon(Transform panelRoot)
        {
            if (panelRoot == null)
            {
                return;
            }

            Transform headerIcon = panelRoot.Find("Header/Icon");
            if (headerIcon == null)
            {
                headerIcon = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null &&
                        string.Equals(transform.name, "Icon", StringComparison.Ordinal) &&
                        string.Equals(transform.parent?.name, "Header", StringComparison.Ordinal));
            }

            if (headerIcon != null)
            {
                headerIcon.gameObject.SetActive(false);
            }
        }

        private static void HideLoadoutEditorCharacterGearImage(Transform equipmentTabRoot)
        {
            if (equipmentTabRoot == null)
            {
                return;
            }

            Transform characterGear = equipmentTabRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform =>
                    transform != null &&
                    (string.Equals(transform.name, "CharacterGear", StringComparison.Ordinal) ||
                     string.Equals(transform.name, "ChracterGear", StringComparison.Ordinal)));

            if (characterGear == null)
            {
                return;
            }

            Image[] gearImages = characterGear.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < gearImages.Length; i++)
            {
                if (gearImages[i] != null)
                {
                    gearImages[i].enabled = false;
                }
            }
        }

        private static CustomTextMeshProUGUI CreateOverlayText(
            string name,
            Transform parent,
            Vector2 offsetMin,
            Vector2 offsetMax,
            TextAlignmentOptions alignment,
            string text,
            float fontSize,
            Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = offsetMin;
            textRect.offsetMax = offsetMax;
            textRect.localScale = Vector3.one;

            CustomTextMeshProUGUI label = textObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = color;
            label.enableWordWrapping = true;
            return label;
        }

        internal static void CloseLoadoutEditorOverlay()
        {
            if (LoadoutEditorEquipmentPanel != null)
            {
                LoadoutEditorEquipmentPanel.Close();
                LoadoutEditorEquipmentPanel.UnConfigure();
                LoadoutEditorEquipmentPanel = null;
            }

            if (LoadoutEditorStashPanel != null)
            {
                LoadoutEditorStashPanel.Close();
                LoadoutEditorStashPanel = null;
            }

            if (LoadoutEditorEquipmentContext != null && ItemUiContext.Instance != null)
            {
                ItemUiContext.Instance.UnregisterView(LoadoutEditorEquipmentContext);
                LoadoutEditorEquipmentContext = null;
            }

            if (LoadoutEditorOverlayRoot != null)
            {
                GameObject.Destroy(LoadoutEditorOverlayRoot);
                LoadoutEditorOverlayRoot = null;
            }

            LoadoutEditorProfile = null;
            LoadoutEditorInventoryController = null;
            LoadoutEditorSourceLoadoutId = null;
            LoadoutEditorSourceLoadoutName = null;

            RestoreProfileItemUiContext();
        }

        private static void RestoreProfileItemUiContext()
        {
            if (ActiveProfileInventoryController == null || ActiveProfileSession == null || ItemUiContext.Instance == null)
            {
                return;
            }

            try
            {
                Profile profileClone = ActiveProfileSession.Profile?.Clone();
                if (profileClone == null)
                {
                    return;
                }

                if (ViewedProfile?.Skills != null)
                {
                    profileClone.Skills = ViewedProfile.Skills;
                }

                ItemUiContext.Instance.Configure(
                    ActiveProfileInventoryController,
                    profileClone,
                    ActiveProfileSession,
                    null,
                    null,
                    null,
                    null,
                    EItemUiContextType.Hideout,
                    ECursorResult.ShowCursor,
                    null,
                    null,
                    null);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI] Failed to restore teammate profile ItemUiContext after closing loadout editor.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private static async Task SaveLoadoutEditorPresetAsync(ResultProfile profile)
        {
            if (profile == null
                || ActiveProfileSession?.EquipmentBuildsStorage == null
                || LoadoutEditorProfile?.Inventory?.Equipment == null
                || ItemUiContext.Instance == null)
            {
                return;
            }

            if (IsDefaultLoadoutEditorSelection())
            {
                await SaveLoadoutEditorDefaultEquipmentAsync(profile);
                return;
            }

            string initialName = string.IsNullOrWhiteSpace(LoadoutEditorSourceLoadoutName)
                ? ActiveProfileSession.EquipmentBuildsStorage.LastEquippedPresetName
                : LoadoutEditorSourceLoadoutName;

            GClass3838 nameDialog = ItemUiContext.Instance.ShowEditBuildNameWindow(
                initialName ?? string.Empty,
                "EquipmentBuild/SetNameWindowCaption".Localized(null),
                "EquipmentBuild/SetNameWindowPlaceholder".Localized(null));

            try
            {
                while (true)
                {
                    string enteredName;
                    try
                    {
                        enteredName = await nameDialog.AcceptResult;
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(enteredName))
                    {
                        NotificationManagerClass.DisplayWarningNotification("Name cannot be empty.", ENotificationDurationType.Default);
                        continue;
                    }

                    enteredName = enteredName.Trim();
                    EquipmentBuildsStorageClass buildsStorage = ActiveProfileSession.EquipmentBuildsStorage;
                    GClass3953 originalBuild = TryGetCustomBuildById(LoadoutEditorSourceLoadoutId);
                    GClass3953 existingBuildByName = buildsStorage.FindCustomBuildByName(enteredName);
                    MongoID targetBuildId;

                    if (existingBuildByName != null
                        && (originalBuild == null || existingBuildByName.Id != originalBuild.Id))
                    {
                        bool replaceExisting = await ShowReplaceBuildPromptAsync();
                        if (!replaceExisting)
                        {
                            continue;
                        }

                        targetBuildId = existingBuildByName.Id;
                    }
                    else if (originalBuild != null && string.Equals(originalBuild.Name, enteredName, StringComparison.Ordinal))
                    {
                        targetBuildId = originalBuild.Id;
                    }
                    else
                    {
                        targetBuildId = new MongoID(ActiveProfileSession.Profile);
                    }

                    GClass3953 editedBuild = new GClass3953(
                        targetBuildId,
                        enteredName,
                        CreateSanitizedLoadoutEditorSaveEquipment(),
                        EEquipmentBuildType.Custom);

                    var saveResult = await buildsStorage.SaveBuild(editedBuild);
                    if (saveResult.Failed)
                    {
                        NotificationManagerClass.DisplayWarningNotification(saveResult.Error ?? "Failed to save equipment preset.", ENotificationDurationType.Default);
                        continue;
                    }

                    await PersistTeammateLoadoutSelectionAsync(profile.AccountId, editedBuild.Id);
                    ActiveTeammateLoadoutId = editedBuild.Id;
                    ActiveTeammateLoadoutName = editedBuild.Name;
                    NotificationManagerClass.DisplayMessageNotification(
                        string.Format("EquipmentBuilds/PresetSaved".Localized(null), editedBuild.Name),
                        ENotificationDurationType.Default,
                        ENotificationIconType.Default,
                        null);

                    CloseLoadoutEditorOverlay();
                    RefreshCurrentTeammateLoadoutSelector(profile);
                    MarkSquadRosterDirty(profile.AccountId);
                    return;
                }
            }
            finally
            {
                nameDialog.Close();
            }
        }

        private static bool IsDefaultLoadoutEditorSelection()
        {
            return string.Equals(LoadoutEditorSourceLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase);
        }

        private static async Task SaveLoadoutEditorDefaultEquipmentAsync(ResultProfile profile)
        {
            FlatItemsDataClass[] serializedEquipment = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(
                new Item[] { CreateSanitizedLoadoutEditorSaveEquipment() });
            if (serializedEquipment == null || serializedEquipment.Length == 0)
            {
                throw new InvalidOperationException("Loadout editor default equipment was unavailable for save.");
            }

            string responseJson = await Task.Run(() => RequestHandler.PostJson(
                DefaultEquipmentRoute,
                SerializeBody(new FriendlyTeammateDefaultEquipmentRequest
                {
                    aid = profile.AccountId,
                    items = serializedEquipment
                })));

            EnsureBodySuccess(responseJson);
            ActiveTeammateLoadoutId = DefaultLoadoutId;
            ActiveTeammateLoadoutName = DefaultLoadoutName;

            CloseLoadoutEditorOverlay();
            RefreshCurrentTeammateLoadoutSelector(profile);
            MarkSquadRosterDirty(profile.AccountId);
        }

        private static InventoryEquipment CreateSanitizedLoadoutEditorSaveEquipment()
        {
            InventoryEquipment sanitizedEquipment = LoadoutEditorProfile?.Inventory?.Equipment?.CloneItem(null) as InventoryEquipment;
            if (sanitizedEquipment == null)
            {
                throw new InvalidOperationException("Loadout editor equipment was unavailable for save.");
            }

            SanitizeLoadoutEditorEquipment(sanitizedEquipment);
            return sanitizedEquipment;
        }

        private static GClass3953 TryGetCustomBuildById(string buildId)
        {
            if (string.IsNullOrWhiteSpace(buildId) || ActiveProfileSession?.EquipmentBuildsStorage?.EquipmentBuilds == null)
            {
                return null;
            }

            foreach (KeyValuePair<MongoID, GClass3953> entry in ActiveProfileSession.EquipmentBuildsStorage.EquipmentBuilds)
            {
                if (entry.Value?.BuildType != EEquipmentBuildType.Custom)
                {
                    continue;
                }

                if (string.Equals(entry.Key.ToString(), buildId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private sealed class FriendlyTeammateDefaultEquipmentRequest
        {
            public string aid { get; set; }
            public FlatItemsDataClass[] items { get; set; }
        }

        private static async Task<bool> ShowReplaceBuildPromptAsync()
        {
            if (ItemUiContext.Instance == null)
            {
                return false;
            }

            GClass3834 messageWindow;
            try
            {
                return await ItemUiContext.Instance.ShowMessageWindow(
                    out messageWindow,
                    "EquipmentBuild/ReplaceMessage".Localized(null),
                    null,
                    false);
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

        private static async Task PersistTeammateLoadoutSelectionAsync(string teammateAccountId, MongoID loadoutId)
        {
            string persistedLoadoutId = loadoutId.ToString();
            if (string.IsNullOrWhiteSpace(teammateAccountId) || string.IsNullOrWhiteSpace(persistedLoadoutId))
            {
                return;
            }

            string responseJson = await Task.Run(() => RequestHandler.PostJson(
                LoadoutRoute,
                SerializeBody(new FriendlyTeammateLoadoutRequest
                {
                    aid = teammateAccountId,
                    loadoutId = persistedLoadoutId
                })));

            EnsureBodySuccess(responseJson);
        }

        private static void RefreshCurrentTeammateLoadoutSelector(ResultProfile profile)
        {
            if (profile == null
                || LoadoutSelector == null
                || ActiveProfileInventoryController == null
                || ActiveProfileSession == null
                || ActiveProfilePlayerModelWindow == null)
            {
                return;
            }

            InventoryClothingSelectionPanel loadoutPanel = LoadoutSelector.GetComponent<InventoryClothingSelectionPanel>();
            FriendlyTeammateProfileOptions options = TryLoadProfileOptions(profile.AccountId);
            if (loadoutPanel == null || options == null)
            {
                return;
            }

            DisplayLoadoutOptions(
                profile,
                ActiveProfileInventoryController,
                ActiveProfileSession,
                loadoutPanel,
                ActiveProfilePlayerModelWindow,
                options);

            RefreshPlayerVisualization(
                profile,
                ActiveProfileInventoryController,
                ActiveProfileSession,
                ActiveProfilePlayerModelWindow);
        }
    }
}
