using EFT;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal static class FollowerImmediateFirePolicy
    {
        public const float LostVisualSuppressSeconds = 1f;
        public const float RecentContactSuppressSeconds = 2f;

        public static bool IsImmediateShootReason(string? reason)
        {
            return string.Equals(reason, "visibleImmediateShoot", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "committedImmediateShoot", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "ShootImmediately", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "sniper.immediateShoot", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "sniper.pushSupportImmediateShoot", System.StringComparison.Ordinal);
        }

        public static bool CanUseLostVisualSuppress(EnemyInfo goalEnemy)
        {
            return !goalEnemy.IsVisible &&
                   Time.time - goalEnemy.PersonalLastSeenTime <= LostVisualSuppressSeconds;
        }

        public static Vector3 GetLostVisualSuppressTarget(EnemyInfo goalEnemy)
        {
            return goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
        }

        public static bool CanUseRecentContactSuppress(EnemyInfo goalEnemy)
        {
            return !goalEnemy.IsVisible &&
                   Time.time - goalEnemy.PersonalLastSeenTime <= RecentContactSuppressSeconds;
        }

        public static Vector3 GetRecentContactSuppressTarget(EnemyInfo goalEnemy)
        {
            return goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
        }
    }
}
