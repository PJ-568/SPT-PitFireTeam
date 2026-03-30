using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToEnemyAction : FollowerCombatActionBase
    {
        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.75f;
        private const int StalledRefreshThreshold = 2;
        private const float TacticalReloadSafeDistance = 25f;
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
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            BotOwner.Sprint(shouldSprint, true);
            RefreshProgressState();
            NotMovingCheck();

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
                BotOwner.StopMove();
                AimingAndShoot(data);
                return;
            }

            if (!goalEnemy.IsVisible && Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeSense >= 5f)
            {
                AimingAndShoot(data);
            }
            else
            {
                BotOwner.Steering.LookToDirection(goalEnemy.CurrPosition - BotOwner.Position);
            }

            if (BotOwner.Mover.HasPathAndNoComplete)
            {
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
                    BotOwner.Steering.LookToDirection(goalEnemy.CurrPosition - BotOwner.Position);
                    return;
                }

                if (!shouldSprint)
                {
                    AimingAndShoot(data);
                }

                return;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(0f);
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
            CustomNavigationPoint point = Covers.GetClosestCoverPoint(BotOwner, centerPos, 50f, cover =>
            {
                return Utils.Utils.CanShootToTarget(shootPoint, cover, BotOwner.LookSensor.Mask, false);
            });

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
            if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
                return;
            }

            Vector3 botPos = BotOwner.GetPlayer.Transform.position;
            Vector3 corner = BotOwner.Mover.CurrentCornerPoint;

            if (Covers.IsPointBetween(corner, botPos, goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToDirection(corner - BotOwner.Position + Vector3.up * 0.5f);
            }
            else
            {
                BotOwner.Steering.LookToDirection(goalEnemy.CurrPosition - BotOwner.Position);
            }
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
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f);
            if (closePoints.Count > 0)
            {
                customNavigationPoint = closePoints.RandomElement();
            }

            if (customNavigationPoint == null)
            {
                customNavigationPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f);
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
    }
}
