using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Moving fire action used for tactical retreats, regroup-with-contact, and pressure movement.
    /// It keeps EFT's attack-moving node as the base, then adds follower-specific primary-weapon
    /// preference, threat-facing, optional suppressive bursts, and unsafe close-threat guards.
    /// </summary>
    internal class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly FollowerAttackMovingLogic baseLogic;

        protected CombatAttackMovingAction(
            BotOwner botOwner,
            bool withSuppress,
            bool autoCover = true,
            bool forceThreatLookWhenShootable = false) : base(botOwner)
        {
            baseLogic = new FollowerAttackMovingLogic(botOwner, withSuppress, autoCover, forceThreatLookWhenShootable);
        }

        public CombatAttackMovingAction(BotOwner botOwner) : this(botOwner, withSuppress: false)
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                StopCombatShooting();
                BotOwner.LookData.SetLookPointByHearing(null);
                BotOwner.Mover.Stop();
                return;
            }

            // Attack-moving can run for a while, so keep non-marksman followers on their primary at
            // range and pass the current decision reason into the wrapped node for suppress behavior.
            TryPreferPrimaryAtRange(goalEnemy, GetReason(data));
            baseLogic.SetCurrentReason(GetReason(data));
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        /// <summary>
        /// Wrapper around EFT's attack-moving node. Movement and cover handling stay vanilla-owned,
        /// while follower code controls when to look at the threat, when to suppress a recent point,
        /// and when to stop a dangerous close retreat from turning the bot's back.
        /// </summary>
        private sealed class FollowerAttackMovingLogic : GClass205
        {
            private const float ArrivalThreatLookAngle = 95f;
            private const float NearCoverDistance = 2f;
            private const float RecentThreatLookSeconds = 2.5f;
            private const float LostVisualSuppressSeconds = 2f;
            private const float UnsafeCloseThreatDistance = 8f;
            private const float UnsafeCloseThreatLookAngle = 70f;
            private const float RegroupCatchUpDestinationDistance = 25f;

            private readonly bool autoCover;
            private readonly bool forceThreatLookWhenShootable;
            private readonly bool withSuppress;
            private string? currentReason;
            private float nextSuppressToggleTime;
            private bool suppressBurstActive;
            private float nextThreatLookTime;

            public FollowerAttackMovingLogic(
                BotOwner botOwner,
                bool withSuppress,
                bool autoCover,
                bool forceThreatLookWhenShootable) : base(botOwner)
            {
                this.withSuppress = withSuppress;
                this.autoCover = autoCover;
                this.forceThreatLookWhenShootable = forceThreatLookWhenShootable;
            }

            public void SetCurrentReason(string? reason)
            {
                currentReason = reason;
            }

            public override void UpdateNodeByBrain(GClass26 data)
            {
                if (!autoCover && BotOwner_0.Memory?.CurCustomCoverPoint != null)
                {
                    ForceCurrentCoverDestination();
                }

                bool regroupCatchUp = ShouldUseRegroupCatchUp();
                if (regroupCatchUp)
                {
                    ForceRegroupCatchUpMovement();
                }

                base.UpdateNodeByBrain(data);

                if (regroupCatchUp)
                {
                    ForceRegroupCatchUpMovement();
                }
            }

            public override void AimingAndShoot(GClass26 data)
            {
                if (ShouldUseRegroupCatchUp())
                {
                    StopShooting();
                    BotOwner_0.LookData.SetLookPointByHearing(null);
                    BotOwner_0.Steering.LookToMovingDirection();
                    return;
                }

                if (nextSuppressToggleTime < Time.time)
                {
                    nextSuppressToggleTime = Time.time + GClass856.Random(2f, 4f);
                    suppressBurstActive = !suppressBurstActive;
                }

                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (!BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner_0))
                {
                    TryMaintainThreatFacing(goalEnemy);
                }

                bool unsafeCloseRetreat = TryStopUnsafeCloseThreatRetreat(goalEnemy);
                Vector3 threatPoint;
                bool shouldShoot = TryGetSafeShootOrSuppressTarget(goalEnemy, out threatPoint);
                if (shouldShoot)
                {
                    if (forceThreatLookWhenShootable && goalEnemy != null)
                    {
                        BotOwner_0.Steering.LookToPoint(threatPoint);
                    }

                    if (IsCurrentAimLaneUnsafe(threatPoint))
                    {
                        StopShooting();
                        return;
                    }

                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                if (unsafeCloseRetreat)
                {
                    return;
                }

                if (goalEnemy != null && nextThreatLookTime < Time.time)
                {
                    nextThreatLookTime = Time.time + GClass856.Random(2f, 3f);
                    BotOwner_0.Steering.LookToPoint(goalEnemy.EnemyLastPosition + new Vector3(0f, 0.6f, 0f));
                }
            }

            private bool TryGetSafeShootOrSuppressTarget(EnemyInfo? goalEnemy, out Vector3 target)
            {
                target = default;
                if (goalEnemy == null)
                {
                    return false;
                }

                bool visibleShot = goalEnemy.CanShoot && goalEnemy.IsVisible;
                bool lostVisualSuppress =
                    withSuppress &&
                    suppressBurstActive &&
                    !goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime <= LostVisualSuppressSeconds;

                if (!visibleShot && !lostVisualSuppress)
                {
                    return false;
                }

                target = visibleShot
                    ? goalEnemy.GetBodyPartPosition()
                    : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;

                Vector3 fireOrigin = BotOwner_0.WeaponRoot != null
                    ? BotOwner_0.WeaponRoot.position
                    : BotOwner_0.Position + Vector3.up * 1.2f;
                return !FollowerShotSafety.IsFriendlyInShotLane(BotOwner_0, fireOrigin, target);
            }

            private bool IsCurrentAimLaneUnsafe(Vector3 target)
            {
                Vector3 aimDirection = BotOwner_0.LookDirection;
                if (aimDirection.sqrMagnitude <= 0.0001f && BotOwner_0.Transform != null)
                {
                    aimDirection = BotOwner_0.Transform.forward;
                }

                Vector3 fireOrigin = BotOwner_0.WeaponRoot != null
                    ? BotOwner_0.WeaponRoot.position
                    : BotOwner_0.Position + Vector3.up * 1.2f;
                return FollowerShotSafety.IsFriendlyInAimLane(
                    BotOwner_0,
                    fireOrigin,
                    aimDirection,
                    Vector3.Distance(fireOrigin, target));
            }

            private bool TryStopUnsafeCloseThreatRetreat(EnemyInfo? goalEnemy)
            {
                if (!forceThreatLookWhenShootable ||
                    goalEnemy == null ||
                    !IsCloseActiveThreat(goalEnemy, UnsafeCloseThreatDistance, 0.75f))
                {
                    return false;
                }

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, allowHardTurn: true);
                if (CombatAttackMoveLook.GetThreatLookAngle(BotOwner_0, goalEnemy) <= UnsafeCloseThreatLookAngle)
                {
                    return false;
                }

                BotOwner_0.Mover.Stop();
                BotOwner_0.Sprint(false, true);
                return true;
            }

            private void TryMaintainThreatFacing(EnemyInfo? goalEnemy)
            {
                if (goalEnemy == null || !ShouldCorrectArrivalLook(goalEnemy))
                {
                    return;
                }

                Vector3 threatPoint = goalEnemy.IsVisible
                    ? goalEnemy.GetBodyPartPosition()
                    : goalEnemy.EnemyLastPositionReal + Vector3.up * 0.6f;
                Vector3 lookDirection = threatPoint - BotOwner_0.Position;
                if (lookDirection.sqrMagnitude < 0.01f)
                {
                    return;
                }

                if (Vector3.Angle(BotOwner_0.LookDirection, lookDirection) < ArrivalThreatLookAngle)
                {
                    return;
                }

                bool allowHardTurn =
                    forceThreatLookWhenShootable ||
                    BotOwner_0.Memory.IsInCover ||
                    global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason);

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, allowHardTurn);
            }

            private bool ShouldCorrectArrivalLook(EnemyInfo goalEnemy)
            {
                if (IsCloseActiveThreat(goalEnemy, UnsafeCloseThreatDistance, 0.75f))
                {
                    return true;
                }

                if (!goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > RecentThreatLookSeconds)
                {
                    return false;
                }

                if (BotOwner_0.Memory.IsInCover)
                {
                    return true;
                }

                if (global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) &&
                    BotOwner_0.GoToSomePointData != null &&
                    BotOwner_0.GoToSomePointData.IsCome())
                {
                    return true;
                }

                CustomNavigationPoint? cover = BotOwner_0.Memory?.CurCustomCoverPoint;
                if (cover == null)
                {
                    return false;
                }

                return (BotOwner_0.Position - cover.Position).sqrMagnitude <= NearCoverDistance * NearCoverDistance;
            }

            private bool IsCloseActiveThreat(EnemyInfo goalEnemy, float maxDistance, float recentSeenWindow)
            {
                return goalEnemy != null &&
                       goalEnemy.Distance <= maxDistance &&
                       BotOwner_0.IsEnemyLookingAtMe(goalEnemy) &&
                       (goalEnemy.IsVisible ||
                        Time.time - goalEnemy.PersonalSeenTime <= recentSeenWindow ||
                        Time.time - goalEnemy.PersonalLastSeenTime <= recentSeenWindow);
            }

            private void ForceCurrentCoverDestination()
            {
                CustomNavigationPoint cover = BotOwner_0.Memory.CurCustomCoverPoint;
                bool withShoot = BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                                 BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Sprint(false, true);
                BotOwner_0.SetPose(1f);
                BotOwner_0.Memory.SetCoverPoints(cover, string.Empty);
                BotOwner_0.GoToPoint(cover);

                if (!cover.CanIShootToEnemy && withShoot)
                {
                    BotOwner_0.BotAttackManager.UpdateNextTick();
                }
            }

            private bool ShouldUseRegroupCatchUp()
            {
                if (!global::pitTeam.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) ||
                    BotOwner_0.GoToSomePointData?.HaveTarget() != true)
                {
                    return false;
                }

                Vector3 target = BotOwner_0.GoToSomePointData.Point;
                return (BotOwner_0.Position - target).sqrMagnitude >
                       RegroupCatchUpDestinationDistance * RegroupCatchUpDestinationDistance;
            }

            private void ForceRegroupCatchUpMovement()
            {
                BotOwner_0.SetPose(1f);
                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Mover.Sprint(true, false);
            }

            private void StopShooting()
            {
                BotOwner_0.ShootData?.EndShoot();
                BotOwner_0.WeaponManager?.ShootController?.SetTriggerPressed(false);
            }
        }
    }
}
