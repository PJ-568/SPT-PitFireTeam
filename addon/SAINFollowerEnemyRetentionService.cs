using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerEnemyRetentionService
    {
        public static bool ShouldAllowAcquire(BotOwner owner, IPlayer enemy, out string reason)
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

            string enemyId = enemy.ProfileId;

            if (BossPlayers.IsPlayerBoss(enemyId))
            {
                reason = "is_player_boss";
                return false;
            }

            BotOwner allied = owner.BotFollower.BossToFollow?.Followers.Find(f => string.Equals(f.ProfileId, enemyId, StringComparison.Ordinal));
            if (allied != null)
            {
                reason = "is_allied_follower";
                return false;
            }

            return true;
        }

        public static bool ShouldAllowSameSideAcquire(BotOwner owner, IPlayer enemy, out string reason)
        {
            reason = "allow_non_same_side";
            if (owner == null || enemy == null)
            {
                return false;
            }

            if (owner.Side != enemy.Side)
            {
                return true;
            }

            if (!enemy.IsAI)
            {
                reason = "blocked_same_side_human";
                return false;
            }

            Player enemyPlayer = enemy as Player;
            if (enemyPlayer == null)
            {
                reason = "blocked_same_side_non_player";
                return false;
            }

            bool hostileIntent =
                FollowerCalcGoalEnemyAcquire.CandidateHasBossOrFollowerAsEnemy(owner, enemyPlayer) ||
                FollowerCalcGoalEnemyAcquire.CandidateHasGoalEnemyBossOrFollower(owner, enemyPlayer);
            hostileIntent = FollowerCalcGoalEnemyAcquire.HasDebouncedSameSideHostileIntent(owner, enemy.ProfileId, hostileIntent);
            if (!hostileIntent)
            {
                reason = "blocked_same_side_no_hostile_intent";
                return false;
            }

            reason = "allow_same_side_hostile_intent";
            return true;
        }
    }
}
