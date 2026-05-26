using EFT;
using HarmonyLib;
using pitTeam.BigBrain;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal sealed class FollowerEnemyInfoCorrectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.CheckLookEnemy));
        }

        [PatchPrefix]
        private static void PatchPrefix(EnemyInfo __instance, ref FollowerEnemyInfoCorrection.SensorState __state)
        {
            __state = FollowerEnemyInfoCorrection.CaptureState(__instance);
            FollowerEnemyInfoCorrection.BeginLookCheck();
        }

        [PatchPostfix]
        private static void PatchPostfix(EnemyInfo __instance, FollowerEnemyInfoCorrection.SensorState __state)
        {
            try
            {
                FollowerEnemyInfoCorrection.CorrectAfterLookCheck(__instance, __state);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Follower enemy-info correction failed: {ex}");
            }
        }

        [PatchFinalizer]
        private static void PatchFinalizer()
        {
            FollowerEnemyInfoCorrection.EndLookCheck();
        }
    }

    internal sealed class FollowerEnemyInfoSetVisibleCorrectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.SetVisible));
        }

        [PatchPrefix]
        private static void PatchPrefix(EnemyInfo __instance, ref FollowerEnemyInfoCorrection.SensorState __state)
        {
            __state = FollowerEnemyInfoCorrection.CaptureState(__instance);
        }

        [PatchPostfix]
        private static void PatchPostfix(
            EnemyInfo __instance,
            bool value,
            FollowerEnemyInfoCorrection.SensorState __state)
        {
            if (FollowerEnemyInfoCorrection.IsInsideLookCheck)
            {
                return;
            }

            try
            {
                if (!value)
                {
                    FollowerEnemyInfoCorrection.CorrectAfterExternalSetInvisible(__instance);
                    return;
                }

                FollowerEnemyInfoCorrection.CorrectAfterLookCheck(__instance, __state);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Follower external visible correction failed: {ex}");
            }
        }
    }
}
