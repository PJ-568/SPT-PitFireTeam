using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class SainDiagnostics
    {
        private static bool initialized;
        private static bool available;
        private static MethodInfo getSainMethod;
        private static PropertyInfo enemyControllerProperty;
        private static PropertyInfo decisionProperty;

        public static object CreateSnapshot(BotOwner bot, IPlayer target)
        {
            if (bot == null || target == null)
            {
                return null;
            }

            if (!pitFireTeam.IsSAINInstalled)
            {
                return new { installed = false };
            }

            if (!Init())
            {
                return new { installed = true, available = false };
            }

            try
            {
                object[] args = { bot.ProfileId, null };
                bool found = (bool)getSainMethod.Invoke(null, args);
                object sainBot = found ? args[1] : null;
                if (sainBot == null)
                {
                    return new { installed = true, available = true, found = false };
                }

                object enemyController = enemyControllerProperty.GetValue(sainBot);
                object decision = decisionProperty?.GetValue(sainBot);
                object targetEnemy = FindEnemy(enemyController, target.ProfileId);
                object goalEnemy = GetPropertyValue(enemyController, "GoalEnemy");

                return new
                {
                    installed = true,
                    available = true,
                    found = true,
                    botActive = GetPropertyValue(sainBot, "BotActive"),
                    botInStandBy = GetPropertyValue(sainBot, "BotInStandBy"),
                    sainLayersActive = GetPropertyValue(sainBot, "SAINLayersActive"),
                    isInCombat = GetPropertyValue(sainBot, "IsInCombat"),
                    hasEnemy = GetPropertyValue(sainBot, "HasEnemy"),
                    atPeace = GetPropertyValue(enemyController, "AtPeace"),
                    activeHumanEnemy = GetPropertyValue(enemyController, "ActiveHumanEnemy"),
                    humanEnemyInLineOfSight = GetPropertyValue(enemyController, "HumanEnemyInLineofSight"),
                    decisions = decision != null
                        ? new
                        {
                            combat = GetPropertyValue(decision, "CurrentCombatDecision")?.ToString(),
                            squad = GetPropertyValue(decision, "CurrentSquadDecision")?.ToString(),
                            self = GetPropertyValue(decision, "CurrentSelfDecision")?.ToString()
                        }
                        : null,
                    counts = new
                    {
                        enemies = Count(GetPropertyValue(enemyController, "Enemies")),
                        known = Count(GetPropertyValue(enemyController, "KnownEnemies")),
                        los = Count(GetPropertyValue(enemyController, "EnemiesInLineOfSight")),
                        visible = Count(GetPropertyValue(enemyController, "VisibleEnemies"))
                    },
                    goalEnemy = CreateEnemySnapshot(goalEnemy),
                    targetEnemy = CreateEnemySnapshot(targetEnemy),
                    targetInEnemies = targetEnemy != null,
                    targetInKnown = ContainsEnemy(GetPropertyValue(enemyController, "KnownEnemies"), target.ProfileId),
                    targetInLineOfSight = ContainsEnemy(GetPropertyValue(enemyController, "EnemiesInLineOfSight"), target.ProfileId),
                    targetInVisible = ContainsEnemy(GetPropertyValue(enemyController, "VisibleEnemies"), target.ProfileId)
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    installed = true,
                    available = true,
                    error = ex.GetType().Name,
                    message = ex.Message
                };
            }
        }

        private static bool Init()
        {
            if (initialized)
            {
                return available;
            }

            initialized = true;

            Type sainEnableType = Type.GetType("SAIN.SAINEnableClass, SAIN");
            Type botComponentType = Type.GetType("SAIN.Components.BotComponent, SAIN");

            if (sainEnableType != null && botComponentType != null)
            {
                getSainMethod = AccessTools.Method(
                    sainEnableType,
                    "GetSAIN",
                    new[] { typeof(string), botComponentType.MakeByRefType() });
                enemyControllerProperty = botComponentType.GetProperty("EnemyController", BindingFlags.Instance | BindingFlags.Public);
                decisionProperty = botComponentType.GetProperty("Decision", BindingFlags.Instance | BindingFlags.Public);
            }

            available = getSainMethod != null && enemyControllerProperty != null;
            return available;
        }

        private static object FindEnemy(object enemyController, string profileId)
        {
            object enemiesObject = GetPropertyValue(enemyController, "Enemies");
            if (enemiesObject is IDictionary dictionary && dictionary.Contains(profileId))
            {
                return dictionary[profileId];
            }

            return null;
        }

        private static bool ContainsEnemy(object list, string profileId)
        {
            if (list is IEnumerable enumerable)
            {
                foreach (object enemy in enumerable)
                {
                    object enemyProfileId = GetPropertyValue(enemy, "EnemyProfileId");
                    if ((enemyProfileId as string) == profileId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int? Count(object value)
        {
            if (value is ICollection collection)
            {
                return collection.Count;
            }

            return null;
        }

        private static object CreateEnemySnapshot(object enemy)
        {
            if (enemy == null)
            {
                return null;
            }

            object knownPlaces = GetPropertyValue(enemy, "KnownPlaces");
            return new
            {
                profileId = GetPropertyValue(enemy, "EnemyProfileId"),
                nickname = GetPropertyValue(enemy, "EnemyName"),
                isCurrentEnemy = GetPropertyValue(enemy, "IsCurrentEnemy"),
                enemyKnown = GetPropertyValue(enemy, "EnemyKnown"),
                seen = GetPropertyValue(enemy, "Seen"),
                isVisible = GetPropertyValue(enemy, "IsVisible"),
                canShoot = GetPropertyValue(enemy, "CanShoot"),
                heard = GetPropertyValue(enemy, "Heard"),
                realDistance = SanitizeFloat(GetPropertyValue(enemy, "RealDistance")),
                lastKnownPosition = CreateVector(GetPropertyValue(enemy, "LastKnownPosition")),
                timeSinceLastKnownUpdated = SanitizeFloat(GetPropertyValue(enemy, "TimeSinceLastKnownUpdated")),
                knownPlaces = knownPlaces != null
                    ? new
                    {
                        hasLastKnownPlace = GetPropertyValue(knownPlaces, "LastKnownPlace") != null,
                        lastKnownPosition = CreateVector(GetPropertyValue(knownPlaces, "LastKnownPosition")),
                        timeSinceLastKnownUpdated = SanitizeFloat(GetPropertyValue(knownPlaces, "TimeSinceLastKnownUpdated"))
                    }
                    : null
            };
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null)
            {
                return null;
            }

            return instance.GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
        }

        private static object CreateVector(object value)
        {
            if (value is Vector3 vector)
            {
                return new { x = vector.x, y = vector.y, z = vector.z };
            }

            if (value is Vector3 nullableVector)
            {
                return new { x = nullableVector.x, y = nullableVector.y, z = nullableVector.z };
            }

            return null;
        }

        private static float? SanitizeFloat(object value)
        {
            if (value is float floatValue && !float.IsNaN(floatValue) && !float.IsInfinity(floatValue))
            {
                return floatValue;
            }

            return null;
        }
    }
}
