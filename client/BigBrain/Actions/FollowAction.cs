using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal class FollowAction : CustomLogic
    {
        private const int DefaultFollowDistance = 12;

        private Player? bossPlayer;
        private pitAIBossPlayer? bossData;
        private BotFollowerPlayer? followerData;

        private float nextFollowUpdateAt = 0f;
        private float nextSettlePointAt = 0f;
        private Vector3 lastMoveTarget;
        private bool isPathBlocked = false;
        private bool wasInFollowRange = false;
        private float holdPositionUntil = 0f;
        private float nextPatrolUpdateAt = 0f;
        private bool movingToPatrolPoint;
        private bool movingToSettlePoint = false;

        private bool patrolEnabled = false;

        private Vector3? lastLeaderPatrolGridPos = null;
        private Vector3? lastLeaderCampGridPos = null;

        private float resumeFollowUntil = 0f;
        private bool isPatrolCampInitialized = false;

        private CustomNavigationPoint? lastCoverPoint;
        private bool noCoverFound = false;

        private float patrolPerimeterRadius;

        private bool playPeacefulActions = false;
        private bool playPeaceLook = false;
        private bool playPeaceHardAim = false;
        private bool playSecondWeaponWatch = false;

        private bool? lastSprintState = null;
        private Vector3 chaseTarget;
        private bool hasChaseTarget;
        private float nextChaseRepathAt;

        public FollowAction(BotOwner botOwner) : base(botOwner)
        {
            lastMoveTarget = botOwner.Position;
            patrolPerimeterRadius = friendlySAIN.patrolRadius.Value;
        }

        public override void Start()
        {
            base.Start();
            isPathBlocked = false;
            wasInFollowRange = false;
            isPatrolCampInitialized = false;
            movingToPatrolPoint = false;
            movingToSettlePoint = false;
            nextFollowUpdateAt = 0f;
            nextSettlePointAt = 0f;
            resumeFollowUntil = 0f;
            holdPositionUntil = 0f;
            nextPatrolUpdateAt = 0f;
            lastSprintState = null;
            hasChaseTarget = false;
            nextChaseRepathAt = 0f;
            ResetPeaceActions();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            SetPatrolEnabled(followerData?.CanPatrol == true);

            if (!TryGetBossAndPlayer())
            {
                BotOwner.StopMove();
                return;
            }

            try
            {
                if (!patrolEnabled)
                {
                    Follow();
                }
                else
                {
                    Patrol();
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
                BotOwner.StopMove();
            }
        }

        private bool TryGetBossAndPlayer()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }
            bossData = boss;
            bossPlayer = boss.realPlayer;
            return true;
        }

        private Vector3 GetLeaderTargetPosition()
        {
            if (bossPlayer == null) return BotOwner.Position;
            return bossPlayer.Transform.position;
        }

        private void Follow(bool forceFollow = false, float forcedDistance = 0f)
        {
            if (bossPlayer == null) return;

            if (movingToSettlePoint)
            {
                BotOwner.GoToSomePointData.UpdateToGo(false);
            }
            else if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            if (nextFollowUpdateAt >= Time.time) return;
            nextFollowUpdateAt = Time.time + Utils.Utils.Random(1f, 2f);

            Vector3 leaderPosition = GetLeaderTargetPosition();
            int followDistance = DefaultFollowDistance;

            float distanceToLeader;
            if (forceFollow)
            {
                distanceToLeader = forcedDistance;
            }
            else
            {
                distanceToLeader = Mathf.Abs((isPathBlocked ? lastMoveTarget : (leaderPosition - BotOwner.Position)).magnitude);
            }

            bool inRange = distanceToLeader < followDistance;
            bool rangeChanged = inRange != wasInFollowRange;
            wasInFollowRange = inRange;

            if (inRange)
            {
                if (isPathBlocked)
                {
                    BotOwner.StopMove();
                    hasChaseTarget = false;
                    return;
                }

                if (movingToSettlePoint)
                {
                    if (BotOwner.GoToSomePointData.IsCome())
                    {
                        movingToSettlePoint = false;
                    }
                    return;
                }

                if (nextSettlePointAt < Time.time || rangeChanged)
                {
                    nextSettlePointAt = Time.time + 8f;
                    int settleDistance = followDistance;

                    CustomNavigationPoint? settlePoint = null;
                    if (lastCoverPoint == null && !noCoverFound)
                    {
                        List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(bossPlayer.Transform.position, settleDistance + 5f);

                        float maxDist = settleDistance;
                        NavMeshPath navMeshPath = new NavMeshPath();
                        List<CustomNavigationPoint> availableCover = new List<CustomNavigationPoint>();

                        foreach (var point in coverPoints)
                        {
                            if (point == null) continue;
                            if (!point.IsFreeById(BotOwner.Id)) continue;
                            if (Utils.Utils.GetNavDistance(leaderPosition, point.Position, navMeshPath) <= maxDist)
                            {
                                availableCover.Add(point);
                            }
                        }

                        if (availableCover.Count > 0)
                        {
                            settlePoint = availableCover[UnityEngine.Random.Range(0, availableCover.Count)];
                        }
                    }
                    else
                    {
                        settlePoint = lastCoverPoint;
                    }

                    if (settlePoint != null)
                    {
                        lastCoverPoint = settlePoint;
                        BotOwner.Memory.SetCoverPoints(settlePoint);
                        BotOwner.GoToSomePointData.SetPoint(settlePoint.Position);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        BotOwner.Steering.LookToPathDestPoint();

                        movingToSettlePoint = true;
                        nextFollowUpdateAt = Time.time + 0.5f;
                        return;
                    }

                    noCoverFound = true;
                    float minOffset = Mathf.Min(1f, settleDistance * 0.19f);
                    float maxOffset = Mathf.Min(5f, settleDistance * 0.65f);
                    float xOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
                    float zOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
                    float targetX = xOffset + leaderPosition.x;
                    float targetZ = zOffset + leaderPosition.z;

                    if (!NavMesh.SamplePosition(new Vector3(targetX, leaderPosition.y, targetZ), out NavMeshHit navMeshHit, 2f, -1))
                    {
                        BotOwner.StopMove();
                        isPathBlocked = true;
                        return;
                    }

                    BotOwner.GoToSomePointData.SetPoint(navMeshHit.position);
                    BotOwner.GoToSomePointData.UpdateToGo(false);
                    BotOwner.Steering.LookToPathDestPoint();
                    movingToSettlePoint = true;
                    nextFollowUpdateAt = Time.time + 0.5f;
                }

                return;
            }

            movingToSettlePoint = false;
            lastCoverPoint = null;
            noCoverFound = false;
            if (ShouldRefreshChase(leaderPosition))
            {
                MoveTowardLeader(leaderPosition);
                chaseTarget = leaderPosition;
                hasChaseTarget = true;
                nextChaseRepathAt = Time.time + 0.6f;
            }
            bool mustSprint = distanceToLeader > Math.Min(followDistance + 3, 16);

            if (BotOwner.Mover.TargetPose != 1f) BotOwner.Mover.SetPose(1f);
            SetSprint(mustSprint);
        }

        private bool ShouldRefreshChase(Vector3 leaderPosition)
        {
            if (!hasChaseTarget) return true;
            if (Time.time >= nextChaseRepathAt) return true;
            return (leaderPosition - chaseTarget).sqrMagnitude > 9f;
        }

        private void Patrol()
        {
            if (bossPlayer == null || bossData == null) return;

            if (resumeFollowUntil > Time.time)
            {
                Follow();
            }

            movingToSettlePoint = false;

            Vector3 leaderPosition = bossPlayer.Transform.position;
            Vector3 botPosition = BotOwner.GetPlayer.Transform.position;
            int followDistance = DefaultFollowDistance;

            if (!isPatrolCampInitialized)
            {
                Vector3 leaderGridPosition = new Vector3(
                    Mathf.Floor(leaderPosition.x / 3f) * 3f,
                    Mathf.Floor(leaderPosition.y / 3f) * 3f,
                    Mathf.Floor(leaderPosition.z / 3f) * 3f
                );

                float distanceToLeader = Mathf.Abs((isPathBlocked ? lastMoveTarget : (leaderGridPosition - botPosition)).magnitude);
                bool inRange = distanceToLeader < followDistance;

                if (!inRange)
                {
                    Follow(true, distanceToLeader);
                    resumeFollowUntil = Time.time + 5f;
                    return;
                }

                if (lastLeaderPatrolGridPos.HasValue && lastLeaderPatrolGridPos.Value != leaderGridPosition)
                {
                    lastLeaderPatrolGridPos = leaderGridPosition;
                    Follow(true, distanceToLeader);
                    resumeFollowUntil = Time.time + 5f;
                    return;
                }

                lastLeaderPatrolGridPos = leaderGridPosition;
                isPatrolCampInitialized = true;
            }

            if (!isPatrolCampInitialized) return;
            if (nextPatrolUpdateAt > Time.time) return;

            nextPatrolUpdateAt = Time.time + 1.5f;
            float campRadius = 30f;

            Vector3 campCenter = new Vector3(
                Mathf.Floor(leaderPosition.x / campRadius) * campRadius,
                Mathf.Floor(leaderPosition.y / campRadius) * campRadius,
                Mathf.Floor(leaderPosition.z / campRadius) * campRadius
            );

            if (lastLeaderCampGridPos.HasValue && campCenter != lastLeaderCampGridPos.Value)
            {
                lastLeaderCampGridPos = null;
                isPatrolCampInitialized = false;
                resumeFollowUntil = Time.time + 5f;
                BotOwner.Mover.SetTargetMoveSpeed(1f);
                Follow();
                return;
            }
            lastLeaderCampGridPos = campCenter;

            if (holdPositionUntil > Time.time)
            {
                if (playPeacefulActions)
                {
                    BotOwner.PeacefulActions.UpdateAction();
                }
                else if (playPeaceLook)
                {
                    BotOwner.PeaceLook.ManualUpdate();
                }
                else if (playPeaceHardAim)
                {
                    BotOwner.PeaceHardAim.ManualUpdate();
                }
                else if (playSecondWeaponWatch)
                {
                    BotOwner.SecondWeaponData.ManualUpdate();
                }

                movingToPatrolPoint = false;
                return;
            }

            if (movingToPatrolPoint)
            {
                if (BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false))
                {
                    BotOwner.StopMove();
                    BotOwner.LookData.SetLookPointByHearing(null);
                    holdPositionUntil = Time.time + Utils.Utils.Random(6f, 10f);
                }
                else
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
                return;
            }

            List<Vector3> keepClearPositions = new List<Vector3> { leaderPosition };
            foreach (BotOwner follower in bossData.Followers)
            {
                keepClearPositions.Add(follower.GetPlayer.Transform.position);
            }
            Vector3[] finalKeepClearPositions = keepClearPositions.ToArray();

            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPosition = campCenter + UnityEngine.Random.insideUnitSphere * patrolPerimeterRadius;
                if (!NavMesh.SamplePosition(randomPosition, out NavMeshHit navMeshHit, 10f, -1)) continue;
                if (!Utils.Utils.IsDangerPositionFarEnough(navMeshHit.position, finalKeepClearPositions, 2f * 2f)) continue;

                if (BotOwner.GoToPoint(navMeshHit.position, true, -1f, false, false) != NavMeshPathStatus.PathComplete) continue;

                SetSprint(false);
                BotOwner.Mover.SetTargetMoveSpeed(0.5f);
                BotOwner.Steering.LookToPoint(navMeshHit.position + Vector3.up * 1.5f);

                ResetPeaceActions();

                bool hasPeaceful = BotOwner.PeacefulActions.HaveActions();
                bool hasLook = BotOwner.PeaceLook.HaveActions();
                bool hasHardAim = BotOwner.PeaceHardAim.HaveActions();
                bool hasSecondWeaponWatch = BotOwner.SecondWeaponData.HaveActions();

                if (hasPeaceful && UnityEngine.Random.value > 0.5f) playPeacefulActions = true;
                if (!playPeacefulActions && hasLook && UnityEngine.Random.value > 0.5f) playPeaceLook = true;
                if (!playPeacefulActions && !playPeaceLook && hasHardAim && UnityEngine.Random.value > 0.5f) playPeaceHardAim = true;
                if (!playPeacefulActions && !playPeaceLook && !playPeaceHardAim && hasSecondWeaponWatch && UnityEngine.Random.value > 0.5f)
                {
                    playSecondWeaponWatch = true;
                }

                movingToPatrolPoint = true;
                return;
            }
        }

        private void MoveTowardLeader(Vector3 leaderPosition)
        {
            isPathBlocked = false;

            if (TryGoToPoint(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                isPathBlocked = false;
            }
            else if (NavMesh.SamplePosition(leaderPosition, out NavMeshHit navMeshHit, 1.5f, -1))
            {
                if (TryGoToPoint(navMeshHit.position) != NavMeshPathStatus.PathComplete)
                {
                    isPathBlocked = true;
                }
            }

            if (isPathBlocked)
            {
                CustomNavigationPoint freeClosePoint = BotOwner.Covers.GetFreeClosePoint(leaderPosition, 0f, false);
                if (freeClosePoint != null)
                {
                    isPathBlocked = true;
                    TryGoToPoint(freeClosePoint.Position);
                }
            }
        }

        private NavMeshPathStatus TryGoToPoint(Vector3 position)
        {
            NavMeshPathStatus pathStatus = BotOwner.Mover.GoToPoint(position, false, 0.5f, false, false);
            if (pathStatus == NavMeshPathStatus.PathComplete)
            {
                if (lastMoveTarget != position) BotOwner.Steering.LookToMovingDirection();
                lastMoveTarget = position;
            }
            return pathStatus;
        }

        private void SetSprint(bool state)
        {
            if (lastSprintState.HasValue && lastSprintState.Value == state) return;
            BotOwner.Mover.Sprint(state, false);
            lastSprintState = state;
        }

        private void SetPatrolEnabled(bool state = false)
        {
            patrolEnabled = state;

            if (!state)
            {
                movingToPatrolPoint = false;
                holdPositionUntil = 0f;
                resumeFollowUntil = 0f;
                isPatrolCampInitialized = false;
                nextPatrolUpdateAt = 0f;
            }
        }

        private void ResetPeaceActions()
        {
            playPeacefulActions = false;
            playPeaceLook = false;
            playPeaceHardAim = false;
            playSecondWeaponWatch = false;
        }
    }
}
