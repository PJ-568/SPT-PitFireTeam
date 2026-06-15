using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatGrenadierObjective : FollowerCombatObjectiveBase
    {
        internal const float OpportunityWindowSeconds = 5f;
        internal const string ReasonPrefix = "objectiveGrenadier";
        private const string RetryHoldReason = "objectiveGrenadier.retry";
        private const string AutonomousActivationReason = "objectiveGrenadier.activateAuto";
        private const float RetryScanSeconds = 0.25f;

        private bool complete;
        private bool active;
        private bool ordered;
        private bool negativeSaid;
        private bool cooldownRecorded;
        private float activeUntil;
        private float retryScanUntil;
        private FollowerCombatCommon.GrenadeLauncherSuppressPlan? launcherPlan;

        public FollowerCombatGrenadierObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            active = false;
            ordered = false;
            negativeSaid = false;
            cooldownRecorded = false;
            activeUntil = 0f;
            retryScanUntil = 0f;
            launcherPlan = null;
        }

        public override void Activate()
        {
            Activate(ordered: false);
        }

        public void Activate(bool ordered)
        {
            Reset();
            active = true;
            this.ordered = ordered;
            activeUntil = Time.time + OpportunityWindowSeconds;
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            DeactivateForObjectiveSwitch("deactivate");
        }

        public void DeactivateForObjectiveSwitch(string reason)
        {
            if (!active)
            {
                return;
            }

            RecordAttemptCooldown(reason);
            active = false;
            CombatCommon.PrepareLauncherSuppressWeaponFallback();
            ClearObjectiveCommitments();
            complete = false;
            launcherPlan = null;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            if (IsLauncherMoveReason(nextDecision.Reason))
            {
                return;
            }

            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return FailObjective("noEnemy");
            }

            if (!CombatCommon.HasUsableSecondPrimaryGrenadeLauncher())
            {
                return FailObjective("noUsableLauncher");
            }

            if (TryGetEmergencyDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> emergencyDecision))
            {
                complete = true;
                RecordAttemptCooldown($"emergency.{emergencyDecision.Reason ?? emergencyDecision.Action.ToString()}");
                CombatCommon.PrepareLauncherSuppressWeaponFallback();
                ClearObjectiveCommitments();
                return emergencyDecision;
            }

            if (Time.time >= activeUntil)
            {
                return FailObjective("windowExpired");
            }

            if (TryGetLauncherDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> launcherDecision))
            {
                return launcherDecision;
            }

            return RetryOrFail("noOpportunity");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!IsGrenadierReason(currentDecision.Reason))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                RecordAttemptCooldown("enemyMissing");
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("grenadierEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                return EndLauncherSuppress(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.goToPoint &&
                IsLauncherMoveReason(currentDecision.Reason))
            {
                return EndLauncherMove();
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition &&
                IsRetryHoldReason(currentDecision.Reason))
            {
                return EndRetryHold();
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return new AICoreActionEndStruct("grenadierHoldComplete", true);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        internal static bool IsAutonomousActivationReason(string? reason)
        {
            return string.Equals(reason, AutonomousActivationReason, StringComparison.Ordinal);
        }

        internal static AICoreActionResultStruct<BotLogicDecision, GClass26> CreateAutonomousActivationDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                AutonomousActivationReason);
        }

        private bool TryGetEmergencyDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (CombatCommon.TryGetDogFightDecision() is { } dogFightDecision)
            {
                decision = dogFightDecision;
                return true;
            }

            if (CombatCommon.TryGetNeedHealDecision() is { } healDecision)
            {
                decision = healDecision;
                return true;
            }

            if (CombatCommon.TryGetReloadRetreatDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
            {
                decision = reloadRetreatDecision;
                return true;
            }

            return false;
        }

        private bool TryGetLauncherDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (launcherPlan != null && TryUseLauncherPlan(goalEnemy, launcherPlan, out decision))
            {
                return true;
            }

            launcherPlan = null;
            if (!CombatCommon.TryPrepareGrenadeLauncherSuppressPlan(
                    goalEnemy,
                    ReasonPrefix,
                    ordered,
                    out FollowerCombatCommon.GrenadeLauncherSuppressPlan? preparedPlan) ||
                preparedPlan == null)
            {
                return false;
            }

            launcherPlan = preparedPlan;
            return TryUseLauncherPlan(goalEnemy, preparedPlan, out decision);
        }

        private bool TryUseLauncherPlan(
            EnemyInfo goalEnemy,
            FollowerCombatCommon.GrenadeLauncherSuppressPlan plan,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (plan.HasSuppressFrom &&
                !CombatCommon.IsAtGrenadeLauncherSuppressPosition(plan))
            {
                decision = CombatCommon.CreateGrenadeLauncherMoveDecision(plan);
                return true;
            }

            if (CombatCommon.TryStartGrenadeLauncherSuppressDecision(goalEnemy, plan, out decision))
            {
                launcherPlan = null;
                return true;
            }

            launcherPlan = null;
            return false;
        }

        private AICoreActionEndStruct EndLauncherSuppress(string? reason)
        {
            AICoreActionEndStruct end = CombatCommon.EndSuppressFire(reason);
            if (!end.Value)
            {
                return end;
            }

            if (ShouldRetryAfterLauncherEnd(end.Reason) && Time.time < activeUntil)
            {
                CombatCommon.ClearFollowerSuppressState();
                launcherPlan = null;
                return end;
            }

            complete = true;
            RecordAttemptCooldown($"end.{end.Reason}");
            CombatCommon.PrepareLauncherSuppressWeaponFallback();
            ClearObjectiveCommitments();
            return end;
        }

        private AICoreActionEndStruct EndLauncherMove()
        {
            if (launcherPlan == null)
            {
                launcherPlan = null;
                return new AICoreActionEndStruct("grenadierMovePlanMissing", true);
            }

            if (CombatCommon.IsAtGrenadeLauncherSuppressPosition(launcherPlan))
            {
                return new AICoreActionEndStruct("grenadierMoveArrived", true);
            }

            AICoreActionEndStruct moveEnd = CombatCommon.EndGoToPoint(endWhenEnemyVisibleShootable: false);
            if (moveEnd.Value)
            {
                CombatCommon.ClearCommittedMovement();
            }

            return moveEnd.Value ? moveEnd : default;
        }

        private AICoreActionEndStruct EndRetryHold()
        {
            if (Time.time >= retryScanUntil)
            {
                return new AICoreActionEndStruct("grenadierRetryScan", true);
            }

            CombatCommon.HoldFor(Mathf.Max(0.05f, retryScanUntil - Time.time));
            return default;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> RetryOrFail(string suffix)
        {
            if (Time.time >= activeUntil)
            {
                return FailObjective(suffix);
            }

            retryScanUntil = Mathf.Min(activeUntil, Time.time + RetryScanSeconds);
            LookTowardEnemy();
            CombatCommon.HoldFor(Mathf.Max(0.05f, retryScanUntil - Time.time));
            BattleRecorder.RecordObjectiveDiagnostic(
                BotOwner,
                nameof(FollowerCombatGrenadierObjective),
                "retry",
                suffix);
            return Hold($"retry.{suffix}");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> FailObjective(string suffix)
        {
            complete = true;
            RecordAttemptCooldown($"fail.{suffix}");
            CombatCommon.PrepareLauncherSuppressWeaponFallback();
            ClearObjectiveCommitments();
            BattleRecorder.RecordObjectiveDiagnostic(
                BotOwner,
                nameof(FollowerCombatGrenadierObjective),
                "reject",
                suffix);
            if (ordered)
            {
                SayNegativeOnce();
            }

            return Hold(suffix);
        }

        private void SayNegativeOnce()
        {
            if (negativeSaid)
            {
                return;
            }

            negativeSaid = true;
            BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
            BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
        }

        private void LookTowardEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            Vector3 enemyPosition = FollowerCombatCommon.GetEnemyCurrentPosition(goalEnemy);
            if (FollowerCombatCommon.IsFinite(enemyPosition))
            {
                BotOwner.Steering.LookToPoint(enemyPosition);
            }
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ClearFollowerSuppressState();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearInitialDecision();
        }

        private void RecordAttemptCooldown(string reason)
        {
            if (cooldownRecorded)
            {
                return;
            }

            cooldownRecorded = true;
            CombatCommon.StartGrenadeLauncherSuppressCooldown(ordered, reason);
        }

        private static bool ShouldRetryAfterLauncherEnd(string? endReason)
        {
            return string.Equals(endReason, "followerSuppressHardBlockedLane", StringComparison.Ordinal) ||
                   string.Equals(endReason, "followerSuppressBlockedLane", StringComparison.Ordinal) ||
                   string.Equals(endReason, "launcherImpactUnsafe", StringComparison.Ordinal) ||
                   string.Equals(endReason, "launcherNoLane", StringComparison.Ordinal);
        }

        internal static bool IsGrenadierReason(string? reason)
        {
            return reason != null && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        private static bool IsLauncherMoveReason(string? reason)
        {
            return string.Equals(reason, $"{ReasonPrefix}.launcherMove", StringComparison.Ordinal);
        }

        private static bool IsRetryHoldReason(string? reason)
        {
            return reason != null && reason.StartsWith(RetryHoldReason, StringComparison.Ordinal);
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
