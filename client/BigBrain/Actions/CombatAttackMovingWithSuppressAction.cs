using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatAttackMovingWithSuppressAction : FollowerCombatActionBase
    {
        private readonly GClass206 baseLogic;

        public CombatAttackMovingWithSuppressAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new FollowerAttackMovingWithSuppressLogic(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy != null && FollowerShotSafety.IsFriendlyInShotLane(BotOwner, goalEnemy.CurrPosition))
            {
                BotOwner.StopMove();
                BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                return;
            }

            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        private sealed class FollowerAttackMovingWithSuppressLogic : GClass206
        {
            private float nextSuppressToggleTime;
            private bool suppressBurstActive;

            public FollowerAttackMovingWithSuppressLogic(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void AimingAndShoot(GClass26 data)
            {
                if (nextSuppressToggleTime < Time.time)
                {
                    nextSuppressToggleTime = Time.time + GClass856.Random(2f, 4f);
                    suppressBurstActive = !suppressBurstActive;
                }

                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
                {
                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                if (suppressBurstActive)
                {
                    CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy);
                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy);
            }
        }
    }
}
