using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatSniper
    {
        private const float CloseQuarterDistance = 20f;
        private const float ClosePushDistance = 14f;
        private const float BossSupportShootCoverRadius = 30f;
        private const float MarksmanStartLowThreatAggression = 0.3f;

        private string? lastLoggedReason;
        private float nextLogAt;
        private readonly BotOwner BotOwner;
        private readonly FollowerCombatCommon CombatCommon;

        public FollowerCombatSniper(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            BotOwner = botOwner;
            CombatCommon = combatCommon;
        }

        public void Reset()
        {
            CombatCommon.ResetCommittedCover();
            lastLoggedReason = null;
            nextLogAt = 0f;
        }

        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            ApplyMarksmanWeaponPolicy(BotOwner.Memory.GoalEnemy, nextDecision);
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
        }

        public void StartDecision()
        {
            PrepareStartDecision();
        }

        public void PrepareStartDecision()
        {
            PrepareMarksmanStartDecision();
        }

        private void PrepareMarksmanStartDecision()
        {
            CombatCommon.ClearInitialDecision();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close &&
                CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat(CloseQuarterDistance))
            {
                // Marksman does not inherit the normal aggression slider for combat entry. Use a
                // fixed cautious baseline so it only close-searches targets we consider weak enough.
                bool isLowThreat = CombatCommon.IsEnemyLowThreat(goalEnemy, MarksmanStartLowThreatAggression);
                if (isLowThreat)
                {
                    CombatCommon.SetInitialDecision(CombatCommon.EnemySearch("sniper.startCloseSearch"));
                    return;
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26>? suppressDecision =
                    CombatCommon.TryGetAllyEngagementSupportDecision(true);
                if (suppressDecision != null)
                {
                    CombatCommon.SetInitialDecision(suppressDecision.Value);
                    return;
                }

                if (TryCreateCloseSuppressMove(goalEnemy, "sniper.startCloseSuppress", out AICoreActionResultStruct<BotLogicDecision, GClass26> closeSuppress))
                {
                    CombatCommon.SetInitialDecision(closeSuppress);
                }

                return;
            }

            if (CombatCommon.TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? startCover, out _, out _) &&
                CombatCommon.IsCoverUsable(startCover))
            {
                CombatCommon.AssignCover(startCover);
                BotLogicDecision moveAction = CombatCommon.SelectCommittedCoverMoveAction(goalEnemy);
                CombatCommon.SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    moveAction,
                    FollowerCombatCommon.CreateMovementReason("sniper.startPosition", moveAction)));
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            ClearAggressiveRequests();

            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = TryGetMarksmanPreFightDecision(goalEnemy);
            if (preFight != null)
            {
                return preFight.Value;
            }

            if (CombatCommon.HasInitialDecision)
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26> startDecision = CombatCommon.ConsumeInitialDecision();
                return startDecision;
            }

            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return Regroup();
            }

            CombatCommon.RefreshShootCover();
            CombatCommon.ValidateCommittedCover();

            // Close-quarter handling is marksman policy: secondary weapon and compact movement,
            // but still before long-range cover/reposition logic.
            if (TryGetCloseQuarterDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> closeQuarter))
            {
                return closeQuarter;
            }

            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visible))
            {
                return visible;
            }

            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recover))
            {
                return recover;
            }

            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> bossSupport))
            {
                return bossSupport;
            }

            if (TryGetSniperSupportDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> support))
            {
                return support;
            }

            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committed))
            {
                return committed;
            }

            if (TryGetRepositionDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> reposition))
            {

                return reposition;
            }

            return Regroup();
        }

        /// <summary>
        /// Handles the marksman-only close-quarter branch: automatic secondary first, then compact pressure.
        /// </summary>
        private bool TryGetCloseQuarterDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            bool closeEnoughForSecondary = goalEnemy.Distance <= CloseQuarterDistance;
            bool secondaryReady = closeEnoughForSecondary &&
                                  CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat(CloseQuarterDistance);

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                if (dogFight.Value.Action == BotLogicDecision.dogFight &&
                    !secondaryReady &&
                    closeEnoughForSecondary &&
                    !CombatCommon.IsCurrentWeaponAutomatic())
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.closePrimaryShoot");
                    return true;
                }

                if (dogFight.Value.Action == BotLogicDecision.dogFight &&
                    !secondaryReady &&
                    CombatCommon.IsHoldingPrimaryAtRange(goalEnemy, Enemy.EnemyDistance.Mid))
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.forcedShootFromPlace");
                    return true;
                }

                decision = dogFight.Value;
                return true;
            }

            if (!secondaryReady)
            {
                return false;
            }

            if (goalEnemy.Distance <= ClosePushDistance)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.runToEnemy,
                    "sniper.closeAutoPush");
                return true;
            }

            if (!TryCreateCloseSuppressMove(goalEnemy, "sniper.closeAutoSuppress", out decision))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Marksman still uses the shared prefight gates, but a generic immediate-shoot handoff
        /// must not steal ownership from close-quarter secondary logic mid-fight.
        /// </summary>
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetMarksmanPreFightDecision(EnemyInfo goalEnemy)
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = CombatCommon.PreFightLogic();
            if (preFight == null)
            {
                return null;
            }

            if (preFight.Value.Action == BotLogicDecision.shootFromPlace &&
                string.Equals(preFight.Value.Reason, "ShootImmediately", StringComparison.Ordinal) &&
                ShouldPreserveCloseQuarterWeaponFlow(goalEnemy))
            {
                return null;
            }

            return preFight;
        }

        /// <summary>
        /// If marksman close-quarter logic should own the frame, do not let generic shootFromPlace
        /// re-evaluation interrupt it and risk a mid-fight weapon swap.
        /// </summary>
        private bool ShouldPreserveCloseQuarterWeaponFlow(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null || goalEnemy.Distance > CloseQuarterDistance)
            {
                return false;
            }

            if (CombatCommon.IsCurrentWeaponAutomatic())
            {
                return true;
            }

            return CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat(CloseQuarterDistance);
        }

        /// <summary>
        /// Re-asserts marksman close-quarter weapon policy on decision handoff so generic action
        /// transitions do not flip the bot back to the sniper rifle right as close combat starts.
        /// </summary>
        private void ApplyMarksmanWeaponPolicy(
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (goalEnemy == null || goalEnemy.Distance > CloseQuarterDistance)
            {
                return;
            }

            if (!ShouldForceCloseQuarterSecondary(decision))
            {
                return;
            }

            CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat(CloseQuarterDistance);
        }

        private static bool ShouldForceCloseQuarterSecondary(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Action == BotLogicDecision.dogFight ||
                decision.Action == BotLogicDecision.shootFromPlace ||
                decision.Action == BotLogicDecision.shootFromCover ||
                decision.Action == BotLogicDecision.runToEnemy)
            {
                return true;
            }

            return decision.Reason != null &&
                   decision.Reason.StartsWith("sniper.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Close marksman suppression is movement, so only emit it after a real destination was committed.
        /// </summary>
        private bool TryCreateCloseSuppressMove(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    reason,
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: true))
            {
                return false;
            }

            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            string supportReason = CreateMovementReason("sniper.supportPosition", decision.Action);
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(supportReason);
            return true;
        }

        /// <summary>
        /// Marksman visible-contact policy: shoot if possible, otherwise commit a firing position.
        /// </summary>
        private bool TryGetVisibleDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                CombatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "sniper.shootFromCover");
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? immediate = CombatCommon.TryGetImmediateShootDecision("sniper.immediateShoot");
            if (immediate != null)
            {
                if (ShouldPreserveCloseQuarterWeaponFlow(goalEnemy))
                {
                    return false;
                }

                decision = immediate.Value;
                return true;
            }

            string reason = BotOwner.Memory.IsInCover ? "sniper.relocate" : "sniper.coverMove";
            if (CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    reason,
                    out string committedReason,
                    preferPointToShoot: true,
                    preferInbetween: !BotOwner.Memory.IsInCover))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(committedReason);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Recovery is tactic-neutral in intent: if the marksman is exposed and hurt/pressured, move to cover.
        /// </summary>
        private bool TryGetRecoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (BotOwner.Memory.IsInCover)
            {
                return false;
            }

            bool needCover =
                BotOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(BotOwner, 1f) ||
                CombatCommon.IsFollowerCriticallyWounded();
            if (!needCover)
            {
                return false;
            }

            if (CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.recoverCover",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: true))
            {
                decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "sniper.recoverNoCover");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Marksman support uses ally contact only as a firing-position cue, not as a rush/push trigger.
        /// </summary>
        private bool TryGetSniperSupportDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (goalEnemy.IsVisible || BotOwner.Memory.IsUnderFire)
            {
                return false;
            }

            if (!CombatCommon.TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out _))
            {
                return false;
            }

            CombatCommon.TryPromoteTrackedEnemyAsGoal(supportEnemyProfileId);
            EnemyInfo? promotedEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(promotedEnemy))
            {
                return false;
            }

            if (!CombatCommon.TryCommitFiringPositionCover(
                    promotedEnemy,
                    "sniper.FireSupport",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: true))
            {
                return false;
            }

            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            return true;
        }

        /// <summary>
        /// Keeps an existing committed firing position sticky once chosen.
        /// </summary>
        private bool TryGetCommittedCoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CombatCommon.HasCommittedCover())
            {
                return false;
            }

            if (CombatCommon.IsBotInCommittedCover())
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot && CombatCommon.CanShootFromCurrentCover(out _))
                {
                    CombatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromCover,
                        "sniper.committedFire");
                    return true;
                }

                if (goalEnemy.IsVisible)
                {
                    CombatCommon.ClearCommittedCover();
                    return false;
                }

                // Unseen break-outs should stay marksman-safe: allow relocalization/support/regroup
                // exits, but do not reuse default advance-pressure behavior for sniper tactic.
                if (CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    CombatCommon.ClearCommittedCover();
                    return false;
                }

                if (ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    CombatCommon.ClearCommittedCover();
                    return false;
                }

                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                {
                    CombatCommon.ClearCommittedCover();
                    return false;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "sniper.coverHold");
                return true;
            }

            CombatCommon.AssignCommittedCover();
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        /// <summary>
        /// With no active visible shot, look for a better firing position instead of default pressure.
        /// </summary>
        private bool TryGetRepositionDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.reposition",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: false))
            {
                return false;
            }

            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
            string repositionReason = CreateMovementReason("sniper.reposition", decision.Action);
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(repositionReason);
            return true;
        }

        private static string CreateMovementReason(string baseReason, BotLogicDecision action)
        {
            string fastReason = baseReason == "sniper.reposition"
                ? "sniper.repositionFast"
                : $"{baseReason}Fast";
            string suppressReason = baseReason == "sniper.reposition"
                ? "sniper.repositionSuppress"
                : $"{baseReason}Suppress";

            return action switch
            {
                BotLogicDecision.runToCover => fastReason,
                BotLogicDecision.attackMovingWithSuppress => suppressReason,
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => $"{baseReason}Retreat",
                _ => baseReason,
            };
        }

        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return EndHoldPosition(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.goToPointTactical)
            {
                return CombatCommon.EndTacticalPoint(currentDecision.Reason, endWhenCanShootFromCover: true);
            }

            if (currentDecision.Reason != null &&
                currentDecision.Reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal))
            {
                if (!CombatCommon.HasActiveCombatEnemy())
                {
                    return new AICoreActionEndStruct("sniperCloseAutoNoEnemy", true);
                }

                if (currentDecision.Action == BotLogicDecision.runToEnemy ||
                    currentDecision.Action == BotLogicDecision.goToEnemy)
                {
                    return CombatCommon.EndBaseGoToEnemy();
                }
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private AICoreActionEndStruct EndHoldPosition(string? reason)
        {
            if (!string.Equals(reason, "sniper.coverHold", StringComparison.Ordinal))
            {
                return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
            }

            CombatCommon.ValidateCommittedCover();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("sniperCoverHoldNoEnemy", true);
            }

            // Break immediately when active combat pressure returns.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldImmediateCombat", true);
            }

            // If damage pressure resumes, stop waiting in hold and re-enter combat routing.
            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldUnderFire", true);
            }

            // Priority 1: keep scanning for better shooting opportunities while waiting in cover.
            if (ShouldRescanShootingPosition(goalEnemy))
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldRescan", true);
            }

            // Break when a better shooting spot appears than the current committed hold point.
            if (HasNewShootingSpotOpportunity())
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldNewShootSpot", true);
            }

            // Priority 2: boss-under-attack only breaks when support opportunity is real
            // (shoot from current cover or bossward support cover exists).
            if (ShouldBreakForBossSupportOpportunity(goalEnemy))
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldBossUnderAttack", true);
            }

            // If an ally starts a real engagement while marksman is holding in cover, break hold
            // and re-evaluate support. Boss-under-attack path still runs first and stays prioritized.
            if (ShouldBreakForAllyEngagementSupportOpportunity())
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldAllySupport", true);
            }

            // Priority 3: when too far from boss, break hold so regroup objective can take over.
            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                CombatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("sniperCoverHoldBossObjective", true);
            }

            return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
        }

        private bool HasNewShootingSpotOpportunity()
        {
            CustomNavigationPoint? pointToShoot = CombatCommon.PointToShoot;
            if (!CombatCommon.IsCoverUsable(pointToShoot))
            {
                return false;
            }

            CustomNavigationPoint? currentCover = BotOwner.Memory?.CurCustomCoverPoint;
            if (currentCover == null)
            {
                return true;
            }

            return pointToShoot == null || pointToShoot.Id != currentCover.Id;
        }

        private bool ShouldBreakForBossSupportOpportunity(EnemyInfo goalEnemy)
        {
            if (!ShouldBreakForBossUnderAttack(goalEnemy))
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return true;
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            return CombatCommon.TryFindCoverTowardBoss(
                goalEnemy,
                bossPosition,
                BossSupportShootCoverRadius,
                requireShootLane: true,
                requireHideFromEnemy: false,
                out _);
        }

            private bool ShouldBreakForAllyEngagementSupportOpportunity()
            {
                return CombatCommon.TryGetAllyEngagementSupportDecision() != null;
            }

        private bool ShouldRescanShootingPosition(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.IsCommittedCoverLockExpired)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                if (goalEnemy.CanShoot && CombatCommon.CanShootFromCurrentCover(out _))
                {
                    return true;
                }

                return true;
            }

            return CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy);
        }

        private bool ShouldBreakForBossUnderAttack(EnemyInfo goalEnemy)
        {
            // A live personal shot remains valid support; keep taking it.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (BotOwner.Memory.HaveEnemy && sinceLastSeen > 2.5f)
            {
                return false;
            }

            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
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

        private bool ShouldBreakCommittedCoverForBossObjective(EnemyInfo goalEnemy, bool allowLockedBreak = false)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            float bossDistance = CombatCommon.GetBossNavDistance(bossPosition);
            if (bossDistance <= BossSupportShootCoverRadius)
            {
                return false;
            }

            if (!allowLockedBreak && CombatCommon.HasCommittedCover() && !CombatCommon.IsCommittedCoverLockExpired)
            {
                return false;
            }

            return true;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> Regroup()
        {
            return CombatCommon.CreateRegroupObjectiveDecision();
        }

        private void ClearAggressiveRequests()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                command == FollowerCommandType.PushEnemy)
            {
                followerData.ClearCommand("Marksman:ignorePush");
            }

            BotRequest? request = BotOwner.BotRequestController?.CurRequest;
            if (request == null)
            {
                return;
            }

            if (request.BotRequestType != BotRequestType.attackClose &&
                request.BotRequestType != BotRequestType.suppressionFire &&
                request.BotRequestType != BotRequestType.throwGrenade &&
                request.BotRequestType != BotRequestType.throwGrenadeFromPlace)
            {
                return;
            }

            request.Complete();
            if (BotOwner.BotRequestController != null)
            {
                BotOwner.BotRequestController.CurRequest = null;
            }
        }

        private bool TryGetBossUnderAttackDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            // If the marksman already has a clean personal shot, taking it is the fastest support.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            AIBossPlayerLogic? bossLogic = boss.GetBossLogic();
            if (bossLogic == null || !bossLogic.IsHitted)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            boss.PrioritizeEnemy(BotOwner, bossEnemy);
            EnemyInfo? prioritizedEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(prioritizedEnemy))
            {
                return false;
            }

            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (CombatCommon.TryFindCoverTowardBoss(
                    prioritizedEnemy,
                    bossPosition,
                    BossSupportShootCoverRadius,
                    requireShootLane: true,
                    requireHideFromEnemy: false,
                    out CustomNavigationPoint? supportCover))
            {
                CombatCommon.AssignCover(supportCover);
                BotOwner.GoToSomePointData.SetPoint(supportCover!.Position);
                BotOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.goToPointTactical,
                    "sniper.protectBossShootCover");
                return true;
            }

            decision = Regroup();
            return true;
        }
    }
}
