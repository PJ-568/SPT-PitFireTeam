using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal static class FollowerBotRequestGate
    {
        private struct AuthorizedRequest
        {
            public BotRequestType RequestType;
            public float ExpiresAt;
        }

        private static readonly Dictionary<string, AuthorizedRequest> AuthorizedByProfileId = new();

        public static void Authorize(BotOwner bot, BotRequestType requestType, float duration = 0.5f)
        {
            if (bot?.ProfileId == null)
            {
                return;
            }

            AuthorizedByProfileId[bot.ProfileId] = new AuthorizedRequest
            {
                RequestType = requestType,
                ExpiresAt = Time.time + Mathf.Max(0.05f, duration)
            };
        }

        public static bool TryConsume(BotOwner bot, BotRequestType requestType)
        {
            if (bot?.ProfileId == null)
            {
                return false;
            }

            if (!AuthorizedByProfileId.TryGetValue(bot.ProfileId, out AuthorizedRequest authorization))
            {
                return false;
            }

            if (authorization.ExpiresAt < Time.time || authorization.RequestType != requestType)
            {
                AuthorizedByProfileId.Remove(bot.ProfileId);
                return false;
            }

            AuthorizedByProfileId.Remove(bot.ProfileId);
            return true;
        }

        public static void Clear(BotOwner bot)
        {
            if (bot?.ProfileId == null)
            {
                return;
            }

            AuthorizedByProfileId.Remove(bot.ProfileId);
        }
    }
}
