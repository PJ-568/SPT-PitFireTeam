using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatDogFightAction : FollowerCombatActionBase
    {
        private readonly GClass178 shootLogic;
        private readonly Queue<Vector3> movementQueue = new Queue<Vector3>();

        private bool isMoving;
        private float nextMovementCheckTime;

        public CombatDogFightAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new GClass183(botOwner);
        }

        public override void Start()
        {
            base.Start();
            movementQueue.Clear();
            isMoving = false;
            nextMovementCheckTime = 0f;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                BotOwner.Mover.Stop();
                BotOwner.LookData.SetLookPointByHearing(null);
                BotOwner.SetPose(1f);
                return;
            }

            BotOwner.Sprint(false, true);
            BotOwner.Mover.SetTargetMoveSpeed(1f);
            Vector3 shootPoint = shootLogic.GetTarget() ?? goalEnemy.GetBodyPartPosition();

            bool isVeryClose = Utils.Enemy.Distance(goalEnemy) <= Utils.Enemy.EnemyDistance.VeryClose;
            bool tense = goalEnemy.IsVisible && isVeryClose;
            if (tense)
            {
                BotOwner.SetPose(0.8f);
            }

            if (goalEnemy.CanShoot && goalEnemy.IsVisible)
            {

                if (BotOwner.DoorOpener.DogFightHaveNearestDoor())
                {
                    BotOwner.Mover.Stop();
                }
                else
                {
                    if (isVeryClose && nextMovementCheckTime < Time.time)
                    {
                        FightMovementClose(goalEnemy);
                    }

                    if (!isMoving)
                    {
                        BotOwner.DogFight.Fight();
                    }
                }



                BotOwner.Steering.LookToPoint(shootPoint);
                shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
                return;
            }

            if (!tense)
            {
                BotOwner.SetPose(1f);
            }

            if (BotOwner.DoorOpener.DogFightHaveNearestDoor())
            {
                BotOwner.Mover.Stop();
            }

            BotOwner.Steering.LookToPoint(shootPoint);
            BotOwner.DogFight.Fight();
        }

        private void FightMovementClose(EnemyInfo goalEnemy)
        {
            if (isMoving && BotOwner.Mover.IsComeTo(0.5f, false))
            {
                isMoving = false;
            }

            if (isMoving)
            {
                return;
            }

            if (movementQueue.Count > 0)
            {
                Vector3 nextMove = movementQueue.Dequeue();
                isMoving = BotOwner.GoToPoint(nextMove, true, -1f, false, false) == NavMeshPathStatus.PathComplete;
                return;
            }

            GenerateArcMovement(goalEnemy);
            if (movementQueue.Count == 0)
            {
                nextMovementCheckTime = Time.time + 5f;
            }
        }

        private void GenerateArcMovement(EnemyInfo goalEnemy)
        {
            movementQueue.Clear();

            Vector3 botPos = BotOwner.Position;
            Vector3 enemyPos = goalEnemy.CurrPosition;
            Vector3 toEnemy = (enemyPos - botPos).normalized;
            float strafeSign = Random.value > 0.5f ? 1f : -1f;
            Vector3 strafeDir = Vector3.Cross(toEnemy, Vector3.up) * strafeSign;

            // Only lateral/forward points — no backward retreat.
            // Backward arc points require ~180° body rotation; aim-clamp cannot maintain
            // look-at-enemy during that turn, causing the bot to fire in the wrong direction.
            Vector3[] arcMoves = new[]
            {
                botPos + strafeDir * 2f,
                botPos + (strafeDir + toEnemy).normalized * 2f,
                botPos - strafeDir * 2f
            };

            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(false);
            for (int i = 0; i < arcMoves.Length; i++)
            {
                if (!NavMesh.SamplePosition(arcMoves[i], out NavMeshHit navMeshHit, 2f, -1))
                {
                    continue;
                }

                Vector3 candidate = navMeshHit.position;
                if (global::friendlySAIN.Utils.Utils.CanShootToTarget(shootPoint, candidate, BotOwner.LookSensor.Mask, false))
                {
                    movementQueue.Enqueue(candidate);
                }
            }

            nextMovementCheckTime = Time.time + 0.2f;
        }
    }
}
