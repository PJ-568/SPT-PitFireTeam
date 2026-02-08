using EFT;
using EFT.Game.Spawning;
using EFT.UI.Matchmaker;
using friendlySAIN.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using playerGroup = System.Collections.Generic.List<GroupPlayerViewModelClass>;

namespace friendlySAIN.Patches
{
    /**
     * Patch to set what followers will the player start with (PMC case only)
     */
    internal class RaidStartPatch : ModulePatch
    {
        public static bool HasFika()
        {
            return Type.GetType("Fika.Core.Coop.GameMode.CoopGame, Fika.Core") != null;
        }
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class308), "SendRaidSettings");
        }
        [PatchPostfix]
        private static void PatchPostfix(Class308 __instance, RaidSettings settings)
        {
            bool badGuy = friendlySAIN.badGuy.Value;

            Utils.SpawnHelper.spawnMemberIds.Clear();
            Utils.SpawnHelper.spawnMemberIdsScav.Clear();
            Utils.SpawnHelper.spawnMemberIdsBoss.Clear();
            // has members selected for spawn
            if (MainMenuControllerPatch.GroupPlayers != null)
            {
                foreach (var player in MainMenuControllerPatch.GroupPlayers)
                {
                    if (player.Id == "677c4e0cc7a538c4210d4d47")
                    {
                        Utils.SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.bossKnight);
                    }
                    else if (player.Id == "677c4e0cc7a538c4210d4d48")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBigPipe);
                    }
                    else if (player.Id == "677c4e0cc7a538c4210d4d49")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBirdEye);
                    }
                    else
                    {
                        if (!settings.IsPmc)
                        {
                            if (Utils.SpawnHelper.ScavSquad) Utils.SpawnHelper.spawnMemberIdsScav.AddRange(MainMenuControllerPatch.GroupPlayers.Select(x => x.AccountId));
                        }
                        else
                            Utils.SpawnHelper.spawnMemberIds.AddRange(MainMenuControllerPatch.GroupPlayers.Select(x => x.AccountId));

                        break;
                    }
                }
            }

            // spawning with a Goon will turn bad guy flag on
            if (SpawnHelper.spawnMemberIdsBoss.Count > 0)
            {
                badGuy = true;
                Utils.Utils.FlagSet("isBadGuy", true);
            }

            Profile profile = __instance.GetProfileBySide(ESideType.Pmc);

            // see if user is to spawn with a Goon do to questing
            List<string> questCompanions = new List<string>();

            if (profile.TryGetTraderInfo(Utils.Props.KnightTrader, out var traderInfo) && !traderInfo.Disabled)
            {
                profile.QuestsData.ForEach(quest =>
                {

                    foreach (var item in Utils.Props.Quests)
                    {
                        foreach (var item1 in item.Value)
                        {
                            if (item1 == quest.Id)
                            {
                                if (quest.Status == EFT.Quests.EQuestStatus.Started)
                                {
                                    bool isGoonQuest = true;

                                    if (Utils.Props.QuestsLocations.TryGetValue(quest.Id, out List<string> locations))
                                    {
                                        isGoonQuest = false;
                                        if (locations.Contains(settings.LocationId.ToLower()))
                                        {
                                            isGoonQuest = true;
                                        }
                                    }

                                    if (Utils.SpawnHelper.spawnMemberIds.Count < 1 && isGoonQuest)
                                    {
                                        Utils.Utils.FlagSet("questGoons", true);
                                        // - when doing Goons quests, we reset any other companions
                                        SpawnHelper.spawnMemberIdsBoss.Clear();
                                        // - when doing Goons quests, we are always bad guys
                                        Utils.Utils.FlagSet("isBadGuy", true);
                                        badGuy = true;

                                        if (item.Key == "Knight")
                                        {
                                            if (!questCompanions.Contains("bossKnight"))
                                            {
                                                questCompanions.Add("bossKnight");
                                            }
                                        }
                                        else if (item.Key == "BigPipe")
                                        {
                                            if (!questCompanions.Contains("followerBigPipe"))
                                            {
                                                questCompanions.Add("followerBigPipe");
                                            }
                                        }
                                        else if (item.Key == "BirdEye")
                                        {
                                            if (!questCompanions.Contains("followerBirdEye"))
                                            {
                                                questCompanions.Add("followerBirdEye");
                                            }
                                        }

                                        break;
                                    }
                                }
                            }
                        }
                    }
                });
            }

            if (questCompanions.Count > 0)
            {
                questCompanions.ForEach(companion =>
                {
                    if (companion == "bossKnight")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.bossKnight);
                    }
                    else if (companion == "followerBigPipe")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBigPipe);
                    }
                    else if (companion == "followerBirdEye")
                    {
                        SpawnHelper.spawnMemberIdsBoss.Add(WildSpawnType.followerBirdEye);
                    }

                });
            }

            // patch raid settings to that we can change the settings without restarting the game
            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            string pitConfig = RequestHandler.PostJson("/client/raid/pitconfig", new
            {
                Config = new Dictionary<string, object>
                {
                    { "friendlySAIN", friendlySAIN.friendlySAINFLAG.Value },
                    { "badGuy", badGuy },
                    { "pmcArmbands", friendlySAIN.pmcArmbands.Value },
                    { "englishBear", friendlySAIN.englishBear.Value },
                    { "location", settings.LocationId }
                }

            }.ToJson(_defaultJsonConverters));

            PitConfig config = Json.Deserialize<PitConfig>(pitConfig);

            Utils.SpawnHelper.ScavSquad = config.ScavSquad;
            Utils.SpawnHelper.ScavSquadSize = config.ScavSquadSize;
            Utils.SpawnHelper.Pickups = config.Pickups;
            Utils.SpawnHelper.Restrictions = config.Restrictions;

            // - when restrictions are enabled, maxium scav squad size will be determined based on fence standing
            if (config.Restrictions)
            {
                double fenceStanding = profile.FenceInfo.Standing;
                int minScavSize = 1;
                int maxScavSize = 10;
                int inputMaxScavSize = Utils.SpawnHelper.ScavSquadSize;
                double minStanding = 1.0;
                double maxStanding = 6.0;

                if (fenceStanding < minStanding) Utils.SpawnHelper.ScavSquadSize = 0;
                else
                {
                    double standingRange = maxStanding - minStanding;
                    double scavSizeRange = maxScavSize - minScavSize;
                    double standingRatio = (fenceStanding - minStanding) / standingRange;
                    double scavSize = minScavSize + (standingRatio * scavSizeRange);

                    int scavSquadSize = (int)Math.Round(scavSize, 0);
                    if (scavSquadSize > inputMaxScavSize)
                    {
                        Utils.SpawnHelper.ScavSquadSize = inputMaxScavSize;
                    }
                    else
                    {
                        Utils.SpawnHelper.ScavSquadSize = inputMaxScavSize;
                    }
                }
            }

            if (Utils.SpawnHelper.ScavSquadSize < 1) Utils.SpawnHelper.ScavSquad = false;

            if (friendlySAIN.badGuy.Value) Utils.Utils.FlagSet("isBadGuy", true);
            if (friendlySAIN.friendlySAINFLAG.Value) Utils.Utils.FlagSet("friendlySAIN", true);
        }
    }
    /**
     * Ensure the game does not see player having a group which would switch the game mode to Online - position #1
     */
    internal class MainMenuControllerPatch : ModulePatch
    {
        public static playerGroup GroupPlayers = new playerGroup();
        public static readonly playerGroup TransitPlayers = new playerGroup();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MainMenuControllerClass), "method_49");
        }

        [PatchPrefix]
        private static void PatchPrefix(MainMenuControllerClass __instance)
        {

            MatchmakerPlayerControllerClass matchmakerPlayerControllerClass = __instance.MatchmakerPlayerControllerClass;

            var removeGroup = new playerGroup();

            foreach (var item in matchmakerPlayerControllerClass.GroupPlayers)
            {
                if (item != matchmakerPlayerControllerClass.CurrentPlayer)
                    removeGroup.Add(item);
            }

            foreach (var item in removeGroup)
            {
                matchmakerPlayerControllerClass.GroupPlayers.Remove(item);
            }

            RaidSettings raidSettings_0 = __instance.RaidSettings_0;
            raidSettings_0.RaidMode = ERaidMode.Local;

        }
    }
    /**
     * Stay in sync with what bots the player adds to the raid group
     */
    internal class MatchmakerPlayerControllerClassAddMemberPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3926<RaidSettings>), "method_39");
        }

        [PatchPostfix]
        private static void PatchPostfix(MatchmakerPlayerControllerClass __instance, GroupPlayerViewModelClass player)
        {
            if (__instance.CurrentPlayer != player && !MainMenuControllerPatch.GroupPlayers.Contains(player)) MainMenuControllerPatch.GroupPlayers.Add(player);
        }
    }
    /**
     * Clear the raid group when the player disbands the orignal group
     */
    internal class MatchmakerPlayerControllerClassDisbandGroupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3926<RaidSettings>), "method_21");
        }

        [PatchPrefix]
        private static void PatchPrefix(MatchmakerPlayerControllerClass __instance, bool revertSettings = true)
        {
            MainMenuControllerPatch.GroupPlayers.Clear();
        }
    }
    /**
     * Clear the raid group when the player aborts the matchmaking
     */
    internal class MatchmakerPlayerControllerClassAbortPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchmakerPlayerControllerClass), "MatchingAbort");
        }

        [PatchPrefix]
        private static void PatchPrefix(MatchmakerPlayerControllerClass __instance)
        {
            MainMenuControllerPatch.GroupPlayers.Clear();

            RequestHandler.GetJson("/client/raid/pitabort");
        }
    }
    /**
     * When leaving the match accept screen, ensure the original group is back in sync with the raid group
     */
    internal class MatchmakerPlayerControllerClassLeavePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerAcceptScreen), "method_25");
        }

        [PatchPrefix]
        private static void PatchPrefix(MatchMakerAcceptScreen __instance)
        {
            var controller = AccessTools.Field(typeof(MatchMakerAcceptScreen), "MatchmakerPlayersController").GetValue(__instance) as MatchmakerPlayerControllerClass;

            var removeGroup = new playerGroup();

            foreach (var item in controller.GroupPlayers)
            {
                if (item != controller.CurrentPlayer)
                    removeGroup.Add(item);
            }

            foreach (var item in removeGroup)
            {
                controller.GroupPlayers.Remove(item);
            }

            if (MainMenuControllerPatch.GroupPlayers.Count > 0)
            {
                foreach (var item in MainMenuControllerPatch.GroupPlayers)
                {
                    controller.GroupPlayers.Add(item);
                }
            }
        }
    }
    /**
     * When a player is removed from the original group, remove them from the raid group as well
     */
    internal class ContextInteractionsPlayerRemovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsClass), "method_21");
        }

        [PatchPrefix]
        private static void PatchPrefix(ContextInteractionsClass __instance)
        {
            string id = __instance.GroupPlayerDataClass.AccountId;
            MainMenuControllerPatch.GroupPlayers.RemoveFirst(x => x.AccountId == id);
        }
    }

    /** Ensure raid loading screen reflects the correct number of players based on raid group instead of original group **/
    internal class MatchmakerTimeHasComeShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchmakerTimeHasCome), "Show", new Type[]
            {
                typeof(ISession),
                typeof(RaidSettings),
                typeof(MatchmakerPlayerControllerClass)
            });
        }
        [PatchPrefix]
        private static void PatchPrefix(MatchmakerTimeHasCome __instance, ISession session, RaidSettings raidSettings, MatchmakerPlayerControllerClass matchmaker)
        {

            if (!raidSettings.IsPmc) MainMenuControllerPatch.GroupPlayers.Clear();


            foreach (var item in MainMenuControllerPatch.GroupPlayers)
            {
                matchmaker.GroupPlayers.Add(item);
            }

            if (MainMenuControllerPatch.TransitPlayers.Count > 0)
            {
                foreach (var item in MainMenuControllerPatch.TransitPlayers)
                {
                    var player = matchmaker.GroupPlayers.FirstOrDefault(x => x.AccountId == item.AccountId);
                    if (player == null)
                    {
                        matchmaker.GroupPlayers.Add(item);
                    }
                }

                MainMenuControllerPatch.TransitPlayers.Clear();
            }
        }
    }

    internal class MatchMakerAcceptScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerAcceptScreen), "Show", new Type[] { typeof(ISession), typeof(RaidSettings), typeof(RaidSettings) });

        }

        [PatchPrefix]
        private static void PatchPrefix(MatchMakerAcceptScreen __instance, ISession session, RaidSettings raidSettings, RaidSettings offlineRaidSettings)
        {
            raidSettings.RaidMode = ERaidMode.Local;
        }
        [PatchPostfix]
        private static void PatchPostfix(MatchMakerAcceptScreen __instance, ISession session, RaidSettings raidSettings, RaidSettings offlineRaidSettings)
        {
            if (!raidSettings.IsPmc) return;

            AddViewListClass UI = AccessTools.Field(typeof(MatchMakerAcceptScreen), "UI").GetValue(__instance) as AddViewListClass;
            MatchMakerGroupPreview _groupPreview = AccessTools.Field(typeof(MatchMakerAcceptScreen), "_groupPreview").GetValue(__instance) as MatchMakerGroupPreview;
            MatchmakerPlayerControllerClass MatchmakerPlayersController = AccessTools.Field(typeof(MatchMakerAcceptScreen), "MatchmakerPlayersController").GetValue(__instance) as MatchmakerPlayerControllerClass;
            RaidSettings raidSettings_0 = AccessTools.Field(typeof(MatchMakerAcceptScreen), "raidSettings_0").GetValue(__instance) as RaidSettings;
            string string_2 = AccessTools.Field(typeof(MatchMakerAcceptScreen), "string_2").GetValue(__instance) as string;

            _groupPreview.Show(string_2, MatchmakerPlayersController, raidSettings_0, new Func<GroupPlayerViewModelClass, bool, bool, ContextInteractionsClass>(MatchmakerPlayersController.GetContextInteractions));
            UI.AddDisposable<MatchMakerGroupPreview>(_groupPreview);

            // fetch current player visual representation for all bots part of the group
            Task.Run(async () =>
            {
                playerGroup groupPlayers = new playerGroup();
                foreach (var teamer in MainMenuControllerPatch.GroupPlayers)
                {
                    var result = await session.GetOtherPlayerProfile(teamer.AccountId);
                    if (result.Succeed)
                    {
                        groupPlayers.Add(teamer);
                        var health = teamer.PlayerVisualRepresentation.Info.Health ?? MatchmakerPlayersController.CurrentPlayer.Info.Health.Clone();
                        var preview = new GClass1416(result.Value);
                        preview.PlayerVisualRepresentation.Info.Health = health;
                        teamer.IsReady = true;
                        teamer.Info.SavageNickname = teamer.Info.Nickname;

                        teamer.method_2(preview.PlayerVisualRepresentation);
                    }
                }

                return groupPlayers;

            }).ContinueWith(r =>
            {
                // MatchMakerPlayerPreview.method_0 replication so we do not turn on Online RaidMode

                playerGroup groupPlayers = r.Result;

                if (groupPlayers.Count < 1)
                {
                    _groupPreview.Close();
                    return;
                }

                List<MatchMakerPlayerPreview> list_0 = AccessTools.Field(typeof(MatchMakerGroupPreview), "list_0").GetValue(_groupPreview) as List<MatchMakerPlayerPreview>;
                List<ComradeView> _comradesPositions = AccessTools.Field(typeof(MatchMakerGroupPreview), "_comradesPositions").GetValue(_groupPreview) as List<ComradeView>;
                Func<GroupPlayerViewModelClass, bool, bool, ContextInteractionsClass> func_0 = AccessTools.Field(typeof(MatchMakerGroupPreview), "func_0").GetValue(_groupPreview) as Func<GroupPlayerViewModelClass, bool, bool, ContextInteractionsClass>;

                List<GroupPlayerViewModelClass> list = Enumerable.ToList<GroupPlayerViewModelClass>(Enumerable.Where<GroupPlayerViewModelClass>(groupPlayers, new Func<GroupPlayerViewModelClass, bool>(_groupPreview.method_3)));
                for (int i = list_0.Count - 1; i >= list.Count; i--)
                {
                    _groupPreview.method_2(i);
                }
                int num = 0;
                while (num < list.Count && num < _comradesPositions.Count)
                {
                    MatchMakerPlayerPreview matchMakerPlayerPreview = _groupPreview.method_1(num);
                    GroupPlayerViewModelClass gclass = list[num];
                    if (matchMakerPlayerPreview.PlayerAid != gclass.AccountId)
                    {
                        matchMakerPlayerPreview.Show(MatchmakerPlayersController, raidSettings, gclass, func_0.Invoke(gclass, false, true))
                        .ContinueWith(t =>
                        {
                            TextMeshProUGUI _groupStatusField = AccessTools.Field(typeof(MatchMakerPlayerPreview), "_groupStatusField").GetValue(matchMakerPlayerPreview) as TextMeshProUGUI;
                            _groupStatusField.text = string.Empty;
                        })
                        .HandleExceptions();

                        _comradesPositions[num].TogglePlaceholder(false);
                    }
                    else
                    {
                        _comradesPositions[num].TogglePlaceholder(false);
                    }
                    num++;
                }

            }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();

        }
    }

    internal class SelectSpawnPointPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SpawnSystemClass), "SelectSpawnPoint");
        }
        [PatchPrefix]
        private static void PatchPrefix(ref ESpawnCategory category, EPlayerSide side, string groupId, string teamId, IPlayer person, string infiltration, string profileId)
        {
            if (!friendlySAIN.spawnPoint.Value || infiltration == "Hideout") return;
            // switch to coop mode if the player has followers
            if (category == ESpawnCategory.Player && person == null)
            {
                if (SpawnHelper.spawnMemberIds.Count > 0 || SpawnHelper.spawnMemberIdsBoss.Count > 0)
                {
                    category = ESpawnCategory.Coop;
                }
            }
        }
    }
}
