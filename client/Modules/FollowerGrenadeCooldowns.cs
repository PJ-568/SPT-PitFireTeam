using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal static class FollowerGrenadeCooldowns
    {
        private const float IndividualCooldownSeconds = 15f;
        private const float GroupCooldownMinSeconds = 5f;
        private const float GroupCooldownMaxSeconds = 8f;
        private const float PendingReservationSeconds = 8f;

        private sealed class GroupCooldownState
        {
            public string? PendingBotProfileId;
            public float PendingUntilTime;
            public float NextAllowedThrowTime;
        }

        private static readonly Dictionary<string, float> NextAllowedThrowByBot = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly Dictionary<string, GroupCooldownState> GroupStateByKey = new Dictionary<string, GroupCooldownState>(StringComparer.Ordinal);

        public static bool TryReserveThrow(BotOwner bot)
        {
            if (!CanProceedToThrow(bot))
            {
                return false;
            }

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey))
            {
                return true;
            }

            GroupCooldownState state = GetOrCreateGroupState(groupKey);
            state.PendingBotProfileId = bot.ProfileId;
            state.PendingUntilTime = Time.time + PendingReservationSeconds;
            return true;
        }

        public static void CancelPending(BotOwner bot)
        {
            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey) || !GroupStateByKey.TryGetValue(groupKey, out GroupCooldownState? state))
            {
                return;
            }

            if (string.Equals(state.PendingBotProfileId, bot?.ProfileId, StringComparison.Ordinal))
            {
                state.PendingBotProfileId = null;
                state.PendingUntilTime = 0f;
            }
        }

        public static bool CanProceedToThrow(BotOwner bot)
        {
            if (!IsFollower(bot))
            {
                return true;
            }

            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return false;
            }

            float now = Time.time;

            if (NextAllowedThrowByBot.TryGetValue(bot.ProfileId, out float nextAllowedThrowTime))
            {
                if (now < nextAllowedThrowTime)
                {
                    return false;
                }

                NextAllowedThrowByBot.Remove(bot.ProfileId);
            }

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey))
            {
                return true;
            }

            GroupCooldownState state = GetOrCreateGroupState(groupKey);
            if (!string.IsNullOrEmpty(state.PendingBotProfileId))
            {
                if (now >= state.PendingUntilTime)
                {
                    state.PendingBotProfileId = null;
                    state.PendingUntilTime = 0f;
                }
                else if (!string.Equals(state.PendingBotProfileId, bot.ProfileId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return now >= state.NextAllowedThrowTime;
        }

        public static void RecordThrow(BotOwner bot)
        {
            if (!IsFollower(bot) || bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return;
            }

            float now = Time.time;
            NextAllowedThrowByBot[bot.ProfileId] = now + IndividualCooldownSeconds;

            string? groupKey = GetGroupKey(bot);
            if (string.IsNullOrEmpty(groupKey))
            {
                return;
            }

            GroupCooldownState state = GetOrCreateGroupState(groupKey);
            state.PendingBotProfileId = null;
            state.PendingUntilTime = 0f;
            state.NextAllowedThrowTime = now + UnityEngine.Random.Range(GroupCooldownMinSeconds, GroupCooldownMaxSeconds);
        }

        public static void ClearAll()
        {
            NextAllowedThrowByBot.Clear();
            GroupStateByKey.Clear();
        }

        private static bool IsFollower(BotOwner bot)
        {
            return bot != null && BossPlayers.GetFollowerByProfileId(bot.ProfileId) != null;
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

        private static GroupCooldownState GetOrCreateGroupState(string groupKey)
        {
            if (!GroupStateByKey.TryGetValue(groupKey, out GroupCooldownState? state))
            {
                state = new GroupCooldownState();
                GroupStateByKey[groupKey] = state;
            }

            return state;
        }
    }
}
