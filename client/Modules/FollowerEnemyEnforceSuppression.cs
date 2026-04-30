using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    public static class FollowerEnemyEnforceSuppression
    {
        private static readonly Dictionary<string, float> SuppressedUntilByProfile = new Dictionary<string, float>();
        private static readonly object SyncRoot = new object();

        public static void Suppress(BotOwner bot, float durationSeconds)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            float until = Time.time + Mathf.Max(0f, durationSeconds);
            lock (SyncRoot)
            {
                SuppressedUntilByProfile[bot.ProfileId] = until;
            }
        }

        public static bool IsSuppressed(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId)) return false;
            return IsSuppressed(bot.ProfileId);
        }

        public static bool IsSuppressed(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return false;

            lock (SyncRoot)
            {
                if (!SuppressedUntilByProfile.TryGetValue(profileId, out float until))
                {
                    return false;
                }

                if (Time.time <= until)
                {
                    return true;
                }

                SuppressedUntilByProfile.Remove(profileId);
                return false;
            }
        }
    }
}
