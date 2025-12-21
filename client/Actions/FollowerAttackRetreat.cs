using EFT;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    internal class FollowerAttackRetreat : FollowerAttackMove
    {
        public FollowerAttackRetreat(BotOwner bot) : base(bot)
        {
            _autoCover = false;
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            base.UpdateNodeByBrain(data);
        }
    }
}
