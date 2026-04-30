using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
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
                return true;
            }

            if (FollowerGrenadeCooldowns.CanProceedToThrow(bot))
            {
                return true;
            }

            __result = false;
            return false;
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

            bool completed = FollowerGrenadeRuntimeGate.ConsumeThrowReleased(bot);

            FollowerGrenadeRuntimeGate.FinishExplicitThrow(bot, completed);
        }
    }

    internal class FollowerGrenadeReleasePatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(BotGrenadeController), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), "method_11");
        }

        [PatchPostfix]
        private static void PatchPostfix(BotGrenadeController __instance, Result<IHandsThrowController> throwResult)
        {
            BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot))
            {
                return;
            }

            if (throwResult.Succeed && throwResult.Value != null)
            {
                FollowerGrenadeRuntimeGate.MarkThrowReleased(bot);
            }
        }
    }
}
