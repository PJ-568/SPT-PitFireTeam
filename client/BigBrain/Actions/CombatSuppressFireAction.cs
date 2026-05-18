using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Suppressive-fire action. It fires at the suppress target selected by the decision/objective,
    /// optionally moves to a suppress-from point, corrects close threat aim, and stops shooting when
    /// the suppress task is complete or unsafe.
    /// </summary>
    internal sealed class CombatSuppressFireAction : FollowerCombatActionBase
    {
        private const float CloseThreatSuppressCorrectionDistance = 18f;
        private const float CloseThreatSuppressFireAlignmentDistance = 6f;
        private const float CloseThreatSuppressFireMaxAngle = 35f;
        private const float SuppressPointCorrectionAngle = 25f;

        private readonly GClass281 baseLogic;

        public CombatSuppressFireAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass281(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                StopCombatShooting();
                return;
            }

            // Follower suppress reasons use the mod-owned target and optional suppress-from point
            // instead of the vanilla node selecting its own target.
            if (IsFollowerSuppressActive())
            {
                UpdateFollowerSuppress();
                return;
            }

            // Recent-contact suppression is a very short continuity window after losing direct LOS.
            // It keeps the bot firing at a fresh last-seen point without becoming blind-fire movement.
            if (FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                Vector3 target = FollowerImmediateFirePolicy.GetRecentContactSuppressTarget(goalEnemy);
                Vector3 fireOrigin = BotOwner.WeaponRoot != null
                    ? BotOwner.WeaponRoot.position
                    : BotOwner.Position + Vector3.up * 1.2f;

                BotOwner.Steering.LookToPoint(target);
                if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target))
                {
                    StopCombatShooting();
                    return;
                }

                if (IsCurrentSuppressionAimUnsafe(fireOrigin, target))
                {
                    StopCombatShooting();
                    return;
                }

                BotOwner.ShootData.Shoot();
                return;
            }

            // The vanilla suppress node can still fire through squadmates, so keep friendly lane
            // safety as the final hard gate before delegating to it.
            if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                StopCombatShooting();
                return;
            }

            if (IsCurrentSuppressionAimUnsafe(BotOwner.WeaponRoot != null
                    ? BotOwner.WeaponRoot.position
                    : BotOwner.Position + Vector3.up * 1.2f,
                goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                StopCombatShooting();
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }

        private bool IsFollowerSuppressActive()
        {
            string? reason = BotOwner.Brain?.Agent?.LastResult().Reason;
            if (!FollowerCombatCommon.IsFollowerSuppressReason(reason))
            {
                return false;
            }

            if (FollowerCombatCommon.IsAutoSuppressReason(reason) ||
                FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason))
            {
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }

        private void UpdateFollowerSuppress()
        {
            Vector3? target = BotOwner.SuppressShoot?.GetPoint();
            if (!target.HasValue)
            {
                StopCombatShooting();
                return;
            }

            // If the stored suppress point is off to the side but the close enemy is actively
            // looking at the follower, correct toward the real threat when the lane is clean.
            target = CorrectCloseThreatSuppressPoint(target.Value);

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            BotOwner.Steering.LookToPoint(target.Value);
            if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target.Value))
            {
                StopCombatShooting();
                return;
            }

            if (IsCurrentSuppressionAimUnsafe(fireOrigin, target.Value))
            {
                StopCombatShooting();
                return;
            }

            if (ShouldHoldCloseThreatSuppressFire(target.Value))
            {
                StopCombatShooting();
                return;
            }

            CustomNavigationPoint suppressFrom = BotOwner.SuppressShoot?.PointToSuppressFrom;
            if (suppressFrom != null && !HasReachedSuppressFromPoint(suppressFrom))
            {
                // Suppression does not wait until arrival. The bot keeps shooting while moving to
                // a better suppress-from point when the objective prepared one, but only if the
                // current moving lane is clear or soft-obstructed. If the current lane is a wall,
                // move first and start firing once the suppress-from point gives a real lane.
                BotOwner.Steering.LookToPoint(target.Value);
                BotOwner.GoToSomePointData.SetPoint(suppressFrom.Position);
                BotOwner.GoToSomePointData.UpdateToGo(true);
                if (ShouldHoldCloseThreatSuppressFire(target.Value))
                {
                    StopCombatShooting();
                    return;
                }

                if (!CanSuppressFromCurrentPosition(fireOrigin, target.Value))
                {
                    StopCombatShooting();
                    return;
                }

                if (IsCurrentSuppressionAimUnsafe(fireOrigin, target.Value))
                {
                    StopCombatShooting();
                    return;
                }

                BotOwner.ShootData.Shoot();
                return;
            }

            if (suppressFrom != null)
            {
                BotOwner.StopMove();
            }

            if (ShouldHoldCloseThreatSuppressFire(target.Value))
            {
                StopCombatShooting();
                return;
            }

            baseLogic.UpdateNodeByBrain(null);
        }

        private bool IsCurrentSuppressionAimUnsafe(Vector3 fireOrigin, Vector3 suppressTarget)
        {
            Vector3 aimDirection = BotOwner.LookDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            float distance = Vector3.Distance(fireOrigin, suppressTarget);
            return FollowerShotSafety.IsFriendlyInAimLane(BotOwner, fireOrigin, aimDirection, distance);
        }

        private bool CanSuppressFromCurrentPosition(Vector3 fireOrigin, Vector3 target)
        {
            if (Utils.Utils.CanShootToTarget(
                    new ShootPointClass(target, 1f),
                    fireOrigin,
                    BotOwner.LookSensor.Mask,
                    false))
            {
                return true;
            }

            return FollowerCombatCommon.IsSoftObstructedSuppressionLane(fireOrigin, target, BotOwner.LookSensor.Mask);
        }

        private bool ShouldHoldCloseThreatSuppressFire(Vector3 suppressTarget)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                !goalEnemy.IsVisible ||
                goalEnemy.Distance > CloseThreatSuppressFireAlignmentDistance)
            {
                return false;
            }

            Vector3 lookDirection = BotOwner.LookDirection;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            Vector3 targetDirection = suppressTarget - BotOwner.Position;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return Vector3.Angle(lookDirection.normalized, targetDirection.normalized) > CloseThreatSuppressFireMaxAngle;
        }

        private Vector3 CorrectCloseThreatSuppressPoint(Vector3 suppressPoint)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                goalEnemy.Distance > CloseThreatSuppressCorrectionDistance ||
                !BotOwner.IsEnemyLookingAtMe(goalEnemy))
            {
                return suppressPoint;
            }

            Vector3 enemyPoint = goalEnemy.IsVisible
                ? goalEnemy.GetBodyPartPosition()
                : goalEnemy.CurrPosition + BotOwner.STAY_HEIGHT;

            Vector3 suppressDirection = suppressPoint - BotOwner.Position;
            Vector3 enemyDirection = enemyPoint - BotOwner.Position;
            suppressDirection.y = 0f;
            enemyDirection.y = 0f;
            if (suppressDirection.sqrMagnitude <= 0.01f ||
                enemyDirection.sqrMagnitude <= 0.01f ||
                Vector3.Angle(suppressDirection, enemyDirection) <= SuppressPointCorrectionAngle)
            {
                return suppressPoint;
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            Vector3 fireDirection = enemyPoint - fireOrigin;
            if (fireDirection.sqrMagnitude <= 0.01f ||
                Physics.Raycast(fireOrigin, fireDirection.normalized, fireDirection.magnitude, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return suppressPoint;
            }

            return enemyPoint;
        }

        private bool HasReachedSuppressFromPoint(CustomNavigationPoint suppressFrom)
        {
            if (BotOwner.GoToSomePointData?.IsCome() == true)
            {
                return true;
            }

            return (BotOwner.Position - suppressFrom.Position).sqrMagnitude <= 1.5f * 1.5f;
        }
    }
}
