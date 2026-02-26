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
        private static Type? sainPlayerTalkPatch = null;
        private static Type? sainBotTalkPatch = null;
        private static Type? sainBotTalkManualUpdatePatch = null;
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

            if (sainPlayerTalkPatch == null)
            {
                sainPlayerTalkPatch = Type.GetType("SAIN.Patches.Talk.PlayerTalkPatch, SAIN");
            }

            if (sainBotTalkPatch == null)
            {
                sainBotTalkPatch = Type.GetType("SAIN.Patches.Talk.BotTalkPatch, SAIN");
            }

            if (sainBotTalkManualUpdatePatch == null)
            {
                sainBotTalkManualUpdatePatch = Type.GetType("SAIN.Patches.Talk.BotTalkManualUpdatePatch, SAIN");
            }

            if (enemyTalk != null)
            {
                harmony.Patch(AccessTools.Method(enemyTalk, "playerTalked"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchPlayerTalked), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (GroupClass != null)
            {
                harmony.Patch(AccessTools.Method(GroupClass, "EnemyConversation"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchEnemyConvesation), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            PatchSainTalkPrefixesForFollowers(harmony);


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

        private static void PatchSainTalkPrefixesForFollowers(Harmony harmony)
        {
            var bypassPrefix = new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BypassSainTalkPatchForFollower), BindingFlags.NonPublic | BindingFlags.Static));

            if (sainPlayerTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainPlayerTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkManualUpdatePatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkManualUpdatePatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }
        }

        [HarmonyPrefix]
        private static bool BypassSainTalkPatchForFollower(object[] __args, ref bool __result)
        {
            try
            {
                if (__args == null || __args.Length == 0) return true;

                BotOwner? botOwner = null;

                if (__args[0] is Player player)
                {
                    if (!player.IsAI) return true;
                    botOwner = player.AIData?.BotOwner;
                }
                else if (__args[0] is BotTalk botTalk)
                {
                    botOwner = botTalk.BotOwner_0;
                }

                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // Skip SAIN talk-patch logic for followers so EFT/plugin talk behavior can run.
                __result = true;
                return false;
            }
            catch
            {
                return true;
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
    }
}
