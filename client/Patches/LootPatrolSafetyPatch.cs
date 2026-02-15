using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    // Prevent vanilla LootPatrol layer from crashing follower bots when it is still present in the brain stack.
    internal class LootPatrolFollowerGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass117), "ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass117 __instance, ref bool __result)
        {
            BotOwner bot = __instance?.BotOwner_0;
            if (bot != null && BossPlayers.IsFollower(bot))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
