using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    /// <summary>
    /// Shared push planner and committed-push lifecycle. Tactics ask this class to build a
    /// pressure plan; common stores the committed decision; the tactic router decides when
    /// to ask for or honor that committed push.
    /// </summary>
    internal sealed class FollowerCombatPush
    {
        private const string PushReasonPrefix = "push.";
        private const float RunToEnemyNonSprintGraceSeconds = 0.75f;
        private const float RunToEnemyNoSprintBlockSeconds = 3f;

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private float runToEnemyNonSprintSince;
        private float committedPushActionableVisibleSince;

        public FollowerCombatPush(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
        }

        public void Reset()
        {
            ClearCommittedPush("reset");
        }

        public void HandleDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            // Push is intentionally latched by reason, not by caller. Default can use it as
            // assault pressure while marksman can use the same commit mechanics for support
            // positioning without becoming a default rusher.
            if (IsPushCommittedDecision(nextDecision))
            {
                CommitPush(nextDecision);
            }
            else
            {
                ClearCommittedPush("decisionChanged");
            }
        }

        public bool HasCommittedPush()
        {
            return combatCommon.HasCommittedPushDecision();
        }

        public bool TryGetCommittedPushDecision(
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
                ClearCommittedPush("committedPushInterrupted");
                return false;
            }

            return combatCommon.TryGetCommittedPushDecision(goalEnemy, out decision);
        }

        public AICoreActionEndStruct EndCommittedPush(AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) &&
                !combatCommon.TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                ClearCommittedPush("pushEnemyMissingOrDead");
                return new AICoreActionEndStruct("pushEnemyMissingOrDead", true);
            }

            if (goalEnemy == null)
            {
                ClearCommittedPush("pushEnemyMissingOrDead");
                return new AICoreActionEndStruct("pushEnemyMissingOrDead", true);
            }

            combatCommon.RefreshCommittedPushEnemyRetention();

            if (ShouldInterruptCommittedPush(goalEnemy, out string interruptReason))
            {
                ClearCommittedPush(interruptReason);
                return new AICoreActionEndStruct(interruptReason, true);
            }

            if (currentDecision.Action == BotLogicDecision.runToEnemy &&
                !combatCommon.CanSprintForCombatMovement())
            {
                combatCommon.BlockRunToEnemy(RunToEnemyNoSprintBlockSeconds);
                ClearCommittedPush("pushRunCannotSprint");
                return new AICoreActionEndStruct("pushRunCannotSprint", true);
            }

            if (currentDecision.Action == BotLogicDecision.runToEnemy &&
                ShouldEndRunToEnemyBecauseNotSprinting())
            {
                combatCommon.BlockRunToEnemy(RunToEnemyNoSprintBlockSeconds);
                ClearCommittedPush("pushRunNotSprinting");
                return new AICoreActionEndStruct("pushRunNotSprinting", true);
            }

            AICoreActionEndStruct endResult = currentDecision.Action switch
            {
                BotLogicDecision.runToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.goToEnemy => combatCommon.EndBaseGoToEnemy(),
                BotLogicDecision.runToCover => combatCommon.EndRunToCover(currentDecision.Reason),
                BotLogicDecision.goToPointTactical => combatCommon.EndTacticalPoint(),
                BotLogicDecision.attackMoving => combatCommon.EndAttackMoving(),
                BotLogicDecision.attackMovingWithSuppress => combatCommon.EndAttackMovingWithSuppress(),
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => combatCommon.EndAttackRetreat(currentDecision.Reason),
                _ => combatCommon.ShallEndCurrentDecision(currentDecision),
            };

            if (endResult.Value)
            {
                ClearCommittedPush(endResult.Reason);
            }

            return endResult;
        }

        public void ClearCommittedPush(string? reason = null)
        {
            ReleasePushEvent(reason ?? "clearPush");
            combatCommon.ClearCommittedPushDecision();
            runToEnemyNonSprintSince = 0f;
            committedPushActionableVisibleSince = 0f;
        }

        public bool IsPushCommittedDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
                   decision.Action == BotLogicDecision.goToPointTactical ||
                   decision.Action == BotLogicDecision.search;
        }

        public static bool IsPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith(PushReasonPrefix, StringComparison.Ordinal);
        }

        public static bool IsStartWeakEnemyPushReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("startWeakEnemyPush", StringComparison.Ordinal);
        }

        /// <summary>
        /// Ported from old plugin EngageEnemy intent: decide direct engage pressure
        /// based on visibility, distance band, and low-threat checks.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> EngageEnemy(bool pushOrdered = false, bool enemyLowThreat = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "engageNoEnemy");
            }

            if (IsEnemyMarksman(goalEnemy) &&
                TryCreateMarksmanFightDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> marksmanFight))
            {
                return marksmanFight;
            }

            bool enemyVisible = goalEnemy.IsVisible;
            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(goalEnemy);
            float enemiesAtLocation = enemyLowThreat || string.IsNullOrEmpty(goalEnemy.ProfileId)
                ? 1f
                : Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition);

            // Old pusher behavior: push aggressively if ordered or if attack-immediate conditions align.
            if (botOwner.Memory.AttackImmediately || pushOrdered)
            {
                bool canRunToEnemy = combatCommon.CanSprintForCombatMovement() &&
                                     combatCommon.CanRunToEnemyNow();
                if ((distanceToEnemy <= Utils.Enemy.EnemyDistance.Close && enemiesAtLocation < 2f) ||
                    (pushOrdered && enemiesAtLocation < 4f))
                {
                    BotLogicDecision pushDecision;
                    if (pushOrdered)
                    {
                        pushDecision = canRunToEnemy
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }
                    else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }
                    else
                    {
                        pushDecision = canRunToEnemy
                            ? BotLogicDecision.runToEnemy
                            : BotLogicDecision.goToEnemy;
                    }

                    if (!Utils.Enemy.IsClosestEnemy(botOwner) && distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }

                    if (!enemyVisible || pushOrdered)
                    {
                        SetAttackTactic();
                        BotLogicDecision moveDecision = enemyVisible ? BotLogicDecision.goToEnemy : pushDecision;
                        return CreatePushDecision(moveDecision);
                    }

                    if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                    {
                        CustomNavigationPoint? approachPoint = combatCommon.GetApproachableCover(true);
                        if (TryCreateApproachCoverDecision(approachPoint, out AICoreActionResultStruct<BotLogicDecision, GClass26> approachDecision))
                        {
                            return approachDecision;
                        }

                        return CreatePushDecision(BotLogicDecision.attackMoving);
                    }

                    if (distanceToEnemy == Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    return CreatePushDecision(BotLogicDecision.attackMoving);
                }

                // Push wanted but unsafe/imperfect conditions.
                if (enemyVisible)
                {
                    if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pushShootFromCover");
                    }

                    if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
                    }

                    SetAttackTactic();
                    return CreatePushDecision(BotLogicDecision.attackMoving);
                }

                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    return CreatePushDecision(BotLogicDecision.goToEnemy);
                }

                CustomNavigationPoint? blindApproach = combatCommon.GetApproachableCover(distanceToEnemy > Utils.Enemy.EnemyDistance.Mid);
                if (TryCreateApproachCoverDecision(blindApproach, out AICoreActionResultStruct<BotLogicDecision, GClass26> blindApproachDecision))
                {
                    return blindApproachDecision;
                }

                return combatCommon.EnemySearch("push.search", pushOrdered: pushOrdered);
            }

            // Old plugin "intimidation" fallback: maintain pressure from cover or hold lane.
            if (botOwner.Memory.IsInCover && botOwner.Memory.CurCustomCoverPoint?.CanIShootToEnemy == true)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, "pressureShootFromCover");
            }

            if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pressureDogFight");
            }

            if (!enemyVisible && Time.time - goalEnemy.PersonalLastSeenTime < UnityEngine.Random.Range(2f, 3f))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "pressureHold");
            }

            if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
            {
                Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
                Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
                float radius = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? 120f : 40f;
                CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(centerPosition, radius);
                if (TryCreateApproachCoverDecision(shootCover, out AICoreActionResultStruct<BotLogicDecision, GClass26> shootCoverDecision))
                {
                    return shootCoverDecision;
                }

                return combatCommon.EnemySearch("push.search");
            }

            return combatCommon.EnemySearch("push.search");
        }


        private bool TryCreateApproachCoverDecision(
            CustomNavigationPoint? cover,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (cover == null)
            {
                return false;
            }

            combatCommon.AssignCover(cover);
            decision = CreatePushDecision(BotLogicDecision.runToCover);
            return true;
        }

        private bool TryCreateMarksmanFightDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (combatCommon.CanShootFromCurrentCoverOrStandingIntent(out _))
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromCover,
                    "push.marksmanShootFromCover");
                return true;
            }

            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 centerPosition = (botOwner.Position + enemyAnchor) * 0.5f;
            CustomNavigationPoint? shootCover = combatCommon.GetClosestShootCover(
                centerPosition,
                160f,
                inbetween: false,
                maxDistanceFromBot: 120f,
                avoidCrossingEnemyFront: true);

            if (combatCommon.TryCommitSelectedCombatCover(goalEnemy, shootCover, "push.marksmanShootCover"))
            {
                decision = combatCommon.CreateCommittedCoverMoveDecision();
                return true;
            }

            if (goalEnemy.CanShoot)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    "push.marksmanShootFromPlace");
                return true;
            }

            return false;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> CreatePushDecision(BotLogicDecision action)
        {
            string suffix = action switch
            {
                BotLogicDecision.runToEnemy => "run",
                BotLogicDecision.goToEnemy => "goToEnemy",
                BotLogicDecision.attackMoving => "attackMoving",
                BotLogicDecision.attackMovingWithSuppress => "attackMovingSuppress",
                BotLogicDecision.runToCover => "runToCover",
                BotLogicDecision.goToPointTactical => "search",
                _ => action.ToString(),
            };

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(action, $"{PushReasonPrefix}{suffix}");
        }

        private void CommitPush(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            combatCommon.CommitPushDecision(decision);
            combatCommon.RefreshCommittedPushEnemyRetention();
            TryEmitPushEvent(decision);
        }

        private bool ShouldInterruptCommittedPush(EnemyInfo goalEnemy, out string reason)
        {
            reason = string.Empty;

            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) &&
                !combatCommon.TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                reason = "pushEnemyMissingOrDead";
                return true;
            }

            if (combatCommon.IsCommittedPushEnemyChanged(goalEnemy))
            {
                reason = "pushEnemyChanged";
                return true;
            }

            if (combatCommon.TryGetCommittedPushDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> committedPush) &&
                combatCommon.ShouldBreakCommittedPushForVisibility(
                    goalEnemy,
                    committedPush,
                    ref committedPushActionableVisibleSince))
            {
                // Fire-while-moving pushes are allowed to continue through brief visibility blips.
                // This break only fires after stable actionable visibility or for push actions that
                // cannot shoot while advancing.
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

        private bool ShouldEndRunToEnemyBecauseNotSprinting()
        {
            if (botOwner.Mover?.HasPathAndNoComplete != true)
            {
                runToEnemyNonSprintSince = 0f;
                return false;
            }

            if (botOwner.Mover.Sprinting)
            {
                runToEnemyNonSprintSince = 0f;
                return false;
            }

            if (runToEnemyNonSprintSince <= 0f)
            {
                runToEnemyNonSprintSince = Time.time;
                return false;
            }

            return Time.time - runToEnemyNonSprintSince >= RunToEnemyNonSprintGraceSeconds;
        }

        private void TryEmitPushEvent(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy) || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return;
            }

            boss.CombatEvents.TryEmitPush(
                botOwner,
                goalEnemy.ProfileId,
                FollowerCombatCommon.GetEnemyAnchor(goalEnemy),
                GetPushDestination(goalEnemy),
                decision.Reason ?? string.Empty,
                IsEnemySearchPushReason(decision.Reason));
        }

        private void ReleasePushEvent(string reason)
        {
            if (botOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                boss.CombatEvents.TryReleasePush(botOwner, reason);
            }
        }

        private Vector3 GetPushDestination(EnemyInfo goalEnemy)
        {
            CustomNavigationPoint? cover = botOwner.Memory?.CurCustomCoverPoint;
            if (cover != null)
            {
                return cover.Position;
            }

            return FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
        }

        private static bool IsEnemySearchPushReason(string? reason)
        {
            return IsStartWeakEnemyPushReason(reason) ||
                   string.Equals(reason, "push.search", StringComparison.Ordinal);
        }

        private static bool IsEnemyMarksman(EnemyInfo goalEnemy)
        {
            return goalEnemy.Person?.Profile?.Info?.Settings?.Role == WildSpawnType.marksman;
        }

        private void SetAttackTactic()
        {
            if (botOwner.Tactic.ShallReturnToAttack)
            {
                botOwner.Tactic.ShallReturnToAttack = false;
                botOwner.Tactic.ReturnToAttackTime = 0f;
            }

            botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
        }

    }
}
