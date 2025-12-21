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
    /** Skip checking bot's role if we have made this bot a follower of a boss player **/
    internal class BotOwnerIsFolowerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "IsFollower");

        }

        [PatchPrefix]
        private static bool PatchPrefix(BotOwner __instance, ref bool __result)
        {

            if (BossPlayers.IsFollower(__instance))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
    /** Patch on botOwner UpdateManual to allow us to execute custom code **/
    internal class BotOwnerManualUpdatePatch : ModulePatch
    {

        public static Dictionary<string, Action<BotOwner>> BotOwnerUpdate = new Dictionary<string, Action<BotOwner>>();
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "UpdateManual");
        }

        [PatchPostfix]
        private static void PatchPostfix(BotOwner __instance)
        {
            try
            {
                if (
                    __instance != null &&
                    __instance.BotState == EBotState.Active &&
                    __instance.GetPlayer != null &&
                    __instance.GetPlayer.HealthController != null &&
                    __instance.ProfileId != null &&
                    __instance.GetPlayer.HealthController.IsAlive
                )
                {
                    Action<BotOwner> OnUpdate;
                    BotOwnerUpdate.TryGetValue(__instance.ProfileId, out OnUpdate);
                    if (OnUpdate != null) OnUpdate(__instance);

                }
            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Exception on BotOwner UpdateManual PatchPostfix");
                Modules.Logger.LogError(e);
            }
        }
    }
    internal class BotOwnerActivatePatch : ModulePatch
    {
        private static List<Action<BotOwner>> onActivate = new List<Action<BotOwner>>
        {
           new Action<BotOwner>((BotOwner bot) =>
           {
               bool isRogue = Utils.Props.BossFollowersType.ToList().AddItem(WildSpawnType.exUsec).Contains(bot.Profile.Info.Settings.Role);

               if(Utils.Utils.FlagGet("friendlySAIN") && (bot.Side == EPlayerSide.Bear || bot.Side == EPlayerSide.Usec))
               {
                    bot.Settings.FileSettings.Boss.SHALL_WARN = false;
                    bot.Settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER = 0f;
                    bot.Settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER_ALLY = 0f;
               }

               // force bosses to always be hostile to PMCs
               if(Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role))
               {
                   bot.Settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                   bot.Settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
               }


               BossPlayers.GetFollowers().ForEach(follower=>{
                    var fl = follower.GetBot();
                    WildSpawnType role = bot.Profile.Info.Settings.Role;
                    pitAIBossPlayer bossPlayer = follower.GetBoss();

                    if(bossPlayer == null) return;

                    bool friendlyRogue = Utils.Utils.PlayerHasKnightQuest(bossPlayer.realPlayer.Profile);
                    // fix 0.16 bug where followers add BTR or the Rogues to their enemy list because they are spawning late
                    if(Utils.Props.friendlyBotTypes.Contains(role) || (isRogue && friendlyRogue))
                    {
                        fl.BotsGroup.RemoveEnemy(bot);
                        fl.BotsGroup.AddNeutral(bot);
                        fl.Memory.DeleteInfoAboutEnemy(bot);
                        return;
                    }

                    // fix 0.16 bug where bots are not attacking player followers
                    // ensure zombies see the followers as enemies as well
                    if(
                        fl != null && !fl.IsDead &&
                        (
                            fl.Side != bot.Side || Utils.Props.ZombieTypes.Contains(role) ||
                            ( Utils.Utils.FlagGet("isBadGuy") && fl.Side != EPlayerSide.Savage )
                        )
                    )
                    {

                        if(!bot.BotsGroup.AddEnemy(fl,EBotEnemyCause.initial))
                        {

                            bool isInEnemyList = false;
                            bool isInNeutralList = false;

                            if(bot.BotsGroup.Enemies.TryGetValue(fl,out var enemy))
                            {
                                isInEnemyList = true;
                            }

                            if(bot.BotsGroup.Neutrals.TryGetValue(fl, out var neutral))
                            {
                                isInNeutralList = true;
                            }

                            if(isInEnemyList) return;

                            var botSettingsClass = new BotSettingsClass(fl.GetPlayer, bot.BotsGroup, EBotEnemyCause.initial);

                            bot.BotsGroup.Enemies.Add(fl,botSettingsClass);
                            if(isInNeutralList)
                            {
                                bot.BotsGroup.Neutrals.Remove(fl);
                            }
                            bot.Memory.AddEnemy(fl, botSettingsClass, false);
                        }
                        // - ensure followers see zombies as enemies
                        if(Utils.Props.ZombieTypes.Contains(role))
                        {
                            fl.BotsGroup.AddEnemy(bot, EBotEnemyCause.addPlayerToBoss);
                        }
                    }
               });
           })
        };
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotOwner), "method_10");

        }

        [PatchPostfix]
        private static void PatchPostfix(BotOwner __instance)
        {
            if (BossPlayers.IsFollower(__instance)) return;

            try
            {
                onActivate.ForEach(action => action(__instance));
            }
            catch (Exception e)
            {
                Modules.Logger.LogError(e);
            }
        }

        public static void AddOnActivate(Action<BotOwner> action)
        {
            if (!onActivate.Contains(action)) onActivate.Add(action);
        }

        public static void RemoveOnActivate(Action<BotOwner> action)
        {
            if (onActivate.Contains(action)) onActivate.Remove(action);
        }
    }
}
