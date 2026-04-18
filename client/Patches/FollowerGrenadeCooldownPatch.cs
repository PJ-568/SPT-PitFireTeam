using EFT;
using EFT.InventoryLogic;
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
            if (bot == null)
            {
                return true;
            }

            if (!BossPlayers.IsFollower(bot))
            {
                return true;
            }

            if (!FollowerGrenadeRuntimeGate.IsThrowAllowed(bot))
            {
                Log(bot, "DoThrow blocked reason=gateClosed");
                __result = false;
                return false;
            }

            BotRequest? currentRequest = bot.BotRequestController?.CurRequest;
            bool explicitGrenadeSequence =
                __instance.ThrowindNow ||
                __instance.ReadyToThrow ||
                bot.SuppressGrenade?.Bool_0 == true ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenade ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenadeFromPlace;
            if (explicitGrenadeSequence)
            {
                Log(bot, $"DoThrow allowed reason=explicitSequence throwNow={__instance.ThrowindNow} ready={__instance.ReadyToThrow} request={currentRequest?.BotRequestType.ToString() ?? "<null>"}");
                return true;
            }

            if (FollowerGrenadeCooldowns.CanProceedToThrow(bot))
            {
                Log(bot, "DoThrow allowed reason=cooldownOpen");
                return true;
            }

            Log(bot, "DoThrow blocked reason=cooldownClosed");
            __result = false;
            return false;
        }

        private static void Log(BotOwner bot, string message)
        {
            friendlySAIN.Log?.LogInfo($"[GrenadeGate] follower={bot?.Profile?.Nickname ?? bot?.ProfileId ?? "<null>"} {message}");
        }

    }

    internal class FollowerGrenadeThrowFinishPatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), "method_6", new[] { typeof(ThrowWeapItemClass) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance, ThrowWeapItemClass grenade)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            bool completed = grenade != null;
            friendlySAIN.Log?.LogInfo(
                $"[GrenadeGate] follower={bot.Profile?.Nickname ?? bot.ProfileId ?? "<null>"} throw-finish completed={completed} grenade={grenade?.TemplateId ?? "<null>"}");
            FollowerGrenadeRuntimeGate.FinishExplicitThrow(bot, completed);
        }
    }
}
