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
        private const float CurrentFightThreatRadius = 180f;
        private const float CurrentFightTargetingFallbackRadius = 100f;

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

        private static RouteThreatSnapshot CalculateCurrentFightEnemyAveragePower(
            pitAIBossPlayer boss,
            IEnumerable<BotFollowerPlayer> squadmates,
            BotOwner escapingBot,
            Vector3 deathPosition)
        {
            try
            {
                GameWorld gameWorld = Singleton<GameWorld>.Instance;
                List<Player> alivePlayers = gameWorld?.AllAlivePlayersList;
                if (alivePlayers == null || alivePlayers.Count == 0)
                {
                    return RouteThreatSnapshot.Empty;
                }

                HashSet<string> squadProfileIds = new HashSet<string>(
                    (squadmates ?? Enumerable.Empty<BotFollowerPlayer>())
                    .Select(follower => follower?.GetBot()?.ProfileId)
                    .Where(profileId => !string.IsNullOrWhiteSpace(profileId)),
                    StringComparer.Ordinal);
                string playerProfileId = boss?.realPlayer?.ProfileId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(playerProfileId))
                {
                    squadProfileIds.Add(playerProfileId);
                }

                Dictionary<string, Player> candidateEnemies = new Dictionary<string, Player>(StringComparer.Ordinal);
                AddBossGroupEnemies(boss, candidateEnemies);
                AddFollowerMemoryEnemies(squadmates, candidateEnemies);
                AddEnemiesTargetingSquad(alivePlayers, squadProfileIds, candidateEnemies);

                List<float> enemyPowers = new List<float>();
                foreach (Player enemy in candidateEnemies.Values)
                {
                    if (!IsValidDeathEscapeEnemy(boss, enemy))
                    {
                        continue;
                    }

                    // Current-fight enemies are normally pulled from live combat memory. The radius
                    // fallback trims stale/remote group entries while still catching nearby bosses
                    // that are not on the chosen extract route.
                    if (!IsNearCurrentFight(enemy, escapingBot, deathPosition))
                    {
                        continue;
                    }

                    float power = GetWeightedEnemyPower(enemy);
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
                Logger.LogError("Failed to calculate current fight enemy average equipment power");
                Logger.LogError(ex);
                return RouteThreatSnapshot.Empty;
            }
        }

        private static void AddBossGroupEnemies(pitAIBossPlayer boss, Dictionary<string, Player> candidates)
        {
            if (boss?.bossGroup?.Enemies == null || candidates == null)
            {
                return;
            }

            foreach (IPlayer enemyRef in boss.bossGroup.Enemies.Keys)
            {
                AddFightEnemyCandidate(enemyRef as Player, candidates);
            }
        }

        private static void AddFollowerMemoryEnemies(
            IEnumerable<BotFollowerPlayer> squadmates,
            Dictionary<string, Player> candidates)
        {
            if (squadmates == null || candidates == null)
            {
                return;
            }

            foreach (BotFollowerPlayer follower in squadmates)
            {
                BotOwner bot = follower?.GetBot();
                AddFightEnemyCandidate(bot?.Memory?.GoalEnemy?.Person as Player, candidates);
                AddFightEnemyCandidate(bot?.Memory?.LastEnemy?.Person as Player, candidates);

                if (bot?.EnemiesController?.EnemyInfos == null)
                {
                    continue;
                }

                foreach (EnemyInfo enemyInfo in bot.EnemiesController.EnemyInfos.Values)
                {
                    if (!IsRecentFightEnemy(enemyInfo))
                    {
                        continue;
                    }

                    AddFightEnemyCandidate(enemyInfo?.Person as Player, candidates);
                }
            }
        }

        private static void AddEnemiesTargetingSquad(
            IEnumerable<Player> alivePlayers,
            HashSet<string> squadProfileIds,
            Dictionary<string, Player> candidates)
        {
            if (alivePlayers == null || squadProfileIds == null || candidates == null)
            {
                return;
            }

            foreach (Player player in alivePlayers)
            {
                BotOwner bot = player?.AIData?.BotOwner;
                EnemyInfo goalEnemy = bot?.Memory?.GoalEnemy;
                string targetProfileId = goalEnemy?.ProfileId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(targetProfileId) && squadProfileIds.Contains(targetProfileId))
                {
                    // GoalEnemy can be a loose investigation target from very far away. Only use
                    // this fallback when the enemy is close enough or has recent active contact.
                    if (IsCommittedSquadTargetingEnemy(goalEnemy, player))
                    {
                        AddFightEnemyCandidate(player, candidates);
                    }
                }
            }
        }

        private static bool IsCommittedSquadTargetingEnemy(EnemyInfo goalEnemy, Player enemy)
        {
            if (goalEnemy == null || enemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return true;
            }

            if (Time.time - goalEnemy.PersonalLastShootTime <= 10f ||
                Time.time - goalEnemy.PersonalLastSeenTime <= 10f)
            {
                return true;
            }

            return goalEnemy.Distance <= CurrentFightTargetingFallbackRadius;
        }

        private static bool IsRecentFightEnemy(EnemyInfo enemyInfo)
        {
            if (enemyInfo == null)
            {
                return false;
            }

            return enemyInfo.IsVisible ||
                   enemyInfo.CanShoot ||
                   Time.time - enemyInfo.PersonalLastSeenTime <= 20f ||
                   Time.time - enemyInfo.PersonalLastShootTime <= 20f;
        }

        private static void AddFightEnemyCandidate(Player enemy, Dictionary<string, Player> candidates)
        {
            if (enemy == null || candidates == null || string.IsNullOrWhiteSpace(enemy.ProfileId))
            {
                return;
            }

            candidates[enemy.ProfileId] = enemy;
        }

        private static bool IsValidDeathEscapeEnemy(pitAIBossPlayer boss, Player player)
        {
            if (player == null ||
                player.ProfileId == boss?.realPlayer?.ProfileId ||
                player.HealthController?.IsAlive != true ||
                !player.IsAI ||
                player.AIData?.BotOwner == null)
            {
                return false;
            }

            BotOwner bot = player.AIData.BotOwner;
            if (BossPlayers.IsFollower(bot))
            {
                return false;
            }

            bool isEnemy = boss?.bossGroup != null &&
                           (boss.bossGroup.IsEnemy(player) || boss.bossGroup.IsPlayerEnemy(player));
            string targetProfileId = bot.Memory?.GoalEnemy?.ProfileId ?? string.Empty;
            bool targetsSquad = targetProfileId == boss?.realPlayer?.ProfileId ||
                                BossPlayers.IsFollowerProfileId(targetProfileId);
            return isEnemy || targetsSquad;
        }

        private static bool IsNearCurrentFight(Player enemy, BotOwner escapingBot, Vector3 deathPosition)
        {
            Vector3 enemyPosition = enemy.Position;
            if ((Flatten(enemyPosition) - Flatten(deathPosition)).sqrMagnitude <= CurrentFightThreatRadius * CurrentFightThreatRadius)
            {
                return true;
            }

            return escapingBot != null &&
                   (Flatten(enemyPosition) - Flatten(escapingBot.Position)).sqrMagnitude <= CurrentFightThreatRadius * CurrentFightThreatRadius;
        }

        private static float GetWeightedEnemyPower(Player player)
        {
            WildSpawnType role = player.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            float roleMultiplier = GetRouteThreatRoleMultiplier(role);
            if (roleMultiplier <= 0f)
            {
                return 0f;
            }

            return (player.AIData?.PowerOfEquipment ?? 0f) * roleMultiplier;
        }

        internal static float GetRouteThreatRoleMultiplier(WildSpawnType role)
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
