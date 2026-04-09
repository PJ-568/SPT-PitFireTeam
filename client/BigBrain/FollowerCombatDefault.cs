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
        private const float CoverCommitLockSeconds = 2.5f;
        private const float CoverSearchCooldownSeconds = 0.35f;
        private const float BossCoverSearchRadius = 30f;
        private const float BossHoldSectorRadius = 20f;
        private const float VisiblePushDistance = 18f;
        private const float BlindPushDistance = 32f;
        private const float RecentSeenPressureSeconds = 2f;
        private const string PushReasonPrefix = "push.";

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private readonly FollowerCombatPush combatPush;

        private CustomNavigationPoint? committedCoverPoint;
        private BotLogicDecision committedCoverMoveAction;
        private string? committedCoverMoveReason;
        private float committedCoverUntil;
        private float committedCoverSetAt;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPushDecision;
        private string? committedPushEnemyProfileId;
        private float nextCoverAcquireTime;
        private Vector3 bossHoldAnchor;
        private bool hasBossHoldAnchor;
        private bool bossHoldSectorReanchorPending;

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
            committedCoverPoint = null;
            committedCoverMoveAction = default;
            committedCoverMoveReason = null;
            committedCoverUntil = 0f;
            committedCoverSetAt = 0f;
            committedPushDecision = null;
            committedPushEnemyProfileId = null;
            nextCoverAcquireTime = 0f;
            bossHoldAnchor = Vector3.zero;
            hasBossHoldAnchor = false;
            bossHoldSectorReanchorPending = false;
        }

        /// <summary>
        /// Updates local commitment state after the BigBrain decision changes.
        /// </summary>
        public void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            BotLogicDecision action = nextDecision.Action;
            if (action != BotLogicDecision.shootFromStationary &&
                action != BotLogicDecision.debugStationary &&
                action != BotLogicDecision.debugStationaryInstantTake &&
                botOwner.WeaponManager.Stationary.Taken)
            {
                botOwner.WeaponManager.Stationary.DropCurWeapon(false, true);
            }

            if (IsCoverAffinedDecision(action) && botOwner.Memory?.CurCustomCoverPoint != null)
            {
                CommitCover(botOwner.Memory.CurCustomCoverPoint, action, nextDecision.Reason);
            }

            if (IsPushCommittedDecision(nextDecision))
            {
                CommitPush(nextDecision);
            }
            else
            {
                ClearCommittedPush();
            }

            if (!IsCoverAffinedDecision(action) && committedCoverUntil < Time.time)
            {
                ClearCommittedCover();
            }

            if (string.Equals(nextDecision.Reason, "bossHold", StringComparison.Ordinal))
            {
                bossHoldAnchor = combatCommon.GetBossPosition();
                hasBossHoldAnchor = true;
            }
            else
            {
                hasBossHoldAnchor = false;
                bossHoldAnchor = Vector3.zero;
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
        /// Main follower combat router: opener first, then visible fire, cover commitment,
        /// recovery, controlled pushes, and only finally boss reanchoring.
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
            ValidateCommittedCover();

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

            // First resolve any immediate visible-enemy action: shoot now, take a quick firing cover,
            // or hand off to engage logic if this visible target should be pressed.
            if (TryGetVisibleDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleDecision))
            {
                return visibleDecision;
            }

            // If a cover point was already committed earlier, keep following through on it before
            // inventing a new plan.
            if (TryGetCommittedCoverDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedDecision))
            {
                return committedDecision;
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

            // Sector-driven bossHold exits should first try to find a fresh boss-local cover in the
            // new sector. If none exists, keep holding instead of immediately walking to the boss.
            if (TryGetPendingBossSectorReanchorDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> sectorBossDecision))
            {
                return sectorBossDecision;
            }

            // If none of the combat-specific branches apply, pull the follower back toward the boss
            // so it does not drift out of escort range.
            if (TryGetBossReanchorDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> bossDecision))
            {
                return bossDecision;
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
                    ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "coverVisibleFire");
                    return true;
                }

                // If the bot is exposed and current context says "take the fight from cover",
                // commit one shooting cover before trading from the open.
                if (!botOwner.Memory.IsInCover &&
                    combatCommon.ShouldTakeVisibleCover(goalEnemy) &&
                    TryCommitCombatCover(goalEnemy, requireShootLane: true, out string coverReason))
                {
                    decision = CreateMoveToCommittedCoverDecision(coverReason);
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
                decision = CreateMoveToCommittedCoverDecision(visibleCoverReason);
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

            FollowerGrenadeRuntimeGate.EnableExplicitThrow(botOwner);
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
            if (!HasCommittedCover())
            {
                return false;
            }

            // If the bot has actually reached the committed point, this method owns the next step:
            // either fight from that cover, briefly settle, or hold it.
            if (IsBotInCommittedCover())
            {
                if (HasActivePushOrder())
                {
                    ClearCommittedCover();
                    return false;
                }

                if (goalEnemy.IsVisible && combatCommon.ShouldShootImmediately())
                {
                    ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "committedImmediateShoot");
                    return true;
                }

                // Once the bot has actually reached committed cover, shooting from that cover always wins.
                if (goalEnemy.IsVisible && goalEnemy.CanShoot && combatCommon.CanShootFromCurrentCover(out _))
                {
                    ExtendCommittedCover();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "committedFire");
                    return true;
                }

                // Visible enemies should not get trapped in passive cover hold just because the current
                // cover-fire validation failed. Break out into active pressure instead.
                if (goalEnemy.IsVisible)
                {
                    if (goalEnemy.CanShoot)
                    {
                        ExtendCommittedCover();
                        decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "committedVisibleShoot");
                        return true;
                    }

                    if (combatCommon.ShouldAdvance(goalEnemy))
                    {
                        ClearCommittedCover();
                        bool enemyLowThreat = combatCommon.IsEnemyLowThreat(goalEnemy, combatCommon.GetAggression01());
                        decision = combatPush.EngageEnemy(false, enemyLowThreat);
                        return true;
                    }
                }

                if (!goalEnemy.IsVisible && combatCommon.ShouldAdvance(goalEnemy))
                {
                    ClearCommittedCover();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
                {
                    ClearCommittedCover();
                    return false;
                }

                if (ShouldBreakCommittedCoverForBossLeash(goalEnemy))
                {
                    ClearCommittedCover();
                    return false;
                }

                // Otherwise remain in the committed cover and wait for the next actionable event.
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "coverHold");
                return true;
            }

            // The bot has not reached its committed cover yet, so keep feeding the same cover back into EFT
            // until arrival or invalidation.
            combatCommon.AssignCover(committedCoverPoint);
            decision = CreateCommittedCoverMoveDecision();
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
                decision = CreateMoveToCommittedCoverDecision(coverReason);
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
            if (botOwner.Memory.HaveEnemy && goalEnemy.IsVisible)
            {
                return false;
            }

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
                float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
                if (bossDistanceSqr > BossCoverSearchRadius * BossCoverSearchRadius &&
                    combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? bossCover))
                {
                    BotLogicDecision moveAction = combatCommon.SelectCommittedCoverMoveAction(prioritizedEnemy);
                    CommitCover(bossCover, moveAction, "protectBossCover");
                    combatCommon.AssignCover(bossCover);
                    decision = CreateCommittedCoverMoveDecision();
                    return true;
                }

                bool enemyLowThreat = combatCommon.IsEnemyLowThreat(prioritizedEnemy, combatCommon.GetAggression01());
                decision = combatPush.EngageEnemy(false, enemyLowThreat);
                return true;
            }

            if (combatCommon.TryFindBossCover(prioritizedEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? supportCover))
            {
                BotLogicDecision moveAction = combatCommon.SelectCommittedCoverMoveAction(prioritizedEnemy);
                CommitCover(supportCover, moveAction, "protectBossCover");
                combatCommon.AssignCover(supportCover);
                decision = CreateCommittedCoverMoveDecision();
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
        /// Pulls the follower back toward the boss anchor once they drift too far from escort range.
        /// </summary>
        private bool TryGetBossReanchorDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            Vector3 bossPosition = combatCommon.GetBossPosition();
            float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
            if (bossDistanceSqr <= BossCoverSearchRadius * BossCoverSearchRadius)
            {
                return false;
            }

            if (combatCommon.TryFindBossCover(goalEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? bossCover))
            {
                BotLogicDecision moveAction = combatCommon.SelectCommittedCoverMoveAction(goalEnemy);
                CommitCover(bossCover, moveAction, "bossReanchorCover");
                combatCommon.AssignCover(bossCover);
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                bossPosition = bossHit.position;
            }

            botOwner.GoToSomePointData.SetPoint(bossPosition);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "bossReanchor");
            return true;
        }

        /// <summary>
        /// Handles the special case where bossHold ended because the boss changed sectors:
        /// first try fresh boss-local cover in the new sector, otherwise keep holding.
        /// </summary>
        private bool TryGetPendingBossSectorReanchorDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!bossHoldSectorReanchorPending)
            {
                return false;
            }

            Vector3 bossPosition = combatCommon.GetBossPosition();
            if (combatCommon.TryFindBossCover(goalEnemy, bossPosition, BossCoverSearchRadius, out CustomNavigationPoint? bossCover))
            {
                BotLogicDecision moveAction = combatCommon.SelectCommittedCoverMoveAction(goalEnemy);
                CommitCover(bossCover, moveAction, "bossReanchorCover");
                combatCommon.AssignCover(bossCover);
                bossHoldSectorReanchorPending = false;
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            bossHoldSectorReanchorPending = false;
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "bossHold");
            return true;
        }

        /// <summary>
        /// Finds and commits a single cover point for the current threat instead of constantly re-picking.
        /// </summary>
        private bool TryCommitCombatCover(EnemyInfo goalEnemy, bool requireShootLane, out string reason)
        {
            reason = requireShootLane ? "shootCover" : "safeCover";

            // Reuse the existing committed point instead of replacing it every update. This is the
            // main anti-thrashing gate for combat cover movement.
            if (HasCommittedCover())
            {
                return true;
            }

            // Slow down new cover searches slightly so the bot does not spam cover acquisition while
            // navigating, getting hit, or reevaluating the same geometry every frame.
            if (Time.time < nextCoverAcquireTime)
            {
                return false;
            }

            nextCoverAcquireTime = Time.time + CoverSearchCooldownSeconds;

            CustomNavigationPoint? cover = null;
            if (requireShootLane && combatCommon.IsCoverUsable(combatCommon.PointToShoot))
            {
                cover = combatCommon.PointToShoot;
                reason = "shootCover";
            }

            if (cover == null &&
                combatCommon.TryAssignRetreatAttackCover(goalEnemy, requireShootLane, combatCommon.GetCombatCoverMaxDistanceSqr(), false))
            {
                cover = botOwner.Memory.CurCustomCoverPoint;
                reason = requireShootLane ? "retreatShootCover" : "retreatSafeCover";
            }

            if (cover == null && !requireShootLane && combatCommon.IsCoverUsable(combatCommon.PointToShoot))
            {
                cover = combatCommon.PointToShoot;
                reason = "safeCover";
            }

            if (cover == null && combatCommon.TryFindBossCover(goalEnemy, BossCoverSearchRadius, out CustomNavigationPoint? bossCover))
            {
                cover = bossCover;
                reason = "bossCover";
            }

            if (cover == null)
            {
                return false;
            }

            BotLogicDecision moveAction = combatCommon.SelectCommittedCoverMoveAction(goalEnemy);
            CommitCover(cover, moveAction, reason);
            combatCommon.AssignCover(cover);
            return true;
        }

        /// <summary>
        /// Converts the current committed cover into the movement action needed to reach it.
        /// </summary>
        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMoveToCommittedCoverDecision(
            string reason)
        {
            // The move mode for a committed cover must stay stable. Recomputing it from live visibility
            // lets the same cover run flip between sprint and attack-moving, which the layer treats as
            // an action change and immediately stops mid-path.
            if (!string.IsNullOrEmpty(reason))
            {
                committedCoverMoveReason = reason;
            }

            return CreateCommittedCoverMoveDecision();
        }

        /// <summary>
        /// Ends passive hold when the bot leaves committed cover or should transition back into action.
        /// </summary>
        private AICoreActionEndStruct EndHoldPosition(string reason)
        {
            ValidateCommittedCover();

            if (string.Equals(reason, "coverHold", StringComparison.Ordinal) ||
                string.Equals(reason, "bossHold", StringComparison.Ordinal))
            {
                EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
                if (HasActivePushOrder())
                {
                    return new AICoreActionEndStruct("orderedPushBreakHold", true);
                }

                if (string.Equals(reason, "coverHold", StringComparison.Ordinal) && !IsBotInCommittedCover())
                {
                    return new AICoreActionEndStruct("leftCommittedCover", true);
                }

                if (string.Equals(reason, "coverHold", StringComparison.Ordinal) &&
                    goalEnemy != null &&
                    ShouldBreakCommittedCoverForBossLeash(goalEnemy))
                {
                    ClearCommittedCover();
                    return new AICoreActionEndStruct("bossTooFarBreakCoverHold", true);
                }

                if (string.Equals(reason, "bossHold", StringComparison.Ordinal))
                {
                    Vector3 bossPosition = combatCommon.GetBossPosition();
                    if (hasBossHoldAnchor &&
                        (bossPosition - bossHoldAnchor).sqrMagnitude > BossHoldSectorRadius * BossHoldSectorRadius)
                    {
                        bossHoldSectorReanchorPending = true;
                        return new AICoreActionEndStruct("bossLeftSectorBreakHold", true);
                    }

                    float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
                    if (bossDistanceSqr > BossCoverSearchRadius * BossCoverSearchRadius)
                    {
                        return new AICoreActionEndStruct("bossTooFarBreakHold", true);
                    }
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
        /// Ends tactical movement once the enemy is reacquired, pressure returns, or the bot reaches the point.
        /// </summary>
        private AICoreActionEndStruct EndGoToPoint(string reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (botOwner.GoToSomePointData.IsCome())
            {
                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            return default;
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
        /// Starts or refreshes the local cover commitment timer.
        /// </summary>
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
        }

        /// <summary>
        /// Keeps a working cover assignment alive while the bot is actively using it.
        /// </summary>
        private void ExtendCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return;
            }

            committedCoverUntil = Mathf.Max(committedCoverUntil, Time.time + 0.75f);
        }

        /// <summary>
        /// Drops the local cover commitment so the bot can pick a new option next update.
        /// </summary>
        private void ClearCommittedCover()
        {
            committedCoverPoint = null;
            committedCoverMoveAction = default;
            committedCoverMoveReason = null;
            committedCoverSetAt = 0f;
            committedCoverUntil = 0f;
        }

        /// <summary>
        /// Checks whether the current cover commitment is still valid enough to keep using.
        /// </summary>
        private bool HasCommittedCover()
        {
            // No point means there is no active commitment to preserve.
            if (committedCoverPoint == null)
            {
                return false;
            }

            // If the bot never reached the point before the lock expired, drop it so a new cover can
            // be selected instead of endlessly pathing toward a stale destination.
            if (committedCoverUntil < Time.time && !IsBotInCommittedCover())
            {
                ClearCommittedCover();
                return false;
            }

            return IsCommittedCoverStillUsable(committedCoverPoint);
        }

        /// <summary>
        /// Discards broken cover commitments before they can keep the bot in a bad state.
        /// </summary>
        private void ValidateCommittedCover()
        {
            // Central cleanup gate for stale cover assignments. This keeps the bot committed when the
            // point is still usable, but releases it as soon as the point becomes invalid.
            if (!HasCommittedCover())
            {
                ClearCommittedCover();
            }
        }

        /// <summary>
        /// Softer validity gate for an already-committed cover point.
        /// Once a bot has committed, keep that cover unless it is clearly broken or out of leash.
        /// </summary>
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

        /// <summary>
        /// Treats the bot as arrived when EFT marks the cover active or the bot is physically close to it.
        /// </summary>
        private bool IsBotInCommittedCover()
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

        private bool ShouldBreakCommittedCoverForBossLeash(EnemyInfo goalEnemy)
        {
            if (!IsBotInCommittedCover())
            {
                return false;
            }

            if (Time.time - committedCoverSetAt < CoverCommitLockSeconds)
            {
                return false;
            }

            float bossDistanceSqr = (botOwner.Position - combatCommon.GetBossPosition()).sqrMagnitude;
            if (bossDistanceSqr <= BossCoverSearchRadius * BossCoverSearchRadius)
            {
                return false;
            }

            if (HasActivePushOrder())
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            if (combatCommon.ShouldAdvance(goalEnemy))
            {
                return false;
            }

            return true;
        }



        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedCoverMoveDecision()
        {
            BotLogicDecision moveAction = committedCoverMoveAction != default
                ? committedCoverMoveAction
                : (botOwner.CanSprintPlayer ? BotLogicDecision.runToCover : BotLogicDecision.attackMoving);
            string reason = !string.IsNullOrEmpty(committedCoverMoveReason)
                ? committedCoverMoveReason
                : "commitCover";
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(moveAction, reason);
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

        /// <summary>
        /// Marks decisions that should preserve the current cover commitment across reevaluations.
        /// </summary>
        private static bool IsCoverAffinedDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == BotLogicDecision.shootFromCover ||
                   decision == BotLogicDecision.holdPosition;
        }

        private static bool IsPushCommittedDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (!IsPushReason(decision.Reason))
            {
                return string.Equals(decision.Reason, "startWeakEnemyPush", StringComparison.Ordinal);
            }

            return decision.Action == BotLogicDecision.runToEnemy ||
                   decision.Action == BotLogicDecision.goToEnemy ||
                   decision.Action == BotLogicDecision.runToCover ||
                   decision.Action == BotLogicDecision.attackMoving ||
                   decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   decision.Action == BotLogicDecision.goToPointTactical;
        }

        private static bool IsPushReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   reason.StartsWith(PushReasonPrefix, StringComparison.Ordinal);
        }
    }
}
