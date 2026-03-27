using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRunToEnemyAction : FollowerCombatActionBase
    {
        private readonly GClass227 baseLogic;

        public CombatRunToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass227(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
