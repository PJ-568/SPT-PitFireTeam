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
        private const float HardPlusVisionGainSightCoef = 1.75f;
        private const float HardPlusHearingDistanceCoef = 1.40f;
        private const float HardPlusVisibleDistCoef = 1.40f;
        private const float HardPlusAggressionCoef = 1.50f;
        private const float HardPlusPrecisionSpeedCoef = 2.10f;
        private const float HardPlusAccuracySpeedCoefMax = 0.24f;
        private const float HardPlusScatteringCoefMax = 0.28f;
        private const float HardPlusFasterCQBReactionsDistance = 50f;
        private const float HardPlusFasterCQBReactionsMinimumMax = 0.20f;
        private const float HardPlusMaxAimingUpgradeByTimeMax = 0.20f;
        private const float HardPlusMaxAimTimeMax = 1.15f;
        private const float HardPlusBaseHitAffectionDelayMax = 0.35f;
        private const float HardPlusVisibleDistanceMin = 250f;
        private const float HardPlusCoreGainSightCoef = 1.00f;
        private const float FollowerAimForHeadChance = 100f;
        private static Type? _sainEnableType;
        private static Type? _sainPluginType;
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
                TryApplyFollowerHardPlusSainTuning(bot);
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

            object? info = GetInstanceMemberValue(sainBot, "Info");
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

        private static void TryApplyFollowerHardPlusSainTuning(BotOwner bot)
        {
            if (bot == null) return;
            if (!ResolveSainGetMethods()) return;

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

            if (sainBot == null) return;

            object? info = GetInstanceMemberValue(sainBot, "Info");
            if (info == null) return;

            object? fileSettings = GetInstanceMemberValue(info, "FileSettings");
            if (fileSettings == null) return;

            object? difficultySettings = GetInstanceMemberValue(fileSettings, "Difficulty");
            object? coreSettings = GetInstanceMemberValue(fileSettings, "Core");
            object? aimingSettings = GetInstanceMemberValue(fileSettings, "Aiming");

            bool changed = false;
            if (difficultySettings != null)
            {
                changed |= SetMinFloat(difficultySettings, "GainSightCoef", HardPlusVisionGainSightCoef);
                changed |= SetMinFloat(difficultySettings, "HearingDistanceCoef", HardPlusHearingDistanceCoef);
                changed |= SetMinFloat(difficultySettings, "VisibleDistCoef", HardPlusVisibleDistCoef);
                changed |= SetMinFloat(difficultySettings, "AggressionCoef", HardPlusAggressionCoef);
                changed |= SetMinFloat(difficultySettings, "PRECISION_SPEED_COEF", HardPlusPrecisionSpeedCoef);
                changed |= SetMaxFloat(difficultySettings, "ACCURACY_SPEED_COEF", HardPlusAccuracySpeedCoefMax);
                changed |= SetMaxFloat(difficultySettings, "ScatteringCoef", HardPlusScatteringCoefMax);
            }
            if (coreSettings != null)
            {
                changed |= SetMinFloat(coreSettings, "HearingDistanceMulti", HardPlusHearingDistanceCoef);
                changed |= SetMinFloat(coreSettings, "GainSightCoef", HardPlusCoreGainSightCoef);
                changed |= SetMinFloat(coreSettings, "VisibleDistance", HardPlusVisibleDistanceMin);
            }
            if (aimingSettings != null)
            {
                changed |= SetMinFloat(aimingSettings, "FasterCQBReactionsDistance", HardPlusFasterCQBReactionsDistance);
                changed |= SetMaxFloat(aimingSettings, "FasterCQBReactionsMinimum", HardPlusFasterCQBReactionsMinimumMax);
                changed |= SetMaxFloat(aimingSettings, "MAX_AIMING_UPGRADE_BY_TIME", HardPlusMaxAimingUpgradeByTimeMax);
                changed |= SetMaxFloat(aimingSettings, "MAX_AIM_TIME", HardPlusMaxAimTimeMax);
                changed |= SetMaxFloat(aimingSettings, "BASE_HIT_AFFECTION_DELAY_SEC", HardPlusBaseHitAffectionDelayMax);
                changed |= SetBool(aimingSettings, "AimCenterMass", false);
                changed |= SetBool(aimingSettings, "AimForHead", true);
                changed |= SetMinFloat(aimingSettings, "AimForHeadChance", FollowerAimForHeadChance);
            }

            if (!changed) return;

            try
            {
                // Re-apply core hearing settings immediately.
                object? eftFileSettings = bot.Settings?.FileSettings;
                if (coreSettings != null && eftFileSettings != null)
                {
                    MethodInfo? apply = AccessTools.Method(coreSettings.GetType(), "Apply", new[] { eftFileSettings.GetType() });
                    apply?.Invoke(coreSettings, new[] { eftFileSettings });
                }

                // Rebuild SAIN difficulty modifiers from updated settings.
                object? difficulty = GetInstanceMemberValue(info, "Difficulty");
                MethodInfo? updateSettings = difficulty != null ? AccessTools.Method(difficulty.GetType(), "UpdateSettings") : null;
                object? loadedPreset = GetSainLoadedPreset();
                if (difficulty != null && updateSettings != null && loadedPreset != null)
                {
                    updateSettings.Invoke(difficulty, new[] { loadedPreset });
                }

                float gainSight = GetFloat(difficultySettings, "GainSightCoef");
                float hearingDiff = GetFloat(difficultySettings, "HearingDistanceCoef");
                float visibleDist = GetFloat(difficultySettings, "VisibleDistCoef");
                float aggression = GetFloat(difficultySettings, "AggressionCoef");
                float precisionSpeed = GetFloat(difficultySettings, "PRECISION_SPEED_COEF");
                float accuracySpeed = GetFloat(difficultySettings, "ACCURACY_SPEED_COEF");
                float scattering = GetFloat(difficultySettings, "ScatteringCoef");
                float hearingCore = GetFloat(coreSettings, "HearingDistanceMulti");
                float gainSightCore = GetFloat(coreSettings, "GainSightCoef");
                float visibleDistanceCore = GetFloat(coreSettings, "VisibleDistance");
                float cqbDistance = GetFloat(aimingSettings, "FasterCQBReactionsDistance");
                float cqbMinimum = GetFloat(aimingSettings, "FasterCQBReactionsMinimum");
                float maxAimingUpgrade = GetFloat(aimingSettings, "MAX_AIMING_UPGRADE_BY_TIME");
                float maxAimTime = GetFloat(aimingSettings, "MAX_AIM_TIME");
                float baseHitAffection = GetFloat(aimingSettings, "BASE_HIT_AFFECTION_DELAY_SEC");
                Modules.Logger.LogInfo(
                    $"[SAIN] follower hard+ tuning applied bot={bot.Profile?.Nickname ?? bot.name} " +
                    $"gainSight={gainSight:F2} hearingDiff={hearingDiff:F2} visibleDist={visibleDist:F2} aggression={aggression:F2} " +
                    $"precisionSpeed={precisionSpeed:F2} accuracySpeed={accuracySpeed:F2} scattering={scattering:F2} " +
                    $"hearingCore={hearingCore:F2} gainSightCore={gainSightCore:F2} visibleDistance={visibleDistanceCore:F1} " +
                    $"cqbDist={cqbDistance:F1} cqbMin={cqbMinimum:F2} maxAimUpgrade={maxAimingUpgrade:F2} " +
                    $"maxAimTime={maxAimTime:F2} hitAffectDelay={baseHitAffection:F2}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Failed applying follower hard+ tuning bot={bot?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
            }
        }

        private static object? GetSainLoadedPreset()
        {
            _sainPluginType ??= AccessTools.TypeByName("SAIN.SAINPlugin");
            if (_sainPluginType == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            PropertyInfo? prop = _sainPluginType.GetProperty("LoadedPreset", flags);
            return prop?.CanRead == true ? prop.GetValue(null, null) : null;
        }

        private static bool SetMinFloat(object target, string memberName, float minValue)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo? prop = target.GetType().GetProperty(memberName, flags);
            if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead && prop.CanWrite)
            {
                float current = (float)prop.GetValue(target);
                if (current < minValue)
                {
                    prop.SetValue(target, minValue);
                    return true;
                }
                return false;
            }

            FieldInfo? field = target.GetType().GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(float))
            {
                float current = (float)field.GetValue(target);
                if (current < minValue)
                {
                    field.SetValue(target, minValue);
                    return true;
                }
            }

            return false;
        }

        private static float GetFloat(object? target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return -1f;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo? prop = target.GetType().GetProperty(memberName, flags);
            if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead)
            {
                return (float)prop.GetValue(target);
            }

            FieldInfo? field = target.GetType().GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(float))
            {
                return (float)field.GetValue(target);
            }

            return -1f;
        }

        private static bool SetMaxFloat(object target, string memberName, float maxValue)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo? prop = target.GetType().GetProperty(memberName, flags);
            if (prop != null && prop.PropertyType == typeof(float) && prop.CanRead && prop.CanWrite)
            {
                float current = (float)prop.GetValue(target);
                if (current > maxValue)
                {
                    prop.SetValue(target, maxValue);
                    return true;
                }
                return false;
            }

            FieldInfo? field = target.GetType().GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(float))
            {
                float current = (float)field.GetValue(target);
                if (current > maxValue)
                {
                    field.SetValue(target, maxValue);
                    return true;
                }
            }

            return false;
        }

        private static bool SetBool(object target, string memberName, bool value)
        {
            if (target == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo? prop = target.GetType().GetProperty(memberName, flags);
            if (prop != null && prop.PropertyType == typeof(bool) && prop.CanRead && prop.CanWrite)
            {
                bool current = (bool)prop.GetValue(target);
                if (current != value)
                {
                    prop.SetValue(target, value);
                    return true;
                }

                return false;
            }

            FieldInfo? field = target.GetType().GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(bool))
            {
                bool current = (bool)field.GetValue(target);
                if (current != value)
                {
                    field.SetValue(target, value);
                    return true;
                }
            }

            return false;
        }

        private static object? GetInstanceMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = target.GetType();

            PropertyInfo? prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead)
            {
                return prop.GetValue(target, null);
            }

            FieldInfo? field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(target);
            }

            return null;
        }
    }
}
