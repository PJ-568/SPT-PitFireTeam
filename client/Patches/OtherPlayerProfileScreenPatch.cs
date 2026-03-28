using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
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
        private sealed class ProfileSkillsHealthController : IHealthController
        {
            private readonly Profile.ProfileHealthClass _health;
            private readonly GClass2182 _snapshot;

            public ProfileSkillsHealthController(Profile.ProfileHealthClass health)
            {
                _health = health ?? throw new ArgumentNullException(nameof(health));
                _snapshot = new GClass2182(health);
            }

            public event Action<IEffect> EffectAddedEvent { add { } remove { } }
            public event Action<IEffect> EffectStartedEvent { add { } remove { } }
            public event Action<IEffect> EffectUpdatedEvent { add { } remove { } }
            public event Action<IEffect> EffectResidualEvent { add { } remove { } }
            public event Action<IEffect> EffectRemovedEvent { add { } remove { } }
            public event Action<IEffect> EffectStatusUpdateEvent { add { } remove { } }
            public event Action<EBodyPart, float, DamageInfoStruct> ApplyDamageEvent { add { } remove { } }
            public event Action<EBodyPart, float, DamageInfoStruct> HealthChangedEvent { add { } remove { } }
            public event Action<float> EnergyChangedEvent { add { } remove { } }
            public event Action<float> HydrationChangedEvent { add { } remove { } }
            public event Action<float> TemperatureChangedEvent { add { } remove { } }
            public event Action<EBodyPart, EDamageType> BodyPartDestroyedEvent { add { } remove { } }
            public event Action<EBodyPart, ValueStruct> BodyPartRestoredEvent { add { } remove { } }
            public event Action<EDamageType> DiedEvent { add { } remove { } }
            public event Action<IEffect> HealerDoneEvent { add { } remove { } }
            public event Action<Vector3, float, float> BurnEyesEvent { add { } remove { } }
            public event Action<IPlayerBuff> StimulatorBuffEvent { add { } remove { } }
            public event Action<IPlayerBuff> StimulatorBuffActivationEvent { add { } remove { } }

            public float FallSafeHeight { set { } }
            public bool IsAlive => GetBodyPartHealth(EBodyPart.Common).Current > 0f;
            public float HealthRate => 0f;
            public float EnergyRate => 0f;
            public float HydrationRate => 0f;
            public float TemperatureRate => 0f;
            public float DamageCoeff => 1f;
            public float StaminaCoeff => 1f;
            public int UpdateTime => _health.UpdateTime ?? 0;
            public HealthEffects BodyPartEffects => default;
            public float CarryingWeightAbsoluteModifier => 0f;
            public float CarryingWeightRelativeModifier => 0f;
            public ValueStruct Hydration => _snapshot.Hydration;
            public ValueStruct Energy => _snapshot.Energy;
            public ValueStruct Temperature => _snapshot.Temperature;
            public ValueStruct Poison => _snapshot.Poison;

            public bool IsBodyPartBroken(EBodyPart bodyPart) => false;
            public bool IsBodyPartDestroyed(EBodyPart bodyPart) => GetBodyPartHealth(bodyPart).Current <= 0f;
            public void GetBodyPartsInCriticalCondition(float threshold, out int all, out int vital)
            {
                all = 0;
                vital = 0;
            }

            public void SetEncumbered(bool encumbered) { }
            public void SetOverEncumbered(bool encumbered) { }
            public void AddFatigue() { }
            public void AddImmunityNotificationEffect() { }
            public TEffect FindExistingEffect<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => default;
            public TEffect FindActiveEffect<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => default;
            public IEnumerable<TEffect> FindActiveEffects<TEffect>(EBodyPart bodyPart = EBodyPart.Common) where TEffect : IEffect => Enumerable.Empty<TEffect>();
            public IEnumerable<IEffect> GetAllActiveEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public IEnumerable<IEffect> GetAllEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public IEnumerable<IEffect> GetAllResidualEffects(EBodyPart bodyPart = EBodyPart.Common) => Enumerable.Empty<IEffect>();
            public GStruct382<EBodyPart> BodyPartsPriority(Item item, bool continuousHealEnabled) => default;
            public bool IsItemForHealing(Item item) => false;
            public IResult HasPartsToApply(Item item) => null;
            public bool CanApplyItem(Item item, EBodyPart bodyPart) => false;
            public bool ApplyItem(Item item, GStruct382<EBodyPart> bodyPart, float? amount = null) => false;
            public bool ApplyItem(Item item, EBodyPart bodyPart, float? amount = null) => false;
            public void CancelApplyingItem() { }
            public void ManualUpdate(float deltaTime) { }
            public void PropagateAllEffects() { }
            public string[] ActiveBuffsNames() => Array.Empty<string>();
            public void DisableMetabolism() { }

            public ValueStruct GetBodyPartHealth(EBodyPart bodyPart, bool rounded = false)
            {
                if (bodyPart == EBodyPart.Common)
                {
                    return _snapshot.GetBodyPartHealth(bodyPart, rounded);
                }

                if (_health.BodyParts != null && _health.BodyParts.TryGetValue(bodyPart, out Profile.ProfileHealthClass.ProfileBodyPartHealthClass part) && part?.Health != null)
                {
                    return new ValueStruct
                    {
                        Current = part.Health.Current,
                        Maximum = part.Health.Maximum
                    };
                }

                return default;
            }
        }

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
        private static readonly FieldInfo SkillsScreenListTabField = AccessTools.Field(typeof(SkillsScreen), "_listTab");
        private static readonly FieldInfo SkillsScreenThumbsTabField = AccessTools.Field(typeof(SkillsScreen), "_thumbsTab");
        private static readonly FieldInfo SkillsScreenTabsControllerField = AccessTools.Field(typeof(SkillsScreen), "gclass3808_0");
        private static readonly MethodInfo SkillsScreenShowMethod = AccessTools.Method(typeof(SkillsScreen), "Show");
        private static readonly FieldInfo SkillsAndMasteringSkillsScreenField = AccessTools.Field(typeof(SkillsAndMasteringScreen), "_skillsScreen");
        private static readonly FieldInfo InventorySkillsAndMasteringScreenField = AccessTools.Field(typeof(InventoryScreen), "_skillsAndMasteringScreen");
        private static readonly FieldInfo SkillManagerSkillsField = AccessTools.Field(typeof(SkillManager), nameof(SkillManager.Skills));
        private static readonly FieldInfo SkillManagerDisplayListField = AccessTools.Field(typeof(SkillManager), nameof(SkillManager.DisplayList));
        private static readonly FieldInfo UiField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "UI");
        private static readonly FieldInfo UpperDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_upperButtonDropDown");
        private static readonly FieldInfo LowerDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_lowerButtonDropDown");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private static readonly string GearIconPath = Path.Combine(PluginDirectory, "gear.png");
        private static readonly string BrainIconPath = Path.Combine(PluginDirectory, "brain.png");
        private static readonly Vector2 SkillsScreenOffset = new Vector2(-38f, -100f);
        private static readonly HashSet<ESkillId> HiddenFollowerSkills = new HashSet<ESkillId>
        {
            ESkillId.Charisma,
            ESkillId.Attention,
            ESkillId.Intellect,
            ESkillId.Search,
            ESkillId.WeaponTreatment,
            ESkillId.Crafting,
            ESkillId.HideoutManagement
        };

        public static ResultProfile ViewedProfile { get; set; }
        public static Transform LoadoutSelector { get; set; }
        public static DefaultUIButton EditLoadoutButton { get; set; }
        public static Transform EditLoadoutButtonRoot { get; set; }
        public static GameObject LoadoutEditorOverlayRoot { get; set; }
        public static SkillsScreen SkillsPanel { get; set; }
        public static RectTransform SkillsPanelHost { get; set; }
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

        private static void MarkSquadRosterDirty()
        {
            Components.SquadControlMenuUi.RequestRosterRefreshOnNextInject();
        }

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
            if (options == null)
            {
                return;
            }

            if (options.Loadouts == null || options.Loadouts.Count == 0)
            {
                friendlySAIN.Log.LogWarning($"[UI] Teammate profile patch aborted: no loadout options returned for '{profile.AccountId}'.");
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
            try
            {
                ConfigureLoadoutPanel(loadoutPanel, clothingSelectionPanel);
                DisplayLoadoutOptions(profile, inventoryController, session, loadoutPanel, playerModelWindow, options);
                ApplyLoadoutPanelLayout(loadoutPanel, clothingSelectionPanel);
                CreateEditLoadoutButton(__instance, clone, parent, profile);
                DisplaySkillsPanel(__instance, profile, session);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to apply teammate profile elements.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void CreateEditLoadoutButton(OtherPlayerProfileScreen screen, RectTransform loadoutSelector, Transform parent, ResultProfile profile)
        {
            if (screen == null || loadoutSelector == null || parent == null || profile == null)
            {
                return;
            }

            if (EditLoadoutButton != null)
            {
                if (EditLoadoutButtonRoot != null)
                {
                    GameObject.Destroy(EditLoadoutButtonRoot.gameObject);
                    EditLoadoutButtonRoot = null;
                }

                EditLoadoutButton = null;
            }

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "friendlySAIN_LoadoutEdit";
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + new Vector2(0f, -72f);
            rowClone.gameObject.SetActive(true);

            Transform upperRoot = rowClone.Find("Upper");
            if (upperRoot != null)
            {
                upperRoot.gameObject.SetActive(false);
            }

            Transform lowerRoot = rowClone.Find("Lower");
            if (lowerRoot != null)
            {
                lowerRoot.gameObject.SetActive(false);
            }

            DefaultUIButton buttonTemplate = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (buttonTemplate == null)
            {
                GameObject.Destroy(rowClone.gameObject);
                friendlySAIN.Log.LogWarning("[UI] Edit Loadout button aborted: hideout button template not found.");
                return;
            }

            DefaultUIButton button = GameObject.Instantiate(buttonTemplate, rowClone, false);
            if (button == null)
            {
                GameObject.Destroy(rowClone.gameObject);
                friendlySAIN.Log.LogWarning("[UI] Edit Loadout button aborted: cloned hideout button not found.");
                return;
            }

            button.name = "friendlySAIN_EditLoadoutButton";
            button.gameObject.SetActive(true);
            button.Interactable = true;
            button.SetRawText(GetSocialUiText("EditLoadout", "Edit Loadout"), 18);
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => ShowLoadoutEditorOverlay(screen, profile));

            if (button.transform is RectTransform buttonRect && buttonTemplate.transform is RectTransform templateRect)
            {
                buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.sizeDelta = templateRect.sizeDelta;
                buttonRect.localScale = Vector3.one;
            }

            EditLoadoutButtonRoot = rowClone;
            EditLoadoutButton = button;
        }

        private static void ShowLoadoutEditorOverlay(OtherPlayerProfileScreen screen, ResultProfile profile)
        {
            CloseLoadoutEditorOverlay();

            if (screen == null || profile == null)
            {
                return;
            }

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
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -20f);
            panelRect.sizeDelta = new Vector2(1220f, 700f);
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
                    GetSocialUiText("EditLoadoutSubtitle", "Loadout editor shell for {0}. Inventory integration comes in the next phase."),
                    profile.Info?.Nickname ?? "teammate"),
                17f,
                new Color(0.67f, 0.67f, 0.64f, 1f));

            CreateLoadoutEditorSection(
                panel.transform,
                "friendlySAIN_PlayerStashPlaceholder",
                GetSocialUiText("PlayerStash", "Player Stash"),
                GetSocialUiText("PlayerStashPlaceholder", "Left pane placeholder.\nThis will host the player's cloned stash."),
                new Vector2(24f, 64f),
                new Vector2(-610f, -104f));

            CreateLoadoutEditorSection(
                panel.transform,
                "friendlySAIN_BotInventoryPlaceholder",
                GetSocialUiText("BotInventory", "Follower Inventory"),
                GetSocialUiText("BotInventoryPlaceholder", "Right pane placeholder.\nThis will host the follower's cloned inventory."),
                new Vector2(610f, 64f),
                new Vector2(-24f, -104f));

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
            doneButton.OnClick.AddListener(() =>
            {
                Modules.Logger.LogInfo($"[UI] Loadout editor shell confirmed for teammate '{profile.AccountId}'.");
                CloseLoadoutEditorOverlay();
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

        private static void CreateLoadoutEditorSection(Transform parent, string name, string title, string body, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject section = new GameObject(name, typeof(RectTransform), typeof(Image));
            section.transform.SetParent(parent, false);
            RectTransform sectionRect = section.GetComponent<RectTransform>();
            sectionRect.anchorMin = Vector2.zero;
            sectionRect.anchorMax = Vector2.one;
            sectionRect.offsetMin = offsetMin;
            sectionRect.offsetMax = offsetMax;
            sectionRect.localScale = Vector3.one;

            Image sectionImage = section.GetComponent<Image>();
            sectionImage.color = new Color(0.09f, 0.09f, 0.09f, 1f);
            sectionImage.raycastTarget = true;

            CreateOverlayText(
                $"{name}_Title",
                section.transform,
                new Vector2(18f, -8f),
                new Vector2(-18f, -44f),
                TextAlignmentOptions.MidlineLeft,
                title.ToUpperInvariant(),
                18f,
                new Color(0.84f, 0.84f, 0.81f, 1f));

            CreateOverlayText(
                $"{name}_Body",
                section.transform,
                new Vector2(22f, 18f),
                new Vector2(-22f, -54f),
                TextAlignmentOptions.Center,
                body,
                21f,
                new Color(0.58f, 0.58f, 0.56f, 1f));
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

        private static void DisplaySkillsPanel(OtherPlayerProfileScreen screen, ResultProfile profile, ISession session)
        {
            if (screen == null || profile?.Skills == null || session?.Profile == null)
            {
                friendlySAIN.Log.LogWarning("[UI] Skills panel skipped: missing screen, profile skills, or session profile.");
                return;
            }

            SkillsScreen template = FindSkillsScreenTemplate();
            if (template == null || !TryPrepareSkillsHost(screen, out RectTransform hostParent))
            {
                friendlySAIN.Log.LogWarning($"[UI] Skills panel skipped: template={(template != null)}.");
                return;
            }

            if (SkillsPanel != null)
            {
                GameObject.Destroy(SkillsPanel.gameObject);
                SkillsPanel = null;
            }

            if (SkillsPanelHost != null)
            {
                GameObject.Destroy(SkillsPanelHost.gameObject);
                SkillsPanelHost = null;
            }

            GameObject hostObject = new GameObject("friendlySAIN_ProfileSkillsHost", typeof(RectTransform));
            hostObject.transform.SetParent(hostParent, false);
            RectTransform hostRect = hostObject.GetComponent<RectTransform>();
            StretchToFillParent(hostRect);
            hostRect.SetAsLastSibling();

            SkillsScreen clone = GameObject.Instantiate(template, hostRect, false);
            clone.name = "friendlySAIN_ProfileSkillsScreen";
            if (clone.transform is RectTransform cloneRect)
            {
                ConfigureInjectedSkillsScreenRect(screen, cloneRect);
            }

            Profile skillsProfile = BuildSkillsProfile(session.Profile, profile.Skills);

            if (!TryInitializeSkillsScreen(clone))
            {
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }

            object healthController = ResolveSkillsHealthController(profile, session);
            if (healthController == null)
            {
                friendlySAIN.Log.LogWarning("[UI] Skills panel skipped: unable to resolve any health controller.");
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }

            try
            {
                SkillsScreenShowMethod?.Invoke(clone, new object[] { skillsProfile, healthController });
                HideDetailedSkillProgressChildren(clone.transform);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to show follower skills panel.");
                Modules.Logger.LogError(ex);
                GameObject.Destroy(hostObject);
                GameObject.Destroy(clone.gameObject);
                return;
            }


            AddViewListClass ui = UiField?.GetValue(screen) as AddViewListClass;
            ui?.AddDisposable(clone);

            SkillsPanelHost = hostRect;
            SkillsPanel = clone;
            friendlySAIN.Log.LogWarning($"[UI] Follower skills panel shown for '{profile.AccountId}'.");
        }

        private static Profile BuildSkillsProfile(Profile sessionProfile, SkillManager sourceSkills)
        {
            Profile skillsProfile = sessionProfile?.Clone();
            if (skillsProfile?.Skills == null || sourceSkills == null)
            {
                return skillsProfile;
            }

            skillsProfile.Skills.ApplyChanges(sourceSkills);
            FilterHiddenSkills(skillsProfile.Skills);
            return skillsProfile;
        }

        private static void FilterHiddenSkills(SkillManager skillManager)
        {
            if (skillManager == null)
            {
                return;
            }

            ReplaceSkillArray(SkillManagerDisplayListField, skillManager);
            ReplaceSkillArray(SkillManagerSkillsField, skillManager);
        }

        private static void ReplaceSkillArray(FieldInfo field, SkillManager skillManager)
        {
            if (field?.GetValue(skillManager) is not SkillClass[] skills)
            {
                return;
            }

            SkillClass[] filtered = skills
                .Where(skill => skill != null && !skill.Locked && !HiddenFollowerSkills.Contains(skill.Id))
                .ToArray();

            field.SetValue(skillManager, filtered);
        }

        private static void HideDetailedSkillProgressChildren(Transform root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == "Progress")
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static object ResolveSkillsHealthController(ResultProfile profile, ISession session)
        {
            try
            {
                if (session?.Profile?.Health != null)
                {
                    return new ProfileSkillsHealthController(session.Profile.Health);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to build session-profile health controller for skills panel.");
                Modules.Logger.LogError(ex);
            }

            try
            {
                return Singleton<GameWorld>.Instantiated
                    ? (object)Singleton<GameWorld>.Instance?.MainPlayer?.ActiveHealthController
                    : null;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to resolve live health controller fallback for skills panel.");
                Modules.Logger.LogError(ex);
                return null;
            }
        }

        private static bool TryInitializeSkillsScreen(SkillsScreen skillsScreen)
        {
            if (skillsScreen == null)
            {
                return false;
            }

            if (SkillsScreenTabsControllerField?.GetValue(skillsScreen) != null)
            {
                return true;
            }

            try
            {
                AccessTools.Method(typeof(SkillsScreen), "Awake")?.Invoke(skillsScreen, null);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to initialize stock SkillsScreen clone.");
                Modules.Logger.LogError(ex);
                return false;
            }

            return SkillsScreenTabsControllerField?.GetValue(skillsScreen) != null;
        }

        private static SkillsScreen FindSkillsScreenTemplate()
        {
            SkillsScreen direct = Resources.FindObjectsOfTypeAll<SkillsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    screen.name != "friendlySAIN_ProfileSkillsScreen" &&
                    screen.transform is RectTransform);
            if (direct != null)
            {
                return direct;
            }

            SkillsAndMasteringScreen skillsAndMastering = Resources.FindObjectsOfTypeAll<SkillsAndMasteringScreen>()
                .FirstOrDefault(screen => screen != null);
            SkillsScreen fromSkillsAndMastering = SkillsAndMasteringSkillsScreenField?.GetValue(skillsAndMastering) as SkillsScreen;
            if (fromSkillsAndMastering?.transform is RectTransform)
            {
                return fromSkillsAndMastering;
            }

            InventoryScreen inventoryScreen = Resources.FindObjectsOfTypeAll<InventoryScreen>()
                .FirstOrDefault(screen => screen != null);
            SkillsAndMasteringScreen inventorySkillsAndMastering = InventorySkillsAndMasteringScreenField?.GetValue(inventoryScreen) as SkillsAndMasteringScreen;
            SkillsScreen fromInventory = SkillsAndMasteringSkillsScreenField?.GetValue(inventorySkillsAndMastering) as SkillsScreen;
            if (fromInventory?.transform is RectTransform)
            {
                return fromInventory;
            }

            friendlySAIN.Log.LogWarning("[UI] Unable to locate a stock SkillsScreen template.");
            return null;
        }

        private static bool TryPrepareSkillsHost(OtherPlayerProfileScreen screen, out RectTransform hostParent)
        {
            hostParent = null;
            if (screen == null)
            {
                return false;
            }

            Transform rightSide = screen.transform.Find("RightSide")
                ?? FindChildRecursive(screen.transform, "RightSide");

            rightSide?.gameObject.SetActive(true);
            hostParent = rightSide as RectTransform;
            return hostParent != null;
        }

        private static void EnsureSkillsScreenOptionsVisible(SkillsScreen skillsScreen)
        {
            if (skillsScreen == null)
            {
                return;
            }

            Transform options = skillsScreen.transform.Find("Options")
                ?? FindChildRecursive(skillsScreen.transform, "Options");
            if (options == null)
            {
                return;
            }

            options.gameObject.SetActive(true);
            if (options is RectTransform optionsRect)
            {
                optionsRect.anchorMin = new Vector2(0f, 1f);
                optionsRect.anchorMax = new Vector2(1f, 1f);
                optionsRect.pivot = new Vector2(0.5f, 1f);
                optionsRect.localScale = Vector3.one;
            }
        }

        private static RectTransform GetSkillsAnchorRect(OtherPlayerProfileScreen screen)
        {
            GameObject[] targets =
            [
                ResolveProfileSectionRoot(screen.transform, (OverallStatsPanelField?.GetValue(screen) as Component)?.transform),
                ResolveProfileSectionRoot(screen.transform, (AchievementsProgressBlockField?.GetValue(screen) as Component)?.transform),
                ResolveProfileSectionRoot(screen.transform, (WeaponsGridLayoutGroupField?.GetValue(screen) as Component)?.transform),
            ];

            foreach (GameObject target in targets)
            {
                if (target?.transform is RectTransform rect)
                {
                    return rect;
                }
            }

            return null;
        }

        private static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.offsetMin = source.offsetMin;
            target.offsetMax = source.offsetMax;
            target.localScale = source.localScale;
            target.localRotation = source.localRotation;
        }

        private static void StretchToFillParent(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            target.anchorMin = new Vector2(0f, 0f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = Vector2.zero;
            target.sizeDelta = Vector2.zero;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }

        private static void ConfigureInjectedSkillsScreenRect(OtherPlayerProfileScreen screen, RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            float referenceHeight = ResolveReferencePanelHeight(screen);
            float calculatedHeight = referenceHeight > 0f
                ? referenceHeight + SkillsScreenOffset.y
                : target.rect.height + SkillsScreenOffset.y;

            target.anchorMin = new Vector2(0f, 1f);
            target.anchorMax = new Vector2(1f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = SkillsScreenOffset;
            target.sizeDelta = new Vector2(0f, calculatedHeight);
            target.localScale = Vector3.one;
            target.localRotation = Quaternion.identity;
        }

        private static float ResolveReferencePanelHeight(OtherPlayerProfileScreen screen)
        {
            if (screen == null)
            {
                return 0f;
            }

            InventoryPlayerModelWithStatsWindow playerModelWindow =
                PlayerModelWindowField?.GetValue(screen) as InventoryPlayerModelWithStatsWindow;

            if (TryGetClothingPanel(screen, playerModelWindow, out RectTransform clothingPanel, out _, out Transform parent))
            {
                if (parent is RectTransform parentRect && parentRect.rect.height > 0f)
                {
                    return parentRect.rect.height;
                }

                if (clothingPanel != null && clothingPanel.rect.height > 0f)
                {
                    return clothingPanel.rect.height;
                }
            }

            if (playerModelWindow?.transform is RectTransform playerModelRect && playerModelRect.rect.height > 0f)
            {
                return playerModelRect.rect.height;
            }

            Transform playerModelRoot = screen.transform.Find("PlayerModelWithStats")
                ?? FindChildRecursive(screen.transform, "PlayerModelWithStats");
            if (playerModelRoot is RectTransform rootRect && rootRect.rect.height > 0f)
            {
                return rootRect.rect.height;
            }

            return 0f;
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
                string responseJson = RequestHandler.PostJson(SuitRoute, SerializeBody(new FriendlyTeammateSuitRequest
                {
                    aid = ViewedProfile.AccountId,
                    suit = new string[] { body, feet }
                }));
                EnsureBodySuccess(responseJson);
                MarkSquadRosterDirty();
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
                MarkSquadRosterDirty();
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

        internal static void CloseLoadoutEditorOverlay()
        {
            if (LoadoutEditorOverlayRoot != null)
            {
                GameObject.Destroy(LoadoutEditorOverlayRoot);
                LoadoutEditorOverlayRoot = null;
            }
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
            HideProfileRightSideRoot(screen, screen?.transform.Find("Overall")?.gameObject);
            Transform rightSide = screen?.transform.Find("RightSide")
                ?? FindChildRecursive(screen?.transform, "RightSide");
            if (rightSide != null)
            {
                foreach (Transform child in rightSide)
                {
                    HideProfileRightSideRoot(screen, child.gameObject);
                }
            }
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

            Transform rightSideRoot = screenRoot.Find("RightSide")
                ?? FindChildRecursive(screenRoot, "RightSide");
            if (rightSideRoot != null && target.IsChildOf(rightSideRoot))
            {
                Transform currentRightSide = target;
                while (currentRightSide.parent != null && currentRightSide.parent != rightSideRoot)
                {
                    currentRightSide = currentRightSide.parent;
                }

                return currentRightSide == rightSideRoot ? null : currentRightSide.gameObject;
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
                ReplaceDropdownIcon(panel.transform, "Lower/Icon", BrainIconPath);
                upperDropdown.gameObject.SetActive(true);
                lowerDropdown.gameObject.SetActive(true);
            }
        }

        private static void ApplyLoadoutPanelLayout(InventoryClothingSelectionPanel panel, InventoryClothingSelectionPanel sourcePanel, bool useLowerSource = false)
        {
            ApplyDropdownLayout(
                UpperDropdownField?.GetValue(panel) as DropDownBox,
                UpperDropdownField?.GetValue(sourcePanel) as DropDownBox);
            ApplyDropdownLayout(
                LowerDropdownField?.GetValue(panel) as DropDownBox,
                LowerDropdownField?.GetValue(sourcePanel) as DropDownBox);

            ApplyIconLayout(panel.transform, "Upper/Icon", sourcePanel.transform, "Upper/Icon");
            ApplyIconLayout(panel.transform, "Lower/Icon", sourcePanel.transform, "Lower/Icon");
        }

        private static void ApplyDropdownLayout(DropDownBox targetDropdown, DropDownBox sourceDropdown)
        {
            if (targetDropdown?.transform is not RectTransform targetRect || sourceDropdown?.transform is not RectTransform sourceRect)
            {
                return;
            }

            targetRect.anchorMin = sourceRect.anchorMin;
            targetRect.anchorMax = sourceRect.anchorMax;
            targetRect.pivot = sourceRect.pivot;
            targetRect.anchoredPosition = sourceRect.anchoredPosition;
            targetRect.sizeDelta = sourceRect.sizeDelta;
            targetRect.offsetMin = sourceRect.offsetMin;
            targetRect.offsetMax = sourceRect.offsetMax;
            targetRect.localScale = sourceRect.localScale;
        }

        private static void ApplyIconLayout(Transform targetParent, string targetPath, Transform sourceParent, string sourcePath)
        {
            RectTransform targetRect = targetParent?.Find(targetPath) as RectTransform;
            RectTransform sourceRect = sourceParent?.Find(sourcePath) as RectTransform;
            if (targetRect == null || sourceRect == null)
            {
                return;
            }

            targetRect.anchorMin = sourceRect.anchorMin;
            targetRect.anchorMax = sourceRect.anchorMax;
            targetRect.pivot = sourceRect.pivot;
            targetRect.anchoredPosition = sourceRect.anchoredPosition;
            targetRect.sizeDelta = sourceRect.sizeDelta;
            targetRect.offsetMin = sourceRect.offsetMin;
            targetRect.offsetMax = sourceRect.offsetMax;
            targetRect.localScale = sourceRect.localScale;
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
            image.preserveAspect = true;
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
            HashSet<string> loadoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            dropDownItem currentLoadout = null;
            foreach (FriendlyTeammateLoadoutOption option in options.Loadouts)
            {
                FriendlyProfileDropdownItem item = new FriendlyProfileDropdownItem
                {
                    Id = option.Id,
                    Name = option.Name
                };

                CustomDropdownIds.Add(item.Id);
                loadoutIds.Add(item.Id);
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

            FriendlyProfileDropdownItem currentTactic = new FriendlyProfileDropdownItem
            {
                Id = "111111111111111111111111",
                Name = GetSocialUiText("ProfileTactic", "Default")
            };

            CustomDropdownIds.Add(currentTactic.Id);
            List<dropDownItem> tacticItems = new List<dropDownItem> { currentTactic };

            panel.Show(loadoutItems, currentLoadout, tacticItems, currentTactic, false, selected =>
            {
                if (selected == null || !loadoutIds.Contains(selected.Id))
                {
                    return;
                }

                try
                {
                    string responseJson = RequestHandler.PostJson(LoadoutRoute, SerializeBody(new FriendlyTeammateLoadoutRequest
                    {
                        aid = profile.AccountId,
                        loadoutId = selected.Id
                    }));
                    EnsureBodySuccess(responseJson);

                    MarkSquadRosterDirty();
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

            OtherPlayerProfileScreenPatch.CloseLoadoutEditorOverlay();

            if (OtherPlayerProfileScreenPatch.EditLoadoutButton != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.EditLoadoutButton.gameObject);
                OtherPlayerProfileScreenPatch.EditLoadoutButton = null;
            }

            OtherPlayerProfileScreenPatch.RestoreHideoutButtonVisuals(__instance);
            OtherPlayerProfileScreenPatch.RestoreProfileRightSideContent(__instance);

            if (OtherPlayerProfileScreenPatch.LoadoutSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.LoadoutSelector.gameObject);
                OtherPlayerProfileScreenPatch.LoadoutSelector = null;
            }

            if (OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot.gameObject);
                OtherPlayerProfileScreenPatch.EditLoadoutButtonRoot = null;
            }

            if (OtherPlayerProfileScreenPatch.SkillsPanel != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.SkillsPanel.gameObject);
                OtherPlayerProfileScreenPatch.SkillsPanel = null;
            }

            if (OtherPlayerProfileScreenPatch.SkillsPanelHost != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.SkillsPanelHost.gameObject);
                OtherPlayerProfileScreenPatch.SkillsPanelHost = null;
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
