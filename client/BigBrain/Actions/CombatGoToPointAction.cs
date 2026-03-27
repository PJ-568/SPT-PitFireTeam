using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToPointAction : FollowerCombatActionBase
    {
        private readonly GClass219 baseLogic;

        public CombatGoToPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass219(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass30>(data));
        }
    }
}
