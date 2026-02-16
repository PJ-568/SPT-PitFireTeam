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
        private static Func<object, BotOwner> _layerBotOwnerGetter;
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

            _layerBotOwnerGetter = BuildBotOwnerGetter(typeof(AICoreLayerClass<BotLogicDecision>));
        }

        private static BotOwner GetBotOwner(List<AICoreLayerClass<BotLogicDecision>> activeLayerList)
        {
            if (_layerBotOwnerGetter == null) return null;

            for (int i = 0; i < activeLayerList.Count; i++)
            {
                object layer = activeLayerList[i];
                if (layer == null) continue;
                BotOwner botOwner = _layerBotOwnerGetter(layer);
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

        internal static Func<object, BotOwner> BuildBotOwnerGetter(Type layerType)
        {
            if (layerType == null) return null;

            for (Type type = layerType; type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (typeof(BotOwner).IsAssignableFrom(field.FieldType))
                    {
                        return instance => instance == null ? null : field.GetValue(instance) as BotOwner;
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    if (typeof(BotOwner).IsAssignableFrom(property.PropertyType))
                    {
                        return instance => instance == null ? null : property.GetValue(instance, null) as BotOwner;
                    }
                }
            }

            return null;
        }
    }

    // Hard-stop vanilla LootPatrol decision for followers if it still leaks through active-layer filtering.
    internal class LootPatrolDecisionBypassPatch : ModulePatch
    {
        private static Func<object, BotOwner> _botOwnerGetter;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass117), "GetDecision");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass117 __instance, ref AICoreActionResultStruct<BotLogicDecision, GClass26> __result)
        {
            try
            {
                if (__instance == null) return true;

                if (_botOwnerGetter == null)
                {
                    _botOwnerGetter = LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(__instance.GetType());
                }

                BotOwner botOwner = _botOwnerGetter?.Invoke(__instance);
                if (botOwner == null) return true;

                if (!BossPlayers.IsFollower(botOwner)) return true;

                __result = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BaseLogicLayerAbstractClass.HoldOrCover(botOwner),
                    "friendlySAIN_skipLootPatrol");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("LootPatrolDecisionBypassPatch failed");
                Logger.LogError(ex);
                return true;
            }
        }
    }
}
