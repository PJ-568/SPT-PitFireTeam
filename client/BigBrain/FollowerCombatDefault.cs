using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatDefault
    {
        private const float BossCoverSearchRadius = 30f;
        private const float BossRegroupTriggerDistance = BossCoverSearchRadius * 0.6f;
        private const float VisiblePushDistance = 18f;
        private const float BlindPushDistance = 32f;
        private const float RecentSeenPressureSeconds = 2f;
        private const string PushReasonPrefix = "push.";

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private readonly FollowerCombatPush combatPush;

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPushDecision;
        private string? committedPushEnemyProfileId;

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
            committedPushDecision = null;
            committedPushEnemyProfileId = null;
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

            if (IsPushCommittedDecision(nextDecision))
            {
                CommitPush(nextDecision);
            }
            else
            {
                ClearCommittedPush();
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
        /// Main follower combat router: opener first, then push/visible/recovery branches,
        /// boss-distance regroup triggers, and finally committed cover or passive hold fallback.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26>? preFight = combatCommon.PreFightLogic();
            if (preFight != null)
            {
                return preFight.Value;
            }

            if (TryActivateFollowerGrenade(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.throwGrenadeFromPlace, "FollowerGrenade");
            }

            if (combatCommon.HasInitialDecision)
            {
                return combatCommon.ConsumeInitialDecision();
            }

            combatCommon.RefreshShootCover();
            // - prevent bot from getting locked out of cover decisions due to a bad commitment or cover destruction
            combatCommon.ValidateCommittedCover();

            // Once a push has been chosen, keep returning that same push action until a hard interrupt
            // or the action end logic says the push phase is over.
            if (TryGetCommittedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush))
            {
                return committedPush;
            }

            // Explicit GoForward push orders should preempt normal visible/cover decisions so the
            // order actually produces pressure instead of getting absorbed by passive combat branches.
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

            // First resolve any immediate visible-enemy action: shoot now, take a quick firing cover,
            // or hand off to engage logic if this visible target should be pressed.
            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision))
            {
                return visibleDecision;
            }

            // When exposed, under fire, or hurt, switch to recovery behavior and look for safe cover
            // before considering more aggressive options.
            if (TryGetRecoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recoverDecision))
            {
                return recoverDecision;
            }

            // If the boss was just hit and this follower is not already committed to a stronger
            // personal fight, bias toward protecting the boss and adopting the boss's attacker.
            if (TryGetBossUnderAttackDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> protectBossDecision))
            {
                return protectBossDecision;
            }

            // While passively holding in cover, scan for a credible ally support opportunity before
            // defaulting into generic push/search or idle holding behavior.
            AICoreActionResultStruct<BotLogicDecision, GClass26>? allySupportDecision =
                combatCommon.TryGetAllyEngagementSupportDecision();
            if (allySupportDecision != null)
            {
                return allySupportDecision.Value;
            }

            // If pushing is justified after the safety checks above, ask the engage helper to choose
            // how to pressure the enemy or search forward.
            if (TryGetAdvanceDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> advanceDecision))
            {
                return advanceDecision;
            }

            // If the boss is outside the combat leash and there is no immediate visible fight to solve
            // first, switch objectives before stale cover commitments can reselect shootCover/bossHold.
            if (TryGetBossCombatObjectiveDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> bossObjectiveDecision))
            {
                return bossObjectiveDecision;
            }

            // If a cover point was already committed earlier, keep following through on it before
            // inventing a new plan.
            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedDecision))
            {
                return committedDecision;
            }

            if (botOwner.Memory.IsInCover)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
            }

            if (goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "fallbackShoot");
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "bossHold");
        }

        /// <summary>
        /// Keeps decision end conditions local to the simplified combat state machine.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (IsPushReason(currentDecision.Reason))
            {
                return EndCommittedPush(currentDecision);
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
                case BotLogicDecision.runToEnemy:
                case BotLogicDecision.goToEnemy:
                    return combatCommon.EndBaseGoToEnemy();
                case BotLogicDecision.goToPoint:
                case BotLogicDecision.goToPointTactical:
                case BotLogicDecision.search:
                    return EndGoToPoint(currentDecision.Reason);
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

                // A fresh immediate-fire window should override cover validation. This lets the bot
                // snap to a newly exposed or flanking enemy even if the previous cover orientation
                // is no longer the correct way to fight.
                if (combatCommon.ShouldShootImmediately())
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleImmediateShoot");
                    return true;
                }

                // If the bot is already protected by cover and that cover still supports a real shot,
                // use it immediately instead of re-evaluating movement.
                if (botOwner.Memory.IsInCover && combatCommon.CanShootFromCurrentCover(out _))
                {
                    combatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "coverVisibleFire");
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

                // Otherwise a visible, shootable enemy is resolved as an immediate stand-and-fire decision.
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "visibleShoot");
                return true;
            }

            // The enemy is seen but cannot be shot from the current spot, so first try to take one
            // committed firing cover that should open the lane.
            // The enemy is visible but not yet shootable from here, so prefer moving to a cover
            // that creates a lane before considering forward pressure.
            if (!botOwner.Memory.IsInCover &&
                TryCommitCombatCover(goalEnemy, requireShootLane: true, out string visibleCoverReason))
            {
                decision = combatCommon.CreateMoveToCommittedCoverDecision(visibleCoverReason);
                return true;
            }

            // If no immediate firing position exists, aggressive followers are allowed to close distance
            // while the enemy is still visible.
            if (combatCommon.ShouldAdvance(goalEnemy) && goalEnemy.Distance <= VisiblePushDistance)
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

        /// <summary>
        /// Keeps push movement stable across reevaluations until a hard interrupt or completion condition fires.
        /// </summary>
        private bool TryGetCommittedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasCommittedPush())
            {
                return false;
            }

            if (ShouldInterruptCommittedPush(goalEnemy, out _))
            {
                ClearCommittedPush();
                return false;
            }

            decision = committedPushDecision!.Value;
            return true;
        }

        /// <summary>
        /// Activates an explicit grenade throw request only when the target is visible, not already
        /// a clean gunfight, and the throw is safe for the boss/followers.
        /// </summary>
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

            FollowerGrenadeRuntimeGate.EnableExplicitThrow(botOwner);
            if (botOwner.WeaponManager.Grenades == null ||
                !botOwner.WeaponManager.Grenades.HaveGrenade)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                return false;
            }

            Vector3 targetPosition = goalEnemy.CurrPosition + Vector3.up;
            if (IsFriendlyTooCloseToGrenadeTarget(targetPosition, 8f))
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                return false;
            }

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
                return false;
            }

            // If the bot has actually reached the committed point, this method owns the next step:
            // either fight from that cover, briefly settle, or hold it.
            if (combatCommon.IsBotInCommittedCover())
            {
                if (HasActivePushOrder())
                {
                    combatCommon.ClearCommittedCover();
                    return false;
                }

                if (goalEnemy.IsVisible && combatCommon.ShouldShootImmediately())
                {
                    combatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "committedImmediateShoot");
                    return true;
                }

                // Once the bot has actually reached committed cover, shooting from that cover always wins.
                if (goalEnemy.IsVisible && goalEnemy.CanShoot && combatCommon.CanShootFromCurrentCover(out _))
                {
                    combatCommon.ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "committedFire");
                    return true;
                }

                // Visible enemies should not get trapped in passive cover hold just because the current
                // cover-fire validation failed. Break out into active pressure instead.
                if (goalEnemy.IsVisible)
                {
                    if (goalEnemy.CanShoot)
                    {
                        combatCommon.ExtendCommittedCover();
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "committedVisibleShoot");
                        return true;
                    }

                    if (combatCommon.ShouldAdvance(goalEnemy))
                    {
                        combatCommon.ClearCommittedCover();
                        bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
                        decision = combatPush.EngageEnemy(false, enemyLowThreat);
                        return true;
                    }
                }

                if (!goalEnemy.IsVisible && combatCommon.ShouldAdvance(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();
                    return false;
                }

                if (ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();
                    return false;
                }

                // Otherwise remain in the committed cover and wait for the next actionable event.
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
                combatCommon.IsFollowerCriticallyWounded();

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
            if (combatCommon.GetBossNavDistance(bossPosition) <= BossCoverSearchRadius)
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
                if (bossDistance > BossCoverSearchRadius &&
                    combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? bossCover) &&
                    combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, bossCover, "protectBossCover"))
                {
                    decision = combatCommon.CreateCommittedCoverMoveDecision();
                    return true;
                }

                bool enemyLowThreat = combatCommon.IsEnemyLowThreat(prioritizedEnemy, combatCommon.GetAggression01());
                decision = combatPush.EngageEnemy(false, enemyLowThreat);
                return true;
            }

            if (combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? supportCover) &&
                combatCommon.TryCommitSelectedCombatCover(prioritizedEnemy, supportCover, "protectBossCover"))
            {
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
        /// Old-plugin GoForward behavior in combat: if the follower already has an enemy,
        /// treat the order as a forced push and delegate to EngageEnemy(true).
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

            bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
            decision = combatPush.EngageEnemy(true, enemyLowThreat);
            return true;
        }

        /// <summary>
        /// Finds and commits a single cover point for the current threat instead of constantly re-picking.
        /// </summary>
        private bool TryCommitCombatCover(EnemyInfo goalEnemy, bool requireShootLane, out string reason)
        {
            return combatCommon.TryCommitCombatCover(goalEnemy, requireShootLane, BossCoverSearchRadius, out reason);
        }

        /// <summary>
        /// Ends passive hold when the bot leaves committed cover or should transition back into action.
        /// </summary>
        private AICoreActionEndStruct EndHoldPosition(string reason)
        {
            combatCommon.ValidateCommittedCover();

            if (string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                string.Equals(reason, "bossHold", StringComparison.Ordinal))
            {
                EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
                if (HasActivePushOrder())
                {
                    return new AICoreActionEndStruct("orderedPushBreakHold", true);
                }

                if (string.Equals(reason, "coverHold", StringComparison.Ordinal) && !combatCommon.IsBotInCommittedCover())
                {
                    return new AICoreActionEndStruct("leftCommittedCover", true);
                }

                if (goalEnemy != null &&
                    (string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                     string.Equals(reason, "bossHold", StringComparison.Ordinal)) &&
                    ShouldBreakForBossUnderAttack(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        string.Equals(reason, "bossHold", StringComparison.Ordinal)
                            ? "bossUnderAttackBreakBossHold"
                            : "bossUnderAttackBreakCoverHold",
                        true);
                }

                if (goalEnemy != null &&
                    (string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                     string.Equals(reason, "bossHold", StringComparison.Ordinal)) &&
                    ShouldBreakCommittedCoverForBossObjective(goalEnemy))
                {
                    combatCommon.ClearCommittedCover();

                    return new AICoreActionEndStruct(
                        string.Equals(reason, "bossHold", StringComparison.Ordinal)
                            ? "bossObjectiveBreakBossHold"
                            : "bossObjectiveBreakCoverHold",
                        true);
                }

                if (goalEnemy != null && !goalEnemy.IsVisible && combatCommon.ShouldAdvance(goalEnemy))
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

        /// <summary>
        /// Sticky cover movement can otherwise keep feeding the same committed cover even after the
        /// boss line changed enough to make regroup the real objective.
        /// </summary>
        private AICoreActionEndStruct EndCoverMoveOrAttackMoving(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy != null && ShouldBreakForBossUnderAttack(goalEnemy))
            {
                combatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("bossUnderAttackBreakCoverMove", true);
            }

            if (ShouldEndCurrentDecisionForBossObjective(currentDecision.Reason, allowMovingCommittedCoverBreak: true))
            {
                combatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("bossObjectiveBreakCoverMove", true);
            }

            return combatCommon.ShallEndCurrentDecision(currentDecision);
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
                return new AICoreActionEndStruct("bossUnderAttackBreakShootCover", true);
            }

            if (goalEnemy != null && ShouldBreakCommittedCoverForBossObjective(goalEnemy))
            {
                combatCommon.ClearCommittedCover();
                return new AICoreActionEndStruct("bossObjectiveBreakShootCover", true);
            }

            return combatCommon.EndShootFromCover();
        }

        /// <summary>
        /// Ends tactical movement once the enemy is reacquired, pressure returns, or the bot reaches the point.
        /// </summary>
        private AICoreActionEndStruct EndGoToPoint(string reason)
        {
            return combatCommon.EndTacticalPoint(
                reason,
                continueEnemySearchOnArrival: IsEnemySearchPushReason(reason));
        }

        /// <summary>
        /// Push actions are interrupted only by a narrow set of events; otherwise they rely on the
        /// normal movement-action end logic to decide when the current push phase is done.
        /// </summary>
        private AICoreActionEndStruct EndCommittedPush(AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                ClearCommittedPush();
                return new AICoreActionEndStruct("pushEnemyMissingOrDead", true);
            }

            if (ShouldInterruptCommittedPush(goalEnemy, out string interruptReason))
            {
                ClearCommittedPush();
                return new AICoreActionEndStruct(interruptReason, true);
            }

            AICoreActionEndStruct endResult = currentDecision.Action switch
            {
                BotLogicDecision.runToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.goToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.runToCover => combatCommon.EndRunToCover(currentDecision.Reason),
                BotLogicDecision.attackMoving => combatCommon.EndAttackMoving(),
                BotLogicDecision.attackMovingWithSuppress => combatCommon.EndAttackMovingWithSuppress(),
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => combatCommon.EndAttackRetreat(),
                BotLogicDecision.goToPointTactical => EndGoToPoint(currentDecision.Reason),
                _ => combatCommon.ShallEndCurrentDecision(currentDecision),
            };

            if (endResult.Value)
            {
                ClearCommittedPush();
            }

            return endResult;
        }

        private bool HasActivePushOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                   activeCommand == FollowerCommandType.PushEnemy;
        }

        private void CommitPush(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedPushDecision = decision;
            committedPushEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
        }

        private void ClearCommittedPush()
        {
            committedPushDecision = null;
            committedPushEnemyProfileId = null;
        }

        private bool HasCommittedPush()
        {
            return committedPushDecision.HasValue && IsPushCommittedDecision(committedPushDecision.Value);
        }

        private bool ShouldInterruptCommittedPush(EnemyInfo goalEnemy, out string reason)
        {
            reason = string.Empty;

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                reason = "pushEnemyMissingOrDead";
                return true;
            }

            if (!string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                !string.Equals(goalEnemy.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal))
            {
                reason = "pushEnemyChanged";
                return true;
            }

            if (goalEnemy.IsVisible)
            {
                reason = "pushEnemyVisible";
                return true;
            }

            if (botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.5f))
            {
                reason = "pushUnderFire";
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                activeCommand != FollowerCommandType.PushEnemy)
            {
                reason = "pushCommandOverride";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reuses the boss-relative objective gate as the "leave stale cover/hold and rejoin the boss"
        /// signal. This only triggers after the initial cover commit window and only when there is no
        /// stronger local fight to justify staying put.
        /// </summary>
        private bool ShouldBreakCommittedCoverForBossObjective(EnemyInfo goalEnemy, bool allowMovingCommittedCoverBreak = false)
        {
            if (HasActivePushOrder())
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (!ShouldRegroupForBossDistance())
            {
                return false;
            }

            // For committed cover specifically, give the follower time to actually use the cover before
            // escort pressure can pull it out again.
            if (combatCommon.HasCommittedCover())
            {
                if (!combatCommon.IsBotInCommittedCover())
                {
                    return allowMovingCommittedCoverBreak &&
                           combatCommon.IsCommittedCoverLockExpired;
                }

                if (!combatCommon.IsCommittedCoverLockExpired)
                {
                    return false;
                }
            }

            return true;
        }
        private bool ShouldBreakForBossUnderAttack(EnemyInfo goalEnemy)
        {
            if (HasActivePushOrder())
            {
                return false;
            }

            // Do not abandon a personal shot that is already available; that is still support.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (botOwner.Memory.HaveEnemy && sinceLastSeen > 2.5f)
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

        private bool ShouldEndCurrentDecisionForBossObjective(string reason, bool allowMovingCommittedCoverBreak = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            bool isBossHold = string.Equals(reason, "bossHold", StringComparison.Ordinal);
            if (isBossHold)
            {
                return ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowMovingCommittedCoverBreak);
            }

            if (!IsCommittedCoverReason(reason))
            {
                return false;
            }

            return ShouldBreakCommittedCoverForBossObjective(goalEnemy, allowMovingCommittedCoverBreak);
        }

        private static bool IsCommittedCoverReason(string reason)
        {
            return string.Equals(reason, "shootCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "safeCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "retreatShootCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "retreatSafeCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "committedFire", StringComparison.Ordinal);
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

            float followerBossDistance = combatCommon.GetBossNavDistance(bossPosition);
            if (followerBossDistance <= BossRegroupTriggerDistance)
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

        private static bool IsPushCommittedDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (!IsPushReason(decision.Reason))
            {
                return IsStartWeakEnemyPushReason(decision.Reason);
            }

            return decision.Action == BotLogicDecision.runToEnemy ||
                   decision.Action == BotLogicDecision.goToEnemy ||
                   decision.Action == BotLogicDecision.runToCover ||
                   decision.Action == BotLogicDecision.attackMoving ||
                   decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision.Action == BotLogicDecision.goToPointTactical;
        }

        private static bool IsPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith(PushReasonPrefix, StringComparison.Ordinal);
        }

        private static bool IsEnemySearchPushReason(string? reason)
        {
            return IsStartWeakEnemyPushReason(reason) ||
                   string.Equals(reason, "push.search", StringComparison.Ordinal);
        }

        private static bool IsStartWeakEnemyPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("startWeakEnemyPush", StringComparison.Ordinal);
        }
    }
}
