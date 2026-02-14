using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
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
