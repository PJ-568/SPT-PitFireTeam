using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System.Collections.Generic;
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
        private bool regroupReservationActive;
        private bool regroupBossAnchorInitialized;
        private Vector3 regroupBossAnchorPosition;
        private float nextRegroupBossAnchorCheckAt;
        private FollowerCommandType lastCommand = FollowerCommandType.None;
        private const float RegroupArriveNavDistance = 6f;
        private const float RegroupRunDistance = 10f;
        private const float SameLevelTolerance = 1.75f;
        private const float RegroupCoverSearchRadius = 15f;
        private const float RegroupRandomRadius = 6f;
        private const float RegroupReservationSpacing = 1.5f;
        private const float RegroupReservationTtl = 2f;
        private static readonly Dictionary<string, RegroupReservation> ActiveRegroupReservations = new Dictionary<string, RegroupReservation>();

        private struct RegroupReservation
        {
            public Vector3 Point;
            public float ExpiresAt;
        }

        public GestureCommandAction(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            ReleaseRegroupReservation();
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
            regroupReservationActive = false;
            regroupBossAnchorInitialized = false;
            regroupBossAnchorPosition = Vector3.zero;
            nextRegroupBossAnchorCheckAt = 0f;
            lastCommand = FollowerCommandType.None;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                ReleaseRegroupReservation();
                lastCommand = FollowerCommandType.None;
                return;
            }

            EnsureCommandControl();

            if (command != lastCommand)
            {
                if (lastCommand == FollowerCommandType.RegroupNearBoss)
                {
                    ReleaseRegroupReservation();
                }

                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                regroupTargetInitialized = false;
                regroupTarget = Vector3.zero;
                nextRegroupRefreshAt = 0f;
                regroupReportedOnPosition = false;
                regroupBossAnchorInitialized = false;
                regroupBossAnchorPosition = Vector3.zero;
                nextRegroupBossAnchorCheckAt = 0f;
                
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
                if(
                    command == FollowerCommandType.MoveToPoint || 
                    command == FollowerCommandType.ComeCloser
                )
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
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
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:missingBoss");
                return;
            }
            // intrerupt on enemy enagage
            if (ShouldInterruptRegroupForThreatOrState(clearForDanger: true))
            {
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:interrupt");
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
            if (!regroupBossAnchorInitialized)
            {
                regroupBossAnchorInitialized = true;
                regroupBossAnchorPosition = bossPos;
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
            }

            if (Time.time >= nextRegroupBossAnchorCheckAt)
            {
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
                if ((bossPos - regroupBossAnchorPosition).sqrMagnitude > 10f * 10f)
                {
                    regroupBossAnchorPosition = bossPos;
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                }
            }

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
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:arrived");
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
                UpsertRegroupReservation(regroupTarget);
                BotOwner.GoToSomePointData.SetPoint(regroupTarget);
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, regroupTarget, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                    return;
                }
            }

            // Regroup should be an urgent converge command: run/sprint while closing.
            float regroupDistance = (regroupTarget - BotOwner.Position).magnitude;
            bool shouldRun = regroupDistance > RegroupRunDistance;
            BotOwner.GoToSomePointData.UpdateToGo(shouldRun, 1, 1f);
            if (shouldRun)
            {
                if (!BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(true, false);
                }
            }
            else if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            nextHoldLookChangeAt = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private bool ShouldInterruptRegroupForThreatOrState(bool clearForDanger)
        {
            if (BotOwner.Memory?.HaveEnemy == true && BotOwner.Memory.GoalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return true;
            }

            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing)
            {
                return true;
            }

            bool dangerNow = currentDecision == BotLogicDecision.runAwayGrenade ||
                             currentDecision == BotLogicDecision.runAwayBTR ||
                             BotOwner.BewareGrenade?.ShallRunAway() == true ||
                             BotOwner.BewareBTR?.ShallRunAway() == true;
                             
            if (dangerNow && clearForDanger)
            {
                return true;
            }

            return false;
        }

        private bool TryGetRegroupTarget(Vector3 bossPos, out Vector3 target)
        {
            CleanupRegroupReservations();
            target = Vector3.zero;
            float bestDistance = float.MaxValue;
            List<CustomNavigationPoint> coverPoints = Covers.GetCoverPoints(
                BotOwner,
                bossPos,
                RegroupCoverSearchRadius,
                point => Mathf.Abs(point.Position.y - bossPos.y) <= SameLevelTolerance && !IsRegroupTargetCrowded(point.Position)
            );

            foreach (CustomNavigationPoint point in coverPoints)
            {
                if (point == null) continue;
                NavMeshPath coverPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, point.Position, NavMesh.AllAreas, coverPath) || coverPath.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathDistance = coverPath.CalculatePathLength();
                if (pathDistance < bestDistance)
                {
                    bestDistance = pathDistance;
                    target = point.Position;
                }
            }

            if (target == Vector3.zero)
            {
                for (int i = 0; i < 12; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float radius = Random.Range(1f, RegroupRandomRadius);
                    Vector3 candidate = bossPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    {
                        continue;
                    }
                    if (Mathf.Abs(navHit.position.y - bossPos.y) > SameLevelTolerance)
                    {
                        continue;
                    }
                    if (IsRegroupTargetCrowded(navHit.position))
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

            return target != Vector3.zero;
        }

        private bool IsRegroupTargetCrowded(Vector3 candidate)
        {
            float spacingSqr = RegroupReservationSpacing * RegroupReservationSpacing;
            if (BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss)
            {
                foreach (BotOwner follower in boss.Followers)
                {
                    if (follower == null || follower == BotOwner || follower.IsDead || follower.BotState != EBotState.Active) continue;
                    if ((follower.Position - candidate).sqrMagnitude < spacingSqr)
                    {
                        return true;
                    }
                }
            }

            string myProfileId = BotOwner.ProfileId;
            foreach (KeyValuePair<string, RegroupReservation> entry in ActiveRegroupReservations)
            {
                if (entry.Key == myProfileId) continue;
                if (entry.Value.ExpiresAt < Time.time) continue;
                if ((entry.Value.Point - candidate).sqrMagnitude < spacingSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpsertRegroupReservation(Vector3 target)
        {
            if (string.IsNullOrEmpty(BotOwner.ProfileId)) return;
            ActiveRegroupReservations[BotOwner.ProfileId] = new RegroupReservation
            {
                Point = target,
                ExpiresAt = Time.time + RegroupReservationTtl
            };
            regroupReservationActive = true;
        }

        private void ReleaseRegroupReservation()
        {
            if (string.IsNullOrEmpty(BotOwner.ProfileId))
            {
                regroupReservationActive = false;
                return;
            }

            ActiveRegroupReservations.Remove(BotOwner.ProfileId);
            regroupReservationActive = false;
        }

        private static void CleanupRegroupReservations()
        {
            if (ActiveRegroupReservations.Count == 0) return;
            List<string> expired = null;
            foreach (KeyValuePair<string, RegroupReservation> entry in ActiveRegroupReservations)
            {
                if (entry.Value.ExpiresAt >= Time.time) continue;
                expired ??= new List<string>();
                expired.Add(entry.Key);
            }

            if (expired == null) return;
            foreach (string id in expired)
            {
                ActiveRegroupReservations.Remove(id);
            }
        }

        private void HandleComeCloser()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand("ComeCloser:missingBoss");
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
                followerData?.ClearCommand("MoveToPoint:enemySeen");
                BotOwner.StopMove();
                return;
            }
            if (HasVisibleKnownEnemy())
            {
                followerData?.ClearCommand("MoveToPoint:enemyVisibleNoGoal");
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
                followerData?.ClearCommand("MoveToPoint:arrived");
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
                    followerData?.ClearCommand("MoveToPoint:pathInvalid");
                    BotOwner.StopMove();
                    return;
                }
            }

            // "There" should always be a walk move.
            BotOwner.GoToSomePointData.UpdateToGo(false);

            if (followerData?.TryGetCommandLookOverride(out Vector3 lookOverridePoint) == true)
            {
                BotOwner.Steering.LookToPoint(lookOverridePoint);
            }
            else
            {
                BotOwner.Steering.LookToPathDestPoint();
            }

            nextHoldLookChangeAt = 0f;
        }

        private bool HasVisibleKnownEnemy()
        {
            try
            {
                var infos = BotOwner?.EnemiesController?.EnemyInfos;
                if (infos == null || infos.Count == 0) return false;

                foreach (var kv in infos)
                {
                    var info = kv.Value;
                    if (info == null) continue;
                    if (info.IsVisible) return true;
                }
            }
            catch
            {
                // Ignore and preserve command behavior on rare enemy-info enumeration issues.
            }
            return false;
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

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);
                
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
            if (BotOwner.Memory.HaveEnemy)
            {
                followerData?.ClearCommand("HoldPosition:enemySeen");
                BotOwner.StopMove();
                return;
            }
            if (HasVisibleKnownEnemy())
            {
                followerData?.ClearCommand("HoldPosition:enemyVisibleNoGoal");
                BotOwner.StopMove();
                return;
            }
            
            BotOwner.StopMove();
            if (BotOwner.Mover.TargetPose > 0.15f || BotOwner.Mover.TargetPose < 0.05f)
            {
                BotOwner.Mover.SetPose(0.1f);
            }
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

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

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);
                
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
