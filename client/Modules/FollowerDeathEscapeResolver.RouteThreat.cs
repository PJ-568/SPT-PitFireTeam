using Comfort.Common;
using EFT;
using pitTeam.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class FollowerDeathEscapeResolver
    {
        private const float RouteThreatCorridorRadius = 80f;

        private static readonly Dictionary<WildSpawnType, float> RouteThreatRoleMultipliers = new Dictionary<WildSpawnType, float>
        {
            { WildSpawnType.bossTest, 0f },
            { WildSpawnType.followerTest, 0f },
            { WildSpawnType.bossZryachiy, 0f },
            { WildSpawnType.followerZryachiy, 0f },
            { WildSpawnType.bossTagillaAgro, 0f },
            { WildSpawnType.bossKillaAgro, 0f },

            { WildSpawnType.followerBirdEye, 2.00f },
            { WildSpawnType.bossKnight, 1.90f },
            { WildSpawnType.followerBigPipe, 1.80f },
            { WildSpawnType.bossTagilla, 1.80f },
            { WildSpawnType.bossKilla, 1.70f },

            { WildSpawnType.bossGluhar, 1.60f },
            { WildSpawnType.bossKolontay, 1.60f },
            { WildSpawnType.bossBoar, 1.50f },
            { WildSpawnType.bossKojaniy, 1.50f },
            { WildSpawnType.bossBoarSniper, 1.50f },
            { WildSpawnType.followerGluharSnipe, 1.45f },
            { WildSpawnType.followerBoarClose1, 1.40f },
            { WildSpawnType.followerBoarClose2, 1.40f },
            { WildSpawnType.followerKolontayAssault, 1.35f },
            { WildSpawnType.followerGluharAssault, 1.35f },

            { WildSpawnType.bossSanitar, 1.30f },
            { WildSpawnType.followerKojaniy, 1.25f },
            { WildSpawnType.followerGluharSecurity, 1.25f },
            { WildSpawnType.followerKolontaySecurity, 1.25f },
            { WildSpawnType.followerBoar, 1.25f },
            { WildSpawnType.followerTagilla, 1.25f },
            { WildSpawnType.followerBully, 1.20f },

            { WildSpawnType.followerGluharScout, 1.15f },
            { WildSpawnType.followerSanitar, 1.15f },
            { WildSpawnType.bossPartisan, 1.10f },
            { WildSpawnType.bossBully, 1.05f }
        };

        private static RouteThreatSnapshot CalculateRouteEnemyAveragePower(pitAIBossPlayer boss, Vector3 routeStart, ExtractSnapshot extract)
        {
            try
            {
                if (!extract.HasPosition)
                {
                    return RouteThreatSnapshot.Empty;
                }

                GameWorld gameWorld = Singleton<GameWorld>.Instance;
                List<Player> alivePlayers = gameWorld?.AllAlivePlayersList;
                if (alivePlayers == null || alivePlayers.Count == 0)
                {
                    return RouteThreatSnapshot.Empty;
                }

                List<float> enemyPowers = new List<float>();
                foreach (Player player in alivePlayers)
                {
                    if (player == null ||
                        player.ProfileId == boss.realPlayer.ProfileId ||
                        player.HealthController?.IsAlive != true ||
                        !player.IsAI ||
                        player.AIData?.BotOwner == null)
                    {
                        continue;
                    }

                    BotOwner bot = player.AIData.BotOwner;
                    if (BossPlayers.IsFollower(bot))
                    {
                        continue;
                    }

                    // Use the boss group's enemy relation when available so neutral same-side bots
                    // do not affect the route threat average.
                    bool isEnemy = boss.bossGroup != null &&
                                   (boss.bossGroup.IsEnemy(player) || boss.bossGroup.IsPlayerEnemy(player));
                    if (!isEnemy)
                    {
                        continue;
                    }

                    // Only enemies between this follower and the selected extract matter for the
                    // simulated escape route. Remote fights elsewhere on the map are ignored.
                    if (!IsPointInRouteCorridor(routeStart, extract.Position, player.Position))
                    {
                        continue;
                    }

                    WildSpawnType role = player.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
                    float roleMultiplier = GetRouteThreatRoleMultiplier(role);
                    if (roleMultiplier <= 0f)
                    {
                        continue;
                    }

                    // Boss and follower roles are more/less dangerous than raw gear score suggests,
                    // so route threat weights equipment power by role before averaging.
                    float power = player.AIData.PowerOfEquipment * roleMultiplier;
                    if (power > 0f)
                    {
                        enemyPowers.Add(power);
                    }
                }

                return enemyPowers.Count > 0
                    ? new RouteThreatSnapshot(enemyPowers.Average(), enemyPowers.Count)
                    : RouteThreatSnapshot.Empty;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to calculate route enemy average equipment power");
                Logger.LogError(ex);
                return RouteThreatSnapshot.Empty;
            }
        }

        private static float GetRouteThreatRoleMultiplier(WildSpawnType role)
        {
            if (RouteThreatRoleMultipliers.TryGetValue(role, out float multiplier))
            {
                return multiplier;
            }

            string roleName = role.ToString();
            if (roleName.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
            {
                return 1.30f;
            }

            if (roleName.StartsWith("follower", StringComparison.OrdinalIgnoreCase))
            {
                return 1.15f;
            }

            return 1f;
        }

        private static bool IsPointInRouteCorridor(Vector3 routeStart, Vector3 routeEnd, Vector3 point)
        {
            Vector3 route = Flatten(routeEnd - routeStart);
            float routeLengthSqr = route.sqrMagnitude;
            if (routeLengthSqr <= 1f)
            {
                return false;
            }

            Vector3 toPoint = Flatten(point - routeStart);
            float projection01 = Vector3.Dot(toPoint, route) / routeLengthSqr;
            if (projection01 < 0f || projection01 > 1f)
            {
                return false;
            }

            Vector3 closestPoint = Flatten(routeStart) + route * projection01;
            float corridorRadiusSqr = RouteThreatCorridorRadius * RouteThreatCorridorRadius;
            return (Flatten(point) - closestPoint).sqrMagnitude <= corridorRadiusSqr;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
