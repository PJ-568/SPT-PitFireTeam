using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Retreating fire action. It reuses the follower attack-moving wrapper, keeps the caller's
    /// selected retreat cover authoritative, and forces threat look when a shot is available so the
    /// follower does not turn his back while backing out.
    /// </summary>
    internal sealed class CombatAttackRetreatAction : CombatAttackMovingAction
    {
        public CombatAttackRetreatAction(BotOwner botOwner)
            : base(botOwner, withSuppress: true, autoCover: false, forceThreatLookWhenShootable: true)
        {
        }
    }
}
