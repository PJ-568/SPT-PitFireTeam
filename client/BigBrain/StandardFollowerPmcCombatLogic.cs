using EFT;

namespace friendlySAIN.BigBrain
{
    internal sealed class StandardFollowerPmcCombatLogic : FollowerCombatLogicBase
    {
        public StandardFollowerPmcCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        protected override FollowerCombatObjectiveBase CreateDefaultObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatDefaultObjective(botOwner, combatCommon);
        }
    }
}
