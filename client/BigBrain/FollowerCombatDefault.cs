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
        private const float CombatCoverMaxDistance = 120f;
        private const float BossCoverSearchRadius = 30f;
        private const float VisiblePushDistance = 18f;
        private const float BlindPushDistance = 32f;
        private const float RecentSeenPressureSeconds = 2f;
        private const string PushReasonPrefix = "push.";

        private readonly BotOwner botOwner;
        private readonly BotFollower botFollower;
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
        private Vector3 lastKnownBossPosition;
        private bool hasLastKnownBossPosition;

        public FollowerCombatDefault(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.botFollower = botOwner.BotFollower;
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
            lastKnownBossPosition = Vector3.zero;
            hasLastKnownBossPosition = false;
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
        }

        /// <summary>
        /// Seeds the one-shot combat opener used immediately after combat activation.
        /// </summary>
        public void PrepareStartDecision()
        {
            combatCommon.PrepareStartDecision(GetAggression01());
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

            // Explicit GoForward push orders should force the engage helper in combat instead of
            // falling back to passive cover/support behavior.
            if (TryGetOrderedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> orderedPushDecision))
            {
                return orderedPushDecision;
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
                    ShouldTakeVisibleCover(goalEnemy) &&
                    TryCommitCombatCover(goalEnemy, requireShootLane: true, out string coverReason))
                {
                    decision = CreateMoveToCommittedCoverDecision(goalEnemy, coverReason);
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
                decision = CreateMoveToCommittedCoverDecision(goalEnemy, visibleCoverReason);
                return true;
            }

            // If no immediate firing position exists, aggressive followers are allowed to close distance
            // while the enemy is still visible.
            if (ShouldAdvance(goalEnemy) && goalEnemy.Distance <= VisiblePushDistance)
            {
                // Once local logic decides a visible enemy should be pushed, hand off to the old-plugin
                // engage helper so it can choose rush/walk/approach behavior from its richer threat checks.
                bool enemyLowThreat = IsEnemyLowThreat(goalEnemy);
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

                    if (ShouldAdvance(goalEnemy))
                    {
                        ClearCommittedCover();
                        bool enemyLowThreat = IsEnemyLowThreat(goalEnemy);
                        decision = combatPush.EngageEnemy(false, enemyLowThreat);
                        return true;
                    }
                }

                if (!goalEnemy.IsVisible && ShouldAdvance(goalEnemy))
                {
                    ClearCommittedCover();
                    return false;
                }

                if (!goalEnemy.IsVisible && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
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
            AssignCover(committedCoverPoint);
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
                decision = CreateMoveToCommittedCoverDecision(goalEnemy, coverReason);
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
            bool pushOrdered = ShouldAdvance(goalEnemy);
            if (!pushOrdered)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                return false;
            }

            bool enemyLowThreat = IsEnemyLowThreat(goalEnemy);
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

            Vector3 bossPosition = GetBossPosition();
            FollowerCombatTactic tactic = combatCommon.GetFollowerTactic();

            if (tactic == FollowerCombatTactic.Protector)
            {
                float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
                if (bossDistanceSqr > BossCoverSearchRadius * BossCoverSearchRadius &&
                    TryFindBossCover(prioritizedEnemy, bossPosition, out CustomNavigationPoint? bossCover))
                {
                    BotLogicDecision moveAction = SelectCommittedCoverMoveAction(prioritizedEnemy);
                    CommitCover(bossCover, moveAction, "protectBossCover");
                    AssignCover(bossCover);
                    decision = CreateCommittedCoverMoveDecision();
                    return true;
                }

                bool enemyLowThreat = IsEnemyLowThreat(prioritizedEnemy);
                decision = combatPush.EngageEnemy(false, enemyLowThreat);
                return true;
            }

            if (TryFindBossCover(prioritizedEnemy, bossPosition, out CustomNavigationPoint? supportCover))
            {
                BotLogicDecision moveAction = SelectCommittedCoverMoveAction(prioritizedEnemy);
                CommitCover(supportCover, moveAction, "protectBossCover");
                AssignCover(supportCover);
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            if (prioritizedEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "protectBossFire");
                return true;
            }

            bool lowThreat = IsEnemyLowThreat(prioritizedEnemy);
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

            bool enemyLowThreat = IsEnemyLowThreat(goalEnemy);
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

            Vector3 bossPosition = GetBossPosition();
            float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
            if (bossDistanceSqr <= BossCoverSearchRadius * BossCoverSearchRadius)
            {
                return false;
            }

            if (TryFindBossCover(goalEnemy, bossPosition, out CustomNavigationPoint? bossCover))
            {
                BotLogicDecision moveAction = SelectCommittedCoverMoveAction(goalEnemy);
                CommitCover(bossCover, moveAction, "bossReanchorCover");
                AssignCover(bossCover);
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
            // Temporary experiment: use EFT's own cover search to isolate whether the remaining
            // cover-churn problem comes from our custom PointToShoot / retreat-cover scoring.
            //
            // Old custom selection path left here intentionally for comparison while testing:
            // if (requireShootLane && IsCoverUsable(combatCommon.PointToShoot))
            // {
            //     cover = combatCommon.PointToShoot;
            // }
            //
            // if (cover == null &&
            //     combatCommon.TryAssignRetreatAttackCover(goalEnemy, requireShootLane, GetBossLeashDistanceSqr(), false))
            // {
            //     cover = botOwner.Memory.CurCustomCoverPoint;
            // }
            //
            // if (cover == null && !requireShootLane && IsCoverUsable(combatCommon.PointToShoot))
            // {
            //     cover = combatCommon.PointToShoot;
            // }
            if (TryFindVanillaCombatCover(goalEnemy, requireShootLane, out CustomNavigationPoint? vanillaCover))
            {
                cover = vanillaCover;
                reason = requireShootLane ? "vanillaShootCover" : "vanillaSafeCover";
            }

            if (cover == null && TryFindBossCover(goalEnemy, GetBossPosition(), out CustomNavigationPoint? bossCover))
            {
                cover = bossCover;
                reason = "bossCover";
            }

            if (cover == null)
            {
                return false;
            }

            BotLogicDecision moveAction = SelectCommittedCoverMoveAction(goalEnemy);
            CommitCover(cover, moveAction, reason);
            AssignCover(cover);
            return true;
        }

        /// <summary>
        /// Uses EFT's own cover search to pick a combat cover point so we can compare vanilla
        /// point selection against the mod's custom cover scoring.
        /// </summary>
        private bool TryFindVanillaCombatCover(EnemyInfo goalEnemy, bool requireShootLane, out CustomNavigationPoint? cover)
        {
            cover = null;

            Vector3 bossPosition = GetBossPosition();
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            CoverShootType shootType = shootPoint != null ? CoverShootType.shoot : CoverShootType.hide;
            Vector3? friendCover = botOwner.Covers.ClosestFriendCoverPoint();

            CoverSearchData searchData = new CoverSearchData(
                bossPosition,
                botOwner.CoverSearchInfo,
                shootType,
                LocalBotSettingsProviderClass.Core.START_DIST_TO_COV,
                0f,
                CoverSearchType.closerToSelectedPoint,
                shootPoint,
                friendCover,
                bossPosition,
                ECheckSHootHide.shootAndHide,
                new CoverSearchDefenceDataClass(botOwner.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL),
                PointsArrayType.byShootType,
                true,
                null,
                null,
                "FollowerCombatDefault");

            CustomNavigationPoint? candidate = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
            if (!IsCoverUsable(candidate))
            {
                return false;
            }

            if ((candidate.Position - botOwner.Position).sqrMagnitude > GetCombatCoverMaxDistanceSqr())
            {
                return false;
            }

            cover = candidate;
            return true;
        }

        /// <summary>
        /// Converts the current committed cover into the movement action needed to reach it.
        /// </summary>
        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMoveToCommittedCoverDecision(
            EnemyInfo goalEnemy,
            string reason)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(SelectCommittedCoverMoveAction(goalEnemy), reason);
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

                if (string.Equals(reason, "bossHold", StringComparison.Ordinal))
                {
                    Vector3 bossPosition = GetBossPosition();
                    float bossDistanceSqr = (botOwner.Position - bossPosition).sqrMagnitude;
                    if (bossDistanceSqr > BossCoverSearchRadius * BossCoverSearchRadius)
                    {
                        return new AICoreActionEndStruct("bossTooFarBreakHold", true);
                    }
                }

                if (goalEnemy != null && !goalEnemy.IsVisible && ShouldAdvance(goalEnemy))
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

        /// <summary>
        /// Decides whether a visible enemy should force the bot into cover before trading shots.
        /// </summary>
        private bool ShouldTakeVisibleCover(EnemyInfo goalEnemy)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            if (combatCommon.IsFollowerCriticallyWounded() || botOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(botOwner, 0.75f))
            {
                return true;
            }

            float aggression = GetAggression01();
            float standAndTradeDistance = botOwner.LookSensor.MaxShootDist * 0.5f;
            return aggression < 0.45f && goalEnemy.Distance > standAndTradeDistance && combatCommon.PointToShoot != null;
        }

        /// <summary>
        /// Central aggression gate for pushes so followers do not advance while hurt, pinned, or uncertain.
        /// </summary>
        private bool ShouldAdvance(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (combatCommon.IsFollowerCriticallyWounded() ||
                botOwner.Memory.IsUnderFire ||
                FollowerCombatCommon.WasHitRecently(botOwner, 1f))
            {
                return false;
            }

            float aggression = GetAggression01();
            float pushThreshold = goalEnemy.IsVisible ? 0.35f : 0.45f;

            if (combatCommon.GetFollowerTactic() == FollowerCombatTactic.Protector)
            {
                pushThreshold += 0.15f;
            }
            else if (combatCommon.GetFollowerTactic() == FollowerCombatTactic.Marksman)
            {
                pushThreshold += 0.3f;
            }

            Enemy.EnemyDistance maxPushDistance = combatCommon.GetMaxPushDistance(aggression);

            if (!IsEnemyLowThreat(goalEnemy))
            {
                return aggression >= 0.7f && maxPushDistance <= Enemy.EnemyDistance.Close;
            }

            if (!goalEnemy.IsVisible && !combatCommon.HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }


            if (Enemy.Distance(goalEnemy) > maxPushDistance)
            {
                return false;
            }

            return aggression >= pushThreshold && combatCommon.ProtectWantKill(goalEnemy.Distance * 1.2f);
        }

        /// <summary>
        /// Reads the configured follower aggression as a normalized 0-1 value.
        /// </summary>
        private float GetAggression01()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            float aggression = followerData?.CombatAggression ?? 50f;
            return Mathf.Clamp01(aggression / 100f);
        }

        private bool IsEnemyLowThreat(EnemyInfo goalEnemy)
        {
            return combatCommon.IsEnemyLowThreat(goalEnemy, GetAggression01() >= 0.4f, GetAggression01() >= 0.7f ? 3f : GetAggression01() >= 0.4f ? 2f : 1f);
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
        /// Basic validity gate for a candidate cover point.
        /// </summary>
        private bool IsCoverUsable(CustomNavigationPoint? cover)
        {
            return cover != null &&
                   cover.IsFreeById(botOwner.Id) &&
                   !cover.IsSpotted;
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

        /// <summary>
        /// Returns the mod-owned maximum combat cover search distance.
        /// </summary>
        private float GetCombatCoverMaxDistanceSqr()
        {
            return CombatCoverMaxDistance * CombatCoverMaxDistance;
        }

        /// <summary>
        /// Pushes the selected cover point into EFT's cover memory so movement actions use it.
        /// </summary>
        private void AssignCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(cover, true);
        }

        /// <summary>
        /// Reads the boss position with a cached fallback so combat does not collapse on transient nulls.
        /// </summary>
        private Vector3 GetBossPosition()
        {
            Vector3? liveBossPos = botFollower.BossToFollow?.Position;
            if (liveBossPos.HasValue &&
                IsFinite(liveBossPos.Value))
            {
                lastKnownBossPosition = liveBossPos.Value;
                hasLastKnownBossPosition = true;
                return liveBossPos.Value;
            }

            if (hasLastKnownBossPosition && IsFinite(lastKnownBossPosition))
            {
                return lastKnownBossPosition;
            }

            return botOwner.Position;
        }

        /// <summary>
        /// Finds a safe boss-local cover to use when the follower needs to reanchor to escort distance.
        /// </summary>
        private bool TryFindBossCover(EnemyInfo goalEnemy, Vector3 bossPosition, out CustomNavigationPoint? cover)
        {
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);

            // Boss-local escort/reanchor cover should prefer the closest valid point to the boss,
            // not an arbitrary vanilla pick somewhere inside the radius.
            CustomNavigationPoint? candidate = Covers.GetClosestCoverPoint(
                botOwner,
                bossPosition,
                BossCoverSearchRadius,
                point =>
                {
                    if (!IsCoverUsable(point))
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude < 2f * 2f)
                    {
                        return false;
                    }

                    return !IsFinite(enemyAnchor) || point.CanIHideFromPos(0f, true, false, enemyAnchor);
                });

            if (candidate == null)
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

        private BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return BotLogicDecision.attackMoving;
            }

            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                return BotLogicDecision.attackMovingWithSuppress;
            }

            return botOwner.CanSprintPlayer
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;
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
        /// Picks the best available enemy anchor for blind pushes and cover searches.
        /// </summary>
        private static Vector3 GetEnemyAnchor(EnemyInfo goalEnemy)
        {
            if (IsFinite(goalEnemy.CurrPosition) && goalEnemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
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
                return false;
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
