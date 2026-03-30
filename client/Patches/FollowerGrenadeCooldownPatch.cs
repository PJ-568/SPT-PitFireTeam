using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class FollowerGrenadeCooldownPatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.DoThrow));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotGrenadeController __instance, ref bool __result)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || FollowerGrenadeCooldowns.CanProceedToThrow(bot))
            {
                return true;
            }

            __result = false;
            return false;
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null)
            {
                return;
            }

            FollowerGrenadeCooldowns.RecordThrow(bot);
        }
    }
}
