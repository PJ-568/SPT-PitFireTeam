using EFT;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatSniperObjective : FollowerCombatObjectiveBase
    {
        private readonly FollowerCombatSniper decisionSniper;

        public FollowerCombatSniperObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
            decisionSniper = new FollowerCombatSniper(botOwner, combatCommon);
        }

        public override void Reset()
        {
            decisionSniper.Reset();
        }

        public override void Activate()
        {
            // Returning from regroup should discard stale sniper-combat commitments, but it
            // must not look like a fresh combat entry that seeds PrepareStartDecision again.
            decisionSniper.Reset();
            CombatCommon.ClearInitialDecision();
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            decisionSniper.DecisionChanged(prevDecision, nextDecision);
        }

        public override void StartDecision()
        {
            decisionSniper.PrepareStartDecision();
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            return decisionSniper.GetDecision(goalEnemy);
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return decisionSniper.ShallEndCurrentDecision(currentDecision);
        }
    }
}
