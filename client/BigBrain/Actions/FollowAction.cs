using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal class FollowAction : CustomLogic
    {
        private const int DefaultFollowDistance = 12;
        private const bool EnableFollowDebug = false;
        private const float MaxBossSpeedForSettle = 0.35f;
        private const float SettleSpacing = 2.5f;
        private const float SettleSpacingSqr = SettleSpacing * SettleSpacing;
        private const float StuckCheckInterval = 0.8f;
        private const float StuckMovementEpsilonSqr = 0.04f;
        private const float StuckDurationToRecover = 3.2f;
        private const float UnstuckCooldownSeconds = 2.5f;
        private const int EscalationTeleportStep = 2;

        private Player? bossPlayer;
        private pitAIBossPlayer? bossData;
        private BotFollowerPlayer? followerData;

        private float nextFollowUpdateAt;
        private float nextSettlePointAt;
        private Vector3? lastMoveTarget;
        private bool isPathBlocked;

        private float holdPositionUntil;
        private float nextPatrolUpdateAt;
        private bool movingToPatrolPoint;
        private bool movingToSettlePoint;
        private bool patrolEnabled;

        private Vector3? lastLeaderPatrolGridPos;
        private Vector3? lastLeaderCampGridPos;
        private float resumeFollowUntil;
        private bool isPatrolCampInitialized;

        private CustomNavigationPoint? lastCoverPoint;
        private bool noCoverFound;
        private float patrolPerimeterRadius;

        private bool playPeacefulActions;
        private bool playPeaceLook;
        private bool playPeaceHardAim;
        private bool playSecondWeaponWatch;

        private bool poseCorrected;

        private Vector3 lastStuckSamplePos;
        private float nextStuckCheckAt;
        private float stuckAccumulated;
        private int stuckRecoverAttempts;
        private float unstuckCooldownUntil;

        public FollowAction(BotOwner botOwner) : base(botOwner)
        {
            patrolPerimeterRadius = friendlySAIN.patrolRadius.Value;
        }

        public override void Start()
        {
            base.Start();
            isPathBlocked = false;
            isPatrolCampInitialized = false;
            movingToPatrolPoint = false;
            movingToSettlePoint = false;
            patrolEnabled = false;

            nextFollowUpdateAt = 0f;
            nextSettlePointAt = 0f;
            resumeFollowUntil = 0f;
            holdPositionUntil = 0f;
            nextPatrolUpdateAt = 0f;

            poseCorrected = false;

            lastLeaderPatrolGridPos = null;
            lastLeaderCampGridPos = null;
            lastCoverPoint = null;
            noCoverFound = false;

            ResetPeaceActions();

            lastStuckSamplePos = BotOwner.Position;
            nextStuckCheckAt = Time.time + StuckCheckInterval;
            stuckAccumulated = 0f;
            stuckRecoverAttempts = 0;
            unstuckCooldownUntil = 0f;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            //SetPatrolEnabled(followerData?.CanPatrol == true);

            if (!TryGetBossAndPlayer())
            {
                BotOwner.StopMove();
                return;
            }

            try
            {
                Follow();
                UpdateStuckWatchdog();
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
            else if (BotOwner.Mover.TargetPose != 1f && !poseCorrected)
            {
                poseCorrected = true;
                BotOwner.Mover.SetPose(1f);
            }

            if (nextFollowUpdateAt > Time.time && !forceFollow) return;
            nextFollowUpdateAt = Time.time + Utils.Utils.Random(1f, 2f);

            Vector3 leaderPosition = GetLeaderTargetPosition();

            float distance = forceFollow
                ? forcedDistance
                : Mathf.Abs((isPathBlocked && lastMoveTarget.HasValue ? (lastMoveTarget.Value - BotOwner.Position) : (leaderPosition - BotOwner.Position)).magnitude);

            bool inRange = distance < DefaultFollowDistance;

            if (inRange)
            {
                if (isPathBlocked)
                {
                    BotOwner.StopMove();
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

                if (nextSettlePointAt > Time.time)
                {
                    return;
                }

                nextSettlePointAt = Time.time + 8f;
                int settleDistance = DefaultFollowDistance;

                CustomNavigationPoint? coverPoint = TryGetSettleCoverPoint(leaderPosition, settleDistance);
                if (coverPoint != null)
                {
                    lastCoverPoint = coverPoint;
                    BotOwner.Memory.SetCoverPoints(coverPoint);
                    BotOwner.GoToSomePointData.SetPoint(coverPoint.Position);
                    BotOwner.GoToSomePointData.UpdateToGo(false);
                    BotOwner.Steering.LookToPathDestPoint();
                    movingToSettlePoint = true;
                    nextFollowUpdateAt = Time.time + 0.5f;
                    return;
                }

                noCoverFound = true;
                if (TryGetRandomSettlePoint(leaderPosition, settleDistance, out Vector3 settlePosition))
                {
                    BotOwner.GoToSomePointData.SetPoint(settlePosition);
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

            UpdateFollowPath(leaderPosition);

            bool shouldSprint = distance > Mathf.Min(DefaultFollowDistance + 3f, 16f);
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }
            BotOwner.Mover.Sprint(shouldSprint, false);

        }

        private CustomNavigationPoint? TryGetSettleCoverPoint(Vector3 leaderPosition, int settleDistance)
        {
            if (lastCoverPoint != null || noCoverFound)
            {
                return lastCoverPoint;
            }

            if (bossPlayer == null) return null;

            List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(bossPlayer.Transform.position, settleDistance + 5f);
            float maxDistance = settleDistance;
            NavMeshPath navMeshPath = new NavMeshPath();
            List<CustomNavigationPoint> availableCover = new List<CustomNavigationPoint>();

            foreach (var point in coverPoints)
            {
                if (point == null) continue;
                if (!point.IsFreeById(BotOwner.Id)) continue;
                if (!IsSettlePositionClear(point.Position)) continue;
                if (Utils.Utils.GetNavDistance(leaderPosition, point.Position, navMeshPath) <= maxDistance)
                {
                    availableCover.Add(point);
                }
            }

            if (availableCover.Count == 0) return null;
            return availableCover[UnityEngine.Random.Range(0, availableCover.Count)];
        }

        private bool TryGetRandomSettlePoint(Vector3 leaderPosition, int settleDistance, out Vector3 settlePosition)
        {
            float minOffset = Mathf.Min(1f, settleDistance * 0.19f);
            float maxOffset = Mathf.Min(5f, settleDistance * 0.65f);
            float xOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
            float zOffset = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minOffset, maxOffset);
            Vector3 candidate = new Vector3(leaderPosition.x + xOffset, leaderPosition.y, leaderPosition.z + zOffset);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, 2f, -1))
            {
                if (!IsSettlePositionClear(navMeshHit.position))
                {
                    settlePosition = default;
                    return false;
                }
                settlePosition = navMeshHit.position;
                return true;
            }

            settlePosition = default;
            return false;
        }

        private bool IsSettlePositionClear(Vector3 position)
        {
            if (bossPlayer != null && (bossPlayer.Transform.position - position).sqrMagnitude < SettleSpacingSqr)
            {
                return false;
            }

            if (bossData != null)
            {
                foreach (BotOwner follower in bossData.Followers)
                {
                    if (follower == null || follower == BotOwner || follower.GetPlayer == null || follower.IsDead) continue;
                    if ((follower.GetPlayer.Transform.position - position).sqrMagnitude < SettleSpacingSqr)
                    {
                        return false;
                    }
                }
            }

            return true;
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

                float distanceToLeader = Mathf.Abs((isPathBlocked && lastMoveTarget.HasValue ? (lastMoveTarget.Value - botPosition) : (leaderGridPosition - botPosition)).magnitude);
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
            const float campRadius = 30f;

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

                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }
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


        private NavMeshPathStatus GoToPosition(Vector3 position)
        {
            NavMeshPathStatus pathStatus = BotOwner.GoToPoint(position, true, -1f, false, false);
            if (pathStatus == NavMeshPathStatus.PathComplete)
            {
                BotOwner.Steering.LookToMovingDirection();
                lastMoveTarget = position;
            }

            return pathStatus;
        }

        private void UpdateFollowPath(Vector3 leaderPosition)
        {
            isPathBlocked = false;
            if (GoToPosition(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                return;
            }

            if (NavMesh.SamplePosition(leaderPosition, out _, 1.5f, -1) && GoToPosition(leaderPosition) != NavMeshPathStatus.PathComplete)
            {
                isPathBlocked = true;
            }

            if (isPathBlocked)
            {
                CustomNavigationPoint freeClosePoint = BotOwner.Covers.GetFreeClosePoint(leaderPosition, 0f, false);
                if (freeClosePoint != null)
                {
                    GoToPosition(freeClosePoint.Position);
                }
            }
        }

        private void SetPatrolEnabled(bool state)
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

        private void UpdateStuckWatchdog()
        {
            if (bossPlayer == null || BotOwner == null)
            {
                return;
            }

            if (Time.time < nextStuckCheckAt)
            {
                return;
            }

            nextStuckCheckAt = Time.time + StuckCheckInterval;

            if (!ShouldExpectMovement())
            {
                ResetStuckWatchdog(BotOwner.Position, resetAttempts: true);
                return;
            }

            if (BotOwner.BotState != EBotState.Active || BotOwner.IsDead || BotOwner.DoorOpener.Interacting)
            {
                return;
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                ResetStuckWatchdog(BotOwner.Position, resetAttempts: true);
                return;
            }

            bool healing = BotOwner.Medecine?.FirstAid?.Using == true || BotOwner.Medecine?.SurgicalKit?.Using == true;
            if (healing)
            {
                ResetStuckWatchdog(BotOwner.Position, resetAttempts: false);
                return;
            }

            Vector3 currentPos = BotOwner.Position;
            bool moved = (currentPos - lastStuckSamplePos).sqrMagnitude > StuckMovementEpsilonSqr;
            bool moverAdvancing = BotOwner.Mover.IsMoving || BotOwner.Mover.Sprinting;

            if (moved || moverAdvancing)
            {
                if (moved)
                {
                    stuckRecoverAttempts = 0;
                }

                ResetStuckWatchdog(currentPos, resetAttempts: false);
                return;
            }

            stuckAccumulated += StuckCheckInterval;
            lastStuckSamplePos = currentPos;

            if (stuckAccumulated < StuckDurationToRecover || Time.time < unstuckCooldownUntil)
            {
                return;
            }

            unstuckCooldownUntil = Time.time + UnstuckCooldownSeconds;
            stuckRecoverAttempts++;

            Vector3 leaderPos = GetLeaderTargetPosition();
            FollowerRecovery.SoftReset(BotOwner);
            BotOwner.StopMove();

            if (stuckRecoverAttempts >= EscalationTeleportStep)
            {
                TryTeleportUnstickNearLeader(leaderPos);
                stuckRecoverAttempts = 0;
            }
            else
            {
                UpdateFollowPath(leaderPos);
                nextFollowUpdateAt = 0f;
            }

            stuckAccumulated = 0f;
            lastStuckSamplePos = BotOwner.Position;
        }

        private bool ShouldExpectMovement()
        {
            if (movingToSettlePoint || movingToPatrolPoint)
            {
                return true;
            }

            if (bossPlayer == null)
            {
                return false;
            }

            float distToLeader = (GetLeaderTargetPosition() - BotOwner.Position).magnitude;
            return distToLeader > DefaultFollowDistance + 1f;
        }

        private void ResetStuckWatchdog(Vector3 samplePos, bool resetAttempts)
        {
            stuckAccumulated = 0f;
            lastStuckSamplePos = samplePos;
            if (resetAttempts)
            {
                stuckRecoverAttempts = 0;
            }
        }

        private void TryTeleportUnstickNearLeader(Vector3 leaderPos)
        {
            if (BotOwner.GetPlayer == null)
            {
                return;
            }

            if ((BotOwner.Position - leaderPos).sqrMagnitude < 25f)
            {
                return;
            }

            Vector3 target = leaderPos;
            if (NavMesh.SamplePosition(leaderPos + UnityEngine.Random.insideUnitSphere * 2.2f, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            {
                target = navHit.position;
            }

            followerData?.BeginTeleportGrace(target);
            BotOwner.Mover.Stop();
            BotOwner.GetPlayer.Teleport(target);
            nextFollowUpdateAt = 0f;
        }

    }
}
