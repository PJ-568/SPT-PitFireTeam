using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToEnemyAction : FollowerCombatActionBase
    {
        private const float AdvanceCommitSeconds = 3f;
        private readonly GClass183 shootLogic;
        private bool shouldSprint;
        private float nextMoveRefreshTime;
        private Vector3 committedAdvancePoint;
        private float committedAdvanceUntil;
        private bool hasCommittedAdvancePoint;

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
            committedAdvanceUntil = 0f;
            hasCommittedAdvancePoint = false;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                BotOwner.StopMove();
                BotOwner.LookData.SetLookPointByHearing(null);
                BotOwner.SetPose(0f);
                return;
            }

            BotOwner.Sprint(shouldSprint, true);
            NotMovingCheck();

            bool trackingEnemy = false;
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                trackingEnemy = true;
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition + Vector3.up);
                BotOwner.StopMove();
                shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
                return;
            }

            if (!goalEnemy.IsVisible && Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeSense >= 5f)
            {
                AimingAndShoot(data);
            }
            else
            {
                trackingEnemy = true;
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
            }

            if (BotOwner.Mover.HasPathAndNoComplete)
            {
                bool reached = BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false);
                if (!BotOwner.WeaponManager.HaveBullets)
                {
                    BotOwner.WeaponManager.Reload.TryReload();
                }

                if (reached && goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    AimAndMove(data);
                    return;
                }

                if (reached)
                {
                    ClearCommittedAdvancePoint();
                    BotOwner.StopMove();
                    BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition + Vector3.up);
                    return;
                }

                if (!shouldSprint)
                {
                    AimingAndShoot(data);
                }

                return;
            }

            if (!trackingEnemy)
            {
                BotOwner.LookData.SetLookPointByHearing(null);
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
            if (nextMoveRefreshTime > Time.time || BotOwner.Memory.GoalEnemy == null)
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
                BotOwner.Steering.LookToPoint(corner - BotOwner.Position + Vector3.up * 0.5f);
            }
            else
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition + Vector3.up);
            }
        }

        private bool TryMoveToEnemy(Vector3 targetPoint)
        {
            if (BotOwner.MoveToEnemyData.method_0(targetPoint, out Vector3 position) &&
                BotOwner.GoToPoint(position, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                CommitAdvancePoint(position);
                shouldSprint = !Utils.Utils.IsWithinDistance(position, BotOwner.GetPlayer.Transform.position, 20f);
                return true;
            }

            if (!BotOwner.MoveToEnemyData.ShallRecalWay(out _) &&
                Time.time - BotOwner.Mover.LastPathSetTime < 10f)
            {
                return true;
            }

            if (BotOwner.GoToPoint(targetPoint, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                Vector3 curPathLastPoint = BotOwner.Mover.TargetPoint.Value;
                if ((targetPoint - curPathLastPoint).magnitude < 2f)
                {
                    CommitAdvancePoint(curPathLastPoint);
                    shouldSprint = !Utils.Utils.IsWithinDistance(curPathLastPoint, BotOwner.GetPlayer.Transform.position, 20f);
                    return true;
                }
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                CommitAdvancePoint(navMeshHit.position);
                shouldSprint = !Utils.Utils.IsWithinDistance(navMeshHit.position, BotOwner.GetPlayer.Transform.position, 20f);
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
                Mathf.Abs(customNavigationPoint.Position.y - targetPoint.y) < 1f &&
                BotOwner.GoToPoint(customNavigationPoint.Position, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                CommitAdvancePoint(customNavigationPoint.Position);
                shouldSprint = !Utils.Utils.IsWithinDistance(customNavigationPoint.Position, BotOwner.GetPlayer.Transform.position, 20f);
                return true;
            }

            return false;
        }

        private void CommitAdvancePoint(Vector3 point)
        {
            committedAdvancePoint = point;
            committedAdvanceUntil = Time.time + AdvanceCommitSeconds;
            hasCommittedAdvancePoint = true;
        }

        private bool HasCommittedAdvancePoint()
        {
            if (!hasCommittedAdvancePoint || committedAdvanceUntil < Time.time)
            {
                hasCommittedAdvancePoint = false;
                return false;
            }

            return true;
        }

        private void ClearCommittedAdvancePoint()
        {
            hasCommittedAdvancePoint = false;
            committedAdvanceUntil = 0f;
            committedAdvancePoint = Vector3.zero;
        }
    }
}
