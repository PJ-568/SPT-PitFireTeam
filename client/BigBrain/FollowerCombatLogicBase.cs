using EFT;

namespace friendlySAIN.BigBrain
{
    internal abstract class FollowerCombatLogicBase
    {
        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;
        protected readonly FollowerCombatCommon combatCommon;
        protected bool errorLogged;

        protected FollowerCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
            combatCommon = new FollowerCombatCommon(botOwner);
        }

        public bool ShallUseNow() => combatCommon.HasActiveCombatEnemy();

        public virtual void Reset() => combatCommon.Reset();

        public abstract AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision();

        public abstract AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision);

        public abstract void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision);

        public abstract void StartDecision();
    }
}
