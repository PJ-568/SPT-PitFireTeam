using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToEnemyAction : FollowerCombatActionBase
    {
        private enum AdvanceLookMode
        {
            None,
            EnemyOwned,
            AdvancePoint,
            MovingDirection,
            KeepCurrent
        }

        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.4f;
        private const int StalledRefreshThreshold = 3;
        private const float TacticalReloadSafeDistance = 25f;
        private const float WallFacingProbeDistance = 1.25f;
        private const float WallFacingFallbackDebounceSeconds = 0.2f;
        private const float LookCommitMinSeconds = 0.2f;
        private const float LookCommitMaxSeconds = 0.4f;
        private const int LargeMagazineLowAmmoThreshold = 20;
        private const int PistolLargeMagazineLowAmmoThreshold = 10;

        private readonly GClass183 shootLogic;
        private bool shouldSprint;
        private float nextMoveRefreshTime;
        private Vector3 committedAdvancePoint;
        private bool hasCommittedAdvancePoint;
        private Vector3 lastProgressPosition;
        private float nextProgressCheckTime;
        private int stalledProgressChecks;
        private AdvanceLookMode committedLookMode;
        private float committedLookModeUntil;
        private float wallFacingSince;
        private bool wallFacingActive;

        public CombatGoToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new GClass183(botOwner);
        }

        public override void Start()
        {
            base.Start();
            shouldSprint = false;
            nextMoveRefreshTime = 0f;
            committedAdvancePoint = Vector3.zero;
            hasCommittedAdvancePoint = false;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = 0f;
            stalledProgressChecks = 0;
            committedLookMode = AdvanceLookMode.None;
            committedLookModeUntil = 0f;
            wallFacingSince = 0f;
            wallFacingActive = false;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            TryPreferPrimaryAtRange(goalEnemy);
            SetCombatSprint(shouldSprint);
            RefreshProgressState();
            NotMovingCheck();
            bool hasPath = BotOwner.Mover.HasPathAndNoComplete;

            // Visibility alone should not hard-stop an advance. Let end-condition logic decide when
            // the shot is stable enough to break the push; otherwise keep moving and shoot while advancing.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.SetPose(1f);
                CommitLookMode(AdvanceLookMode.EnemyOwned);
                TryApplyCommittedLook(goalEnemy);
                if (!hasPath)
                {
                    AimingAndShoot(data);
                    return;
                }
            }

            if (!goalEnemy.IsVisible && Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeSense >= 5f)
            {
                AimingAndShoot(data);
            }
            else if (!hasPath || shouldSprint)
            {
                LookTowardAdvance(goalEnemy);
            }

            if (hasPath)
            {
                BotOwner.SetPose(1f);
                bool reached = BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false);
                if (ShouldReloadWhileAdvancing(goalEnemy))
                {
                    BotOwner.WeaponManager.Reload.TryReload();
                }

                if (reached && goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    ClearCommittedAdvancePoint();
                    AimAndMove(data);
                    return;
                }

                if (reached)
                {
                    ClearCommittedAdvancePoint();
                    BotOwner.StopMove();
                    LookTowardAdvance(goalEnemy);
                    return;
                }

                if (!shouldSprint)
                {
                    AimingAndShoot(data);
                }

                return;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
        }

        public override void Stop()
        {
            shouldSprint = false;
            ClearCommittedAdvancePoint();
            base.Stop();
        }

        private void NotMovingCheck()
        {
            if (BotOwner.Memory.GoalEnemy == null)
            {
                return;
            }

            if (nextMoveRefreshTime > Time.time)
            {
                return;
            }

            nextMoveRefreshTime = Time.time + 3f;
            if (BotOwner.Mover.HasPathAndNoComplete && HasCommittedAdvancePoint())
            {
                return;
            }

            // If we already committed a point and it still looks usable, try to re-acquire that path first
            // instead of clearing and recalculating immediately (reduces repath/look churn).
            if (TryContinueToCommittedAdvancePoint())
            {
                return;
            }

            ClearCommittedAdvancePoint();
            TryMoveToEnemy(BotOwner.Memory.GoalEnemy.CurrPosition);
        }

        private void AimAndMove(CustomLayer.ActionData data)
        {
            if (HasCommittedAdvancePoint())
            {
                BotOwner.BotAttackManager.UpdateNextTick();
                AimingAndShoot(data);
                return;
            }

            Vector3 enemyPos = BotOwner.Memory.GoalEnemy.EnemyLastPosition;
            Vector3 centerPos = BotOwner.Memory.IsInCover && !BotOwner.LookSensor.EnoughDistToShoot(out _)
                ? (BotOwner.Transform.position + enemyPos) / 2f
                : enemyPos;

            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.shoot);
            CustomNavigationPoint point = Covers.GetClosestCoverPoint(BotOwner, centerPos, 50f, cover =>
            {
                return Utils.Utils.CanShootToTarget(shootPoint, cover, BotOwner.LookSensor.Mask, false);
            }, searchType);

            if (point != null)
            {
                BotOwner.GoToPoint(point);
                CommitAdvancePoint(point.Position);
            }
            else if (BotOwner.GoToPoint(centerPos) == NavMeshPathStatus.PathComplete)
            {
                CommitAdvancePoint(centerPos);
            }

            BotOwner.BotAttackManager.UpdateNextTick();
            AimingAndShoot(data);
        }

        private void AimingAndShoot(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
                return;
            }

            // Look toward enemy last known position. Avoid navmesh corner points which are
            // in the middle of walls and cause the "shooting at the wall" behavior.
            LookTowardAdvance(goalEnemy);
        }

        private void LookTowardAdvance(EnemyInfo goalEnemy)
        {
            if (BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                return;
            }

            if (TryApplyCommittedLook(goalEnemy))
            {
                return;
            }

            if (TryGetOwnedEnemyLookDirection(goalEnemy, out Vector3 lookDirection))
            {
                CommitLookMode(AdvanceLookMode.EnemyOwned);
                BotOwner.Steering.LookToDirection(lookDirection.normalized);
                return;
            }

            if (TryLookTowardAdvancePoint())
            {
                return;
            }

            if (BotOwner.Mover.HasPathAndNoComplete)
            {
                CommitLookMode(AdvanceLookMode.MovingDirection);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            CommitLookMode(AdvanceLookMode.KeepCurrent);
            BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
        }

        private bool TryLookTowardAdvancePoint()
        {
            Vector3? advanceTarget = GetAdvanceTargetPoint();
            if (!advanceTarget.HasValue)
            {
                return false;
            }

            Vector3 lookDirection = advanceTarget.Value - BotOwner.Position;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            if (IsFacingWall(lookDirection))
            {
                if (!wallFacingActive)
                {
                    wallFacingActive = true;
                    wallFacingSince = Time.time;
                }

                if (Time.time - wallFacingSince >= WallFacingFallbackDebounceSeconds)
                {
                    CommitLookMode(AdvanceLookMode.MovingDirection);
                    BotOwner.Steering.LookToMovingDirection();
                    return true;
                }

                CommitLookMode(AdvanceLookMode.AdvancePoint);
                BotOwner.Steering.LookToPoint(advanceTarget.Value + Vector3.up * 0.5f);
                return true;
            }

            wallFacingActive = false;
            wallFacingSince = 0f;

            CommitLookMode(AdvanceLookMode.AdvancePoint);
            BotOwner.Steering.LookToPoint(advanceTarget.Value + Vector3.up * 0.5f);
            return true;
        }

        private Vector3? GetAdvanceTargetPoint()
        {
            if (BotOwner.Mover.TargetPoint.HasValue)
            {
                return BotOwner.Mover.TargetPoint.Value;
            }

            if (hasCommittedAdvancePoint)
            {
                return committedAdvancePoint;
            }

            return null;
        }

        private bool IsFacingWall(Vector3 lookDirection)
        {
            Vector3 origin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position + Vector3.up * 0.1f
                : BotOwner.Position + Vector3.up * 1.4f;

            return Physics.Raycast(origin, lookDirection.normalized, WallFacingProbeDistance, LayerMaskClass.HighPolyWithTerrainMask);
        }

        private bool TryMoveToEnemy(Vector3 targetPoint)
        {
            if (BotOwner.MoveToEnemyData.method_0(targetPoint, out Vector3 currentTargetPos) &&
                TryGoToPoint(currentTargetPos, true))
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
                CommitAdvancePoint(curPathLastPoint);
                shouldSprint = !Utils.Utils.IsWithinDistance(curPathLastPoint, BotOwner.GetPlayer.Transform.position, 20f);
                return true;
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                TryGoToPoint(navMeshHit.position, false))
            {
                return true;
            }

            CustomNavigationPoint customNavigationPoint = null;
            CoverSearchType searchType = SetAttackCoverSearchType(CoverShootType.hide);
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f, searchTypeOverride: searchType);
            if (closePoints.Count > 0)
            {
                customNavigationPoint = closePoints.RandomElement();
            }

            if (customNavigationPoint == null)
            {
                customNavigationPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f, searchTypeOverride: searchType);
            }

            if (customNavigationPoint != null &&
                Mathf.Abs(customNavigationPoint.Position.y - targetPoint.y) <= VerticalTolerance &&
                TryGoToPoint(customNavigationPoint.Position, true))
            {
                return true;
            }

            return false;
        }

        private bool TryGoToPoint(Vector3 targetPoint, bool withAttack)
        {
            if (BotOwner.GoToPoint(targetPoint, withAttack, -1f, false, false) != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            CommitAdvancePoint(targetPoint);
            shouldSprint = !Utils.Utils.IsWithinDistance(targetPoint, BotOwner.GetPlayer.Transform.position, 20f);
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
                    ClearCommittedAdvancePoint();
                    nextMoveRefreshTime = 0f;
                    BotOwner.StopMove();
                    stalledProgressChecks = 0;
                }
            }

            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private void CommitAdvancePoint(Vector3 point)
        {
            committedAdvancePoint = point;
            hasCommittedAdvancePoint = true;
            stalledProgressChecks = 0;
            lastProgressPosition = BotOwner.Position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
        }

        private bool TryContinueToCommittedAdvancePoint()
        {
            if (!hasCommittedAdvancePoint)
            {
                return false;
            }

            if (!IsFinite(committedAdvancePoint) ||
                (committedAdvancePoint - BotOwner.Position).sqrMagnitude <= 0.25f)
            {
                return false;
            }

            if (TryGoToPoint(committedAdvancePoint, true))
            {
                nextMoveRefreshTime = Time.time + 4f;
                return true;
            }

            return false;
        }

        private bool HasCommittedAdvancePoint()
        {
            if (!hasCommittedAdvancePoint)
            {
                return false;
            }

            if (!BotOwner.Mover.TargetPoint.HasValue)
            {
                hasCommittedAdvancePoint = false;
                return false;
            }

            Vector3 targetPoint = BotOwner.Mover.TargetPoint.Value;
            if ((targetPoint - committedAdvancePoint).sqrMagnitude > 1f)
            {
                hasCommittedAdvancePoint = false;
                return false;
            }

            return true;
        }

        private void ClearCommittedAdvancePoint()
        {
            hasCommittedAdvancePoint = false;
            committedAdvancePoint = Vector3.zero;
        }

        private bool TryApplyCommittedLook(EnemyInfo goalEnemy)
        {
            if (committedLookMode == AdvanceLookMode.None || committedLookModeUntil <= Time.time)
            {
                return false;
            }

            switch (committedLookMode)
            {
                case AdvanceLookMode.EnemyOwned:
                    if (TryGetOwnedEnemyLookDirection(goalEnemy, out Vector3 lookDirection))
                    {
                        BotOwner.Steering.LookToDirection(lookDirection.normalized);
                        return true;
                    }
                    return false;

                case AdvanceLookMode.AdvancePoint:
                    if (TryGetAdvanceLookPoint(out Vector3 advancePoint))
                    {
                        BotOwner.Steering.LookToPoint(advancePoint + Vector3.up * 0.5f);
                        return true;
                    }
                    return false;

                case AdvanceLookMode.MovingDirection:
                    if (BotOwner.Mover.HasPathAndNoComplete)
                    {
                        BotOwner.Steering.LookToMovingDirection();
                        return true;
                    }
                    return false;

                case AdvanceLookMode.KeepCurrent:
                    BotOwner.Steering.LookToDirection(BotOwner.LookDirection);
                    return true;
            }

            return false;
        }

        private void CommitLookMode(AdvanceLookMode mode)
        {
            if (mode == committedLookMode && committedLookModeUntil > Time.time)
            {
                return;
            }

            committedLookMode = mode;
            committedLookModeUntil = Time.time + GClass856.Random(LookCommitMinSeconds, LookCommitMaxSeconds);
        }

        private bool TryGetAdvanceLookPoint(out Vector3 point)
        {
            point = Vector3.zero;
            Vector3? advanceTarget = GetAdvanceTargetPoint();
            if (!advanceTarget.HasValue || !IsFinite(advanceTarget.Value))
            {
                return false;
            }

            point = advanceTarget.Value;
            return true;
        }

        private void StopAdvance()
        {
            ClearCommittedAdvancePoint();
            BotOwner.StopMove();
            BotOwner.LookData.SetLookPointByHearing(null);
            BotOwner.SetPose(0f);
        }

        private bool ShouldReloadWhileAdvancing(EnemyInfo goalEnemy)
        {
            if (BotOwner?.WeaponManager?.Reload == null || BotOwner.WeaponManager.Reload.Reloading)
            {
                return false;
            }

            if (!BotOwner.WeaponManager.HaveBullets)
            {
                return true;
            }

            if (goalEnemy == null || goalEnemy.Distance < TacticalReloadSafeDistance)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            MagazineItemClass currentMagazine = currentWeapon?.GetCurrentMagazine();
            if (currentWeapon == null || currentMagazine == null)
            {
                return false;
            }

            int capacity = currentMagazine.MaxCount;
            int currentBullets = currentMagazine.Count;
            if (capacity <= 0 || currentBullets >= capacity)
            {
                return false;
            }

            return currentBullets <= GetTacticalReloadThreshold(currentWeapon, capacity);
        }

        private static int GetTacticalReloadThreshold(Weapon weapon, int capacity)
        {
            if (capacity <= 0)
            {
                return 0;
            }

            if (IsPistol(weapon))
            {
                return capacity > PistolLargeMagazineLowAmmoThreshold
                    ? PistolLargeMagazineLowAmmoThreshold
                    : capacity / 2;
            }

            if (capacity <= 20)
            {
                return capacity / 2;
            }

            if (capacity > 30)
            {
                return LargeMagazineLowAmmoThreshold;
            }

            return capacity / 2;
        }

        private static bool IsPistol(Weapon weapon)
        {
            return string.Equals(weapon?.Template?.weapClass, "pistol", System.StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetOwnedEnemyLookDirection(EnemyInfo goalEnemy, out Vector3 lookDirection)
        {
            lookDirection = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                lookDirection = goalEnemy.GetBodyPartPosition() - BotOwner.Position;
                return lookDirection.sqrMagnitude > 0.01f;
            }

            if (Time.time - goalEnemy.PersonalLastSeenTime <= 12f)
            {
                Vector3 personalLastPos = goalEnemy.PersonalLastPos;
                if ((personalLastPos - BotOwner.Position).sqrMagnitude > 0.01f)
                {
                    lookDirection = personalLastPos - BotOwner.Position;
                    return true;
                }

                Vector3 lastKnownPosition = goalEnemy.EnemyLastPositionReal;
                if ((lastKnownPosition - BotOwner.Position).sqrMagnitude > 0.01f)
                {
                    lookDirection = lastKnownPosition - BotOwner.Position;
                    return true;
                }
            }

            return false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
