using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Attack-moving variant that enables intermittent suppressive fire toward recent enemy contact
    /// while the follower moves. Used when the route itself is part of pressure or retreat fire.
    /// </summary>
    internal sealed class CombatAttackMovingWithSuppressAction : CombatAttackMovingAction
    {
        public CombatAttackMovingWithSuppressAction(BotOwner botOwner) : base(botOwner, withSuppress: true)
        {
        }
    }
}
