using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class CombatAttackMovingWithSuppressAction : CombatAttackMovingAction
    {
        public CombatAttackMovingWithSuppressAction(BotOwner botOwner) : base(botOwner, withSuppress: true)
        {
        }
    }
}
