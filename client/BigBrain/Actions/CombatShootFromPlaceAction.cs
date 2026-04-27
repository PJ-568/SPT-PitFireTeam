using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private const float MinEnemyDistanceForProne = 80f;
        private const float SameSpotMaxDistanceSqr = 0.75f * 0.75f;
        private const float StandingPoseThreshold = 0.85f;
        private const float CrouchFireProbeHeight = 1.0f;
        private const float ProneFireProbeHeight = 0.35f;
        private const float CloseImmediateFireDistance = 10f;
        private const float CloseImmediateFireAngle = 35f;
        private readonly GClass276 baseLogic;
        private float aimAlignStartedAt;
        private Vector3 startPosition;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Start()
        {
            base.Start();
            startPosition = BotOwner.Position;
        }

        public override void Stop()
        {
            StopCombatShooting();
            aimAlignStartedAt = 0f;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            bool allowCrouch = CanUseFirePose(goalEnemy, CrouchFireProbeHeight);
            bool allowProne = goalEnemy != null &&
                              goalEnemy.Distance >= MinEnemyDistanceForProne &&
                              CanUseFirePose(goalEnemy, ProneFireProbeHeight);
            baseLogic.CanLay = allowProne;

            if (!allowProne && BotOwner.BotLay.IsLay)
            {
                BotOwner.BotLay.GetUp(false);
            }

            string? reason = GetReason(data) ?? BotOwner.Brain?.Agent?.LastResult().Reason;
            if (string.Equals(reason, "visibleImmediateShoot", System.StringComparison.Ordinal) &&
                (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true || BotOwner.Mover.TargetPose < StandingPoseThreshold))
            {
                BotOwner.SetPose(1f);
            }
            else if (!allowCrouch && BotOwner.Mover.TargetPose < StandingPoseThreshold)
            {
                BotOwner.SetPose(1f);
            }

            if (TryUpdateImmediateLostVisualSuppress(reason, goalEnemy))
            {
                return;
            }

            if (!TryForceCloseImmediateFire(reason, goalEnemy) &&
                WaitForEnemyAimAlignment(ref aimAlignStartedAt))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
            EnforceSupportedFirePose(allowCrouch, allowProne);
        }

        private bool TryForceCloseImmediateFire(string? reason, EnemyInfo? goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                goalEnemy == null ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                goalEnemy.Distance > CloseImmediateFireDistance)
            {
                return false;
            }

            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            Vector3 target = shootPoint?.Point ?? goalEnemy.GetBodyPartPosition();
            BotOwner.StopMove();
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(target);

            if (CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) > CloseImmediateFireAngle)
            {
                return false;
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            if (FollowerShotSafety.IsFriendlyInShotLane(BotOwner, fireOrigin, target))
            {
                StopCombatShooting();
                return true;
            }

            BotOwner.ShootData.Shoot();
            aimAlignStartedAt = 0f;
            return true;
        }

        private void EnforceSupportedFirePose(bool allowCrouch, bool allowProne)
        {
            if (!allowProne && BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.BotLay.GetUp(false);
                return;
            }

            if (!allowCrouch && BotOwner.Mover.TargetPose < StandingPoseThreshold)
            {
                BotOwner.SetPose(1f);
            }
        }

        private bool CanUseFirePose(EnemyInfo? goalEnemy, float probeHeight)
        {
            if (goalEnemy == null || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (!BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(false) ??
                                         new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);
            Vector3 fireOrigin = BotOwner.Position + Vector3.up * probeHeight;
            return Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, BotOwner.LookSensor.Mask, false);
        }

        private bool TryUpdateImmediateLostVisualSuppress(string? reason, EnemyInfo? goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                goalEnemy == null ||
                !FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            if ((BotOwner.Position - startPosition).sqrMagnitude > SameSpotMaxDistanceSqr)
            {
                StopCombatShooting();
                return true;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            if (!FollowerImmediateFirePolicy.HasDirectFireLane(BotOwner, target))
            {
                StopCombatShooting();
                return true;
            }

            if (FollowerShotSafety.IsFriendlyInShotLane(BotOwner, fireOrigin, target))
            {
                BotOwner.Steering.LookToPoint(target);
                StopCombatShooting();
                return true;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(target);
            BotOwner.ShootData.Shoot();
            return true;
        }

    }
}
