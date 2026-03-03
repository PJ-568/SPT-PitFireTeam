using Comfort.Common;
using EFT;
using SAIN;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerEnemyRetentionService
    {
        private sealed class EnemyRetentionState
        {
            public bool LastHaveEnemyKnown;
            public bool LastHaveEnemy;
            public string LastCommittedEnemyId;
            public float LastCommittedAt;
            public float LastCommittedDistanceMeters = float.MaxValue;
            public string LastSainSyncedEnemyId;
        }

        private const string SubscriptionId = "sainaddon.enemyretention";
        private const float RetentionGraceSeconds = 1.0f;
        private const float CloseEnemyDistanceMeters = 50f;
        private const float CloseRetentionGraceSeconds = 2.5f;
        private const bool EnableBridgeDebugLogs = true;

        private static readonly Dictionary<string, EnemyRetentionState> States = new Dictionary<string, EnemyRetentionState>();
        private static readonly object SyncRoot = new object();
        private static bool _initialized;

        public static void Initialize()
        {
            if (!SAINAddonToggles.EnableForcedEnemyRetention) return;
            if (_initialized) return;
            _initialized = true;
            BotOwnerUpdateHub.Register(SubscriptionId, OnBotOwnerUpdate);
        }

        public static bool ShouldAllowAcquire(BotOwner owner, out string reason)
        {
            if (!SAINAddonToggles.EnableForcedEnemyRetention)
            {
                reason = "retention_disabled";
                return true;
            }

            reason = "allow_non_follower";
            if (owner == null) return false;
            if (!BossPlayers.IsFollower(owner)) return true;
            if (FollowerEnemyEnforceSuppression.IsSuppressed(owner))
            {
                reason = "blocked_attention_suppression";
                return false;
            }
            if (owner.Memory?.HaveEnemy == true)
            {
                reason = "allow_memory_have_enemy";
                return true;
            }

            if (HasRecentCommit(owner.ProfileId, out float age))
            {
                reason = $"allow_sticky_recent age={age:F2}s";
                return true;
            }

            reason = "owner.Memory.HaveEnemy=false_no_recent_commit";
            return false;
        }

        private static bool HasRecentCommit(string profileId, out float age)
        {
            age = float.MaxValue;
            if (string.IsNullOrEmpty(profileId)) return false;

            EnemyRetentionState state = GetState(profileId, create: false);
            if (state == null || string.IsNullOrEmpty(state.LastCommittedEnemyId))
            {
                return false;
            }

            age = Time.time - state.LastCommittedAt;
            return age <= GetRetentionGraceSeconds(state);
        }

        private static void OnBotOwnerUpdate(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId)) return;

            if (!BossPlayers.IsFollower(owner))
            {
                RemoveState(owner.ProfileId);
                return;
            }

            if (FollowerEnemyEnforceSuppression.IsSuppressed(owner))
            {
                // Attention/Look should hard-reset retention so sticky enemy cannot bounce back after suppression ends.
                RemoveState(owner.ProfileId);
                return;
            }

            EnemyRetentionState state = GetState(owner.ProfileId, create: true);
            bool haveEnemy = owner.Memory?.HaveEnemy == true;
            EnemyInfo goal = owner.Memory?.GoalEnemy;

            if (haveEnemy && goal != null && goal.ProfileId == owner.BotFollower.BossToFollow?.Player()?.ProfileId)
            {
                haveEnemy = false;
                goal = null;
                state.LastCommittedEnemyId = "";
                state.LastCommittedAt = 0f;
                state.LastCommittedDistanceMeters = float.MaxValue;
                owner.Memory.GoalEnemy = null;
            }

            if (haveEnemy && !string.IsNullOrEmpty(goal?.ProfileId))
            {
                state.LastCommittedEnemyId = goal.ProfileId;
                state.LastCommittedAt = Time.time;
                state.LastCommittedDistanceMeters = GetEnemyDistance(owner, goal);
            }
            else if (!haveEnemy)
            {
                if (TryReapplyStickyEnemy(owner, state, out _))
                {
                    haveEnemy = owner.Memory?.HaveEnemy == true;
                    goal = owner.Memory?.GoalEnemy;
                    if (haveEnemy && !string.IsNullOrEmpty(goal?.ProfileId))
                    {
                        state.LastCommittedEnemyId = goal.ProfileId;
                        state.LastCommittedAt = Time.time;
                        state.LastCommittedDistanceMeters = GetEnemyDistance(owner, goal);
                    }
                }
            }

            if (!haveEnemy)
            {
                if (!string.IsNullOrEmpty(state.LastSainSyncedEnemyId) && Time.time - state.LastCommittedAt > GetRetentionGraceSeconds(state))
                {
                    if (EnableBridgeDebugLogs)
                    {
                        Modules.Logger.LogInfo(
                            $"[SAIN Bridge] reset bot={owner.Profile?.Nickname ?? owner.name} " +
                            $"prevEnemy={state.LastSainSyncedEnemyId} reason=grace_expired");
                    }
                    state.LastSainSyncedEnemyId = null;
                }
            }
            else if (goal?.Person != null)
            {
                TrySyncGoalEnemyToSain(owner, goal, state);
            }

            state.LastHaveEnemy = haveEnemy;
            state.LastHaveEnemyKnown = true;
        }

        private static bool TryReapplyStickyEnemy(BotOwner owner, EnemyRetentionState state, out string enemyId)
        {
            enemyId = state?.LastCommittedEnemyId;
            if (owner == null || state == null || string.IsNullOrEmpty(enemyId)) return false;

            float age = Time.time - state.LastCommittedAt;
            if (age > GetRetentionGraceSeconds(state)) return false;

            Player enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyId);
            if (enemyPlayer == null || enemyPlayer.HealthController?.IsAlive != true) return false;

            try
            {
                EnemyInfo info = global::friendlySAIN.Utils.Enemy.MakeEnemy(owner, enemyPlayer);
                if (info == null)
                {
                    BotSettingsClass botSettings = new BotSettingsClass(enemyPlayer, owner.BotsGroup, EBotEnemyCause.addPlayerToBoss)
                    {
                        EnemyLastPosition = enemyPlayer.Position
                    };
                    owner.Memory.IsPeace = false;
                    owner.Memory.AddEnemy(enemyPlayer, botSettings, false);
                    PromoteEnemyAsGoal(owner, enemyId);
                    info = owner.Memory?.GoalEnemy;
                }

                if (info == null) return false;

                owner.Memory.IsPeace = false;
                PromoteEnemyAsGoal(owner, enemyId);
                return owner.Memory?.HaveEnemy == true;
            }
            catch
            {
                return false;
            }
        }

        private static void PromoteEnemyAsGoal(BotOwner owner, string enemyProfileId)
        {
            if (owner?.EnemiesController?.EnemyInfos == null || string.IsNullOrEmpty(enemyProfileId)) return;

            foreach (var item in owner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId != enemyProfileId) continue;
                EnemyInfo promoted = item.Value;
                if (promoted == null) return;
                promoted.PriorityIndex = 0;
                owner.Memory.GoalEnemy = promoted;
                return;
            }
        }

        private static void TrySyncGoalEnemyToSain(BotOwner owner, EnemyInfo goal, EnemyRetentionState state)
        {
            if (owner == null || goal == null || goal.Person == null || state == null) return;

            string goalId = goal.ProfileId;
            if (string.IsNullOrEmpty(goalId)) return;
            if (string.Equals(state.LastSainSyncedEnemyId, goalId, StringComparison.Ordinal))
            {
                return;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent sainBot) || sainBot?.EnemyController == null)
            {
                if (EnableBridgeDebugLogs)
                {
                    Modules.Logger.LogInfo(
                        $"[SAIN Bridge] bot={owner.Profile?.Nickname ?? owner.name} enemy={goalId} " +
                        "result=skip reason=sainBot_null");
                }
                return;
            }

            SAINEnemyController enemyController = sainBot.EnemyController;
            Enemy bridgeResult = enemyController.CheckAddEnemy(goal.Person);
            bool hasEnemyAfterAdd = bridgeResult != null || enemyController.GetEnemy(goalId, false) != null;

            if (hasEnemyAfterAdd)
            {
                state.LastSainSyncedEnemyId = goalId;
            }

            if (EnableBridgeDebugLogs)
            {
                string sainGoal = enemyController.GoalEnemy?.EnemyProfileId ?? "<none>";
                Modules.Logger.LogInfo(
                    $"[SAIN Bridge] bot={owner.Profile?.Nickname ?? owner.name} enemy={goalId} " +
                    $"result={(hasEnemyAfterAdd ? "ok" : "retry")} returnType={bridgeResult?.GetType().Name ?? "<null>"} " +
                    $"sainGoal={sainGoal}");
            }
        }

        private static EnemyRetentionState GetState(string profileId, bool create)
        {
            if (string.IsNullOrEmpty(profileId)) return null;

            lock (SyncRoot)
            {
                if (!States.TryGetValue(profileId, out EnemyRetentionState state) && create)
                {
                    state = new EnemyRetentionState();
                    States[profileId] = state;
                }
                return state;
            }
        }

        private static void RemoveState(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;
            lock (SyncRoot)
            {
                States.Remove(profileId);
            }
        }

        private static float GetRetentionGraceSeconds(EnemyRetentionState state)
        {
            if (state == null) return RetentionGraceSeconds;
            return state.LastCommittedDistanceMeters <= CloseEnemyDistanceMeters
                ? CloseRetentionGraceSeconds
                : RetentionGraceSeconds;
        }

        private static float GetEnemyDistance(BotOwner owner, EnemyInfo goal)
        {
            if (owner == null || goal == null) return float.MaxValue;

            Vector3 ownerPos = owner.Position;
            Vector3 enemyPos = goal.Person != null ? goal.Person.Position : goal.EnemyLastPosition;
            return Vector3.Distance(ownerPos, enemyPos);
        }
    }
}
