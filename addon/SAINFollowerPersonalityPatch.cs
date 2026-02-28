using EFT;
using HarmonyLib;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerPersonalityPatch
    {
        private static Type? _sainEnableType;
        private static MethodInfo? _getSainByBotOwner;
        private static MethodInfo? _getSainByProfile;
        private static Type? _ePersonalityType;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(BossPlayers),
                "AddFollower",
                new[] { typeof(BotOwner), typeof(pitAIBossPlayer), typeof(bool), typeof(WildSpawnType), typeof(string) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find BossPlayers.AddFollower for SAIN personality patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerPersonalityPatch), nameof(Postfix_AddFollower)));
            Modules.Logger.LogInfo("[Init] SAIN follower personality patch applied.");
        }

        private static void Postfix_AddFollower(BotOwner bot, BotFollowerPlayer __result)
        {
            try
            {
                if (!friendlySAIN.IsSAINInstalled) return;
                if (bot == null || __result == null || bot.IsDead) return;

                // Deterministic split per profile so assignment is stable for the same bot.
                string personalityName = PickFollowerPersonality(bot);
                TrySetSainPersonality(bot, personalityName);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed to assign follower personality.");
                Modules.Logger.LogError(ex);
            }
        }

        private static string PickFollowerPersonality(BotOwner bot)
        {
            int seed = bot.ProfileId?.GetHashCode() ?? 0;
            if (seed == int.MinValue) seed = 0;
            return Math.Abs(seed) % 2 == 0 ? "Chad" : "Normal";
        }

        private static bool TrySetSainPersonality(BotOwner bot, string personalityName)
        {
            if (bot == null || string.IsNullOrEmpty(personalityName)) return false;
            if (!ResolveSainGetMethods()) return false;

            object? sainBot = null;
            if (_getSainByBotOwner != null)
            {
                sainBot = _getSainByBotOwner.Invoke(null, new object[] { bot });
            }
            else if (_getSainByProfile != null && !string.IsNullOrEmpty(bot.ProfileId))
            {
                object?[] args = { bot.ProfileId, null };
                bool hasSain = (bool)_getSainByProfile.Invoke(null, args);
                if (hasSain) sainBot = args[1];
            }

            if (sainBot == null) return false;

            object? info = AccessTools.Property(sainBot.GetType(), "Info")?.GetValue(sainBot);
            if (info == null) return false;

            _ePersonalityType ??= AccessTools.TypeByName("SAIN.Models.Preset.Personalities.EPersonality");
            if (_ePersonalityType == null) return false;

            object enumValue = Enum.Parse(_ePersonalityType, personalityName);
            MethodInfo? setPersonality = AccessTools.Method(info.GetType(), "SetPersonality", new[] { _ePersonalityType });
            if (setPersonality == null) return false;

            setPersonality.Invoke(info, new[] { enumValue });
            return true;
        }

        private static bool ResolveSainGetMethods()
        {
            if (_sainEnableType == null)
            {
                _sainEnableType = AccessTools.TypeByName("SAIN.SAINEnableClass") ??
                                  AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");
            }

            if (_sainEnableType == null) return false;

            if (_getSainByBotOwner == null || _getSainByProfile == null)
            {
                foreach (MethodInfo method in _sainEnableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name != "GetSAIN") continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                    {
                        _getSainByBotOwner = method;
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].IsOut)
                    {
                        _getSainByProfile = method;
                    }
                }
            }

            return _getSainByBotOwner != null || _getSainByProfile != null;
        }
    }
}
