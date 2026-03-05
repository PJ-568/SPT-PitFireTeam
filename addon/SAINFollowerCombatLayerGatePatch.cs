using EFT;
using HarmonyLib;
using friendlySAIN.Modules;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerCombatLayerGatePatch
    {
        private static MethodInfo? _soloIsActiveMethod;
        private static MethodInfo? _squadIsActiveMethod;

        public static void Apply(Harmony harmony)
        {
            Type? soloLayerType = AccessTools.TypeByName("SAIN.Layers.Combat.Solo.CombatSoloLayer");
            if (soloLayerType != null)
            {
                _soloIsActiveMethod = AccessTools.Method(soloLayerType, "IsActive", Type.EmptyTypes);
                if (_soloIsActiveMethod != null)
                {
                    harmony.Patch(_soloIsActiveMethod, prefix: new HarmonyMethod(typeof(SAINFollowerCombatLayerGatePatch), nameof(Prefix_IsActive)));
                }
            }

            Type? squadLayerType = AccessTools.TypeByName("SAIN.Layers.Combat.Squad.CombatSquadLayer");
            if (squadLayerType != null)
            {
                _squadIsActiveMethod = AccessTools.Method(squadLayerType, "IsActive", Type.EmptyTypes);
                if (_squadIsActiveMethod != null)
                {
                    harmony.Patch(_squadIsActiveMethod, prefix: new HarmonyMethod(typeof(SAINFollowerCombatLayerGatePatch), nameof(Prefix_IsActive)));
                }
            }

            if (_soloIsActiveMethod == null && _squadIsActiveMethod == null)
            {
                Modules.Logger.LogError("[Init] Failed to patch SAIN combat solo/squad layer IsActive for follower combat gate.");
                return;
            }

            Modules.Logger.LogInfo("[Init] SAIN follower combat layer gate patch applied.");
        }

        private static bool Prefix_IsActive(object __instance, ref bool __result)
        {
            BotOwner? owner = GetBotOwner(__instance);
            if (owner != null && SAINFollowerCombatLayer.IsFollowerCombatLayerActive(owner))
            {
                __result = false;
                return false;
            }

            return true;
        }

        private static BotOwner? GetBotOwner(object layerInstance)
        {
            if (layerInstance == null)
            {
                return null;
            }

            Type type = layerInstance.GetType();
            PropertyInfo? ownerProperty = AccessTools.Property(type, "BotOwner");
            if (ownerProperty != null)
            {
                return ownerProperty.GetValue(layerInstance, null) as BotOwner;
            }

            FieldInfo? ownerField = AccessTools.Field(type, "BotOwner") ?? AccessTools.Field(type, "_botOwner");
            return ownerField?.GetValue(layerInstance) as BotOwner;
        }
    }
}
