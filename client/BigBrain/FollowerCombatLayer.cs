using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatLayer : CustomLayer
    {
        private const float PostEnemyKeepActiveSeconds = 3f;
        private const string LingerReason = "linger";

        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions = new HashSet<BotLogicDecision>();
        private static readonly HashSet<string> ActiveFollowerCombatBots = new HashSet<string>(StringComparer.Ordinal);

        private FollowerCombatLogicBase? combatLogic;
        private readonly string brainShortName;

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? currentDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? lastDecision;
        private bool hadCombatSinceActivation;
        private float lingerUntil;
        private bool lingerArmed;
        private float lingerHardUntil;
        private bool combatLogicResetForInactive;

        public FollowerCombatLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            brainShortName = botOwner?.Brain?.BaseBrain?.ShortName() ?? string.Empty;
            combatLogic = CreateCombatLogic(BotOwner, brainShortName);
        }

        public override string GetName()
        {
            return "pitTeam.FollowerCombat";
        }

        public override bool IsActive()
        {
            if (pitFireTeam.UseSainFollowerCombat || BotOwner == null || combatLogic == null)
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

            if (lingerArmed && IsLingerExpired() && !HasCurrentLiveGoalEnemy())
            {
                hadCombatSinceActivation = false;
                ClearLinger();
                return false;
            }

            bool isCombatActive = ShouldTreatCombatAsActive();
            if (isCombatActive)
            {
                hadCombatSinceActivation = true;
                if (HasCurrentLiveGoalEnemy())
                {
                    ClearLinger();
                }

                return true;
            }

            if (HasPendingMedicalWork())
            {
                hadCombatSinceActivation = false;
                ClearLinger();
                return false;
            }

            if (!hadCombatSinceActivation)
            {
                return false;
            }

            ArmLingerIfNeeded();
            if (Time.time < lingerUntil)
            {
                return true;
            }

            hadCombatSinceActivation = false;
            ClearLinger();
            return false;
        }

        public override void Start()
        {
            base.Start();
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            combatLogicResetForInactive = false;
            ClearLinger();
            MarkActive(true);
            BotOwner?.GetPlayer?.MovementContext?.SetPatrol(false);
            ClearFollowerCommandOnCombatTransition("CombatLayer:Start");
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic = CreateCombatLogic(BotOwner, brainShortName);
            combatLogic?.Reset();
            combatLogic?.StartDecision();
            BattleRecorder.RecordCombatLayerState(BotOwner, true, "layerStart");
        }

        public override void Stop()
        {
            BattleRecorder.RecordCombatLayerState(BotOwner, false, "layerStop");
            MarkActive(false);
            BossPlayers.Instance?.GetFollower(BotOwner)?.ClearTemporaryCombatAggressionOverride();
            ClearFollowerCommandOnCombatTransition("CombatLayer:Stop");
            currentDecision = null;
            lastDecision = null;
            hadCombatSinceActivation = false;
            combatLogicResetForInactive = false;
            ClearLinger();
            FollowerContactEnemyRetention.Clear(BotOwner);
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
                BossPlayers.Instance?.GetFollower(BotOwner)?.ClearTemporaryCombatAggressionOverride();
                if (!combatLogicResetForInactive)
                {
                    combatLogic.Reset();
                    combatLogicResetForInactive = true;
                }

                ArmLingerIfNeeded();
                nextDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, LingerReason);
            }
            else
            {
                if (combatLogicResetForInactive)
                {
                    combatLogicResetForInactive = false;
                    combatLogic.StartDecision();
                }

                if (HasCurrentLiveGoalEnemy())
                {
                    ClearLinger();
                }

                nextDecision = combatLogic.GetDecision();
                combatLogic.DecisionChanged(currentDecision, nextDecision);
            }

            currentDecision = nextDecision;
            BattleRecorder.RecordDecisionSelected(BotOwner, lastDecision, nextDecision, combatLogic?.GetCurrentObjectiveName());
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
                    if (HasCurrentLiveGoalEnemy())
                    {
                        ClearLinger();
                    }

                    return true;
                }

                if (HasPendingMedicalWork())
                {
                    hadCombatSinceActivation = false;
                    ClearLinger();
                    return true;
                }

                ArmLingerIfNeeded();
                bool expired = IsLingerExpired();
                if (expired)
                {
                    hadCombatSinceActivation = false;
                    ClearLinger();
                }

                return expired;
            }

            if (!IsActive() && !isHealingAction)
            {
                return true;
            }

            // The concrete logic decides end conditions; it may delegate to shared common logic.
            AICoreActionEndStruct endResult = combatLogic.ShallEndCurrentDecision(currentDecision.Value);
            if (endResult.Value)
            {
                BattleRecorder.RecordDecisionEnd(BotOwner, currentDecision.Value, endResult, combatLogic.GetCurrentObjectiveName());
            }

            if (endResult.Value &&
                (currentDecision.Value.Action == BotLogicDecision.runToCover ||
                 currentDecision.Value.Action == BotLogicDecision.runToEnemy))
            {
                BotOwner.BotRun.EndMove();
            }

            return endResult.Value;
        }

        private void ArmLingerIfNeeded()
        {
            if (lingerArmed)
            {
                return;
            }

            lingerUntil = Time.time + PostEnemyKeepActiveSeconds;
            lingerHardUntil = lingerUntil;
            lingerArmed = true;
        }

        private void ClearLinger()
        {
            lingerUntil = 0f;
            lingerHardUntil = 0f;
            lingerArmed = false;
        }

        private bool IsLingerExpired()
        {
            if (lingerHardUntil > 0f && Time.time >= lingerHardUntil)
            {
                return true;
            }

            return lingerUntil <= 0f || Time.time >= lingerUntil;
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

        private bool HasCurrentLiveGoalEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            return IsGoalEnemyAlive(goalEnemy) &&
                   (BotOwner?.Memory?.HaveEnemy == true || goalEnemy!.IsVisible || goalEnemy.CanShoot);
        }

        private bool ShouldTreatCombatAsActive()
        {
            if (FollowerContactEnemyRetention.TryRestore(BotOwner, out _))
            {
                return true;
            }

            if (HasLiveEnemy())
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner?.Memory?.GoalEnemy;
            if (goalEnemy != null && IsGoalEnemyAlive(goalEnemy))
            {
                if (IsActiveFollowerSuppressContinuation())
                {
                    return true;
                }

                if (BotOwner.Memory.HaveEnemy)
                {
                    return true;
                }

                if (goalEnemy.IsVisible || goalEnemy.CanShoot)
                {
                    return true;
                }

                if (currentDecision.HasValue && IsMovementContinuationDecision(currentDecision.Value.Action))
                {
                    return true;
                }
            }

            return BotOwner?.Memory?.IsUnderFire == true &&
                   Time.time - BotOwner.Memory.LastTimeHit <= 2f;
        }

        private static bool IsGoalEnemyAlive(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
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

        private bool HasPendingMedicalWork()
        {
            return BotOwner?.Medecine != null &&
                   (BotOwner.Medecine.FirstAid?.Have2Do == true ||
                    BotOwner.Medecine.SurgicalKit?.HaveWork == true ||
                    BotOwner.Medecine.FirstAid?.Using == true ||
                    BotOwner.Medecine.SurgicalKit?.Using == true);
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

            if (!followerData.TryGetActiveCommand(out FollowerCommandType command, out _))
            {
                return;
            }

            if (reason == "CombatLayer:Start" &&
                (command == FollowerCommandType.PushEnemy ||
                 command == FollowerCommandType.SuppressEnemy ||
                 command == FollowerCommandType.RegroupNearBoss ||
                 command == FollowerCommandType.NeedSniper))
            {
                return;
            }

            followerData.ClearCommand(reason);
        }

        private static bool IsMovementContinuationDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.goToEnemy ||
                   decision == BotLogicDecision.runToEnemy ||
                   decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == BotLogicDecision.suppressFire ||
                   decision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision == BotLogicDecision.goToCoverPoint ||
                   decision == BotLogicDecision.goToCoverPointTactical;
        }

        private bool IsActiveFollowerSuppressContinuation()
        {
            if (!currentDecision.HasValue ||
                currentDecision.Value.Action != BotLogicDecision.suppressFire ||
                !FollowerCombatCommon.IsFollowerSuppressReason(currentDecision.Value.Reason))
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null ||
                !followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) ||
                command != FollowerCommandType.SuppressEnemy)
            {
                return FollowerCombatCommon.IsAutoSuppressReason(currentDecision.Value.Reason);
            }

            return true;
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

        private static FollowerCombatLogicBase Create(BotOwner botOwner)
        {
            BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(botOwner);
            FollowerCombatTactic tactic = follower?.CombatTactic ?? FollowerCombatTactic.Balanced;
            return tactic switch
            {
                FollowerCombatTactic.Balanced => new FollowerPmcCombatLogic(botOwner),
                // Protector currently uses the default PMC objective until its own objective is implemented.
                FollowerCombatTactic.Protector => new FollowerPmcCombatLogic(botOwner),
                FollowerCombatTactic.Marksman => new FollowerSniperCombatLogic(botOwner),
                _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        private static FollowerCombatLogicBase? CreateCombatLogic(BotOwner botOwner, string shortName)
        {
            if (botOwner == null)
            {
                return null;
            }

            return shortName switch
            {
                "PmcBear" => Create(botOwner),
                "PmcUsec" => Create(botOwner),
                "PMC" => Create(botOwner),
                "ExUsec" => Create(botOwner),
                _ => CreateCombatLogicByRole(botOwner),
            };
        }

        private static FollowerCombatLogicBase? CreateCombatLogicByRole(BotOwner botOwner)
        {
            WildSpawnType role = botOwner.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;

            return role switch
            {
                WildSpawnType.pmcBEAR => Create(botOwner),
                WildSpawnType.pmcUSEC => Create(botOwner),
                WildSpawnType.pmcBot => Create(botOwner),
                WildSpawnType.exUsec => Create(botOwner),
                _ => null,
            };
        }

        private Action CreateBigBrainAction(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            FollowerCombatActionData actionData = new FollowerCombatActionData(decision.Action, decision.Reason, decision.Data);

            if (decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                return new Action(typeof(CombatAttackRetreatAction), decision.Reason, actionData);
            }

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
                    BotOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                    return new Action(typeof(CombatSearchAction), decision.Reason, actionData);
                case BotLogicDecision.suppressGrenade:
                    return new Action(typeof(CombatSuppressGrenadeAction), decision.Reason, actionData);
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


