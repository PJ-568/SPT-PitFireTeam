using EFT;

using friendlySAIN;
using friendlySAIN.Modules;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class SAINPatch
    {
        public static bool IsSAINInstalled()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SAIN");
        }

        private static Type? squadType = null;
        private static Type? SAINEnableClass = null;

        private static Type? enemyTalk = null;
        private static Type? GroupClass = null;
        private static Type? playerMovementControllerClass = null;
        public static void PatchSAINIfInstalled(Harmony harmony)
        {
            if (!IsSAINInstalled()) return;

            if (squadType == null)
            {
                squadType = Type.GetType("SAIN.BotController.Classes.Squad, SAIN");
            }

            if (SAINEnableClass == null)
            {
                SAINEnableClass = Type.GetType("SAIN.SAINEnableClass, SAIN");
            }


            if (enemyTalk == null)
            {
                enemyTalk = Type.GetType("SAIN.SAINComponent.Classes.Talk.EnemyTalk, SAIN");

            }

            if (GroupClass == null)
            {
                GroupClass = Type.GetType("SAIN.SAINComponent.Classes.Talk.GroupTalk, SAIN");
            }

            if (playerMovementControllerClass == null)
            {
                playerMovementControllerClass = Type.GetType("SAIN.Classes.PlayerMovementController, SAIN");
            }


            if (enemyTalk != null)
            {
                harmony.Patch(AccessTools.Method(enemyTalk, "playerTalked"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchPlayerTalked), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (GroupClass != null)
            {
                harmony.Patch(AccessTools.Method(GroupClass, "EnemyConversation"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchEnemyConvesation), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (playerMovementControllerClass != null)
            {
                var setTargetMoveDirection = AccessTools.Method(playerMovementControllerClass, "SetTargetMoveDirection");
                if (setTargetMoveDirection != null)
                {
                    harmony.Patch(setTargetMoveDirection, new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchSetTargetMoveDirection), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
                }
            }


            if (squadType != null && SAINEnableClass != null)
            {
                var assignLeader = AccessTools.Method(squadType, "assignSquadLeader");
                if (assignLeader != null)
                {
                    harmony.Patch(assignLeader, new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchAssignSquadLeader), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
                }
                Logger.LogInfo("SAIN Patched");
            }
        }

        [HarmonyPrefix]
        private static bool PatchPlayerTalked(EPhraseTrigger phrase, ETagStatus mask, Player player)
        {
            if (phrase == (EPhraseTrigger)CustomPhrases.TeamStatus || phrase == (EPhraseTrigger)CustomPhrases.OverThere)
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool PatchEnemyConvesation(EPhraseTrigger trigger, ETagStatus status, Player player)
        {
            return PatchPlayerTalked(trigger, status, player);
        }

        [HarmonyPrefix]
        private static bool PatchAssignSquadLeader(object __instance, object sain)
        {
            try
            {
                if (sain == null) return true;

                var botOwnerProp = AccessTools.Property(sain.GetType(), "BotOwner");
                var botOwner = botOwnerProp?.GetValue(sain) as BotOwner;
                if (botOwner == null) return true;

                if (BossPlayers.IsFollower(botOwner))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
            return true;
        }

        [HarmonyPrefix]
        private static bool PatchSetTargetMoveDirection(object playerComp)
        {
            try
            {
                if (playerComp == null) return true;

                var botOwnerProp = AccessTools.Property(playerComp.GetType(), "BotOwner");
                BotOwner botOwner = botOwnerProp?.GetValue(playerComp) as BotOwner;
                if (botOwner == null) return true;

                if (!ShouldSkipSainMoveDirection(botOwner)) return true;

                if (botOwner.Mover != null)
                {
                    botOwner.Mover.Pause = false;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldSkipSainMoveDirection(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (!BossPlayers.IsFollower(botOwner)) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            return botOwner.Memory?.HaveEnemy != true;
        }
    }
}
