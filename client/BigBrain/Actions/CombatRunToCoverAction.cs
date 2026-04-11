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

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            base.Start();
            targetCover = null;
            nextPathRefreshTime = 0f;
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

            bool canRun = BotOwner.DoorOpener.UpdateDoorInteractionStatus() == DoorInteractionStatus.CanRun;
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Steering.LookToMovingDirection();

            if (!BotOwner.Mover.HasPathAndNoComplete && Time.time >= nextPathRefreshTime)
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
                BotOwner.GoToPoint(targetCover);
                nextPathRefreshTime = Time.time + PathRefreshInterval;
            }

            if (BotOwner.Mover.IsComeTo(ArrivalDistance, true, targetCover))
            {
                BotOwner.Sprint(false, false);
                BotOwner.Memory.ComeToPoint();
                return;
            }

            BotOwner.Sprint(canRun, true);
        }

        public override void Stop()
        {
            targetCover = null;
            base.Stop();
        }

        private bool EnsureTargetCover()
        {
            if (targetCover != null)
            {
                return true;
            }

            targetCover = BotOwner.Memory?.CurCustomCoverPoint;
            return targetCover != null;
        }

        private void StopRun()
        {
            targetCover = null;
            BotOwner.Sprint(false, true);
            BotOwner.StopMove();
        }
    }
}
