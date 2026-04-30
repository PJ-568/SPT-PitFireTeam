using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal class PeaceHardAimAction : CustomLogic
    {
        private readonly GClass267 baseLogic;

        public PeaceHardAimAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass267(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
