using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
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
