using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal class HealAction : CustomLogic
    {
        private GClass197 baseLogic;
        public HealAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass197(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}