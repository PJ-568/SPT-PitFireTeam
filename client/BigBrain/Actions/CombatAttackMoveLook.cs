using EFT;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Shared look-control helpers for attack-moving style actions. These methods keep movement
    /// actions from aiming into walls or away from a close threat while the action itself still owns
    /// pathing and movement speed.
    /// </summary>
    internal static class CombatAttackMoveLook
    {
        private const float MaxForcedTurnAngle = 145f;

        public static bool TryLookThreatFacing(BotOwner botOwner, EnemyInfo? goalEnemy, bool allowHardTurn = false)
        {
            if (goalEnemy == null)
            {
                botOwner.LookData.SetLookPointByHearing(null);
                return false;
            }

            Vector3 lookPoint = GetThreatLookPoint(goalEnemy);

            Vector3 lookDirection = lookPoint - botOwner.Position;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return false;
            }

            // Attack-moving should keep the weapon locked to the threat lane while body/pathing handles
            // strafing or backpedaling. If the threat is too far behind current view, do not force a
            // full backwards twist every tick; let normal movement/look control recover instead.
            if (!allowHardTurn && Vector3.Angle(botOwner.LookDirection, lookDirection) > MaxForcedTurnAngle)
            {
                botOwner.LookData.SetLookPointByHearing(null);
                return false;
            }

            botOwner.LookData.SetLookPointByHearing(null);
            botOwner.Memory?.botObserveData?.Stop();
            botOwner.Steering.LookToPoint(lookPoint);
            return true;
        }

        public static float GetThreatLookAngle(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner == null || goalEnemy == null)
            {
                return 180f;
            }

            Vector3 lookPoint = GetThreatLookPoint(goalEnemy);
            Vector3 lookDirection = lookPoint - botOwner.Position;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return 0f;
            }

            return Vector3.Angle(botOwner.LookDirection, lookDirection);
        }

        private static Vector3 GetThreatLookPoint(EnemyInfo goalEnemy)
        {
            try
            {
                Vector3 bodyPoint = goalEnemy.GetBodyPartPosition();
                if (FollowerCombatCommon.IsFinite(bodyPoint) && bodyPoint.sqrMagnitude > 0.01f)
                {
                    return bodyPoint;
                }
            }
            catch
            {
            }

            Vector3 currentPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);
            if (FollowerCombatCommon.IsFinite(currentPosition) && currentPosition.sqrMagnitude > 0.01f)
            {
                return currentPosition + Vector3.up * 0.8f;
            }

            return goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
        }
    }
}
