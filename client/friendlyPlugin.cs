using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using Newtonsoft.Json;

using friendlySAIN.Modules;
using friendlySAIN.BigBrain;
using friendlySAIN.Components;
using friendlySAIN.Localization;
using friendlySAIN.Utils;
using friendlySAIN.Patches;

namespace friendlySAIN
{

    public enum CustomBotRequestType
    {
        TakeLoot = 20,
        Regroup = 30,
        OverThere = 40,
        NeedHelp = 50
    }

    public enum CustomBotDecisions
    {
        SniperSearch = 100,
        CoverToCover = 101,
        EnemySearch = 102,
        MoveToPoint = 103,
        RunToCover = 104,
        GuardToCover = 105,
        FollowBoss = 106,
        attackRetreat = 107,

        DogFight = 108,
    }

    public enum CustomPhrases
    {
        TeamStatus = 10001,
        OverThere = 10002,
    }

    public enum CustomGestures
    {
        OverThere = 220,
    }

    public class LanguageOptions
    {
        public string baseSettings { get; set; }
        public string inputSettings { get; set; }
        public string miscSettings { get; set; }
        public string testSettings { get; set; }
        public string raidSettings { get; set; }
        public string[] equipOptions { get; set; }
        public string[] tacticOptions { get; set; }

        public Dictionary<string, string> statusSound { get; set; }
        public Dictionary<string, string> enemyMarker { get; set; }
        public Dictionary<string, string> scanDistance { get; set; }
        public Dictionary<string, string> enemyRemember { get; set; }
        public Dictionary<string, string> healthMultiplier { get; set; }
        public Dictionary<string, string> pickup { get; set; }
        public Dictionary<string, string> tieredPickup { get; set; }
        public Dictionary<string, string> maximumPickup { get; set; }
        public Dictionary<string, string> recruitPickup { get; set; }

        public Dictionary<string, string> memberTactic { get; set; }
        public Dictionary<string, string> memberEquipment { get; set; }
        public Dictionary<string, string> memberName { get; set; }
        public Dictionary<string, string> memberVoice { get; set; }
        public Dictionary<string, string> memberUniformTop { get; set; }
        public Dictionary<string, string> memberUniformBottom { get; set; }

        public Dictionary<string, string> equipmentLock { get; set; }
        public Dictionary<string, string> npcSendMessage { get; set; }

        [JsonProperty("friendlyPMC")]
        public Dictionary<string, string> friendlySAIN { get; set; }
        public Dictionary<string, string> badGuy { get; set; }
        public Dictionary<string, string> pmcArmbands { get; set; }
        public Dictionary<string, string> englishBear { get; set; }

        public Dictionary<string, string> pingSquad { get; set; }
        public Dictionary<string, string> pingRadioVolume { get; set; }
        public Dictionary<string, string> pingTime { get; set; }
        public Dictionary<string, string> enemyContact { get; set; }
        public Dictionary<string, string> overThere { get; set; }

        public Dictionary<string, string> gestures { get; set; }

        public Dictionary<string, string> botStatus { get; set; }
        public Dictionary<string, string> socialUi { get; set; }

        public Dictionary<string, string> patrolRadius { get; set; }

        public Dictionary<string, string> botTeleport { get; set; }
        public Dictionary<string, string> botHeal { get; set; }

        public Dictionary<string, string> botPrefetch { get; set; }

        public Dictionary<string, string> botGrenades { get; set; }

        public Dictionary<string, string> botTalk { get; set; }

        public Dictionary<string, string> spawnPoint { get; set; }

        // used only by BE
        public string[] returnItems { get; set; }
        public string[] returnItemsDeath { get; set; }
        public string[] teamEscaped { get; set; }
        public string[] teamSomeEscaped { get; set; }
        public string[] friendlyEscaped { get; set; }
    }

