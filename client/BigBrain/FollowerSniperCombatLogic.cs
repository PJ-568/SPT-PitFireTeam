using EFT;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerSniperCombatLogic : FollowerCombatLogicBase
    {
        public FollowerSniperCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void StartDecision()
        {
            currentObjective = CombatObjectiveKind.Default;
            sniperObjective.StartDecision();
        }

        protected override FollowerCombatObjectiveBase GetObjective()
        {
            return sniperObjective;
        }
    }
}
