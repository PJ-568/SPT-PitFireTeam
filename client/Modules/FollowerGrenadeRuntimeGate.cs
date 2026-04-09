using EFT;
using System.Collections.Generic;

namespace friendlySAIN.Modules
{
    internal static class FollowerGrenadeRuntimeGate
    {
        private static readonly HashSet<string> AlwaysDisabledByProfileId = new();
        private static readonly HashSet<string> ExplicitThrowEnabledByProfileId = new();

        public static void EnforceDisabled(BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            SetAlwaysDisabled(bot, true);
            SetExplicitThrowEnabled(bot, false);
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

            SetAlwaysDisabled(bot, false);
            SetExplicitThrowEnabled(bot, true);
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

            if (AlwaysDisabledByProfileId.Contains(bot.ProfileId))
            {
                return false;
            }

            return ExplicitThrowEnabledByProfileId.Contains(bot.ProfileId);
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
    }
}
