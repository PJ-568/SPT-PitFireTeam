using EFT;
using friendlySAIN.Components;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerSuppressionSafety
    {
        private const float LaneRadius = 0.55f;
        private const float CloseFrontDistance = 2.5f;
        private const float CloseFrontDot = 0.25f;
        private const float DistancePadding = 1.0f;

        public static bool IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 targetPosition)
        {
            if (shooter?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            Vector3 origin = GetFireOrigin(shooter);
            Vector3 toTarget = targetPosition - origin;
            float maxDistance = toTarget.magnitude;
            if (maxDistance < 0.2f)
            {
                return false;
            }

            Vector3 dir = toTarget / maxDistance;
            if (IsPlayerInLane(shooter, boss.realPlayer, origin, dir, maxDistance))
            {
                return true;
            }

            var followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == shooter || follower.IsDead || follower.GetPlayer == null)
                {
                    continue;
                }

                if (IsPlayerInLane(shooter, follower.GetPlayer, origin, dir, maxDistance))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 GetFireOrigin(BotOwner shooter)
        {
            Vector3 origin = shooter.Position + Vector3.up * 1.2f;
            var root = shooter.GetPlayer?.PlayerBones?.WeaponRoot;
            if (root != null)
            {
                origin = root.position;
            }
            return origin;
        }

        private static bool IsPlayerInLane(BotOwner shooter, Player ally, Vector3 origin, Vector3 dir, float maxDistance)
        {
            if (ally == null || !ally.HealthController.IsAlive || string.IsNullOrEmpty(ally.ProfileId))
            {
                return false;
            }

            if (string.Equals(ally.ProfileId, shooter.ProfileId, System.StringComparison.Ordinal))
            {
                return false;
            }

            Vector3 feet = ally.Transform.position;
            Vector3 torso = feet + Vector3.up * 1.0f;
            Vector3 head = feet + Vector3.up * 1.55f;

            return IsPointInLane(origin, dir, maxDistance, feet)
                || IsPointInLane(origin, dir, maxDistance, torso)
                || IsPointInLane(origin, dir, maxDistance, head);
        }

        private static bool IsPointInLane(Vector3 origin, Vector3 dir, float maxDistance, Vector3 point)
        {
            Vector3 toPoint = point - origin;
            float forward = Vector3.Dot(toPoint, dir);
            if (forward < 0f)
            {
                return false;
            }

            if (forward <= maxDistance + DistancePadding)
            {
                Vector3 closest = origin + dir * forward;
                float lateral = (point - closest).magnitude;
                if (lateral <= LaneRadius)
                {
                    return true;
                }
            }

            float dist = toPoint.magnitude;
            if (dist <= CloseFrontDistance)
            {
                float dot = dist > 0.001f ? Vector3.Dot(toPoint / dist, dir) : 1f;
                if (dot >= CloseFrontDot)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
