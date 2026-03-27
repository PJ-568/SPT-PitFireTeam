using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly GClass205 baseLogic;

        public CombatAttackMovingAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass205(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
