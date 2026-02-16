using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
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
        private bool comeTargetInitialized;
        private Vector3 comeTarget;
        private bool comePoseInitialized;
        private float comeMovePose = 1f;
        private bool regroupTargetInitialized;
        private Vector3 regroupTarget;
        private float nextRegroupRefreshAt;
        private bool regroupReportedOnPosition;
        private FollowerCommandType lastCommand = FollowerCommandType.None;
        private const float RegroupArriveNavDistance = 8f;
        private const float SameLevelTolerance = 1.75f;

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
            comeTargetInitialized = false;
            comeTarget = Vector3.zero;
            comePoseInitialized = false;
            comeMovePose = 1f;
            regroupTargetInitialized = false;
            regroupTarget = Vector3.zero;
            nextRegroupRefreshAt = 0f;
            regroupReportedOnPosition = false;
            lastCommand = FollowerCommandType.None;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                lastCommand = FollowerCommandType.None;
                return;
            }

            EnsureCommandControl();

            if (command != lastCommand)
            {
                if (command == FollowerCommandType.ComeCloser)
                {
                    comeTargetInitialized = false;
                    comeTarget = Vector3.zero;
                    comePoseInitialized = false;
                    comeMovePose = 1f;
                    regroupTargetInitialized = false;
                    regroupTarget = Vector3.zero;
                    nextRegroupRefreshAt = 0f;
                    regroupReportedOnPosition = false;
                }
                else
                {
                    comeTargetInitialized = false;
                    comeTarget = Vector3.zero;
                    comePoseInitialized = false;
                    comeMovePose = 1f;
                    regroupTargetInitialized = false;
                    regroupTarget = Vector3.zero;
                    nextRegroupRefreshAt = 0f;
                    regroupReportedOnPosition = false;
                }
                
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

                case FollowerCommandType.RegroupNearBoss:
                    HandleRegroupNearBoss();
                    break;
            }

            if(command != lastCommand)
            {
                lastCommand = command;
                if(command == FollowerCommandType.MoveToPoint || command == FollowerCommandType.ComeCloser)
                    BotOwner.Steering.LookToMovingDirection();
            }
        }

        private void EnsureCommandControl()
        {
            if (BotOwner?.Mover != null)
            {
                if (BotOwner.Mover.Pause)
                {
                    BotOwner.Mover.Pause = false;
                }
            }

            if (BotOwner?.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }
        }

        private void HandleRegroupNearBoss()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand();
                return;
            }

            if (BotOwner.Memory?.HaveEnemy == true && BotOwner.Memory.GoalEnemy?.IsVisible == true)
            {
                followerData?.ClearCommand();
                BotOwner.StopMove();
                return;
            }

            // Regroup is an urgent converge order: force move-capable state each tick.
            if (BotOwner.Mover.Pause)
            {
                BotOwner.Mover.Pause = false;
            }

            if (BotOwner.Mover.TargetPose < 0.85f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            Vector3 bossPos = boss.realPlayer.Position;
            float verticalDiff = Mathf.Abs(BotOwner.Position.y - bossPos.y);
            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPos);

            if (verticalDiff <= SameLevelTolerance && navDistanceToBoss <= RegroupArriveNavDistance)
            {
                BotOwner.StopMove();
                if (!regroupReportedOnPosition)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.OnPosition, false);
                    regroupReportedOnPosition = true;
                }
                followerData?.ClearCommand();
                return;
            }

            if (!regroupTargetInitialized || Time.time >= nextRegroupRefreshAt)
            {
                if (!TryGetRegroupTarget(bossPos, out regroupTarget))
                {
                    regroupTarget = bossPos;
                }
                regroupTargetInitialized = true;
                nextRegroupRefreshAt = Time.time + 0.8f;
                BotOwner.GoToSomePointData.SetPoint(regroupTarget);
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, regroupTarget, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    regroupTargetInitialized = false;
                    return;
                }
            }

            // Regroup should be an urgent converge command: run/sprint while closing.
            BotOwner.GoToSomePointData.UpdateToGo(true, 1, 1f);
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            nextHoldLookChangeAt = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private bool TryGetRegroupTarget(Vector3 bossPos, out Vector3 target)
        {
            target = Vector3.zero;
            float bestDistance = float.MaxValue;
            float[] radii = { 2.25f, 3.5f, 5f };

            for (int r = 0; r < radii.Length; r++)
            {
                float radius = radii[r];
                for (int i = 0; i < 6; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    Vector3 candidate = bossPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    {
                        continue;
                    }
                    if (Mathf.Abs(navHit.position.y - bossPos.y) > SameLevelTolerance)
                    {
                        continue;
                    }

                    NavMeshPath path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(BotOwner.Position, navHit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    {
                        continue;
                    }

                    float pathDistance = path.CalculatePathLength();
                    if (pathDistance < bestDistance)
                    {
                        bestDistance = pathDistance;
                        target = navHit.position;
                    }
                }
            }

            if (target == Vector3.zero && NavMesh.SamplePosition(bossPos, out NavMeshHit bossNavHit, 2.5f, NavMesh.AllAreas))
            {
                if (Mathf.Abs(bossNavHit.position.y - bossPos.y) <= SameLevelTolerance)
                {
                    target = bossNavHit.position;
                }
            }

            return target != Vector3.zero;
        }

        private void HandleComeCloser()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand();
                return;
            }

            if (!comeTargetInitialized)
            {
                comeTarget = boss.realPlayer.Transform.position;
                comeTargetInitialized = true;
            }
            if (!comePoseInitialized)
            {
                float bossPose = Mathf.Clamp01(boss.realPlayer.MovementContext?.PoseLevel ?? 1f);
                // Snapshot boss stance at command start.
                comeMovePose = bossPose < 0.75f ? 0.1f : 1f;
                comePoseInitialized = true;
            }

            float distance = (comeTarget - BotOwner.Position).magnitude;
            if (distance > 1.5f && comeArrivalHoldUntil > 0f)
            {
                comeArrivalHoldUntil = 0f;
            }
            if (distance <= 1.5f)
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
                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                followerData?.CompleteComeCloser();
                BotOwner.StopMove();
                return;
            }

            BotOwner.GoToSomePointData.SetPoint(comeTarget);
            BotOwner.GoToSomePointData.UpdateToGo(distance > 16f,1,comeMovePose);
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

            // Always start the arrival hold window first so command is not cleared immediately
            // when random look is temporarily paused (e.g. recent contact command).
            if (moveArrivalLookUntil <= 0f)
            {
                moveArrivalLookUntil = Time.time + Utils.Utils.Random(2f, 4f);
                nextHoldLookChangeAt = 0f;
            }

            if (followerData?.IsCommandLookRandomPaused() == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                return;
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

            if (followerData?.IsCommandLookRandomPaused() == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                moveCommandInitialized = false;
                moveArrivalLookUntil = 0f;
                comeArrivalHoldUntil = 0f;
                activeMoveTarget = Vector3.zero;
                return;
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

            if (followerData?.IsCommandLookRandomPaused() == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                return;
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
            // Keep hold/look-around horizontal so we don't accumulate upward pitch.
            baseForward.y = 0f;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
                baseForward.y = 0f;
            }

            float yawOffset = UnityEngine.Random.Range(-130f, 130f);
            Vector3 lookDir = Quaternion.Euler(0f, yawOffset, 0f) * baseForward.normalized;
            float lookDistance = UnityEngine.Random.Range(8f, 20f);
            Vector3 lookPoint = BotOwner.Position + lookDir * lookDistance;
            lookPoint.y = BotOwner.Position.y + 1.1f;
            return lookPoint;
        }
    }
}
