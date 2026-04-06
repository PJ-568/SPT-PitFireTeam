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

        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions = new HashSet<BotLogicDecision>();

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

            bool isHealingAction = IsHealingDecision(currentDecision);
            bool regroupPending = HasPendingRegroupCommand();
            bool shouldDelayRegroupHandoff = regroupPending && ShouldDelayRegroupHandoff(isHealingAction);
            if (regroupPending && !shouldDelayRegroupHandoff)
            {
                return false;
            }

            bool isCombatActive = combatLogic.ShallUseNow();
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
            FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            combatLogic?.Reset();
            combatLogic?.StartDecision();
        }

        public override void Stop()
        {
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
                return new Action(typeof(CombatHoldPositionAction), "MissingCombatLogic", new FollowerCombatActionData(null));
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision = combatLogic.GetDecision();
            combatLogic.DecisionChanged(currentDecision, nextDecision);
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

            bool regroupPending = HasPendingRegroupCommand();
            bool shouldDelayRegroupHandoff = regroupPending && ShouldDelayRegroupHandoff(isHealingAction);

            if (regroupPending && !shouldDelayRegroupHandoff)
            {
                return true;
            }

            if (!combatLogic.ShallUseNow() && !isHealingAction && !shouldDelayRegroupHandoff)
            {
                return true;
            }

            if (!IsActive() && !isHealingAction && !shouldDelayRegroupHandoff)
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
            bool reasonChanged = currentDecision.Value.Reason != lastDecision.Value.Reason;
            bool reasonPolicyChanged = reasonChanged && ShouldCompareReasonForEndPolicy(currentDecision.Value.Action);

            return endResult.Value || actionChanged || reasonPolicyChanged;
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

        private static bool ShouldCompareReasonForEndPolicy(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.holdPosition ||
                   decision == BotLogicDecision.goToPointTactical;
        }

        private bool HasPendingRegroupCommand()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        private bool ShouldDelayRegroupHandoff(bool isHealingAction)
        {
            if (isHealingAction)
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            if (currentDecision.HasValue && IsActiveEngageDecision(currentDecision.Value.Action) && (goalEnemy.IsVisible || goalEnemy.CanShoot))
            {
                return true;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            return !goalEnemy.IsVisible && sinceLastSeen >= 1.5f;
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

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.Append(" brain=");
            stringBuilder.Append(brainShortName);
            stringBuilder.Append(" decision=");
            stringBuilder.Append(currentDecision?.Action.ToString() ?? "<none>");
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
            FollowerCombatActionData actionData = new FollowerCombatActionData(decision.Data);

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

    internal static class FollowerGroupSearchRuntime
    {
        private static readonly Dictionary<string, Dictionary<string, string>> LeaderProfileIdByBossAndEnemy =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public static BotOwner? GetCurrentLeader(pitAIBossPlayer boss, string? enemyProfileId)
        {
            if (boss?.realPlayer == null || string.IsNullOrEmpty(enemyProfileId))
            {
                return null;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            if (!LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? leadersByEnemy) ||
                leadersByEnemy == null ||
                !leadersByEnemy.TryGetValue(enemyProfileId!, out string? leaderProfileId) ||
                string.IsNullOrEmpty(leaderProfileId))
            {
                return null;
            }

            BotOwner? leader = BossPlayers.GetFollowerByProfileId(leaderProfileId)?.GetBot();
            if (leader == null || leader.IsDead)
            {
                leadersByEnemy.Remove(enemyProfileId!);
                if (leadersByEnemy.Count == 0)
                {
                    LeaderProfileIdByBossAndEnemy.Remove(bossProfileId);
                }
                return null;
            }

            return leader;
        }

        public static BotOwner? GetOrAssignLeader(pitAIBossPlayer boss, EnemyInfo goalEnemy, Func<BotOwner, EnemyInfo, bool> isValidCandidate)
        {
            if (boss?.realPlayer == null || goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return null;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            string enemyProfileId = goalEnemy.ProfileId;
            BotOwner? currentLeader = GetCurrentLeader(boss, enemyProfileId);

            if (currentLeader != null &&
                currentLeader.Memory?.GoalEnemy != null &&
                currentLeader.Memory.GoalEnemy.ProfileId == enemyProfileId &&
                CanOwnerLeadSearch(currentLeader) &&
                isValidCandidate(currentLeader, currentLeader.Memory.GoalEnemy))
            {
                return currentLeader;
            }

            BotOwner? bestLeader = null;
            bool bestHadPersonal = false;
            float bestAggression = float.MinValue;
            float bestPersonalSeenTime = float.MinValue;
            float bestDistanceSqr = float.MaxValue;

            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowersByBoss(bossProfileId))
            {
                BotOwner? owner = follower?.GetBot();
                EnemyInfo? ownerEnemy = owner?.Memory?.GoalEnemy;
                if (owner == null ||
                    ownerEnemy == null ||
                    ownerEnemy.ProfileId != enemyProfileId ||
                    !CanOwnerLeadSearch(owner) ||
                    !isValidCandidate(owner, ownerEnemy))
                {
                    continue;
                }

                bool hadPersonal = Time.time - ownerEnemy.PersonalLastSeenTime <= 12f;
                float aggression = GetOwnerAggression(owner);
                float personalSeenTime = ownerEnemy.PersonalLastSeenTime;
                float distanceSqr = (owner.Position - ownerEnemy.CurrPosition).sqrMagnitude;

                if (bestLeader == null ||
                    (hadPersonal && !bestHadPersonal) ||
                    (hadPersonal == bestHadPersonal && aggression > bestAggression + 0.01f) ||
                    (hadPersonal == bestHadPersonal &&
                     Mathf.Abs(aggression - bestAggression) <= 0.01f &&
                     personalSeenTime > bestPersonalSeenTime + 0.01f) ||
                    (hadPersonal == bestHadPersonal &&
                     Mathf.Abs(aggression - bestAggression) <= 0.01f &&
                     Mathf.Abs(personalSeenTime - bestPersonalSeenTime) <= 0.01f &&
                     distanceSqr < bestDistanceSqr))
                {
                    bestLeader = owner;
                    bestHadPersonal = hadPersonal;
                    bestAggression = aggression;
                    bestPersonalSeenTime = personalSeenTime;
                    bestDistanceSqr = distanceSqr;
                }
            }

            if (bestLeader == null)
            {
                if (LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? leadersByEnemy) &&
                    leadersByEnemy != null)
                {
                    leadersByEnemy.Remove(enemyProfileId);
                    if (leadersByEnemy.Count == 0)
                    {
                        LeaderProfileIdByBossAndEnemy.Remove(bossProfileId);
                    }
                }

                return null;
            }

            if (!LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? bossLeaders) ||
                bossLeaders == null)
            {
                bossLeaders = new Dictionary<string, string>(StringComparer.Ordinal);
                LeaderProfileIdByBossAndEnemy[bossProfileId] = bossLeaders;
            }

            bossLeaders[enemyProfileId] = bestLeader.ProfileId;
            return bestLeader;
        }

        private static bool CanOwnerLeadSearch(BotOwner owner)
        {
            return GetOwnerAggression(owner) > 0.01f;
        }

        private static float GetOwnerAggression(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return 50f;
            }

            return BossPlayers.GetFollowerByProfileId(owner.ProfileId)?.CombatAggression ?? 50f;
        }
    }

}


