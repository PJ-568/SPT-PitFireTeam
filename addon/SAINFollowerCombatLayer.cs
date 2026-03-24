using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Extensions;
using SAIN.Layers;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.Decision;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINFollowerCombatLayer : SAINLayer
    {
        public static readonly string Name = BuildLayerName("Follower Combat Layer");
        private static readonly HashSet<string> ActiveFollowerCombatBots = new HashSet<string>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> ActiveFollowerCombatSeenAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> NextDecisionFallthroughLogAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> NextDecisionBranchLogAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float ActiveFollowerCombatStaleSeconds = 2f;
        private static readonly System.Type SearchActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.SearchAction");
        private static readonly System.Type RushEnemyActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.RushEnemyAction");
        private static readonly Dictionary<string, float> RegroupCommandLatchUntilByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float RegroupCommandLatchSeconds = 8f;
        private const float SquadDecisionNoEnemyGraceSeconds = 2f;
        private const float SelfActionTransitionGraceSeconds = 1.25f;
        private string? _groupSearchLockEnemyProfileId;
        private bool _currentUsesDefaultBossAction;
        private bool _lastActionUsedDefaultBossAction;
        private ESquadDecision _currentDecision = ESquadDecision.None;
        private ESquadDecision _lastActionDecision = ESquadDecision.None;
        private float _lastEnemySeenAt = -1000f;

        public SAINFollowerCombatLayer(BotOwner botOwner, int priority)
            : base(botOwner, priority, Name, ESAINLayer.Squad)
        {
        }

        public override bool IsActive()
        {
            bool active = TryEvaluateFollowerDecision(out _currentDecision);
            if (!active || _currentDecision != ESquadDecision.GroupSearch)
            {
                ReleaseGroupSearchLock();
            }
            MarkActive(active);
            CheckActiveChanged(active);
            return active;
        }

        public override Action GetNextAction()
        {
            _lastActionDecision = _currentDecision;
            _lastActionUsedDefaultBossAction = _currentUsesDefaultBossAction;
            switch (_lastActionDecision)
            {
                case ESquadDecision.Regroup:
                    if (_currentUsesDefaultBossAction)

                        return new Action(typeof(SAINFollowerCombatDefaultBossAction), "PROTECTDELTA");
                    else
                        return new Action(typeof(SAINFollowerCombatRegroupAction), $"{_lastActionDecision}");

                case ESquadDecision.Suppress:
                    return new Action(typeof(SAINFollowerCombatSuppressAction), $"{_lastActionDecision}");

                case ESquadDecision.Search:
                case ESquadDecision.Help:
                    return new Action(ResolveSearchActionType(), $"{_lastActionDecision}");

                case ESquadDecision.GroupSearch:
                    if (BotOwner?.BotFollower?.BossToFollow is pitAIBossPlayer boss &&
                        Bot?.GoalEnemy?.EnemyPlayer?.ProfileId is string enemyProfileId &&
                        SAINFollowerRuntimeBridge.IsSearchPartyLeader(boss, BotOwner, enemyProfileId))
                    {
                        return new Action(ResolveSearchActionType(), $"{_lastActionDecision} : Party Leader Search");
                    }

                    return new Action(typeof(SAINFollowerCombatFollowBossSearchAction), $"{_lastActionDecision} : Follow Boss Search");

                case ESquadDecision.PushSuppressedEnemy:
                    return new Action(ResolveRushEnemyActionType(), $"{_lastActionDecision}");

                default:
                    return new Action(typeof(SAINFollowerCombatDefaultBossAction), "PROTECTDELTA");
            }
        }

        public override bool IsCurrentActionEnding()
        {
            if (base.IsCurrentActionEnding())
            {
                if (_lastActionDecision == ESquadDecision.GroupSearch)
                {
                    ReleaseGroupSearchLock();
                }
                return true;
            }

            if (!TryEvaluateFollowerDecision(out ESquadDecision nextDecision))
            {
                if (_lastActionDecision == ESquadDecision.GroupSearch)
                {
                    ReleaseGroupSearchLock();
                }
                return true;
            }

            bool ending = nextDecision != _lastActionDecision ||
                          _currentUsesDefaultBossAction != _lastActionUsedDefaultBossAction;
            if (ending && _lastActionDecision == ESquadDecision.GroupSearch)
            {
                ReleaseGroupSearchLock();
            }

            return ending;
        }

        public static bool IsFollowerCombatLayerActive(BotOwner? botOwner)
        {
            if (botOwner == null || string.IsNullOrEmpty(botOwner.ProfileId))
            {
                return false;
            }

            string profileId = botOwner.ProfileId;
            if (!ActiveFollowerCombatBots.Contains(profileId))
            {
                return false;
            }

            if (!ActiveFollowerCombatSeenAtByBot.TryGetValue(profileId, out float seenAt))
            {
                ActiveFollowerCombatBots.Remove(profileId);
                return false;
            }

            if (Time.time - seenAt > ActiveFollowerCombatStaleSeconds)
            {
                ActiveFollowerCombatBots.Remove(profileId);
                ActiveFollowerCombatSeenAtByBot.Remove(profileId);
                return false;
            }

            return true;
        }

        private bool TryEvaluateFollowerDecision(out ESquadDecision decision)
        {
            decision = ESquadDecision.None;
            _currentUsesDefaultBossAction = false;
            if (!BotOwner.IsBotActive())
            {
                return false;
            }

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            if (!GetBotComponent())
            {
                return false;
            }

            BotComponent bot = Bot;
            if (bot == null || !bot.BotActive)
            {
                return false;
            }

            SAINDecisionClass decisions = bot.Decision;
            if (decisions.CurrentSelfDecision != ESelfActionType.None || decisions.CurrentCombatDecision == ECombatDecision.DogFight)
            {
                TryLogDecisionBranch(
                    "EarlyExit:SelfOrDogFight",
                    bot,
                    decisions,
                    $"self={decisions.CurrentSelfDecision} combat={decisions.CurrentCombatDecision}");
                return false;
            }

            if (ShouldDeferToSoloSelfAction(bot, decisions))
            {
                TryLogDecisionBranch(
                    "EarlyExit:SoloSelfDefer",
                    bot,
                    decisions,
                    $"self={decisions.CurrentSelfDecision} prevSelf={decisions.PreviousSelfDecision} combat={decisions.CurrentCombatDecision} prevCombat={decisions.PreviousCombatDecision}");
                return false;
            }

            int knownEnemyCount = bot.EnemyController?.KnownEnemies?.Count ?? 0;
            bool haveEnemy = BotOwner.Memory?.HaveEnemy == true;
            bool haveSainEnemy = bot.GoalEnemy != null || knownEnemyCount > 0;
            bool haveSainCombatContext =
                haveEnemy ||
                haveSainEnemy ||
                bot.BotActivation?.BotInCombat == true ||
                bot.ActiveLayer == ESAINLayer.Squad ||
                bot.ActiveLayer == ESAINLayer.Combat;

            if (haveSainCombatContext)
            {
                _lastEnemySeenAt = Time.time;
            }

            if (TryHandleRegroupCommand(out decision))
            {
                _currentUsesDefaultBossAction = false;
                return true;
            }

            if (SAINFollowerSquadDecisionCalculator.TryGetDecision(BotOwner, bot, out decision) && decision != ESquadDecision.None)
            {
                if (decision == ESquadDecision.GroupSearch)
                {
                    _groupSearchLockEnemyProfileId = bot.GoalEnemy?.EnemyPlayer?.ProfileId;
                }
                else if (decision == ESquadDecision.Regroup)
                {
                    _currentUsesDefaultBossAction = true;
                }

                return true;
            }

            // Fallback: if SAIN already has a squad decision, run follower layer and map leader/member-sensitive actions to boss/followers.
            if (decisions.CurrentSquadDecision != ESquadDecision.None)
            {
                bool recentEnemyContext = Time.time - _lastEnemySeenAt <= SquadDecisionNoEnemyGraceSeconds;
                if (!haveSainCombatContext && !recentEnemyContext)
                {
                    return false;
                }

                decision = decisions.CurrentSquadDecision;
                _currentUsesDefaultBossAction = decision == ESquadDecision.Regroup;

                return true;
            }

            if (haveSainCombatContext)
            {
                decision = ESquadDecision.Regroup;
                _currentUsesDefaultBossAction = true;
                return true;
            }

            TryLogDecisionFallthrough(bot, decisions);

            return false;
        }

        private void ReleaseGroupSearchLock()
        {
            if (string.IsNullOrEmpty(_groupSearchLockEnemyProfileId) ||
                BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                string.IsNullOrEmpty(BotOwner?.ProfileId))
            {
                _groupSearchLockEnemyProfileId = null;
                return;
            }

            SAINFollowerRuntimeBridge.UnlockSearchPartyLeader(boss, _groupSearchLockEnemyProfileId, BotOwner.ProfileId);
            _groupSearchLockEnemyProfileId = null;
        }

        private void TryLogDecisionFallthrough(BotComponent bot, SAINDecisionClass decisions)
        {
            string profileId = BotOwner?.ProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (NextDecisionFallthroughLogAtByBot.TryGetValue(profileId, out float nextAt) && Time.time < nextAt)
            {
                return;
            }

            NextDecisionFallthroughLogAtByBot[profileId] = Time.time + 1f;

            int knownEnemyCount = bot?.EnemyController?.KnownEnemies?.Count ?? -1;
            int enemiesArrayCount = bot?.EnemyController?.EnemiesArray?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[CombatLayerDebug] follower={BotOwner.Profile?.Nickname ?? BotOwner.name}[{profileId}] " +
                $"haveEnemy={BotOwner.Memory?.HaveEnemy == true} " +
                $"memoryGoalEnemy={BotOwner.Memory?.GoalEnemy?.ProfileId ?? "<null>"} " +
                $"botActive={bot?.BotActive == true} " +
                $"botInCombat={bot?.BotActivation?.BotInCombat == true} " +
                $"activeLayer={bot?.ActiveLayer.ToString() ?? "<null>"} " +
                $"combat={decisions?.CurrentCombatDecision.ToString() ?? "<null>"} " +
                $"squad={decisions?.CurrentSquadDecision.ToString() ?? "<null>"} " +
                $"self={decisions?.CurrentSelfDecision.ToString() ?? "<null>"} " +
                $"goalEnemy={bot?.GoalEnemy?.EnemyPlayer?.ProfileId ?? "<null>"} " +
                $"goalVisible={bot?.GoalEnemy?.IsVisible == true} " +
                $"knownEnemies={knownEnemyCount} enemiesArray={enemiesArrayCount} " +
                $"lastEnemySeenAgo={(Time.time - _lastEnemySeenAt):0.00}");
        }

        private void TryLogDecisionBranch(string branch, BotComponent bot, SAINDecisionClass decisions, string extra)
        {
            string profileId = BotOwner?.ProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (NextDecisionBranchLogAtByBot.TryGetValue(profileId, out float nextAt) && Time.time < nextAt)
            {
                return;
            }

            NextDecisionBranchLogAtByBot[profileId] = Time.time + 1f;

            int knownEnemyCount = bot?.EnemyController?.KnownEnemies?.Count ?? -1;
            int enemiesArrayCount = bot?.EnemyController?.EnemiesArray?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[CombatLayerBranchDebug] branch={branch} follower={BotOwner.Profile?.Nickname ?? BotOwner.name}[{profileId}] " +
                $"haveEnemy={BotOwner.Memory?.HaveEnemy == true} " +
                $"memoryGoalEnemy={BotOwner.Memory?.GoalEnemy?.ProfileId ?? "<null>"} " +
                $"botActive={bot?.BotActive == true} " +
                $"botInCombat={bot?.BotActivation?.BotInCombat == true} " +
                $"activeLayer={bot?.ActiveLayer.ToString() ?? "<null>"} " +
                $"combat={decisions?.CurrentCombatDecision.ToString() ?? "<null>"} " +
                $"squad={decisions?.CurrentSquadDecision.ToString() ?? "<null>"} " +
                $"self={decisions?.CurrentSelfDecision.ToString() ?? "<null>"} " +
                $"prevSelf={decisions?.PreviousSelfDecision.ToString() ?? "<null>"} " +
                $"prevCombat={decisions?.PreviousCombatDecision.ToString() ?? "<null>"} " +
                $"goalEnemy={bot?.GoalEnemy?.EnemyPlayer?.ProfileId ?? "<null>"} " +
                $"goalVisible={bot?.GoalEnemy?.IsVisible == true} " +
                $"knownEnemies={knownEnemyCount} enemiesArray={enemiesArrayCount} " +
                $"lastEnemySeenAgo={(Time.time - _lastEnemySeenAt):0.00} " +
                $"{extra}");
        }

        private bool TryHandleRegroupCommand(out ESquadDecision decision)
        {
            decision = ESquadDecision.None;

            BotFollowerPlayer? follower = BossPlayers.Instance?.GetFollower(BotOwner);
            bool hasRegroupCommand = follower != null
                && follower.TryGetActiveCommand(out FollowerCommandType command, out _)
                && command == FollowerCommandType.RegroupNearBoss;

            string? botId = BotOwner?.ProfileId;
            if (!hasRegroupCommand || string.IsNullOrEmpty(botId))
            {
                if (!string.IsNullOrEmpty(botId))
                {
                    RegroupCommandLatchUntilByBot.Remove(botId);
                }
                return false;
            }

            bool haveEnemy = BotOwner.Memory?.HaveEnemy == true;
            if (haveEnemy)
            {
                RegroupCommandLatchUntilByBot[botId] = Time.time + RegroupCommandLatchSeconds;
            }
            else if (!RegroupCommandLatchUntilByBot.TryGetValue(botId, out float until) || Time.time > until)
            {
                return false;
            }

            decision = ESquadDecision.Regroup;
            return true;
        }

        private static bool ShouldDeferToSoloSelfAction(BotComponent bot, SAINDecisionClass decisions)
        {
            if (bot == null || decisions == null)
            {
                return false;
            }

            if (decisions.CurrentCombatDecision == ECombatDecision.Retreat)
            {
                return true;
            }

            bool selfProtectiveAction =
                decisions.CurrentSelfDecision == ESelfActionType.Reload ||
                decisions.CurrentSelfDecision == ESelfActionType.FirstAid ||
                decisions.CurrentSelfDecision == ESelfActionType.Surgery;

            bool runtimeProtectiveAction =
                bot.BotOwner?.WeaponManager?.Reload?.Reloading == true ||
                bot.BotOwner?.Medecine?.Using == true ||
                bot.Medical?.Surgery?.SurgeryStarted == true;

            // Plain SeekCover with no self-action context drifts followers away from boss.
            // Keep SAIN solo SeekCover only for protective contexts (reload/med/surgery).
            if (decisions.CurrentCombatDecision == ECombatDecision.SeekCover)
            {
                return selfProtectiveAction || runtimeProtectiveAction;
            }

            if (runtimeProtectiveAction)
            {
                return true;
            }

            if (decisions.PreviousSelfDecision == ESelfActionType.None || decisions.TimeSinceChangeDecision > SelfActionTransitionGraceSeconds)
            {
                return false;
            }

            return decisions.PreviousCombatDecision == ECombatDecision.SeekCover
                || decisions.PreviousCombatDecision == ECombatDecision.Retreat
                || bot.Cover?.CoverInUse != null
                || bot.Cover?.CoverPoint_MovingTo != null;
        }

        private void MarkActive(bool active)
        {
            string profileId = BotOwner?.ProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (active)
            {
                ActiveFollowerCombatBots.Add(profileId);
                ActiveFollowerCombatSeenAtByBot[profileId] = Time.time;
            }
            else
            {
                ActiveFollowerCombatBots.Remove(profileId);
                ActiveFollowerCombatSeenAtByBot.Remove(profileId);
            }
        }

        private static System.Type ResolveSearchActionType()
        {
            return SearchActionType;
        }

        private static System.Type ResolveRushEnemyActionType()
        {
            return RushEnemyActionType;
        }

        private static System.Type ResolveSainActionType(string fullTypeName)
        {
            System.Reflection.Assembly sainAssembly = typeof(SAINLayer).Assembly;
            return sainAssembly.GetType(fullTypeName, false) ?? typeof(SAINFollowerCombatSuppressAction);
        }
    }
}
