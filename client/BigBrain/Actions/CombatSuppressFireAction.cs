using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;

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
        private const float LauncherSuppressFindPositionMinDistance = 12f;
        private const float LauncherSuppressFindPositionMaxRadius = 50f;
        private const float LauncherSuppressFindPositionCooldown = 0.75f;
        private const float LauncherSuppressReachedMoveTargetDistance = 1.5f;
        private const float LauncherSuppressTargetReuseDistance = 1f;
        private const float LauncherSuppressFireMaxAimAngle = 12f;

        private readonly GClass281 baseLogic;
        private Vector3? launcherSuppressMoveTarget;
        private Vector3 launcherSuppressMoveTargetFor;
        private float nextLauncherSuppressPositionSearchAt;
        private string? lastLauncherSuppressSafetyRejectReason;
        private float nextLauncherSuppressSafetyRejectAt;
        private float nextLauncherSuppressAimHoldRecordAt;

        public CombatSuppressFireAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass281(botOwner);
        }

        public override void Stop()
        {
            StopCombatShooting();
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                StopCombatShooting();
                return;
            }

            string? reason = GetReason(data) ?? BotOwner.Brain?.Agent?.LastResult().Reason;
            if (StopUnownedGrenadeLauncherFire(reason, goalEnemy))
            {
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
            string? reason = BotOwner.Brain?.Agent?.LastResult().Reason;
            bool launcherSuppress = FollowerCombatCommon.IsGrenadeLauncherSuppressReason(reason);
            float launcherUnsafeRadius = launcherSuppress ? GetLauncherSuppressUnsafeRadius(reason) : 0f;
            Vector3? target = BotOwner.SuppressShoot?.GetPoint();
            if (!target.HasValue)
            {
                StopCombatShooting();
                return;
            }

            // If the stored suppress point is off to the side but the close enemy is actively
            // looking at the follower, correct toward the real threat when the lane is clean.
            if (!launcherSuppress)
            {
                target = CorrectCloseThreatSuppressPoint(target.Value);
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            BotOwner.Steering.LookToPoint(target.Value);
            if (launcherSuppress)
            {
                BotOwner.SetPose(1f);
            }

            if (launcherSuppress && FollowerShotSafety.IsFriendlyNearImpact(BotOwner, target.Value, launcherUnsafeRadius))
            {
                RecordLauncherSuppressSafetyReject($"{reason}:launcherImpactUnsafe", target.Value);
                StopCombatShooting();
                return;
            }

            if (FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, fireOrigin, target.Value))
            {
                StopCombatShooting();
                return;
            }

            if (IsCurrentSuppressionAimUnsafe(fireOrigin, target.Value, launcherSuppress))
            {
                if (launcherSuppress)
                {
                    RecordLauncherSuppressAimHold($"{reason}:launcherAimNotAligned", target.Value);
                }

                StopCombatShooting();
                return;
            }

            if (ShouldHoldCloseThreatSuppressFire(target.Value))
            {
                StopCombatShooting();
                return;
            }

            CustomNavigationPoint suppressFrom = BotOwner.SuppressShoot?.PointToSuppressFrom;
            if (launcherSuppress && !IsGrenadeLauncherSelectedForSuppress())
            {
                HoldLauncherSuppressPosition(target.Value, suppressFrom);

                BotWeaponSelector? selector = BotOwner?.WeaponManager?.Selector;
                if (selector?.IsWeaponReady == false)
                {
                    return;
                }

                StopCombatShooting();
                return;
            }

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

                if (IsCurrentSuppressionAimUnsafe(fireOrigin, target.Value, launcherSuppress))
                {
                    if (launcherSuppress)
                    {
                        RecordLauncherSuppressAimHold($"{reason}:launcherAimNotAligned", target.Value);
                    }

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

            if (launcherSuppress && !CanSuppressFromCurrentPosition(fireOrigin, target.Value))
            {
                if (TryMoveToLauncherSuppressPosition(target.Value))
                {
                    return;
                }

                StopCombatShooting();
                return;
            }

            if (launcherSuppress)
            {
                launcherSuppressMoveTarget = null;
                BotOwner.ShootData?.Shoot();
                return;
            }

            baseLogic.UpdateNodeByBrain(null);
        }

        private void HoldLauncherSuppressPosition(Vector3 target, CustomNavigationPoint suppressFrom)
        {
            BotOwner.Steering.LookToPoint(target);
            BotOwner.SetPose(1f);
            if (suppressFrom != null && !HasReachedSuppressFromPoint(suppressFrom))
            {
                BotOwner.GoToSomePointData.SetPoint(suppressFrom.Position);
                BotOwner.GoToSomePointData.UpdateToGo(true);
                return;
            }

            if (TryMoveToLauncherSuppressPosition(target))
            {
                return;
            }

            BotOwner.StopMove();
        }

        private bool TryMoveToLauncherSuppressPosition(Vector3 target)
        {
            if (TryUseCachedLauncherSuppressPosition(target))
            {
                return true;
            }

            if (Time.time < nextLauncherSuppressPositionSearchAt)
            {
                return false;
            }

            nextLauncherSuppressPositionSearchAt = Time.time + LauncherSuppressFindPositionCooldown;
            if (!TryFindLauncherSuppressPosition(
                    target,
                LauncherSuppressFindPositionMinDistance,
                LauncherSuppressFindPositionMaxRadius,
                    out Vector3 firePosition))
            {
                return false;
            }

            launcherSuppressMoveTarget = firePosition;
            launcherSuppressMoveTargetFor = target;
            MoveToLauncherSuppressPosition(firePosition, target);
            return true;
        }

        private bool TryFindLauncherSuppressPosition(
            Vector3 target,
            float minDistance,
            float maxRadius,
            out Vector3 firePosition)
        {
            firePosition = Vector3.zero;
            NavMeshPath path = new NavMeshPath();
            const int steps = 48;
            float minDistanceSqr = minDistance * minDistance;

            for (int i = 0; i < steps; i++)
            {
                float angle = i * (360f / steps);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 sample = target + direction * maxRadius;
                if (!NavMesh.SamplePosition(sample, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    continue;
                }

                if ((hit.position - target).sqrMagnitude < minDistanceSqr)
                {
                    continue;
                }

                if (!Covers.IsNavigablePoint(BotOwner.Position, hit.position, 150f, path))
                {
                    continue;
                }

                Vector3 candidateFireOrigin = hit.position + Vector3.up * 1.2f;
                if (!CanSuppressFromCurrentPosition(candidateFireOrigin, target) ||
                    FollowerShotSafety.IsFriendlyInSuppressionLane(BotOwner, candidateFireOrigin, target))
                {
                    continue;
                }

                firePosition = hit.position;
                return true;
            }

            return false;
        }

        private bool TryUseCachedLauncherSuppressPosition(Vector3 target)
        {
            if (!launcherSuppressMoveTarget.HasValue ||
                (launcherSuppressMoveTargetFor - target).sqrMagnitude > LauncherSuppressTargetReuseDistance * LauncherSuppressTargetReuseDistance)
            {
                return false;
            }

            if ((BotOwner.Position - launcherSuppressMoveTarget.Value).sqrMagnitude <=
                LauncherSuppressReachedMoveTargetDistance * LauncherSuppressReachedMoveTargetDistance)
            {
                launcherSuppressMoveTarget = null;
                return false;
            }

            MoveToLauncherSuppressPosition(launcherSuppressMoveTarget.Value, target);
            return true;
        }

        private void MoveToLauncherSuppressPosition(Vector3 position, Vector3 target)
        {
            BotOwner.Steering.LookToPoint(target);
            BotOwner.SetPose(1f);
            BotOwner.GoToSomePointData.SetPoint(position);
            BotOwner.GoToSomePointData.UpdateToGo(true);
        }

        private bool IsGrenadeLauncherSelectedForSuppress()
        {
            BotWeaponSelector? selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return false;
            }

            return selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon &&
                   FollowerCombatCommon.IsGrenadeLauncherWeapon(selector.SecondPrimaryWeaponItem as Weapon);
        }

        private static float GetLauncherSuppressUnsafeRadius(string? reason)
        {
            return FollowerCombatCommon.IsAutoSuppressReason(reason) ? 18f : 12f;
        }

        private bool IsCurrentSuppressionAimUnsafe(
            Vector3 fireOrigin,
            Vector3 suppressTarget,
            bool launcherSuppress = false)
        {
            Vector3 aimDirection = BotOwner.LookDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            float distance = Vector3.Distance(fireOrigin, suppressTarget);
            if (launcherSuppress)
            {
                return IsLauncherAimNotAligned(fireOrigin, suppressTarget, aimDirection);
            }

            return FollowerShotSafety.IsFriendlyInAimLane(BotOwner, fireOrigin, aimDirection, distance);
        }

        private static bool IsLauncherAimNotAligned(Vector3 fireOrigin, Vector3 suppressTarget, Vector3 aimDirection)
        {
            Vector3 targetDirection = suppressTarget - fireOrigin;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            return Vector3.Angle(aimDirection.normalized, targetDirection.normalized) > LauncherSuppressFireMaxAimAngle;
        }

        private void RecordLauncherSuppressSafetyReject(string reason, Vector3 target)
        {
            if (string.Equals(lastLauncherSuppressSafetyRejectReason, reason, System.StringComparison.Ordinal) &&
                Time.time < nextLauncherSuppressSafetyRejectAt)
            {
                return;
            }

            lastLauncherSuppressSafetyRejectReason = reason;
            nextLauncherSuppressSafetyRejectAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherReject",
                reason,
                goalEnemy: BotOwner.Memory?.GoalEnemy,
                target: target);
        }

        private void RecordLauncherSuppressAimHold(string reason, Vector3 target)
        {
            if (Time.time < nextLauncherSuppressAimHoldRecordAt)
            {
                return;
            }

            nextLauncherSuppressAimHoldRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherHold",
                reason,
                goalEnemy: BotOwner.Memory?.GoalEnemy,
                target: target);
        }

        private bool CanSuppressFromCurrentPosition(Vector3 fireOrigin, Vector3 target, bool requireDirectLane = false)
        {
            if (Utils.Utils.CanShootToTarget(
                    new ShootPointClass(target, 1f),
                    fireOrigin,
                    BotOwner.LookSensor.Mask,
                    false))
            {
                return true;
            }

            if (requireDirectLane)
            {
                return false;
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
