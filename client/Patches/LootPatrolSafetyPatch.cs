using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.Patches
{
    // SAIN can know a propagated hostile enemy while vanilla BigBrain is still running a peaceful layer
    // such as Looting or PatrolFollower. In that state the bot looks "hostile" in memory but never lets
    // SAIN's combat layer take control. This helper only intervenes for non-followers that are already
    // hostile to the player boss or one of our followers.
    internal static class HostilePeacefulLayerInterrupt
    {
        private static readonly Dictionary<Type, Func<object, BotOwner>> BotOwnerGetters = new Dictionary<Type, Func<object, BotOwner>>();

        public static void ForceEndActiveLayer(
            AICoreLayerClass<BotLogicDecision> layer,
            BotOwner botOwner,
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            string reason)
        {
            if (layer == null || botOwner == null)
            {
                return;
            }

            EndStatefulNonCombatAction(botOwner, currentDecision);
            layer.Action_0?.Invoke(new AICoreActionEndStruct(reason, true));
        }

        public static bool ShouldSuppressLayerUse(AICoreLayerClass<BotLogicDecision> layer)
        {
            if (layer == null)
            {
                return false;
            }

            return !IsCombatLayer(layer) && HasHostileBossOrFollowerEnemy(GetBotOwner(layer));
        }

        public static bool HasHostileBossOrFollowerEnemy(BotOwner botOwner)
        {
            if (botOwner == null || botOwner.IsDead || botOwner.BotState != EBotState.Active)
            {
                return false;
            }

            if (BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            BotMemoryClass memory = botOwner.Memory;
            if (memory == null || (!memory.HaveEnemy && memory.IsPeace && !memory.IsUnderFire))
            {
                return false;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (IsLiveBossOrFollower(goalEnemy?.Person))
            {
                return true;
            }

            BotsGroup group = botOwner.BotsGroup;
            if (group?.Enemies == null)
            {
                return false;
            }

            foreach (IPlayer enemy in group.Enemies.Keys)
            {
                if (IsLiveBossOrFollower(enemy))
                {
                    return true;
                }
            }

            return false;
        }

        public static void WakeHostileBot(BotOwner botOwner)
        {
            if (botOwner == null)
            {
                return;
            }

            try
            {
                // SAIN's combat layer uses BotOwner.IsBotActive(), which returns false while vanilla
                // standby is paused/goToSave. Waking standby here is narrower than touching SAIN state.
                BotStandBy standBy = botOwner.StandBy;
                if (standBy != null &&
                    standBy.StandByType != BotStandByType.none &&
                    standBy.StandByType != BotStandByType.active)
                {
                    standBy.Activate();
                }
            }
            catch
            {
                // Standby state is best-effort. If activation fails, leave the vanilla bot state untouched.
            }
        }

        public static BotOwner GetBotOwner(object layer)
        {
            Type type = layer.GetType();
            if (!BotOwnerGetters.TryGetValue(type, out Func<object, BotOwner> getter))
            {
                getter = LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(type);
                BotOwnerGetters[type] = getter;
            }

            return getter?.Invoke(layer);
        }

        private static bool IsLiveBossOrFollower(IPlayer player)
        {
            if (player == null || player.HealthController?.IsAlive != true || string.IsNullOrEmpty(player.ProfileId))
            {
                return false;
            }

            return BossPlayers.IsPlayerBoss(player.ProfileId) ||
                   BossPlayers.IsFollowerProfileId(player.ProfileId);
        }

        private static bool IsCombatLayer(AICoreLayerClass<BotLogicDecision> layer)
        {
            string name = SafeName(layer);
            return name.IndexOf("Combat", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EndStatefulNonCombatAction(BotOwner botOwner, AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (botOwner == null)
            {
                return;
            }

            switch (currentDecision.Action)
            {
                case BotLogicDecision.simplePatrol:
                case BotLogicDecision.followerPatrol:
                case BotLogicDecision.alternativePatrol:
                case BotLogicDecision.goToLootPointNode:
                case BotLogicDecision.goToPoint:
                case BotLogicDecision.goToCoverPointTactical:
                case BotLogicDecision.botTakeItem:
                case BotLogicDecision.deadBody:
                    try
                    {
                        botOwner.BotRun?.EndMove();
                    }
                    catch
                    {
                        // Best-effort cleanup only. The layer handoff itself is the important part.
                    }

                    try
                    {
                        botOwner.PatrollingData?.LootData?.StopLootCluster();
                    }
                    catch
                    {
                        // Loot/patrol internals are not guaranteed to be initialized for every layer.
                    }
                    break;
            }
        }

        public static string SafeName(AICoreLayerClass<BotLogicDecision> layer)
        {
            try
            {
                return layer.Name() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    internal class HostileNonCombatActiveLayerFilterPatch : ModulePatch
    {
        private static FieldInfo _activeLayerListField;
        private static PropertyInfo _activeLayerProperty;
        private static bool _resolved;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "Update");
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void PatchPrefix(
            object __instance,
            AICoreActionResultStruct<BotLogicDecision, GClass26> prevResult,
            ref FilterState __state)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                Resolve(__instance.GetType());
                if (_activeLayerListField == null)
                {
                    return;
                }

                List<AICoreLayerClass<BotLogicDecision>> activeLayerList =
                    _activeLayerListField.GetValue(__instance) as List<AICoreLayerClass<BotLogicDecision>>;
                if (activeLayerList == null || activeLayerList.Count == 0)
                {
                    return;
                }

                BotOwner botOwner = GetAnyBotOwner(activeLayerList);
                if (!HostilePeacefulLayerInterrupt.HasHostileBossOrFollowerEnemy(botOwner))
                {
                    return;
                }

                HostilePeacefulLayerInterrupt.WakeHostileBot(botOwner);

                // AICoreStrategyAbstractClass.Update selects the first active layer from List_0.
                // For this single update, hide peaceful layers so an already-hostile bot can fall
                // through to SAIN/vanilla combat. The finalizer restores the list immediately after.
                AICoreLayerClass<BotLogicDecision> activeLayer =
                    _activeLayerProperty?.GetValue(__instance, null) as AICoreLayerClass<BotLogicDecision>;
                FilterState state = null;
                for (int i = activeLayerList.Count - 1; i >= 0; i--)
                {
                    AICoreLayerClass<BotLogicDecision> layer = activeLayerList[i];
                    if (layer == null || !HostilePeacefulLayerInterrupt.ShouldSuppressLayerUse(layer))
                    {
                        continue;
                    }

                    state ??= new FilterState(activeLayerList);
                    state.Removed.Add(new RemovedLayer(i, layer));
                    activeLayerList.RemoveAt(i);

                    if (ReferenceEquals(layer, activeLayer))
                    {
                        HostilePeacefulLayerInterrupt.ForceEndActiveLayer(
                            layer,
                            botOwner,
                            prevResult,
                            "pitFireTeamHostileEnemy");
                    }
                }

                __state = state;
            }
            catch (Exception ex)
            {
                Logger.LogError("HostileNonCombatActiveLayerFilterPatch prefix failed");
                Logger.LogError(ex);
            }
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(Exception __exception, FilterState __state)
        {
            try
            {
                __state?.Restore();
            }
            catch (Exception ex)
            {
                Logger.LogError("HostileNonCombatActiveLayerFilterPatch restore failed");
                Logger.LogError(ex);
            }

            return __exception;
        }

        private static void Resolve(Type strategyType)
        {
            if (_resolved) return;
            _resolved = true;

            for (Type type = strategyType; type != null; type = type.BaseType)
            {
                if (_activeLayerListField == null)
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
                }

                if (_activeLayerProperty == null)
                {
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (property.PropertyType == typeof(AICoreLayerClass<BotLogicDecision>))
                        {
                            _activeLayerProperty = property;
                            break;
                        }
                    }
                }

                if (_activeLayerListField != null && _activeLayerProperty != null)
                {
                    break;
                }
            }
        }

        private static BotOwner GetAnyBotOwner(List<AICoreLayerClass<BotLogicDecision>> activeLayerList)
        {
            for (int i = 0; i < activeLayerList.Count; i++)
            {
                BotOwner botOwner = HostilePeacefulLayerInterrupt.GetBotOwner(activeLayerList[i]);
                if (botOwner != null)
                {
                    return botOwner;
                }
            }

            return null;
        }

        private sealed class FilterState
        {
            private readonly List<AICoreLayerClass<BotLogicDecision>> _list;

            public FilterState(List<AICoreLayerClass<BotLogicDecision>> list)
            {
                _list = list;
            }

            public List<RemovedLayer> Removed { get; } = new List<RemovedLayer>();

            public void Restore()
            {
                for (int i = Removed.Count - 1; i >= 0; i--)
                {
                    RemovedLayer removed = Removed[i];
                    int index = Math.Max(0, Math.Min(removed.Index, _list.Count));
                    if (!_list.Contains(removed.Layer))
                    {
                        _list.Insert(index, removed.Layer);
                    }
                }
            }
        }

        private readonly struct RemovedLayer
        {
            public RemovedLayer(int index, AICoreLayerClass<BotLogicDecision> layer)
            {
                Index = index;
                Layer = layer;
            }

            public int Index { get; }
            public AICoreLayerClass<BotLogicDecision> Layer { get; }
        }
    }

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
                if (!IsConfirmedFollower(botOwner)) return;

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

        private static bool IsConfirmedFollower(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            if (botOwner.BotFollower == null || !botOwner.BotFollower.HaveBoss) return false;
            if (botOwner.BotFollower.BossToFollow is not Components.pitAIBossPlayer) return false;
            return BossPlayers.Instance?.GetFollower(botOwner) != null;
        }
    }

    // Hard-stop vanilla LootPatrol decision for followers if it still leaks through active-layer filtering.
    internal class LootPatrolDecisionBypassPatch : ModulePatch
    {
        private static Func<object, BotOwner> _botOwnerGetter;
        private static float _nextExceptionLogAt;

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
                if (botOwner == null)
                {
                    __result = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        default,
                        "pitFireTeam_skipLootPatrol_no_owner");
                    return false;
                }

                if (!IsConfirmedFollower(botOwner)) return true;

                __result = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BaseLogicLayerAbstractClass.HoldOrCover(botOwner),
                    "pitFireTeam_skipLootPatrol");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError("LootPatrolDecisionBypassPatch failed");
                Logger.LogError(ex);
                return true;
            }
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(
            GClass117 __instance,
            Exception __exception,
            ref AICoreActionResultStruct<BotLogicDecision, GClass26> __result)
        {
            if (__exception == null) return null;

            BotOwner botOwner = null;
            try
            {
                if (_botOwnerGetter == null && __instance != null)
                {
                    _botOwnerGetter = LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(__instance.GetType());
                }

                botOwner = _botOwnerGetter?.Invoke(__instance);
                __result = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    botOwner != null ? BaseLogicLayerAbstractClass.HoldOrCover(botOwner) : default,
                    "pitFireTeam_lootPatrol_exception_guard");
            }
            catch
            {
                __result = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    default,
                    "pitFireTeam_lootPatrol_exception_guard_failed");
            }

            if (UnityEngine.Time.time >= _nextExceptionLogAt)
            {
                _nextExceptionLogAt = UnityEngine.Time.time + 1f;
                Logger.LogError("LootPatrol GetDecision exception suppressed to keep AI update alive");
                Logger.LogError(__exception);
            }

            return null;
        }

        private static bool IsConfirmedFollower(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            if (botOwner.BotFollower == null || !botOwner.BotFollower.HaveBoss) return false;
            if (botOwner.BotFollower.BossToFollow is not Components.pitAIBossPlayer) return false;
            return BossPlayers.Instance?.GetFollower(botOwner) != null;
        }
    }

    // Prevent followers from being trapped in vanilla AdvAssaultTarget due to stale GoalTarget/ZeroGoalTarget.
    internal class AdvAssaultTargetFollowerGuardPatch : ModulePatch
    {
        private static Func<object, BotOwner> _botOwnerGetter;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class99), "ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(Class99 __instance, ref bool __result)
        {
            if (__instance == null) return true;

            _botOwnerGetter ??= LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(__instance.GetType());
            BotOwner botOwner = _botOwnerGetter?.Invoke(__instance);
            if (!IsConfirmedFollower(botOwner))
            {
                return true;
            }

            if (botOwner.Memory?.HaveEnemy != true && botOwner.Memory?.GoalTarget?.HaveMainTarget() == true)
            {
                botOwner.Memory.GoalTarget.Clear();
            }

            __result = false;
            return false;
        }

        private static bool IsConfirmedFollower(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            if (botOwner.BotFollower == null || !botOwner.BotFollower.HaveBoss) return false;
            if (botOwner.BotFollower.BossToFollow is not Components.pitAIBossPlayer) return false;
            return BossPlayers.Instance?.GetFollower(botOwner) != null;
        }
    }
}
