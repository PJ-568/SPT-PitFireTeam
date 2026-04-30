using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal class EatDrinkAction : CustomLogic
    {
        private readonly GClass261 baseLogic;

        public EatDrinkAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass261(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