    [BepInPlugin("xyz.pit.friendlysain", "friendlySAIN", "1.0.0")]
    [BepInDependency("xyz.drakia.bigbrain")]
    public class friendlySAIN : BaseUnityPlugin
    {
        public const string SainPluginId = "me.sol.sain";
        public const string SainAddonPluginId = "xyz.pit.friendlysain.sainaddon";

        public static bool awaken;

        internal static friendlySAIN Instance { get; private set; }

        public static LanguageOptions optionsLang;

        public static ConfigEntry<int> enemyRemember;

        public static ConfigEntry<int> heatlhMultiplier;

        public static ConfigEntry<int> scanDistance;

        public static ConfigEntry<int> statusSound;
        public static ConfigEntry<bool> enemyMarker;
        public static ConfigEntry<bool> npcSendMessage;
        public static ConfigEntry<bool> pickupEnabled;
        public static ConfigEntry<bool> tieredPickup;
        public static ConfigEntry<int> maximumPickup;
        public static ConfigEntry<bool> recruitPickup;

        public static ConfigEntry<bool> friendlySAINFLAG;
        public static ConfigEntry<bool> badGuy;

        public static ConfigEntry<bool> englishBear;

        public static ConfigEntry<bool> botPrefetch;

        public static ConfigEntry<bool> botGrenades;

        public static ConfigEntry<int> botTalk;

        public static ConfigEntry<int> patrolRadius;

        public static ConfigEntry<bool> spawnPoint;

        public static ConfigEntry<KeyboardShortcut> pingKey;
        public static ConfigEntry<int> pingRadioVolume;
        public static ConfigEntry<int> pingTime;

        public static ConfigEntry<KeyboardShortcut> contactKey;
        public static ConfigEntry<KeyboardShortcut> overThereKey;

        public static ConfigEntry<KeyboardShortcut> teleportKey;
        public static ConfigEntry<KeyboardShortcut> healKey;
        public static TarkovApplication application;

        private static Dictionary<ConfigDefinition, string> savedConfigValues;

        public static ManualLogSource Log => Instance.Logger;

        public static bool IsSAINInstalled { get; private set; }
        public static bool IsSAINAddonInstalled { get; private set; }
        // Temporary test mode: allow SAIN regroup layer to handle regroup even out of combat.
        // Final behavior should set this to false so SAIN regroup only handles combat regroup.
        public static bool EnableSainRegroupOutOfCombatTest { get; } = false;

        public static bool UseSainFollowerCombat => IsSAINInstalled && IsSAINAddonInstalled;
        public static bool HasSainRegroupAddon => UseSainFollowerCombat;
        public static bool ShouldDisableSainForFollowers => IsSAINInstalled && !UseSainFollowerCombat;

