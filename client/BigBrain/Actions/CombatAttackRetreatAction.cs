using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    /// <summary>
    /// Port of old FollowerAttackRetreat: same attack-move aim behavior, but it keeps pushing
    /// the already selected cover point so the bot backpedals/strafe-retreats instead of
    /// letting vanilla attack-moving immediately search a new forward cover.
    /// </summary>
    internal sealed class CombatAttackRetreatAction : CombatAttackMovingAction
    {
        public CombatAttackRetreatAction(BotOwner botOwner)
            : base(botOwner, withSuppress: false, autoCover: false, forceThreatLookWhenShootable: true)
        {
        }
    }
}
