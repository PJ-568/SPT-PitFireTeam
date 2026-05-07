using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Modules
{
    public static class FollowerCombatAnchor
    {
        private const float IndependentAllyAnchorDistance = 35f;
        private const float IndependentAllyAnchorDistanceSqr = IndependentAllyAnchorDistance * IndependentAllyAnchorDistance;

        public static Vector3 GetAnchorPosition(BotOwner botOwner)
        {
            if (ShouldUseRealBossAnchor(botOwner))
            {
                return GetRealBossPosition(botOwner);
            }

            if (TryGetClosestFollowerAnchor(botOwner, out Vector3 allyPosition))
            {
                return allyPosition;
            }

            return botOwner?.Position ?? Vector3.zero;
        }

        public static Vector3 GetRealBossPosition(BotOwner botOwner)
        {
            if (botOwner?.BotFollower?.BossToFollow is pitAIBossPlayer boss &&
                boss.realPlayer != null &&
                IsFinite(boss.realPlayer.Transform.position))
            {
                return boss.realPlayer.Transform.position;
            }

            Vector3? liveBossPosition = botOwner?.BotFollower?.BossToFollow?.Position;
            if (liveBossPosition.HasValue && IsFinite(liveBossPosition.Value))
            {
                return liveBossPosition.Value;
            }

            return botOwner?.Position ?? Vector3.zero;
        }

        public static bool IsCombatIndependent(BotOwner botOwner)
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.CombatIndependent == true;
        }

        private static bool ShouldUseRealBossAnchor(BotOwner botOwner)
        {
            BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(botOwner);
            return follower == null ||
                   !follower.CombatIndependent ||
                   follower.CombatRegroupUsesBossAnchor;
        }

        private static bool TryGetClosestFollowerAnchor(BotOwner botOwner, out Vector3 position)
        {
            position = default;
            if (botOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.Followers == null ||
                boss.Followers.Count == 0)
            {
                return false;
            }

            float bestDistanceSqr = float.MaxValue;
            BotOwner? bestFollower = null;
            foreach (BotOwner follower in boss.Followers)
            {
                if (follower == null ||
                    follower == botOwner ||
                    follower.IsDead ||
                    follower.BotState != EBotState.Active ||
                    follower.GetPlayer?.HealthController?.IsAlive != true ||
                    !IsFinite(follower.Position))
                {
                    continue;
                }

                float distanceSqr = (follower.Position - botOwner.Position).sqrMagnitude;
                if (distanceSqr > IndependentAllyAnchorDistanceSqr ||
                    distanceSqr >= bestDistanceSqr)
                {
                    continue;
                }

                bestDistanceSqr = distanceSqr;
                bestFollower = follower;
            }

            if (bestFollower == null)
            {
                return false;
            }

            position = bestFollower.Position;
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
