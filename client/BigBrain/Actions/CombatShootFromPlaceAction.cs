using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Stationary combat fire action used when the decision tree wants the follower to stop moving
    /// and solve the fight from the current position. The action still delegates normal aiming and
    /// shooting to EFT's shoot-from-place node, but wraps it with follower-specific safety gates:
    /// pose/lane validation, close-threat aim correction, recent-contact suppression continuity,
    /// and friendly shot-lane protection.
    /// </summary>
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private const float MinEnemyDistanceForProne = 80f;
        private const float SameSpotMaxDistanceSqr = 0.75f * 0.75f;
        private const float StandingPoseThreshold = 0.85f;
        private const float CrouchFireProbeHeight = 1.0f;
        private const float ProneFireProbeHeight = 0.35f;
        private const float CloseImmediateFireDistance = 18f;
        private const float CloseImmediateFireAngle = 18f;
        private const float CloseThreatAimCorrectionDistance = 18f;
        private const float CloseThreatFireAngle = 18f;
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

            // First decide which fire poses are physically usable from this exact spot. The vanilla
            // node may crouch or prone by itself, but followers should not stay in a pose that has
            // no real shot lane, especially when cover/vegetation blocks the lower weapon origin.
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

            // Immediate fire is a survival branch, so force a standing lane if crouch/prone would
            // delay or obstruct the shot. Otherwise only stand up when the current crouch lane is bad.
            if (string.Equals(reason, "visibleImmediateShoot", System.StringComparison.Ordinal) &&
                (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true || BotOwner.Mover.TargetPose < StandingPoseThreshold))
            {
                BotOwner.SetPose(1f);
            }
            else if (!allowCrouch && BotOwner.Mover.TargetPose < StandingPoseThreshold)
            {
                BotOwner.SetPose(1f);
            }

            // Highest priority override: close enemy looking at the follower. In this case the
            // dangerous failure mode is pressing the trigger while the body/weapon is still angled
            // away, so this path owns both look correction and trigger gating.
            if (TryHandleCloseThreatFire(goalEnemy))
            {
                return;
            }

            // If an immediate-fire decision briefly loses CanShoot because of foliage or a small
            // visibility flicker, keep a short suppressive shot at the last verified point instead
            // of dropping into movement churn.
            if (TryUpdateImmediateLostVisualSuppress(reason, goalEnemy))
            {
                return;
            }

            // For urgent close-range reasons we may bypass the vanilla node and press the trigger
            // once aligned. For normal shoot-from-place, wait briefly for aim alignment before
            // letting the EFT node run so it does not fire while visibly off target.
            if (!TryForceCloseImmediateFire(reason, goalEnemy) &&
                WaitForEnemyAimAlignment(ref aimAlignStartedAt))
            {
                return;
            }

            if (StopIfFriendlyInCurrentFireLane(goalEnemy))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
            EnforceSupportedFirePose(allowCrouch, allowProne);
        }

        /// <summary>
        /// Urgent close-range immediate fire path. This covers cases where the decision tree already
        /// chose "shoot now" but the vanilla shoot-from-place node hesitates with semi-auto/shotgun
        /// weapons. It still requires a tight look angle and a friendly-safe lane before pressing fire.
        /// </summary>
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

            if (StopIfFriendlyInCurrentFireLane(goalEnemy))
            {
                return true;
            }

            BotOwner.ShootData.Shoot();
            aimAlignStartedAt = 0f;
            return true;
        }

        /// <summary>
        /// Active close-threat correction. When the enemy is close, shootable, and looking at this
        /// follower, the action stops movement, stands up, looks at the current shoot point, and
        /// only fires once the look angle is tight enough. If not aligned, it intentionally consumes
        /// the update and stops shooting instead of falling through to the vanilla node.
        /// </summary>
        private bool TryHandleCloseThreatFire(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                goalEnemy.Distance > CloseThreatAimCorrectionDistance ||
                !BotOwner.IsEnemyLookingAtMe(goalEnemy))
            {
                return false;
            }

            ShootPointClass? shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            Vector3 target = shootPoint?.Point ?? goalEnemy.GetBodyPartPosition();
            BotOwner.StopMove();
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(target);

            if (StopIfFriendlyInCurrentFireLane(goalEnemy))
            {
                return true;
            }

            if (CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) > CloseThreatFireAngle)
            {
                StopCombatShooting();
                return true;
            }

            BotOwner.ShootData.Shoot();
            aimAlignStartedAt = 0f;
            return true;
        }

        /// <summary>
        /// Keep the final pose consistent with the lane probes after vanilla has updated. This is a
        /// cleanup pass because the underlying EFT node can still request crouch/prone internally.
        /// </summary>
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

        /// <summary>
        /// Probe whether a fire lane exists from a hypothetical body/weapon height. The result is
        /// used to prevent the bot from crouching or going prone into a pose that cannot actually
        /// see or shoot the target.
        /// </summary>
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

        /// <summary>
        /// Maintains very short fire continuity for immediate-fire decisions when the enemy just
        /// disappeared from the shoot sensor but the last seen point is still fresh and directly
        /// shootable. This is intentionally position-locked so it cannot turn into blind walking fire.
        /// </summary>
        private bool TryUpdateImmediateLostVisualSuppress(string? reason, EnemyInfo? goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                goalEnemy == null ||
                goalEnemy.CanShoot ||
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
            if (!FollowerImmediateFirePolicy.HasDirectFireLane(BotOwner, target))
            {
                StopCombatShooting();
                return true;
            }

            if (StopIfFriendlyInCurrentFireLane(target))
            {
                BotOwner.Steering.LookToPoint(target);
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
