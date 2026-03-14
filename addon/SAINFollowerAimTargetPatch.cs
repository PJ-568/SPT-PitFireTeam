using EFT;
using HarmonyLib;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;
using friendlySAIN.Modules;
using System;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerAimTargetPatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(SAINShootData),
                "GetAimTarget",
                new[] { typeof(Enemy), typeof(BotComponent) });

            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINShootData.GetAimTarget for follower aim-target patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerAimTargetPatch), nameof(Prefix_GetAimTarget)));
            Modules.Logger.LogInfo("[Init] SAIN follower aim-target patch applied.");
        }

        private static bool Prefix_GetAimTarget(Enemy enemy, BotComponent bot, ref Vector3? __result)
        {
            try
            {
                BotOwner owner = bot?.BotOwner;
                if (owner == null || !BossPlayers.IsFollower(owner))
                {
                    return true;
                }

                if (enemy == null || !enemy.IsVisible || !enemy.CanShoot)
                {
                    __result = null;
                    return false;
                }

                EnemyInfo info = enemy.EnemyInfo;
                if (info == null)
                {
                    __result = null;
                    return false;
                }

                Vector3? centerMass = enemy.CenterMass;
                Vector3? partToShoot = info.Distance < 6f
                    ? info.GetBodyPartPosition()
                    : info.GetPartToShoot();

                Vector3? modifiedTarget = CheckYValue(centerMass, partToShoot);
                __result = modifiedTarget ?? partToShoot ?? centerMass;
                return false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower aim-target patch.");
                Modules.Logger.LogError(ex);
                return true;
            }
        }

        private static Vector3? CheckYValue(Vector3? centerMass, Vector3? partTarget)
        {
            if (centerMass != null && partTarget != null && centerMass.Value.y < partTarget.Value.y)
            {
                Vector3 newTarget = partTarget.Value;
                newTarget.y = centerMass.Value.y;
                return newTarget;
            }

            return null;
        }
    }
}
