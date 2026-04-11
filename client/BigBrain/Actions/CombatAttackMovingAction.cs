using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly GClass205 baseLogic;

        public CombatAttackMovingAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new FollowerAttackMovingLogic(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        private sealed class FollowerAttackMovingLogic : GClass205
        {
            public FollowerAttackMovingLogic(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void AimingAndShoot(GClass26 data)
            {
                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
                {
                    if (BotOwner_0.WeaponManager.UnderbarrelLauncherController.CanSwitchInFight(BotOwner_0))
                    {
                        BotOwner_0.WeaponManager.UnderbarrelLauncherController.TryEnable(null);
                    }

                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy);
            }
        }
    }
}
