using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla peaceful idle behavior. Used when the follower should remain in a
    /// non-combat state while other follower layers own movement or command selection.
    /// </summary>
    internal class PeacefulAction : CustomLogic
    {
        private readonly GClass266 baseLogic;

        public PeacefulAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass266(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
