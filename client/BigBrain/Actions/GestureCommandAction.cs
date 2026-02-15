using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal class GestureCommandAction : CustomLogic
    {
        private BotFollowerPlayer? followerData;
        private float nextPathCheckAt;
        private bool moveCommandInitialized;
        private float nextHoldLookChangeAt;
        private Vector3 holdLookPoint;
        private float moveArrivalLookUntil;
        private float comeArrivalHoldUntil;
        private Vector3 activeMoveTarget;

        public GestureCommandAction(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            nextPathCheckAt = 0f;
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                return;
            }

            switch (command)
            {
                case FollowerCommandType.HoldPosition:
                    HandleHoldPosition();
                    break;

                case FollowerCommandType.ComeCloser:
                    HandleComeCloser();
                    break;

                case FollowerCommandType.MoveToPoint:
                    HandleMoveToPoint(target);
                    break;
            }
        }

        private void HandleComeCloser()
        {
            // Keep current pose for "come here"; only force out of prone.
            if (BotOwner.Mover.TargetPose < 0.1f)
            {
                BotOwner.Mover.SetPose(0.1f);
            }
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand();
                return;
            }

            Vector3 leaderPos = boss.realPlayer.Transform.position;
            float distance = (leaderPos - BotOwner.Position).magnitude;
            if (distance > 1f && comeArrivalHoldUntil > 0f)
            {
                comeArrivalHoldUntil = 0f;
            }
            if (distance <= 1f)
            {
                if (followerData?.IsComeCloserFromHold() == true)
                {
                    followerData.CompleteComeCloser();
                    BotOwner.StopMove();
                    return;
                }

                HandleComeArrivalPause();
                if (Time.time < comeArrivalHoldUntil)
                {
                    return;
                }
                comeArrivalHoldUntil = 0f;
                followerData?.CompleteComeCloser();
                BotOwner.StopMove();
                return;
            }

            BotOwner.GoToSomePointData.SetPoint(leaderPos);
            BotOwner.GoToSomePointData.UpdateToGo(distance > 16f);
            BotOwner.Steering.LookToPathDestPoint();
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleMoveToPoint(Vector3 target)
        {
            if(BotOwner.Mover.TargetPose != 1f) BotOwner.Mover.SetPose(1f);
            if (BotOwner.Memory.HaveEnemy)
            {
                followerData?.ClearCommand();
                BotOwner.StopMove();
                return;
            }

            float distance = (target - BotOwner.Position).magnitude;
            if (distance > 1.5f && moveArrivalLookUntil > 0f)
            {
                moveArrivalLookUntil = 0f;
            }
            if (distance <= 1.5f)
            {
                HandleMovePointArrivalLookAround();
                if (Time.time < moveArrivalLookUntil)
                {
                    return;
                }
                moveArrivalLookUntil = 0f;
                BotOwner.StopMove();
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                moveCommandInitialized = false;
                followerData?.ClearCommand();
                return;
            }

            bool targetChanged = !moveCommandInitialized || (activeMoveTarget - target).sqrMagnitude > 0.25f;
            if (targetChanged)
            {
                BotOwner.GoToSomePointData.SetPoint(target);
                moveCommandInitialized = true;
                activeMoveTarget = target;
                moveArrivalLookUntil = 0f;
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    followerData?.ClearCommand();
                    BotOwner.StopMove();
                    return;
                }
            }

            // "There" should always be a walk move.
            BotOwner.GoToSomePointData.UpdateToGo(false);
            BotOwner.Steering.LookToPathDestPoint();
            nextHoldLookChangeAt = 0f;
        }

        private void HandleMovePointArrivalLookAround()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            if (moveArrivalLookUntil <= 0f)
            {
                moveArrivalLookUntil = Time.time + Utils.Utils.Random(2f, 4f);
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.8f, 2f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private void HandleHoldPosition()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.TargetPose > 0.15f || BotOwner.Mover.TargetPose < 0.05f)
            {
                BotOwner.Mover.SetPose(0.1f);
            }
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time  + Utils.Utils.Random(2f, 6f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }

            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleComeArrivalPause()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (comeArrivalHoldUntil <= 0f)
            {
                comeArrivalHoldUntil = Time.time + Utils.Utils.Random(1.25f, 2.5f);
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.6f, 1.5f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private Vector3 PickNextHoldLookPoint()
        {
            Vector3 baseForward = BotOwner.LookDirection;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
            }

            float yawOffset = UnityEngine.Random.Range(-130f, 130f);
            Vector3 lookDir = Quaternion.Euler(0f, yawOffset, 0f) * baseForward.normalized;
            float lookDistance = UnityEngine.Random.Range(8f, 20f);
            Vector3 lookPoint = BotOwner.Position + lookDir * lookDistance;
            lookPoint.y += 1.5f;
            return lookPoint;
        }
    }
}
