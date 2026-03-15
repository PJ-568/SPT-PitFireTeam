using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class FollowerCombatActionData : CustomLayer.ActionData
    {
        public GClass26? Data { get; }

        public FollowerCombatActionData(GClass26? data)
        {
            Data = data;
        }
    }

    internal abstract class FollowerCombatActionBase : CustomLogic
    {
        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
        }

        protected static GClass26? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : GClass26
        {
            return GetRawData(data) as TData;
        }
    }

    internal sealed class CombatHoldPositionAction : FollowerCombatActionBase
    {
        private readonly GClass278 baseLogic;

        public CombatHoldPositionAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass278(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }

    internal sealed class CombatRunToCoverAction : FollowerCombatActionBase
    {
        private readonly GClass228 baseLogic;

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass228(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass31>(data));
        }
    }

    internal sealed class CombatAttackMovingAction : FollowerCombatActionBase
    {
        private readonly GClass205 baseLogic;

        public CombatAttackMovingAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass205(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }

    internal sealed class CombatAttackMovingFlankAction : FollowerCombatActionBase
    {
        private readonly GClass209 baseLogic;

        public CombatAttackMovingFlankAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass209(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass29>(data));
        }
    }

    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private readonly GClass276 baseLogic;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }

    internal sealed class CombatShootFromCoverAction : FollowerCombatActionBase
    {
        private readonly GClass277 baseLogic;

        public CombatShootFromCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass277(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }

    internal sealed class CombatGoToEnemyAction : FollowerCombatActionBase
    {
        private readonly GClass223 baseLogic;

        public CombatGoToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass223(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }

    internal sealed class CombatRunToEnemyAction : FollowerCombatActionBase
    {
        private readonly GClass227 baseLogic;

        public CombatRunToEnemyAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass227(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }

    internal sealed class CombatGoToPointAction : FollowerCombatActionBase
    {
        private readonly GClass219 baseLogic;

        public CombatGoToPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass219(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass30>(data));
        }
    }

    internal sealed class CombatDogFightAction : FollowerCombatActionBase
    {
        private readonly GClass183 shootLogic;
        private readonly GClass274 grenadeLogic;
        private readonly Queue<Vector3> movementQueue = new Queue<Vector3>();

        private bool isMoving;
        private float nextMovementCheckTime;

        public CombatDogFightAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new GClass183(botOwner);
            grenadeLogic = new GClass274(botOwner);
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

            bool isVeryClose = global::friendlySAIN.Utils.Enemy.Distance(BotOwner) <= global::friendlySAIN.Utils.Enemy.EnemyDistance.VeryClose;
            bool tense = goalEnemy.IsVisible && isVeryClose;
            if (tense)
            {
                BotOwner.SetPose(0.8f);
            }

            if (goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                if (isVeryClose && nextMovementCheckTime < Time.time)
                {
                    FightMovementClose(goalEnemy);
                }

                if (!isMoving)
                {
                    BotOwner.DogFight.Fight();
                }

                if (!BotOwner.WeaponManager.IsMelee && !grenadeLogic.UpdateTryThrow())
                {
                    BotOwner.Steering.LookToPoint(goalEnemy.CurrPosition);
                    shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
                }

                return;
            }

            if (!tense)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner.DogFight.Fight();
            BotOwner.LookData.SetLookPointByHearing(null);
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

            Vector3[] arcMoves = new[]
            {
                botPos + strafeDir * 2f,
                botPos + (strafeDir + toEnemy).normalized * 2f,
                botPos - toEnemy * 2f,
                botPos + (-strafeDir - toEnemy).normalized * 2f,
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

    internal sealed class CombatShootToSmokeAction : FollowerCombatActionBase
    {
        private readonly GClass185 baseLogic;

        public CombatShootToSmokeAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass185(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }
    }
}
