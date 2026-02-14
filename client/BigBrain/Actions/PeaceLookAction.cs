using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal class PeaceLookAction : CustomLogic
    {
        private readonly GClass268 baseLogic;

        public PeaceLookAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass268(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
