using EFT;
using friendlySAIN.Modules;
using System;

namespace friendlySAIN.BigBrain
{
    internal sealed class StandardFollowerPmcCombatLogic : FollowerCombatLogicBase
    {
        private readonly FollowerCombatDefault decisionDefault;

        public StandardFollowerPmcCombatLogic(BotOwner botOwner) : base(botOwner)
        {
            decisionDefault = new FollowerCombatDefault(botOwner, combatCommon);
        }

        public override void Reset()
        {
            base.Reset();
            decisionDefault.Reset();
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            decisionDefault.DecisionChanged(prevDecision, nextDecision);
        }

        public override void StartDecision()
        {
            decisionDefault?.PrepareStartDecision();
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "!haveEnemy");
            }

            try
            {
                return decisionDefault.GetDecision(goalEnemy);
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    Logger.LogError(ex);
                    errorLogged = true;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "errorLogged");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "errorLogged2");
            }
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return decisionDefault.ShallEndCurrentDecision(currentDecision);
        }
    }
}
