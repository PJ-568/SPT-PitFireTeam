using EFT;
using friendlySAIN.Utils;
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
                   string.Equals(reason, "cdgfp", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "sniper.immediateShoot", System.StringComparison.Ordinal) ||
                   string.Equals(reason, "sniper.pushSupportImmediateShoot", System.StringComparison.Ordinal);
        }

        public static bool CanUseLostVisualSuppress(EnemyInfo goalEnemy)
        {
            return !goalEnemy.IsVisible &&
                   Time.time - goalEnemy.PersonalLastSeenTime <= LostVisualSuppressSeconds;
        }

        public static bool HasReliableImmediateFireLane(BotOwner botOwner, EnemyInfo goalEnemy)
        {
            if (botOwner == null ||
                goalEnemy == null ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                !botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            Vector3 fireOrigin = GetFireOrigin(botOwner);
            if (goalEnemy.Person is Player enemy && enemy.MainParts != null)
            {
                if (CanShootMainPart(botOwner, enemy, BodyPartType.head, fireOrigin) ||
                    CanShootMainPart(botOwner, enemy, BodyPartType.body, fireOrigin))
                {
                    return true;
                }
            }

            // At point-blank range EFT can legitimately pick arms/legs while bodies overlap.
            // Farther away, requiring head/body prevents one-pixel truck/window cracks from
            // turning into an exposed standing-fire decision.
            if (goalEnemy.Distance <= 12f)
            {
                ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
                return shootPoint != null &&
                       Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false);
            }

            return false;
        }

        public static bool HasDirectFireLane(BotOwner botOwner, Vector3 target)
        {
            if (botOwner == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(
                new ShootPointClass(target, 1f),
                GetFireOrigin(botOwner),
                botOwner.LookSensor.Mask,
                false);
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

        private static bool CanShootMainPart(BotOwner botOwner, Player enemy, BodyPartType partType, Vector3 fireOrigin)
        {
            return enemy.MainParts.TryGetValue(partType, out EnemyPart part) &&
                   part != null &&
                   Utils.Utils.CanShootToTarget(
                       new ShootPointClass(part.Position, 1f),
                       fireOrigin,
                       botOwner.LookSensor.Mask,
                       false);
        }

        private static Vector3 GetFireOrigin(BotOwner botOwner)
        {
            return botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
        }
    }
}
