using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Settings;
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

    internal class FriendlyTeammateTacticOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class FriendlyTeammateProfileOptions
    {
        public string CurrentLoadoutId { get; set; }
        public string CurrentTactic { get; set; }
        public float Aggression { get; set; } = 50f;
        public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; }
        public List<FriendlyTeammateTacticOption> Tactics { get; set; }
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

    internal class FriendlyTeammateAggressionRequest
    {
        public string aid { get; set; }
        public float aggression { get; set; }
    }

    internal class FriendlyTeammateTacticRequest
    {
        public string aid { get; set; }
        public string tactic { get; set; }
    }

    internal sealed class LoadoutEditorHeaderDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;

        private bool _dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = Target != null;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || Target == null)
            {
                return;
            }

            Vector2 delta = eventData.delta;
            Target.offsetMin += delta;
            Target.offsetMax += delta;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }
    }

    internal sealed class ProfileTooltipHoverController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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
        private const string AggressionRoute = "/singleplayer/friendlysain/teammate/profile/aggression";
        private const string TacticRoute = "/singleplayer/friendlysain/teammate/profile/tactic";

        private static readonly FieldInfo PlayerModelWindowField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
        private static readonly FieldInfo ClothingPanelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_clothingPanel");
        private static readonly FieldInfo NicknameLabelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_nicknameLabel");
        private static readonly FieldInfo NicknameFieldInputField = AccessTools.Field(typeof(NicknameField), "_inputField");
        private static readonly FieldInfo NicknameFieldStatusLabelField = AccessTools.Field(typeof(NicknameField), "_statusLabel");
        private static readonly FieldInfo NicknameFieldUsedSymbolsField = AccessTools.Field(typeof(NicknameField), "_usedSymbolsCount");
        private static readonly FieldInfo BackButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_backButton");
        private static readonly FieldInfo HideoutButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_hideoutButton");
        private static readonly MethodInfo HideoutButtonHandlerMethod = AccessTools.Method(typeof(OtherPlayerProfileScreen), "method_11");
        private static readonly FieldInfo SettingsScreenGameTabField = AccessTools.Field(typeof(SettingsScreen), "_gameSettingsScreen");
        private static readonly FieldInfo GameSettingsSliderTemplateField = AccessTools.Field(typeof(GameSettingsTab), "_fov");
        private static readonly FieldInfo NumberSliderValueInputField = AccessTools.Field(typeof(NumberSlider), "_valueInput");
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
        private static readonly FieldInfo TransferItemsScreenStashPanelField = AccessTools.Field(typeof(TransferItemsScreen), "_stashPanel");
        private static readonly FieldInfo InventoryScreenItemsPanelField = AccessTools.Field(typeof(InventoryScreen), "_itemsPanel");
        private static readonly FieldInfo ItemsPanelComplexStashPanelField = AccessTools.Field(typeof(ItemsPanel), "_complexStashPanel");
        private static readonly FieldInfo ComplexStashPanelLootPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_lootPanel");
        private static readonly FieldInfo ComplexStashPanelContainersPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_containersPanel");
        private static readonly FieldInfo ComplexStashPanelEquipmentPanelSourceField = AccessTools.Field(typeof(ComplexStashPanel), "_equipmentPanelSource");
        private static readonly FieldInfo ComplexStashPanelComplexPanelField = AccessTools.Field(typeof(ComplexStashPanel), "_complexPanel");
        private static readonly FieldInfo ComplexStashPanelContainerNamePanelField = AccessTools.Field(typeof(ComplexStashPanel), "_containerNamePanel");
        private static readonly FieldInfo ContainersPanelSlotViewsContainerField = AccessTools.Field(typeof(ContainersPanel), "_slotViewsContainer");
        private static readonly FieldInfo ContainersPanelDictionaryField = AccessTools.Field(typeof(ContainersPanel), "dictionary_0");
        private static readonly FieldInfo ContainersPanelDogtagSlotViewField = AccessTools.Field(typeof(ContainersPanel), "slotView_0");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private static readonly string GearIconPath = Path.Combine(PluginDirectory, "gear.png");
        private static readonly string BrainIconPath = Path.Combine(PluginDirectory, "brain.png");
        private static readonly Vector2 SkillsScreenOffset = new Vector2(-38f, -100f);
        private static readonly Vector2 FactionBadgeFollowerOffset = new Vector2(0f, -60f);
        private static readonly EquipmentSlot[] LoadoutEditorContainerSlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack
        };
        private static readonly EquipmentSlot[] LoadoutEditorEquipmentSlots =
        {
            EquipmentSlot.Scabbard,
            EquipmentSlot.Holster,
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Eyewear,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Headwear,
            EquipmentSlot.Earpiece,
            EquipmentSlot.ArmorVest,
            EquipmentSlot.ArmBand,
            EquipmentSlot.Dogtag
        };
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
        public static InventoryController ActiveProfileInventoryController { get; set; }
        public static ISession ActiveProfileSession { get; set; }
        public static Transform LoadoutSelector { get; set; }
        public static Transform AggressionSelector { get; set; }
        public static DefaultUIButton EditLoadoutButton { get; set; }
        public static Transform EditLoadoutButtonRoot { get; set; }
        public static GameObject LoadoutEditorOverlayRoot { get; set; }
        public static SimpleStashPanel LoadoutEditorStashPanel { get; set; }
        public static ComplexStashPanel LoadoutEditorEquipmentPanel { get; set; }
        public static SkillsScreen SkillsPanel { get; set; }
        public static RectTransform SkillsPanelHost { get; set; }
        public static CustomTextMeshProUGUI OriginalNicknameLabel { get; set; }
        public static DefaultUIButton NicknameRenameButton { get; set; }
        public static GameObject RenameOverlayRoot { get; set; }
        public static NicknameField RenameOverlayField { get; set; }
        public static Vector2? OriginalNicknameAnchoredPosition { get; set; }
        public static RectTransform FactionBadgeIconsContainer { get; set; }
        public static Vector2? OriginalFactionBadgeAnchoredPosition { get; set; }
        public static string OriginalHideoutButtonText { get; set; }
        public static int? OriginalHideoutButtonFontSize { get; set; }
        public static List<MongoID> CustomDropdownIds { get; } = new List<MongoID>();
        private static List<GameObject> HiddenRenameButtonDecorations { get; } = new List<GameObject>();
        private static Dictionary<GameObject, bool> HiddenRightSideRoots { get; } = new Dictionary<GameObject, bool>();
        private static Coroutine PendingAggressionPersistCoroutine { get; set; }
        private static int PendingAggressionPersistRevision { get; set; }
        internal static Action PendingBackOverrideAction { get; set; }
        internal static Action ActiveBackOverrideAction { get; set; }

        private static void MarkSquadRosterDirty(string accountId = null)
        {
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                Components.SquadControlMenuUi.RequestRosterTileRefreshOnNextInject(accountId);
                return;
            }

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
            ResetTeammateProfileUi(playerModelWindow);

            if (session?.Profile != null
                && session.Profile.AccountId == profile.AccountId)
            {
                RestoreHideoutButtonVisuals(__instance, profile);
                return;
            }

            RestoreHideoutButtonVisuals(__instance, profile);

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
            ActiveProfileInventoryController = inventoryController;
            ActiveProfileSession = session;
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            playerModelWindow.OnCustomizationChanged += PlayerModelWithStatsWindow_OnCustomizationChanged;

            HideProfileActions(__instance);
            ClearProfileRightSideContent(__instance);
            ConfigureNicknameRenameUi(__instance, playerModelWindow, profile);
            MoveFactionBadgeForFollowerProfile(__instance, playerModelWindow);

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

            if (AggressionSelector != null)
            {
                GameObject.Destroy(AggressionSelector.gameObject);
                AggressionSelector = null;
            }

            StopPendingAggressionPersist();

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
                CreateAggressionSliderRow(__instance, clone, parent, profile, options);
                CreateEditLoadoutButton(__instance, clone, parent, profile, 2);
                DisplaySkillsPanel(__instance, profile, session);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to apply teammate profile elements.");
                Modules.Logger.LogError(ex);
            }
        }

        private static RectTransform CreateAggressionSliderRow(
            OtherPlayerProfileScreen screen,
            RectTransform loadoutSelector,
            Transform parent,
            ResultProfile profile,
            FriendlyTeammateProfileOptions options)
        {
            if (screen == null || loadoutSelector == null || parent == null || profile == null || options == null)
            {
                return null;
            }

            if (AggressionSelector != null)
            {
                GameObject.Destroy(AggressionSelector.gameObject);
                AggressionSelector = null;
            }

            RectTransform rowClone = GameObject.Instantiate(loadoutSelector, parent, true);
            rowClone.name = "friendlySAIN_AggressionRow";
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

            bool isMarksmanTactic = IsMarksmanTactic(options.CurrentTactic);
            float aggressionValue = Mathf.Clamp(options.Aggression, 0f, 100f);

            CreateAggressionRowContent(rowClone, profile, aggressionValue, !isMarksmanTactic, isMarksmanTactic);
            AggressionSelector = rowClone;
            return rowClone;
        }

        private static bool IsMarksmanTactic(string tactic)
        {
            return string.Equals(tactic, "marksman", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetAggressionRowMarksmanState(bool isMarksman)
        {
            if (AggressionSelector == null)
            {
                return;
            }

            CanvasGroup rowCanvasGroup = AggressionSelector.GetComponent<CanvasGroup>() ?? AggressionSelector.gameObject.AddComponent<CanvasGroup>();
            rowCanvasGroup.alpha = isMarksman ? 0.3f : 1f;

            CustomTextMeshProUGUI label = AggressionSelector.Find("friendlySAIN_AggressionLabel")?.GetComponent<CustomTextMeshProUGUI>();
            if (label != null)
            {
                label.color = isMarksman ? new Color(0.62f, 0.62f, 0.62f, 1f) : Color.white;
            }

            NumberSlider slider = AggressionSelector.GetComponentsInChildren<NumberSlider>(true)
                .FirstOrDefault(candidate => candidate != null && string.Equals(candidate.name, "friendlySAIN_ProfileAggressionSlider", StringComparison.Ordinal));
            if (slider != null)
            {
                slider.enabled = !isMarksman;

                Slider stockSlider = slider.GetComponentInChildren<Slider>(true);
                if (stockSlider != null)
                {
                    stockSlider.interactable = !isMarksman;
                }

                TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
                if (valueInput != null)
                {
                    valueInput.readOnly = isMarksman;
                    valueInput.interactable = !isMarksman;
                }
            }

            Transform existingTooltip = AggressionSelector.Find("friendlySAIN_AggressionDisabledTooltip");
            if (!isMarksman)
            {
                if (existingTooltip != null)
                {
                    GameObject.Destroy(existingTooltip.gameObject);
                }

                return;
            }

            RectTransform sliderRoot = slider?.transform as RectTransform;
            if (existingTooltip != null || sliderRoot == null)
            {
                return;
            }

            GameObject tooltipAreaObject = new GameObject("friendlySAIN_AggressionDisabledTooltip", typeof(RectTransform), typeof(Image));
            tooltipAreaObject.transform.SetParent(sliderRoot, false);

            RectTransform tooltipAreaRect = tooltipAreaObject.GetComponent<RectTransform>();
            tooltipAreaRect.anchorMin = Vector2.zero;
            tooltipAreaRect.anchorMax = Vector2.one;
            tooltipAreaRect.offsetMin = Vector2.zero;
            tooltipAreaRect.offsetMax = Vector2.zero;
            tooltipAreaRect.localScale = Vector3.one;

            Image tooltipAreaImage = tooltipAreaObject.GetComponent<Image>();
            tooltipAreaImage.color = new Color(0f, 0f, 0f, 0f);
            tooltipAreaImage.raycastTarget = true;

            ProfileTooltipHoverController tooltipHover = tooltipAreaObject.AddComponent<ProfileTooltipHoverController>();
            tooltipHover.Configure(GetSocialUiText("ProfileAggressionMarksmanTooltip", "Agression not available for Marksman"));
        }

        private static void CreateAggressionRowContent(RectTransform rowRoot, ResultProfile profile, float aggressionValue, bool interactable, bool isMarksman)
        {
            if (rowRoot == null || profile == null)
            {
                return;
            }

            CanvasGroup rowCanvasGroup = rowRoot.GetComponent<CanvasGroup>() ?? rowRoot.gameObject.AddComponent<CanvasGroup>();
            rowCanvasGroup.alpha = isMarksman ? 0.3f : 1f;

            CustomTextMeshProUGUI label = CreateAggressionLabel(
                "friendlySAIN_AggressionLabel",
                rowRoot,
                GetSocialUiText("ProfileAggression", "Aggression"),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(44f, 0f),
                new Vector2(190f, 28f),
                18f,
                TextAlignmentOptions.MidlineLeft);

            NumberSlider slider = CloneStockNumberSlider(rowRoot);
            if (slider == null)
            {
                return;
            }

            RectTransform sliderRoot = slider.transform as RectTransform;
            if (sliderRoot != null)
            {
                float sliderHeight = sliderRoot.sizeDelta.y > 0f ? sliderRoot.sizeDelta.y : 36f;
                sliderRoot.anchorMin = new Vector2(0f, 0.5f);
                sliderRoot.anchorMax = new Vector2(0f, 0.5f);
                sliderRoot.pivot = new Vector2(0f, 0.5f);
                sliderRoot.anchoredPosition = new Vector2(243f, 42f);
                sliderRoot.sizeDelta = new Vector2(300f, sliderHeight);
                sliderRoot.localScale = Vector3.one;
            }

            Transform captionRoot = sliderRoot?.Find("Caption");
            if (captionRoot != null)
            {
                GameObject.Destroy(captionRoot.gameObject);
            }

            slider.Show(0f, 100f, "0");
            slider.UpdateValue(Mathf.Round(aggressionValue), false, 0f, 100f);

            Slider stockSlider = slider.GetComponentInChildren<Slider>(true);
            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            if (stockSlider != null)
            {
                stockSlider.interactable = interactable;
            }

            slider.enabled = interactable;

            if (valueInput != null)
            {
                valueInput.readOnly = !interactable;
                valueInput.interactable = interactable;
            }
            if (!interactable)
            {
                Color disabledColor = new Color(0.62f, 0.62f, 0.62f, 1f);
                if (label != null)
                {
                    label.color = disabledColor;
                }

                if (sliderRoot != null)
                {
                    GameObject tooltipAreaObject = new GameObject("friendlySAIN_AggressionDisabledTooltip", typeof(RectTransform), typeof(Image));
                    tooltipAreaObject.transform.SetParent(sliderRoot, false);

                    RectTransform tooltipAreaRect = tooltipAreaObject.GetComponent<RectTransform>();
                    tooltipAreaRect.anchorMin = Vector2.zero;
                    tooltipAreaRect.anchorMax = Vector2.one;
                    tooltipAreaRect.offsetMin = Vector2.zero;
                    tooltipAreaRect.offsetMax = Vector2.zero;
                    tooltipAreaRect.localScale = Vector3.one;

                    Image tooltipAreaImage = tooltipAreaObject.GetComponent<Image>();
                    tooltipAreaImage.color = new Color(0f, 0f, 0f, 0f);
                    tooltipAreaImage.raycastTarget = true;

                    ProfileTooltipHoverController tooltipHover = tooltipAreaObject.AddComponent<ProfileTooltipHoverController>();
                    tooltipHover.Configure(GetSocialUiText("ProfileAggressionMarksmanTooltip", "Agression not available for Marksman"));
                }
            }

            slider.Bind(value =>
            {
                int roundedValue = Mathf.RoundToInt(value);
                if (interactable)
                {
                    ScheduleAggressionPersist(profile.AccountId, roundedValue);
                }
            });
        }

        private static CustomTextMeshProUGUI CreateAggressionLabel(
            string name,
            RectTransform parent,
            string text,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            GameObject labelObject = new GameObject(name, typeof(RectTransform), typeof(CustomTextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            CustomTextMeshProUGUI label = labelObject.GetComponent<CustomTextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = new Color(0.87f, 0.87f, 0.84f, 1f);
            label.raycastTarget = false;
            return label;
        }

        private static SettingsScreen ResolveSettingsScreenTemplate()
        {
            return Resources.FindObjectsOfTypeAll<SettingsScreen>()
                .FirstOrDefault(screen =>
                    screen != null &&
                    SettingsScreenGameTabField?.GetValue(screen) is GameSettingsTab);
        }

        private static GameSettingsTab ResolveGameSettingsTabTemplate()
        {
            SettingsScreen settingsScreen = ResolveSettingsScreenTemplate();
            return SettingsScreenGameTabField?.GetValue(settingsScreen) as GameSettingsTab;
        }

        private static RectTransform ResolveSettingsSliderContainerTemplate()
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

        private static NumberSlider ResolveSettingsSliderTemplate()
        {
            GameSettingsTab gameSettingsTab = ResolveGameSettingsTabTemplate();
            return GameSettingsSliderTemplateField?.GetValue(gameSettingsTab) as NumberSlider;
        }

        private static NumberSlider CloneStockNumberSlider(RectTransform parent)
        {
            RectTransform templateRoot = ResolveSettingsSliderContainerTemplate();
            if (templateRoot == null)
            {
                return null;
            }

            RectTransform sliderRoot = GameObject.Instantiate(templateRoot, parent, false);
            NumberSlider slider = sliderRoot.GetComponent<NumberSlider>() ?? sliderRoot.GetComponentInChildren<NumberSlider>(true);
            if (slider == null)
            {
                GameObject.Destroy(sliderRoot.gameObject);
                return null;
            }

            slider.name = "friendlySAIN_ProfileAggressionSlider";
            sliderRoot.localScale = Vector3.one;

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            HideStockLabelContainers(sliderRoot, valueInput?.transform);

            slider.gameObject.SetActive(true);
            return slider;
        }

        private static void HideStockLabelContainers(Transform root, Transform exemptRoot)
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

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                DestroyStockLabelObject(exemptRoot, label?.transform);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                DestroyStockLabelObject(exemptRoot, label?.transform);
            }
        }

        private static void HideStockLabelContainer(Transform root, Transform exemptRoot, Transform labelTransform, HashSet<GameObject> hiddenObjects)
        {
            if (root == null || labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && (labelTransform == exemptRoot || labelTransform.IsChildOf(exemptRoot)))
            {
                return;
            }

            Transform current = labelTransform;
            while (current != null && current != root)
            {
                if (current.TryGetComponent(out LayoutElement _))
                {
                    if (hiddenObjects.Add(current.gameObject))
                    {
                        current.gameObject.SetActive(false);
                    }

                    return;
                }

                current = current.parent;
            }
        }

        private static void DestroyStockLabelObject(Transform exemptRoot, Transform labelTransform)
        {
            if (labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && (labelTransform == exemptRoot || labelTransform.IsChildOf(exemptRoot)))
            {
                return;
            }

            GameObject.Destroy(labelTransform.gameObject);
        }

        private static void ScheduleAggressionPersist(string accountId, int aggression)
        {
            StopPendingAggressionPersist();

            if (string.IsNullOrWhiteSpace(accountId) || friendlySAIN.Instance == null)
            {
                return;
            }

            int revision = ++PendingAggressionPersistRevision;
            PendingAggressionPersistCoroutine = friendlySAIN.Instance.StartCoroutine(
                PersistAggressionDelayed(accountId, aggression, revision));
        }

        internal static void StopPendingAggressionPersist()
        {
            if (PendingAggressionPersistCoroutine != null && friendlySAIN.Instance != null)
            {
                friendlySAIN.Instance.StopCoroutine(PendingAggressionPersistCoroutine);
            }

            PendingAggressionPersistCoroutine = null;
        }

        private static System.Collections.IEnumerator PersistAggressionDelayed(string accountId, int aggression, int revision)
        {
            yield return new WaitForSecondsRealtime(0.35f);

            if (revision != PendingAggressionPersistRevision)
            {
                yield break;
            }

            PendingAggressionPersistCoroutine = null;

            try
            {
                string responseJson = RequestHandler.PostJson(AggressionRoute, SerializeBody(new FriendlyTeammateAggressionRequest
                {
                    aid = accountId,
                    aggression = aggression
                }));
                EnsureBodySuccess(responseJson);
                MarkSquadRosterDirty(accountId);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate aggression change.");
                Modules.Logger.LogError(ex);
            }
        }

        private static bool IsDefaultTacticSelection(string tactic)
        {
            return string.IsNullOrWhiteSpace(tactic)
                || string.Equals(tactic, "default", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tactic, "balanced", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetTacticDisplayName(string tactic)
        {
            if (string.IsNullOrWhiteSpace(tactic))
            {
                return GetSocialUiText("ProfileTactic", "Balanced");
            }

            return IsDefaultTacticSelection(tactic)
                ? GetSocialUiText("ProfileTactic", "Balanced")
                : tactic;
        }

        private static void CreateEditLoadoutButton(OtherPlayerProfileScreen screen, RectTransform loadoutSelector, Transform parent, ResultProfile profile, int rowOffset = 1)
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
            rowClone.anchoredPosition = loadoutSelector.anchoredPosition + new Vector2(0f, -72f * Mathf.Max(1, rowOffset));
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

            if (screen == null || profile == null || ActiveProfileSession?.Profile?.Inventory?.Stash == null)
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

            itemUiContext.Configure(
                ActiveProfileInventoryController,
                ActiveProfileSession.Profile,
                ActiveProfileSession,
                EItemUiContextType.TransferItemsScreen,
                ECursorResult.ShowCursor);

            TryBuildLoadoutEditorStashPanel(leftSection);
            TryBuildLoadoutEditorFollowerPanel(profile, rightSection, itemUiContext);
        }

        private static void TryBuildLoadoutEditorStashPanel(RectTransform leftSection)
        {
            try
            {
                SimpleStashPanel stashTemplate = ResolveLoadoutEditorStashTemplate();
                StashItemClass fakeStash = CreateClonedFakeStash(ActiveProfileSession.Profile.Inventory.Stash);
                if (stashTemplate == null || fakeStash == null)
                {
                    throw new InvalidOperationException($"stashTemplate={(stashTemplate != null)}, fakeStash={(fakeStash != null)}");
                }

                ItemContextAbstractClass stashContext = new GClass3459(
                    fakeStash,
                    GClass3459.EItemType.Inventory,
                    ActiveProfileInventoryController.Inventory.FavoriteItemsStorage,
                    false);

                SimpleStashPanel stashPanel = GameObject.Instantiate(stashTemplate, leftSection, false);
                stashPanel.name = "friendlySAIN_LoadoutEditorStashPanel";
                if (stashPanel.transform is RectTransform stashRect)
                {
                    StretchToFillParent(stashRect);
                }

                stashPanel.Show(
                    fakeStash,
                    ActiveProfileInventoryController,
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

        private static void TryBuildLoadoutEditorFollowerPanel(ResultProfile profile, RectTransform rightSection, ItemUiContext itemUiContext)
        {
            try
            {
                ComplexStashPanel equipmentTemplate = ResolveLoadoutEditorEquipmentTemplate();
                TraderControllerClass followerInventoryController = null;
                Inventory followerInventory = CreateClonedFollowerInventory(profile.Equipment, out followerInventoryController);
                InventoryEquipment equipmentView = followerInventory?.Equipment;
                if (equipmentTemplate == null || equipmentView == null || followerInventoryController == null)
                {
                    throw new InvalidOperationException($"equipmentTemplate={(equipmentTemplate != null)}, followerInventory={(followerInventory != null)}, equipmentView={(equipmentView != null)}, followerController={(followerInventoryController != null)}");
                }

                ItemContextAbstractClass equipmentContext = new GClass3450(EItemViewType.TransferTrader);

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
                    followerInventoryController,
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

        private static Inventory CreateClonedFollowerInventory(InventoryEquipment sourceEquipment, out TraderControllerClass followerInventoryController)
        {
            followerInventoryController = null;
            if (sourceEquipment == null)
            {
                return null;
            }

            InventoryDescriptorClass equipmentDescriptor = EFTItemSerializerClass.SerializeItem(sourceEquipment, null);
            if (equipmentDescriptor == null)
            {
                return null;
            }

            EFTInventoryClass descriptor = new EFTInventoryClass
            {
                Equipment = equipmentDescriptor,
                FavoriteItemsStorage = new List<MongoID>(),
                FastAccess = new Dictionary<EBoundItem, MongoID>(),
                DiscardLimits = new Dictionary<MongoID, int>()
            };

            Inventory clonedInventory = descriptor.ToInventory();
            InventoryEquipment clonedEquipment = clonedInventory?.Equipment;
            if (clonedEquipment == null)
            {
                return clonedInventory;
            }

            followerInventoryController = new TraderControllerClass(
                clonedEquipment,
                "friendlysain_fake_follower",
                sourceEquipment.Owner?.ContainerName ?? "follower",
                false,
                EOwnerType.Profile);

            foreach (EquipmentSlot equipmentSlot in InventoryEquipment.AllSlotNames)
            {
                Slot slot = clonedEquipment.GetSlot(equipmentSlot);
                Item containedItem = slot?.ContainedItem;
                if (slot == null || containedItem == null)
                {
                    continue;
                }

                // Re-apply slot ownership so nested container views resolve a valid owner chain.
                slot.ChangeContainedItemDirectly(containedItem);
            }

            return clonedInventory;
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
            TraderControllerClass followerInventoryController,
            string followerName,
            SkillManager skills,
            InsuranceCompanyClass insurance,
            ItemUiContext itemUiContext)
        {
            if (panelRoot == null || equipmentView == null || followerInventoryController == null)
            {
                throw new InvalidOperationException("equipment panel root, equipment view, or follower inventory controller was null");
            }

            Transform lootPanel = ComplexStashPanelLootPanelField?.GetValue(panelRoot) as Transform;
            ContainersPanel containersPanel = ComplexStashPanelContainersPanelField?.GetValue(panelRoot) as ContainersPanel;
            EquipmentTab equipmentTemplate = ComplexStashPanelEquipmentPanelSourceField?.GetValue(panelRoot) as EquipmentTab;
            GameObject complexPanel = ComplexStashPanelComplexPanelField?.GetValue(panelRoot) as GameObject;
            GameObject containerNamePanel = ComplexStashPanelContainerNamePanelField?.GetValue(panelRoot) as GameObject;

            if (lootPanel == null || containersPanel == null || equipmentTemplate == null || complexPanel == null)
            {
                throw new InvalidOperationException("equipment panel template was missing loot panel, containers panel, equipment tab, or complex panel");
            }

            complexPanel.SetActive(true);
            if (containerNamePanel != null)
            {
                containerNamePanel.SetActive(false);
            }

            RenderLoadoutEditorContainersPanel(
                containersPanel,
                equipmentContext,
                equipmentView,
                followerInventoryController,
                skills,
                insurance);

            EquipmentTab equipmentTab = GameObject.Instantiate(equipmentTemplate, lootPanel, false);
            equipmentTab.transform.SetAsFirstSibling();
            RenderLoadoutEditorEquipmentTab(
                equipmentTab,
                equipmentContext,
                equipmentView,
                followerInventoryController,
                skills,
                insurance);

            SetLoadoutEditorEquipmentHeader(panelRoot.transform, followerName);
            itemUiContext.RegisterView(equipmentContext);
        }

        private static void RenderLoadoutEditorContainersPanel(
            ContainersPanel containersPanel,
            ItemContextAbstractClass equipmentContext,
            InventoryEquipment equipmentView,
            TraderControllerClass followerInventoryController,
            SkillManager skills,
            InsuranceCompanyClass insurance)
        {
            Transform slotViewsContainer = ContainersPanelSlotViewsContainerField?.GetValue(containersPanel) as Transform;
            Dictionary<EquipmentSlot, SlotView> renderedViews =
                ContainersPanelDictionaryField?.GetValue(containersPanel) as Dictionary<EquipmentSlot, SlotView>;

            if (slotViewsContainer == null || renderedViews == null)
            {
                throw new InvalidOperationException("containers panel layout fields were missing");
            }

            ItemUiContext itemUiContext = ItemUiContext.Instance;
            foreach (EquipmentSlot equipmentSlot in LoadoutEditorContainerSlots)
            {
                Slot slot = equipmentView.GetSlot(equipmentSlot);
                if (slot == null)
                {
                    continue;
                }

                SlotView slotView = containersPanel.method_0(equipmentSlot);
                if (slotView == null)
                {
                    continue;
                }

                if (equipmentSlot == EquipmentSlot.Pockets)
                {
                    HorizontalLayoutGroup pocketsLayout = slotView.gameObject.GetComponent<HorizontalLayoutGroup>();
                    if (pocketsLayout != null)
                    {
                        pocketsLayout.spacing += 10f;
                    }
                }

                slotView.transform.SetParent(slotViewsContainer, false);
                slotView.Show(
                    slot,
                    equipmentContext,
                    followerInventoryController,
                    itemUiContext,
                    skills,
                    insurance,
                    false);
                renderedViews[equipmentSlot] = slotView;
            }
        }

        private static void SetLoadoutEditorEquipmentHeader(Transform panelRoot, string followerName)
        {
            if (panelRoot == null || string.IsNullOrWhiteSpace(followerName))
            {
                return;
            }

            Transform headerTextTransform = panelRoot.Find("Header/Text");
            if (headerTextTransform == null)
            {
                headerTextTransform = panelRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform =>
                        transform != null
                        && string.Equals(transform.name, "Text", StringComparison.Ordinal)
                        && string.Equals(transform.parent?.name, "Header", StringComparison.Ordinal));
            }

            if (headerTextTransform == null)
            {
                return;
            }

            LocalizedText localizedText = headerTextTransform.GetComponent<LocalizedText>();
            if (localizedText != null)
            {
                localizedText.enabled = false;
            }

            TMP_Text headerText = headerTextTransform.GetComponent<TMP_Text>();
            if (headerText != null)
            {
                headerText.text = followerName.ToUpperInvariant();
            }
        }

        private static void RenderLoadoutEditorEquipmentTab(
            EquipmentTab equipmentTab,
            ItemContextAbstractClass equipmentContext,
            InventoryEquipment equipmentView,
            TraderControllerClass followerInventoryController,
            SkillManager skills,
            InsuranceCompanyClass insurance)
        {
            equipmentTab.gameObject.SetActive(true);
            HideLoadoutEditorCharacterGearImage(equipmentTab.transform);

            foreach (EquipmentSlot equipmentSlot in LoadoutEditorEquipmentSlots)
            {
                SlotView slotView = equipmentTab.GetSlotView(equipmentSlot);
                if (slotView == null)
                {
                    continue;
                }

                Slot slot = equipmentView.GetSlot(equipmentSlot);
                if (slot == null)
                {
                    slotView.gameObject.SetActive(false);
                    continue;
                }

                ItemContextAbstractClass slotContext = equipmentSlot == EquipmentSlot.Dogtag
                    ? new GClass3450()
                    : equipmentContext;

                slotView.Show(
                    slot,
                    slotContext,
                    followerInventoryController,
                    ItemUiContext.Instance,
                    skills,
                    insurance,
                    false);
            }
        }

        private static void HideLoadoutEditorCharacterGearImage(Transform equipmentTabRoot)
        {
            if (equipmentTabRoot == null)
            {
                return;
            }

            Transform characterGear = equipmentTabRoot.Find("ChracterGear");
            if (characterGear == null)
            {
                return;
            }

            Image characterGearImage = characterGear.GetComponent<Image>();
            if (characterGearImage != null)
            {
                characterGearImage.enabled = false;
            }
        }

        private static StashItemClass CreateClonedFakeStash(StashItemClass sourceStash)
        {
            if (sourceStash?.Grid == null)
            {
                return null;
            }

            StashItemClass fakeStash = Singleton<ItemFactoryClass>.Instance.CreateFakeStash(null);
            if (fakeStash == null)
            {
                return null;
            }

            StashGridClass sourceGrid = sourceStash.Grid;
            StashGridClass fakeGrid = new StashGridClass(
                sourceGrid.ID,
                sourceGrid.GridWidth,
                sourceGrid.GridHeight,
                sourceGrid.CanStretchVertically,
                sourceGrid.CanStretchHorizontally,
                sourceGrid.Filters ?? Array.Empty<ItemFilter>(),
                fakeStash,
                sourceGrid.MaxItemsCount ?? -1);

            fakeStash.Grids[0] = fakeGrid;
            _ = new TraderControllerClass(
                fakeStash,
                "friendlysain_fake_stash",
                sourceStash.Owner?.ContainerName ?? "stash",
                false,
                EOwnerType.Profile);

            foreach (KeyValuePair<Item, LocationInGrid> entry in sourceGrid.ItemCollection)
            {
                if (entry.Key == null || entry.Value == null)
                {
                    continue;
                }

                Item clonedItem = entry.Key.CloneItem(null);
                if (clonedItem == null)
                {
                    continue;
                }

                LocationInGrid clonedLocation = entry.Value.Clone();
                GStruct154<GClass3415> addResult = fakeGrid.AddItemWithoutRestrictions(clonedItem, clonedLocation);
                if (addResult.Failed)
                {
                    friendlySAIN.Log.LogWarning($"[UI] Loadout editor fake stash add failed for '{entry.Key.TemplateId}': {addResult.Error}");
                }
            }

            return fakeStash;
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
                    ?? screen?.transform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen?.transform.Find("ClothingPanel");

                if (hierarchyPanel == null)
                {
                    hierarchyPanel = FindChildRecursive(playerModelTransform, "ClothingPanel")
                        ?? FindChildRecursive(screen?.transform, "ClothingPanel");
                }

                clothingSelectionPanel = hierarchyPanel?.GetComponent<InventoryClothingSelectionPanel>();
            }

            clothingPanel = clothingSelectionPanel?.transform as RectTransform;
            parent = clothingPanel?.parent;
            return clothingPanel != null && clothingSelectionPanel != null && parent != null;
        }

        private static void ResetTeammateProfileUi(InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            ViewedProfile = null;
            ActiveProfileInventoryController = null;
            ActiveProfileSession = null;
            RestoreFactionBadgePosition();

            if (LoadoutSelector != null)
            {
                GameObject.Destroy(LoadoutSelector.gameObject);
                LoadoutSelector = null;
            }

            if (EditLoadoutButtonRoot != null)
            {
                GameObject.Destroy(EditLoadoutButtonRoot.gameObject);
                EditLoadoutButtonRoot = null;
            }

            EditLoadoutButton = null;

            if (TryGetClothingPanel(null, playerModelWindow, out RectTransform clothingPanel, out _, out _)
                && clothingPanel != null)
            {
                clothingPanel.gameObject.SetActive(false);
            }
        }

        private static void MoveFactionBadgeForFollowerProfile(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            RectTransform iconsContainer = FindFactionBadgeIconsContainer(screen, playerModelWindow);
            if (iconsContainer == null)
            {
                return;
            }

            if (!ReferenceEquals(FactionBadgeIconsContainer, iconsContainer))
            {
                RestoreFactionBadgePosition();
                FactionBadgeIconsContainer = iconsContainer;
                OriginalFactionBadgeAnchoredPosition = iconsContainer.anchoredPosition;
            }

            if (OriginalFactionBadgeAnchoredPosition == null)
            {
                OriginalFactionBadgeAnchoredPosition = iconsContainer.anchoredPosition;
            }

            iconsContainer.anchoredPosition = OriginalFactionBadgeAnchoredPosition.Value + FactionBadgeFollowerOffset;
        }

        public static void RestoreFactionBadgePosition()
        {
            if (FactionBadgeIconsContainer != null && OriginalFactionBadgeAnchoredPosition != null)
            {
                FactionBadgeIconsContainer.anchoredPosition = OriginalFactionBadgeAnchoredPosition.Value;
            }

            FactionBadgeIconsContainer = null;
            OriginalFactionBadgeAnchoredPosition = null;
        }

        private static RectTransform FindFactionBadgeIconsContainer(OtherPlayerProfileScreen screen, InventoryPlayerModelWithStatsWindow playerModelWindow)
        {
            Transform iconsContainer = playerModelWindow?.transform.Find("CharacterPanel/PlayerModelView/IconsContainer")
                ?? screen?.transform.Find("PlayerModelWithStats/CharacterPanel/PlayerModelView/IconsContainer")
                ?? FindChildRecursive(playerModelWindow?.transform, "IconsContainer")
                ?? FindChildRecursive(screen?.transform, "IconsContainer");

            return iconsContainer as RectTransform;
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
                MarkSquadRosterDirty(ViewedProfile?.AccountId);
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

            OriginalHideoutButtonText ??= hideoutButton.HeaderText;
            OriginalHideoutButtonFontSize ??= hideoutButton.HeaderSize;

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

        internal static void RestoreHideoutButtonVisuals(OtherPlayerProfileScreen screen, ResultProfile profile = null)
        {
            foreach (GameObject decoration in HiddenRenameButtonDecorations)
            {
                if (decoration != null)
                {
                    decoration.SetActive(true);
                }
            }

            HiddenRenameButtonDecorations.Clear();

            DefaultUIButton hideoutButton = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            if (hideoutButton == null)
            {
                return;
            }

            hideoutButton.OnClick.RemoveAllListeners();
            if (HideoutButtonHandlerMethod != null)
            {
                hideoutButton.OnClick.AddListener(() => HideoutButtonHandlerMethod.Invoke(screen, null));
            }

            if (!string.IsNullOrWhiteSpace(OriginalHideoutButtonText))
            {
                hideoutButton.SetHeaderText(OriginalHideoutButtonText, OriginalHideoutButtonFontSize ?? hideoutButton.HeaderSize);
            }
            if (profile != null)
            {
                hideoutButton.gameObject.SetActive(profile.HideoutData != null);
            }
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
                MarkSquadRosterDirty(profile?.AccountId);
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
            HashSet<string> tacticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> tacticValueByDropdownId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

            List<dropDownItem> tacticItems = [];
            dropDownItem currentTactic = null;
            IEnumerable<FriendlyTeammateTacticOption> availableTactics =
                options.Tactics != null && options.Tactics.Count > 0
                    ? options.Tactics
                    : new[]
                    {
                        new FriendlyTeammateTacticOption { Id = "Balanced", Name = "Balanced" },
                        new FriendlyTeammateTacticOption { Id = "Marksman", Name = "Marksman" },
                        new FriendlyTeammateTacticOption { Id = "Protector", Name = "Protector" },
                    };

            int tacticIdSeed = 0;
            string currentTacticValue = options.CurrentTactic?.Trim() ?? string.Empty;

            foreach (FriendlyTeammateTacticOption tactic in availableTactics)
            {
                if (string.IsNullOrWhiteSpace(tactic?.Id))
                {
                    continue;
                }

                string tacticValue = string.IsNullOrWhiteSpace(tactic.Name)
                    ? tactic.Id
                    : tactic.Name;

                // Dropdown item IDs must be MongoID-compatible (24 hex chars) in this profile UI path.
                string dropdownId = $"11111111111111111111111{(tacticIdSeed++ % 16):x1}";

                FriendlyProfileDropdownItem tacticItem = new FriendlyProfileDropdownItem
                {
                    Id = dropdownId,
                    Name = GetTacticDisplayName(tacticValue)
                };

                CustomDropdownIds.Add(tacticItem.Id);
                tacticIds.Add(tacticItem.Id);
                tacticValueByDropdownId[tacticItem.Id] = tacticValue;
                tacticItems.Add(tacticItem);

                if (string.Equals(tactic.Id, currentTacticValue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tacticValue, currentTacticValue, StringComparison.OrdinalIgnoreCase))
                {
                    currentTactic = tacticItem;
                }
            }

            currentTactic ??= tacticItems.FirstOrDefault();
            if (currentTactic == null)
            {
                return;
            }

            panel.Show(loadoutItems, currentLoadout, tacticItems, currentTactic, false, selected =>
            {
                if (selected == null)
                {
                    return;
                }

                if (loadoutIds.Contains(selected.Id))
                {
                    try
                    {
                        string responseJson = RequestHandler.PostJson(LoadoutRoute, SerializeBody(new FriendlyTeammateLoadoutRequest
                        {
                            aid = profile.AccountId,
                            loadoutId = selected.Id
                        }));
                        EnsureBodySuccess(responseJson);

                        MarkSquadRosterDirty(profile?.AccountId);
                        RefreshPlayerVisualization(profile, inventoryController, session, window);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError("[UI] Failed to persist teammate loadout change.");
                        Modules.Logger.LogError(ex);
                    }

                    return;
                }

                if (tacticIds.Contains(selected.Id))
                {
                    try
                    {
                        if (!tacticValueByDropdownId.TryGetValue(selected.Id, out string tacticValue) || string.IsNullOrWhiteSpace(tacticValue))
                        {
                            tacticValue = "Balanced";
                        }

                        string responseJson = RequestHandler.PostJson(TacticRoute, SerializeBody(new FriendlyTeammateTacticRequest
                        {
                            aid = profile.AccountId,
                            tactic = tacticValue
                        }));
                        EnsureBodySuccess(responseJson);
                        Modules.Logger.LogInfo($"[UI] Persisted teammate tactic '{tacticValue}' for '{profile.AccountId}'.");

                        SetAggressionRowMarksmanState(IsMarksmanTactic(tacticValue));
                        MarkSquadRosterDirty(profile?.AccountId);
                        RefreshPlayerVisualization(profile, inventoryController, session, window);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError("[UI] Failed to persist teammate tactic change.");
                        Modules.Logger.LogError(ex);
                    }
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
            OtherPlayerProfileScreenPatch.ActiveProfileInventoryController = null;
            OtherPlayerProfileScreenPatch.ActiveProfileSession = null;
            OtherPlayerProfileScreenPatch.CustomDropdownIds.Clear();
            OtherPlayerProfileScreenPatch.StopPendingAggressionPersist();
            OtherPlayerProfileScreenPatch.CloseRenameOverlay();
            OtherPlayerProfileScreenPatch.RestoreFactionBadgePosition();

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

            if (OtherPlayerProfileScreenPatch.AggressionSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.AggressionSelector.gameObject);
                OtherPlayerProfileScreenPatch.AggressionSelector = null;
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
