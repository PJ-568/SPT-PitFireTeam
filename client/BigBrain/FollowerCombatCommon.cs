using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using Comfort.Common;
using UnityDiagnostics;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatCommon
    {
        private const float StartSupportSuppressDistance = 30f;
        private const float ShootCoverSuperiorNavImprovementFactor = 0.7f;
        private const float StableShootCoverRefreshInterval = 1.2f;
        private const float UnstableShootCoverRefreshInterval = 0.6f;
        private const float HaveCoverToShootDebounceSeconds = 0.15f;
        private const float ShootLaneUpgradeHysteresisSeconds = 0.2f;
        private const float PointToShootUpdateMinDistance = 1.5f;
        private const float WeakEnemyPushDefaultMaxDistance = 80f;
        private const float WeakEnemyPushProtectorMaxDistance = 60f;
        private const float WeakEnemyPushMarksmanMaxDistance = 150f;
        private const float WeakEnemyPushBossDistanceBuffer = 12f;
        private const float WeakEnemyPushMaxRoleThreatMultiplier = 1.1f;
        private const float StableVisibleImmediateFireSeconds = 0.3f;
        private const float CoverCommitLockSeconds = 2.5f;
        private const float CoverSearchCooldownSeconds = 0.35f;
        private const float RunToCoverProgressMinDistance = 0.35f;
        private const float RunToCoverStallSeconds = 4f;
        private const float TacticalPointProgressMinDistance = 0.35f;
        private const float TacticalPointStallSeconds = 4f;
        private const float TacticalPointArrivalDistance = 1.25f;
        private const float StandingCoverShotProbeHeight = 1.45f;
        private const float HealCoverMinNavDistance = 2f;
        private const float HealCoverMinEnemyDistanceGain = -2f;
        private const float EnemyFrontCrossGuardMaxDistance = 35f;
        private const float BossFireLaneCandidateRadius = 0.9f;
        private const float BossFireLanePathRadius = 1.1f;
        private const float BossFireLaneStartPadding = 0.75f;
        private const float BossFireLaneEndPadding = 2f;
        private const float BossFireLaneSoftPenalty = 24f;
        private const float FireSupportPathEnemyMinDistance = 12f;
        private const float DogFightOutOfRangeCooldownSeconds = 1.25f;
        private const float PointBlankRetreatBlockDistance = 8f;
        private const float PointBlankContactDogFightDistance = 3f;
        private const float PointBlankContactMaxAnchorDistance = 4.5f;
        private const float CloseVisibleThreatBreakDistance = 18f;
        private const float CloseVisibleDogFightStartDistance = 15f;
        private const float CloseVisibleDogFightEndDistance = 15f;
        private const float CloseThreatDogFightDistance = 8f;
        private const float CloseThreatAdvanceBreakDistance = 18f;
        private const float CloseThreatRecentSeenSeconds = 0.75f;
        private const float ReloadRetreatThreatDistance = 18f;
        private const float ReloadRetreatAmmoRatio = 0.25f;
        private const int ReloadRetreatMinMagazineAmmo = 5;
        private const float NoSprintHealSuppressRecentSeenSeconds = 3f;
        private const float HealCoverStallBlacklistSeconds = 10f;
        private const float HealHidePointMinDistance = 4f;
        private const float HealHidePointMaxNavDistance = 35f;
        private const float HealHidePointEnemyDistanceGain = -1f;
        private const float DefaultCommittedCoverHoldSeconds = 3f;
        private const float RetreatCommittedCoverHoldSeconds = 3.5f;
        private const float ShootCommittedCoverHoldSeconds = 2.5f;
        private const float BossCommittedCoverHoldSeconds = 3f;
        private const float DefaultCommittedPositionHoldSeconds = 1.25f;
        private const float HealingCommittedHoldSeconds = 12f;
        private const float DefaultFireWhileMovingPushVisibleBreakSeconds = 0.6f;
        private const float ShootFromCoverLosFlickerGraceSeconds = 0.5f;
        private const float AutoSuppressMinSeconds = 0.75f;
        private const float AutoSuppressMaxSeconds = 3f;
        private const float OrderedSuppressMinSeconds = 2f;
        private const float CloseSuppressFoliageProbeRadius = 0.45f;
        private const float CloseSuppressRecentContactSeconds = 0.6f;
        private const float AutonomousRegroupRecentFightGraceSeconds = 4f;
        private const float AutonomousRegroupExtremeDistanceMultiplier = 1.6f;
        private const float SuppressFromMinDistance = 3f;
        private const float SuppressFromSearchRadius = 35f;
        private static readonly string[] DefaultBossObjectiveCoverBreakReasons =
        {
            "coverHold",
            "bossHold",
            "bossHold.open",
            "shootCover",
            "safeCover",
            "retreatShootCover",
            "retreatSafeCover",
            "bossCover",
            "committedFire"
        };
        private static readonly Dictionary<int, CoverCommitIntent> coverCommitIntents = new Dictionary<int, CoverCommitIntent>();
        private readonly BotOwner botOwner;
        private readonly List<MedsItemClass> stimSearchBuffer = new List<MedsItemClass>();
        private readonly Collider[] closeSuppressFoliageBuffer = new Collider[8];

        // Shared commitment state. Tactics decide why a commitment should break; common only
        // stores the latch, validates basic enemy/arrival state, and hands the latched decision
        // back to the tactic router. Keep new cross-tactic latches here instead of duplicating
        // them in Default/Sniper.
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? initialDecision;
        private float healBlockUntil;
        private float healStartedAt;
        private float stimStartedAt;
        private float nextCombatHealWorkRefreshAt;
        private CustomNavigationPoint? committedHealCover;
        private Vector3 committedHealPoint;
        private bool hasCommittedHealPoint;
        private BotLogicDecision committedHealMoveAction;
        private string? committedHealMoveReason;
        private int blockedHealCoverId = -1;
        private float blockedHealCoverUntil;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedGrenadeDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPushDecision;
        private string? committedPushEnemyProfileId;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedMovementDecision;
        private string? committedMovementEnemyProfileId;
        private Vector3 committedMovementTarget;
        private int? committedMovementCoverId;
        private string? lastFollowerGrenadeRejectReason;
        private float nextFollowerGrenadeRejectRecordAt;

        private CustomNavigationPoint? committedCoverPoint;

        private CustomNavigationPoint? committedHoldCoverPoint;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPositionDecision;
        private Vector3? committedPosition;
        private float committedPointTimer = 0f;
        private float committedPointSetAt = 0f;
        private string? committedPointReason;
        private BotLogicDecision committedCoverMoveAction;
        private string? committedCoverMoveReason;
        private float committedCoverUntil;
        private float committedCoverSetAt;
        private float nextCoverAcquireTime;
        private int runToCoverProgressCoverId = -1;
        private float runToCoverBestDistance = float.MaxValue;
        private float runToCoverLastProgressTime;
        private Vector3 tacticalPointProgressTarget;
        private float tacticalPointBestDistance = float.MaxValue;
        private float tacticalPointLastProgressTime;
        private bool holdActive;
        private float holdEndTime;
        private string? activeFollowerSuppressReason;
        private float activeFollowerSuppressStartedAt;
        private float shootFromCoverGraceUntil;

        private float dangerTimer = 0f;
        private float nextShootCoverCheckTime;
        private float nextClosestShootCoverCheckTime;
        private float nextApproachableCoverCheckTime;
        private float dangerIgnoreEquipTimer = 0f;
        private bool dangerResult = false;
        private bool dangerIgnoreEquipResult = false;
        private CustomNavigationPoint? cachedClosestShootCover;
        private float inCoverSince = 0f;
        private bool pendingHaveCoverToShoot;
        private float pendingHaveCoverToShootSince;
        private float shootLaneUpgradeSince;
        private float dogFightBlockedUntil;
        private float runToEnemyBlockedUntil;

        private readonly struct CoverCommitIntent
        {
            public CoverCommitIntent(int coverId, bool isShootingCover)
            {
                CoverId = coverId;
                IsShootingCover = isShootingCover;
            }

            public int CoverId { get; }
            public bool IsShootingCover { get; }
        }

        private enum CoverSearchIntent
        {
            Attack,
            AttackMoving,
            RunToCover,
            ForCover
        }

        public FollowerCombatCommon(BotOwner botOwner)
        {
            this.botOwner = botOwner;
        }

        public bool HasInitialDecision => initialDecision.HasValue;

        public void ClearInitialDecision()
        {
            initialDecision = null;
        }

        public void SetInitialDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            initialDecision = decision;
        }

        private CoverSearchType SetCoverTacticAndGetSearchType(
            BotsGroup.BotCurrentTactic tactic,
            CoverShootType shootType,
            CoverSearchIntent searchIntent)
        {
            SetCoverTactic(tactic);

            return searchIntent switch
            {
                CoverSearchIntent.Attack => botOwner.Tactic.SubTactic.SearchTypeAttack(shootType),
                CoverSearchIntent.AttackMoving => botOwner.Tactic.SubTactic.SearchTypeAttackMoving(shootType),
                CoverSearchIntent.RunToCover => botOwner.Tactic.SubTactic.SearchRunToCover(shootType),
                CoverSearchIntent.ForCover => botOwner.Tactic.SubTactic.SearchTypeForCover(shootType),
                _ => botOwner.Tactic.SubTactic.SearchTypeForCover(shootType),
            };
        }

        private void SetCoverTactic(BotsGroup.BotCurrentTactic tactic)
        {
            if (botOwner.Tactic.ShallReturnToAttack && tactic != BotsGroup.BotCurrentTactic.Ambush)
            {
                botOwner.Tactic.ShallReturnToAttack = false;
                botOwner.Tactic.ReturnToAttackTime = 0f;
            }

            botOwner.Tactic.SetTactic(tactic);
        }

        public void Reset()
        {
            initialDecision = null;
            healBlockUntil = 0f;
            healStartedAt = 0f;
            stimStartedAt = 0f;
            nextCombatHealWorkRefreshAt = 0f;
            committedHealCover = null;
            committedHealPoint = Vector3.zero;
            hasCommittedHealPoint = false;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
            blockedHealCoverId = -1;
            blockedHealCoverUntil = 0f;
            ClearCommittedGrenade();
            ClearCommittedPushDecision();
            ClearCommittedMovement();
            ClearCommittedPosition();
            ResetCommittedCover();
            holdActive = false;
            holdEndTime = 0f;
            activeFollowerSuppressReason = null;
            activeFollowerSuppressStartedAt = 0f;
            HaveCoverToShoot = false;
            PointToShoot = null;
            cachedClosestShootCover = null;
            nextClosestShootCoverCheckTime = 0f;
            nextApproachableCoverCheckTime = 0f;
            pendingHaveCoverToShoot = false;
            pendingHaveCoverToShootSince = 0f;
            shootLaneUpgradeSince = 0f;
            dogFightBlockedUntil = 0f;
            runToEnemyBlockedUntil = 0f;
        }

        public void RepairGoalEnemyMemory()
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.CurrPosition)
                ? goalEnemy.CurrPosition
                : goalEnemy.PersonalLastPos;

            if (!IsFinite(enemyPosition) || enemyPosition.sqrMagnitude <= 0.01f)
            {
                return;
            }

            Enemy.RepairPersonalMemory(
                goalEnemy,
                enemyPosition,
                goalEnemy.HaveSeen || goalEnemy.IsVisible || goalEnemy.CanShoot);
        }

        public void HandleSharedDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            BotLogicDecision action = nextDecision.Action;
            if (action != BotLogicDecision.shootFromStationary &&
                action != BotLogicDecision.debugStationary &&
                action != BotLogicDecision.debugStationaryInstantTake &&
                botOwner.WeaponManager.Stationary.Taken)
            {
                botOwner.WeaponManager.Stationary.DropCurWeapon(false, true);
            }
        }

        /// <summary>
        /// Tracks decisions that should keep using the same selected cover instead of re-picking.
        /// </summary>
        public void HandleCommittedCoverDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            BotLogicDecision action = nextDecision.Action;
            if (IsCoverAffinedDecision(action) && botOwner.Memory?.CurCustomCoverPoint != null)
            {
                CommitCover(botOwner.Memory.CurCustomCoverPoint, action, nextDecision.Reason);
            }

            if (!IsCoverAffinedDecision(action) && committedCoverUntil < Time.time)
            {
                ClearCommittedCover();
            }
        }

        public void HandleFollowerSuppressDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (nextDecision.Action == BotLogicDecision.suppressFire && IsFollowerSuppressReason(nextDecision.Reason))
            {
                if (!string.Equals(activeFollowerSuppressReason, nextDecision.Reason, StringComparison.Ordinal))
                {
                    activeFollowerSuppressReason = nextDecision.Reason;
                    activeFollowerSuppressStartedAt = Time.time;
                }

                return;
            }

            ClearFollowerSuppressState();
        }

        public void ClearFollowerSuppressState()
        {
            activeFollowerSuppressReason = null;
            activeFollowerSuppressStartedAt = 0f;
        }

        public void CommitPushDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedPushDecision = decision;
            committedPushEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "push",
                "commit",
                decision.Reason,
                decision);
        }

        public void ClearCommittedPushDecision(string? reason = null)
        {
            if (committedPushDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "push",
                    "clear",
                    reason ?? "clear",
                    committedPushDecision);
            }

            committedPushDecision = null;
            committedPushEnemyProfileId = null;
        }

        public bool HasCommittedPushDecision()
        {
            return committedPushDecision.HasValue;
        }

        public bool TryGetCommittedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedPushDecision.HasValue)
            {
                return false;
            }

            if (!HasActiveCombatEnemy(goalEnemy) &&
                !TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                !string.Equals(goalEnemy.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            decision = committedPushDecision.Value;
            return true;
        }

        public bool TryRestoreCommittedPushEnemy(out EnemyInfo? goalEnemy)
        {
            goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!committedPushDecision.HasValue)
            {
                return false;
            }

            if (!FollowerContactEnemyRetention.TryRestore(botOwner, out EnemyInfo? restored) || restored == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                !string.Equals(restored.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            goalEnemy = restored;
            return IsTrackedEnemyAlive(restored);
        }

        public void RefreshCommittedPushEnemyRetention()
        {
            if (!committedPushDecision.HasValue)
            {
                return;
            }

            FollowerContactEnemyRetention.RegisterCurrentGoal(botOwner, prioritized: true);
        }

        public bool IsCommittedPushEnemyChanged(EnemyInfo goalEnemy)
        {
            return !string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                   !string.Equals(goalEnemy.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal);
        }

        public void CommitGrenadeDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedGrenadeDecision = decision;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "grenade",
                "commit",
                decision.Reason,
                decision);
        }

        public void ClearCommittedGrenade(string? reason = null)
        {
            if (committedGrenadeDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "grenade",
                    "clear",
                    reason ?? "clear",
                    committedGrenadeDecision);
            }

            committedGrenadeDecision = null;
        }

        public bool TryGetCommittedGrenadeDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedGrenadeDecision.HasValue)
            {
                return false;
            }

            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            BotRequest? currentRequest = botOwner.BotRequestController?.CurRequest;
            bool grenadeSequenceActive =
                grenades != null &&
                (grenades.ThrowindNow || grenades.ReadyToThrow);
            bool grenadeRequestActive =
                currentRequest?.BotRequestType == BotRequestType.throwGrenade ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenadeFromPlace;
            bool suppressActive = botOwner.SuppressGrenade != null && !botOwner.SuppressGrenade.Complete;

            if (!grenadeSequenceActive && !grenadeRequestActive && !suppressActive)
            {
                ClearCommittedGrenade("inactive");
                return false;
            }

            decision = committedGrenadeDecision.Value;
            return true;
        }

        public void CommitMovement(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedMovementDecision = decision;
            committedMovementEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
            committedMovementTarget = Vector3.zero;
            committedMovementCoverId = null;

            if (committedCoverPoint != null)
            {
                committedMovementTarget = committedCoverPoint.Position;
                committedMovementCoverId = committedCoverPoint.Id;
            }
            else if (botOwner.GoToSomePointData?.HaveTarget() == true &&
                     IsFinite(botOwner.GoToSomePointData.Point))
            {
                committedMovementTarget = botOwner.GoToSomePointData.Point;
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "movement",
                "commit",
                decision.Reason,
                decision,
                IsFinite(committedMovementTarget) ? committedMovementTarget : null,
                committedMovementCoverId);
        }

        public void ClearCommittedMovement(string? reason = null)
        {
            if (committedMovementDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "movement",
                    "clear",
                    reason ?? "clear",
                    committedMovementDecision,
                    IsFinite(committedMovementTarget) ? committedMovementTarget : null,
                    committedMovementCoverId);
            }

            committedMovementDecision = null;
            committedMovementEnemyProfileId = null;
            committedMovementTarget = Vector3.zero;
            committedMovementCoverId = null;
        }

        public bool HasCommittedMovement()
        {
            return committedMovementDecision.HasValue;
        }

        public bool IsSameCommittedMovement(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return committedMovementDecision.HasValue &&
                   committedMovementDecision.Value.Action == decision.Action &&
                   string.Equals(committedMovementDecision.Value.Reason, decision.Reason, StringComparison.Ordinal);
        }

        public bool ShouldCommitMovementDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool isPushDecision)
        {
            return IsMovementDecision(decision) &&
                   !isPushDecision &&
                   !IsCommittedHolderReason(decision.Reason);
        }

        public bool TryGetCommittedMovementDecision(
            EnemyInfo goalEnemy,
            bool hasExplicitRegroupOrder,
            bool hasActivePushOrder,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedMovementDecision.HasValue)
            {
                return false;
            }

            if (!HasActiveCombatEnemy(goalEnemy) ||
                hasExplicitRegroupOrder ||
                hasActivePushOrder)
            {
                ClearCommittedMovement(!HasActiveCombatEnemy(goalEnemy) ? "enemyMissing" : hasExplicitRegroupOrder ? "explicitRegroup" : "activePushOrder");
                return false;
            }

            if (!string.IsNullOrEmpty(committedMovementEnemyProfileId) &&
                !string.Equals(goalEnemy.ProfileId, committedMovementEnemyProfileId, StringComparison.Ordinal))
            {
                ClearCommittedMovement("enemyChanged");
                return false;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> committed = committedMovementDecision.Value;
            if (ShouldInterruptCommittedMovement(goalEnemy, committed, hasActivePushOrder) ||
                HasCommittedMovementArrived(committed))
            {
                ClearCommittedMovement(HasCommittedMovementArrived(committed) ? "arrived" : "interrupted");
                return false;
            }

            decision = committed;
            return true;
        }

        private bool ShouldInterruptCommittedMovement(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool hasActivePushOrder)
        {
            if (HasImmediateExplosiveDanger())
            {
                return true;
            }

            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.5f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance())
            {
                return true;
            }

            if (ShouldBreakForBossUnderAttack(goalEnemy, hasActivePushOrder))
            {
                return true;
            }

            return decision.Action == BotLogicDecision.runToEnemy &&
                   !CanSprintForCombatMovement();
        }

        public bool HasImmediateExplosiveDanger()
        {
            if (botOwner == null)
            {
                return false;
            }

            if (botOwner.BewareGrenade?.ShallRunAway() == true ||
                botOwner.BewareBTR?.ShallRunAway() == true)
            {
                return true;
            }

            BotLogicDecision currentDecision = botOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return currentDecision == BotLogicDecision.runAwayGrenade ||
                   currentDecision == BotLogicDecision.runAwayBTR;
        }

        private bool HasCommittedMovementArrived(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action switch
            {
                BotLogicDecision.runToCover => IsAtCommittedMovementDestination() ||
                                               botOwner.GoToSomePointData?.IsCome() == true,
                BotLogicDecision.goToPoint or BotLogicDecision.goToPointTactical => botOwner.GoToSomePointData?.IsCome() == true,
                BotLogicDecision.attackMoving or BotLogicDecision.attackMovingWithSuppress => IsAtCommittedMovementDestination(),
                var action when action == (BotLogicDecision)CustomBotDecisions.attackRetreat => IsAtCommittedMovementDestination(),
                BotLogicDecision.runToEnemy or BotLogicDecision.goToEnemy => botOwner.Memory.GoalEnemy?.IsVisible == true &&
                                                                             botOwner.Memory.GoalEnemy.CanShoot,
                _ => false,
            };
        }

        private bool IsAtCommittedMovementDestination()
        {
            if (committedMovementCoverId.HasValue &&
                IsBotInCommittedCover())
            {
                return true;
            }

            if (committedMovementCoverId.HasValue &&
                botOwner.Memory?.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedMovementCoverId.Value)
            {
                return true;
            }

            if (IsFinite(committedMovementTarget) &&
                committedMovementTarget.sqrMagnitude > 0.01f &&
                (botOwner.Position - committedMovementTarget).sqrMagnitude <= 2f * 2f)
            {
                return true;
            }

            return botOwner.GoToSomePointData?.IsCome() == true;
        }


        public bool HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (committedPointTimer <= Time.time)
            {
                ClearCommittedPosition("expired");
                return false;
            }

            if (!committedPositionDecision.HasValue)
            {
                ClearCommittedPosition("missingDecision");
                return false;
            }

            if (!committedPosition.HasValue && committedHoldCoverPoint == null)
            {
                ClearCommittedPosition("missingTarget");
                return false;
            }

            if (ShouldBreakCommittedPositionHold())
            {
                ClearCommittedPosition("break");
                return false;
            }

            if (committedHoldCoverPoint != null)
            {
                if (!IsCommittedHoldCoverStillValid())
                {
                    ClearCommittedPosition("coverInvalid");
                    return false;
                }
            }

            HoldFor(Mathf.Max(0.1f, committedPointTimer - Time.time));
            decision = committedPositionDecision.Value;
            return true;
        }

        public bool InHoldingCover(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return HasCommittedPosition(out decision) && committedHoldCoverPoint != null;
        }

        public bool InHoldingPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return HasCommittedPosition(out decision) && committedPosition != null;
        }

        public bool IsCommittedHolderReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("committedCoverHold", StringComparison.Ordinal) ||
                    reason.StartsWith("committedPositionHold", StringComparison.Ordinal));
        }

        public bool IsCommittedHolderTimerActive()
        {
            return committedPositionDecision.HasValue &&
                   committedPointTimer > Time.time;
        }

        public void ClearCommittedPosition(string? reason = null)
        {
            if (committedPositionDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    committedHoldCoverPoint != null ? "arrivalHold.cover" : "arrivalHold.position",
                    "clear",
                    reason ?? "clear",
                    committedPositionDecision,
                    committedHoldCoverPoint != null ? committedHoldCoverPoint.Position : committedPosition,
                    committedHoldCoverPoint?.Id);
            }

            committedPosition = null;
            committedPointTimer = 0f;
            committedPointSetAt = 0f;
            committedHoldCoverPoint = null;
            committedPositionDecision = null;
            committedPointReason = null;
        }

        public void SetCommittedCover(CustomNavigationPoint cover, AICoreActionResultStruct<BotLogicDecision, GClass26> decision, float coverDuration = 0f)
        {
            ClearCommittedPosition("replace");
            committedHoldCoverPoint = cover;
            committedPositionDecision = decision;
            committedPointReason = decision.Reason;
            committedPointSetAt = Time.time;
            committedPointTimer = Time.time + (coverDuration > 0f ? coverDuration : GetCommittedCoverHoldDuration(decision.Reason));
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "arrivalHold.cover",
                "commit",
                decision.Reason,
                decision,
                cover.Position,
                cover.Id,
                true,
                committedPointTimer);
        }

        public void SetCommittedPosition(Vector3 position, AICoreActionResultStruct<BotLogicDecision, GClass26> decision, float positionDuration = 0f)
        {
            ClearCommittedPosition("replace");
            committedPosition = position;
            committedPositionDecision = decision;
            committedPointReason = decision.Reason;
            committedPointSetAt = Time.time;
            committedPointTimer = Time.time + (positionDuration > 0f ? positionDuration : GetCommittedPositionHoldDuration(decision.Reason));
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "arrivalHold.position",
                "commit",
                decision.Reason,
                decision,
                position,
                null,
                false,
                committedPointTimer);
        }

        public bool HasCommittedHolderSettled(float seconds)
        {
            return committedPositionDecision.HasValue &&
                   committedPointSetAt > 0f &&
                   Time.time - committedPointSetAt >= seconds;
        }

        public void ArmCommittedArrivalHold(string? reason, bool preferCover = true)
        {
            // Arrival hold is the anti-churn bridge between "I reached the destination" and
            // "I am allowed to plan again". It does not force the bot to leave cover when the
            // timer expires; it only stops immediate re-selection of another movement action.
            string holdReason = CreateCommittedHoldReason(reason, preferCover);
            AICoreActionResultStruct<BotLogicDecision, GClass26> holdDecision =
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason);

            if (preferCover)
            {
                CustomNavigationPoint? cover = committedCoverPoint ?? botOwner.Memory?.CurCustomCoverPoint;
                if (cover != null)
                {
                    SetCommittedCover(cover, holdDecision, GetCommittedCoverHoldDuration(reason));
                    return;
                }
            }

            Vector3 position = botOwner.Position;
            if (botOwner.GoToSomePointData?.HaveTarget() == true && IsFinite(botOwner.GoToSomePointData.Point))
            {
                position = botOwner.GoToSomePointData.Point;
            }

            SetCommittedPosition(position, holdDecision, GetCommittedPositionHoldDuration(reason));
        }

        private bool ShouldBreakCommittedPositionHold()
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                return true;
            }

            if (IsCommittedHoldEnemyContact(goalEnemy))
            {
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData != null &&
                followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                (command == FollowerCommandType.PushEnemy ||
                 command == FollowerCommandType.RegroupNearBoss ||
                 command == FollowerCommandType.SuppressEnemy ||
                 command == FollowerCommandType.CombatComeToBossCover ||
                 command == FollowerCommandType.CombatMoveToPointTactical))
            {
                return true;
            }

            return false;
        }

        private bool IsCommittedHoldEnemyContact(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.Distance <= CloseVisibleThreatBreakDistance)
            {
                return true;
            }

            return goalEnemy.CanShoot &&
                   (goalEnemy.IsVisible || Enemy.IsVisible(botOwner, goalEnemy));
        }

        private bool IsCommittedHoldCoverStillValid()
        {
            if (committedHoldCoverPoint == null)
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedHoldCoverPoint.Id)
            {
                return true;
            }

            return (botOwner.Position - committedHoldCoverPoint.Position).sqrMagnitude <= 3f * 3f;
        }

        private static string CreateCommittedHoldReason(string? reason, bool cover)
        {
            string prefix = cover ? "committedCoverHold" : "committedPositionHold";
            return string.IsNullOrEmpty(reason)
                ? prefix
                : $"{prefix}.{reason}";
        }

        private static float GetCommittedCoverHoldDuration(string? reason)
        {
            if (IsReasonOrSubreason(reason, "runToHeal") || IsReasonOrSubreason(reason, "moveToHeal"))
            {
                return HealingCommittedHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "shootCover") || IsReasonOrSubreason(reason, "retreatShootCover"))
            {
                return ShootCommittedCoverHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "retreatSafeCover") || IsReasonOrSubreason(reason, "safeCover"))
            {
                return RetreatCommittedCoverHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "bossCover") || IsReasonOrSubreason(reason, "protectBossCover"))
            {
                return BossCommittedCoverHoldSeconds;
            }

            return DefaultCommittedCoverHoldSeconds;
        }

        private static float GetCommittedPositionHoldDuration(string? reason)
        {
            if (IsReasonOrSubreason(reason, "runToHeal") || IsReasonOrSubreason(reason, "moveToHeal"))
            {
                return HealingCommittedHoldSeconds;
            }

            return DefaultCommittedPositionHoldSeconds;
        }

        public static bool IsReasonOrSubreason(string? reason, string baseReason)
        {
            return string.Equals(reason, baseReason, StringComparison.Ordinal) ||
                   (!string.IsNullOrEmpty(reason) &&
                    reason.StartsWith(baseReason + ".", StringComparison.Ordinal));
        }

        public bool IsInFight(BotLogicDecision decision)
        {
            bool engaged = new List<BotLogicDecision>
            {
                BotLogicDecision.shootFromStationary,
                BotLogicDecision.shootFromCover,
                BotLogicDecision.shootFromPlace,
                BotLogicDecision.suppressGrenade,
            }.Contains(decision);

            if (!engaged && decision == BotLogicDecision.suppressFire && IsEnemyVisibleAndShootable())
            {
                engaged = true;
            }

            return engaged;
        }

        public bool HasActiveCombatGestureOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   (command == FollowerCommandType.CombatComeToBossCover ||
                    command == FollowerCommandType.CombatMoveToPointTactical);
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateRegroupObjectiveDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.standBy,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
        }
        /// <summary>
        /// Returns the active tactic so combat branches can bias toward protection or ranged play.
        /// </summary>
        public FollowerCombatTactic GetFollowerTactic()
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.CombatTactic ?? FollowerCombatTactic.Balanced;
        }

        /// <summary>
        /// Reads the configured follower aggression as a normalized 0-1 value.
        /// </summary>
        public float GetAggression01()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            float aggression = followerData?.EffectiveCombatAggression ?? 50f;
            return Mathf.Clamp01(aggression / 100f);
        }

        public bool IsTemporaryHoldPositionAggressionActive()
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.IsTemporaryHoldPositionAggressionActive == true;
        }

        public bool HaveCoverToShoot { get; private set; }
        public CustomNavigationPoint? PointToShoot { get; private set; }

        public bool IsEnemyVisibleAndShootable()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            return HasActiveCombatEnemy(goalEnemy) && goalEnemy.CanShoot && goalEnemy.IsVisible;
        }

        public bool HasActiveCombatEnemy()
        {
            return HasActiveCombatEnemy(botOwner.Memory.GoalEnemy);
        }

        public bool HasActiveCombatEnemy(EnemyInfo? goalEnemy)
        {
            if (!botOwner.Memory.HaveEnemy || goalEnemy == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return goalEnemy.Person?.HealthController?.IsAlive == true;
        }

        private bool HasActiveOrRetainedGoalEnemy(out EnemyInfo? goalEnemy)
        {
            goalEnemy = botOwner.Memory.GoalEnemy;
            if (HasActiveCombatEnemy(goalEnemy))
            {
                return true;
            }

            return FollowerContactEnemyRetention.TryRestore(botOwner, out goalEnemy) && goalEnemy != null;
        }

        public bool HasAnyActiveCombatEnemy()
        {
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (IsTrackedEnemyAlive(enemyInfo))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTrackedEnemyAlive(EnemyInfo? enemyInfo)
        {
            if (enemyInfo == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(enemyInfo.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyInfo.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return enemyInfo.Person?.HealthController?.IsAlive == true;
        }

        private static bool HasActiveCombatEnemy(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner?.Memory?.HaveEnemy != true || goalEnemy == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return goalEnemy.Person?.HealthController?.IsAlive == true;
        }

        /// <summary>
        /// Promotes an already-tracked enemy to the follower's current goal without forcing a new acquire path.
        /// </summary>
        public bool TryPromoteTrackedEnemyAsGoal(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId != enemyProfileId)
                {
                    continue;
                }

                item.Value.PriorityIndex = 0;
                Enemy.RepairPersonalMemory(item.Value, item.Key.Position, item.Value.IsVisible || item.Value.CanShoot || item.Value.HaveSeen);
                botOwner.Memory.GoalEnemy = item.Value;
                return true;
            }

            return false;
        }

        public bool TryForceGoalEnemy(string enemyProfileId, string reason, out EnemyInfo? forcedEnemy)
        {
            forcedEnemy = null;
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.Memory == null)
            {
                return false;
            }

            Player? enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyProfileId);
            if (enemyPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            EnemyInfo? enemyInfo = Enemy.MakeEnemy(botOwner, enemyPlayer, EBotEnemyCause.checkAddTODO);
            if (enemyInfo == null)
            {
                return false;
            }

            bool alreadyGoal = string.Equals(botOwner.Memory.GoalEnemy?.ProfileId, enemyProfileId, StringComparison.Ordinal);
            if (!alreadyGoal)
            {
                // Explicit retarget orders need a stronger hand-off than priority scoring. Clear
                // the current goal and retention once, then install the requested enemy as the new
                // goal so vanilla/group sorting cannot immediately bounce us back to the old target.
                FollowerContactEnemyRetention.ClearAndAllowNextGoalClear(botOwner);
                botOwner.Memory.GoalEnemy = null;
                botOwner.Memory.LastEnemy = null;
            }

            enemyInfo.PriorityIndex = 0;
            enemyInfo.IgnoreUntilAggression = false;
            enemyInfo.SetVisible(enemyInfo.IsVisible);
            Enemy.RepairPersonalMemory(enemyInfo, enemyPlayer.Position, enemyInfo.IsVisible || enemyInfo.CanShoot || enemyInfo.HaveSeen);
            botOwner.Memory.IsPeace = false;
            botOwner.Memory.GoalEnemy = enemyInfo;
            FollowerContactEnemyRetention.Register(botOwner, enemyPlayer, enemyInfo.IsVisible || enemyInfo.CanShoot, prioritized: true);
            forcedEnemy = enemyInfo;
            return HasActiveCombatEnemy(enemyInfo);
        }

        public bool TryForceGoalEnemy(BotOwner enemyBot, string reason, out EnemyInfo? forcedEnemy)
        {
            forcedEnemy = null;
            if (enemyBot?.GetPlayer?.HealthController?.IsAlive != true || string.IsNullOrEmpty(enemyBot.ProfileId))
            {
                return false;
            }

            return TryForceGoalEnemy(enemyBot.ProfileId, reason, out forcedEnemy);
        }

        /// <summary>
        /// Applies the default follower aggression-to-threat mapping used by the core combat path.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, float aggression01)
        {
            bool ignoreEquip = aggression01 >= 0.4f;
            float maximumEnemies = aggression01 >= 0.7f ? 3f : aggression01 >= 0.4f ? 2f : 1f;
            return IsEnemyLowThreat(goalEnemy, ignoreEquip, maximumEnemies);
        }

        /// <summary>
        /// Decides whether a visible enemy should force the bot into cover before trading shots.
        /// </summary>
        public bool ShouldTakeVisibleCover(EnemyInfo goalEnemy, float? aggressionOverride01 = null)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() || botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 0.75f))
            {
                return true;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            float standAndTradeDistance = botOwner.LookSensor.MaxShootDist * 0.5f;
            return aggression < 0.45f && goalEnemy.Distance > standAndTradeDistance && PointToShoot != null;
        }

        /// <summary>
        /// Shared aggression gate for pushes so tactic variants can reuse the same advance logic
        /// while overriding aggression or distance policy where needed.
        /// </summary>
        public bool ShouldAdvance(
            EnemyInfo goalEnemy,
            float? aggressionOverride01 = null,
            FollowerCombatTactic? tacticOverride = null,
            Enemy.EnemyDistance? maxPushDistanceOverride = null)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() ||
                botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 1f))
            {
                return false;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            float pushThreshold = goalEnemy.IsVisible ? 0.35f : 0.45f;

            if (tactic == FollowerCombatTactic.Protector)
            {
                pushThreshold += 0.15f;
            }
            else if (tactic == FollowerCombatTactic.Marksman)
            {
                pushThreshold += 0.3f;
            }

            Enemy.EnemyDistance maxPushDistance = maxPushDistanceOverride ?? GetMaxPushDistance(aggression, tactic);

            if (!IsEnemyLowThreat(goalEnemy, aggression))
            {
                return aggression >= 0.7f && Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close;
            }

            if (!goalEnemy.IsVisible && !HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            Enemy.EnemyDistance distance = Enemy.Distance(goalEnemy);
            if (distance > maxPushDistance)
            {
                return false;
            }

            if (aggression >= 0.5f &&
                !goalEnemy.IsVisible &&
                distance <= Enemy.EnemyDistance.Distant)
            {
                return true;
            }

            return aggression >= pushThreshold && ProtectWantKill(goalEnemy.Distance * 1.2f);
        }

        /// <summary>
        /// Chooses the movement mode used to reach a committed combat cover point.
        /// </summary>
        public BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy)
        {
            return SelectCommittedCoverMoveAction(goalEnemy, botOwner.Memory.CurCustomCoverPoint);
        }

        public BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy, CustomNavigationPoint? targetCover)
        {
            // If this is heal cover and bot has healed enough/threat changed, clear it and return to combat
            if (targetCover == committedHealCover && ShouldClearHealCover(goalEnemy, out string? clearReason))
            {
                if (pitFireTeam.IsDebugBuild)
                {
                    Modules.Logger.LogInfo($"[HealCover] follower={botOwner.name ?? botOwner.Profile?.Nickname ?? "unknown"} reason={clearReason ?? "unknown"}");
                }

                ClearCommittedHealCover();
                // Fall through to threat-based move decision; likely need to rejoin combat or regroup
                bool canSprintPlayer = CanSprintForCombatMovement();
                return canSprintPlayer
                    ? BotLogicDecision.runToCover
                    : BotLogicDecision.attackMoving;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                if (IsRetreatCover(goalEnemy, targetCover))
                {
                    return (BotLogicDecision)CustomBotDecisions.attackRetreat;
                }

                return BotLogicDecision.attackMoving;
            }

            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                if (IsRetreatCover(goalEnemy, targetCover))
                {
                    return (BotLogicDecision)CustomBotDecisions.attackRetreat;
                }

                return BotLogicDecision.attackMovingWithSuppress;
            }

            return CanSprintForCombatMovement()
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;
        }

        private bool IsRetreatCover(EnemyInfo goalEnemy, CustomNavigationPoint? targetCover)
        {
            if (targetCover == null)
            {
                return false;
            }

            Vector3 enemyPosition = goalEnemy.CurrPosition;
            float currentEnemyDistanceSqr = (botOwner.Position - enemyPosition).sqrMagnitude;
            float coverEnemyDistanceSqr = (targetCover.Position - enemyPosition).sqrMagnitude;
            return coverEnemyDistanceSqr > currentEnemyDistanceSqr + 2f * 2f;
        }

        /// <summary>
        /// Pushes the selected cover point into EFT cover memory so movement actions use it.
        /// </summary>
        public void AssignCover(CustomNavigationPoint? cover)
        {
            SetCover(cover);
            if (cover != null && cover.IsFreeById(botOwner.Id))
            {
                cover.SetOwner(botOwner);
            }
        }

        /// <summary>
        /// Assigns the already-committed cover point back into EFT memory before reissuing movement.
        /// </summary>
        public void AssignCommittedCover()
        {
            AssignCover(committedCoverPoint);
        }

        /// <summary>
        /// Finds and commits a single combat cover using the default follower cover preference order.
        /// </summary>
        public bool TryCommitCombatCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            float bossCoverSearchRadius,
            out string reason,
            bool avoidBossFireLane = false)
        {
            reason = requireShootLane ? "shootCover" : "safeCover";

            if (HasCommittedCover())
            {
                reason = GetCommittedCoverReason();
                return true;
            }

            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = null;
            if (requireShootLane &&
                IsCoverUsable(PointToShoot) &&
                (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true)))
            {
                cover = PointToShoot;
                reason = "shootCover";
            }

            if (cover == null &&
                TryAssignRetreatAttackCover(goalEnemy, requireShootLane, GetCombatCoverMaxDistanceSqr(), false))
            {
                CustomNavigationPoint? retreatCover = botOwner.Memory.CurCustomCoverPoint;
                if (!avoidBossFireLane ||
                    retreatCover == null ||
                    !IsBossFireLaneMovementRisk(retreatCover.Position, goalEnemy, includePath: true))
                {
                    cover = retreatCover;
                    reason = requireShootLane ? "retreatShootCover" : "retreatSafeCover";
                }
            }

            if (cover == null &&
                !requireShootLane &&
                IsCoverUsable(PointToShoot) &&
                (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true)))
            {
                cover = PointToShoot;
                reason = "safeCover";
            }

            if (cover == null && TryFindBossCover(goalEnemy, bossCoverSearchRadius, out CustomNavigationPoint? bossCover))
            {
                if (!avoidBossFireLane ||
                    bossCover == null ||
                    !IsBossFireLaneMovementRisk(bossCover.Position, goalEnemy, includePath: true))
                {
                    cover = bossCover;
                    reason = "bossCover";
                }
            }

            return TryCommitSelectedCombatCover(goalEnemy, cover, reason);
        }

        /// <summary>
        /// Finds and commits a firing-position cover. Tactics can use this when they want their
        /// own selection policy but still need the same sticky-cover movement behavior.
        /// </summary>
        public bool TryCommitFiringPositionCover(
            EnemyInfo goalEnemy,
            string reason,
            out string committedReason,
            bool preferPointToShoot = true,
            bool preferInbetween = false,
            bool enforceMarksmanPositionPolicy = false,
            bool avoidBossFireLane = false)
        {
            committedReason = reason;
            if (HasCommittedCover())
            {
                committedReason = GetCommittedCoverReason();
                return true;
            }

            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = preferPointToShoot &&
                                           IsCoverUsable(PointToShoot) &&
                                           (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true))
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true, avoidBossFireLane: avoidBossFireLane) ??
                  GetApproachableCover(avoidBossFireLane: avoidBossFireLane)
                : GetApproachableCover(avoidBossFireLane: avoidBossFireLane);

            if (enforceMarksmanPositionPolicy &&
                cover != null &&
                !IsMarksmanFiringPositionAllowed(goalEnemy, cover.Position))
            {
                return false;
            }

            return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
        }

        public bool TryCommitMarksmanSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 enemyPosition,
            Vector3 watchedDestination,
            string reason,
            out string committedReason)
        {
            committedReason = reason;
            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = FindPushSupportCover(goalEnemy, pushOwnerPosition, enemyPosition, requireEnemyShootLane: true, keepBehindBoss: true);
            if (cover == null)
            {
                cover = FindPushSupportCover(goalEnemy, pushOwnerPosition, watchedDestination, requireEnemyShootLane: false, keepBehindBoss: true);
                committedReason += ".watchDestination";
            }
            else
            {
                committedReason += ".shootEnemy";
            }

            if (cover != null)
            {
                return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
            }

            return false;
        }

        public bool TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent)
        {
            pushEvent = default;
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            return boss.CombatEvents.TryGetActivePushFor(botOwner, out pushEvent);
        }

        public bool TryGetActivePushEventForCurrentEnemy(out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetActivePushEvent(out pushEvent))
            {
                return false;
            }

            return IsCurrentGoalEnemy(pushEvent.EnemyProfileId);
        }

        // Helper eligibility for Rifleman-style push support. We require both straight-line and
        // nav-distance proximity so a follower across a wall/building does not "join" a push that
        // is tactically nearby but unreachable without a long detour.
        public bool TryGetNearbyActivePushEvent(
            float maxStraightDistance,
            float maxNavDistance,
            out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetActivePushEvent(out pushEvent))
            {
                return false;
            }

            if (!IsFinite(pushEvent.Owner.Position))
            {
                return false;
            }

            float straightDistance = Vector3.Distance(botOwner.Position, pushEvent.Owner.Position);
            if (straightDistance > maxStraightDistance)
            {
                return false;
            }

            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, pushEvent.Owner.Position);
            return !IsFinite(navDistance) || navDistance <= maxNavDistance;
        }

        public bool TryGetNearbyActivePushEventForCurrentEnemy(
            float maxStraightDistance,
            float maxNavDistance,
            out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetNearbyActivePushEvent(maxStraightDistance, maxNavDistance, out pushEvent))
            {
                return false;
            }

            return IsCurrentGoalEnemy(pushEvent.EnemyProfileId);
        }

        private bool IsCurrentGoalEnemy(string enemyProfileId)
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            return !string.IsNullOrEmpty(enemyProfileId) &&
                   HasActiveCombatEnemy(goalEnemy) &&
                   string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal);
        }

        public bool HasActivePushFromOther()
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            return boss.CombatEvents.HasActivePushFromOther(botOwner);
        }

        public bool TryCommitSupportFiringCover(
            EnemyInfo supportEnemy,
            string reason,
            out string committedReason,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            float maxSearchRadius = 35f)
        {
            committedReason = reason;
            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            if (!TryGetSupportCoverForEnemy(supportEnemy, out CustomNavigationPoint? supportCover, out _, maxSearchRadius))
            {
                return false;
            }

            if (preferBackline)
            {
                Vector3 bossPosition = GetBossPosition();
                Vector3 enemyAnchor = GetEnemyAnchorOrFallback(supportEnemy, Vector3.zero);
                if (IsFinite(bossPosition) &&
                    IsFinite(enemyAnchor) &&
                    !IsSupportPositionBehindBossLine(supportCover!.Position, bossPosition, enemyAnchor))
                {
                    return false;
                }
            }

            if (enforceMarksmanPositionPolicy &&
                supportCover != null &&
                !IsMarksmanFiringPositionAllowed(supportEnemy, supportCover.Position))
            {
                return false;
            }

            return TryCommitSelectedCombatCover(supportEnemy, supportCover, committedReason);
        }

        /// <summary>
        /// Returns true when the current cover commitment still exists and should be managed.
        /// </summary>
        public bool HasCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return false;
            }

            if (committedCoverUntil < Time.time && !IsBotInCommittedCover())
            {
                ClearCommittedCover("expired");
                return false;
            }

            return IsCommittedCoverStillUsable(committedCoverPoint);
        }

        /// <summary>
        /// Drops invalid committed cover before it can keep feeding stale movement.
        /// </summary>
        public void ValidateCommittedCover()
        {
            if (!HasCommittedCover())
            {
                ClearCommittedCover("invalid");
            }
        }

        /// <summary>
        /// Treats the bot as arrived when EFT marks the cover active or the bot is physically close.
        /// </summary>
        public bool IsBotInCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedCoverPoint.Id)
            {
                return true;
            }

            return (botOwner.Position - committedCoverPoint.Position).sqrMagnitude <= 2f * 2f;
        }

        /// <summary>
        /// Keeps a working cover commitment alive while the bot is actively using it.
        /// </summary>
        public void ExtendCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return;
            }

            committedCoverUntil = Mathf.Max(committedCoverUntil, Time.time + 0.75f);
        }

        /// <summary>
        /// Drops the current combat-cover commitment so the next decision can select fresh cover.
        /// </summary>
        public void ClearCommittedCover(string? reason = null)
        {
            if (committedCoverPoint != null)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "cover",
                    "clear",
                    reason ?? "clear",
                    committedCoverMoveAction != default
                        ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedCoverMoveAction, committedCoverMoveReason ?? "cover")
                        : null,
                    committedCoverPoint.Position,
                    committedCoverPoint.Id);
                coverCommitIntents.Remove(botOwner.Id);
            }

            committedCoverPoint = null;
            committedCoverMoveAction = default;
            committedCoverMoveReason = null;
            committedCoverSetAt = 0f;
            committedCoverUntil = 0f;
            ResetRunToCoverProgress();
            ResetTacticalPointProgress();
        }

        /// <summary>
        /// Clears committed cover and the search cooldown used when selecting a fresh cover point.
        /// </summary>
        public void ResetCommittedCover()
        {
            ClearCommittedCover();
            nextCoverAcquireTime = 0f;
        }

        /// <summary>
        /// Returns how long the current cover point has been committed.
        /// </summary>
        public float CommittedCoverAge => committedCoverSetAt <= 0f ? 0f : Time.time - committedCoverSetAt;

        public int? CommittedCoverId => committedCoverPoint?.Id;

        public string? CommittedCoverReason => committedCoverMoveReason;

        public string? CommittedPositionReason => committedPointReason;

        public bool IsCommittedCoverLockExpired => CommittedCoverAge >= CoverCommitLockSeconds;

        public bool ShouldBreakCommittedPushForVisibility(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentPush,
            ref float actionableVisibleSince,
            float fireWhileMovingVisibleBreakSeconds = DefaultFireWhileMovingPushVisibleBreakSeconds)
        {
            bool actionableVisible = goalEnemy.IsVisible && goalEnemy.CanShoot;
            bool closeVisible = goalEnemy.IsVisible &&
                                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance();
            if (!actionableVisible && !closeVisible)
            {
                actionableVisibleSince = 0f;
                return false;
            }

            if (IsDirectEnemyPush(currentPush.Action))
            {
                actionableVisibleSince = 0f;
                return true;
            }

            if (!IsFireWhileMovingPush(currentPush.Action) && !IsDirectEnemyPush(currentPush.Action))
            {
                actionableVisibleSince = 0f;
                return true;
            }

            if (actionableVisibleSince <= 0f)
            {
                actionableVisibleSince = Time.time;
                return false;
            }

            return Time.time - actionableVisibleSince >= fireWhileMovingVisibleBreakSeconds;
        }

        public static bool IsFireWhileMovingPush(BotLogicDecision action)
        {
            return action == BotLogicDecision.attackMoving ||
                   action == BotLogicDecision.attackMovingWithSuppress;
        }

        public static bool IsDirectEnemyPush(BotLogicDecision action)
        {
            return action == BotLogicDecision.runToEnemy ||
                   action == BotLogicDecision.goToEnemy;
        }

        public static bool IsMovementDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action == BotLogicDecision.runToEnemy ||
                   decision.Action == BotLogicDecision.goToEnemy ||
                   decision.Action == BotLogicDecision.runToCover ||
                   decision.Action == BotLogicDecision.attackMoving ||
                   decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision.Action == BotLogicDecision.goToPoint ||
                   decision.Action == BotLogicDecision.goToPointTactical;
        }

        public bool ShouldBreakForBossUnderAttack(
            EnemyInfo goalEnemy,
            bool hasActivePushOrder = false,
            float stalePersonalEnemySeconds = 2.5f)
        {
            if (FollowerCombatAnchor.IsCombatIndependent(botOwner))
            {
                return false;
            }

            if (hasActivePushOrder)
            {
                return false;
            }

            // A live personal shot remains valid support; keep taking it.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (botOwner.Memory.HaveEnemy && sinceLastSeen > stalePersonalEnemySeconds)
            {
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            AIBossPlayerLogic? bossLogic = boss.GetBossLogic();
            if (bossLogic == null || !bossLogic.IsHitted)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            return bossEnemy != null && bossEnemy.GetPlayer?.HealthController?.IsAlive == true;
        }

        public bool ShouldBreakCommittedCoverForBossObjective(
            EnemyInfo goalEnemy,
            bool shouldRegroupForBossDistance,
            bool hasActivePushOrder = false,
            bool hasImmediateShot = false,
            bool allowMovingCommittedCoverBreak = false)
        {
            if (hasActivePushOrder || hasImmediateShot)
            {
                return false;
            }

            if (!shouldRegroupForBossDistance)
            {
                return false;
            }

            // Give committed cover time to be reached and used before escort pressure can pull it out.
            if (HasCommittedCover())
            {
                if (!IsBotInCommittedCover())
                {
                    return allowMovingCommittedCoverBreak && IsCommittedCoverLockExpired;
                }

                if (!IsCommittedCoverLockExpired)
                {
                    return false;
                }
            }

            return true;
        }

        public bool ShouldEndCurrentDecisionForBossObjective(
            string reason,
            EnemyInfo? goalEnemy,
            bool shouldRegroupForBossDistance,
            bool hasActivePushOrder = false,
            bool hasImmediateShot = false,
            bool allowMovingCommittedCoverBreak = false,
            IEnumerable<string>? committedCoverReasons = null)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (!IsBossHoldReason(reason) &&
                !IsCommittedCoverReason(reason, committedCoverReasons ?? DefaultBossObjectiveCoverBreakReasons))
            {
                return false;
            }

            return ShouldBreakCommittedCoverForBossObjective(
                goalEnemy,
                shouldRegroupForBossDistance,
                hasActivePushOrder,
                hasImmediateShot,
                allowMovingCommittedCoverBreak);
        }

        public static bool IsBossDistanceProtectedCommitmentReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            return reason.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("protect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsCommittedCoverReason(string reason, IEnumerable<string>? committedCoverReasons = null)
        {
            IEnumerable<string> reasons = committedCoverReasons ?? DefaultBossObjectiveCoverBreakReasons;
            foreach (string coverReason in reasons)
            {
                if (IsReasonOrSubreason(reason, coverReason))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsBossHoldReason(string? reason)
        {
            return string.Equals(reason, "bossHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHoldOpen", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHold.open", StringComparison.Ordinal);
        }

        private string GetCommittedCoverReason()
        {
            return !string.IsNullOrEmpty(committedCoverMoveReason)
                ? committedCoverMoveReason!
                : "commitCover";
        }

        /// <summary>
        /// Converts the current committed cover into the stable movement action needed to reach it.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMoveToCommittedCoverDecision(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                committedCoverMoveReason = reason;
            }

            return CreateCommittedCoverMoveDecision();
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedCoverMoveDecision()
        {
            BotLogicDecision moveAction = committedCoverMoveAction != default
                ? committedCoverMoveAction
                : (CanSprintForCombatMovement() ? BotLogicDecision.runToCover : BotLogicDecision.attackMoving);
            string reason = GetCommittedCoverReason();
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(moveAction, reason);
        }

        public bool TryCreateSuppressDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool allowObstructedSuppression = false)
        {
            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress())
            {
                return false;
            }

            if (!TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            if (Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.place");
                return true;
            }

            if (TryFindSuppressFromPoint(suppressTarget, out CustomNavigationPoint? suppressFrom))
            {
                if (botOwner.SuppressShoot.InitToPoint(suppressTarget, suppressFrom))
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.suppressFire,
                        $"{reasonPrefix}.move");
                    return true;
                }
            }

            if (allowObstructedSuppression &&
                IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.softObstructedPlace");
                return true;
            }

            return false;
        }

        public bool TryCreateSuppressFromPlaceDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool allowSoftObstructedSuppression = false)
        {
            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress() ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            if (Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.place");
                return true;
            }

            if (allowSoftObstructedSuppression &&
                IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.softObstructedPlace");
                return true;
            }

            return false;
        }

        public bool TryCreateSoftObstructedSuppressDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (TryCreateSuppressDecision(goalEnemy, reasonPrefix, out decision))
            {
                return true;
            }

            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress() ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            if (!IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) ||
                FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) ||
                !botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                return false;
            }

            botOwner.Steering.LookToPoint(suppressTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.suppressFire,
                $"{reasonPrefix}.softObstructedPlace");
            return true;
        }

        internal bool IsSoftObstructedSuppressionLane(Vector3 fireOrigin, Vector3 suppressTarget)
        {
            return IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask);
        }

        internal static bool IsSoftObstructedSuppressionLane(Vector3 fireOrigin, Vector3 suppressTarget, LayerMask mask)
        {
            Vector3 direction = suppressTarget - fireOrigin;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            RaycastHit[] softObstructionHits = new RaycastHit[1];
            int hitCount = Physics.RaycastNonAlloc(
                new Ray(fireOrigin, direction / distance),
                softObstructionHits,
                distance,
                mask);
            if (hitCount <= 0)
            {
                return false;
            }

            Collider collider = softObstructionHits[0].collider;
            if (collider == null)
            {
                return false;
            }

            return IsSoftFoliageCollider(collider);
        }

        private static bool IsSoftFoliageCollider(Collider collider)
        {
            GameObject gameObject = collider.gameObject;
            if (IsLayerInMask(gameObject.layer, LayerMaskClass.Grass) ||
                IsLayerInMask(gameObject.layer, LayerMaskClass.Foliage))
            {
                return true;
            }

            if (ContainsSoftFoliageToken(gameObject.name) ||
                ContainsSoftFoliageToken(collider.name))
            {
                return true;
            }

            Transform parent = collider.transform?.parent;
            while (parent != null)
            {
                if (ContainsSoftFoliageToken(parent.name))
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        private static bool IsLayerInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private static bool ContainsSoftFoliageToken(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("reed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("branch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryFindSuppressFromPoint(Vector3 suppressTarget, out CustomNavigationPoint? suppressFrom)
        {
            suppressFrom = null;
            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            float minDistanceSqr = SuppressFromMinDistance * SuppressFromMinDistance;

            CustomNavigationPoint? cover = Covers.GetClosestCoverPoint(
                botOwner,
                botOwner.Position,
                SuppressFromSearchRadius,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if ((point.Position - botOwner.Position).sqrMagnitude < minDistanceSqr)
                    {
                        return false;
                    }

                    if (!Utils.Utils.CanShootToTarget(shootPoint, point.Position, botOwner.LookSensor.Mask, false))
                    {
                        return false;
                    }

                    Vector3 fireOrigin = point.Position + Vector3.up * 1.2f;
                    return !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget);
                },
                CoverSearchType.distToToCenter);

            if (!IsCoverUsable(cover, true))
            {
                return false;
            }

            suppressFrom = cover;
            return true;
        }

        public bool TryGetSuppressTarget(EnemyInfo goalEnemy, out Vector3 suppressTarget)
        {
            suppressTarget = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint != null && IsFinite(shootPoint.Point))
            {
                suppressTarget = shootPoint.Point;
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor) || enemyAnchor.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            suppressTarget = enemyAnchor + Vector3.up * 0.8f;
            return true;
        }

        private bool CanAcquireCommittedCover()
        {
            if (Time.time < nextCoverAcquireTime)
            {
                return false;
            }

            nextCoverAcquireTime = Time.time + CoverSearchCooldownSeconds;
            return true;
        }

        public bool TryCommitSelectedCombatCover(EnemyInfo goalEnemy, CustomNavigationPoint? cover, string reason)
        {
            if (!IsCoverUsable(cover))
            {
                return false;
            }

            if (IsUnsafeFireSupportPath(goalEnemy, cover, reason))
            {
                return false;
            }

            BotLogicDecision moveAction = SelectCommittedCoverMoveAction(goalEnemy, cover);
            if (moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat &&
                IsPointBlankVisibleShootableThreat(goalEnemy))
            {
                return false;
            }

            if (moveAction == BotLogicDecision.runToCover)
            {
                reason += ".run";
                SetRunToCoverTactic(cover, reason);
            }
            else if (moveAction == BotLogicDecision.attackMovingWithSuppress)
            {
                reason += ".suppress";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }
            else if (moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                reason += ".retreat";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
                if (!goalEnemy.IsVisible)
                {
                    botOwner.Steering.LookToPoint(GetEnemyAnchor(goalEnemy) + Vector3.up * 0.8f);
                }
            }
            else if (moveAction == BotLogicDecision.attackMoving)
            {
                reason += ".walk";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            CommitCover(cover, moveAction, reason);
            AssignCover(cover);
            return true;
        }

        private void SetRunToCoverTactic(CustomNavigationPoint? cover, string reason)
        {
            if (IsProtectCoverReason(reason))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
                return;
            }

            if (IsAttackCoverReason(reason, cover))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                return;
            }

            SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
        }

        private static bool IsProtectCoverReason(string? reason)
        {
            return reason != null &&
                   (reason.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("protect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("regroup", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsAttackCoverReason(string? reason, CustomNavigationPoint? cover)
        {
            return cover?.CanIShootToEnemy == true ||
                   (reason != null &&
                    (reason.IndexOf("shoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private bool IsUnsafeFireSupportPath(EnemyInfo goalEnemy, CustomNavigationPoint cover, string reason)
        {
            if (goalEnemy == null ||
                string.IsNullOrEmpty(reason) ||
                !reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            if (Covers.IsPathTooCloseToEnemy(
                    botOwner.Position,
                    cover.Position,
                    enemyAnchor,
                    FireSupportPathEnemyMinDistance))
            {
                return true;
            }

            return Covers.IsPathExposedToEnemy(
                botOwner.Position,
                cover.Position,
                enemyAnchor,
                botOwner.LookSensor.Mask,
                sampleCount: 6);
        }

        /// <summary>
        /// Emergency fallback for the frame where vanilla drops GoalEnemy while the follower is still being hit.
        /// Without a goal enemy, tactic stacks cannot pick normal recover/cover logic, so commit any nearby
        /// usable cover and run instead of standing in a null-enemy hold.
        /// </summary>
        public bool TryGetNoEnemyThreatCoverDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!botOwner.Memory.IsUnderFire && !WasHitRecently(botOwner, 2f))
            {
                return false;
            }

            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Ambush,
                CoverShootType.hide,
                CoverSearchIntent.RunToCover);
            CustomNavigationPoint? cover = Covers.GetClosestCoverPoint(
                botOwner,
                botOwner.Position,
                50f,
                candidate => IsCoverUsable(candidate, ignoreSpotted: true),
                searchType);

            if (!IsCoverUsable(cover, ignoreSpotted: true))
            {
                return false;
            }

            CommitCover(cover, BotLogicDecision.runToCover, "noEnemyHitCover");
            AssignCover(cover);
            decision = CreateCommittedCoverMoveDecision();
            return true;
        }

        private void CommitCover(CustomNavigationPoint? cover, BotLogicDecision moveAction, string? reason)
        {
            if (cover == null)
            {
                return;
            }

            committedCoverPoint = cover;
            committedCoverMoveAction = moveAction;
            committedCoverMoveReason = reason;
            committedCoverSetAt = Time.time;
            committedCoverUntil = Time.time + CoverCommitLockSeconds;
            coverCommitIntents[botOwner.Id] = new CoverCommitIntent(cover.Id, IsCommittedShootingCoverReason(reason));
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "cover",
                "commit",
                reason,
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(moveAction, reason ?? "cover"),
                cover.Position,
                cover.Id,
                null,
                committedCoverUntil);
        }

        private bool IsCommittedCoverStillUsable(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return false;
            }

            if (IsBotInCommittedCover())
            {
                return true;
            }

            return cover.IsFreeById(botOwner.Id);
        }

        private static bool IsCoverAffinedDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision == BotLogicDecision.shootFromCover;
        }

        public void RefreshShootCover()
        {
            if (nextShootCoverCheckTime >= Time.time)
            {
                return;
            }

            Vector3 bossPosition = GetBossPosition();
            CustomNavigationPoint? candidate = FindFollowerShootCover();
            bool pointChangedMeaningfully = IsPointMeaningfullyDifferent(PointToShoot, candidate);
            if (ShouldUpdatePointToShoot(PointToShoot, candidate))
            {
                PointToShoot = candidate;
            }

            if (!IsCoverUsable(candidate))
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            if (candidate == null)
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            bool requireShootLane = ProtectCareKill();
            bool candidateCanShoot = candidate.CanIShootToEnemy;
            bool candidateShootLaneStable = !requireShootLane || IsShootLaneUpgradeStable(candidateCanShoot);
            bool rawHaveCoverToShoot = !requireShootLane || candidateShootLaneStable;
            HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(rawHaveCoverToShoot);
            if (!HaveCoverToShoot)
            {
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            CustomNavigationPoint? current = botOwner.Memory.CurCustomCoverPoint;
            if (!ShouldCommitRefreshedShootCover(current, candidate, bossPosition, requireShootLane, candidateShootLaneStable))
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            if (current != null && current.Id == candidate.Id)
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(candidate, true);
            ScheduleShootCoverRefresh(stable: false);
        }

        private bool ShouldCommitRefreshedShootCover(
            CustomNavigationPoint? current,
            CustomNavigationPoint candidate,
            Vector3 bossPosition,
            bool requireShootLane,
            bool candidateShootLaneStable)
        {
            // Rule 1: no current cover or current cover is invalid.
            if (IsCurrentCoverInvalid(current, bossPosition))
            {
                return true;
            }

            if (current == null)
            {
                return true;
            }

            bool currentCanShoot = current.CanIShootToEnemy;
            bool candidateCanShoot = candidate.CanIShootToEnemy;

            // Rule 2: current cannot shoot and candidate can.
            if (!currentCanShoot && candidateCanShoot && candidateShootLaneStable)
            {
                return true;
            }

            bool currentUsable = IsCoverUsable(current);
            bool candidateUsable = IsCoverUsable(candidate);

            // Rule 3: current violates boss-distance/usability and candidate does not.
            if (!currentUsable && candidateUsable)
            {
                return true;
            }

            // Rule 4: meaningful superiority only; avoid reshuffle from already-valid shoot-capable cover.
            if (currentUsable && currentCanShoot)
            {
                return false;
            }

            if (requireShootLane && !candidateShootLaneStable)
            {
                return false;
            }

            return HasMeaningfulNavImprovement(current, candidate);
        }

        private bool IsCurrentCoverInvalid(CustomNavigationPoint? cover, Vector3 bossPosition)
        {
            return cover == null ||
                   !cover.IsFreeById(botOwner.Id) ||
                   cover.IsSpotted;
        }

        /// <summary>
        /// Basic validity gate for a candidate cover point.
        /// </summary>
        public bool IsCoverUsable(CustomNavigationPoint? cover, bool ignoreSpotted = false)
        {
            return cover != null &&
                   cover.IsFreeById(botOwner.Id) &&
                   (ignoreSpotted || !cover.IsSpotted);
        }

        /// <summary>
        /// Returns the mod-owned maximum combat cover search distance.
        /// </summary>
        public float GetCombatCoverMaxDistanceSqr()
        {
            return CombatDistanceConfiguration.Instance.GetCombatCoverMaxDistanceSqr();
        }

        private bool HasMeaningfulNavImprovement(CustomNavigationPoint current, CustomNavigationPoint candidate)
        {
            float currentNavDistance = GetCoverNavDistance(current);
            float candidateNavDistance = GetCoverNavDistance(candidate);

            if (!IsFinite(currentNavDistance) || !IsFinite(candidateNavDistance))
            {
                return false;
            }

            return candidateNavDistance <= currentNavDistance * ShootCoverSuperiorNavImprovementFactor;
        }

        private bool ShouldUpdatePointToShoot(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (candidate == null)
            {
                return currentPoint == null;
            }

            if (currentPoint == null)
            {
                return true;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool IsPointMeaningfullyDifferent(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (currentPoint == null || candidate == null)
            {
                return currentPoint != candidate;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool UpdateDebouncedHaveCoverToShoot(bool rawValue)
        {
            if (rawValue == HaveCoverToShoot)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = 0f;
                return HaveCoverToShoot;
            }

            if (pendingHaveCoverToShootSince <= 0f || pendingHaveCoverToShoot != rawValue)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = Time.time;
                return HaveCoverToShoot;
            }

            if (Time.time - pendingHaveCoverToShootSince < HaveCoverToShootDebounceSeconds)
            {
                return HaveCoverToShoot;
            }

            HaveCoverToShoot = rawValue;
            pendingHaveCoverToShootSince = 0f;
            return HaveCoverToShoot;
        }

        private bool IsHaveCoverDebouncePending()
        {
            return pendingHaveCoverToShootSince > 0f && pendingHaveCoverToShoot != HaveCoverToShoot;
        }

        private bool IsShootLaneUpgradeStable(bool candidateCanShoot)
        {
            if (!candidateCanShoot)
            {
                shootLaneUpgradeSince = 0f;
                return false;
            }

            if (shootLaneUpgradeSince <= 0f)
            {
                shootLaneUpgradeSince = Time.time;
            }

            return Time.time - shootLaneUpgradeSince >= ShootLaneUpgradeHysteresisSeconds;
        }

        private void ScheduleShootCoverRefresh(bool stable)
        {
            nextShootCoverCheckTime = Time.time + (stable ? StableShootCoverRefreshInterval : UnstableShootCoverRefreshInterval);
        }

        private float GetCoverNavDistance(CustomNavigationPoint cover)
        {
            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (IsFinite(navDistance))
            {
                return navDistance;
            }

            return Vector3.Distance(botOwner.Position, cover.Position);
        }

        private CustomNavigationPoint? FindFollowerShootCover()
        {
            Vector3 bossPosition = GetBossPosition();
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            Vector3 enemyAnchor = goalEnemy != null ? GetEnemyAnchor(goalEnemy) : Vector3.zero;
            LayerMask mask = botOwner.LookSensor.Mask;
            if (goalEnemy != null)
            {
                if (shootPoint != null)
                {
                    CoverSearchType shootSearchType = SetCoverTacticAndGetSearchType(
                        BotsGroup.BotCurrentTactic.Attack,
                        CoverShootType.shoot,
                        CoverSearchIntent.Attack);
                    CustomNavigationPoint? directionalShootCover = Covers.GetClosestCoverPointTowardPoint(
                        botOwner,
                        bossPosition,
                        enemyAnchor,
                        25f,
                        cover => Utils.Utils.CanShootToTarget(shootPoint, cover, mask, false),
                        searchTypeOverride: shootSearchType);
                    if (directionalShootCover != null)
                    {
                        return directionalShootCover;
                    }
                }

                CoverSearchType attackSearchType = SetCoverTacticAndGetSearchType(
                    BotsGroup.BotCurrentTactic.Attack,
                    CoverShootType.hide,
                    CoverSearchIntent.Attack);
                CustomNavigationPoint? directionalCover = Covers.GetClosestCoverPointTowardPoint(
                    botOwner,
                    bossPosition,
                    enemyAnchor,
                    22f,
                    searchTypeOverride: attackSearchType);
                if (directionalCover != null)
                {
                    return directionalCover;
                }
            }

            return null;
        }

        /// <summary>
        /// Old-plugin equivalent of GetClosestAttackCoverPoint/GetClosestShootCover.
        /// Finds a nearby cover point with a clear shot to the enemy target point.
        /// </summary>
        public CustomNavigationPoint? GetClosestShootCover(
            Vector3 centerPosition,
            float maxDistance = 150f,
            bool inbetween = false,
            float? maxDistanceFromBot = null,
            bool avoidCrossingEnemyFront = false,
            bool avoidBossFireLane = false)
        {
            ShootPointClass shootPointClass = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPointClass == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            bool cachedCoverCrossesBossLane =
                avoidBossFireLane &&
                cachedClosestShootCover != null &&
                IsBossFireLaneMovementRisk(cachedClosestShootCover.Position, shootPointClass.Point, includePath: true);
            if (nextClosestShootCoverCheckTime > Time.time && !cachedCoverCrossesBossLane)
            {
                return cachedClosestShootCover;
            }

            nextClosestShootCoverCheckTime = Time.time + 1f;

            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.Attack);
            float weaponShootDistMaxSqr = botOwner.LookSensor.MaxShootDist * botOwner.LookSensor.MaxShootDist;
            float? maxDistanceFromBotSqr = maxDistanceFromBot.HasValue
                ? maxDistanceFromBot.Value * maxDistanceFromBot.Value
                : null;
            Func<CustomNavigationPoint, bool> eligibility = point =>
            {
                if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                {
                    return false;
                }

                if (maxDistanceFromBotSqr.HasValue &&
                    (point.Position - botOwner.Position).sqrMagnitude > maxDistanceFromBotSqr.Value)
                {
                    return false;
                }

                if (inbetween && !Covers.IsPointBetween(point.Position, botOwner.Position, centerPosition))
                {
                    return false;
                }

                if ((point.Position - shootPointClass.Point).sqrMagnitude >= weaponShootDistMaxSqr)
                {
                    return false;
                }

                if (avoidCrossingEnemyFront &&
                    ShouldAvoidCoverBecauseCrossesEnemyFront(point.Position, shootPointClass.Point))
                {
                    return false;
                }

                return Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner.LookSensor.Mask, false);
            };

            CustomNavigationPoint? cover = null;
            if (avoidBossFireLane)
            {
                cover = Covers.GetClosestCoverPoint(
                    botOwner,
                    centerPosition,
                    maxDistance,
                    point => eligibility(point) &&
                             !IsBossFireLaneMovementRisk(point.Position, shootPointClass.Point, includePath: true),
                    searchType);
            }

            cachedClosestShootCover = cover ?? Covers.GetClosestCoverPoint(
                botOwner,
                centerPosition,
                maxDistance,
                eligibility,
                searchType);

            if (cachedClosestShootCover != null)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            botOwner.Memory.SetCoverPoints(cachedClosestShootCover);
            return cachedClosestShootCover;
        }

        /// <summary>
        /// Old-plugin equivalent of GetApproachablePoint/GetApproachableCover.
        /// Picks a shooting cover around the midpoint between bot and enemy.
        /// </summary>
        public CustomNavigationPoint? GetApproachableCover(bool inbetween = false, bool avoidBossFireLane = false)
        {
            if (nextApproachableCoverCheckTime > Time.time && !avoidBossFireLane)
            {
                return cachedClosestShootCover;
            }

            nextApproachableCoverCheckTime = Time.time + 1f;
            nextClosestShootCoverCheckTime = 0f;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.EnemyLastPositionReal)
                ? goalEnemy.EnemyLastPositionReal
                : goalEnemy.CurrPosition;

            Vector3 midpoint = (botOwner.Position + enemyPosition) * 0.5f;
            return GetClosestShootCover(
                midpoint,
                120f,
                inbetween,
                avoidCrossingEnemyFront: true,
                avoidBossFireLane: avoidBossFireLane);
        }

        public CustomNavigationPoint? GetWeakEnemyPushCover(bool avoidBossFireLane = false)
        {
            float maxDistance = GetWeakEnemyPushMaxDistance();
            float maxDistanceSqr = maxDistance * maxDistance;
            CustomNavigationPoint? approachCover = GetApproachableCover(maxDistance, avoidBossFireLane: avoidBossFireLane);
            if (approachCover == null)
            {
                return null;
            }

            return (approachCover.Position - botOwner.Position).sqrMagnitude <= maxDistanceSqr
                ? approachCover
                : null;
        }

        private CustomNavigationPoint? GetApproachableCover(float maxDistance, bool inbetween = false, bool avoidBossFireLane = false)
        {
            if (nextApproachableCoverCheckTime > Time.time && !avoidBossFireLane)
            {
                return cachedClosestShootCover != null &&
                       (cachedClosestShootCover.Position - botOwner.Position).sqrMagnitude <= maxDistance * maxDistance
                    ? cachedClosestShootCover
                    : null;
            }

            nextApproachableCoverCheckTime = Time.time + 1f;
            nextClosestShootCoverCheckTime = 0f;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.EnemyLastPositionReal)
                ? goalEnemy.EnemyLastPositionReal
                : goalEnemy.CurrPosition;

            Vector3 midpoint = (botOwner.Position + enemyPosition) * 0.5f;
            return GetClosestShootCover(
                midpoint,
                maxDistance,
                inbetween,
                maxDistanceFromBot: maxDistance,
                avoidCrossingEnemyFront: true,
                avoidBossFireLane: avoidBossFireLane);
        }

        private bool ShouldAvoidCoverBecauseCrossesEnemyFront(Vector3 coverPosition, Vector3 enemyPosition)
        {
            Vector3 botPosition = botOwner.Position;
            Vector3 toEnemy = enemyPosition - botPosition;
            Vector3 toCover = coverPosition - botPosition;

            toEnemy.y = 0f;
            toCover.y = 0f;

            if (toEnemy.sqrMagnitude < 0.01f || toCover.sqrMagnitude < 0.01f)
            {
                return false;
            }

            // If candidate is not generally toward enemy direction, this check is irrelevant.
            if (Vector3.Dot(toCover.normalized, toEnemy.normalized) <= 0f)
            {
                return false;
            }

            float enemyDist = toEnemy.magnitude;
            float coverDist = toCover.magnitude;

            // At longer ranges, a quick crossing segment is usually acceptable and often safer
            // than over-constraining cover picks.
            if (enemyDist > EnemyFrontCrossGuardMaxDistance)
            {
                return false;
            }

            // Cover not beyond enemy depth usually does not force a frontal cross.
            if (coverDist <= enemyDist + 1.5f)
            {
                return false;
            }

            // If the straight path to candidate runs too close to enemy anchor, treat as frontal cross.
            float enemyDistToPath = DistancePointToSegmentXZ(enemyPosition, botPosition, coverPosition);
            return enemyDistToPath < 7f;
        }

        public bool IsBossFireLaneMovementRisk(Vector3 destination, EnemyInfo goalEnemy, bool includePath)
        {
            return IsBossFireLaneMovementRisk(destination, GetEnemyAnchor(goalEnemy), includePath);
        }

        public bool IsBossFireLaneMovementRisk(Vector3 destination, Vector3 enemyAnchor, bool includePath)
        {
            if (!IsFinite(destination) ||
                !IsFinite(enemyAnchor) ||
                FollowerCombatAnchor.IsCombatIndependent(botOwner))
            {
                return false;
            }

            Vector3 bossPosition = GetRealBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            if (bossToEnemy.sqrMagnitude < 4f)
            {
                return false;
            }

            bool botStartsInLane = IsPointInsideBossFireLane(botOwner.Position, bossPosition, enemyAnchor, BossFireLanePathRadius);
            if (IsPointInsideBossFireLane(destination, bossPosition, enemyAnchor, BossFireLaneCandidateRadius))
            {
                return true;
            }

            return includePath &&
                   !botStartsInLane &&
                   DistanceSegmentToSegmentXZ(botOwner.Position, destination, bossPosition, enemyAnchor) <= BossFireLanePathRadius;
        }

        private static bool IsPointInsideBossFireLane(Vector3 point, Vector3 bossPosition, Vector3 enemyAnchor, float radius)
        {
            Vector3 lane = enemyAnchor - bossPosition;
            lane.y = 0f;
            float laneLength = lane.magnitude;
            if (laneLength < 0.01f)
            {
                return false;
            }

            Vector3 direction = lane / laneLength;
            Vector3 bossToPoint = point - bossPosition;
            bossToPoint.y = 0f;
            float forward = Vector3.Dot(bossToPoint, direction);
            if (forward < -BossFireLaneStartPadding || forward > laneLength + BossFireLaneEndPadding)
            {
                return false;
            }

            Vector3 closest = bossPosition + direction * forward;
            closest.y = point.y;
            return (point - closest).sqrMagnitude <= radius * radius;
        }

        private static float DistancePointToSegmentXZ(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(segmentStart.x, segmentStart.z);
            Vector2 b = new Vector2(segmentEnd.x, segmentEnd.z);

            Vector2 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;
            if (abLenSqr <= 0.0001f)
            {
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }

        private static float DistanceSegmentToSegmentXZ(Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB)
        {
            if (SegmentsIntersectXZ(startA, endA, startB, endB))
            {
                return 0f;
            }

            return Mathf.Min(
                Mathf.Min(DistancePointToSegmentXZ(startA, startB, endB), DistancePointToSegmentXZ(endA, startB, endB)),
                Mathf.Min(DistancePointToSegmentXZ(startB, startA, endA), DistancePointToSegmentXZ(endB, startA, endA)));
        }

        private static bool SegmentsIntersectXZ(Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB)
        {
            Vector2 a = new Vector2(startA.x, startA.z);
            Vector2 b = new Vector2(endA.x, endA.z);
            Vector2 c = new Vector2(startB.x, startB.z);
            Vector2 d = new Vector2(endB.x, endB.z);

            float Cross(Vector2 left, Vector2 right)
            {
                return left.x * right.y - left.y * right.x;
            }

            Vector2 ab = b - a;
            Vector2 cd = d - c;
            float denominator = Cross(ab, cd);
            if (Mathf.Abs(denominator) < 0.0001f)
            {
                return false;
            }

            Vector2 ac = c - a;
            float t = Cross(ac, cd) / denominator;
            float u = Cross(ac, ab) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private float GetWeakEnemyPushMaxDistance()
        {
            return GetFollowerTactic() switch
            {
                FollowerCombatTactic.Balanced => WeakEnemyPushDefaultMaxDistance,
                FollowerCombatTactic.Marksman => WeakEnemyPushMarksmanMaxDistance,
                FollowerCombatTactic.Protector => WeakEnemyPushProtectorMaxDistance,
                FollowerCombatTactic tactic => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        public Vector3 GetBossPosition()
        {
            return FollowerCombatAnchor.GetAnchorPosition(botOwner);
        }

        public Vector3 GetRealBossPosition()
        {
            return FollowerCombatAnchor.GetRealBossPosition(botOwner);
        }

        /// <summary>
        /// Returns boss distance using path distance first. Boss leash decisions should not use only
        /// straight-line distance because floors, doors, and building routes can make a nearby 3D
        /// position tactically far away.
        /// </summary>
        public float GetBossNavDistance(Vector3 bossPosition)
        {
            return Utils.Utils.GetNavDistance(botOwner.Position, bossPosition);
        }

        public bool ShouldDeferAutonomousRegroupAfterRecentFight(
            EnemyInfo? goalEnemy,
            float followerBossDistance,
            float regroupTriggerDistance)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                !IsFinite(followerBossDistance) ||
                !IsFinite(regroupTriggerDistance) ||
                regroupTriggerDistance <= 0f)
            {
                return false;
            }

            if (IsAutonomousRegroupDistanceExtreme(followerBossDistance, regroupTriggerDistance))
            {
                return false;
            }

            if (goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return true;
            }

            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 1.5f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner))
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalSeenTime <= AutonomousRegroupRecentFightGraceSeconds ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= AutonomousRegroupRecentFightGraceSeconds;
        }

        public bool IsAutonomousRegroupDistanceExtreme(float followerBossDistance, float regroupTriggerDistance)
        {
            return IsFinite(followerBossDistance) &&
                   IsFinite(regroupTriggerDistance) &&
                   regroupTriggerDistance > 0f &&
                   followerBossDistance >= regroupTriggerDistance * AutonomousRegroupExtremeDistanceMultiplier;
        }

        /// <summary>
        /// Shared boss/follower/enemy spacing snapshot used by combat objective logic.
        /// This lets the higher-level combat tree compare who currently owns the forward line:
        /// the boss or the follower.
        /// </summary>
        public bool TryGetBossRelativeCombatSpacing(
            EnemyInfo goalEnemy,
            out Vector3 bossPosition,
            out Vector3 enemyAnchor,
            out float followerBossDistance,
            out float followerEnemyDistance,
            out float bossEnemyDistance)
        {
            bossPosition = GetBossPosition();
            enemyAnchor = GetEnemyAnchor(goalEnemy);
            followerBossDistance = 0f;
            followerEnemyDistance = 0f;
            bossEnemyDistance = 0f;

            if (!IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            followerBossDistance = GetBossNavDistance(bossPosition);
            followerEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            bossEnemyDistance = Vector3.Distance(bossPosition, enemyAnchor);
            return true;
        }

        /// <summary>
        /// Finds a step cover that moves the follower toward the boss while optionally requiring
        /// either a shoot lane or a hide lane from the active enemy.
        /// Used by the boss-relative combat objective so rejoin/retreat movement is cover-to-cover
        /// instead of a blind run straight at the boss.
        /// </summary>
        public bool TryFindCoverTowardBoss(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            bool requireShootLane,
            bool requireHideFromEnemy,
            out CustomNavigationPoint? cover)
        {
            return TryFindCoverTowardBoss(
                goalEnemy,
                bossPosition,
                searchRadius,
                requireShootLane,
                requireHideFromEnemy,
                keepBehindBoss: false,
                out cover);
        }

        public bool TryFindCoverTowardBoss(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            bool requireShootLane,
            bool requireHideFromEnemy,
            bool keepBehindBoss,
            out CustomNavigationPoint? cover)
        {
            cover = null;
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            LayerMask mask = botOwner.LookSensor.Mask;
            BotsGroup.BotCurrentTactic tactic = requireShootLane
                ? BotsGroup.BotCurrentTactic.Attack
                : BotsGroup.BotCurrentTactic.Protect;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                tactic,
                requireShootLane ? CoverShootType.shoot : CoverShootType.hide,
                requireShootLane ? CoverSearchIntent.AttackMoving : CoverSearchIntent.RunToCover);

            // The selector still comes from our custom cover path. The forward direction is bossward,
            // while the extra checks decide whether this step is a fighting cover or a retreat cover.
            CustomNavigationPoint? candidate = Covers.GetClosestCoverPointTowardPoint(
                botOwner,
                botOwner.Position,
                bossPosition,
                searchRadius,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if (requireHideFromEnemy &&
                        IsFinite(enemyAnchor) &&
                        !point.CanIHideFromPos(0f, true, false, enemyAnchor))
                    {
                        return false;
                    }

                    if (shootPoint != null &&
                        !Utils.Utils.CanShootToTarget(shootPoint, point, mask, false))
                    {
                        return false;
                    }

                    if (keepBehindBoss &&
                        !IsSupportPositionBehindBossLine(point.Position, bossPosition, enemyAnchor))
                    {
                        return false;
                    }

                    if (!IsCoverSafeFromAlternateThreats(point, goalEnemy.ProfileId, strict: keepBehindBoss))
                    {
                        return false;
                    }

                    return true;
                },
                searchTypeOverride: searchType);

            if (candidate == null)
            {
                return false;
            }

            cover = candidate;
            return true;
        }

        public bool TryCommitPushSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 enemyPosition,
            Vector3 watchedDestination,
            string reason,
            out string committedReason)
        {
            committedReason = reason;
            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = FindPushSupportCover(
                goalEnemy,
                pushOwnerPosition,
                enemyPosition,
                requireEnemyShootLane: true,
                avoidBossFireLane: true);
            if (cover == null)
            {
                cover = FindPushSupportCover(
                    goalEnemy,
                    pushOwnerPosition,
                    watchedDestination,
                    requireEnemyShootLane: false,
                    avoidBossFireLane: true);
                committedReason += ".watchDestination";
            }
            else
            {
                committedReason += ".shootEnemy";
            }

            if (cover != null)
            {
                return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
            }

            return TryCommitFiringPositionCover(
                goalEnemy,
                reason + ".fallbackFirePosition",
                out committedReason,
                preferPointToShoot: true,
                preferInbetween: true,
                avoidBossFireLane: true);
        }

        private CustomNavigationPoint? FindPushSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 targetPosition,
            bool requireEnemyShootLane,
            bool keepBehindBoss = false,
            bool avoidBossFireLane = false)
        {
            if (!IsFinite(targetPosition))
            {
                return null;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            ShootPointClass targetPoint = new ShootPointClass(targetPosition + Vector3.up * 1.1f, 1f);
            LayerMask mask = botOwner.LookSensor.Mask;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.Attack);

            bool IsEligible(CustomNavigationPoint point, bool rejectBossFireLane)
            {
                if (!IsCoverUsable(point))
                {
                    return false;
                }

                if (!IsTeamSearchSupportPosition(point.Position, pushOwnerPosition, enemyAnchor))
                {
                    return false;
                }

                if (rejectBossFireLane &&
                    IsBossFireLaneMovementRisk(point.Position, enemyAnchor, includePath: true))
                {
                    return false;
                }

                if (requireEnemyShootLane &&
                    IsFinite(enemyAnchor) &&
                    !point.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    return false;
                }

                if (keepBehindBoss &&
                    !IsSupportPositionBehindBossLine(point.Position, pushOwnerPosition, enemyAnchor))
                {
                    return false;
                }

                if (!IsCoverSafeFromAlternateThreats(point, goalEnemy.ProfileId, strict: keepBehindBoss))
                {
                    return false;
                }

                return Utils.Utils.CanShootToTarget(targetPoint, point, mask, false);
            }

            CustomNavigationPoint? cover = null;
            if (avoidBossFireLane)
            {
                cover = Covers.GetClosestCoverPoint(
                    botOwner,
                    botOwner.Position,
                    60f,
                    point => IsEligible(point, rejectBossFireLane: true),
                    searchType);
            }

            return cover ?? Covers.GetClosestCoverPoint(
                botOwner,
                botOwner.Position,
                60f,
                point => IsEligible(point, rejectBossFireLane: false),
                searchType);
        }

        public bool TryCreateTeamSearchSupportDecision(
            CombatEvents.PushEvent pushEvent,
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!TryFindTeamSearchSupportPoint(pushEvent.Owner.Position, GetEnemyAnchor(goalEnemy), out Vector3 supportPoint))
            {
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            return true;
        }

        private bool TryFindTeamSearchSupportPoint(Vector3 pushOwnerPosition, Vector3 enemyAnchor, out Vector3 supportPoint)
        {
            supportPoint = default;
            if (!IsFinite(pushOwnerPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 forward = enemyAnchor - pushOwnerPosition;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                return false;
            }

            forward.Normalize();
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            Vector3[] candidates =
            {
                pushOwnerPosition - forward * 8f,
                pushOwnerPosition - forward * 6f + side * 5f,
                pushOwnerPosition - forward * 6f - side * 5f,
                pushOwnerPosition - forward * 10f + side * 8f,
                pushOwnerPosition - forward * 10f - side * 8f,
                pushOwnerPosition + side * 8f,
                pushOwnerPosition - side * 8f
            };

            float bestScore = float.MaxValue;
            bool found = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!NavMesh.SamplePosition(candidates[i], out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    continue;
                }

                if (!IsTeamSearchSupportPosition(hit.position, pushOwnerPosition, enemyAnchor))
                {
                    continue;
                }

                float selfDistance = Vector3.Distance(botOwner.Position, hit.position);
                float ownerDistance = Vector3.Distance(pushOwnerPosition, hit.position);
                float lanePenalty = IsBossFireLaneMovementRisk(hit.position, enemyAnchor, includePath: true)
                    ? BossFireLaneSoftPenalty
                    : 0f;
                float score = selfDistance + ownerDistance * 0.35f + lanePenalty;
                if (score < bestScore)
                {
                    supportPoint = hit.position;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private static bool IsTeamSearchSupportPosition(Vector3 candidate, Vector3 pushOwnerPosition, Vector3 enemyAnchor)
        {
            if (!IsFinite(candidate) || !IsFinite(pushOwnerPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 ownerToEnemy = enemyAnchor - pushOwnerPosition;
            ownerToEnemy.y = 0f;
            if (ownerToEnemy.sqrMagnitude < 0.01f)
            {
                return false;
            }

            ownerToEnemy.Normalize();
            Vector3 ownerToCandidate = candidate - pushOwnerPosition;
            ownerToCandidate.y = 0f;
            float ahead = Vector3.Dot(ownerToCandidate, ownerToEnemy);
            if (ahead > 1.5f)
            {
                return false;
            }

            float ownerEnemyDistance = Vector3.Distance(pushOwnerPosition, enemyAnchor);
            float candidateEnemyDistance = Vector3.Distance(candidate, enemyAnchor);
            return candidateEnemyDistance >= ownerEnemyDistance - 2f;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> ConsumeInitialDecision()
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision = initialDecision ??
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "missingInitialDecision");
            initialDecision = null;
            return decision;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? PreFightLogic()
        {
            if (ShouldPrioritizeEmergencyHeal())
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? emergencyHealDecision = TryGetNeedHealDecision();
                if (emergencyHealDecision != null)
                {
                    initialDecision = null;
                    return emergencyHealDecision;
                }
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                initialDecision = null;
                return dogFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = InFightLogic();
            if (inFightDecision != null)
            {
                initialDecision = null;
                return inFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = TryGetNeedHealDecision();
            if (healDecision != null)
            {
                initialDecision = null;
                return healDecision;
            }

            return null;
        }

        private bool ShouldPrioritizeEmergencyHeal()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            bool haveHealWork =
                botOwner.Medecine.FirstAid?.Have2Do == true ||
                botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                botOwner.Medecine.FirstAid?.Using == true ||
                botOwner.Medecine.SurgicalKit?.Using == true;
            if (!haveHealWork)
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            return healthStatus == ETagStatus.BadlyInjured ||
                   healthStatus == ETagStatus.Dying ||
                   IsFollowerCriticallyWounded();
        }

        /// <summary>
        /// Standalone in-cover ally support check.
        /// Allows follower to switch targets and support an actively engaged allied enemy
        /// when:
        /// 1. Follower is in cover and stably held position (≥1s)
        /// 2. Current goal enemy is not visible or does not exist
        /// 3. Not under direct fire
        /// 4. An ally is clearly engaging an enemy (visible, shootable)
        /// 5. Support cover for that engagement exists within reasonable distance
        /// 
        /// Prevents flip-flopping by:
        /// - Requiring minimum cover duration
        /// - Checking recent enemy-seen time (don't abandon hot targets)
        /// - Requiring good support cover availability
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetAllyEngagementSupportDecision(bool selfSupport = false)
        {

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            if (selfSupport && goalEnemy == null)
            {
                return null;
            }

            // Gate 1: Must be in cover and have held it for stability
            if (!selfSupport)
            {
                if (!botOwner.Memory.IsInCover)
                {
                    inCoverSince = 0f;
                    return null;
                }

                if (inCoverSince <= 0f)
                {
                    inCoverSince = Time.time;
                }

                if (Time.time - inCoverSince < 1f)
                {
                    return null;
                }
            }


            // Gate 2: Current enemy conditions allow switching

            // If we can see current enemy, don't switch away
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                return null;
            }

            // If under active fire, need to stay focused on threat
            if (botOwner.Memory.IsUnderFire && !selfSupport)
            {
                return null;
            }

            // If current enemy was recently seen, maintain focus (avoid flip-flopping)
            if (goalEnemy != null && Time.time - goalEnemy.PersonalLastSeenTime < 2.5f && !selfSupport)
            {
                return null;
            }

            // Gate 3: An ally must be clearly engaging an enemy (visible + shootable = credible threat)
            string supportEnemyProfileId;
            Vector3 supportEnemyPosition;
            if (selfSupport)
            {
                if (goalEnemy == null)
                {
                    return null;
                }

                supportEnemyPosition = goalEnemy.CurrPosition;
                supportEnemyProfileId = goalEnemy.ProfileId;
            }
            else if (!TryGetAllyEngagementEnemy(out supportEnemyProfileId, out supportEnemyPosition))
            {
                return null;
            }

            if (!TrySelectPreferredSupportEnemy(supportEnemyProfileId, supportEnemyPosition, out EnemyInfo? selectedEnemy))
            {
                return null;
            }

            // Support should own a real committed cover, not a one-frame move order that the next
            // branch pass can immediately replace.
            bool preferBackline = GetFollowerTactic() is FollowerCombatTactic.Marksman or FollowerCombatTactic.Protector;
            bool enforceMarksmanPositionPolicy = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            bool allowMarksmanBattlefieldPosition = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            if (!TryCommitSupportFiringCover(
                    selectedEnemy,
                    "allySupportCover",
                    out string committedReason,
                    preferBackline,
                    enforceMarksmanPositionPolicy))
            {
                if (!TryCreateSupportFiringPositionDecision(
                        selectedEnemy,
                        supportEnemyPosition,
                        "allySupportPosition",
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> positionDecision,
                        preferBackline,
                        enforceMarksmanPositionPolicy,
                        allowForwardPositions: false,
                        allowBattlefieldPositions: allowMarksmanBattlefieldPosition,
                        maxNavDistance: allowMarksmanBattlefieldPosition ? 90f : 45f))
                {
                    return null;
                }

                if (!string.IsNullOrEmpty(selectedEnemy.ProfileId))
                {
                    TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
                }

                return positionDecision;
            }

            if (!string.IsNullOrEmpty(selectedEnemy.ProfileId))
            {
                TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
            }

            return CreateMoveToCommittedCoverDecision(committedReason);
        }

        public bool TryCreateSupportFiringPositionDecision(
            EnemyInfo supportEnemy,
            Vector3 supportEnemyPosition,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            bool allowForwardPositions = false,
            bool allowBattlefieldPositions = false,
            float maxNavDistance = 45f)
        {
            decision = default;
            if (!HasActiveCombatEnemy(supportEnemy))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(supportEnemy, supportEnemyPosition);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            if (!TryFindSupportFiringPosition(
                    supportEnemy,
                    enemyAnchor,
                    preferBackline,
                    enforceMarksmanPositionPolicy,
                    allowForwardPositions,
                    allowBattlefieldPositions,
                    maxNavDistance,
                    out Vector3 supportPoint))
            {
                return false;
            }

            BotLogicDecision moveDecision;
            string moveReason;
            if (!TrySelectSupportFiringPositionMove(enemyAnchor, supportPoint, reason, out moveDecision, out moveReason))
            {
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveDecision,
                moveReason);
            return true;
        }

        public bool TryCreateFiringPositionDecisionAt(
            EnemyInfo supportEnemy,
            Vector3 enemyPosition,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            bool allowForwardPositions = false,
            bool allowBattlefieldPositions = false,
            float maxNavDistance = 45f)
        {
            decision = default;
            if (!HasActiveCombatEnemy(supportEnemy) || !IsFinite(enemyPosition))
            {
                return false;
            }

            if (!TryFindSupportFiringPosition(
                    supportEnemy,
                    enemyPosition,
                    preferBackline,
                    enforceMarksmanPositionPolicy,
                    allowForwardPositions,
                    allowBattlefieldPositions,
                    maxNavDistance,
                    out Vector3 supportPoint))
            {
                return false;
            }

            if (!TrySelectSupportFiringPositionMove(enemyPosition, supportPoint, reason, out BotLogicDecision moveDecision, out string moveReason))
            {
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveDecision,
                moveReason);
            return true;
        }

        private bool TrySelectSupportFiringPositionMove(
            Vector3 enemyAnchor,
            Vector3 supportPoint,
            string reason,
            out BotLogicDecision moveDecision,
            out string moveReason)
        {
            moveDecision = default;
            moveReason = string.Empty;

            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float supportEnemyDistance = Vector3.Distance(supportPoint, enemyAnchor);
            bool increasesEnemyDistance = supportEnemyDistance >= currentEnemyDistance + 2f;
            float pointNavDistance = Utils.Utils.GetNavDistance(botOwner.Position, supportPoint);
            if (!IsFinite(pointNavDistance))
            {
                pointNavDistance = Vector3.Distance(botOwner.Position, supportPoint);
            }

            bool enemyClose = currentEnemyDistance <= 35f;
            bool pointClose = pointNavDistance <= 18f;
            bool pathSafe = IsSupportRunPathSafe(supportPoint, enemyAnchor);

            if (enemyClose && (pointClose || !pathSafe))
            {
                moveDecision = BotLogicDecision.goToPoint;
                moveReason = $"{reason}.goToPoint";
                return true;
            }

            if (increasesEnemyDistance &&
                CanSprintForCombatMovement() &&
                CanRunToEnemyNow() &&
                (!enemyClose || pathSafe))
            {
                moveDecision = BotLogicDecision.goToPoint;
                moveReason = $"{reason}.runToPoint";
                return true;
            }

            moveDecision = BotLogicDecision.goToPoint;
            moveReason = $"{reason}.goToPoint";
            return true;
        }

        private bool IsSupportRunPathSafe(Vector3 supportPoint, Vector3 enemyAnchor)
        {
            if (Covers.IsPathExposedToEnemy(botOwner.Position, supportPoint, enemyAnchor, botOwner.LookSensor.Mask, sampleCount: 5))
            {
                return false;
            }

            return !Covers.IsPathTooCloseToEnemy(
                botOwner.Position,
                supportPoint,
                enemyAnchor,
                CombatDistanceConfiguration.Instance.GetCloseQuarterDistance());
        }

        private bool TryFindSupportFiringPosition(
            EnemyInfo supportEnemy,
            Vector3 enemyAnchor,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy,
            bool allowForwardPositions,
            bool allowBattlefieldPositions,
            float maxNavDistance,
            out Vector3 supportPoint)
        {
            supportPoint = Vector3.zero;
            Vector3 bossPosition = GetBossPosition();
            Vector3 anchor = IsFinite(bossPosition) ? bossPosition : botOwner.Position;
            Vector3 anchorToEnemy = enemyAnchor - anchor;
            anchorToEnemy.y = 0f;
            if (anchorToEnemy.sqrMagnitude < 0.01f)
            {
                anchorToEnemy = enemyAnchor - botOwner.Position;
                anchorToEnemy.y = 0f;
            }

            if (anchorToEnemy.sqrMagnitude < 0.01f)
            {
                return false;
            }

            anchorToEnemy.Normalize();
            Vector3 side = new Vector3(-anchorToEnemy.z, 0f, anchorToEnemy.x);
            List<Vector3> candidates = new List<Vector3>
            {
                anchor - anchorToEnemy * 10f,
                anchor - anchorToEnemy * 14f,
                anchor - anchorToEnemy * 10f + side * 8f,
                anchor - anchorToEnemy * 10f - side * 8f,
                anchor - anchorToEnemy * 16f + side * 10f,
                anchor - anchorToEnemy * 16f - side * 10f,
                botOwner.Position + side * 8f,
                botOwner.Position - side * 8f,
                botOwner.Position - anchorToEnemy * 6f,
                allowForwardPositions ? anchor + anchorToEnemy * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 10f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 8f + side * 7f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 8f - side * 7f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f + side * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f - side * 6f : Vector3.positiveInfinity
            };

            if (allowBattlefieldPositions)
            {
                AddBattlefieldFiringCandidates(candidates, anchor, botOwner.Position, enemyAnchor, anchorToEnemy, side);
            }

            ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
            Vector3 weaponOffset = Vector3.up * 1.2f;
            float bestScore = float.MaxValue;
            bool found = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (!IsFinite(candidates[i]))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidates[i], out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    continue;
                }

                Vector3 candidate = hit.position;
                if (Vector3.Distance(candidate, enemyAnchor) < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
                {
                    continue;
                }

                if (enforceMarksmanPositionPolicy &&
                    !IsMarksmanFiringPositionAllowed(supportEnemy, candidate))
                {
                    continue;
                }

                if (preferBackline &&
                    IsFinite(bossPosition) &&
                    !IsSupportPositionBehindBossLine(candidate, bossPosition, enemyAnchor))
                {
                    continue;
                }

                if (!IsSupportPositionSafeFromAlternateThreats(candidate, supportEnemy.ProfileId, strict: preferBackline))
                {
                    continue;
                }

                if (!Utils.Utils.CanShootToTarget(shootPoint, candidate + weaponOffset, botOwner.LookSensor.Mask, false))
                {
                    continue;
                }

                float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, candidate);
                if (!IsFinite(navDistance))
                {
                    navDistance = Vector3.Distance(botOwner.Position, candidate);
                }

                if (navDistance > maxNavDistance)
                {
                    continue;
                }

                float bossDistance = IsFinite(bossPosition) ? Vector3.Distance(candidate, bossPosition) : 0f;
                float lanePenalty = IsBossFireLaneMovementRisk(candidate, enemyAnchor, includePath: true)
                    ? BossFireLaneSoftPenalty
                    : 0f;
                float score = navDistance + bossDistance * 0.35f + lanePenalty;
                if (score < bestScore)
                {
                    supportPoint = candidate;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private void AddBattlefieldFiringCandidates(
            List<Vector3> candidates,
            Vector3 anchor,
            Vector3 botPosition,
            Vector3 enemyAnchor,
            Vector3 anchorToEnemy,
            Vector3 side)
        {
            float safeFloor = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() + 8f;
            float anchorEnemyDistance = Vector3.Distance(anchor, enemyAnchor);
            float botEnemyDistance = Vector3.Distance(botPosition, enemyAnchor);

            AddForwardCandidateSet(candidates, anchor, anchorToEnemy, side, anchorEnemyDistance, safeFloor, 24f, 36f, 50f, 70f, 95f);
            AddForwardCandidateSet(candidates, botPosition, anchorToEnemy, side, botEnemyDistance, safeFloor, 24f, 40f, 60f, 85f, 115f);
        }

        private static void AddForwardCandidateSet(
            List<Vector3> candidates,
            Vector3 origin,
            Vector3 direction,
            Vector3 side,
            float enemyDistance,
            float safeFloor,
            params float[] forwardDistances)
        {
            for (int i = 0; i < forwardDistances.Length; i++)
            {
                float distance = forwardDistances[i];
                if (distance >= enemyDistance - safeFloor)
                {
                    continue;
                }

                Vector3 forwardPoint = origin + direction * distance;
                float sideOffset = Mathf.Clamp(distance * 0.2f, 8f, 16f);
                candidates.Add(forwardPoint);
                candidates.Add(forwardPoint + side * sideOffset);
                candidates.Add(forwardPoint - side * sideOffset);
            }
        }

        public bool IsMarksmanFiringPositionAllowed(EnemyInfo goalEnemy, Vector3 position)
        {
            if (IsEnemyMarksman(goalEnemy))
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(goalEnemy, goalEnemy.CurrPosition);
            if (!IsFinite(position) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float positionEnemyDistance = Vector3.Distance(position, enemyAnchor);
            if (positionEnemyDistance + 1.5f >= currentEnemyDistance)
            {
                return true;
            }

            float safeFloor = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() + 5f;
            float aggression = GetAggression01();
            if (aggression > 0.55f)
            {
                return positionEnemyDistance >= safeFloor;
            }

            Vector3 bossPosition = GetBossPosition();
            if (IsFinite(bossPosition) &&
                IsSupportPositionBehindBossLine(position, bossPosition, enemyAnchor))
            {
                return true;
            }

            bool enemyClose = currentEnemyDistance <= 35f;
            float distanceReduction = currentEnemyDistance - positionEnemyDistance;
            return enemyClose &&
                   distanceReduction <= 6f &&
                   positionEnemyDistance >= safeFloor;
        }

        private bool IsSupportPositionSafeFromAlternateThreats(Vector3 position, string? primaryEnemyProfileId, bool strict)
        {
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return true;
            }

            Vector3 firePosition = position + Vector3.up * 1.2f;
            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!HasActiveCombatEnemy(enemyInfo) ||
                    string.Equals(enemyInfo.ProfileId, primaryEnemyProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 enemyAnchor = GetEnemyAnchor(enemyInfo);
                if (!IsFinite(enemyAnchor))
                {
                    continue;
                }

                bool dangerousThreat =
                    enemyInfo.CanShoot ||
                    enemyInfo.IsVisible ||
                    Time.time - enemyInfo.PersonalLastSeenTime <= 3f;
                if (!dangerousThreat)
                {
                    continue;
                }

                if (strict && Vector3.Distance(position, enemyAnchor) < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
                {
                    return false;
                }

                ShootPointClass threatShootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                if (Utils.Utils.CanShootToTarget(threatShootPoint, firePosition, botOwner.LookSensor.Mask, false))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TrySelectPreferredSupportEnemy(
            string requestedEnemyProfileId,
            Vector3 requestedEnemyPosition,
            out EnemyInfo? selectedEnemy,
            bool preferBackline = false,
            bool promoteSelected = true)
        {
            selectedEnemy = null;

            EnemyInfo? requestedEnemy = GetTrackedEnemyByProfileId(requestedEnemyProfileId);
            EnemyInfo? currentEnemy = botOwner.Memory?.GoalEnemy;

            float requestedScore = ScoreSupportEnemy(requestedEnemy, requestedEnemyPosition, preferBackline);
            float currentScore = ScoreSupportEnemy(currentEnemy, GetEnemyAnchorOrFallback(currentEnemy, requestedEnemyPosition), preferBackline);

            EnemyInfo? bestKnownEnemy = null;
            float bestKnownScore = float.MinValue;
            if (botOwner.EnemiesController?.EnemyInfos != null)
            {
                foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
                {
                    float score = ScoreSupportEnemy(enemyInfo, GetEnemyAnchorOrFallback(enemyInfo, Vector3.zero), preferBackline);
                    if (score > bestKnownScore)
                    {
                        bestKnownEnemy = enemyInfo;
                        bestKnownScore = score;
                    }
                }
            }

            selectedEnemy = requestedScore >= currentScore ? requestedEnemy : currentEnemy;
            float selectedScore = Mathf.Max(requestedScore, currentScore);
            if (bestKnownScore > selectedScore + 1.5f)
            {
                selectedEnemy = bestKnownEnemy;
                selectedScore = bestKnownScore;
            }

            if (!HasActiveCombatEnemy(selectedEnemy))
            {
                return false;
            }

            if (promoteSelected && !string.IsNullOrEmpty(selectedEnemy.ProfileId))
            {
                TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
            }

            return true;
        }

        public void PrepareStartDecision(float aggression)
        {
            initialDecision = null;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            bool haveCover = TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? startCover, out float startCoverNavDistance, out bool startCoverHasShootLane);
            bool closeCover = haveCover &&
                              startCoverNavDistance <= CombatDistanceConfiguration.Instance.GetStartCloseCoverDistance();
            bool farCover = haveCover && !closeCover;

            // Decision 1: enemy visible + close shooting cover -> attack-moving into that cover.
            // Marksman enemies are a special case: default riflemen should not generic
            // attack-move around them, because elevated marksmen often cannot be reached
            // safely. Let the tactic planner pick a firing position instead.
            if (!IsEnemyMarksman(goalEnemy) &&
                goalEnemy.IsVisible &&
                closeCover &&
                startCover != null &&
                startCover.CanIShootToEnemy)
            {
                SetCover(startCover);
                BotLogicDecision action = BotLogicDecision.attackMoving;
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    action,
                    CreateMovementReason("startVisCloseCover", action));
                return;
            }

            // Decision 2: enemy unseen + under fire.
            // If close cover exists -> move with suppressive fire.
            // Else if far cover exists -> run to cover.
            // Else -> hold lane with suppressive fire in place.
            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                if (closeCover)
                {
                    SetCover(startCover);
                    BotLogicDecision action = BotLogicDecision.attackMovingWithSuppress;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        action,
                        CreateMovementReason("startSuppressionCover", action));
                    return;
                }

                if (farCover)
                {
                    SetCover(startCover);
                    BotLogicDecision action = BotLogicDecision.runToCover;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        action,
                        CreateMovementReason("startUnderFireCover", action));
                    return;
                }

                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    CreateMovementReason("startUnderFire", BotLogicDecision.suppressFire));
                return;
            }

            // Decision 3: enemy unseen, not under fire, and allies are actively engaging -> support from shooting cover.
            if (!goalEnemy.IsVisible && !botOwner.Memory.IsUnderFire && TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition))
            {
                TryPromoteTrackedEnemyAsGoal(supportEnemyProfileId);

                if (TryGetSupportCover(supportEnemyPosition, out CustomNavigationPoint? supportCover, out float supportCoverNavDistance))
                {
                    SetCover(supportCover);
                    BotLogicDecision supportDecision = supportCoverNavDistance <= StartSupportSuppressDistance
                        ? BotLogicDecision.attackMovingWithSuppress
                        : BotLogicDecision.runToCover;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        supportDecision,
                        CreateMovementReason("startAllySupport", supportDecision));
                    return;
                }
            }

            // Decision 4: enemy unseen and low threat -> close pressure/push.
            if (!goalEnemy.IsVisible &&
                IsEnemyLowThreat(goalEnemy, aggression > 0.6f, aggression >= 0.8f ? 2f : 1f) &&
                IsWeakEnemyAutoPushRoleAllowed(goalEnemy))
            {

                initialDecision = EnemySearch("startWeakEnemyPush.tactical", true);
                return;
            }

            // Decision 5: any far cover opportunity at combat start -> run to cover.
            if (farCover)
            {
                SetCover(startCover);
                BotLogicDecision action = BotLogicDecision.runToCover;
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    action,
                    CreateMovementReason(goalEnemy.IsVisible ? "startVisFarCover" : "startBlindFarCover", action));
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? InFightLogic()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            AICoreActionResultStruct<BotLogicDecision, GClass26>? shootNowDecision = TryGetImmediateShootDecision("ShootImmediately");
            if (shootNowDecision != null)
            {
                return shootNowDecision;
            }

            if (CanShootFromCurrentCover(out string cause))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, cause);
            }

            if (botOwner.NearDoorData.RecentlyClosedDoorCheckTime + 0.3f < Time.time &&
                botOwner.BotsGroup.EnemyLastSeenTimeReal + 7f >= Time.time &&
                goalEnemy != null &&
                EnemyPathCrossesRecentDoor(goalEnemy))
            {
                botOwner.Memory.Spotted(false, null, null);
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetDogFightDecision()
        {
            EnemyInfo goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                ClearDogFightState();
                return null;
            }

            if (ShouldSeekReloadRetreat(goalEnemy))
            {
                ClearDogFightState();
                return null;
            }

            bool hasLiveVisibleDogFightContact = goalEnemy.IsVisible && goalEnemy.CanShoot;
            if (!hasLiveVisibleDogFightContact)
            {
                if (IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy))
                {
                    SetDogFightState(BotDogFightStatus.dogFight);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pointBlankContactDogFight");
                }

                if (!IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatDogFightDistance, CloseThreatRecentSeenSeconds))
                {
                    ClearDogFightState();
                    return null;
                }

                SetDogFightState(BotDogFightStatus.dogFight);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "closeThreatDogFight");
            }

            BotDogFightStatus dogFightState = botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            bool canUseDogFight = CanUseDogFightNow(goalEnemy);
            if (Time.time < dogFightBlockedUntil)
            {
                SetDogFightState(BotDogFightStatus.shootFromPlace);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgCooldownFire");
            }

            if (ShouldUseCloseVisibleDogFight(goalEnemy, dogFightState))
            {
                SetDogFightState(BotDogFightStatus.dogFight);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "closeVisibleDogFight");
            }

            if (dogFightState == BotDogFightStatus.dogFight)
            {
                if (canUseDogFight)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
                }

                SetDogFightState(BotDogFightStatus.shootFromPlace);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgOutOfRangeFire");
            }

            if (dogFightState == BotDogFightStatus.shootFromPlace)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (TryPromoteDogFightState(goalEnemy, out dogFightState))
            {
                return dogFightState == BotDogFightStatus.dogFight
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < 18f &&
                goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_IN)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgNoPlace");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose &&
                canUseDogFight)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "enemyVeryClose");
            }

            return null;
        }

        public bool TryGetReloadRetreatDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!ShouldSeekReloadRetreat(goalEnemy))
            {
                return false;
            }

            if (botOwner.Memory.IsInCover)
            {
                TryStartCombatReload();
                HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "reloadInCover");
                return true;
            }

            if (HasCommittedPosition(out decision))
            {
                return true;
            }

            if (HasCommittedCover())
            {
                AssignCommittedCover();
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            if (TryCommitCombatCover(
                    goalEnemy,
                    requireShootLane: false,
                    CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                    out string coverReason,
                    avoidBossFireLane: true))
            {
                decision = CreateMoveToCommittedCoverDecision($"reloadRetreat.{coverReason}");
                return true;
            }

            if (ShouldReloadInPlaceWithoutCover())
            {
                TryStartCombatReload();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "reloadNoCover");
                return true;
            }

            return false;
        }

        public bool ShouldSeekReloadRetreat(EnemyInfo? goalEnemy)
        {
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!IsUnsafeReloadThreat(goalEnemy))
            {
                return false;
            }

            return IsReloadingOrLowOnAmmo();
        }

        private bool IsUnsafeReloadThreat(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                goalEnemy.Distance <= ReloadRetreatThreatDistance)
            {
                return true;
            }

            if ((botOwner.Memory.IsUnderFire ||
                 WasHitRecently(botOwner, 0.75f) ||
                 FollowerAwareness.WasRecentlyThreatened(botOwner)) &&
                IsEnemyActivelyThreateningMe(goalEnemy, ReloadRetreatThreatDistance, CloseThreatRecentSeenSeconds))
            {
                return true;
            }

            return false;
        }

        private bool IsReloadingOrLowOnAmmo()
        {
            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            if (weaponManager?.Reload == null)
            {
                return false;
            }

            if (weaponManager.Reload.Reloading || !weaponManager.HaveBullets)
            {
                return true;
            }

            int currentAmmo = weaponManager.Reload.BulletCount;
            int maxAmmo = weaponManager.Reload.MaxBulletCount;
            if (maxAmmo > 0 && currentAmmo > 0)
            {
                float ammoRatio = (float)currentAmmo / maxAmmo;
                if (ammoRatio <= ReloadRetreatAmmoRatio)
                {
                    return true;
                }
            }

            Weapon? activeWeapon = weaponManager.ShootController?.Item;
            int? magazineCount = activeWeapon?.GetCurrentMagazine()?.Cartridges?.Count;
            return magazineCount.HasValue && magazineCount.Value <= ReloadRetreatMinMagazineAmmo;
        }

        private bool ShouldReloadInPlaceWithoutCover()
        {
            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            if (weaponManager?.Reload == null)
            {
                return false;
            }

            if (weaponManager.Reload.Reloading || !weaponManager.HaveBullets)
            {
                return true;
            }

            Weapon? activeWeapon = weaponManager.ShootController?.Item;
            int? magazineCount = activeWeapon?.GetCurrentMagazine()?.Cartridges?.Count;
            return magazineCount.HasValue && magazineCount.Value <= 0;
        }

        private void TryStartCombatReload()
        {
            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotReload? reload = weaponManager?.Reload;
            if (reload == null ||
                reload.Reloading ||
                weaponManager?.ShootController?.CanStartReload() != true)
            {
                return;
            }

            reload.TryReload();
        }

        public bool TryPrepareCloseVisibleDogFightDecision(EnemyInfo? goalEnemy, string reason)
        {
            if (!ShouldUseCloseVisibleDogFight(goalEnemy, botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none))
            {
                return false;
            }

            SetDogFightState(BotDogFightStatus.dogFight);
            SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, reason));
            return true;
        }

        public bool TryPreparePointBlankDogFightDecision(EnemyInfo? goalEnemy, string reason)
        {
            if (!IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy))
            {
                return false;
            }

            SetDogFightState(BotDogFightStatus.dogFight);
            SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, reason));
            return true;
        }

        private bool CanUseDogFightNow(EnemyInfo goalEnemy)
        {
            return goalEnemy.Distance <= botOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT ||
                   botOwner.Memory.BotCurrentCoverInfo.UseDogFight(botOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE);
        }

        private static bool ShouldUseCloseVisibleDogFight(EnemyInfo? goalEnemy, BotDogFightStatus dogFightState)
        {
            if (goalEnemy == null || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            float maxDistance = dogFightState == BotDogFightStatus.dogFight
                ? CloseVisibleDogFightEndDistance
                : CloseVisibleDogFightStartDistance;
            return goalEnemy.Distance <= maxDistance;
        }

        private void SetDogFightState(BotDogFightStatus state)
        {
            if (botOwner?.DogFight == null)
            {
                return;
            }

            botOwner.DogFight.DogFightState = state;
            botOwner.DogFight.PursuitInProgress = false;
        }

        private void ClearDogFightState()
        {
            if (botOwner?.DogFight == null)
            {
                return;
            }

            botOwner.DogFight.DogFightState = BotDogFightStatus.none;
            botOwner.DogFight.PursuitInProgress = false;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetNeedHealDecision()
        {
            bool coverTried = false;

            if (botOwner.Medecine == null)
            {
                return null;
            }

            RefreshCombatHealWorkIfNeeded();

            if (!botOwner.Memory.HaveEnemy)
            {
                healBlockUntil = 0f;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do ||
                                botOwner.Medecine.SurgicalKit.HaveWork ||
                                botOwner.Medecine.FirstAid.Using ||
                                botOwner.Medecine.SurgicalKit.Using;
            var stims = botOwner.Medecine.Stimulators;
            bool shouldUseStim = stims?.HaveSmt == true &&
                                 Time.time - stims.LastEndUseTime > 3f &&
                                 stims.CanUseNow() &&
                                 botOwner.GetPlayer?.HealthStatus != ETagStatus.Healthy;

            if (botOwner.Medecine.Stimulators.Using)
            {
                if (stimStartedAt <= 0f)
                {
                    stimStartedAt = Time.time;
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
            }

            if (TryGetBlackStomachPainStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> painStimDecision))
            {
                return painStimDecision;
            }

            if (!haveHealWork)
            {
                ClearCommittedHealCover();

                if (shouldUseStim &&
                    goalEnemy != null &&
                    !goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > 1.5f)
                {
                    stimStartedAt = Time.time;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
                }

                return null;
            }

            if (healBlockUntil >= Time.time)
            {
                return null;
            }

            if (CanHealAtCommittedHealCover(goalEnemy))
            {
                if (TryGetHealCoverStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> healCoverStimDecision))
                {
                    return healCoverStimDecision;
                }

                if (healStartedAt <= 0f)
                {
                    healStartedAt = Time.time;
                }

                ClearCommittedHealCover();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
            }

            if (TryGetNoSprintHealContactFireDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> contactFireDecision))
            {
                return contactFireDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? committedHealMove = TryGetCommittedHealMoveDecision(goalEnemy);
            if (committedHealMove != null)
            {
                return committedHealMove;
            }

            if (goalEnemy == null ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit.Using)
            {
                if (goalEnemy == null)
                {
                    healBlockUntil = Time.time;
                }

                if (healStartedAt <= 0f)
                {
                    healStartedAt = Time.time;
                }
                ClearCommittedHealCover();

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
            }

            float lastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            bool enemyVisible = goalEnemy.IsVisible;
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);

            if (!enemyVisible && lastSeen > 3f)
            {
                if (botOwner.Memory.IsInCover && enemyProxyDistance > Enemy.ProxyDistance.VeryClose)
                {
                    if (healStartedAt <= 0f)
                    {
                        healStartedAt = Time.time;
                    }
                    ClearCommittedHealCover();
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> emergencyStimDecision))
                {
                    return emergencyStimDecision;
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (!enemyVisible && lastSeen <= 3f)
            {
                if (enemyProxyDistance > Enemy.ProxyDistance.Close)
                {
                    if (botOwner.Memory.IsInCover)
                    {
                        if (healStartedAt <= 0f)
                        {
                            healStartedAt = Time.time;
                        }
                        ClearCommittedHealCover();
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                    }

                    if (TryAssignHealCover(goalEnemy, ref coverTried))
                    {
                        return CreateCommittedHealMoveDecision(goalEnemy);
                    }

                    if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> closeRecentStimDecision))
                    {
                        return closeRecentStimDecision;
                    }

                    healBlockUntil = Time.time + 3f;
                    return null;
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> recentStimDecision))
                {
                    return recentStimDecision;
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (TryAssignHealCover(goalEnemy, ref coverTried))
            {
                return CreateCommittedHealMoveDecision(goalEnemy);
            }

            if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackStimDecision))
            {
                return fallbackStimDecision;
            }

            healBlockUntil = Time.time + 3f;
            return null;
        }

        public static bool IsEnemyMarksman(EnemyInfo? goalEnemy)
        {
            return goalEnemy?.Person?.Profile?.Info?.Settings?.Role == WildSpawnType.marksman;
        }

        private void RefreshCombatHealWorkIfNeeded()
        {
            if (botOwner?.Medecine == null ||
                botOwner.GetPlayer?.ActiveHealthController == null ||
                botOwner.HealthController?.IsAlive != true ||
                botOwner.Medecine.Using ||
                botOwner.Medecine.FirstAid?.Have2Do == true ||
                botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                Time.time < nextCombatHealWorkRefreshAt)
            {
                return;
            }

            bool shouldRefresh = botOwner.GetPlayer.HealthStatus != ETagStatus.Healthy;
            if (!shouldRefresh)
            {
                foreach (EBodyPart part in GClass3058.RealBodyParts)
                {
                    if (botOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                    {
                        shouldRefresh = true;
                        break;
                    }
                }
            }

            if (!shouldRefresh)
            {
                return;
            }

            nextCombatHealWorkRefreshAt = Time.time + 1f;
            try
            {
                botOwner.Medecine.RefreshCurMeds();
                botOwner.Medecine.GetDamaged();
                botOwner.Medecine.SurgicalKit?.FindDamagedPart();
                botOwner.Medecine.FirstAid?.CheckParts();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"Combat heal-work refresh failed for {botOwner.Profile?.Nickname ?? botOwner.name ?? "unknown"}: {ex.Message}");
            }
        }

        private bool TryGetHealCoverStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CanUseStimulatorNow(out GClass491 stims))
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            if ((healthStatus == ETagStatus.BadlyInjured || healthStatus == ETagStatus.Dying) &&
                TrySelectPositiveHealthRateStimulator(stims))
            {
                stimStartedAt = Time.time;
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.healStimulators,
                    "healCoverHealthStim");
                return true;
            }

            if (ShouldUsePainStimForDestroyedPartAtHealCover() &&
                TrySelectPainStimulator(stims))
            {
                stimStartedAt = Time.time;
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.healStimulators,
                    "healCoverBlackLimbPainStim");
                return true;
            }

            return false;
        }

        private bool TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (TryGetHealCoverStimDecision(out decision))
            {
                botOwner.SetPose(0.5f);
                return true;
            }

            return false;
        }

        private bool TryGetBlackStomachPainStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            Player player = botOwner.GetPlayer;
            if (player == null ||
                player.ActiveHealthController?.IsBodyPartDestroyed(EBodyPart.Stomach) != true ||
                player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) == true)
            {
                return false;
            }

            GClass491 stims = botOwner.Medecine.Stimulators;
            if (!CanUseStimulatorNow(out stims))
            {
                return false;
            }

            if (!TrySelectPainStimulator(stims))
            {
                return false;
            }

            stimStartedAt = Time.time;
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.healStimulators,
                "blackStomachPainStim");
            return true;
        }

        private bool CanUseStimulatorNow(out GClass491 stims)
        {
            stims = botOwner.Medecine?.Stimulators;
            return stims != null &&
                   !stims.Using &&
                   Time.time - stims.LastEndUseTime > 3f &&
                   stims.CanUseNow() &&
                   botOwner.WeaponManager?.Reload?.Reloading != true;
        }

        private bool ShouldUsePainStimForDestroyedPartAtHealCover()
        {
            Player player = botOwner.GetPlayer;
            if (player == null ||
                player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) == true ||
                botOwner.Medecine?.SurgicalKit?.HaveWork != true)
            {
                return false;
            }

            EBodyPart? targetPart = botOwner.Medecine.SurgicalKit.Nullable_0;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            botOwner.Medecine.SurgicalKit.FindDamagedPart();
            targetPart = botOwner.Medecine.SurgicalKit.Nullable_0;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            return HasDestroyedPainManagedPart(player);
        }

        private static bool HasDestroyedPainManagedPart(Player player)
        {
            return IsDestroyedPainManagedPart(player, EBodyPart.Stomach) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftLeg) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightLeg);
        }

        private static bool IsDestroyedPainManagedPart(Player player, EBodyPart part)
        {
            return part != EBodyPart.Head &&
                   part != EBodyPart.Chest &&
                   player.ActiveHealthController?.IsBodyPartDestroyed(part) == true;
        }

        private bool TrySelectPainStimulator(GClass491 stims)
        {
            return TrySelectStimulator(stims, HasPainReliefEffect);
        }

        private bool TrySelectPositiveHealthRateStimulator(GClass491 stims)
        {
            return TrySelectStimulator(stims, HasPositiveHealthRateBuff);
        }

        private bool TrySelectStimulator(GClass491 stims, Func<StimulatorItemClass, bool> predicate)
        {
            Player player = botOwner.GetPlayer;
            if (player == null || player.InventoryController == null)
            {
                return false;
            }

            EquipmentSlot[] searchSlots = stims.Bool_2 ? BotMedecine.secureSlots : BotMedecine.anySlots;
            stimSearchBuffer.Clear();
            player.InventoryController.GetAcceptableItemsNonAlloc<MedsItemClass>(searchSlots, stimSearchBuffer, null, null);

            for (int i = 0; i < stimSearchBuffer.Count; i++)
            {
                if (stimSearchBuffer[i] is not StimulatorItemClass stimulator)
                {
                    continue;
                }

                if (!predicate(stimulator))
                {
                    continue;
                }

                stims.StimulatorItemClass = stimulator;
                stims.HaveSmt = true;
                return true;
            }

            stims.Refresh();
            return false;
        }

        private static bool HasPainReliefEffect(StimulatorItemClass stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            return effects?.DamageEffects?.ContainsKey(EDamageEffectType.Pain) == true;
        }

        private static bool HasPositiveHealthRateBuff(StimulatorItemClass stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            if (effects == null)
            {
                return false;
            }

            GClass3019.GClass3044.GClass3045[] buffs = effects.BuffSettings;
            for (int i = 0; i < buffs.Length; i++)
            {
                if (buffs[i].BuffType == EStimulatorBuffType.HealthRate &&
                    buffs[i].Value > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetImmediateShootDecision(string reason)
        {
            if (botOwner.WeaponManager?.Reload?.Reloading == true)
            {
                return null;
            }

            if (!ShouldShootImmediately())
            {
                return null;
            }

            FollowerContactEnemyRetention.RegisterCurrentGoal(botOwner, prioritized: true);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, reason);
        }

        private bool TryGetNoSprintHealContactFireDecision(
            EnemyInfo? goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (goalEnemy == null ||
                CanSprintForCombatMovement() ||
                botOwner.Memory.IsInCover ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit.Using)
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                if (IsPointBlankVisibleShootableThreat(goalEnemy))
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "healRetreatPointBlankFire");
                    return true;
                }

                bool coverTried = false;
                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    decision = CreateCommittedHealMoveDecision(goalEnemy);
                    return true;
                }

                if (TryGetNoCoverEmergencyStimDecision(out decision))
                {
                    return true;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "healRetreatVisibleSuppress");
                return true;
            }

            if (Time.time - goalEnemy.PersonalLastSeenTime > NoSprintHealSuppressRecentSeenSeconds)
            {
                return false;
            }

            Vector3 suppressTarget = FollowerImmediateFirePolicy.GetRecentContactSuppressTarget(goalEnemy);
            if (FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, suppressTarget))
            {
                return false;
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressFire, "healRetreatSuppress");
            return true;
        }

        private bool TryAssignHealCover(EnemyInfo goalEnemy, ref bool coverTried)
        {
            if (coverTried)
            {
                return false;
            }

            coverTried = true;

            if (IsCommittedHealCoverValid(goalEnemy))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(committedHealCover);
                return true;
            }

            if (TryFindHealCover(goalEnemy, out CustomNavigationPoint? healCover))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(healCover);
                committedHealCover = healCover;
                hasCommittedHealPoint = false;
                committedHealPoint = Vector3.zero;
                CommitHealMove(goalEnemy);
                return true;
            }

            float healCoverMaxNavDistance = CombatDistanceConfiguration.Instance.GetHealCoverMaxNavDistance();
            if (TryAssignRetreatAttackCover(goalEnemy, false, healCoverMaxNavDistance * healCoverMaxNavDistance))
            {
                if (!IsBlockedHealCover(botOwner.Memory.CurCustomCoverPoint))
                {
                    SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                    committedHealCover = botOwner.Memory.CurCustomCoverPoint;
                    hasCommittedHealPoint = false;
                    committedHealPoint = Vector3.zero;
                    CommitHealMove(goalEnemy);
                    return true;
                }

                SetCover(null);
            }

            if (TryFindHealHidePoint(goalEnemy, out Vector3 healPoint))
            {
                committedHealCover = null;
                committedHealPoint = healPoint;
                hasCommittedHealPoint = true;
                botOwner.GoToSomePointData.SetPoint(healPoint);
                CommitHealPointMove(goalEnemy, healPoint);
                return true;
            }

            return false;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null || botOwner.Memory.IsInCover)
            {
                ClearCommittedHealCover();
                return null;
            }

            if (committedHealCover != null)
            {
                if (!IsCommittedHealCoverValid(goalEnemy))
                {
                    ClearCommittedHealCover();
                    return null;
                }

                SetCover(committedHealCover);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
            }
            else if (hasCommittedHealPoint)
            {
                if (!IsCommittedHealPointValid(goalEnemy))
                {
                    ClearCommittedHealCover();
                    return null;
                }

                botOwner.GoToSomePointData.SetPoint(committedHealPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
            }
            else
            {
                return null;
            }

            return CreateCommittedHealMoveDecision(goalEnemy);
        }

        private void CommitHealMove(EnemyInfo? goalEnemy)
        {
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            bool canSprintToHealCover = CanSprintForCombatMovement();
            if (canSprintToHealCover && enemyProxyDistance > Enemy.ProxyDistance.VeryClose)
            {
                committedHealMoveAction = BotLogicDecision.runToCover;
                committedHealMoveReason = "runToHeal";
                return;
            }

            committedHealMoveAction = (BotLogicDecision)CustomBotDecisions.attackRetreat;
            committedHealMoveReason = canSprintToHealCover ? "moveToHeal.retreat" : "moveToHeal.noSprintRetreat";
        }

        private void CommitHealPointMove(EnemyInfo? goalEnemy, Vector3 healPoint)
        {
            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, healPoint);
            bool canRun =
                CanSprintForCombatMovement() &&
                IsFinite(navDistance) &&
                navDistance > 12f &&
                goalEnemy != null &&
                !Covers.IsPathExposedToEnemy(botOwner.Position, healPoint, GetEnemyAnchor(goalEnemy), botOwner.LookSensor.Mask, sampleCount: 5);

            committedHealMoveAction = BotLogicDecision.goToPoint;
            committedHealMoveReason = canRun ? "moveToHealPoint.runToPoint" : "moveToHealPoint.goToPoint";
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (committedHealMoveAction == default)
            {
                if (hasCommittedHealPoint)
                {
                    CommitHealPointMove(goalEnemy, committedHealPoint);
                }
                else
                {
                    CommitHealMove(goalEnemy);
                }
            }

            string reason = committedHealMoveReason ?? "runToHeal";
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedHealMoveAction, reason);
        }

        private bool CanHealAtCommittedHealCover(EnemyInfo? goalEnemy)
        {
            if (!IsBotAtCommittedHealCover())
            {
                return false;
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return false;
            }

            if (goalEnemy == null)
            {
                return true;
            }

            if (committedHealCover != null && !IsCommittedHealCoverValid(goalEnemy))
            {
                return false;
            }

            if (hasCommittedHealPoint && !IsCommittedHealPointValid(goalEnemy))
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            if (goalEnemy.IsVisible && enemyProxyDistance <= Enemy.ProxyDistance.Close)
            {
                return false;
            }

            return true;
        }

        private bool IsBotAtCommittedHealCover()
        {
            if (committedHealCover == null)
            {
                if (!hasCommittedHealPoint)
                {
                    return false;
                }

                if ((botOwner.Position - committedHealPoint).sqrMagnitude <= 2f * 2f)
                {
                    return true;
                }

                return botOwner.GoToSomePointData?.IsCome() == true;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedHealCover.Id)
            {
                return true;
            }

            if ((botOwner.Position - committedHealCover.Position).sqrMagnitude <= 2f * 2f)
            {
                return true;
            }

            return botOwner.GoToSomePointData?.IsCome() == true;
        }

        public bool CanSprintForCombatMovement()
        {
            if (!botOwner.CanSprintPlayer || botOwner.Mover?.NoSprint == true)
            {
                return false;
            }

            Player? player = botOwner.GetPlayer ?? botOwner.AIData?.Player;
            if (player?.HealthController == null)
            {
                return true;
            }

            return !player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) &&
                   !player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) &&
                   !player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) &&
                   !player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg);
        }

        /// <summary>
        /// Expands push distance as aggression rises while still respecting follower tactics.
        /// </summary>
        public Enemy.EnemyDistance GetMaxPushDistance(float aggression, FollowerCombatTactic? tacticOverride = null)
        {
            Enemy.EnemyDistance defaultDistance;

            if (aggression <= 0.2f)
            {
                defaultDistance = Enemy.EnemyDistance.VeryClose;
            }

            else if (aggression <= 0.4f)
            {
                defaultDistance = Enemy.EnemyDistance.Close;
            }
            else if (aggression <= 0.65f)
            {
                defaultDistance = Enemy.EnemyDistance.Distant;
            }
            else
            {
                defaultDistance = Enemy.EnemyDistance.Far;
            }

            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            return tactic switch
            {
                FollowerCombatTactic.Balanced => defaultDistance,
                FollowerCombatTactic.Protector => Enemy.EnemyDistance.Close,
                FollowerCombatTactic.Marksman => Enemy.EnemyDistance.VeryClose,
                _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        private bool TryFindHealCover(EnemyInfo goalEnemy, out CustomNavigationPoint? cover)
        {
            cover = null;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 awayFromEnemy = botOwner.Position - enemyAnchor;
            awayFromEnemy.y = 0f;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = GetBossPosition() - enemyAnchor;
                awayFromEnemy.y = 0f;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                return false;
            }

            float healCoverRetreatDistance = CombatDistanceConfiguration.Instance.GetHealCoverRetreatDistance();
            float healCoverSearchRadius = CombatDistanceConfiguration.Instance.GetHealCoverSearchRadius();
            float healCoverMaxNavDistance = CombatDistanceConfiguration.Instance.GetHealCoverMaxNavDistance();
            CoverSearchType healSearchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Ambush,
                CoverShootType.hide,
                CoverSearchIntent.RunToCover);

            Vector3 retreatAnchor = botOwner.Position + awayFromEnemy.normalized * healCoverRetreatDistance;
            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            cover = Covers.GetClosestCoverPoint(
                botOwner,
                retreatAnchor,
                healCoverSearchRadius,
                point =>
                {
                    if (!IsCoverUsable(point))
                    {
                        return false;
                    }

                    if (IsBlockedHealCover(point))
                    {
                        return false;
                    }

                    if (!point.CanIHideFromPos(0f, true, false, enemyAnchor))
                    {
                        return false;
                    }

                    float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, point.Position);
                    if (!IsFinite(navDistance) ||
                        navDistance < HealCoverMinNavDistance ||
                        navDistance > healCoverMaxNavDistance)
                    {
                        return false;
                    }

                    float candidateEnemyDistance = Vector3.Distance(point.Position, enemyAnchor);
                    return candidateEnemyDistance + HealCoverMinEnemyDistanceGain >= currentEnemyDistance;
                },
                healSearchType);

            return cover != null;
        }

        private bool TryFindHealHidePoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 awayFromEnemy = botOwner.Position - enemyAnchor;
            awayFromEnemy.y = 0f;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = GetBossPosition() - enemyAnchor;
                awayFromEnemy.y = 0f;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                return false;
            }

            awayFromEnemy.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, awayFromEnemy).normalized;
            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float bestScore = float.MaxValue;
            bool found = false;

            float[] distances = { 6f, 10f, 14f, 18f, 24f };
            float[] lateralOffsets = { 0f, 4f, -4f, 8f, -8f };
            for (int d = 0; d < distances.Length; d++)
            {
                for (int l = 0; l < lateralOffsets.Length; l++)
                {
                    Vector3 candidate = botOwner.Position + awayFromEnemy * distances[d] + lateral * lateralOffsets[l];
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    Vector3 navPoint = hit.position;
                    float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, navPoint);
                    if (!IsFinite(navDistance) ||
                        navDistance < HealHidePointMinDistance ||
                        navDistance > HealHidePointMaxNavDistance)
                    {
                        continue;
                    }

                    float candidateEnemyDistance = Vector3.Distance(navPoint, enemyAnchor);
                    if (candidateEnemyDistance + HealHidePointEnemyDistanceGain < currentEnemyDistance)
                    {
                        continue;
                    }

                    if (!IsPointHiddenFromEnemy(navPoint, enemyAnchor))
                    {
                        continue;
                    }

                    if (Covers.IsPathExposedToEnemy(botOwner.Position, navPoint, enemyAnchor, botOwner.LookSensor.Mask, sampleCount: 5) &&
                        candidateEnemyDistance < currentEnemyDistance + 4f)
                    {
                        continue;
                    }

                    float score = navDistance - Mathf.Max(0f, candidateEnemyDistance - currentEnemyDistance) * 0.5f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        point = navPoint;
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool IsPointHiddenFromEnemy(Vector3 point, Vector3 enemyAnchor)
        {
            Vector3 enemyEye = enemyAnchor + Vector3.up * 1.5f;
            Vector3 bodyPoint = point + Vector3.up * 0.9f;
            Vector3 headPoint = point + Vector3.up * 1.55f;
            LayerMask mask = botOwner.LookSensor.Mask;
            return Physics.Linecast(enemyEye, bodyPoint, mask) &&
                   Physics.Linecast(enemyEye, headPoint, mask);
        }

        private bool IsCommittedHealCoverValid(EnemyInfo? goalEnemy = null)
        {
            if (committedHealCover == null)
            {
                return false;
            }

            if (IsBlockedHealCover(committedHealCover) ||
                !committedHealCover.IsFreeById(botOwner.Id) ||
                committedHealCover.IsSpotted)
            {
                committedHealCover = null;
                return false;
            }

            if (goalEnemy != null)
            {
                Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
                if (IsFinite(enemyAnchor) && !committedHealCover.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    committedHealCover = null;
                    return false;
                }
            }

            return true;
        }

        private bool IsBlockedHealCover(CustomNavigationPoint? cover)
        {
            return cover != null &&
                   blockedHealCoverId == cover.Id &&
                   Time.time < blockedHealCoverUntil;
        }

        private void BlockHealCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            blockedHealCoverId = cover.Id;
            blockedHealCoverUntil = Time.time + HealCoverStallBlacklistSeconds;
        }

        private bool IsCommittedHealPointValid(EnemyInfo? goalEnemy = null)
        {
            if (!hasCommittedHealPoint || !IsFinite(committedHealPoint))
            {
                return false;
            }

            if (goalEnemy == null)
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            return !IsFinite(enemyAnchor) || IsPointHiddenFromEnemy(committedHealPoint, enemyAnchor);
        }

        private void ClearCommittedHealCover()
        {
            committedHealCover = null;
            committedHealPoint = Vector3.zero;
            hasCommittedHealPoint = false;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
        }

        /// <summary>
        /// Assign a retreat/attack cover point opposite the enemy relative to the boss anchor.
        /// Returns true when a valid cover was assigned to BotCurrentCoverInfo.
        /// </summary>
        public bool TryAssignRetreatAttackCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            float maxBossDistanceSqr = 100f,
            bool allowSpotted = false)
        {
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyPosition = IsFinite(goalEnemy.CurrPosition) ? goalEnemy.CurrPosition : goalEnemy.EnemyLastPositionReal;
            Vector3 awayFromEnemy = bossPosition - enemyPosition;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = botOwner.Position - enemyPosition;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = Vector3.back;
            }

            Vector3 retreatAnchor = bossPosition + awayFromEnemy.normalized * 6f;
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            BotsGroup.BotCurrentTactic tactic = requireShootLane
                ? BotsGroup.BotCurrentTactic.Attack
                : BotsGroup.BotCurrentTactic.Ambush;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                tactic,
                requireShootLane ? CoverShootType.shoot : CoverShootType.hide,
                CoverSearchIntent.RunToCover);

            CustomNavigationPoint? retreatCover = Covers.GetClosestCoverPoint(
                botOwner,
                retreatAnchor,
                18f,
                point =>
                {
                    if (!IsCoverUsable(point, allowSpotted))
                    {
                        return false;
                    }

                    if ((point.Position - botOwner.Position).sqrMagnitude > maxBossDistanceSqr)
                    {
                        return false;
                    }

                    if (shootPoint != null && !Utils.Utils.CanShootToTarget(shootPoint, point, botOwner.LookSensor.Mask, false))
                    {
                        return false;
                    }

                    return true;
                },
                searchType);

            if (retreatCover == null)
            {
                return false;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(retreatCover, true);
            return true;
        }

        /// <summary>
        /// Finds a safe boss-local cover to use when the follower needs to reanchor or protect the boss.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, float searchRadius, out CustomNavigationPoint? cover)
        {
            return TryFindBossCover(goalEnemy, GetBossPosition(), searchRadius, out cover);
        }

        /// <summary>
        /// Finds a safe boss-local cover around the supplied boss anchor.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, Vector3 bossPosition, float searchRadius, out CustomNavigationPoint? cover)
        {
            float searchRadiusSqr = searchRadius * searchRadius;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Protect,
                CoverShootType.hide,
                CoverSearchIntent.ForCover);
            CustomNavigationPoint? candidate = Covers.GetClosestCoverPoint(
                botOwner,
                bossPosition,
                searchRadius,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude > searchRadiusSqr)
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude < 2f * 2f)
                    {
                        return false;
                    }

                    return !IsFinite(enemyAnchor) || point.CanIHideFromPos(0f, true, false, enemyAnchor);
                },
                searchType);

            if (candidate == null)
            {
                cover = null;
                return false;
            }

            if ((candidate.Position - bossPosition).sqrMagnitude > searchRadiusSqr)
            {
                cover = null;
                return false;
            }

            if ((candidate.Position - bossPosition).sqrMagnitude < 2f * 2f)
            {
                cover = null;
                return false;
            }

            if (IsFinite(enemyAnchor) && !candidate.CanIHideFromPos(0f, true, false, enemyAnchor))
            {
                cover = null;
                return false;
            }

            cover = candidate;
            return true;
        }

        public bool TryGetGeneralStartCover(EnemyInfo goalEnemy, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (goalEnemy == null)
            {
                return false;
            }

            Vector3 enemyPosition = goalEnemy.CurrPosition;
            if (!IsFinite(enemyPosition))
            {
                enemyPosition = goalEnemy.EnemyLastPositionReal;
            }

            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out hasShootLane);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance)
        {
            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out _);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            return TryGetSupportCover(enemyPosition, 35f, out cover, out navDistance, out hasShootLane);
        }

        private bool TryGetSupportCover(
            Vector3 enemyPosition,
            float searchRadius,
            out CustomNavigationPoint? cover,
            out float navDistance,
            out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(enemyPosition + Vector3.up * 1.1f, 1f);
            LayerMask mask = botOwner.LookSensor.Mask;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.ForCover);

            cover = Covers.GetClosestCoverPoint(
                botOwner,
                botOwner.Position,
                searchRadius,
                point => point != null &&
                         !point.IsSpotted &&
                         point.IsFreeById(botOwner.Id) &&
                         Utils.Utils.CanShootToTarget(shootPoint, point, mask, false),
                searchType);

            if (cover == null)
            {
                return false;
            }

            navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (!IsFinite(navDistance))
            {
                navDistance = Vector3.Distance(botOwner.Position, cover.Position);
            }

            hasShootLane = true;
            return true;
        }

        /// <summary>
        /// Picks the best available enemy anchor for blind pushes and cover searches.
        /// </summary>
        public static Vector3 GetEnemyAnchor(EnemyInfo goalEnemy)
        {
            if (IsFinite(goalEnemy.CurrPosition) && goalEnemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        public static Vector3 GetEnemyCurrentPosition(EnemyInfo goalEnemy)
        {
            if (goalEnemy.Person != null &&
                IsFinite(goalEnemy.Person.Position) &&
                goalEnemy.Person.Position.sqrMagnitude > 0.01f)
            {
                return goalEnemy.Person.Position;
            }

            return GetEnemyAnchor(goalEnemy);
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? EnemyCoverSearch(
            string reason = "enemySearch",
            bool weakEnemy = false,
            bool avoidBossFireLane = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            Vector3 searchPoint = enemyAnchor;

            // Prefer an approach cover with a clear shot from a nearby tactical point.
            CustomNavigationPoint? approachCover = weakEnemy
                ? GetWeakEnemyPushCover(avoidBossFireLane)
                : GetApproachableCover(avoidBossFireLane: avoidBossFireLane);

            if (approachCover != null)
            {
                searchPoint = approachCover.Position;
                botOwner.GoToSomePointData.SetPoint(searchPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            }

            return null;

        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySimpleSearch(string reason = "enemySearch")
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            Vector3 searchPoint = enemyAnchor;

            if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit hit, 8f, -1))
            {
                ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                Vector3 firePos = hit.position + Vector3.up * 1.2f;
                if (Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    searchPoint = hit.position;
                }
            }

            botOwner.SearchData.SearchPoint = new BotSearchPoint(searchPoint, EBotSearchPoint.playerPosition);
            botOwner.SearchData.LastSearchPoint = null;
            botOwner.SearchData.NextPosibleCheckTime = Time.time + 10f;
            botOwner.SearchData.NextPosibleGoRefresh = 0f;
            botOwner.SearchData.Going = false;
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.search, reason);
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySearch(
            string reason = "enemySearch",
            bool weakEnemy = false,
            bool pushOrdered = false,
            bool cautious = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Enemy.EnemyDistance distance = Enemy.Distance(goalEnemy);
            if (distance <= Enemy.EnemyDistance.Close)
            {
                if (EnemyCoverSearch(reason, weakEnemy, avoidBossFireLane: !pushOrdered) is AICoreActionResultStruct<BotLogicDecision, GClass26> tacticalSearchResult)
                {
                    return tacticalSearchResult;
                }

                return EnemySimpleSearch(reason);
            }

            bool canSprintToSearch = !cautious && CanSprintForCombatMovement();
            canSprintToSearch &= CanRunToEnemyNow();
            if (canSprintToSearch &&
                weakEnemy &&
                !pushOrdered &&
                ShouldBlockWeakEnemyRushForBossDistance(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    FollowerCombatRegroupObjective.ActivateRegroupReason);
            }

            if (canSprintToSearch)
            {
                reason += ".rush";
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToEnemy, reason);
            }

            if (pushOrdered && distance <= Enemy.EnemyDistance.Distant)
            {
                reason += ".walk";
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToEnemy, reason);
            }

            if (distance <= Enemy.EnemyDistance.Mid)
            {
                if (EnemyCoverSearch($"{reason}.walk", weakEnemy, avoidBossFireLane: !pushOrdered) is AICoreActionResultStruct<BotLogicDecision, GClass26> walkCoverResult)
                {
                    return walkCoverResult;
                }

                return EnemySimpleSearch($"{reason}.walk");
            }

            if (pushOrdered)
            {
                return EnemySimpleSearch($"{reason}.orderedSearch");
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
        }

        public bool ShouldBlockWeakEnemyRushForBossDistance(EnemyInfo goalEnemy)
        {
            if (!IsWeakEnemyAutoPushRoleAllowed(goalEnemy))
            {
                return true;
            }

            if (goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return false;
            }

            if (!IsUsableDistance(goalEnemy.Distance))
            {
                return true;
            }

            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return true;
            }

            float bossDistance = GetBossNavDistance(bossPosition);
            float directBossDistance = Vector3.Distance(botOwner.Position, bossPosition);
            if (!IsUsableDistance(bossDistance))
            {
                bossDistance = directBossDistance;
            }
            else
            {
                bossDistance = Mathf.Max(bossDistance, directBossDistance);
            }

            float triggerDistance = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            if (bossDistance > triggerDistance + WeakEnemyPushBossDistanceBuffer)
            {
                return true;
            }

            float distanceToEnemyAnchor = Vector3.Distance(botOwner.Position, enemyAnchor);
            if (!IsUsableDistance(distanceToEnemyAnchor) ||
                distanceToEnemyAnchor > GetWeakEnemyPushMaxDistance())
            {
                return true;
            }

            Vector3 predictedBossOffset = enemyAnchor - bossPosition;
            return predictedBossOffset.sqrMagnitude >
                   (triggerDistance + WeakEnemyPushBossDistanceBuffer) *
                   (triggerDistance + WeakEnemyPushBossDistanceBuffer);
        }

        private static bool IsWeakEnemyAutoPushRoleAllowed(EnemyInfo goalEnemy)
        {
            WildSpawnType role = goalEnemy?.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            return FollowerDeathEscapeResolver.GetRouteThreatRoleMultiplier(role) <= WeakEnemyPushMaxRoleThreatMultiplier;
        }

        private static bool IsUsableDistance(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > 0.1f &&
                   value < float.MaxValue * 0.5f;
        }



        public bool TryCreateTacticalCoverDecision(
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferPointToShoot = true,
            bool preferInbetween = false)
        {
            decision = default;
            CustomNavigationPoint? cover = preferPointToShoot && IsCoverUsable(PointToShoot)
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true) ?? GetApproachableCover()
                : GetApproachableCover();

            if (!IsCoverUsable(cover))
            {
                return false;
            }

            AssignCover(cover);
            botOwner.GoToSomePointData.SetPoint(cover!.Position);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            return true;
        }

        public bool TryCreateCoverMoveDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferInbetween = false)
        {
            decision = default;
            CustomNavigationPoint? cover = IsCoverUsable(PointToShoot)
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true) ?? GetApproachableCover()
                : GetApproachableCover();

            if (!IsCoverUsable(cover))
            {
                return false;
            }

            AssignCover(cover);
            BotLogicDecision moveAction = SelectCommittedCoverMoveAction(goalEnemy);
            if (moveAction == BotLogicDecision.runToCover)
            {
                SetRunToCoverTactic(cover, reason);
            }
            else if (moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
            }
            else if (moveAction == BotLogicDecision.attackMoving ||
                     moveAction == BotLogicDecision.attackMovingWithSuppress)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveAction,
                CreateMovementReason(reason, moveAction));
            return true;
        }

        public bool TryCreateBossCoverAttackMovingDecision(
            EnemyInfo goalEnemy,
            float searchRadius,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            ResetCommittedCover();
            ClearCommittedMovement();

            Vector3 bossPosition = GetBossPosition();
            if (!TryFindBossCover(goalEnemy, bossPosition, searchRadius, out CustomNavigationPoint? bossCover) ||
                !IsCoverUsable(bossCover, true))
            {
                if (!TryGetBossApproachFallbackPoint(bossPosition, out Vector3 fallbackPoint))
                {
                    return false;
                }

                botOwner.GoToSomePointData.SetPoint(fallbackPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.goToPointTactical,
                    $"{reason}.fallbackPoint");
                return true;
            }

            BotLogicDecision action = BotLogicDecision.attackMoving;
            string movementReason = CreateMovementReason(reason, action);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            CommitCover(bossCover, action, movementReason);
            AssignCover(bossCover);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(action, movementReason);
            return true;
        }

        private bool TryGetBossApproachFallbackPoint(Vector3 bossPosition, out Vector3 fallbackPoint)
        {
            const float BossApproachStopDistance = 1.5f;
            const float BossApproachMaxDistance = 2f;

            fallbackPoint = default;
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            if (!NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, BossApproachMaxDistance, NavMesh.AllAreas))
            {
                return false;
            }

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(botOwner.Position, bossHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete ||
                path.corners == null ||
                path.corners.Length == 0)
            {
                return false;
            }

            Vector3 target = GetPointBackFromPathEnd(path.corners, BossApproachStopDistance);
            if (!NavMesh.SamplePosition(target, out NavMeshHit targetHit, 1f, NavMesh.AllAreas))
            {
                return false;
            }

            if ((targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
            {
                target = GetPointBackFromPathEnd(path.corners, 1f);
                if (!NavMesh.SamplePosition(target, out targetHit, 1f, NavMesh.AllAreas) ||
                    (targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
                {
                    return false;
                }
            }

            fallbackPoint = targetHit.position;
            return IsFinite(fallbackPoint);
        }

        private static Vector3 GetPointBackFromPathEnd(Vector3[] corners, float distanceFromEnd)
        {
            Vector3 target = corners[corners.Length - 1];
            float remaining = Mathf.Max(0f, distanceFromEnd);

            for (int i = corners.Length - 2; i >= 0 && remaining > 0f; i--)
            {
                Vector3 previous = corners[i];
                Vector3 segment = previous - target;
                float segmentLength = segment.magnitude;
                if (segmentLength <= 0.01f)
                {
                    target = previous;
                    continue;
                }

                if (segmentLength >= remaining)
                {
                    return target + segment / segmentLength * remaining;
                }

                remaining -= segmentLength;
                target = previous;
            }

            return target;
        }

        public bool TryCreateBossCommandTacticalPointDecision(
            Vector3 target,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!IsFinite(target))
            {
                return false;
            }

            ResetCommittedCover();
            ClearCommittedMovement();
            botOwner.GoToSomePointData.SetPoint(target);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.goToPointTactical,
                reason);
            return true;
        }

        public static string CreateMovementReason(string baseReason, BotLogicDecision moveAction)
        {
            return moveAction switch
            {
                BotLogicDecision.runToCover => $"{baseReason}.runToCover",
                BotLogicDecision.runToEnemy => $"{baseReason}.runToEnemy",
                BotLogicDecision.goToEnemy => $"{baseReason}.goToEnemy",
                BotLogicDecision.goToPoint => $"{baseReason}.goToPoint",
                BotLogicDecision.goToPointTactical => $"{baseReason}.goToPointTactical",
                BotLogicDecision.attackMoving => $"{baseReason}.attackMoving",
                BotLogicDecision.attackMovingWithSuppress => $"{baseReason}.attackMovingWithSuppress",
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => $"{baseReason}.attackRetreat",
                BotLogicDecision.suppressFire => $"{baseReason}.suppressFire",
                _ => baseReason,
            };
        }

        private void SetCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(cover, true);
        }

        public bool TryGetAllyEngagementEnemy(out string enemyProfileId, out Vector3 enemyPosition)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            if (boss.IsPlayerEngaging(out string playerEnemyProfileId, out Vector3 playerEnemyPosition) &&
                !string.IsNullOrEmpty(playerEnemyProfileId) &&
                IsFinite(playerEnemyPosition))
            {
                enemyProfileId = playerEnemyProfileId;
                enemyPosition = playerEnemyPosition;
                return true;
            }

            foreach (BotOwner followerBot in boss.Followers)
            {
                if (followerBot == null || followerBot == botOwner || followerBot.IsDead || followerBot.Memory?.GoalEnemy == null)
                {
                    continue;
                }

                EnemyInfo followerEnemy = followerBot.Memory.GoalEnemy;
                if (!followerEnemy.IsVisible || !followerEnemy.CanShoot || string.IsNullOrEmpty(followerEnemy.ProfileId))
                {
                    continue;
                }

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(followerBot);
                if (followerData == null || !followerData.IsBotActivelyEngaging(followerEnemy.ProfileId))
                {
                    continue;
                }

                enemyProfileId = followerEnemy.ProfileId;
                enemyPosition = followerEnemy.CurrPosition;
                return IsFinite(enemyPosition);
            }

            return false;
        }

        private EnemyInfo? GetTrackedEnemyByProfileId(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner.EnemiesController?.EnemyInfos == null)
            {
                return null;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId == enemyProfileId)
                {
                    return item.Value;
                }
            }

            return null;
        }

        private bool TryPromoteDogFightState(EnemyInfo? goalEnemy, out BotDogFightStatus dogFightState)
        {
            dogFightState = botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            if (goalEnemy == null || !goalEnemy.IsVisible)
            {
                return false;
            }

            BotDogFight? dogFight = botOwner.DogFight;
            if (dogFight == null)
            {
                return false;
            }

            if (goalEnemy.Distance >= 18f)
            {
                return false;
            }

            if (CanUseDogFightNow(goalEnemy) && dogFight.method_1(out _))
            {
                dogFight.DogFightState = BotDogFightStatus.dogFight;
                dogFightState = BotDogFightStatus.dogFight;
                return true;
            }

            dogFight.DogFightState = BotDogFightStatus.shootFromPlace;
            dogFightState = BotDogFightStatus.shootFromPlace;
            return true;
        }

        private Vector3 GetEnemyAnchorOrFallback(EnemyInfo? enemyInfo, Vector3 fallback)
        {
            if (enemyInfo != null)
            {
                Vector3 anchor = GetEnemyAnchor(enemyInfo);
                if (IsFinite(anchor))
                {
                    return anchor;
                }
            }

            return fallback;
        }

        private float ScoreSupportEnemy(EnemyInfo? enemyInfo, Vector3 fallbackPosition, bool preferBackline)
        {
            if (!HasActiveCombatEnemy(enemyInfo))
            {
                return float.MinValue;
            }

            float score = 0f;
            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(enemyInfo, fallbackPosition);
            float distance = IsFinite(enemyAnchor)
                ? Vector3.Distance(botOwner.Position, enemyAnchor)
                : float.MaxValue;

            if (enemyInfo!.IsVisible)
            {
                score += 5f;
            }

            if (enemyInfo.CanShoot)
            {
                score += 4f;
            }

            if (botOwner.Memory?.GoalEnemy != null &&
                string.Equals(botOwner.Memory.GoalEnemy.ProfileId, enemyInfo.ProfileId, StringComparison.Ordinal))
            {
                score += 2.5f;
            }

            float sinceLastSeen = Time.time - enemyInfo.PersonalLastSeenTime;
            if (sinceLastSeen <= 2.5f)
            {
                score += 2f;
            }

            if (distance < float.MaxValue)
            {
                score -= Mathf.Clamp(distance / 25f, 0f, 4f);
            }

            if (preferBackline && distance < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                score -= 3f;
            }

            return score;
        }

        private bool TryGetSupportCoverForEnemy(
            EnemyInfo supportEnemy,
            out CustomNavigationPoint? supportCover,
            out float supportCoverNavDistance,
            float maxSearchRadius = 35f)
        {
            supportCover = null;
            supportCoverNavDistance = float.MaxValue;
            Vector3 enemyPosition = GetEnemyAnchorOrFallback(supportEnemy, Vector3.zero);
            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            if (!TryGetSupportCover(enemyPosition, maxSearchRadius, out supportCover, out supportCoverNavDistance, out _))
            {
                return false;
            }

            bool strict = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            if (!IsCoverSafeFromAlternateThreats(supportCover, supportEnemy.ProfileId, strict))
            {
                supportCover = null;
                supportCoverNavDistance = float.MaxValue;
                return false;
            }

            return true;
        }

        private bool IsCoverSafeFromAlternateThreats(CustomNavigationPoint? cover, string? primaryEnemyProfileId, bool strict)
        {
            if (!IsCoverUsable(cover))
            {
                return false;
            }

            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return true;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!HasActiveCombatEnemy(enemyInfo) ||
                    string.Equals(enemyInfo.ProfileId, primaryEnemyProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 enemyAnchor = GetEnemyAnchor(enemyInfo);
                if (!IsFinite(enemyAnchor))
                {
                    continue;
                }

                bool dangerousThreat =
                    enemyInfo.CanShoot ||
                    enemyInfo.IsVisible ||
                    Time.time - enemyInfo.PersonalLastSeenTime <= 3f;
                if (!dangerousThreat)
                {
                    continue;
                }

                if (!cover!.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    if (strict)
                    {
                        return false;
                    }

                    float primaryDistance = botOwner.Memory?.GoalEnemy != null &&
                                            IsFinite(GetEnemyAnchor(botOwner.Memory.GoalEnemy))
                        ? Vector3.Distance(cover.Position, GetEnemyAnchor(botOwner.Memory.GoalEnemy))
                        : float.MaxValue;
                    float alternateDistance = Vector3.Distance(cover.Position, enemyAnchor);
                    if (alternateDistance <= primaryDistance + 5f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSupportPositionBehindBossLine(Vector3 candidate, Vector3 bossPosition, Vector3 enemyAnchor)
        {
            if (!IsFinite(candidate) || !IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            if (bossToEnemy.sqrMagnitude < 0.01f)
            {
                return true;
            }

            bossToEnemy.Normalize();
            Vector3 bossToCandidate = candidate - bossPosition;
            bossToCandidate.y = 0f;
            float forward = Vector3.Dot(bossToCandidate, bossToEnemy);
            return forward <= 1.5f;
        }

        /// <summary>
        /// Treats very recent visible contacts as an immediate-fire window so followers do not hesitate
        /// before taking their first shot.
        /// </summary>
        public bool ShouldShootImmediately()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool recentVisibleShoot =
                goalEnemy != null &&
                goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Time.time - goalEnemy.PersonalSeenTime < 1.5f;
            bool shootNow = ((goalEnemy != null && goalEnemy.Distance < botOwner.Settings.FileSettings.Shoot.SHOOT_IMMEDIATELY_DIST) ||
                             botOwner.BotsGroup.AnyBodyShootImmediately) &&
                            goalEnemy != null &&
                            goalEnemy.IsVisible &&
                            goalEnemy.CanShoot &&
                            Time.time - goalEnemy.AddTime < 5f;

            bool launcherActive = botOwner.WeaponManager.UnderbarrelLauncherController.IsActive;
            botOwner.BotsGroup.AnyBodyShootImmediately = shootNow || recentVisibleShoot || launcherActive;
            return botOwner.BotsGroup.AnyBodyShootImmediately;
        }

        /// <summary>
        /// A committed cover run should only break for immediate fire if the visible contact is stable
        /// enough to be real, not just a one-frame LOS flicker while crossing geometry.
        /// </summary>
        public bool ShouldBreakRunToCoverForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (WasHitRecently(botOwner, 0.5f) && HasActiveCombatEnemy(goalEnemy))
            {
                return true;
            }

            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatAdvanceBreakDistance, CloseThreatRecentSeenSeconds);
            }

            // While a committed cover run is still inside its initial lock window, treat the move as
            // sticky unless the bot was actually hit. This avoids SAIN-unlike flicker breaks where a
            // transient LOS blip peels the follower off the chosen cover before arrival.
            if (HasCommittedCover() &&
                !IsBotInCommittedCover() &&
                !IsCommittedCoverLockExpired)
            {
                return false;
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        /// <summary>
        /// Returns true if the currently equipped weapon supports full-auto.
        /// </summary>
        public bool IsCurrentWeaponAutomatic()
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item;
            return IsAutomaticWeapon(activeWeapon);
        }

        /// <summary>
        /// Suppression is only useful with enough fire volume. Single-shot/small-mag weapons should
        /// keep using normal shoot/reposition decisions instead of wasting precise ammo into cover.
        /// </summary>
        public bool CanCurrentWeaponSuppress()
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item;
            return IsSuppressCapableWeapon(activeWeapon);
        }

        public static bool IsSuppressCapableWeapon(Weapon? weapon)
        {
            if (weapon == null)
            {
                return false;
            }

            if (IsAutomaticWeapon(weapon))
            {
                return true;
            }

            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            return magazine?.MaxCount >= 25;
        }

        /// <summary>
        /// Returns true if the bot already has a loaded automatic weapon equipped or can swap to a
        /// loaded automatic second primary for close combat.
        /// </summary>
        public bool HasAutomaticCloseCombatWeaponAvailable()
        {
            if (botOwner == null)
            {
                return false;
            }

            Weapon? activeWeapon = botOwner.WeaponManager?.ShootController?.Item;
            if (IsAutomaticWeapon(activeWeapon) &&
                activeWeapon?.GetCurrentMagazine()?.Cartridges?.Count > 0)
            {
                return true;
            }

            var selector = botOwner.WeaponManager?.Selector;
            if (selector == null ||
                !selector.CanChangeToSecondWeapons ||
                selector.SecondPrimaryWeaponItem == null)
            {
                return false;
            }

            Weapon? secondaryWeapon = selector.SecondPrimaryWeaponItem as Weapon;
            if (!IsAutomaticWeapon(secondaryWeapon))
            {
                return false;
            }

            return secondaryWeapon?.GetCurrentMagazine()?.Cartridges?.Count > 0;
        }

        /// <summary>
        /// Marksman close-quarter helper: if the current weapon is not full-auto and the bot has
        /// a loaded full-auto secondary weapon, switch to it.
        /// </summary>
        public bool TrySwitchToAutomaticSecondaryForCloseCombat()
        {
            if (botOwner == null)
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (IsCurrentWeaponAutomatic())
            {
                return true;
            }

            var selector = botOwner?.WeaponManager?.Selector;
            if (selector == null ||
                !selector.CanChangeToSecondWeapons ||
                selector.SecondPrimaryWeaponItem == null)
            {
                return false;
            }

            Weapon? secondaryWeapon = selector.SecondPrimaryWeaponItem as Weapon;
            if (!IsAutomaticWeapon(secondaryWeapon))
            {
                return false;
            }

            if (secondaryWeapon?.GetCurrentMagazine()?.Cartridges?.Count <= 0)
            {
                return false;
            }

            Player? player = botOwner?.GetPlayer;
            var equipment = player?.InventoryController?.Inventory?.Equipment;
            Weapon? primaryWeapon = equipment != null
                ? equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon
                : null;
            if (primaryWeapon == null || IsAutomaticWeapon(primaryWeapon))
            {
                return false;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                selector.ChangeToSecond();
                return true;
            }

            return false;
        }

        public void TrySwitchBackToPrimaryAtRange(EnemyInfo goalEnemy, Enemy.EnemyDistance minDistance)
        {
            if (Enemy.Distance(goalEnemy) >= minDistance &&
                botOwner.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                botOwner.WeaponManager.Selector.TryChangeToMain();
            }
        }

        public bool IsHoldingPrimaryAtRange(EnemyInfo goalEnemy, Enemy.EnemyDistance minDistance)
        {
            return botOwner.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon &&
                   Enemy.Distance(goalEnemy) > minDistance;
        }

        public bool IsCommittedCoverRetreatingFromEnemy(EnemyInfo goalEnemy)
        {
            return IsRetreatCover(goalEnemy, committedCoverPoint);
        }

        private static bool IsAutomaticWeapon(Weapon? weapon)
        {
            return weapon != null &&
                   weapon.WeapFireType != null &&
                   System.Array.IndexOf(weapon.WeapFireType, Weapon.EFireMode.fullauto) >= 0;
        }

        /// <summary>
        /// Push movement should only end for a firing transition when the shot is stable enough to
        /// capitalize on immediately, not on a brief visible/shootable flicker while advancing.
        /// </summary>
        public bool ShouldBreakAdvanceForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatAdvanceBreakDistance, CloseThreatRecentSeenSeconds);
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        public bool IsEnemyActivelyThreateningMe(
            EnemyInfo? goalEnemy,
            float maxDistance,
            float recentSeenWindow)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > maxDistance ||
                !botOwner.IsEnemyLookingAtMe(goalEnemy))
            {
                return false;
            }

            return goalEnemy.IsVisible ||
                   Time.time - goalEnemy.PersonalSeenTime <= recentSeenWindow ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= recentSeenWindow;
        }

        internal static bool IsPointBlankContactWithoutHardSeparation(BotOwner? botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner == null ||
                !HasActiveCombatEnemy(botOwner, goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > PointBlankContactDogFightDistance)
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyCurrentPosition(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            if ((enemyAnchor - botOwner.Position).sqrMagnitude >
                PointBlankContactMaxAnchorDistance * PointBlankContactMaxAnchorDistance)
            {
                return false;
            }

            Vector3 weaponOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.25f;
            Vector3 bodyOrigin = botOwner.Position + Vector3.up * 1.25f;

            Vector3 bodyTarget = GetPointBlankBodyTarget(goalEnemy, enemyAnchor);
            Vector3 headTarget = enemyAnchor + Vector3.up * 1.55f;
            Vector3 chestTarget = enemyAnchor + Vector3.up * 1.05f;

            return HasNoHardObstruction(weaponOrigin, bodyTarget) ||
                   HasNoHardObstruction(weaponOrigin, chestTarget) ||
                   HasNoHardObstruction(bodyOrigin, bodyTarget) ||
                   HasNoHardObstruction(bodyOrigin, headTarget);
        }

        private static Vector3 GetPointBlankBodyTarget(EnemyInfo goalEnemy, Vector3 fallbackAnchor)
        {
            try
            {
                Vector3 bodyPart = goalEnemy.GetBodyPartPosition();
                if (IsFinite(bodyPart) && bodyPart.sqrMagnitude > 0.01f)
                {
                    return bodyPart;
                }
            }
            catch
            {
            }

            return fallbackAnchor + Vector3.up * 1.1f;
        }

        private static bool HasNoHardObstruction(Vector3 origin, Vector3 target)
        {
            if (!IsFinite(origin) || !IsFinite(target))
            {
                return false;
            }

            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.05f)
            {
                return true;
            }

            if (!Physics.Raycast(origin, direction / distance, out RaycastHit hit, distance, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return true;
            }

            return hit.collider != null && IsSoftFoliageCollider(hit.collider);
        }

        /// <summary>
        /// Verifies that the follower can actually fire from the current cover, with a direct line-of-sight
        /// fallback when EFT's cover cast says no but the shot is still physically clear.
        /// </summary>
        public bool CanShootFromCurrentCover(out string cause)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                cause = "noActiveEnemy";
                return false;
            }

            if (!goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                cause = "enemyNotShootable";
                return false;
            }

            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            if (botOwner.Memory.CurCustomCoverPoint == null)
            {
                cause = "noCurrentCoverPoint";
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            if (!botOwner.Memory.CurCustomCoverPoint.CanShootToTargetCast(
                    botOwner,
                    botOwner.Settings.FileSettings.Cover.DELTA_SEEN_FROM_COVE_LAST_POS))
            {
                ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
                Vector3 firePos = botOwner.WeaponRoot.position;
                if (shootPoint == null || !Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    cause = "CanShootToTargetCast";
                    return false;
                }
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                cause = "EndSho";
                return false;
            }

            cause = "allFine";
            return true;
        }

        /// <summary>
        /// Detects the crouched-cover failure case where the enemy is visible but EFT does not mark
        /// the current pose as shootable even though a standing lane from the same cover is clear.
        /// </summary>
        public bool CanShootFromCurrentCoverIfStanding(out string cause)
        {
            return CanShootFromCurrentCoverIfStanding(botOwner, out cause);
        }

        public bool TryRaiseForStandingCoverShot(out string cause)
        {
            if (!HasCommittedShootingCoverIntent(botOwner, committedCoverPoint))
            {
                cause = "notShootingCoverIntent";
                return false;
            }

            return TryRaiseForStandingCoverShot(botOwner, out cause);
        }

        public bool CanShootFromCurrentCoverOrStandingIntent(out string cause)
        {
            if (CanShootFromCurrentCover(out cause))
            {
                return true;
            }

            return TryRaiseForStandingCoverShot(out cause);
        }

        public static bool TryRaiseForStandingCoverShot(
            BotOwner botOwner,
            out string cause,
            bool requireShootingCoverIntent = true)
        {
            if (requireShootingCoverIntent &&
                !HasCommittedShootingCoverIntent(botOwner, botOwner?.Memory?.CurCustomCoverPoint))
            {
                cause = "notShootingCoverIntent";
                return false;
            }

            return TryRaiseForStandingCoverShotUnchecked(botOwner, out cause);
        }

        private static bool TryRaiseForStandingCoverShotUnchecked(BotOwner botOwner, out string cause)
        {
            if (!CanShootFromCurrentCoverIfStanding(botOwner, out cause))
            {
                return false;
            }

            botOwner.SetPose(1f);

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(false);
            if (shootPoint != null)
            {
                botOwner.Steering.LookToPoint(shootPoint.Point);
            }
            else if (botOwner.Memory?.GoalEnemy != null)
            {
                botOwner.Steering.LookToPoint(botOwner.Memory.GoalEnemy.GetBodyPartPosition());
            }

            return true;
        }

        private static bool HasCommittedShootingCoverIntent(BotOwner? botOwner, CustomNavigationPoint? currentCover)
        {
            if (botOwner == null || currentCover == null)
            {
                return false;
            }

            return coverCommitIntents.TryGetValue(botOwner.Id, out CoverCommitIntent intent) &&
                   intent.IsShootingCover &&
                   intent.CoverId == currentCover.Id;
        }

        private static bool IsCommittedShootingCoverReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            return reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.shootFromCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.protectBossShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.startPosition", StringComparison.Ordinal) ||
                   reason.StartsWith("shootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("retreatShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("coverVisibleFire", StringComparison.Ordinal) ||
                   reason.StartsWith("committedFire", StringComparison.Ordinal);
        }

        public static bool CanShootFromCurrentCoverIfStanding(BotOwner botOwner, out string cause)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(botOwner, goalEnemy))
            {
                cause = "noActiveEnemy";
                return false;
            }

            if (!goalEnemy.IsVisible)
            {
                cause = "enemyNotVisible";
                return false;
            }

            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            CustomNavigationPoint? cover = botOwner.Memory.CurCustomCoverPoint;
            if (cover == null)
            {
                cause = "noCurrentCoverPoint";
                return false;
            }

            if (cover.CoverLevel == CoverLevel.Lay)
            {
                cause = "layCover";
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(false);
            shootPoint ??= new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);

            Vector3 standingWeaponPosition = botOwner.Position + Vector3.up * StandingCoverShotProbeHeight;
            if (Utils.Utils.CanShootToTarget(shootPoint, standingWeaponPosition, botOwner.LookSensor.Mask, false))
            {
                cause = "standingPoseLane";
                return true;
            }

            Vector3 standingCoverFirePosition = cover.FirePosition + Vector3.up * StandingCoverShotProbeHeight;
            if (Utils.Utils.CanShootToTarget(shootPoint, standingCoverFirePosition, botOwner.LookSensor.Mask, false))
            {
                cause = "coverFirePositionStandingLane";
                return true;
            }

            cause = "standingLaneBlocked";
            return false;
        }

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

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Check if the current enemy is low threat based on equipment, and number of nearby enemies.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, bool ignoreEquip = false, float maximumEnemies = 2)
        {
            if (!ignoreEquip && dangerTimer > Time.time) return dangerResult;
            else if (ignoreEquip && dangerIgnoreEquipTimer > Time.time) return dangerIgnoreEquipResult;

            if (!ignoreEquip)
            {
                dangerTimer = Time.time + 1f;
                dangerResult = botOwner.Memory.AttackImmediately && Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) <= maximumEnemies;

                return dangerResult;
            }
            else
            {
                dangerIgnoreEquipTimer = Time.time + 1f;
                dangerIgnoreEquipResult = Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) < 3;

                return dangerIgnoreEquipResult;
            }
        }

        /// <summary>
        /// Close search is only safe when the current contact is actually isolated. This adds a
        /// non-cached recent-memory cluster check on top of the physical enemy count so target
        /// flicker inside a squad does not get treated as a weak single enemy.
        /// </summary>
        public bool IsSafeCloseSearchTarget(EnemyInfo goalEnemy, float aggression01, float clusterRadius = 22f)
        {
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!IsEnemyLowThreat(goalEnemy, aggression01))
            {
                return false;
            }

            Vector3 anchor = GetEnemyAnchor(goalEnemy);
            int allowedClusterEnemies = GetAllowedLowThreatEnemyCount(aggression01);
            if (Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, anchor, clusterRadius) > allowedClusterEnemies)
            {
                return false;
            }

            return CountRecentKnownEnemiesNear(anchor, clusterRadius, 4f) <= allowedClusterEnemies;
        }

        private static int GetAllowedLowThreatEnemyCount(float aggression01)
        {
            if (aggression01 >= 0.7f)
            {
                return 3;
            }

            if (aggression01 >= 0.4f)
            {
                return 2;
            }

            return 1;
        }

        private int CountRecentKnownEnemiesNear(Vector3 position, float radius, float recentSeconds)
        {
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return 0;
            }

            float radiusSqr = radius * radius;
            int count = 0;
            HashSet<string> counted = new HashSet<string>();
            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                IPlayer? player = item.Key;
                EnemyInfo info = item.Value;
                if (player == null ||
                    info == null ||
                    player.HealthController?.IsAlive != true ||
                    string.IsNullOrEmpty(player.ProfileId) ||
                    counted.Contains(player.ProfileId))
                {
                    continue;
                }

                bool recentlyKnown =
                    info.IsVisible ||
                    info.CanShoot ||
                    info.HaveSeen ||
                    Time.time - info.PersonalLastSeenTime <= recentSeconds;
                if (!recentlyKnown)
                {
                    continue;
                }

                Vector3 enemyPosition = player.Position;
                enemyPosition.y = position.y;
                Vector3 flatPosition = position;
                flatPosition.y = enemyPosition.y;
                if ((enemyPosition - flatPosition).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                counted.Add(player.ProfileId);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Check if there is a reliable known position of the goal enemy from either personal or
        /// retained shared combat memory.
        /// </summary>
        public bool HasReliablePersonalEnemyLocation(EnemyInfo goalEnemy)
        {
            return Enemy.HasReliableKnownPosition(botOwner, goalEnemy);
        }

        /// <summary>
        /// Check if follower is critically wounded based on recent damage and hit frequency.
        /// Blocks aggressive pushes when critically injured.
        /// </summary>
        public bool IsFollowerCriticallyWounded()
        {
            bool multipleRecentHits = WasHitRecently(botOwner, 1.5f) && Time.time - botOwner.Memory.LastTimeHit - 0.5f > 0f;
            bool heavyFire = botOwner.Memory.IsUnderFire && WasHitRecently(botOwner, 3f);
            return multipleRecentHits || heavyFire;
        }

        public bool HasUrgentHealWork()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            return botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                   botOwner.Medecine.SurgicalKit?.Using == true ||
                   healthStatus == ETagStatus.BadlyInjured ||
                   healthStatus == ETagStatus.Dying;
        }

        public bool HasActiveOrPendingHealWork()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            RefreshCombatHealWorkIfNeeded();

            return botOwner.Medecine.FirstAid?.Have2Do == true ||
                   botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                   botOwner.Medecine.FirstAid?.Using == true ||
                   botOwner.Medecine.SurgicalKit?.Using == true ||
                   botOwner.Medecine.Stimulators?.Using == true ||
                   HasUrgentHealWork();
        }

        /// <summary>
        /// Check if follower is injured and should avoid aggressive advances.
        /// Prefers cover-holding or cautious movement when injured and under recent fire.
        /// </summary>
        public bool IsFollowerInjured()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool underThreat = botOwner.Memory.IsUnderFire || (goalEnemy != null && goalEnemy.IsVisible);
            return WasHitRecently(botOwner, 2.5f) && underThreat;
        }

        /// <summary>
        /// Check if boss/player wants to kill the current enemy (not just protect).
        /// </summary>
        public bool ProtectWantKill(float maxEnemyDistance = 50f)
        {
            return Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal <
                   botOwner.Settings.FileSettings.Mind.ATTACK_ENEMY_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        /// <summary>
        /// Check if follower should care about protecting/holding boss position.
        /// </summary>
        public bool ProtectCareKill(float maxEnemyDistance = 50f)
        {
            float protectSeenTime = Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal;
            return protectSeenTime < botOwner.Settings.FileSettings.Mind.HOLD_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        public static bool WasHitRecently(BotOwner bot, float seconds)
        {
            return Time.time - bot.Memory.LastTimeHit < seconds;
        }

        /// <summary>
        /// Shared dogfight-state probe used by both decision and end-condition logic.
        /// </summary>
        public bool IsDogFightActive() => botOwner.DogFight.DogFightState > BotDogFightStatus.none;

        // ──────────────────────────────────────────────────────────────────────────
        // End-condition dispatch
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shared end-condition dispatcher.
        /// Keep this focused on decisions that are common across follower combat implementations,
        /// so specialized logic classes can override before/after this call without duplicating base behavior.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return currentDecision.Action switch
            {
                BotLogicDecision.dogFight => EndDogFight(),
                BotLogicDecision.shootToSmoke => EndImmediately(),
                BotLogicDecision.runToCover => EndRunToCover(currentDecision.Reason),
                BotLogicDecision.attackMoving => EndAttackMoving(currentDecision.Reason),
                BotLogicDecision.attackMovingWithSuppress => EndAttackMovingWithSuppress(currentDecision.Reason),
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => EndAttackRetreat(currentDecision.Reason),
                BotLogicDecision.goToPointTactical => EndTacticalPoint(),
                BotLogicDecision.goToPoint => EndGoToPoint(),
                BotLogicDecision.runToEnemy => EndBaseGoToEnemy(),
                BotLogicDecision.goToEnemy => EndBaseGoToEnemy(),
                BotLogicDecision.shootFromPlace => EndShootFromPlace(currentDecision.Reason),
                BotLogicDecision.heal => EndHeal(),
                BotLogicDecision.healStimulators => EndStimulators(),
                BotLogicDecision.suppressFire => EndSuppressFire(currentDecision.Reason),
                BotLogicDecision.suppressGrenade => EndSuppressGrenade(),
                BotLogicDecision.shootFromCover => EndShootFromCover(),
                BotLogicDecision.search => EndEnemySearch(),
                _ => EndImmediately(),
            };
        }

        public AICoreActionEndStruct EndDogFight()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                ClearDogFightState();
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (ShouldSeekReloadRetreat(goalEnemy))
            {
                ClearDogFightState();
                return new AICoreActionEndStruct("reloadRetreatNeeded", true);
            }

            if ((goalEnemy == null || goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT) &&
                !botOwner.WeaponManager.Reload.Reloading &&
                !botOwner.Memory.BotCurrentCoverInfo.UseDogFight(botOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE))
            {
                dogFightBlockedUntil = Time.time + DogFightOutOfRangeCooldownSeconds;
                ClearDogFightState();
                return new AICoreActionEndStruct("dogFightOutOfRange", true);
            }

            return Continue();
        }

        /// <summary>
        /// Common run-to-cover stop conditions.
        /// Specialized logic can short-circuit this in its own dispatcher when needed.
        /// </summary>
        public AICoreActionEndStruct EndRunToCover(string? reason = null)
        {
            bool isRunToHeal = string.Equals(reason, "runToHeal", StringComparison.Ordinal);

            if (!isRunToHeal &&
                IsCommittedShootingCoverReason(reason) &&
                !HasActiveOrRetainedGoalEnemy(out _))
            {
                ClearCommittedCover();
                ClearCommittedMovement();
                return new AICoreActionEndStruct("shootCoverEnemyMissingOrDead", true);
            }

            if (!isRunToHeal && IsCloseVisibleShootableThreat(botOwner.Memory.GoalEnemy))
            {
                return new AICoreActionEndStruct("visibleCloseFireBreakCoverMove", true);
            }

            if (ShouldBreakRunToCoverForImmediateFire())
            {
                return new AICoreActionEndStruct("stableImmediateFire", true);
            }

            if (botOwner.Memory.IsInCover)
            {
                if (!isRunToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: true);
                }
                return new AICoreActionEndStruct("alreadyInCover", true);
            }

            // EFT cover flags can lag while movement has already reached the selected cover point.
            // Treat committed-cover proximity as arrival so decision routing can transition to hold/scan.
            if (IsBotInCommittedCover())
            {
                if (!isRunToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: true);
                }
                return new AICoreActionEndStruct("arrivedCommittedCover", true);
            }

            // Some move actions settle at the destination before IsInCover flips.
            // If the go-to-point path reports arrival, transition out of runToCover.
            if (botOwner.GoToSomePointData.IsCome())
            {
                if (!isRunToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: committedCoverPoint != null || botOwner.Memory.CurCustomCoverPoint != null);
                }
                return new AICoreActionEndStruct("arrivedCoverPoint", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (!isRunToHeal &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.IsSpotted)
            {
                return new AICoreActionEndStruct("coverSpotted", true);
            }

            AICoreActionEndStruct stalled = EndRunToCoverIfStalled(reason);
            if (stalled.Value)
            {
                return stalled;
            }

            return Continue();
        }

        private AICoreActionEndStruct EndRunToCoverIfStalled(string? reason)
        {
            CustomNavigationPoint? targetCover = committedCoverPoint ?? committedHealCover ?? botOwner.Memory?.CurCustomCoverPoint;
            if (targetCover == null || !IsFinite(targetCover.Position))
            {
                ResetRunToCoverProgress();
                return Continue();
            }

            float distance = Vector3.Distance(botOwner.Position, targetCover.Position);
            if (!IsFinite(distance))
            {
                ResetRunToCoverProgress();
                return Continue();
            }

            if (runToCoverProgressCoverId != targetCover.Id)
            {
                runToCoverProgressCoverId = targetCover.Id;
                runToCoverBestDistance = distance;
                runToCoverLastProgressTime = Time.time;
                return Continue();
            }

            if (distance <= runToCoverBestDistance - RunToCoverProgressMinDistance)
            {
                runToCoverBestDistance = distance;
                runToCoverLastProgressTime = Time.time;
                return Continue();
            }

            if (runToCoverLastProgressTime <= 0f ||
                Time.time - runToCoverLastProgressTime <= RunToCoverStallSeconds)
            {
                return Continue();
            }

            bool isRunToHeal = string.Equals(reason, "runToHeal", StringComparison.Ordinal);
            if (isRunToHeal)
            {
                BlockHealCover(targetCover);
                ClearCommittedHealCover();
                ResetRunToCoverProgress();
            }
            else if (committedCoverPoint != null && committedCoverPoint.Id == targetCover.Id)
            {
                ClearCommittedCover();
            }
            else
            {
                ResetRunToCoverProgress();
            }

            return new AICoreActionEndStruct("runToCoverStalled", true);
        }

        private void ResetRunToCoverProgress()
        {
            runToCoverProgressCoverId = -1;
            runToCoverBestDistance = float.MaxValue;
            runToCoverLastProgressTime = 0f;
        }

        public AICoreActionEndStruct EndTacticalPoint(
            bool endWhenCanShootFromCover = true,
            bool endWhenEnemyVisibleShootable = true)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null || !HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (endWhenCanShootFromCover && CanShootFromCurrentCover(out _))
            {
                HoldCoverForMaxDuration();
                ArmCommittedArrivalHold("tacticalShootCover", preferCover: true);
                return new AICoreActionEndStruct("foundShootCover", true);
            }

            if (endWhenEnemyVisibleShootable && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (botOwner.GoToSomePointData.IsCome() || IsAtTacticalPoint())
            {

                if (botOwner.Memory.IsInCover || IsBotInCommittedCover())
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold("tacticalPoint", preferCover: true);
                }
                else
                {
                    ArmCommittedArrivalHold("tacticalPoint", preferCover: false);
                }

                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            AICoreActionEndStruct stalled = EndTacticalPointIfStalled();
            if (stalled.Value)
            {
                return stalled;
            }

            return default;
        }

        private bool IsAtTacticalPoint()
        {
            if (botOwner.GoToSomePointData == null ||
                !botOwner.GoToSomePointData.HaveTarget())
            {
                return false;
            }

            Vector3 target = botOwner.GoToSomePointData.Point;
            return IsFinite(target) &&
                   (botOwner.Position - target).sqrMagnitude <= TacticalPointArrivalDistance * TacticalPointArrivalDistance;
        }

        private AICoreActionEndStruct EndTacticalPointIfStalled()
        {
            if (!botOwner.GoToSomePointData.HaveTarget())
            {
                ResetTacticalPointProgress();
                return Continue();
            }

            Vector3 target = botOwner.GoToSomePointData.Point;
            float distance = Vector3.Distance(botOwner.Position, target);

            if ((target - tacticalPointProgressTarget).sqrMagnitude > 1f)
            {
                tacticalPointProgressTarget = target;
                tacticalPointBestDistance = distance;
                tacticalPointLastProgressTime = Time.time;
                return Continue();
            }

            if (distance <= tacticalPointBestDistance - TacticalPointProgressMinDistance)
            {
                tacticalPointBestDistance = distance;
                tacticalPointLastProgressTime = Time.time;
                return Continue();
            }

            if (tacticalPointLastProgressTime <= 0f ||
                Time.time - tacticalPointLastProgressTime <= TacticalPointStallSeconds)
            {
                return Continue();
            }

            ResetTacticalPointProgress();
            return new AICoreActionEndStruct("tacticalPointStalled", true);
        }

        private void ResetTacticalPointProgress()
        {
            tacticalPointProgressTarget = Vector3.zero;
            tacticalPointBestDistance = float.MaxValue;
            tacticalPointLastProgressTime = 0f;
        }

        public AICoreActionEndStruct EndGoToPoint(bool endWhenEnemyVisibleShootable = true)
        {
            return EndTacticalPoint(
                endWhenCanShootFromCover: false,
                endWhenEnemyVisibleShootable: endWhenEnemyVisibleShootable);
        }

        public AICoreActionEndStruct EndAttackMoving(string? reason = null)
        {
            bool isMoveToHeal = string.Equals(reason, "moveToHeal", StringComparison.Ordinal);

            RefreshShootCover();
            if (HaveCoverToShoot && botOwner.Memory.IsInCover)
            {
                if (!isMoveToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason ?? "attackMovingShootCover", preferCover: true);
                }
                return new AICoreActionEndStruct("foundCoverToShoot", true);
            }

            return EndBaseAttackMoving(reason);
        }

        public AICoreActionEndStruct EndAttackMovingWithSuppress(string? reason = null)
        {
            return EndAttackMoving(reason);
        }

        public AICoreActionEndStruct EndAttackRetreat(string? reason = null)
        {
            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (IsPointBlankVisibleShootableThreat(goalEnemy))
            {
                return new AICoreActionEndStruct("retreatPointBlankVisibleThreat", true);
            }

            if (botOwner.Memory.IsInCover)
            {
                HoldCoverForMaxDuration();
                ArmCommittedArrivalHold(reason ?? "attackRetreat", preferCover: true);
                return new AICoreActionEndStruct("inCover", true);
            }

            return Continue();
        }

        private static bool IsPointBlankVisibleShootableThreat(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= PointBlankRetreatBlockDistance;
        }

        private static bool IsCloseVisibleShootableThreat(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= CloseVisibleThreatBreakDistance;
        }

        public bool CanRunToEnemyNow()
        {
            return Time.time >= runToEnemyBlockedUntil;
        }

        public void BlockRunToEnemy(float seconds)
        {
            runToEnemyBlockedUntil = Mathf.Max(runToEnemyBlockedUntil, Time.time + seconds);
        }

        public AICoreActionEndStruct EndShootFromPlace(string? reason = null)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                    !FollowerContactEnemyRetention.TryRestore(botOwner, out goalEnemy) ||
                    goalEnemy == null)
                {
                    return new AICoreActionEndStruct("enemyMissingOrDead", true);
                }
            }

            if (ShouldSeekReloadRetreat(goalEnemy) &&
                TryGetReloadRetreatDecision(goalEnemy, out _))
            {
                return new AICoreActionEndStruct("reloadRetreatNeeded", true);
            }

            if (botOwner.DogFight.ShallStartCauseHavePlace())
            {
                return new AICoreActionEndStruct("dogFightHavePlace", true);
            }

            if (FollowerImmediateFirePolicy.IsImmediateShootReason(reason) &&
                !goalEnemy.IsVisible &&
                !goalEnemy.CanShoot &&
                !CanContinueImmediateLostVisualFire(goalEnemy))
            {
                return new AICoreActionEndStruct("immediateLostVisualExpired", true);
            }

            if (!goalEnemy.CanShoot)
            {
                if (FollowerImmediateFirePolicy.IsImmediateShootReason(reason) &&
                    CanContinueImmediateLostVisualFire(goalEnemy))
                {
                    return Continue();
                }

                return new AICoreActionEndStruct("enemyCannotShoot", true);
            }

            if (ShouldShootImmediately())
            {
                return Continue();
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (goalEnemy.Distance < 1f)
            {
                return new AICoreActionEndStruct("enemyTooClose", true);
            }

            if (botOwner.WeaponManager.Reload.Reloading)
            {
                return Continue();
            }

            return Continue();
        }

        private bool CanContinueImmediateLostVisualFire(EnemyInfo goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            return FollowerImmediateFirePolicy.HasDirectFireLane(botOwner, target) &&
                   !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, target);
        }

        public AICoreActionEndStruct EndHeal()
        {
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do || botOwner.Medecine.SurgicalKit.HaveWork;
            bool activelyHealing = botOwner.Medecine.FirstAid.Using || botOwner.Medecine.SurgicalKit.Using;
            if (!haveHealWork)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healCompleted", true);
            }

            // If the heal action never transitions into active first-aid/surgery use, do not let the
            // bot sit in healInCover forever waiting on a stuck vanilla node.
            if (!activelyHealing &&
                healStartedAt > 0f &&
                healStartedAt + 3f < Time.time)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healIdleTimedOut", true);
            }

            float timeout = botOwner.Medecine.SurgicalKit.Using ? 45f : 15f;
            if (healStartedAt > 0f && healStartedAt + timeout < Time.time)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healTimedOut", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndStimulators()
        {
            if (!botOwner.Medecine.Stimulators.Using)
            {
                stimStartedAt = 0f;
                FollowerMedical.RefreshMedicalWork(botOwner);
                return new AICoreActionEndStruct("stimsCompleted", true);
            }

            if (stimStartedAt > 0f && stimStartedAt + 5f < Time.time)
            {
                botOwner.Medecine.Stimulators.CancelCurrent();
                stimStartedAt = 0f;
                FollowerMedical.RefreshMedicalWork(botOwner);
                return new AICoreActionEndStruct("stimsTimedOut", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndSuppressFire(string? reason = null)
        {
            if (IsFollowerSuppressReason(reason))
            {
                return EndFollowerSuppressFire(reason);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (ShouldShootImmediately())
            {
                return new AICoreActionEndStruct("shootImmediately", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (goalEnemy != null && FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                return Continue();
            }

            // If enemy cannot be shot (not visible or can't shoot), suppress fire ends
            if (goalEnemy != null && (!goalEnemy.CanShoot || !goalEnemy.IsVisible))
            {
                return new AICoreActionEndStruct("enemyNotShootable", true);
            }

            return Continue();
        }

        private AICoreActionEndStruct EndFollowerSuppressFire(string? reason)
        {
            bool ordered = IsOrderedSuppressReason(reason) ||
                           FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason);
            bool commandOwned = IsOrderedSuppressReason(reason);
            BotFollowerPlayer? followerData = commandOwned ? BossPlayers.Instance?.GetFollower(botOwner) : null;
            if (commandOwned &&
                (followerData == null ||
                 !followerData.TryGetActiveCommand(out FollowerCommandType command, out _) ||
                 command != FollowerCommandType.SuppressEnemy))
            {
                return new AICoreActionEndStruct("orderedSuppressCommandMissing", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                followerData?.ClearCommand("SuppressEnemy:noActiveEnemy");
                return new AICoreActionEndStruct("followerSuppressEnemyMissingOrDead", true);
            }

            if (botOwner.SuppressShoot == null)
            {
                followerData?.ClearCommand("SuppressEnemy:missingSuppressShoot");
                return new AICoreActionEndStruct("followerSuppressMissingController", true);
            }

            float suppressElapsed = activeFollowerSuppressStartedAt > 0f
                ? Time.time - activeFollowerSuppressStartedAt
                : 0f;
            float protectedSeconds = GetFollowerSuppressProtectedSeconds(ordered);

            if (!ordered && suppressElapsed >= AutoSuppressMaxSeconds)
            {
                return new AICoreActionEndStruct("autoSuppressTimedOut", true);
            }

            if (botOwner.SuppressShoot.Complete)
            {
                if (suppressElapsed < protectedSeconds)
                {
                    RestartFollowerSuppress(goalEnemy);
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:complete");
                return new AICoreActionEndStruct("followerSuppressComplete", true);
            }

            Vector3? point = botOwner.SuppressShoot.GetPoint();
            if (!point.HasValue || !IsFinite(point.Value))
            {
                if (suppressElapsed < protectedSeconds)
                {
                    RestartFollowerSuppress(goalEnemy);
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:missingTarget");
                return new AICoreActionEndStruct("followerSuppressMissingTarget", true);
            }

            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
            if (ShouldBreakFollowerSuppressForPointBlankContact(goalEnemy, point.Value, fireOrigin))
            {
                followerData?.ClearCommand("SuppressEnemy:pointBlankNonFoliageContact");
                return new AICoreActionEndStruct("pointBlankNonFoliageContact", true);
            }

            if (FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, point.Value))
            {
                if (suppressElapsed < protectedSeconds)
                {
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:blockedLane");
                return new AICoreActionEndStruct("followerSuppressBlockedLane", true);
            }

            if (!Utils.Utils.CanShootToTarget(
                    new ShootPointClass(point.Value, 1f),
                    fireOrigin,
                    botOwner.LookSensor.Mask,
                    false) &&
                !IsSoftObstructedSuppressionLane(fireOrigin, point.Value))
            {
                if (suppressElapsed < protectedSeconds)
                {
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:hardBlockedLane");
                return new AICoreActionEndStruct("followerSuppressHardBlockedLane", true);
            }

            return Continue();
        }

        private bool ShouldBreakFollowerSuppressForPointBlankContact(
            EnemyInfo goalEnemy,
            Vector3 suppressTarget,
            Vector3 fireOrigin)
        {
            if (!IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy) ||
                !HasRecentPointBlankContact(goalEnemy))
            {
                return false;
            }

            return !HasConfirmedCloseSuppressFoliage(goalEnemy, suppressTarget, fireOrigin);
        }

        private static bool HasRecentPointBlankContact(EnemyInfo goalEnemy)
        {
            return goalEnemy.IsVisible ||
                   Time.time - goalEnemy.PersonalSeenTime <= CloseSuppressRecentContactSeconds ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= CloseSuppressRecentContactSeconds;
        }

        private bool HasConfirmedCloseSuppressFoliage(
            EnemyInfo goalEnemy,
            Vector3 suppressTarget,
            Vector3 fireOrigin)
        {
            LayerMask mask = botOwner.LookSensor?.Mask ?? LayerMaskClass.HighPolyWithTerrainMaskAI;
            if (IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, mask))
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyCurrentPosition(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 capsuleStart = botOwner.Position + Vector3.up * 0.7f;
            Vector3 capsuleEnd = enemyAnchor + Vector3.up * 1.25f;
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                capsuleStart,
                capsuleEnd,
                CloseSuppressFoliageProbeRadius,
                closeSuppressFoliageBuffer,
                LayerMaskClass.AI);

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = closeSuppressFoliageBuffer[i];
                closeSuppressFoliageBuffer[i] = null;
                if (collider != null && IsSoftFoliageCollider(collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetFollowerSuppressProtectedSeconds(bool ordered)
        {
            return ordered ? OrderedSuppressMinSeconds : AutoSuppressMinSeconds;
        }

        private bool RestartFollowerSuppress(EnemyInfo goalEnemy)
        {
            if (botOwner.SuppressShoot == null ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            CustomNavigationPoint? suppressFrom = botOwner.SuppressShoot.PointToSuppressFrom;
            return botOwner.SuppressShoot.InitToPoint(suppressTarget, suppressFrom);
        }

        public static bool IsFollowerSuppressReason(string? reason)
        {
            return IsOrderedSuppressReason(reason) ||
                   IsAutoSuppressReason(reason) ||
                   FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason);
        }

        public static bool IsOrderedSuppressReason(string? reason)
        {
            return reason != null && reason.StartsWith("orderedSuppress", StringComparison.Ordinal);
        }

        public static bool IsAutoSuppressReason(string? reason)
        {
            return reason != null && reason.StartsWith("autoSuppress", StringComparison.Ordinal);
        }

        public AICoreActionEndStruct EndSuppressGrenade()
        {
            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            if (grenades == null)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("grenadeControllerMissing", true);
            }

            float lastPeriod = botOwner.Brain?.Agent?.LastPeriod ?? Time.time;
            if (Time.time - lastPeriod > 6f)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeTimeout", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner) &&
                IsGrenadeThrowUnsafe(goalEnemy))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeUnsafe", true);
            }

            if (!HasAnyActiveCombatEnemy() &&
                (grenades.ThrowindNow || grenades.ReadyToThrow) &&
                !FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeCanceledNoEnemies", true);
            }

            if (grenades.ThrowindNow || grenades.ReadyToThrow)
            {
                return Continue();
            }

            if (botOwner.SuppressGrenade != null && !botOwner.SuppressGrenade.Complete)
            {
                return Continue();
            }

            ClearCommittedGrenade();
            return new AICoreActionEndStruct("suppressGrenadeComplete", true);
        }

        public AICoreActionEndStruct EndEnemySearch()
        {

            if (!botOwner.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner.Memory.GoalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (Time.time - botOwner.Memory.LastTimeHit <= 1f)
            {
                return new AICoreActionEndStruct("enemy.ShotMe", true);
            }

            if (botOwner.SearchData.SearchPoint == null)
            {
                return new AICoreActionEndStruct("search.End", true);
            }

            return Continue();
        }

        /// <summary>
        /// Initializes the vanilla suppress-grenade flow only when the target is visible, not already
        /// a clean gunfight, and the throw is safe for the boss/followers.
        /// </summary>
        public bool TryActivateFollowerGrenade(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (!pitFireTeam.botGrenades.Value)
            {
                return RejectFollowerGrenade("disabled", goalEnemy: goalEnemy);
            }

            if (goalEnemy == null || !goalEnemy.IsVisible || goalEnemy.Person == null)
            {
                return RejectFollowerGrenade("noVisibleEnemy", goalEnemy: goalEnemy);
            }

            if (goalEnemy.Distance < 15f || goalEnemy.Distance > 28f)
            {
                return RejectFollowerGrenade("distance", goalEnemy: goalEnemy);
            }

            if (botOwner.WeaponManager == null || botOwner.WeaponManager.IsMelee)
            {
                return RejectFollowerGrenade("weaponUnavailable", goalEnemy: goalEnemy);
            }

            if (botOwner.BotRequestController == null)
            {
                return RejectFollowerGrenade("requestControllerMissing", goalEnemy: goalEnemy);
            }

            if (botOwner.BotRequestController.HaveActivatedRequests())
            {
                return RejectFollowerGrenade("activeRequest", goalEnemy: goalEnemy);
            }

            if (botOwner.Medecine.Using)
            {
                return RejectFollowerGrenade("medicine", goalEnemy: goalEnemy);
            }

            if (!IsSafeGrenadeThrowPosition(goalEnemy))
            {
                return RejectFollowerGrenade("unsafePosition", goalEnemy: goalEnemy);
            }

            if (!FollowerGrenadeCooldowns.TryReserveThrow(botOwner))
            {
                return RejectFollowerGrenade("cooldown", goalEnemy: goalEnemy);
            }

            if (IsDogFightActive() ||
                botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 2f) ||
                Time.time - goalEnemy.FirstTimeSeen < 1.5f)
            {
                return RejectFollowerGrenade("dogfightOrPressure", goalEnemy, cancelPending: true);
            }

            if (goalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return RejectFollowerGrenade("cleanShot", goalEnemy, cancelPending: true);
            }

            FollowerGrenadeRuntimeGate.EnableExplicitThrow(botOwner);
            if (botOwner.WeaponManager.Grenades == null ||
                botOwner.SuppressGrenade == null)
            {
                return RejectFollowerGrenade("grenadeControllerMissing", goalEnemy, cancelPending: true, disableGate: true);
            }

            EnemyInfo suppressEnemy = GetSuppressGrenadeTarget(goalEnemy, out ThrowWeapType? preferredThrowType);
            Vector3 targetPosition = suppressEnemy.CurrPosition;
            if (IsFriendlyTooCloseToGrenadeTarget(targetPosition, 8f))
            {
                return RejectFollowerGrenade("friendlyTooClose", suppressEnemy, cancelPending: true, disableGate: true);
            }

            if (preferredThrowType != null &&
                botOwner.WeaponManager.Grenades.HaveGrenadeOfType(preferredThrowType.Value) &&
                botOwner.SuppressGrenade.Init(suppressEnemy, preferredThrowType, null, AIGreandeAng.ang45))
            {
                FollowerGrenadeCooldowns.RecordThrow(botOwner);
                BattleRecorder.RecordGrenadeEvent(botOwner, "init", "SupGrenade", goalEnemy: suppressEnemy);
                HoldFor(botOwner.Settings.FileSettings.Boss.KILLA_AFTER_GRENADE_SUPPRESS_DELAY);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressGrenade, "SupGrenade");
                CommitGrenadeDecision(decision);
                return true;
            }

            ThrowWeapType throwType = ThrowWeapType.frag_grenade;
            if (botOwner.WeaponManager.Grenades.HaveGrenadeOfType(throwType))
            {
                if (botOwner.SuppressGrenade.Init(suppressEnemy, throwType, null, AIGreandeAng.ang45))
                {
                    FollowerGrenadeCooldowns.RecordThrow(botOwner);
                    BattleRecorder.RecordGrenadeEvent(botOwner, "init", "SupGrenade2", goalEnemy: suppressEnemy);
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressGrenade, "SupGrenade2");
                    CommitGrenadeDecision(decision);
                    return true;
                }
            }
            else
            {
                throwType = ThrowWeapType.stun_grenade;
                if (botOwner.WeaponManager.Grenades.HaveGrenadeOfType(throwType) &&
                    botOwner.SuppressGrenade.Init(suppressEnemy, throwType, null, AIGreandeAng.ang45))
                {
                    FollowerGrenadeCooldowns.RecordThrow(botOwner);
                    BattleRecorder.RecordGrenadeEvent(botOwner, "init", "SupGrenade3", goalEnemy: suppressEnemy);
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressGrenade, "SupGrenade3");
                    CommitGrenadeDecision(decision);
                    return true;
                }
            }

            return RejectFollowerGrenade("initFailedOrNoGrenade", suppressEnemy, cancelPending: true, disableGate: true);
        }

        private bool IsSafeGrenadeThrowPosition(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 2f))
            {
                return false;
            }

            if (botOwner.Mover?.HasPathAndNoComplete == true)
            {
                return false;
            }

            if (!botOwner.Memory.IsInCover || botOwner.Memory.CurCustomCoverPoint == null)
            {
                return !goalEnemy.CanShoot;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            return !IsFinite(enemyAnchor) ||
                   botOwner.Memory.CurCustomCoverPoint.CanIHideFromPos(0f, true, false, enemyAnchor);
        }

        private bool RejectFollowerGrenade(
            string reason,
            EnemyInfo? goalEnemy = null,
            bool cancelPending = false,
            bool disableGate = false)
        {
            if (cancelPending)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
            }

            if (disableGate)
            {
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
            }

            RecordFollowerGrenadeReject(reason, goalEnemy);
            return false;
        }

        private void RecordFollowerGrenadeReject(string reason, EnemyInfo? goalEnemy)
        {
            if (string.Equals(lastFollowerGrenadeRejectReason, reason, StringComparison.Ordinal) &&
                Time.time < nextFollowerGrenadeRejectRecordAt)
            {
                return;
            }

            lastFollowerGrenadeRejectReason = reason;
            nextFollowerGrenadeRejectRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(botOwner, "reject", reason, goalEnemy: goalEnemy);
        }

        private bool IsGrenadeThrowUnsafe(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return true;
            }

            if (botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 2f))
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            return false;
        }

        private EnemyInfo GetSuppressGrenadeTarget(EnemyInfo goalEnemy, out ThrowWeapType? preferredThrowType)
        {
            preferredThrowType = null;
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return goalEnemy;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (enemyInfo != goalEnemy && enemyInfo.IsSuppressed())
                {
                    preferredThrowType = ThrowWeapType.smoke_grenade;
                    return enemyInfo;
                }
            }

            return goalEnemy;
        }

        private bool IsFriendlyTooCloseToGrenadeTarget(Vector3 targetPosition, float unsafeRadius)
        {
            float unsafeRadiusSqr = unsafeRadius * unsafeRadius;

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            Player bossPlayer = boss.realPlayer;
            if (bossPlayer != null && (bossPlayer.Position - targetPosition).sqrMagnitude <= unsafeRadiusSqr)
            {
                return true;
            }

            var followers = boss.Followers;
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

        private void CancelActiveHealIfNeeded()
        {
            ClearCommittedHealCover();
            FollowerMedical.CancelActiveMedical(botOwner);
        }

        private void CompleteActiveHeal()
        {
            ClearCommittedHealCover();
            FollowerMedical.CompleteHealing(botOwner);
            healBlockUntil = Time.time + 5f;
            healStartedAt = 0f;
        }

        public AICoreActionEndStruct EndShootFromCover()
        {
            if (CanShootFromCurrentCover(out string cause))
            {
                shootFromCoverGraceUntil = Time.time + ShootFromCoverLosFlickerGraceSeconds;
                return Continue();
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (HasActiveCombatEnemy(goalEnemy) &&
                goalEnemy != null &&
                goalEnemy.IsVisible &&
                Time.time < shootFromCoverGraceUntil)
            {
                return Continue();
            }

            shootFromCoverGraceUntil = 0f;
            return new AICoreActionEndStruct(cause, true);
        }

        public AICoreActionEndStruct EndThrowGrenadeFromPlace()
        {
            BotRequest? currentRequest = botOwner.BotRequestController?.CurRequest;
            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            if (grenades != null &&
                !HasAnyActiveCombatEnemy() &&
                (grenades.ThrowindNow || grenades.ReadyToThrow) &&
                !FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("grenadeCanceledNoEnemies", true);
            }

            bool grenadeSequenceActive =
                grenades != null &&
                (grenades.ThrowindNow || grenades.ReadyToThrow);
            bool grenadeRequestActive =
                currentRequest?.BotRequestType == BotRequestType.throwGrenade ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenadeFromPlace;
            if (grenadeSequenceActive || grenadeRequestActive)
            {

                return Continue();
            }

            ClearCommittedGrenade();
            return new AICoreActionEndStruct("grenadeRequestFinished", true);
        }

        private static void AbortPendingGrenadeThrow(BotGrenadeController grenades)
        {
            grenades?.method_6(null);
        }

        public AICoreActionEndStruct EndBaseGoToPoint()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (botOwner.GoToSomePointData.IsCome())
            {
                ArmCommittedArrivalHold("goToPoint", preferCover: false);
                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndBaseGoToEnemy()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (ShouldBreakAdvanceForImmediateFire())
            {
                return new AICoreActionEndStruct("stableAdvanceFire", true);
            }

            if (!IsDogFightActive() && (!goalEnemy.IsVisible || !goalEnemy.CanShoot))
            {
                return Continue();
            }

            return new AICoreActionEndStruct("dogFightConditionsMet", true);
        }

        public AICoreActionEndStruct EndBaseAttackMoving(string? reason = null)
        {
            bool isMoveToHeal = string.Equals(reason, "moveToHeal", StringComparison.Ordinal);

            if (!isMoveToHeal &&
                IsCommittedShootingCoverReason(reason) &&
                !HasActiveOrRetainedGoalEnemy(out _))
            {
                ClearCommittedCover();
                ClearCommittedMovement();
                return new AICoreActionEndStruct("shootCoverEnemyMissingOrDead", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightActive", true);
            }

            if (botOwner.Memory.IsInCover)
            {
                if (!isMoveToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: true);
                }
                return new AICoreActionEndStruct("inCover", true);
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                return new AICoreActionEndStruct("stationary", true);
            }

            return Continue();
        }

        public void HoldFor(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            holdEndTime = Time.time + seconds;
            holdActive = true;
        }

        /// <summary>
        /// Hold in cover for a tactic/aggression-aware duration. Marksman holds longer; aggressive follows hold shorter.
        /// Use this instead of explicit seconds for follower combat hold decisions to respect tactic intent.
        /// </summary>
        public void HoldCoverForMaxDuration()
        {
            if (holdActive && holdEndTime > Time.time)
            {
                return;
            }

            HoldFor(GetMaxCoverHoldDuration());
        }

        public static bool IsStableNoCoverHoldReason(string reason)
        {
            return string.Equals(reason, "goalEnemy.P", StringComparison.Ordinal) ||
                   string.Equals(reason, "canShootLas", StringComparison.Ordinal) ||
                   string.Equals(reason, "deltaLastHi", StringComparison.Ordinal) ||
                   string.Equals(reason, "unsafePushBossHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "escortNoSafeCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "recoveryCoverHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHoldOpen", StringComparison.Ordinal) ||
                   string.Equals(reason, "reloadNoCover", StringComparison.Ordinal) ||
                   reason.StartsWith("committedPositionHold", StringComparison.Ordinal) ||
                   reason.StartsWith("committedCoverHold", StringComparison.Ordinal);
        }

        public AICoreActionEndStruct EndBaseHoldPosition(string reason)
        {
            if (HasActiveCombatGestureOrder())
            {
                return new AICoreActionEndStruct("combatGestureBreakHold", true);
            }

            if (holdActive && holdEndTime < Time.time)
            {
                holdActive = false;
                return new AICoreActionEndStruct("holdExpired", true);
            }

            if (string.Equals(reason, "reloadNoCover", StringComparison.Ordinal) &&
                botOwner.WeaponManager?.Reload?.Reloading == true)
            {
                return Continue();
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                return new AICoreActionEndStruct("underFireHold", true);
            }

            if (!botOwner.Memory.IsInCover)
            {
                if (!IsStableNoCoverHoldReason(reason))
                {
                    return new AICoreActionEndStruct("notInCover", true);
                }

                // No-cover hold reasons are allowed to crouch-wait, but not under active pressure.
                if (botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 0.5f))
                {
                    return new AICoreActionEndStruct("underFireNoCover", true);
                }
            }

            if (goalEnemy == null)
            {
                return new AICoreActionEndStruct("canSearchEnemy", true);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < botOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
            {
                return new AICoreActionEndStruct("enemyCloseAndVisible", true);
            }

            return Continue();
        }

        /// <summary>
        /// Convenience terminal result for decisions that always end in one update.
        /// </summary>
        public static AICoreActionEndStruct EndImmediately() => new AICoreActionEndStruct(string.Empty, true);

        public static AICoreActionEndStruct Continue() => default;

        /// <summary>
        /// Determines if heal cover should be cleared due to improved health, increased threat, or exceeded duration.
        /// </summary>
        private bool ShouldClearHealCover(EnemyInfo? goalEnemy, out string? clearReason)
        {
            clearReason = null;

            if (committedHealCover == null)
            {
                return false;
            }

            // Exit if health status now healthy (healed enough to rejoin)
            if (botOwner.GetPlayer?.HealthStatus == ETagStatus.Healthy)
            {
                clearReason = "healthy";
                return true;
            }

            // Exit if enemy pushed closer (cover ineffective against new threat)
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                float enemyDist = Vector3.Distance(botOwner.Position, goalEnemy.CurrPosition);
                if (enemyDist < CombatDistanceConfiguration.Instance.GetHealCoverRetreatDistance() * 0.6f)  // Enemy too close relative to retreat distance
                {
                    clearReason = "enemyClose";
                    return true;
                }
            }

            // Exit if heal cycle exceeded reasonable max duration (prevents indefinite heal holds)
            const float MaxHealDurationSeconds = 20f;
            if (Time.time - healStartedAt > MaxHealDurationSeconds)
            {
                clearReason = "timeout";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the maximum time a follower should hold in cover based on tactic and aggression.
        /// Marksman holds longer to provide sniper support; defensive followers hold at assigned positions.
        /// </summary>
        private float GetMaxCoverHoldDuration()
        {
            FollowerCombatTactic tactic = GetFollowerTactic();
            float aggression = GetAggression01();

            // Base hold duration by tactic
            float baseDuration = tactic switch
            {
                FollowerCombatTactic.Marksman => 12f,      // Snipers hold longer for optimal shots and teammate support
                FollowerCombatTactic.Protector => 8f,      // Defensive followers hold their assigned position
                FollowerCombatTactic.Balanced => 6f,       // Balanced, more active repositioning
                _ => 6f
            };

            // Aggression multiplier (lower aggression = longer hold, higher = shorter)
            // Range: 1.5x (very passive) to 1.0x (very aggressive)
            float aggressionMultiplier = 1f + (0.5f * (1f - Mathf.Clamp01(aggression)));

            return baseDuration * aggressionMultiplier;
        }
    }
}