        private void Awake()
        {

            if (!awaken)
            {
                awaken = true;
                Instance = this;
                new Modules.Logger();
                RefreshPluginFlags();
            }

            // initialize follower brain layers
            FollowerLayerRegistry.Init();

            var harmony = new Harmony("xyz.pit.friendlysain");

            // bot patches to help with various scenarios while being a follower of the player
            // Temporarily disabled for 4.x stability; revisit once BotsGroup method signatures are remapped.
            new BotGroupAddEnemyPatch().Enable();
            new BotGroupReportEnemyPatch().Enable();
            new BotGroupUsecEnemyPatch().Enable();
            new BotGroupCalcGoalPatch().Enable();
            new BotControllerEnemyPropagationSafetyPatch().Enable();

            new BotMemoryDamagePatch().Enable();
            new ExUsecBrainHitPatch().Enable();

            new BotOwnerIsFolowerPatch().Enable();
            new BotOwnerManualUpdatePatch().Enable();
            new BotOwnerActivatePatch().Enable();
            new SessionLoadBotsEnglishVoicePatch().Enable();
            new LootPatrolActiveLayerListPatch().Enable();
            new LootPatrolDecisionBypassPatch().Enable();
            new AdvAssaultTargetFollowerGuardPatch().Enable();
            new PatrolDataFollowerUpdateGuardPatch().Enable();
            new AvoidDangerFollowerGuardPatch().Enable();
            new PmcBearCombatLayerSuppressionPatch().Enable();
            new PmcUsecCombatLayerSuppressionPatch().Enable();
            new PmcFlankCombatLayerSuppressionPatch().Enable();

            if (!IsSAINInstalled)
                new FollowerSprintPatch().Enable();
            new FollowerSprintStateDirectionPatch().Enable();

            new AICoreAgentUpdatePatch().Enable();

            // recruit/request patches
            new BotReceiverFollowMeRecruitPatch().Enable();
            new FollowRequestPatch().Enable();
            new HoldRequestPatch().Enable();
            new OpenDoorRequestPatch().Enable();


            // spawn patches
            new BotsControllerPatch().Enable();
            new BotsControllerStopPatch().Enable();
            new LocalGameCleanupPatch().Enable();

            // Only patch LocalGame ctor here; avoid broad PatchAll side effects at menu/hideout time.
            harmony.CreateClassProcessor(typeof(LocalGameCtorPatch)).Patch();
            new BotsEventsControllerSpawnPatch().Enable();
            new BossSpawnWaveManagerClassPatch().Enable();

            // patch bot equipment to prevent looting companions
            new UnlootableComponentPatch().Enable();
            new ModRaidModdablePatch().Enable();
            new ItemSpecificationPanelPatch().Enable();

            // bot misc patches
            new BotTalkTrySayPatch().Enable();
            new BotTalkSayPatch().Enable();
            new GrenadeThrowPatch().Enable();
            new BulletImpactPatch().Enable();
            new HearingSensorPatch().Enable();
            new FootstepSoundPatch().Enable();
            new PlayerSayPatch().Enable();
            new PlayerKilledPatch().Enable();
            new PlayerShotPatch().Enable();
            new AddTeammateBackButtonPatch().Enable();
            new AddTeammateNicknameFieldEndEditPatch().Enable();
            new AddTeammateNicknameFieldInitPatch().Enable();
            new AddTeammateNicknameFieldStatusPatch().Enable();
            new AddTeammateNicknameValueChangedPatch().Enable();
            new AddTeammateFinishPatch().Enable();

            // AIBossPlayer class patch
            new AIDataContructPatch().Enable();

            // command/request patches
            new QuickPanelPatch().Enable();
            new GestureMenuPatch().Enable();
            new GestureMenuAvailablePhrasesPatch().Enable();
            new EPhraseTriggerPatch().Enable();
            new PlayPhraseOrGesturePatch().Enable();
            new BotReceiverGestureOverridePatch().Enable();
            new RaidStartPatch().Enable();
            new MainMenuControllerPatch().Enable();
            new MainMenuControllerReadyScreenGatePatch().Enable();
            new TarkovApplicationLocalRaidGatePatch().Enable();
            new TarkovApplicationOnlineFallbackPatch().Enable();
            new MatchmakerPlayerControllerClassAddMemberPatch().Enable();
            new MatchmakerPlayerControllerClassDisbandGroupPatch().Enable();
            new MatchmakerPlayerControllerClassAbortPatch().Enable();
            new MatchmakerPlayerControllerClassLeavePatch().Enable();
            new MatchMakerAcceptScreenPatch().Enable();
            new MatchMakerPlayerPreviewFollowerUiPatch().Enable();
            new ContextInteractionsPlayerRemovePatch().Enable();
            new TransitPointPatch().Enable();
            new MatchMakerSelectionLocationScreenPatch().Enable();
            new SelectSpawnPointPatch().Enable();

            new MatchmakerTimeHasComeShowPatch().Enable();
            new MenuScreenShowSquadControlPatch().Enable();
            new MenuScreenReconnectVisibilitySquadControlPatch().Enable();
            new MenuScreenMinimizedVisibilitySquadControlPatch().Enable();
            new MatchMakerSideSelectionScreenShowPatch().Enable();
            new MatchMakerSideSelectionScreenClosePatch().Enable();
            new MainMenuControllerOpenSideSelectionGuardPatch().Enable();
            new CurrentScreenTryReturnToRootScreenPatch().Enable();
            new PlayerModelViewShowProfilePatch().Enable();
            new PlayerModelViewShowLastStatePatch().Enable();

            //new ConditionCounterPatch().Enable();

            // social / teammate management patches
            new SocialNetworkClassPatch().Enable();
            new FriendListInvitePlayerPanelPatch().Enable();
            new TeammateContextMenuButtonsPatch().Enable();
            new TeammateGroupContextMenuButtonsPatch().Enable();
            new ChatInvitePlayersPanelRefreshPatch().Enable();
            new ChatCreateDialoguePanelRefreshPatch().Enable();
            new ChatFriendsRequestsPanelRefreshPatch().Enable();
            // new SocialNetworkClassSendPatch().Enable();
            // new QuestClassPatch().Enable();

            // set configuration manager
            SetConfiguration();

            new FriendlyDropdownNamePatch().Enable();
            new OtherPlayerProfileScreenPatch().Enable();
            new OtherPlayerProfileScreenClosePatch().Enable();

            // SAIN/Donuts patches
            if (IsSAINInstalled)
            {
                SAINPatch.PatchSAINIfInstalled(harmony);
            }
        }

