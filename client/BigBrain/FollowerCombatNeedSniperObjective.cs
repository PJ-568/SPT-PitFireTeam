using EFT;
using pitTeam.Components;
using pitTeam.Utils;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatNeedSniperObjective : FollowerCombatObjectiveBase
    {
        private const float ArrivalSettleSeconds = 2f;
        private const float SearchRetrySeconds = 2.5f;
        private const float SearchRetryScanSeconds = 0.35f;
        private const string ReasonPrefix = "sniper.NeedSniper";
        private const string PositionHoldReason = "sniper.NeedSniper.positionHold";
        private const string RetryHoldReason = "sniper.NeedSniper.retry";

        private bool complete;
        private float settleUntil;
        private float searchRetryUntil;
        private float retryScanUntil;
        private string? lockedSupportEnemyProfileId;
        private Vector3 lockedSupportPosition;

        public FollowerCombatNeedSniperObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            settleUntil = 0f;
            searchRetryUntil = 0f;
            retryScanUntil = 0f;
            lockedSupportEnemyProfileId = null;
            lockedSupportPosition = Vector3.zero;
        }

        public override void Activate()
        {
            Reset();
            searchRetryUntil = Time.time + SearchRetrySeconds;
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            ClearObjectiveCommitments();
            complete = false;
            settleUntil = 0f;
            searchRetryUntil = 0f;
            retryScanUntil = 0f;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleCommittedCoverDecisionChanged(nextDecision);

            if (CombatCommon.ShouldCommitMovementDecision(nextDecision, false))
            {
                CombatCommon.CommitMovement(nextDecision);
            }
            else if (!CombatCommon.IsSameCommittedMovement(nextDecision))
            {
                CombatCommon.ClearCommittedMovement();
            }
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return RejectObjective("noEnemy");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                complete = true;
                return dogFight.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                complete = true;
                return healDecision.Value;
            }

            CombatCommon.RefreshShootCover();
            CombatCommon.ValidateCommittedCover();

            if (CombatCommon.TryGetImmediateShootDecision($"{ReasonPrefix}.immediateShoot") is { } immediateShoot)
            {
                complete = true;
                return immediateShoot;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                complete = true;
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    $"{ReasonPrefix}.currentCover");
            }

            if (CombatCommon.HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPosition))
            {
                return committedPosition;
            }

            if (CombatCommon.TryGetCommittedMovementDecision(
                    goalEnemy,
                    false,
                    false,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedMovement))
            {
                return committedMovement;
            }

            if (CombatCommon.HasCommittedCover() && !CombatCommon.IsBotInCommittedCover())
            {
                CombatCommon.AssignCommittedCover();
                return CombatCommon.CreateCommittedCoverMoveDecision();
            }

            if (!TryResolveLockedOrNewSupportEnemy(goalEnemy, out EnemyInfo? supportEnemy, out Vector3 supportPosition) ||
                !CombatCommon.HasActiveCombatEnemy(supportEnemy))
            {
                return RetryOrRejectObjective("noSupportEnemy");
            }

            if (!string.IsNullOrEmpty(supportEnemy.ProfileId))
            {
                lockedSupportEnemyProfileId ??= supportEnemy.ProfileId;
                lockedSupportPosition = supportPosition;
                if (!CombatCommon.TryForceGoalEnemy(lockedSupportEnemyProfileId, "NeedSniper", out EnemyInfo? forcedEnemy) ||
                    !CombatCommon.HasActiveCombatEnemy(forcedEnemy))
                {
                    return RetryOrRejectObjective("forceEnemyFailed");
                }

                supportEnemy = forcedEnemy;
            }

            if (CombatCommon.TryCommitSupportFiringCover(
                    supportEnemy,
                    ReasonPrefix,
                    out string coverReason,
                    preferBackline: false,
                    enforceMarksmanPositionPolicy: false))
            {
                return CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            }

            Vector3 currentEnemyPosition = FollowerCombatCommon.GetEnemyCurrentPosition(supportEnemy);
            if (CombatCommon.TryCreateFiringPositionDecisionAt(
                    supportEnemy,
                    currentEnemyPosition,
                    $"{ReasonPrefix}.currentPosition",
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> currentPositionDecision,
                    preferBackline: false,
                    enforceMarksmanPositionPolicy: true,
                    allowForwardPositions: true,
                    allowBattlefieldPositions: true,
                    maxNavDistance: 140f))
            {
                return currentPositionDecision;
            }

            if (CombatCommon.TryCreateSupportFiringPositionDecision(
                    supportEnemy,
                    supportPosition,
                    $"{ReasonPrefix}.position",
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> positionDecision,
                    preferBackline: false,
                    enforceMarksmanPositionPolicy: true,
                    allowForwardPositions: true,
                    allowBattlefieldPositions: true,
                    maxNavDistance: 140f))
            {
                return positionDecision;
            }

            return RetryOrRejectObjective("noLane");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (currentDecision.Reason == null || !currentDecision.Reason.StartsWith(ReasonPrefix, StringComparison.Ordinal))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("needSniperEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.shootFromCover ||
                currentDecision.Action == BotLogicDecision.shootFromPlace)
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (currentDecision.Action == BotLogicDecision.goToPoint)
            {
                return EndPositionMove(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.runToCover ||
                currentDecision.Action == BotLogicDecision.attackMoving ||
                currentDecision.Action == BotLogicDecision.attackMovingWithSuppress ||
                currentDecision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                return EndCoverMove(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return EndHold(currentDecision.Reason);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private AICoreActionEndStruct EndCoverMove(string? reason)
        {
            AICoreActionEndStruct end = CombatCommon.EndRunToCover(reason);
            if (!end.Value)
            {
                return end;
            }

            CombatCommon.ClearCommittedMovement();
            if (IsArrivalEnd(end.Reason))
            {
                ArmArrivalHold();
            }

            return end;
        }

        private AICoreActionEndStruct EndPositionMove(string? reason)
        {
            AICoreActionEndStruct end = CombatCommon.EndGoToPoint(endWhenEnemyVisibleShootable: true);
            if (!end.Value)
            {
                return end;
            }

            CombatCommon.ClearCommittedMovement();
            if (string.Equals(end.Reason, "arrivedAtPoint", StringComparison.Ordinal))
            {
                ArmArrivalHold();
                return new AICoreActionEndStruct("needSniperPositionArrived", true);
            }

            return end;
        }

        private AICoreActionEndStruct EndHold(string? reason)
        {
            if (IsRetryHoldReason(reason))
            {
                if (Time.time >= retryScanUntil)
                {
                    return new AICoreActionEndStruct("needSniperRetryScan", true);
                }

                CombatCommon.HoldFor(Mathf.Max(0.1f, retryScanUntil - Time.time));
                return default;
            }

            if (CombatCommon.TryGetImmediateShootDecision($"{ReasonPrefix}.holdShoot") != null ||
                CombatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                complete = true;
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("needSniperShotReady", true);
            }

            if (Time.time >= settleUntil)
            {
                complete = true;
                CombatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("needSniperArrivedSettled", true);
            }

            CombatCommon.HoldFor(Mathf.Max(0.1f, settleUntil - Time.time));
            return default;
        }

        private void ArmArrivalHold()
        {
            settleUntil = Time.time + ArrivalSettleSeconds;
            CombatCommon.SetCommittedPosition(
                BotOwner.Position,
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, PositionHoldReason),
                ArrivalSettleSeconds);
        }

        private bool TryResolveLockedOrNewSupportEnemy(EnemyInfo goalEnemy, out EnemyInfo? supportEnemy, out Vector3 supportPosition)
        {
            if (!string.IsNullOrEmpty(lockedSupportEnemyProfileId))
            {
                supportPosition = FollowerCombatCommon.IsFinite(lockedSupportPosition)
                    ? lockedSupportPosition
                    : FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);
                return CombatCommon.TryForceGoalEnemy(lockedSupportEnemyProfileId, "NeedSniper.locked", out supportEnemy);
            }

            return TryResolveSupportEnemy(goalEnemy, out supportEnemy, out supportPosition);
        }

        private bool TryResolveSupportEnemy(EnemyInfo goalEnemy, out EnemyInfo? supportEnemy, out Vector3 supportPosition)
        {
            supportEnemy = goalEnemy;
            supportPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);

            if (TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent))
            {
                supportPosition = IsFinite(pushEvent.EnemyPosition) ? pushEvent.EnemyPosition : pushEvent.Destination;
                if (!string.IsNullOrEmpty(pushEvent.EnemyProfileId))
                {
                    CombatCommon.TrySelectPreferredSupportEnemy(
                        pushEvent.EnemyProfileId,
                        supportPosition,
                        out supportEnemy,
                        preferBackline: false,
                        promoteSelected: false);
                }

                return true;
            }

            if (CombatCommon.TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 allyEnemyPosition))
            {
                supportPosition = allyEnemyPosition;
                CombatCommon.TrySelectPreferredSupportEnemy(
                    supportEnemyProfileId,
                    allyEnemyPosition,
                    out supportEnemy,
                    preferBackline: false,
                    promoteSelected: false);
            }

            return supportEnemy != null;
        }

        private bool TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent)
        {
            // NeedSniper is an explicit boss order. Unlike autonomous push support, it is allowed
            // to use the active squad push context even when the sniper has to retarget first.
            return CombatCommon.TryGetActivePushEvent(out pushEvent);
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ResetCommittedCover();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearInitialDecision();
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> RetryOrRejectObjective(string suffix)
        {
            if (Time.time >= searchRetryUntil)
            {
                return RejectObjective(suffix);
            }

            retryScanUntil = Time.time + SearchRetryScanSeconds;
            CombatCommon.HoldFor(SearchRetryScanSeconds);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{RetryHoldReason}.{suffix}");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> RejectObjective(string suffix)
        {
            complete = true;
            ClearObjectiveCommitments();
            BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
            BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
            return Hold(suffix);
        }

        private static bool IsRetryHoldReason(string? reason)
        {
            return reason != null && reason.StartsWith(RetryHoldReason, StringComparison.Ordinal);
        }

        private static bool IsArrivalEnd(string? reason)
        {
            return string.Equals(reason, "alreadyInCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "arrivedCommittedCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "arrivedCoverPoint", StringComparison.Ordinal);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
