using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class BotGroupUsecEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "method_0");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotsGroup __instance, ref bool __result, IPlayer player)
        {
            // fix Usecs turning hostile because of UsecRaidRemainKills
            if (player.Profile.Info.Side == EPlayerSide.Usec)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    internal class BotGroupAddEnemyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "AddEnemy");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotsGroup __instance, ref bool __result, IPlayer person, EBotEnemyCause cause)
        {
            if (person == null || (person.IsAI && person.AIData?.BotOwner?.GetPlayer == null)) return true;
            if (new EBotEnemyCause[] { EBotEnemyCause.addPlayerToBoss, EBotEnemyCause.checkAddTODO }.Contains(cause)) return true;


            bool isBossPlayerGroup = __instance is BotsGroupPlayer;

            pitAIBossPlayer plBoss = BossPlayers.GetBoss(person.ProfileId);

            BotsGroup bossGroup = plBoss != null ? plBoss.bossGroup : null;

            bool isInitialCause = new EBotEnemyCause[] { EBotEnemyCause.initial, EBotEnemyCause.AddNewMember, EBotEnemyCause.warn, EBotEnemyCause.addBotNoGroup }.Contains(cause);

            bool badGuy = Utils.Utils.FlagGet("isBadGuy");

            bool isPMCGroup = new EPlayerSide[] { EPlayerSide.Bear, EPlayerSide.Usec }.Contains(__instance.Side);

            bool isFriendlyPMC = isPMCGroup && Utils.Utils.FlagGet("friendlySAIN");

            // prevent Rogues from adding the player and his followers as enemies if they are friends with the Goons
            WildSpawnType groupRole = __instance.InitialBotType;

            List<WildSpawnType> _rougeTypes = Utils.Props.BossFollowersType.ToList();
            _rougeTypes.Add(WildSpawnType.exUsec);

            if (!isBossPlayerGroup && isInitialCause && __instance.MembersCount > 0)
            {
                if (plBoss == null)
                {
                    var followerOfBoss = BossPlayers.GetFollowers().Find(x => x.GetBot().ProfileId == person.ProfileId);
                    if (followerOfBoss != null) plBoss = followerOfBoss.GetBoss();
                }

                if (plBoss != null && Utils.Utils.PlayerHasKnightQuest(plBoss.realPlayer.Profile))
                {
                    bool isRogue = _rougeTypes.Contains(groupRole);
                    if (isRogue)
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            // bad guy flag will exclude the player and his followers from the friendly PMCs
            if (
                badGuy &&
                !isBossPlayerGroup &&
                person.Profile.Info.Side == __instance.Side &&
                isPMCGroup &&
                (
                    plBoss != null ||
                    BossPlayers.GetFollowers().Any(x => x.GetBot().ProfileId == person.ProfileId)
                )
            )
            {
                return true;

            }

            // if friendly PMC side is on, prevent groups from adding same side players as enemies
            if (
                isInitialCause && person.Profile.Info.Side == __instance.Side && isFriendlyPMC
            )
            {
                __result = false;
                return false;
            }

            // prevent BTR from adding the player's followers as enemies (bug in 0.16 it seems)
            if (
                __instance.InitialBotType == WildSpawnType.shooterBTR &&
                BossPlayers.GetFollowers().Any(x => x.GetBot().ProfileId == person.ProfileId)
            )
            {
                __result = false;
                return false;
            }

            // from this point if this is not the player boss group, allow adding enemies
            if (!isBossPlayerGroup) return true;

            // prevent followers from adding teammates
            if (__instance.Members.Any(x => x.ProfileId == person.ProfileId))
            {
                __result = false;
                return false;
            }
            // prevent boss player from being added as an enemy
            else if ((__instance as BotsGroupPlayer).Boss != null && (__instance as BotsGroupPlayer).Boss.realPlayer.ProfileId == person.ProfileId)
            {
                __result = false;
                return false;
            }

            WildSpawnType? personRole = person.Profile?.Info?.Settings?.Role;

            // prevent followers group from adding friendly bots as enemies
            if (
                personRole.HasValue &&
                (
                    (isInitialCause || personRole == WildSpawnType.shooterBTR || personRole == WildSpawnType.gifter) &&
                    Utils.Props.friendlyBotTypes.Contains(personRole.Value)
                )
            )
            {
                __result = false;
                return false;
            }

            // prevent Rogues from being added as enemies if they are friends with the player
            pitAIBossPlayer bossOfGroup = BossPlayers.GetBossByGroup(__instance.Id);

            if (isInitialCause && personRole != null && bossOfGroup != null)
            {
                Player bossPlayer = bossOfGroup.realPlayer;
                var friendly = Utils.Props.BossFollowersType.ToList();
                friendly.Add(WildSpawnType.exUsec);
                if (Utils.Utils.PlayerHasKnightQuest(bossPlayer.Profile))
                {
                    if (
                        friendly.Contains(personRole.Value)
                    )
                    {
                        __result = false;
                        return false;
                    }
                }
            }

            return true;
        }
        /**
         * Whoever makes the player or his followers an enemy will become the enemy of the player's boss group (BTR is the exception)
         */
        [PatchPostfix]
        private static void PatchPostfix(BotsGroup __instance, IPlayer person, EBotEnemyCause cause)
        {
            if (
                person == null ||
                (person.IsAI && person.AIData?.BotOwner?.GetPlayer == null) ||
                new EBotEnemyCause[] { EBotEnemyCause.warn }.Contains(cause) ||
                __instance is BotsGroupPlayer
            )
            {
                return;
            }

            bool isFriend = false;

            for (int i = 0; i < __instance.MembersCount; i++)
            {
                var mem = __instance.Member(i);
                // ignore BTR 
                if (
                    mem.Profile.Info.Settings.Role == WildSpawnType.shooterBTR
                )
                {
                    isFriend = true;
                    break;
                }
            }

            if (isFriend) return;

            var plBoss = BossPlayers.GetBoss(person.ProfileId);

            if (plBoss == null && person.IsAI && person.AIData?.BotOwner != null && BossPlayers.IsFollower(person.AIData.BotOwner))
            {
                plBoss = BossPlayers.GetBossByGroup(person.AIData.BotOwner.BotsGroup.Id);
            }

            if (plBoss == null) return;


            // - skip if the group is of the Rouges and they are just spawning
            if (cause == EBotEnemyCause.AddNewMember)
            {
                var friendly = Utils.Props.BossFollowersType.ToList();
                friendly.Add(WildSpawnType.exUsec);

                bool isFriendly = false;

                for (int i = 0; i < __instance.MembersCount; i++)
                {
                    var mem = __instance.Member(i);
                    if (friendly.Contains(mem.Profile.Info.Settings.Role))
                    {
                        isFriendly = true;
                        break;
                    }
                }

                if (isFriendly && Utils.Utils.PlayerHasKnightQuest(plBoss.realPlayer.Profile))
                {
                    return;
                }
            }

            try
            {
                BotsGroup bossGroup = plBoss.bossGroup;

                if (bossGroup != null)
                    for (int i = 0; i < __instance.MembersCount; i++)
                    {
                        var item = __instance.Member(i);
                        if (cause == EBotEnemyCause.AddNewMember)
                        {
                            bossGroup.AddEnemy(item, cause);
                        }
                        else
                            bossGroup.AddEnemy(item, EBotEnemyCause.addPlayerToBoss);
                    }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to make a group an enemy");
                Modules.Logger.LogError(ex);
            }
        }
    }

    /**
     * Patch to make friendly bots who just turned on the player to be marked as an enemy by the player's followers
     */
    /*internal class BotsGroupCheckAddPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotsGroup), "CheckAndAddEnemy");

        }
        [PatchPostfix]
        private static void PatchPostfix(BotsGroup __instance, IPlayer player, bool ignoreAI = false)
        {
            if (player == null || !player.IsAI) return;
            pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);

            if (boss == null) return;
            if (boss.bossGroup == null) return;

            if (__instance.Id == boss.bossGroup.Id) return;

            for (int i = 0; i < __instance.MembersCount; i++)
            {
                boss.bossGroup.AddEnemy(__instance.Member(i), EBotEnemyCause.addPlayerToBoss);
            }

        }
    }*/

    internal class BotsGroupPlayer : BotsGroup
    {
        private pitAIBossPlayer boss;
        public pitAIBossPlayer Boss => boss;

        public BotsGroupPlayer(BotZone zone, IBotGame botGame, BotOwner initialBot, List<BotOwner> enemies, DeadBodiesController deadBodiesController, List<Player> allPlayers, pitAIBossPlayer player) : base(zone, botGame, initialBot, enemies, deadBodiesController, allPlayers, false)
        {
            RemoveEnemy(player.Player());
            AddAlly(player.realPlayer);
            Side = player.realPlayer.Side;

            boss = player;

            foreach (var item in Enemies)
            {
                WildSpawnType? Role = item.Value.Player?.Profile?.Info?.Settings?.Role;
                if (
                    Role.HasValue &&
                    Utils.Props.friendlyBotTypes.Contains(Role.Value)
                )
                {
                    RemoveEnemy(item.Value.Player, item.Value.Cause);
                    break;
                }
            }

            initialBot.Settings.GetFriendlyBotTypes().AddRange(Utils.Props.friendlyBotTypes);

            initialBot.Settings.GetEnemyBotTypes().RemoveAll(x => Utils.Props.friendlyBotTypes.Contains(x));

            if (Utils.Utils.FlagGet("isBadGuy"))
            {
                initialBot.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                initialBot.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

                if (Side == EPlayerSide.Bear) initialBot.Settings.GetEnemyBotTypes().Add(WildSpawnType.pmcBEAR);
                else if (Side == EPlayerSide.Usec) initialBot.Settings.GetEnemyBotTypes().Add(WildSpawnType.pmcUSEC);

                initialBot.Settings.GetWarnBotTypes().Remove(WildSpawnType.pmcBEAR);
                initialBot.Settings.GetWarnBotTypes().Remove(WildSpawnType.pmcUSEC);

            }
        }
    }
}
