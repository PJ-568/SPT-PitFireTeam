using EFT;
using HarmonyLib;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerSquadLayerDisablePatch
    {
        private static MethodInfo? _combatSquadIsActiveMethod;

        public static void Apply(Harmony harmony)
        {
            Type? squadLayerType = AccessTools.TypeByName("SAIN.Layers.Combat.Squad.CombatSquadLayer");
            if (squadLayerType == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN CombatSquadLayer type for follower disable patch.");
                return;
            }

            _combatSquadIsActiveMethod = AccessTools.Method(squadLayerType, "IsActive", Type.EmptyTypes);
            if (_combatSquadIsActiveMethod == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN CombatSquadLayer.IsActive for follower disable patch.");
                return;
            }

            harmony.Patch(_combatSquadIsActiveMethod, prefix: new HarmonyMethod(typeof(SAINFollowerSquadLayerDisablePatch), nameof(Prefix_IsActive)));
            Modules.Logger.LogInfo("[Init] SAIN CombatSquadLayer disabled for followers.");
        }

        private static bool Prefix_IsActive(object __instance, ref bool __result)
        {
            BotOwner? owner = GetBotOwner(__instance);
            if (owner != null && BossPlayers.IsFollower(owner))
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
            return AccessTools.Property(type, "BotOwner")?.GetValue(layerInstance) as BotOwner
                ?? AccessTools.Field(type, "BotOwner")?.GetValue(layerInstance) as BotOwner
                ?? AccessTools.Field(type, "_botOwner")?.GetValue(layerInstance) as BotOwner
                ?? AccessTools.Field(type, "BotOwner_0")?.GetValue(layerInstance) as BotOwner
                ?? AccessTools.Field(type, "botOwner_0")?.GetValue(layerInstance) as BotOwner;
        }
    }
}
