using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromCoverAction : FollowerCombatActionBase
    {
        private readonly GClass277 baseLogic;

        public CombatShootFromCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass277(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }
}
