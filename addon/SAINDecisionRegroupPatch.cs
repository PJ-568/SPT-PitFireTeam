using EFT;
using HarmonyLib;
using SAIN;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using System;
using System.Reflection;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINDecisionRegroupPatch
    {
        private const bool EnableDecisionDebugLogs = false;
        private static MethodInfo? _setDecisionsMethod;
        private static float _nextLogAt;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(BotDecisionManager), "getDecision");
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN BotDecisionManager.getDecision for regroup patch.");
                return;
            }

            _setDecisionsMethod = AccessTools.Method(
                typeof(BotDecisionManager),
                "SetDecisions",
                new[] { typeof(ECombatDecision), typeof(ESquadDecision), typeof(ESelfActionType), typeof(Enemy) });

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINDecisionRegroupPatch), nameof(Prefix_GetDecision)));
            Modules.Logger.LogInfo("[Init] SAIN regroup decision patch applied (forces DebugNoDecision while regroup active).");
        }

        private static bool Prefix_GetDecision(BotDecisionManager __instance)
        {
            try
            {
                BotOwner botOwner = __instance.BotOwner;
                if (botOwner == null || !friendlySAIN.ShouldSainRegroupLayerHandle(botOwner))
                {
                    return true;
                }

                BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(botOwner);
                if (follower == null || !follower.TryGetActiveCommand(out FollowerCommandType command, out _)
                    || command != FollowerCommandType.RegroupNearBoss)
                {
                    return true;
                }

                if (_setDecisionsMethod == null)
                {
                    return true;
                }

                Enemy? enemy = null;
                try
                {
                    enemy = __instance.Bot?.EnemyController?.ChooseEnemy();
                }
                catch
                {
                    // Keep patch resilient if SAIN enemy choose path throws during transient state.
                }

                _setDecisionsMethod.Invoke(__instance, new object[] { ECombatDecision.DebugNoDecision, ESquadDecision.None, ESelfActionType.None, enemy });

                if (EnableDecisionDebugLogs && Time.time >= _nextLogAt)
                {
                    _nextLogAt = Time.time + 1f;
                    Modules.Logger.LogInfo($"[SAIN Regroup] Decision override -> DebugNoDecision follower={botOwner.Profile?.Nickname ?? botOwner.name}");
                }

                return false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN Regroup] Decision patch exception: {ex}");
                return true;
            }
        }
    }
}
