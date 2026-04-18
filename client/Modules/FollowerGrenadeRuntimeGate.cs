using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal static class FollowerGrenadeRuntimeGate
    {
        private const float DeniedOwnerLogThrottleSeconds = 1f;

        private static readonly HashSet<string> AlwaysDisabledByProfileId = new();
        private static readonly HashSet<string> ExplicitThrowEnabledByProfileId = new();
        private static readonly Dictionary<string, string> ExplicitThrowOwnerByGroupKey = new();
        private static readonly Dictionary<string, float> NextDeniedOwnerLogAtByProfileId = new();

        public static void EnforceDisabled(BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId == bot.ProfileId)
            {
                ExplicitThrowOwnerByGroupKey.Remove(groupKey);
                Log(bot, $"release-owner reason=enforceDisabled group={groupKey}");
            }

            SetThrowState(bot, enabled: false, disabled: true);
            RefreshFollowerGroup(bot);
            Log(bot, $"disabled group={groupKey ?? "<none>"}");
        }

        public static void EnableExplicitThrow(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (!friendlySAIN.botGrenades.Value)
            {
                EnforceDisabled(bot);
                return;
            }

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey))
            {
                SetThrowState(bot, enabled: true, disabled: false);
                Log(bot, "claim-open group=<none>");
                return;
            }

            if (!ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) ||
                string.IsNullOrEmpty(ownerProfileId) ||
                ownerProfileId == bot.ProfileId)
            {
                bool newOwner = ownerProfileId != bot.ProfileId;
                ExplicitThrowOwnerByGroupKey[groupKey] = bot.ProfileId;
                if (newOwner)
                {
                    Log(bot, $"claim-open group={groupKey}");
                }
            }

            bool ownsThrowWindow = ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out ownerProfileId) &&
                                   ownerProfileId == bot.ProfileId;
            SetThrowState(bot, enabled: ownsThrowWindow, disabled: !ownsThrowWindow);
            if (!ownsThrowWindow)
            {
                LogDeniedOwner(bot, groupKey, ownerProfileId);
            }

            RefreshFollowerGroup(bot);
        }

        public static void FinishExplicitThrow(BotOwner bot, bool completed)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId == bot.ProfileId)
            {
                ExplicitThrowOwnerByGroupKey.Remove(groupKey);
                Log(bot, $"release-owner reason=finish completed={completed} group={groupKey}");
            }

            SetThrowState(bot, enabled: false, disabled: true);
            if (completed)
            {
                FollowerGrenadeCooldowns.RecordThrow(bot);
                Log(bot, "cooldown-start reason=throwComplete");
            }
            else
            {
                FollowerGrenadeCooldowns.CancelPending(bot);
                Log(bot, "no-cooldown reason=throwCancelled");
            }

            RefreshFollowerGroup(bot);
        }

        public static bool IsThrowAllowed(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            if (!friendlySAIN.botGrenades.Value)
            {
                return false;
            }

            string? groupKey = GetGroupKey(bot);
            if (!string.IsNullOrEmpty(groupKey) &&
                ExplicitThrowOwnerByGroupKey.TryGetValue(groupKey, out string ownerProfileId) &&
                ownerProfileId != bot.ProfileId)
            {
                return false;
            }

            if (AlwaysDisabledByProfileId.Contains(bot.ProfileId))
            {
                return false;
            }

            return ExplicitThrowEnabledByProfileId.Contains(bot.ProfileId);
        }

        private static void SetThrowState(BotOwner bot, bool enabled, bool disabled)
        {
            SetAlwaysDisabled(bot, disabled);
            SetExplicitThrowEnabled(bot, enabled);
        }

        private static void SetAlwaysDisabled(BotOwner bot, bool disabled)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (disabled)
            {
                AlwaysDisabledByProfileId.Add(bot.ProfileId);
            }
            else
            {
                AlwaysDisabledByProfileId.Remove(bot.ProfileId);
            }

            RefreshCoreCanGrenade(bot);
        }

        private static void SetExplicitThrowEnabled(BotOwner bot, bool enabled)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            if (enabled)
            {
                ExplicitThrowEnabledByProfileId.Add(bot.ProfileId);
            }
            else
            {
                ExplicitThrowEnabledByProfileId.Remove(bot.ProfileId);
            }

            RefreshCoreCanGrenade(bot);
        }

        private static void RefreshCoreCanGrenade(BotOwner bot)
        {
            if (bot?.Settings?.FileSettings?.Core == null)
            {
                return;
            }

            bot.Settings.FileSettings.Core.CanGrenade = IsThrowAllowed(bot);
        }

        private static string? GetGroupKey(BotOwner bot)
        {
            string bossProfileId = bot?.BotFollower?.BossToFollow?.Player()?.ProfileId;
            if (!string.IsNullOrEmpty(bossProfileId))
            {
                return $"boss:{bossProfileId}";
            }

            if (bot?.BotsGroup != null)
            {
                return $"group:{bot.BotsGroup.Id}";
            }

            return null;
        }

        private static void RefreshFollowerGroup(BotOwner bot)
        {
            IBossToFollow boss = bot?.BotFollower?.BossToFollow;
            if (boss?.Followers == null)
            {
                RefreshCoreCanGrenade(bot);
                return;
            }

            var followers = boss.Followers;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null)
                {
                    continue;
                }

                RefreshCoreCanGrenade(follower);
            }
        }

        private static void LogDeniedOwner(BotOwner bot, string groupKey, string ownerProfileId)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            float now = Time.time;
            if (NextDeniedOwnerLogAtByProfileId.TryGetValue(bot.ProfileId, out float nextLogAt) && nextLogAt > now)
            {
                return;
            }

            NextDeniedOwnerLogAtByProfileId[bot.ProfileId] = now + DeniedOwnerLogThrottleSeconds;
            Log(bot, $"claim-denied group={groupKey} owner={ownerProfileId ?? "<null>"}");
        }

        private static void Log(BotOwner bot, string message)
        {
            friendlySAIN.Log?.LogInfo($"[GrenadeGate] follower={bot?.Profile?.Nickname ?? bot?.ProfileId ?? "<null>"} {message}");
        }
    }
}
