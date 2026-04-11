using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatLayer : CustomLayer
    {
        private const float PostEnemyKeepActiveSeconds = 3f;
        private const string LingerReason = "linger";

        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions = new HashSet<BotLogicDecision>();
        private static readonly HashSet<string> ActiveFollowerCombatBots = new HashSet<string>(StringComparer.Ordinal);

        private readonly FollowerCombatLogicBase? combatLogic;
        private readonly string brainShortName;

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? currentDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? lastDecision;
        private bool hadCombatSinceActivation;
        private float noEnemySinceTime;

        public FollowerCombatLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            brainShortName = botOwner?.Brain?.BaseBrain?.ShortName() ?? string.Empty;
            combatLogic = CreateCombatLogic(BotOwner, brainShortName);
        }

        public override string GetName()
        {
            return "friendlySAIN.FollowerCombat";
        }

        public override bool IsActive()
        {
            if (friendlySAIN.UseSainFollowerCombat || BotOwner == null || combatLogic == null)
            {
                return false;
            }

            if (BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss || BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer)
            {
                return false;
            }

            bool isCombatActive = ShouldTreatCombatAsActive();
            if (isCombatActive)
            {
                hadCombatSinceActivation = true;
                noEnemySinceTime = 0f;
                return true;
            }

            if (!hadCombatSinceActivation)
            {
                return false;
            }

            if (noEnemySinceTime <= 0f)
            {
                noEnemySinceTime = Time.time;
            }

            if (Time.time - noEnemySinceTime < PostEnemyKeepActiveSeconds)
            {
                return true;
            }

            hadCombatSinceActivation = false;
            noEnemySinceTime = 0f;
            return false;
        }

        public override void Start()
        {
            base.Start();
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            noEnemySinceTime = 0f;
            MarkActive(true);
            BotOwner?.GetPlayer?.MovementContext?.SetPatrol(false);
            ClearFollowerCommandOnCombatTransition("CombatLayer:Start");
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic?.Reset();
            combatLogic?.StartDecision();
        }

        public override void Stop()
        {
            MarkActive(false);
            ClearFollowerCommandOnCombatTransition("CombatLayer:Stop");
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            noEnemySinceTime = 0f;
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic?.Reset();
            base.Stop();
        }

        public override Action GetNextAction()
        {
            lastDecision = currentDecision;

            if (combatLogic == null)
            {
                return new Action(
                    typeof(CombatHoldPositionAction),
                    "MissingCombatLogic",
                    new FollowerCombatActionData(BotLogicDecision.holdPosition, "MissingCombatLogic", null));
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision;
            if (!ShouldTreatCombatAsActive())
            {
                // As soon as live enemy is gone, hand off to a short linger hold while the
                // combat layer remains active for release/handoff timing.
                nextDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, LingerReason);
            }
            else
            {
                nextDecision = combatLogic.GetDecision();
                combatLogic.DecisionChanged(currentDecision, nextDecision);
            }

            currentDecision = nextDecision;
            return CreateBigBrainAction(nextDecision);
        }

        public override bool IsCurrentActionEnding()
        {
            if (combatLogic == null || currentDecision == null)
            {
                return true;
            }

            bool isHealingAction = IsHealingDecision(currentDecision);

            if (currentDecision.Value.Reason != LingerReason && !ShouldTreatCombatAsActive() && !isHealingAction)
            {
                return true;
            }

            // Linger hold: layer is active but no live enemy. End immediately if combat becomes live
            // again; otherwise end when the linger window expires.
            if (currentDecision.HasValue && currentDecision.Value.Reason == LingerReason)
            {
                if (ShouldTreatCombatAsActive())
                {
                    return true;
                }

                return !IsActive();
            }

            if (!IsActive() && !isHealingAction)
            {
                return true;
            }

            // The concrete logic decides end conditions; it may delegate to shared common logic.
            AICoreActionEndStruct endResult = combatLogic.ShallEndCurrentDecision(currentDecision.Value);
            if (endResult.Value &&
                (currentDecision.Value.Action == BotLogicDecision.runToCover ||
                 currentDecision.Value.Action == BotLogicDecision.runToEnemy))
            {
                BotOwner.BotRun.EndMove();
            }

            if (!lastDecision.HasValue || !currentDecision.HasValue)
            {
                return endResult.Value;
            }

            bool actionChanged = currentDecision.Value.Action != lastDecision.Value.Action;
            return endResult.Value || actionChanged;
        }

        public static bool IsFollowerCombatLayerActive(BotOwner? botOwner)
        {
            return botOwner != null
                && !string.IsNullOrEmpty(botOwner.ProfileId)
                && ActiveFollowerCombatBots.Contains(botOwner.ProfileId);
        }

        private void MarkActive(bool active)
        {
            if (string.IsNullOrEmpty(BotOwner?.ProfileId))
            {
                return;
            }

            if (active)
            {
                ActiveFollowerCombatBots.Add(BotOwner.ProfileId);
            }
            else
            {
                ActiveFollowerCombatBots.Remove(BotOwner.ProfileId);
            }
        }

        private bool HasLiveEnemy()
        {
            return combatLogic?.ShallUseNow() == true;
        }

        private bool ShouldTreatCombatAsActive()
        {
            if (HasLiveEnemy())
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy != null && goalEnemy.Person?.HealthController?.IsAlive == true)
            {
                if (BotOwner.Memory.HaveEnemy)
                {
                    return true;
                }

                if (goalEnemy.IsVisible || goalEnemy.CanShoot)
                {
                    return true;
                }

                if (currentDecision.HasValue && IsCombatContinuationDecision(currentDecision.Value.Action))
                {
                    return true;
                }
            }

            return BotOwner?.Memory?.IsUnderFire == true &&
                   Time.time - BotOwner.Memory.LastTimeHit <= 2f;
        }

        private static bool IsHealingDecision(AICoreActionResultStruct<BotLogicDecision, GClass26>? decision)
        {
            if (!decision.HasValue)
            {
                return false;
            }

            return decision.Value.Action == BotLogicDecision.heal ||
                   decision.Value.Action == BotLogicDecision.healStimulators;
        }

        private void ClearFollowerCommandOnCombatTransition(string reason)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return;
            }

            if (!followerData.TryGetActiveCommand(out _, out _))
            {
                return;
            }

            followerData.ClearCommand(reason);
        }

        private static bool IsActiveEngageDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.dogFight ||
                   decision == BotLogicDecision.shootFromPlace ||
                   decision == BotLogicDecision.shootFromCover ||
                   decision == BotLogicDecision.suppressFire ||
                   decision == BotLogicDecision.goToEnemy ||
                   decision == BotLogicDecision.runToEnemy;
        }

        private static bool IsCombatContinuationDecision(BotLogicDecision decision)
        {
            return IsActiveEngageDecision(decision) ||
                   decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == BotLogicDecision.goToCoverPoint ||
                   decision == BotLogicDecision.goToCoverPointTactical;
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.Append(" brain=");
            stringBuilder.Append(brainShortName);
            stringBuilder.Append(" decision=");
            stringBuilder.Append(currentDecision?.Action.ToString() ?? "<none>");
            stringBuilder.Append(" reason=");
            stringBuilder.Append(currentDecision?.Reason ?? "<none>");

            if (BotOwner?.BotFollower?.BossToFollow != null)
            {
                Vector3 bossPosition = BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss && boss.realPlayer != null
                    ? boss.realPlayer.Transform.position
                    : BotOwner.BotFollower.BossToFollow.Position;
                float bossNavDistance = Utils.Utils.GetNavDistance(BotOwner.Position, bossPosition);
                stringBuilder.Append(" bossNav=");
                stringBuilder.Append(bossNavDistance.ToString("F1"));
            }
        }

        private static FollowerCombatLogicBase? CreateCombatLogic(BotOwner botOwner, string shortName)
        {
            if (botOwner == null)
            {
                return null;
            }

            return shortName switch
            {
                "PmcBear" => new StandardFollowerPmcCombatLogic(botOwner),
                "PmcUsec" => new StandardFollowerPmcCombatLogic(botOwner),
                "PMC" => new StandardFollowerPmcCombatLogic(botOwner),
                "ExUsec" => new StandardFollowerPmcCombatLogic(botOwner),
                _ => CreateCombatLogicByRole(botOwner),
            };
        }

        private static FollowerCombatLogicBase? CreateCombatLogicByRole(BotOwner botOwner)
        {
            WildSpawnType role = botOwner.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;

            return role switch
            {
                WildSpawnType.pmcBEAR => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.pmcUSEC => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.pmcBot => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.exUsec => new StandardFollowerPmcCombatLogic(botOwner),
                _ => null,
            };
        }

        private static Action CreateBigBrainAction(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            FollowerCombatActionData actionData = new FollowerCombatActionData(decision.Action, decision.Reason, decision.Data);

            switch (decision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return new Action(typeof(CombatHoldPositionAction), decision.Reason, actionData);
                case BotLogicDecision.runToCover:
                    return new Action(typeof(CombatRunToCoverAction), decision.Reason, actionData);
                case BotLogicDecision.attackMoving:
                    return new Action(typeof(CombatAttackMovingAction), decision.Reason, actionData);
                case BotLogicDecision.attackMovingWithSuppress:
                    return new Action(typeof(CombatAttackMovingWithSuppressAction), decision.Reason, actionData);
                case BotLogicDecision.dogFight:
                    return new Action(typeof(CombatDogFightAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromPlace:
                    return new Action(typeof(CombatShootFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromCover:
                    return new Action(typeof(CombatShootFromCoverAction), decision.Reason, actionData);
                case BotLogicDecision.goToEnemy:
                    return new Action(typeof(CombatGoToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.runToEnemy:
                    return new Action(typeof(CombatRunToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.goToPoint:
                    if (FollowerCombatRegroupObjective.IsRunReason(decision.Reason))
                    {
                        return new Action(typeof(CombatRegroupRunAction), decision.Reason, actionData);
                    }

                    return new Action(typeof(CombatGoToPointAction), decision.Reason, actionData);
                case BotLogicDecision.goToPointTactical:
                    return new Action(typeof(CombatGoToPointTacticalAction), decision.Reason, actionData);
                case BotLogicDecision.heal:
                    return new Action(typeof(HealAction), decision.Reason, actionData);
                case BotLogicDecision.healStimulators:
                    return new Action(typeof(HealStimulatorsAction), decision.Reason, actionData);
                case BotLogicDecision.search:
                    return new Action(typeof(CombatGoToPointTacticalAction), decision.Reason, actionData);
                case BotLogicDecision.throwGrenadeFromPlace:
                    return new Action(typeof(CombatThrowGrenadeFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.suppressFire:
                    return new Action(typeof(CombatSuppressFireAction), decision.Reason, actionData);
                case BotLogicDecision.shootToSmoke:
                    return new Action(typeof(CombatShootToSmokeAction), decision.Reason, actionData);
                case BotLogicDecision.goToCoverPoint:
                    return new Action(typeof(GoToCoverPointAction), decision.Reason, actionData);
                default:
                    if (LoggedUnsupportedDecisions.Add(decision.Action))
                    {
                        Modules.Logger.LogError($"[FollowerCombat] Unsupported decision '{decision.Action}', falling back to hold.");
                    }

                    return new Action(typeof(CombatHoldPositionAction), $"Unsupported:{decision.Action}", actionData);
            }
        }

    }

}


