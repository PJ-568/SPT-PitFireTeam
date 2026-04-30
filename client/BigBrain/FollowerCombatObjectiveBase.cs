using EFT;

namespace pitTeam.BigBrain
{
    internal abstract class FollowerCombatObjectiveBase
    {
        protected readonly BotOwner BotOwner;
        protected readonly FollowerCombatCommon CombatCommon;

        protected FollowerCombatObjectiveBase(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            BotOwner = botOwner;
            CombatCommon = combatCommon;
        }

        public virtual bool IsComplete => false;

        public virtual void Reset()
        {
        }

        public virtual void Activate()
        {
        }

        public virtual void Deactivate()
        {
        }

        public abstract void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision);

        public abstract void StartDecision();

        public abstract AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy);

        public abstract AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision);
    }
}
