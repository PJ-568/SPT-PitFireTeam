using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal static class FollowerPmcCombatSuppression
    {
        public static bool Prefix(BotOwner botOwner, ref bool result)
        {
            if (friendlySAIN.UseSainFollowerCombat)
            {
                return true;
            }

            if (botOwner != null && BossPlayers.IsFollower(botOwner))
            {
                result = false;
                return false;
            }

            return true;
        }
    }

    internal sealed class PmcBearCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GClass141).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass141 __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?.BotOwner_0, ref __result);
        }
    }

    internal sealed class PmcUsecCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GClass145).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass145 __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?.BotOwner_0, ref __result);
        }
    }

    internal sealed class PmcFlankCombatLayerSuppressionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GClass142).GetMethod("ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass142 __instance, ref bool __result)
        {
            return FollowerPmcCombatSuppression.Prefix(__instance?.BotOwner_0, ref __result);
        }
    }
}