        private void Start()
        {
            RefreshPluginFlags();
            if (IsSAINInstalled && !IsSAINAddonInstalled)
            {
                Logger.LogWarning("[Init] SAIN detected but friendlySAIN SAIN addon is missing.");
                Logger.LogWarning($"[Init] Followers will fall back to core vanilla combat behavior. Install plugin '{SainAddonPluginId}' to enable SAIN follower combat.");
            }
        }

        private static void RefreshPluginFlags()
        {
            IsSAINInstalled = HasPlugin(SainPluginId);
            IsSAINAddonInstalled = HasPlugin(SainAddonPluginId);
        }

        public static bool ShouldUseSainRegroupRoute(bool isCombatRegroupContext)
        {
            if (!HasSainRegroupAddon)
            {
                return false;
            }

            return isCombatRegroupContext || EnableSainRegroupOutOfCombatTest;
        }

        public static bool ShouldSainRegroupLayerHandle(BotOwner? botOwner)
        {
            if (!HasSainRegroupAddon)
            {
                return false;
            }

            if (EnableSainRegroupOutOfCombatTest)
            {
                return true;
            }

            return botOwner?.Memory?.HaveEnemy == true;
        }

        private static bool HasPlugin(string pluginId)
        {
            var pluginInfos = BepInEx.Bootstrap.Chainloader.PluginInfos;
            if (pluginInfos.ContainsKey(pluginId))
            {
                return true;
            }

            return pluginInfos.Values.Any(p =>
                p?.Metadata != null &&
                string.Equals(p.Metadata.GUID, pluginId, StringComparison.OrdinalIgnoreCase));
        }


