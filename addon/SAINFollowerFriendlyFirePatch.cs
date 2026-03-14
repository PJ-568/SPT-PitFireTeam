using EFT;
using HarmonyLib;
using SAIN;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using friendlySAIN.Modules;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerFriendlyFirePatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(SAINFriendlyFireClass),
                nameof(SAINFriendlyFireClass.CheckFriendlyFire),
                new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(BotComponent) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINFriendlyFireClass.CheckFriendlyFire for follower FF patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerFriendlyFirePatch), nameof(Postfix_CheckFriendlyFire)));
            Modules.Logger.LogInfo("[Init] SAIN follower friendly-fire patch applied.");
        }

        private static void Postfix_CheckFriendlyFire(
            Vector3 weaponFirePort,
            float distance,
            Vector3 weaponPointDirection,
            BotComponent bot,
            ref FriendlyFireStatus __result)
        {
            if (__result == FriendlyFireStatus.FriendlyBlock)
            {
                return;
            }

            if (bot?.BotOwner == null || !BossPlayers.IsFollower(bot.BotOwner))
            {
                return;
            }

            if (distance <= 0.05f || weaponPointDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            BotOwner shooter = bot.BotOwner;
            IBotAiming? currentAiming = shooter.AimingManager?.CurrentAiming;
            if (currentAiming == null || shooter.GetPlayer?.PlayerBones?.WeaponRoot == null)
            {
                return;
            }

            Vector3 from = shooter.GetPlayer.PlayerBones.WeaponRoot.position;
            Vector3 to = currentAiming.RealTargetPoint;
            if (to == Vector3.zero)
            {
                return;
            }

            if (shooter.ShootData != null && shooter.ShootData.CheckFriendlyFire(from, to))
            {
                __result = FriendlyFireStatus.FriendlyBlock;
            }
        }
    }
}
