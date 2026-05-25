using EFT;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Utils;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal sealed class FollowerShootFromPlaceCrouchPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BotShootFromPlace),
                nameof(BotShootFromPlace.CheckTarget),
                new[] { typeof(Vector3) });
        }

        [PatchPostfix]
        private static void PatchPostfix(BotShootFromPlace __instance, Vector3 target)
        {
            BotOwner botOwner = __instance?.BotOwner_0;
            if (botOwner == null ||
                !BossPlayers.IsFollower(botOwner) ||
                !__instance.CanShootSit)
            {
                return;
            }

            if (!FollowerShootPoseSafety.HasReliableCrouchLane(botOwner, target))
            {
                __instance.CanShootSit = false;
            }
        }
    }
}