        private void GetLanguage()
        {
            // Temporary no-BE default. Replace with BE language route when backend is reintroduced.
            optionsLang = TempEnglishLanguageProvider.Create();
        }
        private void ConfigSet()
        {
            Config.SaveOnConfigSet = false;

            savedConfigValues = new Dictionary<ConfigDefinition, string>();

            var orphanedEntries = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries").GetValue(Config) as Dictionary<ConfigDefinition, string>;

            orphanedEntries.ExecuteForEach(it =>
            {
                savedConfigValues.Add(it.Key, it.Value);
            });

            scanDistance = Config.Bind("", "1 " + optionsLang.scanDistance["Name"], 140, new ConfigDescription(optionsLang.scanDistance["Description"], new AcceptableValueRange<int>(50, 300), new ConfigurationManagerAttributes { Order = -100, Browsable = false }));

            patrolRadius = Config.Bind("", "2 " + optionsLang.patrolRadius["Name"], 50, new ConfigDescription(optionsLang.patrolRadius["Description"], new AcceptableValueRange<int>(30, 100), new ConfigurationManagerAttributes { Order = -200, Browsable = false }));

            enemyRemember = Config.Bind("", "3 " + optionsLang.enemyRemember["Name"], 20, new ConfigDescription(optionsLang.enemyRemember["Description"], new AcceptableValueRange<int>(5, 60), new ConfigurationManagerAttributes { Order = -300, Browsable = false }));

            heatlhMultiplier = Config.Bind("", "4 " + optionsLang.healthMultiplier["Name"], 1, new ConfigDescription(optionsLang.healthMultiplier["Description"], new AcceptableValueRange<int>(1, 10), new ConfigurationManagerAttributes { Order = -400, Browsable = false }));

            statusSound = Config.Bind("", "5 " + optionsLang.statusSound["Name"], 100, new ConfigDescription(optionsLang.statusSound["Description"], new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { Order = -500, Browsable = false }));

            enemyMarker = Config.Bind("", "6 " + optionsLang.enemyMarker["Name"], true, new ConfigDescription(optionsLang.enemyMarker["Description"], null, new ConfigurationManagerAttributes { Order = -600, Browsable = false }));

            pickupEnabled = Config.Bind("", "7 " + optionsLang.pickup["Name"], true, new ConfigDescription(optionsLang.pickup["Description"], null, new ConfigurationManagerAttributes { Order = -700, Browsable = false }));

            tieredPickup = Config.Bind("", "8 " + optionsLang.tieredPickup["Name"], true, new ConfigDescription(optionsLang.tieredPickup["Description"], null, new ConfigurationManagerAttributes { Order = -800, Browsable = false }));

            maximumPickup = Config.Bind("", "9 " + optionsLang.maximumPickup["Name"], 2, new ConfigDescription(optionsLang.maximumPickup["Description"], new AcceptableValueRange<int>(0, 10), new ConfigurationManagerAttributes { Order = -900, Browsable = false }));

            recruitPickup = Config.Bind("", "10 " + optionsLang.recruitPickup["Name"], true, new ConfigDescription(optionsLang.recruitPickup["Description"], null, new ConfigurationManagerAttributes { Order = -1000, Browsable = false }));

            npcSendMessage = Config.Bind("", "11 " + optionsLang.npcSendMessage["Name"], true, new ConfigDescription(optionsLang.npcSendMessage["Description"], null, new ConfigurationManagerAttributes { Order = -1001, Browsable = false }));

            friendlySAINFLAG = Config.Bind("", "12 " + optionsLang.friendlySAIN["Name"], true, new ConfigDescription(optionsLang.friendlySAIN["Description"], null, new ConfigurationManagerAttributes { Order = -1002, Browsable = false }));

            badGuy = Config.Bind("", "13 " + optionsLang.badGuy["Name"], false, new ConfigDescription(optionsLang.badGuy["Description"], null, new ConfigurationManagerAttributes { Order = -1003, Browsable = false }));

            englishBear = Config.Bind("", "14 " + optionsLang.englishBear["Name"], true, new ConfigDescription(optionsLang.englishBear["Description"], null, new ConfigurationManagerAttributes { Order = -1004, Browsable = false }));

            botGrenades = Config.Bind("", "15 " + optionsLang.botGrenades["Name"], true, new ConfigDescription(optionsLang.botGrenades["Description"], null, new ConfigurationManagerAttributes { Order = -1005, Browsable = false }));

            pingKey = Config.Bind("", "16 " + optionsLang.pingSquad["Name"], new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.pingSquad["Description"], null, new ConfigurationManagerAttributes { Order = -1006, Browsable = false }));

            pingRadioVolume = Config.Bind("", "17 " + optionsLang.pingRadioVolume["Name"], 50, new ConfigDescription(optionsLang.pingRadioVolume["Description"], new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { Order = -1007, Browsable = false }));

