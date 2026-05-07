using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class FollowerGrenadeAvailabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(BotGrenadeController), nameof(BotGrenadeController.HaveGrenade));
        }

        [PatchPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void PatchPostfix(BotGrenadeController __instance, ref bool __result)
        {
            BotOwner bot = __instance?.BotOwner_0;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            if (FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
                __result = __instance.Grenade != null;
                return;
            }

            if (__result)
            {
                __result = false;
            }
        }
    }
}
