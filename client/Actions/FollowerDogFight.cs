using EFT;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Custom DogFight
     */
    public class FollowerDogFight : GClass196
    {
        public FollowerDogFight(BotOwner bot) : base(bot)
        {

        }

        private Queue<Vector3> movementQueue = new Queue<Vector3>();

        private bool isMoving = false; // Track if bot is currently moving
        private float movementCheckTime = 0f;

        private void FightMovementClose()
        {

            // If bot is moving, check if it has reached the position
            if (isMoving && botOwner_0.Mover.IsComeTo(0.5f, false))
            {
                isMoving = false; // Stop moving
            }

            // If still moving, don't change direction
            if (isMoving) return;

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            // Get bot & enemy positions
            Vector3 botPos = botOwner_0.Position;
            Vector3 enemyPos = goalEnemy.CurrPosition;

            // Get direction to enemy
            Vector3 toEnemy = (enemyPos - botPos).normalized;

            // If we have more moves in queue, execute next step
            if (movementQueue.Count > 0)
            {
                Vector3 nextMove = movementQueue.Dequeue();
                isMoving = botOwner_0.GoToPoint(nextMove, true, -1f, false, false) == NavMeshPathStatus.PathComplete;
                return;
            }

            // Generate a new arc movement if idle
            GenerateArcMovement(botPos, toEnemy);

            if (movementQueue.Count <= 0)
            {
                movementCheckTime = Time.time + 5f;
            }
        }

        // Generates the arc movement path
        private void GenerateArcMovement(Vector3 botPos, Vector3 toEnemy)
        {
            movementQueue.Clear();

            // Pick a random strafe direction (left or right)
            float strafeSign = Random.value > 0.5f ? 1f : -1f;
            Vector3 strafeDir = Vector3.Cross(toEnemy, Vector3.up) * strafeSign;

            // Define arc movement pattern
            Vector3[] arcMoves = new Vector3[]
            {
                botPos + strafeDir * 2f,                // First strafe
                botPos + (strafeDir + toEnemy).normalized * 2f, // Forward diagonal
                botPos - toEnemy * 2f,                 // Backward
                botPos + (-strafeDir - toEnemy).normalized * 2f, // Backward diagonal
                botPos - strafeDir * 2f                 // Opposite side
            };

            // Validate & queue up valid positions
            foreach (Vector3 movePos in arcMoves)
            {
                if (NavMesh.SamplePosition(movePos, out NavMeshHit navMeshHit, 2f, -1))
                {
                    Vector3 newFirePos = navMeshHit.position;
                    ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(false);

                    if (Utils.Utils.CanShootToTarget(shootPointClass, newFirePos, botOwner_0.LookSensor.Mask, false))
                    {
                        movementQueue.Enqueue(newFirePos);
                    }
                }
            }
        }


        public override void UpdateNodeByBrain(StandardBrain data)
        {

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if (goalEnemy == null)
            {
                botOwner_0.Mover.Stop();
                botOwner_0.LookData.SetLookPointByHearing(null);
                botOwner_0.SetPose(1f);
                return;
            }

            botOwner_0.Sprint(false, true);
            botOwner_0.Mover.SetTargetMoveSpeed(1f);

            bool tense = false;

            Utils.Enemy.EnemyDistance enemyDistance = Utils.Enemy.Distance(botOwner_0);
            bool isVeryClose = enemyDistance <= Utils.Enemy.EnemyDistance.VeryClose;

            if (goalEnemy.IsVisible && isVeryClose)
            {
                botOwner_0.SetPose(0.8f);
                tense = true;
            }

            if (goalEnemy.CanShoot && goalEnemy.IsVisible)
            {

                if (movementCheckTime < Time.time && isVeryClose)
                {
                    FightMovementClose();
                }

                if (!isMoving)
                {
                    botOwner_0.DogFight.Fight();
                }

                botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());
                gclass171_0.UpdateNodeByBrain(data as GClass27);

                return;
            }
            else if (!tense)
            {
                botOwner_0.SetPose(1f);
            }


            botOwner_0.DogFight.Fight();

            botOwner_0.LookData.SetLookPointByHearing(null);
        }
    }

    /**
     * Overwrite of dogFight decision to fix bot's aiming direction
     */
    public class AltDogFight : GClass196
    {
        public AltDogFight(BotOwner bot) : base(bot)
        {

        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            botOwner_0.Mover.SetTargetMoveSpeed(1f);
            botOwner_0.DogFight.Fight();

            bool tense = false;

            if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.Distance < 15f)
            {
                botOwner_0.SetPose(0.7f);
                tense = true;
            }

            if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());
                gclass171_0.UpdateNodeByBrain(data as GClass27);
                return;
            }
            else if (!tense)
            {
                botOwner_0.SetPose(1f);
            }

            botOwner_0.LookData.SetLookPointByHearing(null);
        }
    }

}