            pingTime = Config.Bind("", "18 " + optionsLang.pingTime["Name"], 5, new ConfigDescription(optionsLang.pingTime["Description"], new AcceptableValueRange<int>(5, 30), new ConfigurationManagerAttributes { Order = -1008, Browsable = false }));

            contactKey = Config.Bind("", "19 " + optionsLang.enemyContact["Name"], new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.enemyContact["Description"], null, new ConfigurationManagerAttributes { Order = -1009, Browsable = false }));

            overThereKey = Config.Bind("", "20 " + optionsLang.overThere["Name"], new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.overThere["Description"], null, new ConfigurationManagerAttributes { Order = -1010, Browsable = false }));

            teleportKey = Config.Bind("", "21 " + optionsLang.botTeleport["Name"], new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.botTeleport["Description"], null, new ConfigurationManagerAttributes { Order = -1011, Browsable = false }));
            healKey = Config.Bind("", "22 " + optionsLang.botHeal["Name"], new KeyboardShortcut(KeyCode.None), new ConfigDescription(optionsLang.botHeal["Description"], null, new ConfigurationManagerAttributes { Order = -1012, Browsable = false }));

            botPrefetch = Config.Bind("", "23 " + optionsLang.botPrefetch["Name"], true, new ConfigDescription(optionsLang.botPrefetch["Description"], null, new ConfigurationManagerAttributes { Order = -1013, Browsable = false }));

            botTalk = Config.Bind("", "24 " + optionsLang.botTalk["Name"], 100, new ConfigDescription(optionsLang.botTalk["Description"], new AcceptableValueRange<int>(0, 100), new ConfigurationManagerAttributes { Order = -1014, Browsable = false }));

            spawnPoint = Config.Bind("", "25 " + optionsLang.spawnPoint["Name"], true, new ConfigDescription(optionsLang.spawnPoint["Description"], null, new ConfigurationManagerAttributes { Order = -1015, Browsable = false }));

            Config.SaveOnConfigSet = true;
            Config.Save();
        }
        private void _BotTeleport()
        {
            Modules.Logger.LogInfo("Followers teleport");

            if (GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            string id = GamePlayerOwner.MyPlayer.ProfileId;

            if (BossPlayers.Instance != null)
            {
                var followers = BossPlayers.GetFollowersByBoss(id);
                Vector3 position = GamePlayerOwner.MyPlayer.Transform.position;
                List<Vector3> reservedSpots = new List<Vector3> { position };
                int followerIndex = 0;
                foreach (var follower in followers)
                {
                    if (follower != null && follower.GetBot().HealthController.IsAlive && !follower.GetBot().DoorOpener.Interacting)
                    {
                        BotOwner bot = follower.GetBot();
                        Vector3 target = FindTeleportSpot(position, reservedSpots, followerIndex);
                        followerIndex++;

                        follower.BeginTeleportGrace(target);
                        bot.Mover.Stop();
                        bot.GetPlayer.Teleport(target);
                        reservedSpots.Add(target);
                    }
                }
            }
        }

        private static Vector3 FindTeleportSpot(Vector3 playerPosition, List<Vector3> reservedSpots, int index)
        {
            const float minDistanceToPlayer = 2f;
            const float minDistanceBetweenFollowers = 1.6f;
            const float sampleRadius = 1.4f;
            const float baseRadius = 2.25f;
            const float ringStep = 1.4f;

            float seedAngle = UnityEngine.Random.Range(0f, 360f);

            for (int attempt = 0; attempt < 24; attempt++)
            {
                int ring = attempt / 8;
                float radius = baseRadius + ring * ringStep;
                float angle = seedAngle + (index * 67f) + (attempt * 45f);
                float radians = angle * Mathf.Deg2Rad;
                Vector3 candidate = playerPosition + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * radius;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                {
                    if ((hit.position - playerPosition).sqrMagnitude < minDistanceToPlayer * minDistanceToPlayer)
                    {
                        continue;
                    }

                    bool overlaps = false;
                    foreach (Vector3 spot in reservedSpots)
                    {
                        if ((hit.position - spot).sqrMagnitude < minDistanceBetweenFollowers * minDistanceBetweenFollowers)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (!overlaps)
                    {
                        return hit.position;
                    }
                }
            }

            // Fallback near player if no valid spread point is found.
            if (NavMesh.SamplePosition(playerPosition, out NavMeshHit fallbackHit, 1.2f, NavMesh.AllAreas))
            {
                return fallbackHit.position;
            }
            return playerPosition;
        }

        private void _BotHeal()
        {
            Modules.Logger.LogInfo("Followers fix heal");

            if (GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            string id = GamePlayerOwner.MyPlayer.ProfileId;

            if (BossPlayers.Instance != null)
            {
                var followers = BossPlayers.GetFollowersByBoss(id);

                foreach (var follower in followers)
                {
                    var bot = follower.GetBot();
                    var player = bot.GetPlayer;

                    if (follower != null && bot.HealthController.IsAlive)
                    {
                        foreach (var part in GClass3058.RealBodyParts)
                        {
                            if (player.ActiveHealthController.IsBodyPartBroken(part)) player.ActiveHealthController.RemoveNegativeEffects(part);
                            if (player.ActiveHealthController.IsBodyPartDestroyed(part)) player.ActiveHealthController.RestoreBodyPart(part, 0);
                        }

                        bot.AIData.Player.ActiveHealthController.RestoreFullHealth();

                        bot.WeaponManager.Selector.TakePrevWeapon();

                        if (bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
                        {
                            bot.WeaponManager.Selector.TryChangeToMain();
                        }
                    }
                }
            }

        }

        private void SetConfiguration()
        {
            // - get config language
            GetLanguage();
            // - set config
            ConfigSet();
            RegisterDebugConsoleCommands();
        }

        private void RegisterDebugConsoleCommands()
        {
            try
            {
                Type consoleType = AccessTools.TypeByName("EFT.UI.ConsoleScreen")
                    ?? AccessTools.TypeByName("ConsoleScreen");
                if (consoleType == null)
                {
                    Modules.Logger.LogError("ConsoleScreen type not found; fs_spawnfollower not registered.");
                    return;
                }

                FieldInfo processorField = AccessTools.Field(consoleType, "Processor");
                object processor = processorField?.GetValue(null);
                if (processor == null)
                {
                    PropertyInfo processorProperty = AccessTools.Property(consoleType, "Processor");
                    processor = processorProperty?.GetValue(null);
                }
                if (processor == null)
                {
                    Modules.Logger.LogError("ConsoleScreen.Processor not found; fs_spawnfollower not registered.");
                    return;
                }

                MethodInfo registerWithDescription = null;
                MethodInfo registerSimple = null;
                MethodInfo[] registerCandidates = processor.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo method in registerCandidates)
                {
                    if (!string.Equals(method.Name, "RegisterCommand", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 3
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(Action)
                        && parameters[2].ParameterType == typeof(string))
                    {
                        registerWithDescription = method;
                    }
                    else if (parameters.Length == 2
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(Action))
                    {
                        registerSimple = method;
                    }
                }

                if (registerWithDescription != null)
                {
                    registerWithDescription.Invoke(
                        processor,
                        new object[] { "fs_spawnfollower", (Action)SpawnFollowerConsoleCommand, "Spawn a follower bot near the player" });
                    Modules.Logger.LogInfo("Registered console command: fs_spawnfollower");
                    return;
                }

                if (registerSimple != null)
                {
                    registerSimple.Invoke(processor, new object[] { "fs_spawnfollower", (Action)SpawnFollowerConsoleCommand });
                    Modules.Logger.LogInfo("Registered console command: fs_spawnfollower");
                    return;
                }

                Modules.Logger.LogError("RegisterCommand overload not found; fs_spawnfollower not registered.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to register fs_spawnfollower console command");
                Modules.Logger.LogError(ex);
            }
        }

        private static void SpawnFollowerConsoleCommand()
        {
            _ = SpawnFollowerConsoleCommandAsync();
        }

        private static async Task SpawnFollowerConsoleCommandAsync()
        {
            if (!Singleton<AbstractGame>.Instantiated || Singleton<GameWorld>.Instance == null || GamePlayerOwner.MyPlayer == null)
            {
                DebugConsoleLog("fs_spawnfollower: this command can only be used in-raid.", true);
                return;
            }

            Player player = GamePlayerOwner.MyPlayer;
            EPlayerSide spawnSide = player.Side;

            if (BossPlayers.Instance == null)
            {
                DebugConsoleLog("fs_spawnfollower: boss system is not initialized.", true);
                return;
            }

            pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);
            if (boss == null)
            {
                if (BotsControllerPatch.Controller == null)
                {
                    DebugConsoleLog("fs_spawnfollower: bots controller is not ready.", true);
                    return;
                }

                boss = BossPlayers.AddPlayerAsBoss(player, BotsControllerPatch.Controller);
                if (boss == null)
                {
                    DebugConsoleLog("fs_spawnfollower: failed to initialize player boss.", true);
                    return;
                }
            }

            if (BotsControllerPatch.Instance == null)
            {
                DebugConsoleLog("fs_spawnfollower: bots patch instance is missing.", true);
                return;
            }

            try
            {
                string spawnFailureReason = null;
                bool result = await BotsControllerPatch.Instance.SpawnDebugFollower(
                    boss,
                    spawnSide,
                    reason => spawnFailureReason = reason
                );
                if (result)
                {
                    DebugConsoleLog($"fs_spawnfollower: spawn requested ({spawnSide}).", false);
                }
                else
                {
                    string details = string.IsNullOrEmpty(spawnFailureReason) ? "unknown reason" : spawnFailureReason;
                    DebugConsoleLog($"fs_spawnfollower: spawn request failed ({details}).", true);
                }
            }
            catch (Exception ex)
            {
                DebugConsoleLog($"fs_spawnfollower: exception: {ex.Message}", true);
                Modules.Logger.LogError(ex);
            }
        }

        private static void DebugConsoleLog(string message, bool isError)
        {
            try
            {
                Type consoleType = AccessTools.TypeByName("EFT.UI.ConsoleScreen")
                    ?? AccessTools.TypeByName("ConsoleScreen");
                if (consoleType != null)
                {
                    MethodInfo logMethod = AccessTools.Method(consoleType, isError ? "LogError" : "Log", new[] { typeof(string) });
                    logMethod?.Invoke(null, new object[] { message });
                }
            }
            catch
            {
                // ignore console reflection errors
            }

            if (isError) Modules.Logger.LogError(message);
            else Modules.Logger.LogInfo(message);
        }

        void Update()
        {
            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            if (GamePlayerOwner.MyPlayer == null || GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
            {
                return;
            }

            if (pingKey.Value.IsUp() || contactKey.Value.IsUp() || overThereKey.Value.IsUp())
            {

                string id = GamePlayerOwner.MyPlayer.ProfileId;

                if (BossPlayers.Instance != null && PingTeamates.Instance != null)
                {
                    var boss = BossPlayers.Instance.GetBossPlayer(id);
                    if (boss != null)
                    {
                        if (pingKey.Value.IsUp())
                            boss.PhraseSaid(new BotEventHandler.GClass692
                            {
                                phrase = (EPhraseTrigger)CustomPhrases.TeamStatus,
                                PlayerRequester = boss.realPlayer
                            });
                        else if (overThereKey.Value.IsUp())
                            boss.realPlayer.Say((EPhraseTrigger)CustomPhrases.OverThere, true);
                        else
                            boss.realPlayer.Say(EPhraseTrigger.OnRepeatedContact, true);
                    }
                }
            }

            else if (teleportKey.Value.IsUp())
            {
                _BotTeleport();
            }

            else if (healKey.Value.IsUp())
            {
                _BotHeal();
            }

        }
    }
}
