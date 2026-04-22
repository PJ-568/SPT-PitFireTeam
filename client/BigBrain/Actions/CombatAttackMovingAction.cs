using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    /// <summary>
    /// Direct port of the old plugin's FollowerAttackMove behavior onto the 4.x attack-moving base.
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
            baseLogic.SetCurrentReason(GetReason(data));
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        private sealed class FollowerAttackMovingLogic : GClass205
        {
            private const float ArrivalThreatLookAngle = 95f;
            private const float NearCoverDistance = 2f;
            private const float RecentThreatLookSeconds = 2.5f;
            private const float LostVisualSuppressSeconds = 2f;

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

                base.UpdateNodeByBrain(data);
            }

            public override void AimingAndShoot(GClass26 data)
            {
                if (nextSuppressToggleTime < Time.time)
                {
                    nextSuppressToggleTime = Time.time + GClass856.Random(2f, 4f);
                    suppressBurstActive = !suppressBurstActive;
                }

                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                TryMaintainThreatFacing(goalEnemy);

                Vector3 threatPoint;
                bool shouldShoot = TryGetSafeShootOrSuppressTarget(goalEnemy, out threatPoint);
                if (shouldShoot)
                {
                    if (forceThreatLookWhenShootable && goalEnemy != null)
                    {
                        BotOwner_0.Steering.LookToPoint(threatPoint);
                    }

                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
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

                return !FollowerShotSafety.IsFriendlyInShotLane(BotOwner_0, target);
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
                    global::friendlySAIN.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason);

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, allowHardTurn);
            }

            private bool ShouldCorrectArrivalLook(EnemyInfo goalEnemy)
            {
                if (!goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > RecentThreatLookSeconds)
                {
                    return false;
                }

                if (BotOwner_0.Memory.IsInCover)
                {
                    return true;
                }

                if (global::friendlySAIN.BigBrain.FollowerCombatRegroupObjective.IsRegroupReason(currentReason) &&
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
        }
    }
}
