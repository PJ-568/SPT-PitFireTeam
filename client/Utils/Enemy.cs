using EFT;
using friendlySAIN.Modules;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace friendlySAIN.Utils
{
    public class Enemy
    {
        private struct CachedEnemyInfo
        {
            public float EnemyCount;
            public Vector3 CachedPosition;

            public CachedEnemyInfo(float enemyCount, Vector3 cachedPosition)
            {
                EnemyCount = enemyCount;
                CachedPosition = cachedPosition;
            }
        }

        private static ConcurrentDictionary<(Vector3, string), CachedEnemyInfo> enemyLocationCache = new ConcurrentDictionary<(Vector3, string), CachedEnemyInfo>();
        private static ConcurrentBag<string> enemies = new ConcurrentBag<string>();

        public enum EnemyDistance
        {
            VeryClose = 0,
            Close = 1,
            Mid = 2,
            Distant = 3,
            Far = 4
        }

        public enum ProxyDistance
        {
            VeryClose = 0,
            Close = 1,
            Mid = 2,
            Distant = 3,
            Far = 4
        }
        public static bool IsClose(BotOwner bot)
        {
            if (!bot.Memory.HaveEnemy) return false;

            EnemyInfo goalEnemy = bot.Memory.GoalEnemy;
            return (Distance(goalEnemy) <= EnemyDistance.Close && goalEnemy.IsVisible) || Distance(goalEnemy) == EnemyDistance.VeryClose;
        }

        public static ProxyDistance DistanceProxy(BotOwner bot, Vector3 position)
        {
            if (!bot.Memory.HaveEnemy) return ProxyDistance.Far;
            Vector3 enemyPosition = bot.Memory.GoalEnemy.CurrPosition;

            float distance = Vector3.Distance(position, enemyPosition);

            if (distance < 17f) return ProxyDistance.VeryClose;

            if (distance <= 30f)
            {
                return ProxyDistance.Close;
            }

            if (distance < 55f)
            {
                return ProxyDistance.Mid;
            }

            if (distance < 110f)
            {
                return ProxyDistance.Distant;
            }

            return ProxyDistance.Far;
        }

        public static EnemyDistance Distance(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return EnemyDistance.Far;
            }

            float distance = goalEnemy.Distance;

            if (distance < 18f) return EnemyDistance.VeryClose;

            if (distance < 31f)
            {
                return EnemyDistance.Close;
            }

            if (distance < 55f)
            {
                return EnemyDistance.Mid;
            }

            if (distance < 96f)
            {
                return EnemyDistance.Distant;
            }

            return EnemyDistance.Far;
        }

        public static float GetEnemiesAtLocation(BotOwner bot, EnemyInfo enemy, Vector3 position, float radius = 17f)
        {
            try
            {
                // Ensure enemy is tracked
                string enemyId = enemy.ProfileId;
                if (!enemies.Contains(enemyId))
                {
                    enemy.Person.OnIPlayerDeadOrUnspawn += (IPlayer pl) =>
                    {
                        ClearEnemyLocations(enemyId);
                        enemies = new ConcurrentBag<string>(enemies.Where(x => x != enemyId));
                    };
                }

                // Use a 10x10x10 grid for caching
                Vector3 cacheKey = new Vector3(
                    Mathf.Floor(position.x / 10f) * 10f,
                    Mathf.Floor(position.y / 10f) * 10f,
                    Mathf.Floor(position.z / 10f) * 10f
                );
                (Vector3, string) cacheKeyWithId = (cacheKey, enemyId);

                // Check cache first
                if (enemyLocationCache.TryGetValue(cacheKeyWithId, out CachedEnemyInfo cachedInfo))
                {
                    return cachedInfo.EnemyCount;
                }

                // Find enemies in range
                Collider[] hits = new Collider[20]; // Pre-allocated array to prevent memory allocation
                int numHits = Physics.OverlapSphereNonAlloc(position, radius, hits, LayerMaskClass.PlayerMask);

                if (numHits == 0)
                {
                    enemyLocationCache[cacheKeyWithId] = new CachedEnemyInfo(0f, cacheKey);
                    return 0;
                }

                int nr = 0;
                HashSet<string> processedEnemies = new HashSet<string>();

                for (int i = 0; i < numHits; i++)
                {
                    Player pl = bot.ShootData.method_4(hits[i]);
                    if (pl == null || !pl.HealthController.IsAlive || processedEnemies.Contains(pl.ProfileId))
                        continue;

                    if (bot.EnemiesController.IsEnemy(pl) ||
                        bot.Settings.FileSettings.Mind.ENEMY_BOT_TYPES.Contains(pl.GetPlayer.Profile.Info.Settings.Role))
                    {
                        if (bot.GetPlayer.ProfileId != enemy.ProfileId &&
                            !(pl.IsAI && bot.BotsGroup.Contains(pl.AIData.BotOwner)) &&
                            !bot.BotsGroup.IsAlly(pl))
                        {
                            nr++;
                            processedEnemies.Add(enemy.ProfileId);
                        }
                    }
                }

                // Store in cache
                enemyLocationCache[cacheKeyWithId] = new CachedEnemyInfo(nr, cacheKey);

                return nr;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("GetEnemiesAtLocation Error");
                Modules.Logger.LogError(ex);
                return 1;
            }
        }


        public static EnemyInfo? MakeEnemy(BotOwner bot, Player enemy, EBotEnemyCause cause = EBotEnemyCause.addPlayerToBoss)
        {
            if (bot == null || enemy == null) return null;

            if (BossPlayers.IsFollower(bot))
            {
                if (BossPlayers.IsPlayerBoss(enemy.ProfileId))
                {
                    return null;
                }

                if (enemy.IsAI && enemy.AIData?.BotOwner != null && BossPlayers.IsFollower(enemy.AIData.BotOwner))
                {
                    return null;
                }

                if (enemy.IsAI)
                {
                    WildSpawnType? role = enemy.Profile?.Info?.Settings?.Role;
                    if (role.HasValue && Props.friendlyBotTypes.Contains(role.Value))
                    {
                        return null;
                    }
                }
            }

            BotSettingsClass groupInfo;
            bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);

            if (groupInfo == null)
            {
                bot.BotsGroup.AddEnemy(enemy, cause);
                bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);

                // Some group validation paths can reject specific causes for same-side hostile AI.
                // Retry with checkAddTODO so BotsGroup can still mark enemy/neutrals/allies correctly.
                if (groupInfo == null && cause != EBotEnemyCause.checkAddTODO)
                {
                    bot.BotsGroup.AddEnemy(enemy, EBotEnemyCause.checkAddTODO);
                    bot.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);
                }
            }

            if (groupInfo == null)
            {
                groupInfo = new BotSettingsClass(enemy, bot.BotsGroup, cause);

                bot.Memory.AddEnemy(enemy, groupInfo, false);
            }

            EnemyInfo info;

            bot.EnemiesController.EnemyInfos.TryGetValue(enemy, out info);

            if (info == null)
            {
                groupInfo.EnemyLastPosition = enemy.Transform.position;
                info = bot.EnemiesController.AddNew(bot.BotsGroup, enemy, groupInfo);

                bot.EnemiesController.SetInfo(enemy, info);
            }

            info.IgnoreUntilAggression = false;

            return info;

        }

        public static void ForceIgnoreUntilAggressionOff(BotOwner bot)
        {
            if (bot?.EnemiesController?.EnemyInfos == null) return;

            foreach (var kv in bot.EnemiesController.EnemyInfos)
            {
                EnemyInfo info = kv.Value;
                if (info != null)
                {
                    info.IgnoreUntilAggression = false;
                }
            }
        }

        public static void ForceIgnoreUntilAggressionOff(BotsGroup group)
        {
            if (group == null) return;

            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                ForceIgnoreUntilAggressionOff(member);
            }
        }

        public static void ClearEnemyLocations(string enemyId)
        {
            var keysToRemove = enemyLocationCache.Keys.Where(key => key.Item2 == enemyId).ToList();
            foreach (var key in keysToRemove)
            {
                enemyLocationCache.TryRemove(key, out _);
            }
        }

        public static void ClearEnemiesLocations()
        {
            enemyLocationCache.Clear();
            enemies = new ConcurrentBag<string>();
        }

        public static bool IsClosestEnemy(BotOwner botOwner_0)
        {
            bool result = true;
            EnemyInfo enemyInfo = botOwner_0.Memory.GoalEnemy;
            foreach (EnemyInfo enemy in botOwner_0.EnemiesController.EnemyInfos.Values)
            {
                if (enemy.Distance < enemyInfo.Distance)
                {
                    result = false;
                    break;
                }
            }

            return result;
        }
    }
}
