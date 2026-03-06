using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINRegroupBootstrap
    {
        private static bool _initialized;
        private const int FollowerCombatLayerPriority = 71;

        public static void Initialize(Harmony harmony, ManualLogSource logger)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            List<string> brains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault",
                "Knight",
                "BigPipe",
                "BirdEye"
            };

            try
            {
                SAINFollowerCombatLayerGatePatch.Apply(harmony);
                SAINFollowerFriendlyFirePatch.Apply(harmony);
                if (SAINAddonToggles.EnableForcedEnemyRetention)
                {
                    SAINCalcGoalPatch.Apply(harmony);
                    SAINEnemyAcquireGatePatch.Apply(harmony);
                    SAINFollowerEnemyRetentionService.Initialize();
                }
                else
                {
                    logger.LogInfo("[Init] SAIN forced enemy retention disabled by toggle.");
                }
                SAINFollowerPersonalityPatch.Apply(harmony);
                SAINFollowerHitAccuracyPatch.Apply(harmony);
                SAINFollowerLowLightVisionPatch.Apply(harmony);
                BrainManager.AddCustomLayer(typeof(SAINFollowerCombatLayer), brains, FollowerCombatLayerPriority);
                logger.LogInfo("[Init] SAIN regroup command handling routed through follower combat layer.");
                logger.LogInfo($"[Init] SAIN follower combat layer registered at priority {FollowerCombatLayerPriority}.");
            }
            catch (Exception ex)
            {
                logger.LogError("[Init] Failed to register SAIN follower combat layer.");
                logger.LogError(ex);
            }
        }
    }
}
