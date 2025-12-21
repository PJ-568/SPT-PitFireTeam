using EFT;
using UnityEngine;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Overwrite of attackMoving decision to fix bot's aiming direction
     */
    public class FollowerAttackMove : GClass198
    {

        protected bool _autoCover = true;
        protected bool _withSuppress = false;

        private float float_3 = 0f;
        private bool bool_0 = false;
        private float float_4 = 0f;
        public FollowerAttackMove(BotOwner bot, bool withSuppress = false) : base(bot)
        {
            _withSuppress = withSuppress;
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            if (!_autoCover)
            {
                float_0 = Time.time;
                GClass198.Class115 @class = new GClass198.Class115();
                @class.gclass198_0 = this;
                botOwner_0.SetTargetMoveSpeed(1f);
                botOwner_0.Sprint(false, true);
                botOwner_0.SetPose(1f);
                @class.recalcTime = 0f;
                @class.withShoot = botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) || botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                if (botOwner_0.Memory.CurCustomCoverPoint != null)
                {
                    @class.method_0(botOwner_0.Memory.CurCustomCoverPoint);
                }
            }

            base.UpdateNodeByBrain(data);
        }

        public override void AimingAndShoot(StandardBrain data)
        {
            if (float_3 < Time.time)
            {
                float_3 = Time.time + Utils.Utils.Random(2f, 4f);
                bool_0 = !bool_0;
            }

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if ((bool_0 && _withSuppress) || (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible))
            {
                gclass171_0.UpdateNodeByBrain(data as GClass27);
                return;
            }

            if (goalEnemy != null && float_4 < Time.time)
            {
                float_4 = Time.time + Utils.Utils.Random(2f, 3f);
                botOwner_0.Steering.LookToPoint(botOwner_0.Memory.GoalEnemy.EnemyLastPosition + new Vector3(0, 0.6f, 0));
            }
        }
    }
}
