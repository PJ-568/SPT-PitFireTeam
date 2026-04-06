using EFT;
using EFT.HealthSystem;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    /// <summary>
    /// Self-contained vanilla PMC follower combat decision and end-condition engine.
    /// Mirrors the vanilla PMC follower layer logic.
    /// Does not depend on FollowerCombatLogicBase - only on BotOwner and FollowerCombatCommon.
    /// </summary>
    internal sealed class FollowerCombatDefault
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Enums
        // ──────────────────────────────────────────────────────────────────────────

        private enum FollowerCombatStyle { HangBack, MoveForward }
        private enum EscortTargetType { None, Cover, Point }
        private enum GroupSearchRole { None, Leader, Follower }

        // ──────────────────────────────────────────────────────────────────────────
        // Constants
        // ──────────────────────────────────────────────────────────────────────────


        private const float CombatAreaExitDistance = 12f;
        private const float CombatAreaArrivalDistance = 8f;
        private const float PostCombatHealDelay = 1f;
        private const float SurgeryHealDelayMultiplier = 1.5f;
        private const float EscortEnemyMoveReevalDistance = 9f;
        private const float EscortEnemyAngleReeval = 30f;
        private const float GroupSearchJoinDistanceMin = 12f;
        private const float GroupSearchJoinDistanceMax = 30f;
        private const float GroupSearchSectorAngleTolerance = 55f;
        private const float StartWeakEnemyCloseDistance = 20f;
        private const float NoVisualReliablePressureDistance = 25f;
        private const float HoldCoverSignalDebounceSeconds = 0.2f;
        private const float GoalEnemyPHoldLockSeconds = 0.75f;
        private const float GoalEnemyPCoverRefreshInterval = 0.2f;
        private const float PushEnabledMaxEnemyDistance = 60f;

        // ──────────────────────────────────────────────────────────────────────────
        // Core references
        // ──────────────────────────────────────────────────────────────────────────

        private readonly BotOwner botOwner;
        private readonly BotFollower botFollower;
        private readonly FollowerCombatCommon combatCommon;
        private readonly FollowerCombatPush combatPush;

        // ──────────────────────────────────────────────────────────────────────────
        // Combat area state
        // ──────────────────────────────────────────────────────────────────────────

        private float closeBossSqr = 100f;
        private const float bossPointRadius = 5f;
        private Vector3 combatAreaCenter;
        private FollowerCombatStyle combatStyle;
        private bool combatAreaInitialized;

        // ──────────────────────────────────────────────────────────────────────────
        // Boss cover state
        // ──────────────────────────────────────────────────────────────────────────

        private float nextBossCoverCheckTime;
        private bool haveNearBossCover;
        private Vector3 lastBossPointSample;
        private CustomNavigationPoint? nearBossCoverPoint;

        // ──────────────────────────────────────────────────────────────────────────
        // Hold / timing state
        // ──────────────────────────────────────────────────────────────────────────

        private float lastGoToPointEndTime;
        private bool holdCoverSignalActive;
        private float holdCoverSignalSince;
        private float goalEnemyPHoldLockUntil;
        private float nextGoalEnemyPCoverRefreshTime;

        // ──────────────────────────────────────────────────────────────────────────
        // ──────────────────────────────────────────────────────────────────────────
        // Escort state
        // ──────────────────────────────────────────────────────────────────────────

        private CustomNavigationPoint? escortCoverPoint;
        private Vector3 escortPoint;
        private EscortTargetType escortTargetType;
        private string? escortEnemyId;
        private Vector3 escortBossAnchor;
        private Vector3 escortEnemyAnchor;
        private bool escortWantedShootLane;

        // ──────────────────────────────────────────────────────────────────────────
        // Group search state
        // ──────────────────────────────────────────────────────────────────────────

        private GroupSearchRole groupSearchRole;
        private string? groupSearchEnemyId;
        private string? groupSearchLeaderProfileId;
        private Vector3 groupSearchBossAnchor;
        private Vector3 groupSearchEnemyAnchor;
        private Vector3 groupSearchLeaderAnchor;
        private Vector3 groupSearchPoint;

        // ──────────────────────────────────────────────────────────────────────────
        // Constructor
        // ──────────────────────────────────────────────────────────────────────────

        public FollowerCombatDefault(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.botFollower = botOwner.BotFollower;
            this.combatCommon = combatCommon;
            this.combatPush = new FollowerCombatPush(botOwner, combatCommon);

        }

        // ──────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        public void Reset()
        {
            lastGoToPointEndTime = 0f;
            holdCoverSignalActive = false;
            holdCoverSignalSince = 0f;
            goalEnemyPHoldLockUntil = 0f;
            nextGoalEnemyPCoverRefreshTime = 0f;
            combatAreaCenter = Vector3.zero;
            combatStyle = FollowerCombatStyle.HangBack;
            combatAreaInitialized = false;
            ClearEscortCommit();
            ClearGroupSearchCommit();
        }

        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (prevDecision.HasValue &&
                (prevDecision.Value.Action == BotLogicDecision.search ||
                 prevDecision.Value.Action == BotLogicDecision.goToPointTactical) &&
                nextDecision.Action != BotLogicDecision.search &&
                nextDecision.Action != BotLogicDecision.goToPointTactical)
            {
                ClearGroupSearchCommit();
            }

            BotLogicDecision action = nextDecision.Action;
            if (action != BotLogicDecision.shootFromStationary &&
                action != BotLogicDecision.debugStationary &&
                action != BotLogicDecision.debugStationaryInstantTake &&
                botOwner.WeaponManager.Stationary.Taken)
            {
                botOwner.WeaponManager.Stationary.DropCurWeapon(false, true);
            }

            bool isGoalEnemyPHold = nextDecision.Action == BotLogicDecision.holdPosition &&
                                    string.Equals(nextDecision.Reason, "goalEnemy.P", StringComparison.Ordinal);
            bool wasGoalEnemyPHold = prevDecision.HasValue &&
                                     prevDecision.Value.Action == BotLogicDecision.holdPosition &&
                                     string.Equals(prevDecision.Value.Reason, "goalEnemy.P", StringComparison.Ordinal);

            if (isGoalEnemyPHold && !wasGoalEnemyPHold)
            {
                goalEnemyPHoldLockUntil = Time.time + GoalEnemyPHoldLockSeconds;
                nextGoalEnemyPCoverRefreshTime = 0f;
                ResetHoldCoverSignal();
            }

            if (nextDecision.Action != BotLogicDecision.holdPosition)
            {
                goalEnemyPHoldLockUntil = 0f;
                nextGoalEnemyPCoverRefreshTime = 0f;
                ResetHoldCoverSignal();
            }
        }

        public void PrepareStartDecision()
        {
            combatCommon.PrepareStartDecision(GetPushEnemyMaxDistance());
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Decision entry point
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Full combat decision. Includes pre-fight gate, opener, and decision tree.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            // Pre-fight gate: dogfight, in-fight, heal.
            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = combatCommon.PreFightLogic();
            if (preFight != null)
            {
                return preFight.Value;
            }

            // One-shot opener picked at combat start.
            if (combatCommon.HasInitialDecision)
            {
                return combatCommon.ConsumeInitialDecision();
            }

            // Immediate shoot window.
            AICoreActionResultStruct<BotLogicDecision, GClass26>? shootNow = combatCommon.TryGetImmediateShootDecision("shootNow");
            if (shootNow != null)
            {
                return shootNow.Value;
            }

            return DecideCombat(goalEnemy);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // End-condition dispatch
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Default end-condition dispatcher.
        /// This class handles end checks that depend on Default-owned commit/state,
        /// then delegates shared end checks to <see cref="FollowerCombatCommon"/>.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            // Default-owned decisions rely on local commit state (escort/group-search/push).
            switch (currentDecision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return EndHoldPosition(currentDecision.Reason);
                case BotLogicDecision.runToEnemy:
                    return combatCommon.EndBaseGoToEnemy();
                case BotLogicDecision.goToEnemy:
                    return combatCommon.EndBaseGoToEnemy();
                case BotLogicDecision.goToPoint:
                    return EndGoToPoint();
                case BotLogicDecision.search:
                    return EndGroupSearchLeader();
                case BotLogicDecision.goToPointTactical:
                    return EndGoToPointTactical(currentDecision.Reason);
            }

            // Everything else is shared and centralized in Common for reuse by other logic implementations.
            return combatCommon.ShallEndCurrentDecision(currentDecision);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Core decision tree (private)
        // ──────────────────────────────────────────────────────────────────────────

        private AICoreActionResultStruct<BotLogicDecision, GClass26> DecideCombat(EnemyInfo goalEnemy)
        {
            bool canShoot = goalEnemy.CanShoot;
            bool wantKill = combatCommon.ProtectWantKill(GetPushEnemyMaxDistance());
            bool careKill = combatCommon.ProtectCareKill(GetPushEnemyMaxDistance());
            UpdateCombatAreaStyle();
            combatCommon.RefreshShootCover();
            // disable grenades, for now
            /* if (TryActivateFollowerGrenade(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.throwGrenadeFromPlace, "FollowerGrenade");
            } */

            if (!goalEnemy.IsVisible && botOwner.SmokeGrenade.ShallShoot())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootToSmoke, "SmokeGrenad");
            }
            // temporary disabled
            /* AICoreActionResultStruct<BotLogicDecision, GClass26>? groupSearchDecision = TryGetGroupSearchDecision(goalEnemy);
            if (groupSearchDecision != null)
            {
                return groupSearchDecision.Value;
            } */

            if (careKill &&
                !combatCommon.IsEnemyVisibleAndShootable() &&
                Time.time - goalEnemy.PersonalSeenTime < 3f)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "goalEnemy.P");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? wantKillPushDecision =
                TryGetWantKillPushDecision(goalEnemy, careKill, wantKill);
            if (wantKillPushDecision != null)
            {
                return wantKillPushDecision.Value;
            }

            if (IsMoveForwardStyle())
            {
                return DecideBossPositionAction(GetBossPosition());
            }

            if (combatCommon.HaveCoverToShoot && wantKill)
            {
                bool canShootLastPosition = CanShootLastKnownPosition(goalEnemy);
                bool seesShootableEnemy = combatCommon.IsEnemyVisibleAndShootable();
                if (canShootLastPosition && !seesShootableEnemy && Time.time - goalEnemy.PersonalSeenTime < 3f)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "canShootLas");
                }

                return combatPush.EngageEnemy();
            }

            bool canStandAndShoot = false;
            if (canShoot)
            {
                BotOwner closestFriend = botOwner.Covers.GetClosestFriend(out float friendDist);
                canStandAndShoot = friendDist >= LocalBotSettingsProviderClass.Core.MIN_DIST_CLOSE_DEF ||
                    closestFriend == null ||
                    closestFriend.Id > botOwner.Id;
            }

            if (canStandAndShoot)
            {
                if (goalEnemy.IsVisible)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "goalEnemy.V");
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26>? noVisualDecision =
                    TryGetNoVisualStandShootDecision(goalEnemy);
                if (noVisualDecision != null)
                {
                    return noVisualDecision.Value;
                }

                if (!FollowerCombatCommon.WasHitRecently(botOwner, botOwner.Settings.FileSettings.Boss.IF_I_HITTED_GO_AWAY_SEC_HIT) && !botOwner.Memory.IsUnderFire)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "deltaLastHi");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "deltaLastHi");
            }

            Vector3 bossPosition = GetBossPosition();
            if ((botOwner.Position - bossPosition).sqrMagnitude > closeBossSqr)
            {
                return DecideBossPositionAction(bossPosition);
            }

            if (botOwner.Memory.IsInCover)
            {
                if (ShouldStartCoverHeal(goalEnemy))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "CoverHealDelay");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "distToBoss");
            }

            if (combatCommon.HaveCoverToShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(HoldOrCover(), "HaveCoverSh");
            }

            return DecideBossPositionAction(GetBossPosition());
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetWantKillPushDecision(
            EnemyInfo goalEnemy,
            bool careKill,
            bool wantKill)
        {
            if (!careKill || !wantKill)
            {
                return null;
            }

            if (!combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            if (!IsPushEnabled())
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            if (!combatCommon.IsEnemyLowThreat(false, 2f))
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            if (combatCommon.IsFollowerCriticallyWounded())
            {
                // Try strict cover search first: within boss proximity, must have shoot lane
                if (combatCommon.TryAssignRetreatAttackCover(goalEnemy, true, closeBossSqr))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "critAtkRetreat");
                }

                // Fallback: try to find ANY cover within 100m, no shoot lane requirement
                return TryGetCriticalHealthRetreatDecision(goalEnemy);
            }

            if (combatCommon.IsFollowerInjured() && !combatCommon.HaveCoverToShoot)
            {
                combatCommon.TryAssignRetreatAttackCover(goalEnemy, false, closeBossSqr);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "injuredProc");
            }

            BotLogicDecision pushDecision = goalEnemy.Distance <= StartWeakEnemyCloseDistance
                ? BotLogicDecision.goToEnemy
                : BotLogicDecision.runToEnemy;

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushDecision, "wantKill");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetNoVisualStandShootDecision(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible)
            {
                return null;
            }

            bool reliablePersonalLocation = combatCommon.HasReliablePersonalEnemyLocation(goalEnemy);
            if (!reliablePersonalLocation)
            {
                // Without a reliable personal location, bias to safe boss/escort behavior.
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            float personalDistanceSqr = (goalEnemy.PersonalLastPos - botOwner.Position).sqrMagnitude;
            if (personalDistanceSqr <= NoVisualReliablePressureDistance * NoVisualReliablePressureDistance)
            {
                // Reliable and close: keep pressure instead of idling.
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "noVisualReliableClose");
            }

            return null;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetProtectorEngageDecision(EnemyInfo goalEnemy)
        {
            if (combatCommon.IsFollowerCriticallyWounded() && goalEnemy.Distance > 15f)
            {
                return null;
            }

            if (!IsPushEnabled() ||
                !combatCommon.HasReliablePersonalEnemyLocation(goalEnemy) ||
                !ShouldAttackImmediately(goalEnemy) ||
                !combatCommon.IsEnemyLowThreat(false, 2f) ||
                !CanLeaveBossForPush() ||
                goalEnemy.Distance > GetPushEnemyMaxDistance(goalEnemy))
            {
                return null;
            }

            Enemy.EnemyDistance distanceToEnemy = Enemy.Distance(botOwner);
            if (distanceToEnemy <= Enemy.EnemyDistance.VeryClose && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
            }

            if (distanceToEnemy <= Enemy.EnemyDistance.Close)
            {
                BotLogicDecision pushDecision = goalEnemy.IsVisible || !botOwner.CanSprintPlayer
                    ? BotLogicDecision.goToEnemy
                    : BotLogicDecision.runToEnemy;

                if (!Enemy.IsClosestEnemy(botOwner) && distanceToEnemy <= Enemy.EnemyDistance.Mid)
                {
                    pushDecision = BotLogicDecision.goToEnemy;
                }

                ClearEscortCommit();
                ClearGroupSearchCommit();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushDecision, "pushEnemy");
            }

            if (distanceToEnemy == Enemy.EnemyDistance.Mid)
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    ClearEscortCommit();
                    ClearGroupSearchCommit();
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "getInCloseSlow");
                }

                ClearEscortCommit();
                ClearGroupSearchCommit();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToEnemy, "getInCloseFast");
            }

            return null;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetGroupSearchDecision(EnemyInfo goalEnemy)
        {
            if (!CanGroupSearch(goalEnemy) ||
                botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.realPlayer == null ||
                string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return null;
            }

            BotOwner? leader = FollowerGroupSearchRuntime.GetOrAssignLeader(boss, goalEnemy, IsValidGroupSearchCandidate);
            if (leader == null)
            {
                return null;
            }

            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            if (leader.ProfileId == botOwner.ProfileId)
            {
                if (!TryGetGroupSearchLeaderPoint(goalEnemy, out Vector3 searchPoint))
                {
                    return null;
                }

                CommitGroupSearchLeader(goalEnemy, bossPosition, searchPoint);
                botOwner.GoToSomePointData.SetPoint(searchPoint);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.search, "groupSearchLeader");
            }

            if (!CanJoinGroupSearchLeader(leader, bossPosition, enemyAnchor))
            {
                return null;
            }

            if (!TryGetGroupSearchFollowerPoint(leader.Position, out Vector3 followPoint))
            {
                return null;
            }

            CommitGroupSearchFollower(goalEnemy, bossPosition, leader, followPoint);
            botOwner.GoToSomePointData.SetPoint(followPoint);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, "groupSearchFollow");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> DecideBossPositionAction(Vector3 bossPosition)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            if (goalEnemy != null)
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? committedEscortDecision =
                    TryGetCommittedEscortDecision(goalEnemy, bossPosition);
                if (committedEscortDecision != null)
                {
                    return committedEscortDecision.Value;
                }
            }

            if (goalEnemy != null &&
                TryFindEscortCover(goalEnemy, bossPosition, out CustomNavigationPoint? preferredCover, out bool coverHasShootLane))
            {
                return MoveToEscortCover(goalEnemy, bossPosition, preferredCover, coverHasShootLane, "escortCover");
            }

            if (goalEnemy != null)
            {
                combatCommon.HoldFor(2f);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "escortNoSafeCover");
            }

            RefreshBossCover();
            if (haveNearBossCover)
            {
                return MoveToEscortCover(goalEnemy, bossPosition, nearBossCoverPoint, false, "sDistCloseB");
            }

            if (Time.time - lastGoToPointEndTime > 10f &&
                NavMesh.SamplePosition(bossPosition, out NavMeshHit navMeshHit, bossPointRadius, -1))
            {
                lastBossPointSample = navMeshHit.position;
                if (goalEnemy != null)
                {
                    CommitEscortPoint(goalEnemy, bossPosition, lastBossPointSample);
                }

                botOwner.GoToSomePointData.SetPoint(lastBossPointSample);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "HaveCoverSh");
            }

            if (botOwner.Memory.IsInCover)
            {
                combatCommon.HoldFor(4f);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "HaveCoverSh");
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossNavMeshHit, 2f, -1))
            {
                if (goalEnemy != null)
                {
                    CommitEscortPoint(goalEnemy, bossPosition, bossNavMeshHit.position);
                }

                botOwner.GoToSomePointData.SetPoint(bossNavMeshHit.position);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss");
            }

            if (goalEnemy != null)
            {
                CommitEscortPoint(goalEnemy, bossPosition, bossPosition);
            }

            botOwner.GoToSomePointData.SetPoint(bossPosition);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss");
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedEscortDecision(
            EnemyInfo goalEnemy,
            Vector3 bossPosition)
        {
            if (!IsEscortCommitStillValid(goalEnemy, bossPosition))
            {
                return null;
            }

            if (escortTargetType == EscortTargetType.Cover && escortCoverPoint != null)
            {
                botOwner.Memory.BotCurrentCoverInfo.SetCover(escortCoverPoint, true);
                if (botOwner.Memory.IsInCover)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "escortHold");
                }

                return botOwner.CanSprintPlayer
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "escortCover")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "escortCover");
            }

            if (escortTargetType == EscortTargetType.Point)
            {
                botOwner.GoToSomePointData.SetPoint(escortPoint);
                if (botOwner.GoToSomePointData.IsCome())
                {
                    combatCommon.HoldFor(2f);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "escortPointHold");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "escortPoint");
            }

            ClearEscortCommit();
            return null;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> MoveToEscortCover(
            EnemyInfo? goalEnemy,
            Vector3 bossPosition,
            CustomNavigationPoint? coverPoint,
            bool wantedShootLane,
            string reason)
        {
            if (goalEnemy != null)
            {
                CommitEscortCover(goalEnemy, bossPosition, coverPoint, wantedShootLane);
            }

            botOwner.Memory.BotCurrentCoverInfo.SetCover(coverPoint, true);
            return botOwner.CanSprintPlayer
                ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, reason)
                : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, reason);
        }

        /// <summary>
        /// When critically wounded and strict retreat cover fails, try to find ANY cover within 100m.
        /// If found, decide between walking (attackMoving) or running (runToCover) based on distance.
        /// If no cover found, return null to fall back to healing/safe position logic.
        /// </summary>
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCriticalHealthRetreatDecision(
            EnemyInfo goalEnemy)
        {
            CustomNavigationPoint? emergencyCover = TryFindCriticalHealthCover(goalEnemy);
            if (emergencyCover == null)
            {
                // No cover available even with large radius - fall back to healing
                return null;
            }

            // Cover found - assign it and decide if we run or walk based on distance
            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(emergencyCover, true);

            float distanceToCover = (emergencyCover.Position - botOwner.Position).magnitude;

            // Walk to nearby cover, run to distant cover
            bool shouldRun = distanceToCover > 15f;
            BotLogicDecision decision = shouldRun ? BotLogicDecision.runToCover : BotLogicDecision.attackMoving;
            string reason = shouldRun ? "critHealthRunToCover" : "critHealthWalkToCover";

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(decision, reason);
        }

        /// <summary>
        /// Search for any available cover within 100m for critically wounded follower.
        /// Uses relaxed filters - cover must be free and not spotted, but no shoot lane required.
        /// </summary>
        private CustomNavigationPoint? TryFindCriticalHealthCover(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return null;
            }

            Vector3 bossPosition = GetBossPosition();
            Vector3 searchAnchor = botOwner.Position;
            const float largeSearchRadius = 100f; // Search within 100m to minimize chance of no cover

            List<CustomNavigationPoint> customNavigationPoints = Utils.Covers.GetCoverPoints(botOwner,
                searchAnchor,
                largeSearchRadius,
                point =>
                {
                    // Must be free and not spotted
                    if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                    {
                        return false;
                    }
                    // Must not be very close to the boss
                    if ((point.Position - bossPosition).sqrMagnitude < 2.5f * 2.5f)
                    {
                        return false;
                    }
                    return true;
                });

            // Get the closest one to the bot
            CustomNavigationPoint? emergencyCover = customNavigationPoints.FirstOrDefault();

            return emergencyCover;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> GetNoPushSafePositionDecision(EnemyInfo goalEnemy)
        {
            if (combatCommon.TryAssignRetreatAttackCover(goalEnemy, true, closeBossSqr))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "unsafePushCover");
            }

            if (combatCommon.HaveCoverToShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(HoldOrCover(), "unsafePushHold");
            }

            Vector3 bossPosition = GetBossPosition();
            if ((botOwner.Position - bossPosition).sqrMagnitude <= closeBossSqr && botOwner.Memory.IsInCover)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "unsafePushBossHold");
            }

            return DecideBossPositionAction(bossPosition);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // End conditions (private)
        // ──────────────────────────────────────────────────────────────────────────

        private AICoreActionEndStruct EndHoldPosition(string reason)
        {
            if (string.Equals(reason, "distToBoss", StringComparison.Ordinal))
            {
                return EndDistToBossHoldPosition();
            }

            Vector3 bossPosition = GetBossPosition();
            if ((botOwner.Position - bossPosition).sqrMagnitude > closeBossSqr)
            {
                return new AICoreActionEndStruct("bossTooFar", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (string.Equals(reason, "goalEnemy.P", StringComparison.Ordinal))
            {
                return EndGoalEnemyPHoldPosition(goalEnemy);
            }

            combatCommon.RefreshShootCover();
            bool stableCoverSignal = UpdateDebouncedHoldCoverSignal(
                combatCommon.HaveCoverToShoot && combatCommon.ProtectWantKill() && combatCommon.ProtectCareKill());

            if (stableCoverSignal)
            {
                return new AICoreActionEndStruct("coverAvailableToShootStable", true);
            }

            return combatCommon.EndBaseHoldPosition(reason);
        }

        private AICoreActionEndStruct EndGoalEnemyPHoldPosition(EnemyInfo? goalEnemy)
        {
            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.5f))
            {
                return new AICoreActionEndStruct("goalEnemyPHardInterrupt", true);
            }

            if (goalEnemy != null)
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    return new AICoreActionEndStruct("goalEnemyVisibleAndShootable", true);
                }

                if (goalEnemy.IsVisible &&
                    goalEnemy.Distance < botOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
                {
                    return new AICoreActionEndStruct("goalEnemyCloseAndVisible", true);
                }
            }

            if (Time.time >= nextGoalEnemyPCoverRefreshTime)
            {
                combatCommon.RefreshShootCover();
                nextGoalEnemyPCoverRefreshTime = Time.time + GoalEnemyPCoverRefreshInterval;
            }

            bool stableCoverSignal = UpdateDebouncedHoldCoverSignal(combatCommon.HaveCoverToShoot);

            // Optional lock window to prevent immediate crouch/stand churn right after entering this hold reason.
            if (Time.time < goalEnemyPHoldLockUntil)
            {
                return Continue();
            }

            if (stableCoverSignal)
            {
                bool reliableLastKnownShooting = goalEnemy != null && CanShootLastKnownPosition(goalEnemy);
                bool visibleSignal = goalEnemy != null && goalEnemy.IsVisible;
                if (reliableLastKnownShooting || visibleSignal)
                {
                    return new AICoreActionEndStruct("goalEnemyPStableSignal", true);
                }

                // Stable debounced cover signal alone is accepted as a stronger signal than a one-frame cover flip.
                return new AICoreActionEndStruct("goalEnemyPStableCover", true);
            }

            return combatCommon.EndBaseHoldPosition("goalEnemy.P");
        }

        private AICoreActionEndStruct EndDistToBossHoldPosition()
        {
            if (!botOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("notInCover", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null &&
                HasEscortTarget() &&
                !IsEscortCommitStillValid(goalEnemy, GetBossPosition()))
            {
                return new AICoreActionEndStruct("escortInvalid", true);
            }

            if (goalEnemy != null)
            {
                if (goalEnemy.IsVisible &&
                    goalEnemy.Distance < botOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
                {
                    return new AICoreActionEndStruct("enemyCloseAndVisible", true);
                }

                if (goalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out _))
                {
                    return new AICoreActionEndStruct("enemyCanShoot", true);
                }

                if (combatCommon.CanShootFromCurrentCover(out _))
                {
                    return new AICoreActionEndStruct("canShootCover", true);
                }
            }

            return Continue();
        }

        private AICoreActionEndStruct EndGoToPoint()
        {
            AICoreActionEndStruct result = EndBossPointMove();
            if (result.Value)
            {
                lastGoToPointEndTime = Time.time;
            }

            return result;
        }

        private AICoreActionEndStruct EndBossPointMove()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null &&
                HasEscortTarget() &&
                !IsEscortCommitStillValid(goalEnemy, GetBossPosition()))
            {
                return new AICoreActionEndStruct("escortInvalid", true);
            }

            if ((botOwner.GoToSomePointData.Point - GetBossPosition()).sqrMagnitude > bossPointRadius * bossPointRadius)
            {
                return new AICoreActionEndStruct("bossTooFar", true);
            }

            if (botOwner.GoToSomePointData.IsCome())
            {
                return new AICoreActionEndStruct("atPoint", true);
            }

            return combatCommon.EndBaseGoToPoint();
        }

        private AICoreActionEndStruct EndGroupSearchLeader() => EndGroupSearchMove(GroupSearchRole.Leader);
        private AICoreActionEndStruct EndGroupSearchFollower() => EndGroupSearchMove(GroupSearchRole.Follower);

        private AICoreActionEndStruct EndGoToPointTactical(string reason)
        {
            if (string.Equals(reason, "enemySearch", StringComparison.Ordinal))
            {
                return EndEnemySearch();
            }

            return combatCommon.EndBaseGoToPoint();
        }

        private AICoreActionEndStruct EndEnemySearch()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (FollowerCombatCommon.WasHitRecently(botOwner, 0.5f))
            {
                return new AICoreActionEndStruct("enemy.ShotMe", true);
            }

            if (Enemy.Distance(botOwner) <= Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionEndStruct("enemy.Close", true);
            }

            return Continue();
        }

        private AICoreActionEndStruct EndGroupSearchMove(GroupSearchRole expectedRole)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                ClearGroupSearchCommit();
                return new AICoreActionEndStruct("groupSearchEnemyMissing", true);
            }

            if (combatCommon.HasReliablePersonalEnemyLocation(goalEnemy) || (goalEnemy.IsVisible && goalEnemy.CanShoot))
            {
                ClearGroupSearchCommit();
                return new AICoreActionEndStruct("groupSearchEnemyAcquired", true);
            }

            if (!IsGroupSearchCommitStillValid(goalEnemy, GetBossPosition(), expectedRole))
            {
                return new AICoreActionEndStruct("groupSearchCommitInvalid", true);
            }

            if (botOwner.GoToSomePointData.IsCome() ||
                (botOwner.Position - groupSearchPoint).sqrMagnitude <= 2.25f)
            {
                return new AICoreActionEndStruct("groupSearchArrived", true);
            }

            return Continue();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Combat area state
        // ──────────────────────────────────────────────────────────────────────────

        private void UpdateCombatAreaStyle()
        {
            Vector3 bossPosition = GetBossPosition();
            if (!combatAreaInitialized)
            {
                combatAreaCenter = bossPosition;
                combatStyle = FollowerCombatStyle.HangBack;
                combatAreaInitialized = true;
                return;
            }

            if (combatStyle == FollowerCombatStyle.HangBack)
            {
                if ((bossPosition - combatAreaCenter).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
                {
                    combatStyle = FollowerCombatStyle.MoveForward;
                    combatAreaCenter = bossPosition;
                }
                return;
            }

            if ((botOwner.Position - combatAreaCenter).sqrMagnitude <= CombatAreaArrivalDistance * CombatAreaArrivalDistance)
            {
                combatStyle = FollowerCombatStyle.HangBack;
            }
        }

        private bool IsMoveForwardStyle() => combatStyle == FollowerCombatStyle.MoveForward;

        private Vector3 GetBossPosition() => botOwner.BotFollower.BossToFollow?.Position ?? botOwner.Position;

        private void RefreshBossCover()
        {
            if (nextBossCoverCheckTime >= Time.time)
            {
                return;
            }

            Vector3 bossPosition = GetBossPosition();
            nextBossCoverCheckTime = Time.time + 1f;
            CoverSearchData searchData = new CoverSearchData(
                bossPosition,
                botOwner.CoverSearchInfo,
                CoverShootType.hide,
                closeBossSqr,
                0f,
                CoverSearchType.closerToSelectedPoint,
                null,
                null,
                bossPosition,
                ECheckSHootHide.shootAndHide,
                new CoverSearchDefenceDataClass(0f),
                PointsArrayType.byShootType,
                true,
                null,
                null,
                "Default");

            nearBossCoverPoint = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
            haveNearBossCover = nearBossCoverPoint != null &&
                                (bossPosition - nearBossCoverPoint.Position).sqrMagnitude < closeBossSqr &&
                                !nearBossCoverPoint.IsSpotted;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Push helpers
        // ──────────────────────────────────────────────────────────────────────────

        private bool IsPushEnabled() => GetFollowerAggression() > 0.01f;

        private float GetPushEnemyMaxDistance(EnemyInfo? goalEnemy = null)
        {
            float aggression = GetFollowerAggression();
            if (aggression <= 0.001f)
            {
                return PushEnabledMaxEnemyDistance;
            }

            if (goalEnemy != null && combatCommon.IsEnemyLowThreat(false, 2f))
            {
                float aggressiveDistance = PushEnabledMaxEnemyDistance * (aggression * 2f);
                if (combatStyle == FollowerCombatStyle.MoveForward)
                {
                    aggressiveDistance *= 1.2f;
                }
                return aggressiveDistance;
            }

            return PushEnabledMaxEnemyDistance;
        }

        private bool CanLeaveBossForPush()
        {
            if (botOwner.Medecine?.Using == true || botOwner.Medecine?.FirstAid?.Have2Do == true)
            {
                return false;
            }

            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 1.5f))
            {
                return false;
            }

            Vector3 bossPosition = GetBossPosition();
            float aggression = GetFollowerAggression();
            float allowedDistance = Mathf.Sqrt(botOwner.Settings.FileSettings.Boss.MAX_DIST_COVER_BOSS_SQRT);
            if (aggression > 0.001f && botOwner.Memory.GoalEnemy?.Distance <= 100f)
            {
                allowedDistance = Mathf.Lerp(allowedDistance, 100f, aggression * aggression);
            }

            return (botOwner.Position - bossPosition).sqrMagnitude <= allowedDistance * allowedDistance;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Escort helpers
        // ──────────────────────────────────────────────────────────────────────────

        private void CommitEscortCover(EnemyInfo goalEnemy, Vector3 bossPosition, CustomNavigationPoint? coverPoint, bool wantedShootLane)
        {
            if (coverPoint == null)
            {
                return;
            }

            ClearGroupSearchCommit();
            escortTargetType = EscortTargetType.Cover;
            escortCoverPoint = coverPoint;
            escortPoint = coverPoint.Position;
            escortEnemyId = goalEnemy.ProfileId;
            escortBossAnchor = bossPosition;
            escortEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            escortWantedShootLane = wantedShootLane;
        }

        private void CommitEscortPoint(EnemyInfo goalEnemy, Vector3 bossPosition, Vector3 point)
        {
            ClearGroupSearchCommit();
            escortTargetType = EscortTargetType.Point;
            escortCoverPoint = null;
            escortPoint = point;
            escortEnemyId = goalEnemy.ProfileId;
            escortBossAnchor = bossPosition;
            escortEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            escortWantedShootLane = false;
        }

        private void ClearEscortCommit()
        {
            escortTargetType = EscortTargetType.None;
            escortCoverPoint = null;
            escortPoint = Vector3.zero;
            escortEnemyId = null;
            escortBossAnchor = Vector3.zero;
            escortEnemyAnchor = Vector3.zero;
            escortWantedShootLane = false;
        }

        private bool HasEscortTarget() =>
            escortTargetType != EscortTargetType.None && !string.IsNullOrEmpty(escortEnemyId);

        private bool IsEscortCommitStillValid(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            if (!HasEscortTarget() ||
                !string.Equals(escortEnemyId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                ClearEscortCommit();
                return false;
            }

            if ((bossPosition - escortBossAnchor).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
            {
                ClearEscortCommit();
                return false;
            }

            Vector3 currentEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            Vector3 previousEnemyDirection = escortEnemyAnchor - escortBossAnchor;
            previousEnemyDirection.y = 0f;
            Vector3 currentEnemyDirection = currentEnemyAnchor - bossPosition;
            currentEnemyDirection.y = 0f;
            if ((currentEnemyAnchor - escortEnemyAnchor).sqrMagnitude > EscortEnemyMoveReevalDistance ||
                (previousEnemyDirection.sqrMagnitude > 0.01f &&
                 currentEnemyDirection.sqrMagnitude > 0.01f &&
                 Vector3.Angle(previousEnemyDirection, currentEnemyDirection) > EscortEnemyAngleReeval))
            {
                ClearEscortCommit();
                return false;
            }

            if (escortTargetType == EscortTargetType.Cover)
            {
                if (escortCoverPoint == null ||
                    !escortCoverPoint.IsFreeById(botOwner.Id) ||
                    escortCoverPoint.IsSpotted ||
                    (escortCoverPoint.Position - bossPosition).sqrMagnitude > closeBossSqr)
                {
                    ClearEscortCommit();
                    return false;
                }

                if (escortWantedShootLane)
                {
                    ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
                    if (shootPoint != null && !Utils.Utils.CanShootToTarget(shootPoint, escortCoverPoint, botOwner.LookSensor.Mask, false))
                    {
                        ClearEscortCommit();
                        return false;
                    }
                }

                return true;
            }

            if (escortTargetType == EscortTargetType.Point)
            {
                if ((escortPoint - bossPosition).sqrMagnitude > closeBossSqr)
                {
                    ClearEscortCommit();
                    return false;
                }

                return true;
            }

            ClearEscortCommit();
            return false;
        }

        private bool TryFindEscortCover(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            out CustomNavigationPoint? bestPoint,
            out bool bestPointHasShootLane)
        {
            bestPoint = null;
            bestPointHasShootLane = false;

            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = botOwner.LookSensor.Mask;
            const float escortSearchRadius = 22f;

            Func<CustomNavigationPoint, bool> escortEligibility = point =>
            {
                if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                {
                    return false;
                }

                return (point.Position - bossPosition).sqrMagnitude <= closeBossSqr;
            };

            List<CustomNavigationPoint> bossAreaCovers = Covers.GetCoverPoints(
                botOwner,
                bossPosition,
                escortSearchRadius,
                escortEligibility);

            if (bossAreaCovers.Count == 0)
            {
                return false;
            }

            Vector3 enemyDirection = enemyAnchor - bossPosition;
            enemyDirection.y = 0f;
            bool hasEnemyDirection = enemyDirection.sqrMagnitude > 0.01f;
            if (hasEnemyDirection)
            {
                enemyDirection.Normalize();
            }

            bool PointHasShootLane(CustomNavigationPoint point)
            {
                return shootPoint != null && Utils.Utils.CanShootToTarget(shootPoint, point, mask, false);
            }

            bool PointIsHiddenFromEnemy(CustomNavigationPoint point)
            {
                return hasEnemyDirection && point.CanIHideFromPos(0f, true, false, enemyAnchor);
            }

            bool IsFront(CustomNavigationPoint point)
            {
                if (!hasEnemyDirection)
                {
                    return false;
                }

                Vector3 pointDirection = point.Position - bossPosition;
                pointDirection.y = 0f;
                if (pointDirection.sqrMagnitude <= 0.01f)
                {
                    return false;
                }

                pointDirection.Normalize();
                return Vector3.Dot(pointDirection, enemyDirection) >= 0.25f;
            }

            bool IsSide(CustomNavigationPoint point)
            {
                if (!hasEnemyDirection)
                {
                    return false;
                }

                Vector3 pointDirection = point.Position - bossPosition;
                pointDirection.y = 0f;
                if (pointDirection.sqrMagnitude <= 0.01f)
                {
                    return false;
                }

                pointDirection.Normalize();
                float dot = Vector3.Dot(pointDirection, enemyDirection);
                return Mathf.Abs(dot) < 0.35f;
            }

            CustomNavigationPoint? PickByPreference(Func<CustomNavigationPoint, bool> areaPredicate)
            {
                foreach (CustomNavigationPoint point in bossAreaCovers)
                {
                    if (areaPredicate(point) && PointIsHiddenFromEnemy(point))
                    {
                        return point;
                    }
                }

                foreach (CustomNavigationPoint point in bossAreaCovers)
                {
                    if (areaPredicate(point))
                    {
                        return point;
                    }
                }

                return null;
            }

            bestPoint = PickByPreference(IsFront);
            bestPoint ??= PickByPreference(IsSide);
            bestPoint ??= PickByPreference(_ => true);

            if (bestPoint == null)
            {
                return false;
            }

            bestPointHasShootLane = PointHasShootLane(bestPoint);
            return true;
        }

        private Vector3 GetEscortEnemyAnchor(EnemyInfo goalEnemy)
        {
            Vector3 enemyAnchor = goalEnemy.CurrPosition;
            if ((enemyAnchor - botOwner.Position).sqrMagnitude > 0.01f)
            {
                return enemyAnchor;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Group search helpers
        // ──────────────────────────────────────────────────────────────────────────

        private void CommitGroupSearchLeader(EnemyInfo goalEnemy, Vector3 bossPosition, Vector3 targetPoint)
        {
            ClearEscortCommit();
            groupSearchRole = GroupSearchRole.Leader;
            groupSearchEnemyId = goalEnemy.ProfileId;
            groupSearchLeaderProfileId = botOwner.ProfileId;
            groupSearchBossAnchor = bossPosition;
            groupSearchEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            groupSearchLeaderAnchor = botOwner.Position;
            groupSearchPoint = targetPoint;
        }

        private void CommitGroupSearchFollower(EnemyInfo goalEnemy, Vector3 bossPosition, BotOwner leader, Vector3 targetPoint)
        {
            ClearEscortCommit();
            groupSearchRole = GroupSearchRole.Follower;
            groupSearchEnemyId = goalEnemy.ProfileId;
            groupSearchLeaderProfileId = leader.ProfileId;
            groupSearchBossAnchor = bossPosition;
            groupSearchEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            groupSearchLeaderAnchor = leader.Position;
            groupSearchPoint = targetPoint;
        }

        private void ClearGroupSearchCommit()
        {
            groupSearchRole = GroupSearchRole.None;
            groupSearchEnemyId = null;
            groupSearchLeaderProfileId = null;
            groupSearchBossAnchor = Vector3.zero;
            groupSearchEnemyAnchor = Vector3.zero;
            groupSearchLeaderAnchor = Vector3.zero;
            groupSearchPoint = Vector3.zero;
        }

        private bool IsGroupSearchCommitStillValid(EnemyInfo goalEnemy, Vector3 bossPosition, GroupSearchRole expectedRole)
        {
            if (groupSearchRole != expectedRole ||
                expectedRole == GroupSearchRole.None ||
                string.IsNullOrEmpty(groupSearchEnemyId) ||
                !string.Equals(groupSearchEnemyId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                ClearGroupSearchCommit();
                return false;
            }

            if ((bossPosition - groupSearchBossAnchor).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
            {
                ClearGroupSearchCommit();
                return false;
            }

            Vector3 currentEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            Vector3 previousEnemyDirection = groupSearchEnemyAnchor - groupSearchBossAnchor;
            previousEnemyDirection.y = 0f;
            Vector3 currentEnemyDirection = currentEnemyAnchor - bossPosition;
            currentEnemyDirection.y = 0f;
            if ((currentEnemyAnchor - groupSearchEnemyAnchor).sqrMagnitude > EscortEnemyMoveReevalDistance ||
                (previousEnemyDirection.sqrMagnitude > 0.01f &&
                 currentEnemyDirection.sqrMagnitude > 0.01f &&
                 Vector3.Angle(previousEnemyDirection, currentEnemyDirection) > EscortEnemyAngleReeval))
            {
                ClearGroupSearchCommit();
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                ClearGroupSearchCommit();
                return false;
            }

            BotOwner? currentLeader = FollowerGroupSearchRuntime.GetCurrentLeader(boss, goalEnemy.ProfileId);
            if (expectedRole == GroupSearchRole.Leader)
            {
                if (currentLeader == null || currentLeader.ProfileId != botOwner.ProfileId)
                {
                    ClearGroupSearchCommit();
                    return false;
                }
            }
            else
            {
                if (currentLeader == null ||
                    currentLeader.IsDead ||
                    string.IsNullOrEmpty(groupSearchLeaderProfileId) ||
                    currentLeader.ProfileId != groupSearchLeaderProfileId ||
                    (currentLeader.Position - groupSearchLeaderAnchor).sqrMagnitude > 16f * 16f ||
                    !CanJoinGroupSearchLeader(currentLeader, bossPosition, currentEnemyAnchor))
                {
                    ClearGroupSearchCommit();
                    return false;
                }
            }

            return true;
        }

        private bool CanGroupSearch(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                goalEnemy.IsVisible ||
                goalEnemy.CanShoot ||
                !goalEnemy.CanISearch ||
                combatCommon.HaveCoverToShoot ||
                CanShootLastKnownPosition(goalEnemy) ||
                !botOwner.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                FollowerCombatCommon.WasHitRecently(botOwner, 10f))
            {
                return false;
            }

            if (combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            return botOwner.Memory.LastEnemyVisionOld(LocalBotSettingsProviderClass.Core.COVER_SECONDS_AFTER_LOSE_VISION);
        }

        private bool IsValidGroupSearchCandidate(BotOwner owner, EnemyInfo goalEnemy)
        {
            if (owner == null ||
                owner.IsDead ||
                owner.BotState != EBotState.Active ||
                owner.GetPlayer?.HealthController?.IsAlive != true ||
                owner.BotFollower?.HaveBoss != true ||
                owner.Memory?.GoalEnemy == null ||
                owner.Memory.GoalEnemy.ProfileId != goalEnemy.ProfileId)
            {
                return false;
            }

            return !owner.Memory.GoalEnemy.IsVisible &&
                   !owner.Memory.GoalEnemy.CanShoot &&
                   owner.Memory.GoalEnemy.CanISearch;
        }

        private bool TryGetGroupSearchLeaderPoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            CustomNavigationPoint? coverPoint = Covers.GetClosestCoverPointTowardPoint(
                botOwner,
                botOwner.Position,
                enemyAnchor,
                25f,
                cover => !cover.IsSpotted && cover.IsFreeById(botOwner.Id));
            if (coverPoint != null)
            {
                point = coverPoint.Position;
                return true;
            }

            if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit navMeshHit, 4f, -1))
            {
                point = navMeshHit.position;
                return true;
            }

            point = enemyAnchor;
            return point != Vector3.zero;
        }

        private bool TryGetGroupSearchFollowerPoint(Vector3 leaderPosition, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(leaderPosition, out NavMeshHit leaderHit, 3f, -1))
            {
                return false;
            }

            Vector3 leaderDirection = botOwner.Position - leaderHit.position;
            leaderDirection.y = 0f;
            if (leaderDirection.sqrMagnitude <= 0.01f)
            {
                leaderDirection = -botOwner.LookDirection;
                leaderDirection.y = 0f;
            }

            if (leaderDirection.sqrMagnitude <= 0.01f)
            {
                leaderDirection = Vector3.back;
            }

            leaderDirection = leaderDirection.normalized * 2f;
            if (NavMesh.Raycast(leaderHit.position, leaderHit.position + leaderDirection, out NavMeshHit rayHit, -1))
            {
                point = rayHit.position;
                return true;
            }

            point = leaderHit.position + leaderDirection;
            return true;
        }

        private bool CanJoinGroupSearchLeader(BotOwner leader, Vector3 bossPosition, Vector3 enemyAnchor)
        {
            if (leader == null || leader.IsDead)
            {
                return false;
            }

            Vector3 leaderOffset = leader.Position - botOwner.Position;
            leaderOffset.y = 0f;
            float joinDistance = GetGroupSearchJoinDistance(bossPosition, enemyAnchor);
            if (leaderOffset.sqrMagnitude > joinDistance * joinDistance)
            {
                return false;
            }

            Vector3 enemyDirection = enemyAnchor - bossPosition;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            Vector3 myBossOffset = botOwner.Position - bossPosition;
            myBossOffset.y = 0f;
            Vector3 leaderBossOffset = leader.Position - bossPosition;
            leaderBossOffset.y = 0f;
            if (myBossOffset.sqrMagnitude <= 4f || leaderBossOffset.sqrMagnitude <= 4f)
            {
                return true;
            }

            float myAngle = Vector3.Angle(enemyDirection, myBossOffset);
            float leaderAngle = Vector3.Angle(enemyDirection, leaderBossOffset);
            return Mathf.Abs(myAngle - leaderAngle) <= GroupSearchSectorAngleTolerance;
        }

        private float GetGroupSearchJoinDistance(Vector3 bossPosition, Vector3 enemyAnchor)
        {
            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            float scaledDistance = bossToEnemy.magnitude * 0.35f;
            return Mathf.Clamp(scaledDistance, GroupSearchJoinDistanceMin, GroupSearchJoinDistanceMax);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Hold / heal helpers
        // ──────────────────────────────────────────────────────────────────────────

        private bool ShouldStartCoverHeal(EnemyInfo? goalEnemy)
        {
            if (!HasPendingCoverHealWork())
            {
                return false;
            }

            if (ShouldHealImmediatelyInCover())
            {
                return true;
            }

            if (goalEnemy != null && (goalEnemy.IsVisible || goalEnemy.CanShoot))
            {
                return false;
            }

            float latestThreatSeenTime = GetLatestThreatSeenTime(goalEnemy);
            if (latestThreatSeenTime <= 0f)
            {
                return true;
            }

            float requiredQuietTime = botOwner.Settings.FileSettings.Mind.PROTECT_DELTA_HEAL_SEC + GetAdditionalHealDelay();
            return Time.time - latestThreatSeenTime > requiredQuietTime;
        }

        private bool HasPendingCoverHealWork() =>
            botOwner.Medecine.FirstAid.Have2Do || botOwner.Medecine.SurgicalKit.HaveWork;

        private bool ShouldHealImmediatelyInCover()
        {
            if (botOwner.Medecine.FirstAid.IsBleeding)
            {
                return true;
            }

            return combatCommon.IsFollowerCriticallyWounded();
        }

        private float GetAdditionalHealDelay() =>
            botOwner.Medecine.SurgicalKit.HaveWork
                ? PostCombatHealDelay * SurgeryHealDelayMultiplier
                : PostCombatHealDelay;

        private float GetLatestThreatSeenTime(EnemyInfo? goalEnemy)
        {
            float latestThreatSeenTime = Mathf.Max(botOwner.Memory.LastEnemyTimeSeen, botOwner.BotsGroup.EnemyLastSeenTimeReal);
            if (goalEnemy != null)
            {
                latestThreatSeenTime = Mathf.Max(latestThreatSeenTime, goalEnemy.PersonalSeenTime);
            }

            return latestThreatSeenTime;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Retreat / cover assignment
        // ──────────────────────────────────────────────────────────────────────────

        private bool CanShootLastKnownPosition(EnemyInfo enemyInfo)
        {
            Vector3 target = enemyInfo.EnemyLastPositionReal + Vector3.up * 1.6f;
            return !Physics.Linecast(botOwner.WeaponRoot.position, target, out _, LayerMaskClass.HighPolyWithTerrainMask);
        }

        private BotLogicDecision HoldOrCover() =>
            botOwner.Memory.IsInCover ? BotLogicDecision.holdPosition : BotLogicDecision.goToCoverPoint;

        // ──────────────────────────────────────────────────────────────────────────
        // Grenade
        // ──────────────────────────────────────────────────────────────────────────

        private bool TryActivateFollowerGrenade(EnemyInfo goalEnemy)
        {
            if (!friendlySAIN.botGrenades.Value ||
                goalEnemy == null ||
                !goalEnemy.IsVisible ||
                goalEnemy.Person == null ||
                goalEnemy.Distance < 15f ||
                goalEnemy.Distance > 28f ||
                botOwner.WeaponManager == null ||
                botOwner.WeaponManager.IsMelee ||
                botOwner.WeaponManager.Grenades == null ||
                !botOwner.WeaponManager.Grenades.HaveGrenade ||
                botOwner.BotRequestController == null ||
                botOwner.BotRequestController.HaveActivatedRequests() ||
                botOwner.Medecine.Using)
            {
                return false;
            }

            if (!FollowerGrenadeCooldowns.TryReserveThrow(botOwner))
            {
                return false;
            }

            if (combatCommon.IsDogFightActive() ||
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 2f) ||
                Time.time - goalEnemy.FirstTimeSeen < 1.5f)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                return false;
            }

            if (goalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                return false;
            }

            Vector3 targetPosition = goalEnemy.CurrPosition + Vector3.up;
            if (IsFriendlyTooCloseToGrenadeTarget(targetPosition, 8f))
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                return false;
            }

            // Open a short explicit throw window only for our own throw request flow.
            FollowerGrenadeRuntimeGate.AllowThrowWindow(botOwner, 2.5f);

            bool activated = botOwner.BotRequestController.TryActivateThrowGrenadeRequest(targetPosition, null, out _);
            if (!activated)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
            }

            return activated;
        }

        private bool IsFriendlyTooCloseToGrenadeTarget(Vector3 targetPosition, float unsafeRadius)
        {
            float unsafeRadiusSqr = unsafeRadius * unsafeRadius;

            if (botFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            Player bossPlayer = boss.realPlayer;
            if (bossPlayer != null && (bossPlayer.Position - targetPosition).sqrMagnitude <= unsafeRadiusSqr)
            {
                return true;
            }

            List<BotOwner> followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == botOwner || follower.IsDead)
                {
                    continue;
                }

                if ((follower.Position - targetPosition).sqrMagnitude <= unsafeRadiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Cover / shoot queries
        // ──────────────────────────────────────────────────────────────────────────

        private bool EnemyPathCrossesRecentDoor(EnemyInfo enemy)
        {
            NavMeshDoorLink nearestDoor = botOwner.NearDoorData.GetNearestDoor();
            if (nearestDoor == null)
            {
                return false;
            }

            Vector3 from = botOwner.Transform.position;
            Vector3 to = enemy.CurrPosition;
            GClass365 segment = new GClass365(from, to);
            Vector3 delta = nearestDoor.SegmentOpen.b - nearestDoor.SegmentOpen.a;
            Vector3 a = nearestDoor.SegmentOpen.a - delta * 0.1f;
            Vector3 b = nearestDoor.SegmentOpen.b + delta * 0.1f;
            return GClass369.GetCrossPoint(segment.a, segment.b, a, b) != null;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // General-purpose queries
        // ──────────────────────────────────────────────────────────────────────────

        private bool ShouldAttackImmediately(EnemyInfo goalEnemy) =>
            botOwner.Memory.AttackImmediately ||
            (goalEnemy.IsVisible && goalEnemy.CanShoot && Time.time - goalEnemy.PersonalSeenTime < 2f);

        private bool UpdateDebouncedHoldCoverSignal(bool activeSignal)
        {
            if (!activeSignal)
            {
                holdCoverSignalActive = false;
                holdCoverSignalSince = 0f;
                return false;
            }

            if (!holdCoverSignalActive)
            {
                holdCoverSignalActive = true;
                holdCoverSignalSince = Time.time;
                return false;
            }

            return Time.time - holdCoverSignalSince >= HoldCoverSignalDebounceSeconds;
        }

        private void ResetHoldCoverSignal()
        {
            holdCoverSignalActive = false;
            holdCoverSignalSince = 0f;
        }

        private float GetFollowerAggression()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            float aggr = followerData?.CombatAggression ?? 50f;
            return Mathf.Clamp01(aggr / 100f);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Trivial helpers
        // ──────────────────────────────────────────────────────────────────────────

        private static AICoreActionEndStruct Continue() => default;
    }
}
