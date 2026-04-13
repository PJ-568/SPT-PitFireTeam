using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    /// <summary>
    /// Direct port of the old plugin's FollowerAttackMove behavior onto the 4.x attack-moving base.
    /// </summary>
    internal class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly FollowerAttackMovingLogic baseLogic;

        protected CombatAttackMovingAction(BotOwner botOwner, bool withSuppress, bool autoCover = true) : base(botOwner)
        {
            baseLogic = new FollowerAttackMovingLogic(botOwner, withSuppress, autoCover);
        }

        public CombatAttackMovingAction(BotOwner botOwner) : this(botOwner, withSuppress: false)
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        private sealed class FollowerAttackMovingLogic : GClass205
        {
            private readonly bool autoCover;
            private readonly bool withSuppress;
            private float nextSuppressToggleTime;
            private bool suppressBurstActive;
            private float nextThreatLookTime;

            public FollowerAttackMovingLogic(BotOwner botOwner, bool withSuppress, bool autoCover) : base(botOwner)
            {
                this.withSuppress = withSuppress;
                this.autoCover = autoCover;
            }

            public override void UpdateNodeByBrain(GClass26 data)
            {
                if (!autoCover && BotOwner_0.Memory?.CurCustomCoverPoint != null)
                {
                    ForceCurrentCoverDestination();
                }

                base.UpdateNodeByBrain(data);
            }

            public override void AimingAndShoot(GClass26 data)
            {
                if (nextSuppressToggleTime < Time.time)
                {
                    nextSuppressToggleTime = Time.time + GClass856.Random(2f, 4f);
                    suppressBurstActive = !suppressBurstActive;
                }

                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if ((withSuppress && suppressBurstActive) ||
                    (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible))
                {
                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                if (goalEnemy != null && nextThreatLookTime < Time.time)
                {
                    nextThreatLookTime = Time.time + GClass856.Random(2f, 3f);
                    BotOwner_0.Steering.LookToPoint(goalEnemy.EnemyLastPosition + new Vector3(0f, 0.6f, 0f));
                }
            }

            private void ForceCurrentCoverDestination()
            {
                CustomNavigationPoint cover = BotOwner_0.Memory.CurCustomCoverPoint;
                bool withShoot = BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                                 BotOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                BotOwner_0.SetTargetMoveSpeed(1f);
                BotOwner_0.Sprint(false, true);
                BotOwner_0.SetPose(1f);
                BotOwner_0.Memory.SetCoverPoints(cover, string.Empty);
                BotOwner_0.GoToPoint(cover);

                if (!cover.CanIShootToEnemy && withShoot)
                {
                    BotOwner_0.BotAttackManager.UpdateNextTick();
                }
            }
        }
    }
}
