using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatSniper
    {
        private const float RepositionCooldownSeconds = 4f;
        private const float RegroupSameLevelTolerance = 1.75f;
        private const float MarksmanStartLowThreatAggression = 0.3f;
        private const float CloseIntentRecentSeenSeconds = 2.25f;
        private const float FireSupportSettleSeconds = 2.5f;
        private const string FireSupportHoldReason = "sniper.fireSupportHold";

        private readonly CommittedCoverPhaseState repositionPhase = new CommittedCoverPhaseState();
        private readonly CommittedCoverPhaseState supportPhase = new CommittedCoverPhaseState();
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
            repositionPhase.Reset();
            supportPhase.Reset();
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

            if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close)
            {

                // Marksman does not inherit the normal aggression slider for combat entry. Use a
                // fixed cautious baseline so it only close-searches targets we consider weak enough.
                bool isLowThreat = CombatCommon.IsEnemyLowThreat(goalEnemy, MarksmanStartLowThreatAggression);
                if (isLowThreat && CombatCommon.HasAutomaticCloseCombatWeaponAvailable())
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

            if (CombatCommon.TryActivateFollowerGrenade(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> grenadeDecision))
            {
                return grenadeDecision;
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

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                ClearRegroupCommand();
                return Regroup(isExplicitOrder: true);
            }

            // Boss-under-attack support should preempt active reposition travel/hold.
            if (repositionPhase.IsActive &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
            }

            if (TryGetActiveCommittedTravelDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> activeTravel))
            {
                return activeTravel;
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

            bool closeEnoughForSecondary = goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
            if (!closeEnoughForSecondary)
            {
                return false;
            }

            // Face-to-face contact should favor immediate fire with the current weapon.
            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot
            )
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "sniper.closeImmediateShoot");
                return true;
            }
            else if (
                !BotOwner.Memory.GoalEnemy.HaveSeen ||
                Time.time - BotOwner.Memory.GoalEnemy.PersonalLastSeenTime > 1.5f
            )
            {
                CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat();
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                if (dogFight.Value.Action == BotLogicDecision.dogFight)
                {
                    // Dogfight aiming can be unstable for marksman at close breakouts. Prefer a
                    // stable immediate shot decision here and let close-quarter policy continue next tick.
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "sniper.closeImmediateShoot");
                    return true;
                }

                decision = dogFight.Value;
                return true;
            }


            if (CombatCommon.IsEnemyLowThreat(goalEnemy, MarksmanStartLowThreatAggression) &&
                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance() &&
                CombatCommon.HasAutomaticCloseCombatWeaponAvailable())
            {
                decision = CombatCommon.EnemySearch("sniper.closeSearch");
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
            if (goalEnemy == null || goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (CombatCommon.IsCurrentWeaponAutomatic())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-asserts marksman close-quarter weapon policy on decision handoff so generic action
        /// transitions do not flip the bot back to the sniper rifle right as close combat starts.
        /// </summary>
        private void ApplyMarksmanWeaponPolicy(
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (goalEnemy == null)
            {
                return;
            }

            if (ShouldUseCloseIntentSecondary(decision))
            {
                TryApplyCloseIntentSecondary(goalEnemy, true);
                return;
            }

            if (ShouldSwitchBackToPrimaryForSniperDecision(goalEnemy, decision))
            {
                TrySwitchToPrimaryForSniperDecision();
            }
        }

        private static bool ShouldUseCloseIntentSecondary(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null)
            {
                return false;
            }

            return decision.Reason.StartsWith("sniper.startClose", StringComparison.Ordinal) ||
                   decision.Reason.StartsWith("sniper.closeSearch", StringComparison.Ordinal) ||
                   decision.Reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal);
        }

        private bool CanUseCloseIntentSecondary(EnemyInfo goalEnemy, bool distanceIgnore = false)
        {
            if (goalEnemy == null || (!distanceIgnore && goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance()))
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalSeenTime <= CloseIntentRecentSeenSeconds;
        }

        private void TryApplyCloseIntentSecondary(EnemyInfo goalEnemy, bool distanceIgnore = false)
        {
            if (!CanUseCloseIntentSecondary(goalEnemy, distanceIgnore))
            {
                return;
            }

            CombatCommon.TrySwitchToAutomaticSecondaryForCloseCombat();
        }

        private void TrySwitchToPrimaryForSniperDecision()
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                selector.TryChangeToMain();
            }
        }

        private bool ShouldSwitchBackToPrimaryForSniperDecision(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            var selector = BotOwner?.WeaponManager?.Selector;
            if (selector == null || selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
            {
                return false;
            }

            if (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (Time.time - goalEnemy.PersonalSeenTime <= CloseIntentRecentSeenSeconds)
            {
                return false;
            }

            if (new List<string>
            {
                "sniper.reposition",
                "sniper.FireSupport",
                "sniper.protectBossShootCover",
                "sniper.coverHold",
                FireSupportHoldReason
            }.Contains(decision.Reason))
            {
                return true;
            }

            return CombatCommon.IsCommittedCoverRetreatingFromEnemy(goalEnemy);
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

            // The committed cover already has the mutated reason stored.
            // Just use the stored values directly.
            decision = CombatCommon.CreateMoveToCommittedCoverDecision(coverReason);
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
            if (supportPhase.IsActive)
            {
                return false;
            }

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

            supportPhase.BeginTravel();

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
                repositionPhase.Clear();
                supportPhase.Clear();
                return false;
            }

            if (CombatCommon.IsBotInCommittedCover())
            {
                if (repositionPhase.PromoteToHoldOnArrival())
                {
                    repositionPhase.StartCooldown(RepositionCooldownSeconds);
                }

                bool supportArrived = supportPhase.PromoteToHoldOnArrival();

                if (supportPhase.IsHolding)
                {
                    if (HasImmediateShotFromCurrentCover(goalEnemy))
                    {
                        CombatCommon.ExtendCommittedCover();
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                            BotLogicDecision.shootFromCover,
                            "sniper.committedFire");
                        return true;
                    }

                    if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    if (supportArrived)
                    {
                        CombatCommon.HoldFor(FireSupportSettleSeconds);
                    }

                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        FireSupportHoldReason);
                    return true;
                }

                if (HasImmediateShotFromCurrentCover(goalEnemy))
                {
                    CombatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromCover,
                        "sniper.committedFire");
                    return true;
                }

                if (goalEnemy.IsVisible)
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                // Reposition cover is sticky: do not re-evaluate to new covers just because
                // enemy memory or boss position moved while the follower is settled.
                if (repositionPhase.IsHolding)
                {
                    if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                    {
                        ClearCommittedCoverAndRepositionState();
                        return false;
                    }

                    CombatCommon.HoldCoverForMaxDuration();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        "sniper.coverHold");
                    return true;
                }

                // Unseen break-outs should stay marksman-safe: allow relocalization/support/regroup
                // exits, but do not reuse default advance-pressure behavior for sniper tactic.
                if (CombatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
                {
                    ClearCommittedCoverAndRepositionState();
                    return false;
                }

                CombatCommon.HoldCoverForMaxDuration();
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
            if (IsRepositionCooldownActive())
            {
                return false;
            }

            if (!CombatCommon.TryCommitFiringPositionCover(
                    goalEnemy,
                    "sniper.reposition",
                    out string coverReason,
                    preferPointToShoot: true,
                    preferInbetween: false))
            {
                return false;
            }

            repositionPhase.BeginTravel();

            // The committed cover already has the action and mutated reason stored.
            // Just use the stored values instead of recomputing them.
            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            // Explicit regroup commands should interrupt any ongoing action immediately.
            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperExplicitRegroup", true);
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                return EndHoldPosition(currentDecision.Reason);
            }

            if (currentDecision.Action == BotLogicDecision.runToCover &&
                IsMarksmanCommittedTravelReason(currentDecision.Reason))
            {
                return EndMarksmanCommittedRunToCover(currentDecision.Reason);
            }
            if (currentDecision.Reason != null &&
                currentDecision.Reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal))
            {
                if (!CombatCommon.HasActiveCombatEnemy())
                {
                    return new AICoreActionEndStruct("sniperCloseAutoNoEnemy", true);
                }
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private bool TryGetActiveCommittedTravelDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            bool activeTravel =
                (repositionPhase.IsPendingArrival || supportPhase.IsPendingArrival) &&
                CombatCommon.HasCommittedCover() &&
                !CombatCommon.IsBotInCommittedCover();
            if (!activeTravel)
            {
                return false;
            }

            decision = CombatCommon.CreateCommittedCoverMoveDecision();
            if (ShouldYieldCommittedTravelForImmediateCombat(goalEnemy, decision.Action))
            {
                decision = default;
                return false;
            }

            CombatCommon.AssignCommittedCover();
            return true;
        }

        private bool ShouldYieldCommittedTravelForImmediateCombat(
            EnemyInfo goalEnemy,
            BotLogicDecision moveAction)
        {
            if (moveAction == BotLogicDecision.runToCover)
            {
                return CombatCommon.ShouldBreakRunToCoverForImmediateFire();
            }

            if (moveAction == BotLogicDecision.attackMoving ||
                moveAction == BotLogicDecision.attackMovingWithSuppress ||
                moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                return CombatCommon.ShouldBreakAdvanceForImmediateFire();
            }

            return goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose;
        }

        private AICoreActionEndStruct EndMarksmanCommittedRunToCover(string? reason)
        {
            if (CombatCommon.ShouldBreakRunToCoverForImmediateFire())
            {
                return new AICoreActionEndStruct("stableImmediateFire", true);
            }

            if (BotOwner.Memory.IsInCover)
            {
                CombatCommon.HoldCoverForMaxDuration();
                return new AICoreActionEndStruct("alreadyInCover", true);
            }

            if (CombatCommon.IsBotInCommittedCover())
            {
                CombatCommon.HoldCoverForMaxDuration();
                return new AICoreActionEndStruct("arrivedCommittedCover", true);
            }

            if (BotOwner.GoToSomePointData.IsCome())
            {
                CombatCommon.HoldCoverForMaxDuration();
                return new AICoreActionEndStruct("arrivedCoverPoint", true);
            }

            if (CombatCommon.IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            return default;
        }

        private AICoreActionEndStruct EndHoldPosition(string? reason)
        {
            if (string.Equals(reason, FireSupportHoldReason, StringComparison.Ordinal))
            {
                return EndFireSupportHoldPosition();
            }

            if (!string.IsNullOrEmpty(reason) && reason.Contains("regroupNotNeeded"))
            {
                if (HasExplicitRegroupOrder())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEndStruct("sniperRegroupHoldExplicitRegroup", true);
                }

                if (ShouldRegroupForBossDistance())
                {
                    ClearCommittedCoverAndRepositionState();
                    return new AICoreActionEndStruct("sniperRegroupNowNeeded", true);
                }

                return CombatCommon.EndBaseHoldPosition(reason);
            }

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
            if (goalEnemy.IsVisible)
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldVisibleEnemy", true);
            }

            // If damage pressure resumes, stop waiting in hold and re-enter combat routing.
            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldUnderFire", true);
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldExplicitRegroup", true);
            }

            // Priority 1: keep scanning for better shooting opportunities while waiting in cover.
            if (ShouldRescanShootingPosition(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldRescan", true);
            }

            // Break when a better shooting spot appears than the current committed hold point.
            if (HasNewShootingSpotOpportunity())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldNewShootSpot", true);
            }

            // Priority 2: boss-under-attack only breaks when support opportunity is real
            // (shoot from current cover or bossward support cover exists).
            if (ShouldBreakForBossSupportOpportunity(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldBossUnderAttack", true);
            }

            // If an ally starts a real engagement while marksman is holding in cover, break hold
            // and re-evaluate support. Boss-under-attack path still runs first and stays prioritized.
            if (ShouldBreakForAllyEngagementSupportOpportunity())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldAllySupport", true);
            }

            // Priority 3: when too far from boss, break hold so regroup objective can take over.
            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("sniperCoverHoldBossObjective", true);
            }

            // Note: baseHoldPosition end-timeout uses EndBaseHoldPosition which respects EFT-level hold gates.
            // For tactical control, consider calling CombatCommon.HoldCoverForMaxDuration() during hold entry
            // to apply marksman-aware hold durations (10-18s based on aggression).
            return CombatCommon.EndBaseHoldPosition(reason ?? string.Empty);
        }

        private AICoreActionEndStruct EndFireSupportHoldPosition()
        {
            CombatCommon.ValidateCommittedCover();

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                supportPhase.Clear();
                return new AICoreActionEndStruct("fireSupportHoldNoEnemy", true);
            }

            if (BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldUnderFire", true);
            }

            if (HasExplicitRegroupOrder())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldExplicitRegroup", true);
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return new AICoreActionEndStruct("fireSupportHoldShotReady", true);
            }

            if (CombatCommon.TryRaiseForStandingCoverShot(out _))
            {
                return new AICoreActionEndStruct("fireSupportHoldStandingShotReady", true);
            }

            if (ShouldBreakForBossSupportOpportunity(goalEnemy))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldBossUnderAttack", true);
            }

            if (ShouldBreakForAllyEngagementSupportOpportunity())
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldAllySupport", true);
            }

            if (ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowLockedBreak: true))
            {
                ClearCommittedCoverAndRepositionState();
                return new AICoreActionEndStruct("fireSupportHoldBossObjective", true);
            }

            AICoreActionEndStruct baseEnd = CombatCommon.EndBaseHoldPosition(FireSupportHoldReason);
            if (baseEnd.Value)
            {
                ClearCommittedCoverAndRepositionState();
            }

            return baseEnd;
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

        private static bool IsMarksmanCommittedTravelReason(string? reason)
        {
            if (reason == null)
            {
                return false;
            }

            return reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.protectBossShootCover", StringComparison.Ordinal);
        }

        private bool HasImmediateShotFromCurrentCover(EnemyInfo goalEnemy)
        {
            if (!goalEnemy.IsVisible)
            {
                return false;
            }

            if (CombatCommon.CanShootFromCurrentCover(out _))
            {
                return true;
            }

            return CombatCommon.TryRaiseForStandingCoverShot(out _);
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
                CombatDistanceConfiguration.Instance.GetBossSupportShootCoverRadius(),
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
            if (repositionPhase.IsHolding)
            {
                return false;
            }

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
            if (HasImmediateShotFromCurrentCover(goalEnemy))
            {
                return false;
            }

            if (!ShouldRegroupForBossDistance())
            {
                return false;
            }


            // For committed cover specifically, give the follower time to actually use the cover before
            // escort pressure can pull it out again.
            if (CombatCommon.HasCommittedCover())
            {
                if (!CombatCommon.IsBotInCommittedCover())
                {
                    return allowLockedBreak &&
                           CombatCommon.IsCommittedCoverLockExpired;
                }

                if (!CombatCommon.IsCommittedCoverLockExpired)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsRepositionCooldownActive()
        {
            return repositionPhase.IsCooldownActive;
        }

        private void ClearCommittedCoverAndRepositionState()
        {
            CombatCommon.ClearCommittedCover();
            repositionPhase.Clear();
            supportPhase.Clear();
        }

        /// <summary>
        /// Computes whether the follower should switch to the regroup objective based on live boss
        /// nav distance. Regroup itself decides whether that becomes forward, backward, or lateral movement.
        /// </summary>
        private bool ShouldRegroupForBossDistance()
        {
            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float navDistance = CombatCommon.GetBossNavDistance(bossPosition);
            float directDistance = Vector3.Distance(BotOwner.Position, bossPosition);
            float followerBossDistance = GetSafeRegroupDistance(navDistance, directDistance);
            if (followerBossDistance <= CombatDistanceConfiguration.Instance.GetRegroupNeededDistanceMarksman())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Guards against NaN or infinity vectors from stale game data.
        /// </summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> Regroup(bool isExplicitOrder = false)
        {
            // Explicit player regroup commands must always activate regroup, bypassing distance checks.
            // Autonomous regroup decisions only activate when far enough from the boss.
            if (!isExplicitOrder && !ShouldRegroupForBossDistance())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        "sniper.regroupNotNeeded");
            }

            return CombatCommon.CreateRegroupObjectiveDecision();
        }

        private bool HasExplicitRegroupOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            return followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        private bool ClearRegroupCommand()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            if (followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                command == FollowerCommandType.RegroupNearBoss)
            {
                followerData.ClearCommand("Marksman:ignoreRegroup");
                return true;
            }

            return false;
        }

        private static float GetSafeRegroupDistance(float navDistance, float directDistance)
        {
            bool navValid = !float.IsNaN(navDistance) && !float.IsInfinity(navDistance) && navDistance > 0.1f;
            if (!navValid)
            {
                return directDistance;
            }

            // Use the conservative larger value so bad/short nav samples cannot mark far bots as "already regrouped".
            return Mathf.Max(navDistance, directDistance);
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
                    CombatDistanceConfiguration.Instance.GetBossSupportShootCoverRadius(),
                    requireShootLane: true,
                    requireHideFromEnemy: false,
                    out CustomNavigationPoint? supportCover))
            {
                if (CombatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, supportCover, "sniper.protectBossShootCover"))
                {
                    supportPhase.BeginTravel();
                    decision = CombatCommon.CreateCommittedCoverMoveDecision();
                    return true;
                }
            }

            decision = Regroup();
            return true;
        }
    }
}
