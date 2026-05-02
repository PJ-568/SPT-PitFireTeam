using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla stimulator use. The combat/patrol layers decide when stimulators are
    /// safe or urgent; this action only updates the stock stim node.
    /// </summary>
    internal class HealStimulatorsAction : CustomLogic
    {
        private readonly GClass283 baseLogic;

        public HealStimulatorsAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass283(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
