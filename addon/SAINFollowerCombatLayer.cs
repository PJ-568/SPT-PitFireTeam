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
        private const float ActiveFollowerCombatStaleSeconds = 2f;
        private static readonly System.Type SearchActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.SearchAction");
        private static readonly System.Type RushEnemyActionType = ResolveSainActionType("SAIN.Layers.Combat.Solo.RushEnemyAction");
        private static readonly Dictionary<string, float> RegroupCommandLatchUntilByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float RegroupCommandLatchSeconds = 8f;
        private const float SquadDecisionNoEnemyGraceSeconds = 2f;
        private const float SelfActionTransitionGraceSeconds = 1.25f;
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
            MarkActive(active);
            CheckActiveChanged(active);
            return active;
        }

        public override Action GetNextAction()
        {
            _lastActionDecision = _currentDecision;
            switch (_lastActionDecision)
            {
                case ESquadDecision.Regroup:
                    return new Action(typeof(SAINFollowerCombatRegroupAction), $"{_lastActionDecision}");

                case ESquadDecision.Suppress:
                    return new Action(typeof(SAINFollowerCombatSuppressAction), $"{_lastActionDecision}");

                case ESquadDecision.Search:
                case ESquadDecision.Help:
                    return new Action(ResolveSearchActionType(), $"{_lastActionDecision}");

                case ESquadDecision.GroupSearch:
                    return new Action(typeof(SAINFollowerCombatFollowBossSearchAction), $"{_lastActionDecision} : Follow Boss Search");

                case ESquadDecision.PushSuppressedEnemy:
                    return new Action(ResolveRushEnemyActionType(), $"{_lastActionDecision}");

                default:
                    return new Action(typeof(SAINFollowerCombatRegroupAction), "DEFAULT");
            }
        }

        public override bool IsCurrentActionEnding()
        {
            if (base.IsCurrentActionEnding())
            {
                return true;
            }

            if (!TryEvaluateFollowerDecision(out ESquadDecision nextDecision))
            {
                return true;
            }

            return nextDecision != _lastActionDecision;
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
                return false;
            }

            if (ShouldDeferToSoloSelfAction(bot, decisions))
            {
                return false;
            }

            bool haveEnemy = BotOwner.Memory?.HaveEnemy == true;
            if (haveEnemy)
            {
                _lastEnemySeenAt = Time.time;
            }

            if (TryHandleRegroupCommand(out decision))
            {
                return true;
            }

            if (SAINFollowerSquadDecisionCalculator.TryGetDecision(BotOwner, bot, out decision) && decision != ESquadDecision.None)
            {
                return true;
            }

            // Fallback: if SAIN already has a squad decision, run follower layer and map leader/member-sensitive actions to boss/followers.
            if (decisions.CurrentSquadDecision != ESquadDecision.None)
            {
                int knownEnemyCount = bot.EnemyController?.KnownEnemies?.Count ?? 0;
                bool recentEnemyContext = Time.time - _lastEnemySeenAt <= SquadDecisionNoEnemyGraceSeconds;
                if (!haveEnemy && knownEnemyCount == 0 && !recentEnemyContext)
                {
                    return false;
                }

                decision = decisions.CurrentSquadDecision;
                return true;
            }

            if (haveEnemy)
            {
                decision = ESquadDecision.Regroup;
                return true;
            }

            return false;
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
