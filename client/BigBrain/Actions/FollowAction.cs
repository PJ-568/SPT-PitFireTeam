using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Out-of-combat follower patrol action. It keeps the follower near the boss, chooses stable
    /// settle/cover positions, resumes chase when out of range, and plays peaceful look/watch actions
    /// when no command or combat layer owns the bot.
    /// </summary>
    internal class FollowAction : CustomLogic
    {
        private const float FallbackFollowDistance = 12f;
        private const bool EnableFollowDebug = false;
        private const float MaxBossSpeedForSettle = 0.35f;
        private const float SettleSpacing = 2.5f;
        private const float SettleSpacingSqr = SettleSpacing * SettleSpacing;

        private Player? bossPlayer;
        private pitAIBossPlayer? bossData;
        private BotFollowerPlayer? followerData;

        private float nextFollowUpdateAt;
        private float nextSettlePointAt;
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

        /// <summary>
        /// Tracks committed cover point and sector anchor for stable hold behavior.
        /// Prevents constant cover-switching by committing to a cover within a 20m sector.
        /// </summary>
        private FollowerCoverCommitment coverCommitment;

        public FollowAction(BotOwner botOwner) : base(botOwner)
        {
            patrolPerimeterRadius = pitFireTeam.patrolRadius.Value;
            coverCommitment = new FollowerCoverCommitment();
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

            coverCommitment.ClearCommitment();

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
                if (patrolEnabled)
                {
                    Patrol();
                }
                else
                {
                    Follow();
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

        private float GetEffectiveFollowDistance()
        {
            float distance = pitFireTeam.followDistance?.Value ?? FallbackFollowDistance;
            return Mathf.Clamp(distance, 8f, 30f);
        }

        private void Follow(bool forceFollow = false, float forcedDistance = 0f)
        {
            if (bossPlayer == null) return;

            // A follower that is already walking to a settle point should keep the point active.
            // Otherwise normalize posture once so the bot does not continue patrol crouched/prone
            // after combat, looting, or a hold command.
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
                : Mathf.Abs((leaderPosition - BotOwner.Position).magnitude);

            float followDistance = GetEffectiveFollowDistance();
            bool inRange = distance < followDistance;

            if (inRange)
            {
                // Close enough to the boss: either settle into a committed local cover/position or
                // hold the existing one. This is patrol stabilization, not combat cover selection.
                if (isPathBlocked)
                {
                    BotOwner.StopMove();
                    return;
                }

                // ──────────────────────────────────────────────────────────────────────────
                // Sector-based cover commitment system
                // ──────────────────────────────────────────────────────────────────────────

                // Check if boss moved to a new sector (>20m from anchor)
                if (coverCommitment.SectorChanged(leaderPosition))
                {
                    coverCommitment.OnSectorChange(leaderPosition);
                }

                // Validate committed cover; clear if no longer valid
                if (coverCommitment.IsCommitted && !coverCommitment.IsCoverStillValid(BotOwner, leaderPosition))
                {
                    coverCommitment.ClearCommitment();
                }

                // Determine current strategy
                FollowerCoverCommitment.FollowStrategy strategy = coverCommitment.GetStrategy();

                if (strategy == FollowerCoverCommitment.FollowStrategy.Move)
                {
                    // MOVE: Search for and commit to a new cover in 80m radius
                    if (nextSettlePointAt > Time.time)
                    {
                        return;
                    }

                    nextSettlePointAt = Time.time + 3f; // Check less frequently during move state

                    CustomNavigationPoint? newCover = TryFindExpandedCoverPoint(leaderPosition);
                    if (newCover != null)
                    {
                        // Commit to this cover for the current sector
                        coverCommitment.CommitToCover(newCover, leaderPosition);
                        BotOwner.Memory.SetCoverPoints(newCover);
                        BotOwner.GoToSomePointData.SetPoint(newCover.Position);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        if (!TryApplyCommandLookOverride())
                        {
                            BotOwner.Steering.LookToPathDestPoint();
                        }
                        movingToSettlePoint = true;
                        nextFollowUpdateAt = Time.time + 0.5f;
                        return;
                    }

                    // No cover found: follow boss at medium range (boss-proximity mode)
                    if (TryGetRandomSettlePoint(leaderPosition, followDistance, out Vector3 settlePosition))
                    {
                        BotOwner.GoToSomePointData.SetPoint(settlePosition);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        if (!TryApplyCommandLookOverride())
                        {
                            BotOwner.Steering.LookToPathDestPoint();
                        }
                        movingToSettlePoint = true;
                        nextFollowUpdateAt = Time.time + 0.5f;
                    }
                    return;
                }
                else
                {
                    // HOLD: Bot is committed to a cover. Just hold and scan for engagement/support.
                    // The combat layer will handle engagement detection while holding.
                    if (movingToSettlePoint)
                    {
                        if (BotOwner.GoToSomePointData.IsCome())
                        {
                            movingToSettlePoint = false;
                            BotOwner.StopMove();
                        }
                        return;
                    }

                    // At committed cover position: hold and let combat layer scan
                    BotOwner.StopMove();
                    return;
                }
            }

            // OUT OF RANGE: Chase boss
            movingToSettlePoint = false;
            UpdateFollowPath(leaderPosition);

            // Normal follow is allowed to sprint when the boss has opened distance. Once close enough,
            // the settle logic above takes over and moves the follower into a stable local position.
            bool shouldSprint = distance > Mathf.Min(followDistance + 3f, 16f);
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }
            BotOwner.Mover.Sprint(shouldSprint, false);
        }

        private CustomNavigationPoint? TryGetSettleCoverPoint(Vector3 leaderPosition, float settleDistance)
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

        /// <summary>
        /// Find best cover within 80m of boss for sector-based commitment.
        /// Prefers cover closest to boss to maintain protective positioning.
        /// Avoids crowded/occupied cover and spotted positions.
        /// </summary>
        private CustomNavigationPoint? TryFindExpandedCoverPoint(Vector3 bossPosition)
        {
            if (bossPlayer == null) return null;

            float searchRadius = FollowerCoverCommitment.GetCoverSearchRadius();
            float maxBossDist = FollowerCoverCommitment.GetCoverMaxBossDistance();

            List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(bossPosition, searchRadius);
            List<CustomNavigationPoint> validCandidates = new List<CustomNavigationPoint>();

            foreach (var point in coverPoints)
            {
                if (point == null) continue;
                if (!point.IsFreeById(BotOwner.Id)) continue;
                if (point.IsSpotted) continue;
                if (!IsSettlePositionClear(point.Position)) continue;

                // Must be within max distance of boss
                if ((point.Position - bossPosition).sqrMagnitude > maxBossDist * maxBossDist)
                {
                    continue;
                }

                validCandidates.Add(point);
            }

            if (validCandidates.Count == 0)
            {
                return null;
            }

            // Sort by distance to boss (prefer closest for protective positioning)
            validCandidates.Sort((a, b) =>
            {
                float distA = (a.Position - bossPosition).sqrMagnitude;
                float distB = (b.Position - bossPosition).sqrMagnitude;
                return distA.CompareTo(distB);
            });

            return validCandidates[0];
        }

        private bool TryGetRandomSettlePoint(Vector3 leaderPosition, float settleDistance, out Vector3 settlePosition)
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
                return;
            }

            movingToSettlePoint = false;

            Vector3 leaderPosition = bossPlayer.Transform.position;
            Vector3 botPosition = BotOwner.GetPlayer.Transform.position;
            float followDistance = GetEffectiveFollowDistance();

            if (!isPatrolCampInitialized)
            {
                Vector3 leaderGridPosition = new Vector3(
                    Mathf.Floor(leaderPosition.x / 3f) * 3f,
                    Mathf.Floor(leaderPosition.y / 3f) * 3f,
                    Mathf.Floor(leaderPosition.z / 3f) * 3f
                );

                float distanceToLeader = Mathf.Abs((leaderGridPosition - botPosition).magnitude);
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
                    if (!TryApplyCommandLookOverride())
                    {
                        BotOwner.Steering.LookToMovingDirection();
                    }
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
                if (!TryApplyCommandLookOverride())
                {
                    BotOwner.Steering.LookToPoint(navMeshHit.position + Vector3.up * 1.5f);
                }

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
                if (!TryApplyCommandLookOverride())
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }

            return pathStatus;
        }

        private bool TryApplyCommandLookOverride()
        {
            return BotFollowerPlayer.TryApplyCommandLookOverride(BotOwner);
        }

        private void UpdateFollowPath(Vector3 leaderPosition)
        {
            isPathBlocked = false;
            if (GoToPosition(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                return;
            }

            if (NavMesh.SamplePosition(leaderPosition, out _, 5f, -1) && GoToPosition(leaderPosition) != NavMeshPathStatus.PathComplete)
            {
                isPathBlocked = true;
            }

            if (isPathBlocked)
            {
                CustomNavigationPoint freeClosePoint = BotOwner.Covers.GetFreeClosePoint(leaderPosition, 1f, false);
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

    }
}
