using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.Patches
{

    // BigBrain scans active layers every update. Ensure crashed vanilla LootPatrol layer is removed from that list first.
    internal class LootPatrolActiveLayerListPatch : ModulePatch
    {
        private static FieldInfo _activeLayerListField;
        private static PropertyInfo _activeLayerProperty;
        private static FieldInfo _layerBotOwnerField;
        private static bool _resolved;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "Update");
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void PatchPrefix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                Resolve(__instance.GetType());
                if (_activeLayerListField == null) return;

                List<AICoreLayerClass<BotLogicDecision>> activeLayerList =
                    _activeLayerListField.GetValue(__instance) as List<AICoreLayerClass<BotLogicDecision>>;
                if (activeLayerList == null || activeLayerList.Count == 0) return;

                BotOwner botOwner = GetBotOwner(activeLayerList);
                if (botOwner == null || !BossPlayers.IsFollower(botOwner)) return;

                bool removed = false;
                for (int i = activeLayerList.Count - 1; i >= 0; i--)
                {
                    AICoreLayerClass<BotLogicDecision> layer = activeLayerList[i];
                    if (layer == null) continue;
                    if (!IsVanillaLootPatrol(layer)) continue;
                    activeLayerList.RemoveAt(i);
                    removed = true;
                }

                if (!removed || _activeLayerProperty == null || !_activeLayerProperty.CanRead || !_activeLayerProperty.CanWrite)
                    return;

                AICoreLayerClass<BotLogicDecision> activeLayer =
                    _activeLayerProperty.GetValue(__instance, null) as AICoreLayerClass<BotLogicDecision>;
                if (activeLayer != null && IsVanillaLootPatrol(activeLayer))
                {
                    _activeLayerProperty.SetValue(__instance, null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("LootPatrolActiveLayerListPatch failed");
                Logger.LogError(ex);
            }
        }

        private static void Resolve(Type strategyType)
        {
            if (_resolved) return;
            _resolved = true;

            Type type = strategyType;
            while (type != null && _activeLayerListField == null)
            {
                _activeLayerListField = AccessTools.Field(type, "List_0");
                if (_activeLayerListField == null)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (field.FieldType == typeof(List<AICoreLayerClass<BotLogicDecision>>))
                        {
                            _activeLayerListField = field;
                            break;
                        }
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property.PropertyType == typeof(AICoreLayerClass<BotLogicDecision>))
                    {
                        _activeLayerProperty = property;
                        break;
                    }
                }

                type = type.BaseType;
            }

            _layerBotOwnerField = AccessTools.Field(typeof(AICoreLayerClass<BotLogicDecision>), "BotOwner_0");
        }

        private static BotOwner GetBotOwner(List<AICoreLayerClass<BotLogicDecision>> activeLayerList)
        {
            if (_layerBotOwnerField == null) return null;

            for (int i = 0; i < activeLayerList.Count; i++)
            {
                object layer = activeLayerList[i];
                if (layer == null) continue;
                BotOwner botOwner = _layerBotOwnerField.GetValue(layer) as BotOwner;
                if (botOwner != null) return botOwner;
            }
            return null;
        }

        private static bool IsVanillaLootPatrol(AICoreLayerClass<BotLogicDecision> layer)
        {
            if (layer == null) return false;
            if (layer.GetType() == typeof(GClass117)) return true;

            string name = string.Empty;
            try
            {
                name = layer.Name() ?? string.Empty;
            }
            catch
            {
                // ignore name failures
            }

            return name.IndexOf("LootPatrol", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
