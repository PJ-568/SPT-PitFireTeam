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
        private const int RegroupLayerPriority = 73;

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
                SAINDecisionRegroupPatch.Apply(harmony);
                BrainManager.AddCustomLayer(typeof(SAINRegroupLayer), brains, RegroupLayerPriority);
                logger.LogInfo($"[Init] SAIN regroup layer registered at priority {RegroupLayerPriority}.");
            }
            catch (Exception ex)
            {
                logger.LogError("[Init] Failed to register SAIN regroup layer.");
                logger.LogError(ex);
            }
        }
    }
}
