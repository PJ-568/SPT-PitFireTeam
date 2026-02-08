using Comfort.Common;
using EFT;
using EFT.Interactive;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.Patches
{
    /** On extracting via transit point, remember bots the player was with so we can re-spawn them **/
    internal class TransitPointPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TransitPoint), "method_6");
        }

        [PatchPostfix]
        private static void PatchPostfix(string groupId,
                        HashSet<string> __result,
                        HashSet<string> singlePlayers,
                        HashSet<string> partyPlayers,
                        ref bool partyIsFull)
        {
            partyIsFull = true;

            IEnumerable<IPlayer> groupPlayers = Singleton<GameWorld>.Instance.GroupPlayers(groupId, null);

            Utils.SpawnHelper.spawnMemberIdsScav.Clear();
            Utils.SpawnHelper.spawnMemberIds.Clear();
            Utils.SpawnHelper.spawnMemberIdsBoss.Clear();

            foreach (IPlayer player in groupPlayers)
            {
                if (BossPlayers.IsPlayerBoss(player.ProfileId))
                {
                    pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);
                    BossPlayers.GetFollowersByBoss(player.ProfileId).ForEach(follower =>
                    {
                        BotOwner bot = follower.GetBot();

                        if (follower != null && !bot.IsDead &&
                            (
                                follower.IsSquadMate ||
                                (Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role) && !Utils.Utils.FlagGet("questGoons"))
                            )
                        )
                        {
                            InfoClass info = bot.Profile.Info;

                            bool isBoss = Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role);

                            LastPlayerStateClass playerVisualization = new LastPlayerStateClass(new GClass1410
                            {
                                Level = isBoss ? 60 : info.Level,
                                MemberCategory = isBoss ? EMemberCategory.Sherpa : ((info.Side != EPlayerSide.Savage) ? info.SelectedMemberCategory : EMemberCategory.Default),
                                SelectedMemberCategory = isBoss ? EMemberCategory.Sherpa : info.SelectedMemberCategory,
                                Nickname = info.Nickname,
                                Side = info.Side,
                                Health = bot.Profile.Health
                            }, bot.Profile.Customization, bot.Profile.Inventory.Equipment);

                            MainMenuControllerPatch.TransitPlayers.Add(new GroupPlayerViewModelClass(new GroupPlayerDataClass
                            {
                                AccountId = bot.AccountId,
                                Id = bot.Profile.Id,
                                Info = new GClass1410
                                {
                                    Level = isBoss ? 60 : info.Level,
                                    PrestigeLevel = info.PrestigeLevel,
                                    MemberCategory = isBoss ? EMemberCategory.Sherpa : info.MemberCategory,
                                    SelectedMemberCategory = isBoss ? EMemberCategory.Sherpa : info.SelectedMemberCategory,
                                    Nickname = bot.Profile.GetCorrectedNickname(),
                                    Side = info.Side,
                                    SavageLockTime = player.Profile.Info.SavageLockTime,
                                    SavageNickname = player.Profile.Info.Nickname,
                                    GameVersion = info.GameVersion,
                                    HasCoopExtension = info.HasCoopExtension,
                                    Health = bot.Profile.Health,
                                },
                                PlayerVisualRepresentation = playerVisualization
                            }));

                            if (player.Profile.Side == EPlayerSide.Savage) Utils.SpawnHelper.spawnMemberIdsScav.Add(bot.AccountId);
                            else if (isBoss) Utils.SpawnHelper.spawnMemberIdsBoss.Add(bot.Profile.Info.Settings.Role);
                            else Utils.SpawnHelper.spawnMemberIds.Add(bot.AccountId);

                            Modules.Logger.LogInfo("Transitioning with " + bot.Profile.Nickname);

                            Utils.Utils.FlagSet("RaidTransit", true);
                            Utils.SpawnHelper.ScavSquadSize = 0;
                        }
                    });

                }
            }
        }
    }
}
