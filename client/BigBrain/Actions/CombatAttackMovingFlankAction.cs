using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class CombatAttackMovingFlankAction : FollowerCombatActionBase
    {
        private readonly GClass209 baseLogic;

        public CombatAttackMovingFlankAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass209(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass29>(data));
        }
    }
}
