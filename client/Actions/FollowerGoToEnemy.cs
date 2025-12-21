using EFT;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * A combination of the attackMoving and goToEnemy to use our cover system for "goToEnemy" decision
     */
    public class FollowerGoToEnemy : GClass198
    {
        private bool shouldSprint = false;

        private float float_3 = 0f;
        private float float_4 = 0f;

        public FollowerGoToEnemy(BotOwner bot) : base(bot)
        {
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            botOwner_0.DoorOpener.Update();
            botOwner_0.Sprint(shouldSprint, true);
            NotMovingCheck();

            bool flag = false;
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                flag = true;
                botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());
                botOwner_0.StopMove();
                gclass171_0.UpdateNodeByBrain(data as GClass27);
                return;
            }
            else if (!goalEnemy.IsVisible && Time.time - goalEnemy.GroupInfo.EnemyLastSeenTimeSense >= 5f)
            {
                AimingAndShoot(data);
            }
            else
            {
                flag = true;
                botOwner_0.Steering.LookToPoint(goalEnemy.CurrPosition);
            }

            if (botOwner_0.Mover.HasPathAndNoComplete)
            {

                bool flag2 = botOwner_0.Mover.IsComeTo(botOwner_0.Settings.FileSettings.Move.REACH_DIST, false);
                if (!botOwner_0.WeaponManager.HaveBullets)
                {
                    botOwner_0.WeaponManager.Reload.TryReload();
                }
                if (flag2 && goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    AimAndMove(data);
                    return;
                }
                if (flag2)
                {
                    botOwner_0.StopMove();
                    botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());

                    return;
                }
                else if (!shouldSprint)
                {
                    AimAndMove(data);
                }
            }
            else
            {
                if (!flag)
                {
                    botOwner_0.LookData.SetLookPointByHearing(null);
                }
                botOwner_0.StopMove();
                botOwner_0.SetPose(0f);
            }
        }

        public void NotMovingCheck()
        {
            if (float_3 > Time.time)
            {
                return;
            }
            float_3 = Time.time + 3f;
            Vector3 currPosition = botOwner_0.Memory.GoalEnemy.CurrPosition;
            TryMoveToEnemy(currPosition);
        }

        public void AimAndMove(StandardBrain data)
        {

            bool flag;
            Vector3 centerPos;
            Vector3 enemyPos = botOwner_0.Memory.GoalEnemy.EnemyLastPosition;
            if (botOwner_0.Memory.IsInCover && !botOwner_0.LookSensor.EnoughDistToShoot(out flag))
            {
                centerPos = (botOwner_0.Transform.position + enemyPos) / 2f;
            }
            else
            {
                centerPos = enemyPos;
            }


            if (float_4 < Time.time)
            {
                float_4 = Time.time + 2f;
                var shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                var point = Utils.Covers.GetClosestCoverPoint(botOwner_0, centerPos, 50f, cover =>
                {
                    if (Utils.Utils.CanShootToTarget(shootPointClass, cover, botOwner_0.LookSensor.Mask, false))
                    {
                        return true;
                    }

                    return false;
                });

                if (point != null)
                {
                    botOwner_0.GoToPoint(point);
                }
                else
                {
                    botOwner_0.GoToPoint(centerPos);
                }
            }

            botOwner_0.BotAttackManager.UpdateNextTick();

            AimingAndShoot(data);
        }

        public override void AimingAndShoot(StandardBrain data)
        {
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                gclass171_0.UpdateNodeByBrain(data as GClass27);
                return;
            }
            Vector3 botPos = botOwner_0.GetPlayer.Transform.position;
            Vector3 corner = botOwner_0.Mover.CurrentCornerPoint;

            if (Utils.Covers.IsPointBetween(corner, botPos, goalEnemy.CurrPosition))
            {
                botOwner_0.Steering.LookToPoint(corner - botOwner_0.Position + Vector3.up * 0.5f);
            }
            else
            {
                botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());
            }
        }
        // replication of MoveToEnemyData.TryToMoveToEnemy, but adapted to use our cover system
        public bool TryMoveToEnemy(Vector3 targetPoint)
        {
            Vector3 position;
            if (botOwner_0.MoveToEnemyData.method_0(targetPoint, out position) && botOwner_0.GoToPoint(position, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                shouldSprint = !Utils.Utils.IsWithinDistance(position, botOwner_0.GetPlayer.Transform.position, 20f);
                return true;
            }

            Vector3 vector;
            if (!botOwner_0.MoveToEnemyData.ShallRecalWay(out vector) && Time.time - botOwner_0.Mover.LastPathSetTime < 10f)
            {
                return true;
            }
            if (botOwner_0.GoToPoint(targetPoint, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                Vector3 curPathLastPoint = botOwner_0.Mover.TargetPoint.Value;
                if ((targetPoint - curPathLastPoint).magnitude < 2f)
                {
                    return true;
                }
            }

            NavMeshHit navMeshHit;
            if (NavMesh.SamplePosition(targetPoint, out navMeshHit, 2.6f, -1) && botOwner_0.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                shouldSprint = !Utils.Utils.IsWithinDistance(navMeshHit.position, botOwner_0.GetPlayer.Transform.position, 20f);
                return true;
            }

            CustomNavigationPoint customNavigationPoint = null;

            List<CustomNavigationPoint> closePoints = Utils.Covers.GetCoverPoints(botOwner_0, targetPoint, 20f);
            if (closePoints.Count > 0)
            {
                customNavigationPoint = closePoints.RandomElement();
            }

            if (customNavigationPoint == null)
            {
                CustomNavigationPoint freeClosePoint = Utils.Covers.GetClosestCoverPoint(botOwner_0, targetPoint, 30f);
                if (freeClosePoint != null)
                {
                    customNavigationPoint = freeClosePoint;
                }
            }

            if (customNavigationPoint != null && Mathf.Abs(customNavigationPoint.Position.y - targetPoint.y) < 1f && this.botOwner_0.GoToPoint(customNavigationPoint.Position, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
            {
                shouldSprint = !Utils.Utils.IsWithinDistance(customNavigationPoint.Position, botOwner_0.GetPlayer.Transform.position, 20f);
                return true;
            }

            return false;
        }
    }
}
