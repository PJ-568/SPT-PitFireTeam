using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatHoldPositionAction : FollowerCombatActionBase
    {
        private readonly GClass278 baseLogic;

        public CombatHoldPositionAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass278(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }
}
