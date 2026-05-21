using System;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;

namespace pitTeam.Utils
{
    internal static class LootingBotsInterop
    {
        private const string PluginGuid = "me.skwizzy.lootingbots";
        private const string ExternalTypeName = "LootingBots.External, skwizzy.LootingBots";

        private static bool _initialized;
        private static bool _available;
        private static MethodInfo _preventBotFromLootingMethod;

        public static bool PreventBotFromLooting(BotOwner bot, float duration)
        {
            if (bot == null || !Init())
            {
                return false;
            }

            try
            {
                return (bool)_preventBotFromLootingMethod.Invoke(null, new object[] { bot, duration });
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"LootingBots PreventBotFromLooting failed for {bot.Profile?.Nickname}.");
                Modules.Logger.LogError(ex);
                return false;
            }
        }

        private static bool Init()
        {
            if (_initialized)
            {
                return _available;
            }

            _initialized = true;

            if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
            {
                _available = false;
                return false;
            }

            Type externalType = Type.GetType(ExternalTypeName);
            _preventBotFromLootingMethod = externalType != null
                ? AccessTools.Method(externalType, "PreventBotFromLooting")
                : null;

            _available = _preventBotFromLootingMethod != null;
            if (!_available)
            {
                Modules.Logger.LogInfo("LootingBots PreventBotFromLooting interop is unavailable.");
            }

            return _available;
        }
    }
}
