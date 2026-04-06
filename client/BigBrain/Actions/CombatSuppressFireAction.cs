using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatSuppressFireAction : FollowerCombatActionBase
    {
        private readonly GClass281 baseLogic;

        public CombatSuppressFireAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass281(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy != null && FollowerShotSafety.IsFriendlyInShotLane(BotOwner, goalEnemy.CurrPosition))
            {
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }
    }
}
