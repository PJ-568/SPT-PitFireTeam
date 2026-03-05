using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINFollowerCombatSuppressAction : BotAction
    {
        public SAINFollowerCombatSuppressAction(BotOwner botOwner)
            : base(botOwner, nameof(SAINFollowerCombatSuppressAction))
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            var enemy = Bot.GoalEnemy;
            if (enemy == null)
            {
                return;
            }

            Vector3? lastKnown = enemy.LastKnownPosition;
            if (lastKnown != null)
            {
                Bot.Mover.WalkToPointByWay(enemy.Path.PathToEnemy);
            }
        }

        public override void OnSteeringTicked()
        {
            var enemy = Bot.GoalEnemy;
            if (Shoot.ShootAnyVisibleEnemies(enemy))
            {
                Bot.Steering.SteerByPriority(enemy, false);
                return;
            }

            if (Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies))
            {
                Bot.Steering.SteerByPriority(enemy, false);
                return;
            }

            Bot.Suppression.ResetSuppressing();
            if (!Bot.Steering.SteerByPriority(enemy, false))
            {
                Bot.Steering.LookToLastKnownEnemyPosition(enemy);
            }
        }

        public override void Stop()
        {
            base.Stop();
            Bot.Suppression.ResetSuppressing();
        }
    }
}
