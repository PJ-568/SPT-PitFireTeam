using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatDefault
    {
        private const float RecentSeenPressureSeconds = 2f;
        private const string BossHoldReason = "bossHold";
        private const string BossHoldOpenReason = "bossHoldOpen";
        private const string RecoveryCoverHoldReason = "recoveryCoverHold";
        private const float ShootCoverSettleSeconds = 2.5f;
        private const float CommittedHolderEngagementBreakSettleSeconds = 1f;
        private const string ShootCoverHoldReason = "shootCoverHold";
        private const float IntentSettleSeconds = 2.5f;
        private const float IntentScanIntervalSeconds = 0.75f;
        private const float SupportIntentMaxHoldSeconds = 8f;
        private const float ProtectIntentMaxHoldSeconds = 8f;
        private const float IntentRetryCooldownSeconds = 3f;
        private const float SupportBreakRetryCooldownSeconds = 1f;
        private const float VisibleAlignedFireMaxDistance = 35f;
        private const float VisibleAlignedFireMaxAngle = 12f;
        private const float AutoSuppressRetryCooldownSeconds = 3.5f;
        private const float AllyPushSupportMaxStraightDistance = 45f;
        private const float AllyPushSupportMaxNavDistance = 65f;

        private enum CoverIntentKind
        {
            None,
            Support,
            ProtectBoss,
        }

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private readonly FollowerCombatPush combatPush;
        private readonly CommittedCoverPhaseState shootCoverSettlePhase = new CommittedCoverPhaseState();
        private readonly CommittedCoverPhaseState coverIntentPhase = new CommittedCoverPhaseState();

        private CoverIntentKind activeCoverIntent;
        private float supportIntentRetryUntil;
        private float protectIntentRetryUntil;
        private float autoSuppressRetryUntil;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? preparedAllySupportDecision;

        public FollowerCombatDefault(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
            this.combatPush = new FollowerCombatPush(botOwner, combatCommon);
        }

        /// <summary>
        /// Clears follower-local combat state when the combat layer stops or restarts.
        /// </summary>
        public void Reset()
        {
            combatCommon.ResetCommittedCover();
            combatCommon.ClearCommittedPosition();
            combatPush.Reset();
            combatCommon.ClearCommittedMovement();
            shootCoverSettlePhase.Reset();
            ClearCoverIntent();
            autoSuppressRetryUntil = 0f;
            preparedAllySupportDecision = null;
        }

        /// <summary>
        /// Updates local commitment state after the BigBrain decision changes.
        /// </summary>
        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            combatCommon.HandleSharedDecisionChanged(nextDecision);
            combatCommon.HandleCommittedCoverDecisionChanged(nextDecision);
            combatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
            UpdateShootCoverSettleState(nextDecision);
            combatPush.HandleDecisionChanged(nextDecision);

            if (combatCommon.ShouldCommitMovementDecision(nextDecision, combatPush.IsPushCommittedDecision(nextDecision)))
            {
                combatCommon.CommitMovement(nextDecision);
            }
            else if (!combatCommon.IsSameCommittedMovement(nextDecision))
            {
                combatCommon.ClearCommittedMovement();
            }

            if (activeCoverIntent != CoverIntentKind.None && !IsDecisionCompatibleWithCoverIntent(nextDecision))
            {
                ClearCoverIntent();
            }

        }

        /// <summary>
        /// Seeds the one-shot combat opener used immediately after combat activation.
        /// </summary>
        public void PrepareStartDecision()
        {
            combatCommon.PrepareStartDecision(combatCommon.GetAggression01());
        }

        /// <summary>
        /// Main default-combat router. Keep urgent fighting and medical work explicit, then honor
        /// destination/hold commitments before considering support, protection, or fresh pressure.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            combatCommon.RefreshShootCover();

            // Router order is deliberate. Immediate survival/contact can break plans, but normal
            // tactical ideas must pass through commitments so a chosen destination gets reached,
            // held briefly, and then re-evaluated instead of churned every tick.
            combatCommon.ValidateCommittedCover();

            if (TryGetImmediateFightDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> fightDecision))
            {
                return fightDecision;
            }

            if (combatCommon.TryGetCommittedGrenadeDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedGrenade))
            {
                return committedGrenade;
            }

            if (combatCommon.HasInitialDecision)
            {
                return combatCommon.ConsumeInitialDecision();
            }

            if (TryGetHealDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> healDecision))
            {
                return healDecision;
            }

            // When exposed, under fire, or hurt, switch to recovery behavior before honoring
            // normal commitments or support routing.
            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recoverDecision))
            {
                return recoverDecision;
            }

            if (TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> bossDistanceDecision))
            {
                return bossDistanceDecision;
            }

            // Arrival holders block re-planning after the bot reaches a cover/position. Their end
            // conditions still break for direct contact, under-fire pressure, or settled support needs.
            if (combatCommon.HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPosition))
            {
                return committedPosition;
            }

            // Once a push has been chosen, keep returning that same push action until a hard interrupt
            // or the action end logic says the push phase is over.
            if (combatPush.TryGetCommittedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush))
            {
                return committedPush;
            }

            // Non-push movement commitments prevent mid-route replanning. Arrival and urgent
            // fight/survival interrupts clear the latch so completed movement can hand off to hold/shoot.
            if (combatCommon.TryGetCommittedMovementDecision(
                    goalEnemy,
                    HasExplicitRegroupOrder(),
                    HasActivePushOrder(),
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> committedMovement))
            {
                return committedMovement;
            }

            // If a cover point was already committed earlier, keep following through on it before
            // inventing a new plan.
            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedCoverDecision))
            {
                return committedCoverDecision;
            }

            if (TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> bossDistanceAfterCommitment))
            {
                return bossDistanceAfterCommitment;
            }

            if (TryGetOrderedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> orderedPushDecision))
            {
                return orderedPushDecision;
            }

            // Very low aggression followers should treat bossward regroup as their primary objective
            // unless the enemy is already close enough that local engagement is unavoidable.
            if (TryGetLowAggressionRegroupDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> lowAggressionRegroup))
            {
                return lowAggressionRegroup;
            }

            // If the boss was just hit and this follower is not already committed to a stronger
            // personal fight, bias toward protecting the boss and adopting the boss's attacker.
            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> protectBossDecision))
            {
                return protectBossDecision;
            }

            // While passively holding in cover, scan for a credible ally support opportunity before
            // defaulting into generic push/search or idle holding behavior.
            if (TryGetAllySupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> allySupportDecision))
            {
                return allySupportDecision;
            }

            if (TryGetAutonomousSuppressDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> autoSuppressDecision))
            {
                return autoSuppressDecision;
            }

            if (combatCommon.TryActivateFollowerGrenade(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> grenadeDecision))
            {
                return grenadeDecision;
            }

            // Resolve visible-but-not-immediate contact: take a firing cover or hand off to pressure.
            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision))
            {
                return visibleDecision;
            }

            // If pushing is justified after the safety checks above, ask the engage helper to choose
            // how to pressure the enemy or search forward.
            if (TryGetAdvanceDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> advanceDecision))
            {
                return advanceDecision;
            }

            if (botOwner.Memory.IsInCover)
            {
                combatCommon.HoldCoverForMaxDuration();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
            }

            if (goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "fallbackShoot");
            }

            if (ShouldBlockPassiveBossHold(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "postHitNoPassiveHold");
            }

            combatCommon.HoldCoverForMaxDuration();
            string holdReason = botOwner.Memory.IsInCover ? BossHoldReason : BossHoldOpenReason;
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason);
        }

        private bool TryGetImmediateFightDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = combatCommon.TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                combatCommon.ClearInitialDecision();
                decision = dogFightDecision.Value;
                return true;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!ShouldDeferExposedImmediateFireForRecovery(goalEnemy))
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = combatCommon.InFightLogic();
                if (inFightDecision != null)
                {
                    combatCommon.ClearInitialDecision();
                    decision = inFightDecision.Value;
                    return true;
                }
            }

            return false;
        }

        private bool ShouldDeferExposedImmediateFireForRecovery(EnemyInfo? goalEnemy)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool damagePressure =
                FollowerCombatCommon.WasHitRecently(botOwner, 1f) ||
                FollowerAwareness.WasRecentlyHit(botOwner) ||
                combatCommon.IsFollowerCriticallyWounded() ||
                combatCommon.HasUrgentHealWork();
            if (!damagePressure)
            {
                return false;
            }

            // At point blank, breaking aim to route recovery can be worse than fighting through.
            return goalEnemy == null ||
                   !goalEnemy.IsVisible ||
                   !goalEnemy.CanShoot ||
                   Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose;
        }

        private bool TryGetHealDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = combatCommon.TryGetNeedHealDecision();
            if (healDecision == null)
            {
                return false;
            }

            combatCommon.ClearInitialDecision();
            decision = healDecision.Value;
            return true;
        }

        private bool TryGetBossDistanceRegroupDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (ShouldDeferBossDistanceRegroupForCommitment())
            {
                return false;
            }

            return TryGetBossCombatObjectiveDecision(out decision);
        }

        private bool ShouldDeferBossDistanceRegroupForCommitment()
        {
            if (HasActivePushOrder() || combatPush.HasCommittedPush())
            {
                return true;
            }

            if (activeCoverIntent != CoverIntentKind.None)
            {
                return true;
            }

            if (combatCommon.HasCommittedMovement())
            {
                return true;
            }

            if (combatCommon.HasCommittedCover() && !combatCommon.IsBotInCommittedCover())
            {
                return true;
            }

            return FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(combatCommon.CommittedPositionReason) ||
                   FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(combatCommon.CommittedCoverReason);
        }

        private bool TryGetAllySupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (preparedAllySupportDecision.HasValue)
            {
                decision = preparedAllySupportDecision.Value;
                preparedAllySupportDecision = null;
                return true;
            }

            return TryBuildAndCommitAllySupportDecision(out decision, false);
        }

        private bool TryBuildAndCommitAllySupportDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool retryOnFailure)
        {
            decision = default;
            if (IsCoverIntentRetryActive(CoverIntentKind.Support))
            {
                return false;
            }

            if (TryBuildAndCommitPushSupportDecision(out decision))
            {
                CommitCoverIntent(CoverIntentKind.Support);
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? allySupportDecision =
                combatCommon.TryGetAllyEngagementSupportDecision();
            if (allySupportDecision == null)
            {
                if (retryOnFailure)
                {
                    supportIntentRetryUntil = Mathf.Max(supportIntentRetryUntil, Time.time + SupportBreakRetryCooldownSeconds);
                }

                return false;
            }

            CommitCoverIntent(CoverIntentKind.Support);
            decision = allySupportDecision.Value;
            return true;
        }

        private bool TryBuildAndCommitPushSupportDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // Rifleman helper path: consume another follower's active autonomous push event and
            // convert it into support. This branch never emits a new push event and never chooses
            // direct go-to-enemy/run-to-enemy as its support objective.
            if (!combatCommon.TryGetNearbyActivePushEvent(
                    AllyPushSupportMaxStraightDistance,
                    AllyPushSupportMaxNavDistance,
                    out CombatEvents.PushEvent pushEvent))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null ||
                !string.Equals(goalEnemy.ProfileId, pushEvent.EnemyProfileId, StringComparison.Ordinal))
            {
                combatCommon.TryPromoteTrackedEnemyAsGoal(pushEvent.EnemyProfileId);
                goalEnemy = botOwner.Memory.GoalEnemy;
                if (!combatCommon.HasActiveCombatEnemy(goalEnemy) ||
                    !string.Equals(goalEnemy.ProfileId, pushEvent.EnemyProfileId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (combatCommon.CanShootFromCurrentCover(out _))
            {
                // Best support is doing nothing fancy: stay in cover and shoot if this position
                // already contributes to the pusher's fight.
                combatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "allyPushSupport.currentCover");
                return true;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? immediate =
                combatCommon.TryGetImmediateShootDecision("allyPushSupport.immediateShoot");
            if (immediate != null)
            {
                decision = immediate.Value;
                return true;
            }

            if (pushEvent.IsSearchPush &&
                (botOwner.Position - pushEvent.Owner.Position).sqrMagnitude <= 20f * 20f &&
                combatCommon.TryCreateTeamSearchSupportDecision(
                    pushEvent,
                    goalEnemy,
                    "allyPushSupport.teamSearch",
                    out decision))
            {
                // Search pushes use a small flank/backstop point near the pusher, not the enemy.
                return true;
            }

            if (combatCommon.TryCommitPushSupportCover(
                    goalEnemy,
                    pushEvent.Owner.Position,
                    pushEvent.EnemyPosition,
                    pushEvent.Destination,
                    "allyPushSupport.cover",
                    out string coverReason))
            {
                // Normal support push: take a cover that can watch the enemy or pusher destination.
                decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            Vector3 supportAnchor = FollowerCombatCommon.IsFinite(pushEvent.EnemyPosition)
                ? pushEvent.EnemyPosition
                : pushEvent.Destination;
            // Final fallback is still a support firing point, not a direct assault push.
            return combatCommon.TryCreateSupportFiringPositionDecision(
                goalEnemy,
                supportAnchor,
                "allyPushSupport.position",
                out decision,
                preferBackline: false,
                enforceMarksmanPositionPolicy: false);
        }

        private bool TryPrepareAllySupportBreak(
            string reason,
            bool clearCommittedPosition,
            out AICoreActionEndStruct end)
        {
            end = FollowerCombatCommon.Continue();
            if (!TryBuildAndCommitAllySupportDecision(
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> supportDecision,
                    true))
            {
                return false;
            }

            if (clearCommittedPosition)
            {
                combatCommon.ClearCommittedPosition();
            }

            preparedAllySupportDecision = supportDecision;
            end = new AICoreActionEndStruct(reason, true);
            return true;
        }

        /// <summary>
        /// Keeps decision end conditions local to the simplified combat state machine.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            // Fight actions should not be interrupted by regroup/support routing, but they still
            // need their own end checks so stale visible-fire decisions release when LOS is gone.
            if (combatCommon.IsInFight(currentDecision.Action))
            {
                return combatCommon.ShallEndCurrentDecision(currentDecision);
            }

            // Explicit regroup commands should interrupt any ongoing action immediately.
            // expect if bot has clearly visible enemy and is enganged
            if (HasExplicitRegroupOrder())
            {
                combatPush.ClearCommittedPush("explicitRegroup");
                combatCommon.ClearCommittedCover();

                combatCommon.ClearCommittedPosition();

                ClearCoverIntent();
                return new AICoreActionEndStruct("defaultExplicitRegroup", true);
            }

            if (FollowerCombatPush.IsPushReason(currentDecision.Reason) ||
                FollowerCombatPush.IsStartWeakEnemyPushReason(currentDecision.Reason))
            {
                return combatPush.EndCommittedPush(currentDecision);
            }

            switch (currentDecision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return EndHoldPosition(currentDecision.Reason);
                case BotLogicDecision.runToCover:
                case BotLogicDecision.attackMoving:
                case BotLogicDecision.attackMovingWithSuppress:
                    return EndCoverMoveOrAttackMoving(currentDecision);
                case BotLogicDecision.shootFromCover:
                    return EndShootFromCover(currentDecision.Reason);
                case BotLogicDecision.suppressFire:
                    return combatCommon.EndSuppressFire(currentDecision.Reason);
                default:
                    return combatCommon.ShallEndCurrentDecision(currentDecision);
            }
        }

        /// <summary>
        /// Prioritizes immediate action on visible enemies: fire now, take cover for fire, or pressure forward.
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

            // If the previous cover move just armed an arrival hold, do not let visible-cover
            // acquisition immediately reselect the same already-reached cover. The holder itself
            // decides whether contact is urgent enough to break.
            if (combatCommon.HasCommittedPosition(out decision))
            {
                return true;
            }

            // If the enemy is visible and the bot can already fire, resolve the fight immediately
            // instead of falling through to slower blind-search or recovery logic.
            if (goalEnemy.CanShoot)
            {
                // Very-close visible enemies skip cover logic and collapse into point-blank combat.
                if (Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "visibleDogFight");
                    return true;
                }

                // A fresh immediate-fire window can snap to exposed/flanking enemies, but a bot
                // already in cover must first prove a crouch or standing cover lane exists.
                if (combatCommon.ShouldShootImmediately())
                {
                    if (!botOwner.Memory.IsInCover)
                    {
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleImmediateShoot");
                        return true;
                    }

                    if (TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "visibleImmediateCoverFire", out decision))
                    {
                        return true;
                    }

                    // Covered but no verified crouch/standing lane: do not force shootFromPlace into
                    // the cover wall. Let the normal cover/reposition branches below pick the next move.
                }

                // If the bot is already protected by cover and that cover still supports a real shot,
                // use it immediately instead of re-evaluating movement.
                if (botOwner.Memory.IsInCover &&
                    TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "coverVisibleFire", out decision))
                {
                    return true;
                }

                // If the bot is exposed and current context says "take the fight from cover",
                // commit one shooting cover before trading from the open.
                if (!botOwner.Memory.IsInCover &&
                    combatCommon.ShouldTakeVisibleCover(goalEnemy) &&
                    TryCommitCombatCover(goalEnemy, requireShootLane: true, out string coverReason))
                {
                    decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                    return true;
                }

                if (botOwner.Memory.IsInCover)
                {
                    return false;
                }

                // Otherwise an exposed bot with a visible, shootable enemy stands and fires.
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleShoot");
                return true;
            }

            // The enemy is seen but cannot be shot from the current spot, so first try to take one
            // committed firing cover that should open the lane.
            // The enemy is visible but not yet shootable from here, so prefer moving to a cover
            // that creates a lane before considering forward pressure.
            if (!botOwner.Memory.IsInCover && ShouldForceVisibleAlignedFire(goalEnemy))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleAlignedShoot");
                return true;
            }

            if (!botOwner.Memory.IsInCover &&
                TryCommitCombatCover(goalEnemy, requireShootLane: true, out string visibleCoverReason))
            {
                decision = combatCommon.CreateMoveToCommittedCoverDecision(visibleCoverReason);
                return true;
            }

            // If no immediate firing position exists, aggressive followers are allowed to close distance
            // while the enemy is still visible.
            if (combatCommon.ShouldAdvance(goalEnemy) &&
                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetVisiblePushDistance())
            {
                // Once local logic decides a visible enemy should be pushed, hand off to the old-plugin
                // engage helper so it can choose rush/walk/approach behavior from its richer threat checks.
                bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
                decision = combatPush.EngageEnemy(false, enemyLowThreat);
                return true;
            }

            // Visible contact still matters, but if there is no immediate fire/cover answer let the
            // engage helper decide whether this should become a push, search, or brief pressure hold.
            return false;
        }

        private bool TryGetCoveredVisibleFireOrAdvanceDecision(
            EnemyInfo goalEnemy,
            string coverFireReason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (combatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                combatCommon.ExtendCommittedCover();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, coverFireReason);
                return true;
            }

            if (!combatCommon.ShouldAdvance(goalEnemy))
            {
                return false;
            }

            combatCommon.ClearCommittedCover();
            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(false, enemyLowThreat);
            return true;
        }

        /// <summary>
        /// Keeps the bot tied to a chosen cover point long enough to actually arrive, settle, and use it.
        /// </summary>
        private bool TryGetCommittedCoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // No committed cover means this branch has nothing to manage, so let the rest of the
            // combat tree choose a fresh action.
            if (!combatCommon.HasCommittedCover())
            {
                shootCoverSettlePhase.Clear();
                ClearCoverIntent();
                return false;
            }

            // If the bot has actually reached the committed point, this method owns the next step:
            // either fight from that cover, briefly settle, or hold it.
            if (combatCommon.IsBotInCommittedCover())
            {
                shootCoverSettlePhase.PromoteToHoldOnArrival();
                UpdateCoverIntentArrival();

                if (HasActivePushOrder())
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (goalEnemy.IsVisible && combatCommon.ShouldShootImmediately())
                {
                    if (TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "committedImmediateCoverFire", out decision))
                    {
                        return true;
                    }

                    // Reached committed cover but no valid crouch/standing lane exists. Continue into
                    // the visible-cover handling below instead of forcing shootFromPlace into geometry.
                }

                // Once the bot has actually reached committed cover, shooting from that cover always wins.
                if (goalEnemy.IsVisible && TryGetCoveredVisibleFireOrAdvanceDecision(goalEnemy, "committedFire", out decision))
                {
                    return true;
                }

                // Visible enemies should not get trapped in passive cover hold just because the current
                // cover-fire validation failed. Break out into active pressure instead.
                if (goalEnemy.IsVisible)
                {
                    return false;
                }

                if (shootCoverSettlePhase.IsHolding)
                {
                    combatCommon.HoldFor(ShootCoverSettleSeconds);
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, ShootCoverHoldReason);
                    return true;
                }

                // Once the initial cover settle is over, boss-distance regroup should beat passive
                // local cover churn unless there is already a real visible shot to solve here.
                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (TryGetCoverIntentRepositionDecision(goalEnemy, out decision))
                {
                    return true;
                }

                if (ShouldReleaseCoverIntentForOpportunity(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (ShouldExpireCoverIntent())
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ApplyCoverIntentRetryCooldown();
                    ClearCoverIntent();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.ShouldAdvance(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    shootCoverSettlePhase.Clear();
                    combatCommon.ClearCommittedCover();
                    ClearCoverIntent();
                    return false;
                }

                // Otherwise remain in the committed cover and wait for the next actionable event.
                combatCommon.HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
                return true;
            }

            // The bot has not reached its committed cover yet, so keep feeding the same cover back into EFT
            // until arrival or invalidation.
            combatCommon.AssignCommittedCover();
            decision = combatCommon.CreateCommittedCoverMoveDecision();
            return true;
        }

        /// <summary>
        /// Forces cover-seeking when recent damage, suppression, or poor enemy certainty makes standing unsafe.
        /// </summary>
        private bool TryGetRecoverDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // Recovery only matters while exposed. If the bot is already in cover, let the normal
            // visible/committed-cover logic decide whether to shoot or hold.
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool needCover =
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 1f) ||
                FollowerAwareness.WasRecentlyHit(botOwner) ||
                combatCommon.IsFollowerCriticallyWounded() ||
                combatCommon.IsEnemyActivelyThreateningMe(goalEnemy, 18f, 0.75f);

            // If the enemy was only just lost and the bot does not have a trustworthy personal location,
            // bias toward safety instead of standing in the open waiting for certainty.
            if (!needCover &&
                !goalEnemy.IsVisible &&
                Time.time - goalEnemy.PersonalLastSeenTime < RecentSeenPressureSeconds &&
                !combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                needCover = true;
            }

            if (!needCover)
            {
                return false;
            }

            if (ShouldFightThroughPointBlankRecovery(goalEnemy))
            {
                if (TryCreatePointBlankRecoveryPressure(goalEnemy, out decision))
                {
                    return true;
                }

                return false;
            }

            // Arrival holds are allowed to stabilize after reaching cover unless the holder's own
            // break rules clear it for real pressure. Without this, recovery can re-emit runToCover
            // on the same arrived point for several frames.
            if (combatCommon.HasCommittedPosition(out decision))
            {
                return true;
            }

            if (combatCommon.HasCommittedCover() && combatCommon.IsBotInCommittedCover())
            {
                combatCommon.ArmCommittedArrivalHold(combatCommon.CommittedCoverReason ?? "retreatSafeCover");
                if (combatCommon.HasCommittedPosition(out decision))
                {
                    return true;
                }

                combatCommon.HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    RecoveryCoverHoldReason);
                return true;
            }

            // First try to convert recovery into an actual committed cover move rather than a one-frame panic.
            if (TryCommitCombatCover(goalEnemy, requireShootLane: goalEnemy.IsVisible && goalEnemy.CanShoot, out string coverReason))
            {
                decision = combatCommon.CreateMoveToCommittedCoverDecision(coverReason);
                return true;
            }

            // If no cover exists but the enemy is still actively visible and dangerous, suppress from place
            // instead of idling.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressFire, "underFireNoCover");
                return true;
            }

            return false;
        }

        private bool ShouldFightThroughPointBlankRecovery(EnemyInfo goalEnemy)
        {
            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose)
            {
                return false;
            }

            // At point blank, a healthy rifleman turning away for retreat cover is usually worse
            // than forcing local pressure. Only critical medical pressure is allowed to override it.
            return !combatCommon.IsFollowerCriticallyWounded() &&
                   !combatCommon.HasUrgentHealWork();
        }

        private bool TryCreatePointBlankRecoveryPressure(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "pointBlankRecoveryShoot");
                return true;
            }

            // This is only a short continuity burst for a just-lost close threat. Do not create a
            // generic suppress-from-point movement here; when the recent-contact window expires,
            // reselecting that move every frame creates churn instead of useful pressure.
            if (FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "pointBlankRecoveryRecentSuppress");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Uses the restored old-plugin engage helper only after the current tree has already decided
        /// that a non-visible enemy should still be actively pushed.
        /// </summary>
        private bool TryGetAdvanceDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            bool pushOrdered = combatCommon.ShouldAdvance(goalEnemy);
            if (!pushOrdered)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                return false;
            }

            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(false, enemyLowThreat);
            return true;
        }

        /// <summary>
        /// Converts boss-distance pressure into a regroup objective switch. Ordered push and immediate
        /// local survival branches run earlier and still win before this objective trigger.
        /// </summary>
        private bool TryGetBossCombatObjectiveDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (!ShouldRegroupForBossDistance())
            {
                return false;
            }

            // Do not try to execute the bossward move here anymore. Default combat only decides that
            // "bossward regroup should now be the primary objective". The router will immediately swap
            // objectives and ask regroup for the real move/fire decision in the same frame.
            decision = combatCommon.CreateRegroupObjectiveDecision();
            return true;
        }

        private bool ShouldBlockPassiveBossHold(EnemyInfo goalEnemy)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            bool recentHitPressure =
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 1f) ||
                FollowerAwareness.WasRecentlyHit(botOwner);
            if (!recentHitPressure)
            {
                return false;
            }

            if (goalEnemy.CanShoot || Enemy.IsVisible(botOwner, goalEnemy))
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalLastSeenTime < RecentSeenPressureSeconds &&
                   combatCommon.HasReliablePersonalEnemyLocation(goalEnemy);
        }

        private bool ShouldForceVisibleAlignedFire(EnemyInfo goalEnemy)
        {
            Vector3 enemyPosition = goalEnemy.CurrPosition;
            Vector3 toEnemy = enemyPosition - botOwner.Position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            float distance = toEnemy.magnitude;
            if (distance > VisibleAlignedFireMaxDistance)
            {
                return false;
            }

            Vector3 lookDirection = botOwner.LookDirection;
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            float lookAngle = Vector3.Angle(lookDirection.normalized, toEnemy.normalized);
            if (lookAngle > VisibleAlignedFireMaxAngle ||
                Time.time - goalEnemy.PersonalSeenTime > 1.25f)
            {
                return false;
            }

            Vector3 target = enemyPosition + Vector3.up * 0.8f;
            return FollowerImmediateFirePolicy.HasDirectFireLane(botOwner, target);
        }

        /// <summary>
        /// Low aggression can force regroup as the primary combat objective. This is stronger than the
        /// normal boss-line split and exists mainly so defensive followers stop solving the fight from
        /// their current position unless the enemy is already close enough to demand local engagement.
        /// 0% aggression only fights at VeryClose. Up to 30% aggression only fights at Close.
        /// </summary>
        private bool TryGetLowAggressionRegroupDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            float aggression = combatCommon.GetAggression01();
            if (aggression > 0.3f)
            {
                return false;
            }

            Enemy.EnemyDistance maxLocalEngageDistance = aggression <= 0.01f
                ? Enemy.EnemyDistance.VeryClose
                : Enemy.EnemyDistance.Close;

            if (Enemy.Distance(goalEnemy) <= maxLocalEngageDistance)
            {
                return false;
            }

            Vector3 bossPosition = combatCommon.GetBossPosition();
            if (combatCommon.GetBossNavDistance(bossPosition) <=
                CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius())
            {
                return false;
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.standBy,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
            return true;
        }

        /// <summary>
        /// Old-plugin boss protection behavior: if the boss was just hit, adopt the boss's closest
        /// attacker and either move to boss-local cover or pressure that attacker.
        /// </summary>
        private bool TryGetBossUnderAttackDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (IsCoverIntentRetryActive(CoverIntentKind.ProtectBoss))
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

            // Immediate visible personal combat still wins over boss-protection routing.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (botOwner.Memory.HaveEnemy && sinceLastSeen > 2.5f)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            boss.PrioritizeEnemy(botOwner, bossEnemy);
            EnemyInfo? prioritizedEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(prioritizedEnemy))
            {
                return false;
            }

            Vector3 bossPosition = combatCommon.GetBossPosition();
            FollowerCombatTactic tactic = combatCommon.GetFollowerTactic();

            if (tactic == FollowerCombatTactic.Protector)
            {
                float bossDistance = combatCommon.GetBossNavDistance(bossPosition);
                float bossCoverSearchRadius = CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius();
                if (bossDistance > bossCoverSearchRadius &&
                    combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, bossCoverSearchRadius, out CustomNavigationPoint? bossCover) &&
                    combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, bossCover, "protectBossCover"))
                {
                    CommitCoverIntent(CoverIntentKind.ProtectBoss);
                    decision = combatCommon.CreateCommittedCoverMoveDecision();
                    return true;
                }

                bool enemyLowThreat = combatCommon.IsEnemyLowThreat(prioritizedEnemy, combatCommon.GetAggression01());
                decision = combatPush.EngageEnemy(false, enemyLowThreat);
                return true;
            }

            if (combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(), out CustomNavigationPoint? supportCover) &&
                combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, supportCover, "protectBossCover"))
            {
                CommitCoverIntent(CoverIntentKind.ProtectBoss);
                decision = combatCommon.CreateCommittedCoverMoveDecision();
                return true;
            }

            if (prioritizedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "protectBossFire");
                return true;
            }

            bool lowThreat = combatCommon.IsEnemyLowThreat(prioritizedEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(false, lowThreat);
            return true;
        }

        /// <summary>
        /// Ordered GoForward should commit to a concrete firing position against the enemy's
        /// current body position before falling back to generic pressure/search behavior.
        /// </summary>
        private bool TryGetOrderedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData == null ||
                !followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) ||
                activeCommand != FollowerCommandType.PushEnemy)
            {
                return false;
            }

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("PushEnemy:noActiveEnemy");
                return false;
            }

            if (combatPush.TryCreateOrderedPushFiringPosition(goalEnemy, out decision))
            {
                return true;
            }

            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = MarkOrderedPushDecision(combatPush.EngageEnemy(true, enemyLowThreat));
            return true;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> MarkOrderedPushDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null ||
                decision.Reason.StartsWith("push.ordered", StringComparison.Ordinal))
            {
                return decision;
            }

            if (decision.Reason.StartsWith("push.", StringComparison.Ordinal))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    decision.Action,
                    "push.ordered." + decision.Reason.Substring("push.".Length));
            }

            return decision;
        }

        private bool TryGetAutonomousSuppressDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (Time.time < autoSuppressRetryUntil)
            {
                return false;
            }

            autoSuppressRetryUntil = Time.time + AutoSuppressRetryCooldownSeconds;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (goalEnemy.IsSuppressed() || !goalEnemy.ShallISuppress())
            {
                return false;
            }

            if (combatCommon.TryCreateSuppressDecision(goalEnemy, "autoSuppress", out decision))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds and commits a single cover point for the current threat instead of constantly re-picking.
        /// </summary>
        private bool TryCommitCombatCover(EnemyInfo goalEnemy, bool requireShootLane, out string reason)
        {
            return combatCommon.TryCommitCombatCover(
                goalEnemy,
                requireShootLane,
                CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                out reason);
        }

        /// <summary>
        /// Ends passive hold when the bot leaves committed cover or should transition back into action.
        /// </summary>
        private AICoreActionEndStruct EndHoldPosition(string reason)
        {
            combatCommon.ValidateCommittedCover();

            if (combatCommon.IsCommittedHolderReason(reason))
            {
                return EndCommittedHolder(reason);
            }

            if (string.Equals(reason, ShootCoverHoldReason, StringComparison.Ordinal))
            {
                return EndShootCoverHoldPosition();
            }

            bool isCoverHoldReason = IsCoverHoldReason(reason);
            bool isHoldingInCover = isCoverHoldReason ||
                                    botOwner.Memory.IsInCover ||
                                    combatCommon.IsBotInCommittedCover();

            if (isHoldingInCover)
            {
                EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
                if (HasActivePushOrder())
                {
                    return new AICoreActionEndStruct("orderedPushBreakHold", true);
                }

                if (string.Equals(reason, "coverHold", StringComparison.Ordinal) &&
                    combatCommon.HasCommittedCover() &&
                    !combatCommon.IsBotInCommittedCover())
                {
                    return new AICoreActionEndStruct("leftCommittedCover", true);
                }

                if (goalEnemy != null && ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        IsBossHoldReason(reason)
                            ? "bossUnderAttackBreakBossHold"
                            : "bossUnderAttackBreakCoverHold",
                        true);
                }

                if (goalEnemy != null &&
                    (FollowerCombatCommon.WasHitRecently(botOwner, 0.75f) ||
                     FollowerAwareness.WasRecentlyHit(botOwner)))
                {
                    combatCommon.ClearCommittedCover();
                    return new AICoreActionEndStruct("hitBreakHold", true);
                }

                if (goalEnemy != null && ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        IsBossHoldReason(reason)
                            ? "bossObjectiveBreakBossHold"
                            : "bossObjectiveBreakCoverHold",
                        true);
                }

                if (goalEnemy != null &&
                    TryPrepareAllySupportBreak(
                        "allyEngagementBreakHold",
                        false,
                        out AICoreActionEndStruct allySupportBreak))
                {
                    return allySupportBreak;
                }

                if (!string.Equals(reason, RecoveryCoverHoldReason, StringComparison.Ordinal) &&
                    goalEnemy != null &&
                    !goalEnemy.IsVisible &&
                    combatCommon.ShouldAdvance(goalEnemy))
                {
                    return new AICoreActionEndStruct("advanceFromCover", true);
                }

                if (goalEnemy != null && goalEnemy.IsVisible)
                {
                    return new AICoreActionEndStruct("visibleEnemyBreakHold", true);
                }
            }

            return combatCommon.EndBaseHoldPosition(reason);
        }

        private AICoreActionEndStruct EndCommittedHolder(string reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (HasActivePushOrder())
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("orderedPushBreakCommittedHold", true);
            }

            if (botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("underFireBreakCommittedHold", true);
            }

            bool canBreakForEngagement = combatCommon.HasCommittedHolderSettled(CommittedHolderEngagementBreakSettleSeconds);

            if (canBreakForEngagement &&
                goalEnemy != null &&
                ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakCommittedHold", true);
            }

            if (canBreakForEngagement &&
                goalEnemy != null &&
                TryPrepareAllySupportBreak(
                    "allyEngagementBreakCommittedHold",
                    true,
                    out AICoreActionEndStruct allySupportBreak))
            {
                return allySupportBreak;
            }

            if (goalEnemy != null && IsCommittedHoldEnemyContact(goalEnemy))
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("enemyContactBreakCommittedHold", true);
            }

            if (!FollowerCombatCommon.IsBossDistanceProtectedCommitmentReason(reason) &&
                ShouldRegroupForBossDistance())
            {
                combatCommon.ClearCommittedPosition();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossDistanceBreakCommittedHold", true);
            }

            if (!combatCommon.IsCommittedHolderTimerActive())
            {
                combatCommon.ClearCommittedPosition();
                return new AICoreActionEndStruct("committedHoldExpired", true);
            }

            return FollowerCombatCommon.Continue();
        }

        private bool IsCommittedHoldEnemyContact(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.Distance <= 18f)
            {
                return true;
            }

            return Enemy.IsVisible(botOwner, goalEnemy) &&
                   (goalEnemy.CanShoot || goalEnemy.Distance <= VisibleAlignedFireMaxDistance);
        }

        private static bool IsCoverHoldReason(string? reason)
        {
            return string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                   IsBossHoldReason(reason);
        }

        private AICoreActionEndStruct EndShootCoverHoldPosition()
        {
            combatCommon.ValidateCommittedCover();

            if (!combatCommon.IsBotInCommittedCover())
            {
                shootCoverSettlePhase.Clear();
                ClearCoverIntent();
                return new AICoreActionEndStruct("leftCommittedCover", true);
            }

            if (HasActivePushOrder())
            {
                shootCoverSettlePhase.Clear();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("orderedPushBreakShootCoverHold", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                shootCoverSettlePhase.Clear();
                return new AICoreActionEndStruct("visibleEnemyBreakShootCoverHold", true);
            }

            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.75f))
            {
                shootCoverSettlePhase.Clear();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("underFireBreakShootCoverHold", true);
            }

            AICoreActionEndStruct baseEnd = combatCommon.EndBaseHoldPosition(ShootCoverHoldReason);
            if (baseEnd.Value)
            {
                shootCoverSettlePhase.Clear();
            }

            return baseEnd;
        }

        /// <summary>
        /// Sticky cover movement can otherwise keep feeding the same committed cover even after the
        /// boss line changed enough to make regroup the real objective.
        /// </summary>
        private AICoreActionEndStruct EndCoverMoveOrAttackMoving(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            bool isHealMove =
                FollowerCombatCommon.IsReasonOrSubreason(currentDecision.Reason, "runToHeal") ||
                FollowerCombatCommon.IsReasonOrSubreason(currentDecision.Reason, "moveToHeal");
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!isHealMove && goalEnemy != null && ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedMovement();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakCoverMove", true);
            }

            if (!isHealMove &&
                ShouldEndCurrentDecisionForBossObjective(currentDecision.Reason, allowMovingCommittedCoverBreak: true))
            {
                combatCommon.ClearCommittedMovement();
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossObjectiveBreakCoverMove", true);
            }

            AICoreActionEndStruct result = combatCommon.ShallEndCurrentDecision(currentDecision);
            if (result.Value)
            {
                combatCommon.ClearCommittedMovement();
            }

            return result;
        }

        /// <summary>
        /// Shooting from cover remains sticky while the shot is valid, so it needs the same regroup
        /// objective escape hatch as coverHold/bossHold.
        /// </summary>
        private AICoreActionEndStruct EndShootFromCover(string reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null && ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossUnderAttackBreakShootCover", true);
            }

            if (goalEnemy != null && ShouldBreakCommittedCoverForBossObjective(goalEnemy))
            {
                combatCommon.ClearCommittedCover();
                ClearCoverIntent();
                return new AICoreActionEndStruct("bossObjectiveBreakShootCover", true);
            }

            return combatCommon.EndShootFromCover();
        }
        private bool HasActivePushOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                   activeCommand == FollowerCommandType.PushEnemy;
        }

        private void CommitCoverIntent(CoverIntentKind intent)
        {
            activeCoverIntent = intent;
            coverIntentPhase.BeginTravel();
        }

        private void ClearCoverIntent()
        {
            activeCoverIntent = CoverIntentKind.None;
            coverIntentPhase.Clear();
            preparedAllySupportDecision = null;
        }

        private void UpdateCoverIntentArrival()
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return;
            }

            if (coverIntentPhase.PromoteToHoldOnArrival())
            {
                float timeout = activeCoverIntent == CoverIntentKind.ProtectBoss
                    ? ProtectIntentMaxHoldSeconds
                    : SupportIntentMaxHoldSeconds;
                coverIntentPhase.BeginHoldLifecycle(IntentSettleSeconds, timeout);
            }
        }

        private bool CanScanCoverIntent()
        {
            return activeCoverIntent != CoverIntentKind.None && coverIntentPhase.CanScan;
        }

        private void MarkCoverIntentScanned()
        {
            if (activeCoverIntent != CoverIntentKind.None)
            {
                coverIntentPhase.MarkScanned(IntentScanIntervalSeconds);
            }
        }

        private bool ShouldExpireCoverIntent()
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return false;
            }

            return coverIntentPhase.IsHoldExpired;
        }

        private void ApplyCoverIntentRetryCooldown()
        {
            switch (activeCoverIntent)
            {
                case CoverIntentKind.Support:
                    supportIntentRetryUntil = Mathf.Max(supportIntentRetryUntil, Time.time + IntentRetryCooldownSeconds);
                    break;
                case CoverIntentKind.ProtectBoss:
                    protectIntentRetryUntil = Mathf.Max(protectIntentRetryUntil, Time.time + IntentRetryCooldownSeconds);
                    break;
            }
        }

        private bool IsCoverIntentRetryActive(CoverIntentKind intent)
        {
            return intent switch
            {
                CoverIntentKind.Support => Time.time < supportIntentRetryUntil,
                CoverIntentKind.ProtectBoss => Time.time < protectIntentRetryUntil,
                _ => false,
            };
        }

        private bool ShouldReleaseCoverIntentForOpportunity(EnemyInfo goalEnemy)
        {
            if (!CanScanCoverIntent())
            {
                return false;
            }

            MarkCoverIntentScanned();
            return goalEnemy.IsVisible ||
                   combatCommon.ShouldAdvance(goalEnemy) ||
                   (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance() &&
                    combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01()));
        }

        private bool TryGetCoverIntentRepositionDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CanScanCoverIntent())
            {
                return false;
            }

            int? previousCoverId = combatCommon.CommittedCoverId;
            switch (activeCoverIntent)
            {
                case CoverIntentKind.Support:
                    if (!combatCommon.TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition) ||
                        !combatCommon.TrySelectPreferredSupportEnemy(supportEnemyProfileId, supportEnemyPosition, out EnemyInfo? supportEnemy))
                    {
                        return false;
                    }

                    bool preferBackline = combatCommon.GetFollowerTactic() is FollowerCombatTactic.Marksman or FollowerCombatTactic.Protector;
                    if (!combatCommon.TryCommitSupportFiringCover(supportEnemy, "allySupportCover.refresh", out string supportReason, preferBackline) ||
                        combatCommon.CommittedCoverId == previousCoverId)
                    {
                        return false;
                    }

                    CommitCoverIntent(CoverIntentKind.Support);
                    decision = combatCommon.CreateMoveToCommittedCoverDecision(supportReason);
                    return true;

                case CoverIntentKind.ProtectBoss:
                    if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
                    {
                        return false;
                    }

                    BotOwner? bossEnemy = boss.ClosestEnemy();
                    if (bossEnemy == null || bossEnemy.GetPlayer?.HealthController?.IsAlive != true)
                    {
                        return false;
                    }

                    boss.PrioritizeEnemy(botOwner, bossEnemy);
                    EnemyInfo? prioritizedEnemy = botOwner.Memory.GoalEnemy;
                    if (!combatCommon.HasActiveCombatEnemy(prioritizedEnemy) ||
                        !combatCommon.TryFindBossCover(prioritizedEnemy, combatCommon.GetBossPosition(), CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(), out CustomNavigationPoint? protectCover) ||
                        !combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, protectCover, "protectBossCover.refresh") ||
                        combatCommon.CommittedCoverId == previousCoverId)
                    {
                        return false;
                    }

                    CommitCoverIntent(CoverIntentKind.ProtectBoss);
                    decision = combatCommon.CreateCommittedCoverMoveDecision();
                    return true;
            }

            return false;
        }

        private bool IsDecisionCompatibleWithCoverIntent(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (activeCoverIntent == CoverIntentKind.None)
            {
                return false;
            }

            return nextDecision.Action == BotLogicDecision.runToCover ||
                   nextDecision.Action == BotLogicDecision.attackMoving ||
                   nextDecision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   nextDecision.Action == BotLogicDecision.holdPosition ||
                   nextDecision.Action == BotLogicDecision.shootFromCover;
        }

        /// <summary>
        /// Reuses the boss-relative objective gate as the "leave stale cover/hold and rejoin the boss"
        /// signal. This only triggers after the initial cover commit window and only when there is no
        /// stronger local fight to justify staying put.
        /// </summary>
        private bool ShouldBreakCommittedCoverForBossObjective(EnemyInfo goalEnemy, bool allowMovingCommittedCoverBreak = false)
        {
            return combatCommon.ShouldBreakCommittedCoverForBossObjective(
                goalEnemy,
                ShouldRegroupForBossDistance(),
                HasActivePushOrder(),
                goalEnemy.IsVisible && goalEnemy.CanShoot,
                allowMovingCommittedCoverBreak);
        }

        private bool ShouldBreakForBossUnderAttack(EnemyInfo goalEnemy)
        {
            return combatCommon.ShouldBreakForBossUnderAttack(goalEnemy, HasActivePushOrder());
        }

        private bool ShouldEndCurrentDecisionForBossObjective(string reason, bool allowMovingCommittedCoverBreak = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            return combatCommon.ShouldEndCurrentDecisionForBossObjective(
                reason,
                goalEnemy,
                ShouldRegroupForBossDistance(),
                HasActivePushOrder(),
                goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot,
                allowMovingCommittedCoverBreak);
        }

        private void UpdateShootCoverSettleState(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (IsShootCoverMoveDecision(nextDecision))
            {
                shootCoverSettlePhase.BeginTravel();
                return;
            }

            if (!string.Equals(nextDecision.Reason, ShootCoverHoldReason, StringComparison.Ordinal))
            {
                shootCoverSettlePhase.Clear();
            }
        }

        private static bool IsShootCoverMoveDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return IsShootCoverReason(decision.Reason) &&
                   (decision.Action == BotLogicDecision.runToCover ||
                    decision.Action == BotLogicDecision.attackMoving ||
                    decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                    decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat);
        }

        private static bool IsShootCoverReason(string? reason)
        {
            return FollowerCombatCommon.IsReasonOrSubreason(reason, "shootCover") ||
                   FollowerCombatCommon.IsReasonOrSubreason(reason, "retreatShootCover");
        }

        private static bool IsBossHoldReason(string? reason)
        {
            return FollowerCombatCommon.IsBossHoldReason(reason);
        }

        /// <summary>
        /// Computes whether the follower should switch to the regroup objective based on live boss
        /// nav distance. Regroup itself decides whether that becomes forward, backward, or lateral movement.
        /// </summary>
        private bool ShouldRegroupForBossDistance()
        {
            Vector3 bossPosition = combatCommon.GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float navDistance = combatCommon.GetBossNavDistance(bossPosition);
            float directDistance = Vector3.Distance(botOwner.Position, bossPosition);
            float followerBossDistance = GetSafeRegroupDistance(navDistance, directDistance);
            if (followerBossDistance <= CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance())
            {
                return false;
            }

            return true;
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

        private bool HasExplicitRegroupOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData == null)
            {
                return false;
            }

            return followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }
    }
}
