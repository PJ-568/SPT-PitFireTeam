using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal class GestureAction : CustomLogic
    {
        private readonly GClass263 baseLogic;

        public GestureAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass263(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
