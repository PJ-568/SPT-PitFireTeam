using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRunToEnemyAction : FollowerCombatActionBase
    {
        private enum RunLookMode
        {
            None,
            Enemy,
            MovingDirection,
            KeepCurrent
        }

        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.4f;
        private const int StalledRefreshThreshold = 3;
        private const float MinEnemyRunPointDistance = 5f;
        private const float MaxEnemyRunPointDistance = 10f;
        private const float RunPointNavSampleRadius = 2.25f;
        private const float LookCommitMinSeconds = 0.2f;
        private const float LookCommitMaxSeconds = 0.4f;
        private static readonly float[] RunPointDistances = { 8f, 10f, 6.5f, 5f };
        private static readonly float[] RunPointAngles = { 0f, -25f, 25f, -50f, 50f, -90f, 90f, 180f };

        private float nextMoveRefreshTime;
        private Vector3 committedRunPoint;
        private bool hasCommittedRunPoint;
        private Vector3 lastProgressPosition;
        private float nextProgressCheckTime;
        private int stalledProgressChecks;
        private RunLookMode committedLookMode;
        private float committedLookModeUntil;

        public CombatRunToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            base.Start();
            nextMoveRefreshTime = 0f;
            committedRunPoint = Vector3.zero;
            hasCommittedRunPoint = false;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = 0f;
            stalledProgressChecks = 0;
            committedLookMode = RunLookMode.None;
            committedLookModeUntil = 0f;

            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy != null)
            {
                TryMoveToEnemy(goalEnemy);
            }
            SetCombatSprint(true);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            if (!HasCommittedRunPoint() && ShouldStopForImmediateFire(goalEnemy))
            {
                StopRunForFire(goalEnemy);
                return;
            }

            bool canRun = BotOwner.DoorOpener.UpdateDoorInteractionStatus() == DoorInteractionStatus.CanRun;
            BotOwner.SetTargetMoveSpeed(1f);
            RefreshProgressState();
            NotMovingCheck(goalEnemy);
            TryPreferPrimaryAtRange(goalEnemy);

            BotOwner.SetPose(1f);
            SetCombatSprint(canRun);
            UpdateLook(goalEnemy, canRun);

            if (!BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false, null))
            {
                return;
            }

            ClearCommittedRunPoint();
            BotOwner.StopMove();
            if (!TryContinueAdvance(goalEnemy))
            {
                BotOwner.StopMove();
            }
        }

        public override void Stop()
        {
            ClearCommittedRunPoint();
            base.Stop();
        }

        private void NotMovingCheck(EnemyInfo goalEnemy)
        {
            if (nextMoveRefreshTime > Time.time)
            {
                return;
            }

            nextMoveRefreshTime = Time.time + 3f;
            if (BotOwner.Mover.HasPathAndNoComplete && HasCommittedRunPoint())
            {
                return;
            }

            ClearCommittedRunPoint();
            TryMoveToEnemy(goalEnemy);
        }

        private bool TryContinueAdvance(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            nextMoveRefreshTime = Time.time + 0.25f;
            return TryMoveToEnemy(goalEnemy);
        }

        private bool ShouldStopForImmediateFire(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (!BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            return shootPoint != null &&
                   Utils.Utils.CanShootToTarget(shootPoint, BotOwner.WeaponRoot.position, BotOwner.LookSensor.Mask, false);
        }

        private void StopRunForFire(EnemyInfo goalEnemy)
        {
            ClearCommittedRunPoint();
            BotOwner.StopMove();
            SetCombatSprint(false);
            BotOwner.SetPose(1f);
            CommitLookMode(RunLookMode.Enemy);
            BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
        }

        private void UpdateLook(EnemyInfo goalEnemy, bool canRun)
        {
            if (BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                return;
            }

            if (TryApplyCommittedLook(goalEnemy, canRun))
            {
                return;
            }

            if (canRun && BotOwner.Mover.HasPathAndNoComplete)
            {
                CommitLookMode(RunLookMode.MovingDirection);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.LookData.SetLookPointByHearing(null);
            CommitLookMode(RunLookMode.KeepCurrent);
            BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
        }

        private bool TryApplyCommittedLook(EnemyInfo goalEnemy, bool canRun)
        {
            if (committedLookMode == RunLookMode.None || committedLookModeUntil <= Time.time)
            {
                return false;
            }

            switch (committedLookMode)
            {
                case RunLookMode.Enemy:
                    if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot)
                    {
                        BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
                        return true;
                    }

                    return false;

                case RunLookMode.MovingDirection:
                    if (canRun && BotOwner.Mover.HasPathAndNoComplete)
                    {
                        BotOwner.Steering.LookToMovingDirection();
                        return true;
                    }

                    return false;

                case RunLookMode.KeepCurrent:
                    BotOwner.LookData.SetLookPointByHearing(null);
                    BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
                    return true;
            }

            return false;
        }

        private void CommitLookMode(RunLookMode mode)
        {
            if (mode == committedLookMode && committedLookModeUntil > Time.time)
            {
                return;
            }

            committedLookMode = mode;
            committedLookModeUntil = Time.time + GClass856.Random(LookCommitMinSeconds, LookCommitMaxSeconds);
        }

        private bool TryMoveToEnemy(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (TryFindEnemyRunPoint(goalEnemy, out Vector3 attackPoint) &&
                TryMoveToPoint(attackPoint))
            {
                return true;
            }

            return TryMoveToEnemyFallback(goalEnemy.CurrPosition);
        }

        private bool TryMoveToEnemyFallback(Vector3 targetPoint)
        {
            if (BotOwner.MoveToEnemyData.method_0(targetPoint, out Vector3 currentTargetPos) &&
                TryMoveToPoint(currentTargetPos))
            {
                return true;
            }

            if (!BotOwner.MoveToEnemyData.ShallRecalWay(out _) &&
                Time.time - BotOwner.Mover.LastPathSetTime < 10f)
            {
                return true;
            }

            if (BotOwner.GoToPoint(targetPoint, true, -1f, false, false) == NavMeshPathStatus.PathComplete &&
                BotOwner.Mover.TargetPoint is Vector3 curPathLastPoint &&
                (targetPoint - curPathLastPoint).magnitude < 2f)
            {
                CommitRunPoint(curPathLastPoint);
                return true;
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                TryMoveToPoint(navMeshHit.position, false))
            {
                return true;
            }

            CustomNavigationPoint fallbackPoint = null;
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.hide);
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f, searchTypeOverride: searchType);
            if (closePoints.Count > 0)
            {
                fallbackPoint = closePoints.RandomElement();
            }

            if (fallbackPoint == null)
            {
                fallbackPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f, searchTypeOverride: searchType);
            }

            if (fallbackPoint != null &&
                Mathf.Abs(fallbackPoint.Position.y - targetPoint.y) <= VerticalTolerance &&
                TryMoveToPoint(fallbackPoint.Position))
            {
                return true;
            }

            return false;
        }

        private bool TryMoveToPoint(Vector3 targetPoint, bool withAttack = true)
        {
            if (BotOwner.GoToPoint(targetPoint, withAttack, -1f, false, false) != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            CommitRunPoint(targetPoint);
            return true;
        }

        private void RefreshProgressState()
        {
            if (!BotOwner.Mover.HasPathAndNoComplete)
            {
                stalledProgressChecks = 0;
                lastProgressPosition = BotOwner.Position;
                nextProgressCheckTime = Time.time + ProgressCheckInterval;
                return;
            }

            if (nextProgressCheckTime > Time.time)
            {
                return;
            }

            float minProgressSqr = MinimumProgressDistance * MinimumProgressDistance;
            if ((BotOwner.Position - lastProgressPosition).sqrMagnitude >= minProgressSqr)
            {
                stalledProgressChecks = 0;
            }
            else
            {
                stalledProgressChecks++;
                if (stalledProgressChecks >= StalledRefreshThreshold)
                {
                    ClearCommittedRunPoint();
                    nextMoveRefreshTime = 0f;
                    BotOwner.StopMove();
                    stalledProgressChecks = 0;
                }
            }

            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private void CommitRunPoint(Vector3 point)
        {
            committedRunPoint = point;
            hasCommittedRunPoint = true;
            stalledProgressChecks = 0;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private bool HasCommittedRunPoint()
        {
            if (!hasCommittedRunPoint)
            {
                return false;
            }

            if (!BotOwner.Mover.TargetPoint.HasValue)
            {
                hasCommittedRunPoint = false;
                return false;
            }

            Vector3 targetPoint = BotOwner.Mover.TargetPoint.Value;
            if ((targetPoint - committedRunPoint).sqrMagnitude > 1f)
            {
                hasCommittedRunPoint = false;
                return false;
            }

            return true;
        }

        private bool TryFindEnemyRunPoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 enemyPosition = goalEnemy.CurrPosition;
            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            Vector3 baseDirection = BotOwner.Position - enemyPosition;
            baseDirection.y = 0f;
            if (baseDirection.sqrMagnitude <= 0.01f)
            {
                baseDirection = BotOwner.Transform != null ? -BotOwner.Transform.forward : -Vector3.forward;
                baseDirection.y = 0f;
            }

            if (baseDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            baseDirection.Normalize();
            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true) ?? new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);

            Vector3 fallbackPoint = Vector3.zero;
            bool hasFallbackPoint = false;
            for (int distanceIndex = 0; distanceIndex < RunPointDistances.Length; distanceIndex++)
            {
                float distance = RunPointDistances[distanceIndex];
                for (int angleIndex = 0; angleIndex < RunPointAngles.Length; angleIndex++)
                {
                    Vector3 direction = Quaternion.Euler(0f, RunPointAngles[angleIndex], 0f) * baseDirection;
                    Vector3 candidate = enemyPosition + direction * distance;
                    if (!TrySampleRunPoint(candidate, enemyPosition, out Vector3 navPoint))
                    {
                        continue;
                    }

                    if (!hasFallbackPoint)
                    {
                        fallbackPoint = navPoint;
                        hasFallbackPoint = true;
                    }

                    if (CanShootEnemyFromPoint(shootPoint, navPoint))
                    {
                        point = navPoint;
                        return true;
                    }
                }
            }

            if (hasFallbackPoint)
            {
                point = fallbackPoint;
                return true;
            }

            return false;
        }

        private static bool TrySampleRunPoint(Vector3 candidate, Vector3 enemyPosition, out Vector3 navPoint)
        {
            navPoint = Vector3.zero;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, RunPointNavSampleRadius, NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 sampledPoint = navMeshHit.position;
            float enemyDistanceSqr = (sampledPoint - enemyPosition).sqrMagnitude;
            return enemyDistanceSqr >= MinEnemyRunPointDistance * MinEnemyRunPointDistance &&
                   enemyDistanceSqr <= MaxEnemyRunPointDistance * MaxEnemyRunPointDistance &&
                   Mathf.Abs(sampledPoint.y - enemyPosition.y) <= VerticalTolerance;
        }

        private bool CanShootEnemyFromPoint(ShootPointClass shootPoint, Vector3 point)
        {
            if (shootPoint == null)
            {
                return false;
            }

            Vector3 weaponOffset = BotOwner.ShootData != null ? BotOwner.ShootData.WeaponRootOffset : Vector3.up * 1.4f;
            return Utils.Utils.CanShootToTarget(shootPoint, point + weaponOffset, BotOwner.LookSensor.Mask, false) ||
                   Utils.Utils.CanShootToTarget(shootPoint, point + weaponOffset * 0.5f, BotOwner.LookSensor.Mask, false);
        }

        private void ClearCommittedRunPoint()
        {
            hasCommittedRunPoint = false;
            committedRunPoint = Vector3.zero;
        }

        private void StopAdvance()
        {
            ClearCommittedRunPoint();
            BotOwner.StopMove();
            BotOwner.LookData.SetLookPointByHearing(null);
            SetCombatSprint(false);
            BotOwner.SetPose(0f);
            committedLookMode = RunLookMode.None;
            committedLookModeUntil = 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
