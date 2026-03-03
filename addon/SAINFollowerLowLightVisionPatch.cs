using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.EnemyClasses;
using friendlySAIN.Modules;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerLowLightVisionPatch
    {
        // 0 = no penalty for followers (fastest), 1 = vanilla SAIN penalty.
        private const float FollowerNightPenaltyFactor = 0.40f;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(EnemyGainSightClass),
                "CalcTimeModifier",
                new[] { typeof(bool), typeof(Enemy) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find EnemyGainSightClass.CalcTimeModifier for follower low-light patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerLowLightVisionPatch), nameof(Postfix_CalcTimeModifier)));
            Modules.Logger.LogInfo("[Init] SAIN follower low-light vision patch applied.");
        }

        private static void Postfix_CalcTimeModifier(Enemy Enemy, ref float __result)
        {
            BotOwner owner = Enemy?.BotOwner;
            if (owner == null) return;
            if (!BossPlayers.IsFollower(owner)) return;

            // Pull follower modifier closer to 1.0 so low-light slows spotting less.
            if (__result > 1f)
            {
                __result = Mathf.Lerp(1f, __result, FollowerNightPenaltyFactor);
            }
        }
    }
}
