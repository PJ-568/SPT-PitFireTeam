using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal static class FollowerGrenadeRuntimeGate
    {
        private static readonly Dictionary<string, float> ThrowAllowedUntilByProfileId = new();

        public static void EnforceDisabled(BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            if (bot.Settings?.FileSettings?.Core != null)
            {
                bot.Settings.FileSettings.Core.CanGrenade = false;
            }

            if (!string.IsNullOrEmpty(bot.ProfileId))
            {
                ThrowAllowedUntilByProfileId.Remove(bot.ProfileId);
            }
        }

        public static void AllowThrowWindow(BotOwner bot, float seconds = 2.5f)
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

            if (bot.Settings?.FileSettings?.Core != null)
            {
                bot.Settings.FileSettings.Core.CanGrenade = true;
            }

            ThrowAllowedUntilByProfileId[bot.ProfileId] = Time.time + Mathf.Max(0.25f, seconds);
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

            if (!ThrowAllowedUntilByProfileId.TryGetValue(bot.ProfileId, out float allowedUntil))
            {
                return false;
            }

            if (allowedUntil < Time.time)
            {
                ThrowAllowedUntilByProfileId.Remove(bot.ProfileId);
                return false;
            }

            return true;
        }
    }
}
