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
        private float _coverRecalcTime = 0f;
        private int _ambushShootCount = 0;
        public FollowerAttackMove(BotOwner bot, bool withSuppress = false) : base(bot)
        {
            _withSuppress = withSuppress;
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            if (!_autoCover)
            {
                float_0 = Time.time;
                botOwner_0.SetTargetMoveSpeed(1f);
                botOwner_0.Sprint(false, true);
                botOwner_0.SetPose(1f);
                bool withShoot = botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) || botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect);

                if (botOwner_0.Memory.CurCustomCoverPoint != null)
                {
                    ApplyCoverPoint(botOwner_0.Memory.CurCustomCoverPoint, withShoot);
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

        private void ApplyCoverPoint(CustomNavigationPoint navigationPoint, bool withShoot)
        {
            botOwner_0.BotTalk.TrySay(EPhraseTrigger.OnFight, true);
            float now = Time.time;
            if (now - _coverRecalcTime < 2f && botOwner_0.Memory.CurCustomCoverPoint != null)
            {
                if (!botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy && withShoot)
                {
                    _ambushShootCount++;
                    if (_ambushShootCount > botOwner_0.Settings.FileSettings.Shoot.CAN_SHOOTS_TIME_TO_AMBUSH)
                    {
                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Ambush, true, 2f);
                    }
                }
            }
            else
            {
                _ambushShootCount = 0;
            }

            _coverRecalcTime = now;
            botOwner_0.Memory.SetCoverPoints(navigationPoint, "");
            botOwner_0.GoToPoint(navigationPoint);
        }
    }
}
