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

        private static Type squadType = null;
        private static Type SAINEnableClass = null;

        private static Type enemyTalk = null;
        private static Type GroupClass = null;

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


            if (enemyTalk != null)
            {
                harmony.Patch(AccessTools.Method(enemyTalk, "playerTalked"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchPlayerTalked), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (GroupClass != null)
            {
                harmony.Patch(AccessTools.Method(GroupClass, "EnemyConversation"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchEnemyConvesation), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }


            if (squadType != null && SAINEnableClass != null)
            {
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
    }
}
