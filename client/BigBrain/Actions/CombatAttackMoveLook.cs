using EFT;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
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

            Vector3 lookPoint = goalEnemy.IsVisible
                ? goalEnemy.GetBodyPartPosition()
                : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;

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

            Vector3 lookPoint = goalEnemy.IsVisible
                ? goalEnemy.GetBodyPartPosition()
                : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
            Vector3 lookDirection = lookPoint - botOwner.Position;
            if (lookDirection.sqrMagnitude < 0.01f)
            {
                return 0f;
            }

            return Vector3.Angle(botOwner.LookDirection, lookDirection);
        }
    }
}
