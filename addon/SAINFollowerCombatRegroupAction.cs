using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINFollowerCombatRegroupAction : BotAction
    {
        private float _nextChangeSprintTime;

        public SAINFollowerCombatRegroupAction(BotOwner botOwner)
            : base(botOwner, nameof(SAINFollowerCombatRegroupAction))
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!TryGetBossPosition(out Vector3 bossPosition))
            {
                return;
            }

            Enemy enemy = Bot.GoalEnemy;
            bool hasEnemy = enemy != null;
            bool enemyLOS = enemy?.InLineOfSight == true;
            float leadDist = (bossPosition - BotOwner.Position).magnitude;
            float enemyDist = hasEnemy ? enemy.KnownPlaces.BotDistanceFromLastKnown : 999f;
            bool sprint = hasEnemy && leadDist > 30f && !enemyLOS && enemyDist > 50f;

            if (_nextChangeSprintTime < Time.time)
            {
                _nextChangeSprintTime = Time.time + 1f;
                if (sprint)
                {
                    Bot.Mover.RunToPoint(bossPosition);
                }
                else
                {
                    Bot.Mover.WalkToPoint(bossPosition);
                }
            }

            Bot.Mover.SetTargetPose(1f);
            Bot.Mover.SetTargetMoveSpeed(1f);
        }

        public override void OnSteeringTicked()
        {
            Enemy enemy = Bot.GoalEnemy;
            if (!Shoot.ShootAnyVisibleEnemies(enemy))
            {
                Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
            }

            if (!Bot.Steering.SteerByPriority(enemy))
            {
                Bot.Steering.LookToMovingDirection();
            }
        }

        private bool TryGetBossPosition(out Vector3 position)
        {
            position = default;
            if (BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            position = boss.realPlayer.Transform.position;
            return true;
        }
    }
}
