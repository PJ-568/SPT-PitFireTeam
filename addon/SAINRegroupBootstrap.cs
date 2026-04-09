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
        private const int FollowerCombatLayerPriority = 73;

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
                SAINFollowerSquadLayerDisablePatch.Apply(harmony);
                SAINFollowerAimSwayPatch.Apply(harmony);
                SAINFollowerHitAccuracyPatch.Apply(harmony);
                SAINFollowerRecoilPatch.Apply(harmony);
                SAINFollowerFriendlyFirePatch.Apply(harmony);
                SAINFollowerGroupTalkDirectionPatch.Apply(harmony);
                SAINFollowerTalkMutePatch.Apply(harmony);
                SAINFollowerSearchCurrentEnemyLookPatch.Apply(harmony);
                SAINFollowerDoorPatch.Apply(harmony);

                if (SAINAddonToggles.EnableForcedEnemyRetention)
                {
                    SAINEnemyAcquireGatePatch.Apply(harmony);
                }

                SAINFollowerPersonalityPatch.Apply(harmony);
                SAINFollowerSquadLeaderPatch.Apply(harmony);
                SAINFollowerLowLightVisionPatch.Apply(harmony);
                SAINFollowerBushVisionPatch.Apply(harmony);
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
