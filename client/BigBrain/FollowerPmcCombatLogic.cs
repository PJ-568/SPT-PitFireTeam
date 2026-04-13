using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerPmcCombatLogic : FollowerCombatLogicBase
    {
        public FollowerPmcCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void StartDecision()
        {
            currentObjective = CombatObjectiveKind.Default;
            defaultObjective.StartDecision();
        }

        protected override FollowerCombatObjectiveBase GetObjective()
        {
            return defaultObjective;
        }
    }
}
