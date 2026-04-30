using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class CombatShootToSmokeAction : FollowerCombatActionBase
    {
        private readonly GClass185 baseLogic;

        public CombatShootToSmokeAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass185(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }
    }
}
