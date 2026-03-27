using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRunToEnemyAction : FollowerCombatActionBase
    {
        private const float VerticalTolerance = 1.25f;
        private const float ProgressCheckInterval = 1.25f;
        private const float MinimumProgressDistance = 0.75f;
        private const int StalledRefreshThreshold = 2;

        private float nextMoveRefreshTime;
        private Vector3 committedRunPoint;
        private bool hasCommittedRunPoint;
        private Vector3 lastProgressPosition;
        private float nextProgressCheckTime;
        private int stalledProgressChecks;

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
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                StopAdvance();
                return;
            }

            bool canRun = BotOwner.DoorOpener.UpdateDoorInteractionStatus() == DoorInteractionStatus.CanRun;
            BotOwner.SetTargetMoveSpeed(1f);
            RefreshProgressState();
            NotMovingCheck(goalEnemy);
            EnsurePrimaryWeapon();
            BotOwner.SetPose(1f);
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
            }

            if (canRun && BotOwner.Mover.HasPathAndNoComplete)
            {
                if (!goalEnemy.IsVisible || !goalEnemy.CanShoot)
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
                BotOwner.Sprint(true, true);
            }
            else
            {
                BotOwner.Steering.LookToDirection(goalEnemy.CurrPosition - BotOwner.Position);
                BotOwner.Sprint(false, true);
            }

            if (!BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false, null))
            {
                return;
            }

            ClearCommittedRunPoint();
            BotOwner.StopMove();
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
            TryMoveToEnemy(goalEnemy.CurrPosition);
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
                CommitRunPoint(curPathLastPoint);
                return true;
            }

            if (NavMesh.SamplePosition(targetPoint, out NavMeshHit navMeshHit, 2.6f, -1) &&
                TryGoToPoint(navMeshHit.position, false))
            {
                return true;
            }

            CustomNavigationPoint fallbackPoint = null;
            List<CustomNavigationPoint> closePoints = Covers.GetCoverPoints(BotOwner, targetPoint, 20f);
            if (closePoints.Count > 0)
            {
                fallbackPoint = closePoints.RandomElement();
            }

            if (fallbackPoint == null)
            {
                fallbackPoint = Covers.GetClosestCoverPoint(BotOwner, targetPoint, 30f);
            }

            if (fallbackPoint != null &&
                Mathf.Abs(fallbackPoint.Position.y - targetPoint.y) <= VerticalTolerance &&
                TryGoToPoint(fallbackPoint.Position, true))
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

        private void ClearCommittedRunPoint()
        {
            hasCommittedRunPoint = false;
            committedRunPoint = Vector3.zero;
        }

        private void EnsurePrimaryWeapon()
        {
            if (BotOwner.WeaponManager.IsMelee)
            {
                BotOwner.WeaponManager.Selector.ChangeToMain();
            }
        }

        private void StopAdvance()
        {
            ClearCommittedRunPoint();
            BotOwner.StopMove();
            BotOwner.LookData.SetLookPointByHearing(null);
            BotOwner.Sprint(false, true);
            BotOwner.SetPose(0f);
        }
    }
}
