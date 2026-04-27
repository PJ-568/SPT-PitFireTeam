using Comfort.Common;
using EFT;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Modules
{
    public static class FollowerContactEnemyRetention
    {
        private const float RetainSeconds = 12f;

        private sealed class RetainedContact
        {
            public string EnemyProfileId = string.Empty;
            public bool WasVisible;
            public bool Prioritized;
            public float Until;
            public int RestoreCount;
            public float NextRestoreLogAt;
            public int BlockedClearCount;
            public float NextBlockedClearLogAt;
        }

        private static readonly Dictionary<string, RetainedContact> RetainedByFollowerId = new(StringComparer.Ordinal);

        public static void Register(BotOwner follower, Player enemy, bool visible, bool prioritized)
        {
            if (follower == null || enemy == null || string.IsNullOrEmpty(follower.ProfileId) || string.IsNullOrEmpty(enemy.ProfileId))
            {
                return;
            }

            if (RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact existing) &&
                Time.time <= existing.Until)
            {
                if (!prioritized && existing.Prioritized && existing.EnemyProfileId != enemy.ProfileId)
                {
                    return;
                }

                if (existing.EnemyProfileId == enemy.ProfileId)
                {
                    existing.WasVisible |= visible;
                    existing.Prioritized |= prioritized;
                    existing.Until = Time.time + RetainSeconds;
                    existing.RestoreCount = 0;
                    existing.NextRestoreLogAt = 0f;
                    existing.BlockedClearCount = 0;
                    existing.NextBlockedClearLogAt = 0f;
                    return;
                }
            }

            RetainedByFollowerId[follower.ProfileId] = new RetainedContact
            {
                EnemyProfileId = enemy.ProfileId,
                WasVisible = visible,
                Prioritized = prioritized,
                Until = Time.time + RetainSeconds,
                RestoreCount = 0,
                NextRestoreLogAt = 0f,
                BlockedClearCount = 0,
                NextBlockedClearLogAt = 0f,
            };
        }

        public static bool RegisterCurrentGoal(BotOwner follower, bool prioritized)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (follower == null || goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return false;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                return false;
            }

            Register(follower, enemy, goalEnemy.IsVisible || goalEnemy.CanShoot, prioritized);
            return true;
        }

        public static bool HasActiveRetainedContact(BotOwner follower)
        {
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time <= contact.Until)
            {
                Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
                if (enemy?.HealthController?.IsAlive == true)
                {
                    return !HasDifferentLiveVisibleGoal(follower, contact.EnemyProfileId);
                }
            }

            RetainedByFollowerId.Remove(follower.ProfileId);
            return false;
        }

        public static bool TryGetActiveRetainedEnemy(BotOwner follower, out Player? enemy, out bool prioritized)
        {
            enemy = null;
            prioritized = false;
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (HasDifferentLiveVisibleGoal(follower, contact.EnemyProfileId))
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                enemy = null;
                return false;
            }

            prioritized = contact.Prioritized;
            return true;
        }

        public static bool ShouldBlockGoalEnemyClear(BotOwner follower, EnemyInfo? currentGoal)
        {
            if (follower == null || currentGoal == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until || currentGoal.ProfileId != contact.EnemyProfileId)
            {
                return false;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            contact.BlockedClearCount++;
            if (contact.BlockedClearCount == 1 || Time.time >= contact.NextBlockedClearLogAt)
            {
                contact.NextBlockedClearLogAt = Time.time + 1f;
            }

            return true;
        }

        public static bool TryRestore(BotOwner follower, out EnemyInfo? restored)
        {
            restored = null;
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return false;
            }

            if (!RetainedByFollowerId.TryGetValue(follower.ProfileId, out RetainedContact contact))
            {
                return false;
            }

            if (Time.time > contact.Until)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (HasDifferentLiveVisibleGoal(follower, contact.EnemyProfileId))
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            Player enemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId);
            if (enemy?.HealthController?.IsAlive != true)
            {
                RetainedByFollowerId.Remove(follower.ProfileId);
                return false;
            }

            if (follower.Memory?.GoalEnemy?.ProfileId == contact.EnemyProfileId)
            {
                restored = follower.Memory.GoalEnemy;
                return true;
            }

            EnemyInfo? info = Enemy.MakeEnemy(follower, enemy, EBotEnemyCause.checkAddTODO);
            if (info == null)
            {
                return false;
            }

            info.PriorityIndex = 0;
            info.PersonalLastPos = enemy.Position;
            info.SetVisible(contact.WasVisible);
            Enemy.RepairPersonalMemory(info, enemy.Position, contact.WasVisible || info.HaveSeen || info.CanShoot);
            follower.Memory.IsPeace = false;
            follower.Memory.GoalEnemy = info;
            restored = info;

            contact.RestoreCount++;
            if (contact.RestoreCount == 1 || Time.time >= contact.NextRestoreLogAt)
            {
                contact.NextRestoreLogAt = Time.time + 1f;
            }

            return true;
        }

        private static bool HasDifferentLiveVisibleGoal(BotOwner follower, string retainedEnemyProfileId)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                string.Equals(goalEnemy.ProfileId, retainedEnemyProfileId, StringComparison.Ordinal) ||
                goalEnemy.Person?.HealthController?.IsAlive != true)
            {
                return false;
            }

            return goalEnemy.IsVisible || goalEnemy.CanShoot;
        }

        public static void Clear(BotOwner follower)
        {
            if (string.IsNullOrEmpty(follower?.ProfileId))
            {
                return;
            }

            RetainedByFollowerId.Remove(follower.ProfileId);
        }
    }
}
