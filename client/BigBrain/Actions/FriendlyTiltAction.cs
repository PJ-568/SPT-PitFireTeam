using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla friendly tilt behavior. Used by peaceful/follower layers when the
    /// bot should lean or tilt without entering combat movement.
    /// </summary>
    internal class FriendlyTiltAction : CustomLogic
    {
        private readonly GClass262 baseLogic;

        public FriendlyTiltAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass262(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
