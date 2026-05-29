using EFT;
using System;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatSuppressionObjective : FollowerCombatObjectiveBase
    {
        internal const string ReasonPrefix = "objectiveSuppress";

        private bool complete;

        public FollowerCombatSuppressionObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
        }

        public override void Activate()
        {
            Reset();
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            ClearObjectiveCommitments();
            complete = false;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                return Hold("noEnemy");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                return dogFight.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            if (CombatCommon.TryCreateGrenadeLauncherSuppressDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> launcherDecision,
                    ordered: true))
            {
                return launcherDecision;
            }

            if (CombatCommon.TryCreateSuppressDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
                    allowObstructedSuppression: true))
            {
                return decision;
            }

            complete = true;
            return Hold("noSuppressionDecision");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!IsSuppressionObjectiveReason(currentDecision.Reason))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                AICoreActionEndStruct end = CombatCommon.EndSuppressFire(currentDecision.Reason);
                if (end.Value)
                {
                    complete = true;
                    ClearObjectiveCommitments();
                }

                return end;
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionNoAction", true);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        internal static bool IsSuppressionObjectiveReason(string? reason)
        {
            return reason != null && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ClearFollowerSuppressState();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearInitialDecision();
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
