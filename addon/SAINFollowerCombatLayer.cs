using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
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
        private static System.Type? _searchActionType;
        private static System.Type? _rushEnemyActionType;
        private static readonly Dictionary<string, float> RegroupCommandLatchUntilByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private const float RegroupCommandLatchSeconds = 8f;
        private ESquadDecision _currentDecision = ESquadDecision.None;
        private ESquadDecision _lastActionDecision = ESquadDecision.None;

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
                MarkActive(false);
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

            return ActiveFollowerCombatBots.Contains(botOwner.ProfileId);
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
                decision = decisions.CurrentSquadDecision;
                return true;
            }

            if (BotOwner.Memory?.HaveEnemy == true)
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
            }
            else
            {
                ActiveFollowerCombatBots.Remove(profileId);
            }
        }

        private static System.Type ResolveSearchActionType()
        {
            _searchActionType ??= AccessTools.TypeByName("SAIN.Layers.Combat.Solo.SearchAction") ?? typeof(SAINFollowerCombatSuppressAction);
            return _searchActionType;
        }

        private static System.Type ResolveRushEnemyActionType()
        {
            _rushEnemyActionType ??= AccessTools.TypeByName("SAIN.Layers.Combat.Solo.RushEnemyAction") ?? typeof(SAINFollowerCombatSuppressAction);
            return _rushEnemyActionType;
        }
    }
}
