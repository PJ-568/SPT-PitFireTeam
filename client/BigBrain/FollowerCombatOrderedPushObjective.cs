using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatOrderedPushObjective : FollowerCombatObjectiveBase
    {
        private const string ReasonPrefix = "objectivePush";

        private readonly FollowerCombatPush combatPush;
        private bool complete;
        private string? targetProfileId;

        public FollowerCombatOrderedPushObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
            combatPush = new FollowerCombatPush(botOwner, combatCommon);
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            targetProfileId = null;
            combatPush.Reset();
        }

        public void Activate(EnemyInfo goalEnemy)
        {
            Reset();
            targetProfileId = goalEnemy?.ProfileId;
            CombatCommon.ClearInitialDecision();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
        }

        public override void Deactivate()
        {
            Reset();
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            combatPush.HandleDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!TryGetOrderedTarget(goalEnemy, out EnemyInfo? orderedEnemy, out string rejectReason) ||
                orderedEnemy == null)
            {
                complete = true;
                combatPush.ClearCommittedPush(rejectReason);
                return Hold(rejectReason);
            }

            BossPlayers.Instance?.GetFollower(BotOwner)?.RefreshOrderedPushTargetLock(orderedEnemy);

            if (CombatCommon.TryGetReloadRetreatDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
            {
                return reloadRetreatDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = CombatCommon.TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                return dogFightDecision.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = CombatCommon.InFightLogic();
            if (inFightDecision != null)
            {
                return inFightDecision.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            if (CombatCommon.HasActiveOrPendingHealWork())
            {
                combatPush.ClearCommittedPush("orderedPushHealPending");
                return Hold("healPending");
            }

            if (TryGetRecoveryDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> recoveryDecision))
            {
                combatPush.ClearCommittedPush("orderedPushRecovery");
                return recoveryDecision;
            }

            if (CombatCommon.HasCommittedPosition(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> pressureHoldDecision))
            {
                return pressureHoldDecision;
            }

            if (combatPush.TryGetCommittedPushDecision(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush))
            {
                return committedPush;
            }

            if (combatPush.TryCreateOrderedPushFiringPosition(
                    orderedEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> firingPositionDecision))
            {
                return firingPositionDecision;
            }

            return MarkOrderedPushDecision(combatPush.EngageEnemy(FollowerCombatPush.PushActivationSource.Ordered));
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (currentDecision.Action == BotLogicDecision.heal ||
                currentDecision.Action == BotLogicDecision.healStimulators)
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (!TryGetOrderedTarget(BotOwner.Memory?.GoalEnemy, out _, out string rejectReason))
            {
                complete = true;
                combatPush.ClearCommittedPush(rejectReason);
                return new AICoreActionEndStruct(rejectReason, true);
            }

            if (combatPush.IsPushCommittedDecision(currentDecision))
            {
                return combatPush.EndCommittedPush(currentDecision);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private bool TryGetOrderedTarget(
            EnemyInfo? currentGoalEnemy,
            out EnemyInfo? orderedEnemy,
            out string rejectReason)
        {
            orderedEnemy = currentGoalEnemy;
            rejectReason = string.Empty;

            if (string.IsNullOrEmpty(targetProfileId))
            {
                rejectReason = "orderedPushMissingTarget";
                return false;
            }

            if (currentGoalEnemy?.Person?.HealthController?.IsAlive == true &&
                string.Equals(currentGoalEnemy.ProfileId, targetProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            if (currentGoalEnemy?.Person?.HealthController?.IsAlive == true &&
                !string.Equals(currentGoalEnemy.ProfileId, targetProfileId, StringComparison.Ordinal) &&
                FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(currentGoalEnemy))
            {
                orderedEnemy = currentGoalEnemy;
                return true;
            }

            if (!CombatCommon.TryForceGoalEnemy(
                    targetProfileId,
                    "orderedPushTarget",
                    out orderedEnemy) ||
                orderedEnemy == null)
            {
                rejectReason = "orderedPushTargetMissingOrDead";
                return false;
            }

            return true;
        }

        private bool TryGetRecoveryDecision(
            EnemyInfo orderedEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            bool pressured =
                BotOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(BotOwner, 1f) ||
                FollowerAwareness.WasRecentlyDamaged(BotOwner);
            if (!pressured)
            {
                return false;
            }

            if (FollowerImmediateFirePolicy.IsLocalSelfDefenseThreat(orderedEnemy))
            {
                return false;
            }

            if (CombatCommon.HasCommittedPosition(out decision))
            {
                return true;
            }

            if (CombatCommon.HasCommittedCover() && CombatCommon.IsBotInCommittedCover())
            {
                CombatCommon.ArmCommittedArrivalHold("orderedPushRecovery");
                if (CombatCommon.HasCommittedPosition(out decision))
                {
                    return true;
                }

                CombatCommon.HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "objectivePush.recoveryCoverHold");
                return true;
            }

            bool requireShootLane = orderedEnemy.IsVisible && orderedEnemy.CanShoot;
            if (CombatCommon.TryCommitCombatCover(
                    orderedEnemy,
                    requireShootLane,
                    CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                    out string coverReason,
                    avoidBossFireLane: true))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision($"objectivePush.recovery.{coverReason}");
                return true;
            }

            if (orderedEnemy.IsVisible && orderedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "objectivePush.recoveryNoCoverSuppress");
                return true;
            }

            return false;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> MarkOrderedPushDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null ||
                decision.Reason.StartsWith("push.ordered", StringComparison.Ordinal))
            {
                return decision;
            }

            if (decision.Reason.StartsWith("push.", StringComparison.Ordinal))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    decision.Action,
                    "push.ordered." + decision.Reason.Substring("push.".Length));
            }

            return decision;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
