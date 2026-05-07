using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Runs to the currently committed/assigned cover point. The action trusts the decision layer
    /// for cover selection, executes sprint movement, and turns back toward the threat on arrival.
    /// </summary>
    internal sealed class CombatRunToCoverAction : FollowerCombatActionBase
    {
        private enum MovementMode
        {
            Run,
            Walk
        }

        private const float PathRefreshInterval = 1.5f;
        private const float ArrivalDistance = 0.75f;
        private const float RunRestoreStableSeconds = 0.45f;

        private readonly CombatAttackMovingAction walkFallback;
        private MovementMode movementMode;
        private CustomNavigationPoint? targetCover;
        private float nextPathRefreshTime;
        private bool targetPointAssigned;
        private float canRunStableSince;

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
            walkFallback = new CombatAttackMovingAction(botOwner);
        }

        public override void Start()
        {
            base.Start();
            movementMode = MovementMode.Run;
            canRunStableSince = 0f;
            StartRunMode();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            bool canRun = CanActuallyRun();
            if (movementMode == MovementMode.Walk)
            {
                UpdateWalkFallback(data, canRun);
                return;
            }

            if (!canRun)
            {
                SwitchToWalkFallback(data);
                return;
            }

            UpdateRun(data);
        }

        public override void Stop()
        {
            if (movementMode == MovementMode.Walk)
            {
                walkFallback.Stop();
            }
            else
            {
                StopRunMode();
            }

            base.Stop();
        }

        private void StartRunMode()
        {
            targetCover = null;
            nextPathRefreshTime = 0f;
            targetPointAssigned = false;
        }

        private void StopRunMode()
        {
            targetCover = null;
            targetPointAssigned = false;
            SetCombatSprint(false);
        }

        private void SwitchToWalkFallback(CustomLayer.ActionData data)
        {
            if (EnsureTargetCover())
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
            }

            StopRunMode();
            movementMode = MovementMode.Walk;
            canRunStableSince = 0f;
            walkFallback.Start();
            walkFallback.Update(data);
        }

        private void UpdateWalkFallback(CustomLayer.ActionData data, bool canRun)
        {
            if (EnsureTargetCover())
            {
                BotOwner.Memory.SetCoverPoints(targetCover);
            }

            walkFallback.Update(data);

            if (!canRun)
            {
                canRunStableSince = 0f;
                return;
            }

            if (canRunStableSince <= 0f)
            {
                canRunStableSince = Time.time;
                return;
            }

            if (Time.time - canRunStableSince < RunRestoreStableSeconds)
            {
                return;
            }

            walkFallback.Stop();
            movementMode = MovementMode.Run;
            canRunStableSince = 0f;
            StartRunMode();
            UpdateRun(data);
        }

        private void UpdateRun(CustomLayer.ActionData data)
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
            if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner))
            {
                BotOwner.Steering.LookToMovingDirection();
            }

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

        private bool CanActuallyRun()
        {
            if (!BotOwner.CanSprintPlayer || BotOwner.Mover?.NoSprint == true)
            {
                return false;
            }

            Player? player = BotOwner.GetPlayer ?? BotOwner.AIData?.Player;
            if (player?.HealthController != null &&
                (player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) ||
                 player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) ||
                 player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg)))
            {
                return false;
            }

            return BotOwner.DoorOpener.UpdateDoorInteractionStatus() == DoorInteractionStatus.CanRun;
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
