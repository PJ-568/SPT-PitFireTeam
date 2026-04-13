using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRunToCoverAction : FollowerCombatActionBase
    {
        private const float PathRefreshInterval = 1.5f;
        private const float ArrivalDistance = 0.75f;

        private CustomNavigationPoint? targetCover;
        private float nextPathRefreshTime;
        private bool targetPointAssigned;

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            base.Start();
            targetCover = null;
            nextPathRefreshTime = 0f;
            targetPointAssigned = false;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!EnsureTargetCover())
            {
                StopRun();
                return;
            }

            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Steering.LookToMovingDirection();

            if ((!targetPointAssigned || Time.time >= nextPathRefreshTime) && targetCover != null)
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
                BotOwner.GoToSomePointData.SetPoint(targetCover.Position);
                targetPointAssigned = true;
                nextPathRefreshTime = Time.time + PathRefreshInterval;
            }

            BotOwner.GoToSomePointData.UpdateToGo(true, 1f, 1f);

            if (BotOwner.Mover.IsComeTo(ArrivalDistance, true, targetCover))
            {
                SetCombatSprint(false);
                BotOwner.Memory.ComeToPoint();
                LookTowardThreatOnArrival();
                return;
            }

            SetCombatSprint(true);
        }

        public override void Stop()
        {
            targetCover = null;
            targetPointAssigned = false;
            base.Stop();
        }

        private bool EnsureTargetCover()
        {
            if (targetCover != null)
            {
                return true;
            }

            targetCover = BotOwner.Memory?.CurCustomCoverPoint;
            targetPointAssigned = false;
            return targetCover != null;
        }

        private void StopRun()
        {
            targetCover = null;
            targetPointAssigned = false;
            SetCombatSprint(false);
            BotOwner.StopMove();
        }

        private void LookTowardThreatOnArrival()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                BotOwner.Steering.LookToPoint(goalEnemy.GetBodyPartPosition());
                return;
            }

            Vector3 lookPoint = goalEnemy.EnemyLastPositionReal;
            if (!IsFinite(lookPoint))
            {
                lookPoint = goalEnemy.CurrPosition;
            }

            if (IsFinite(lookPoint))
            {
                BotOwner.Steering.LookToPoint(lookPoint + Vector3.up * 0.8f);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }
    }
}
