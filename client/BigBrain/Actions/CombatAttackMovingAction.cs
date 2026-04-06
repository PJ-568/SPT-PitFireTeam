using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

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
            private float nextLookAtEnemyTime;

            public FollowerAttackMovingLogic(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void AimingAndShoot(GClass26 data)
            {
                EnemyInfo goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
                {
                    if (BotOwner_0.WeaponManager.UnderbarrelLauncherController.CanSwitchInFight(BotOwner_0))
                    {
                        BotOwner_0.WeaponManager.UnderbarrelLauncherController.TryEnable(null);
                    }

                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                if (goalEnemy != null && nextLookAtEnemyTime < Time.time)
                {
                    nextLookAtEnemyTime = Time.time + GClass856.Random(2f, 3f);
                    BotOwner_0.Steering.LookToPoint(goalEnemy.EnemyLastPositionReal + new Vector3(0f, 0.6f, 0f));
                    return;
                }

                BotOwner_0.LookData.SetLookPointByHearing(null);
            }
        }
    }
}
