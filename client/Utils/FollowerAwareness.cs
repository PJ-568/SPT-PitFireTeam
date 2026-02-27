using Comfort.Common;
using EFT;
using UnityEngine;
using friendlySAIN.Modules;

namespace friendlySAIN.Utils
{
    internal static class FollowerAwareness
    {
        private const bool EnableReactionTrace = true;
        private sealed class State
        {
            public float GotShotUntil;
            public float LastSoundTime;
            public float LastGunshotTime;
            public float NextBulletReactionAt;
            public readonly System.Collections.Generic.List<Vector3> ProcessedSoundZones = new();
        }

        private static readonly System.Collections.Generic.Dictionary<string, State> States = new();
        private const float BulletHearDistanceSqr = 50f * 50f;
        private const float BulletImpactDispersionSqr = 5f * 5f;
        private const float CloseThreatAutoAcquireDistance = 6f;

        public static bool WasRecentlyHit(BotOwner bot)
        {
            var state = GetState(bot);
            return state != null && state.GotShotUntil > Time.time;
        }

        public static bool FakeShot(BotOwner bot, Vector3 lookPoint)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) return false;
            if (bot.Memory.HaveEnemy && bot.Memory.GoalEnemy != null && bot.Memory.GoalEnemy.IsVisible)
            {
                Trace(bot, $"FakeShot ignore visibleGoal={bot.Memory.GoalEnemy.ProfileId}");
                return false;
            }

            var state = GetState(bot);
            if (state == null) return false;

            state.GotShotUntil = Time.time + 3f;
            Vector3 botPos = bot.GetPlayer?.Transform?.position ?? bot.Position;
            Vector3 targetDirection = lookPoint - botPos;
            if (targetDirection.sqrMagnitude < 0.01f)
            {
                targetDirection = bot.LookDirection.sqrMagnitude > 0.01f ? bot.LookDirection : Vector3.forward;
                lookPoint = botPos + targetDirection.normalized * 5f;
            }

            Trace(bot, $"FakeShot turn target={Fmt(lookPoint)}");
            bot.Steering.LookToPoint(lookPoint, CalcTurnSpeed(bot.LookDirection, targetDirection));
            return true;
        }

        public static void SoundHeard(BotOwner bot, Player enemy, Vector3 position, float distance, AISoundType type)
        {
            if (bot == null || enemy == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            var state = GetState(bot);
            if (state == null) return;
            Trace(bot, $"SoundHeard type={type} src={enemy.Profile?.Nickname ?? enemy.ProfileId} dist={distance:F1} haveEnemy={bot.Memory?.HaveEnemy}");
            bool gunSound = type == AISoundType.silencedGun || type == AISoundType.gun;
            if (gunSound && Time.time < state.LastGunshotTime + 3f)
            {
                Trace(bot, $"SoundHeard ignore gunCooldown");
                return;
            }

            if (gunSound && !WasRecentlyHit(bot))
            {
                Vector3 lookPoint2 = BuildLookPoint(bot, position, 20f);
                bool hostileToBossGroup = IsHostileToBossGroup(bot, enemy);
                bool localIsEnemy = bot.EnemiesController.IsEnemy(enemy) || bot.BotsGroup.IsEnemy(enemy);
                Trace(bot, $"SoundHeard gun precheck src={enemy.Profile?.Nickname ?? enemy.ProfileId} dist={distance:F1} localIsEnemy={localIsEnemy} hostileToBossGroup={hostileToBossGroup} haveEnemy={bot.Memory?.HaveEnemy}");

                if (distance <= 35f)
                {
                    bool turned = FakeShot(bot, lookPoint2);
                    bool acquired = false;
                    if (Utils.GetNavDistance(bot.Position, position) <= 15f)
                    {
                        acquired = TryAutoAcquireCloseThreat(bot, enemy, distance, "gunClose");
                        if (!acquired && hostileToBossGroup && CanBotShootEnemy(bot, enemy))
                        {
                            acquired = TryAcquireVisibleHostileOfBossGroup(bot, enemy, "gunCloseVisibleHostile");
                        }
                    }
                    Trace(bot, $"SoundHeard gunClose result turned={turned} autoAcquire={acquired}");
                }
                else if (Utils.CanShootToTarget(
                    new ShootPointClass(bot.GetPlayer.MainParts[BodyPartType.head].Position, 1f),
                    enemy.PlayerBones.WeaponRoot.position,
                    bot.LookSensor.Mask
                ))
                {
                    bool turned = FakeShot(bot, lookPoint2);
                    if (hostileToBossGroup)
                    {
                        TryAcquireVisibleHostileOfBossGroup(bot, enemy, "gunFarVisibleHostile");
                    }
                    state.LastGunshotTime = Time.time;
                    Trace(bot, $"SoundHeard gunFarLOS result turned={turned}");
                }
                else
                {
                    Trace(bot, "SoundHeard gunFar ignore noLOS");
                }
                return;
            }
            else if (gunSound)
            {
                Trace(bot, "SoundHeard gun ignore recentlyHit");
            }

            if (type != AISoundType.step)
            {
                Trace(bot, $"SoundHeard ignore unsupportedType={type}");
                return;
            }

            Vector3 positionZone = new(
                Mathf.Floor(position.x / 8f) * 8f,
                Mathf.Floor(position.y / 8f) * 8f,
                Mathf.Floor(position.z / 8f) * 8f
            );

            bool wasProcessed = state.ProcessedSoundZones.Contains(positionZone);
            if (wasProcessed && Time.time - state.LastSoundTime < 5f)
            {
                Trace(bot, "SoundHeard step ignore zoneCooldown");
                return;
            }

            Vector3 lookPoint = BuildLookPoint(bot, position, 20f);

            if (!wasProcessed)
            {
                state.ProcessedSoundZones.Add(positionZone);
                if (state.ProcessedSoundZones.Count > 20) state.ProcessedSoundZones.RemoveAt(0);
            }

            if (distance <= 8f)
            {
                bool turned = FakeShot(bot, lookPoint);
                bool acquired = TryAutoAcquireCloseThreat(bot, enemy, distance, "stepClose");
                Trace(bot, $"SoundHeard stepClose result turned={turned} autoAcquire={acquired}");
            }
            else
            {
                state.LastSoundTime = Time.time;
                bool turned = FakeShot(bot, lookPoint);
                Trace(bot, $"SoundHeard stepFar result turned={turned}");
            }
        }

        public static void BulletFelt(BotOwner bot, EftBulletClass bullet, Vector3? impactPoint = null)
        {
            if (bot == null || bullet == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (bot.Memory.HaveEnemy)
            {
                Trace(bot, "BulletFelt ignore alreadyHaveEnemy");
                return;
            }

            var state = GetState(bot);
            if (state == null) return;
            if (Time.time < state.NextBulletReactionAt)
            {
                Trace(bot, "BulletFelt ignore cooldown");
                return;
            }

            Player shooter = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(bullet.PlayerProfileID);
            if (shooter == null)
            {
                Trace(bot, "BulletFelt ignore shooterNotFound");
                return;
            }

            bool isEnemy = bot.EnemiesController.IsEnemy(shooter) ||
                (bullet.Player?.iPlayer != null && bot.BotsGroup.IsEnemy(bullet.Player.iPlayer));
            if (!isEnemy)
            {
                bool hostileToBossGroup = IsHostileToBossGroup(bot, shooter);
                if (!hostileToBossGroup)
                {
                    Trace(bot, $"BulletFelt ignore nonEnemy shooter={shooter.Profile?.Nickname ?? shooter.ProfileId}");
                    return;
                }
            }

            state.NextBulletReactionAt = Time.time + 1f;
            Vector3 impact = impactPoint ?? bullet.HitPoint;
            if (impact == Vector3.zero)
            {
                impact = bullet.CurrentPosition;
            }

            float distanceSqr = (impact - bot.Position).sqrMagnitude;
            if (distanceSqr > BulletHearDistanceSqr)
            {
                Trace(bot, $"BulletFelt ignore tooFar dist={Mathf.Sqrt(distanceSqr):F1}");
                return;
            }

            float dispersion = distanceSqr / BulletImpactDispersionSqr;
            Vector3 random = Random.onUnitSphere;
            random.y = 0f;
            random = random.normalized * dispersion;
            Vector3 estimatedShooterPos = shooter.Transform.position + random;

            bool acquired = TryAutoAcquireCloseThreat(bot, shooter, Mathf.Sqrt(distanceSqr), "bulletClose");
            if (acquired)
            {
                Trace(bot, "BulletFelt felt=true turned=false autoAcquire=true");
                return;
            }
            if (CanBotShootEnemy(bot, shooter) && TryAcquireVisibleHostileOfBossGroup(bot, shooter, "bulletVisibleHostile"))
            {
                Trace(bot, "BulletFelt felt=true turned=false autoAcquire=true(hostileVisible)");
                return;
            }
            bool turned = FakeShot(bot, estimatedShooterPos);
            Trace(bot, $"BulletFelt felt=true turned={turned} autoAcquire=false");
        }

        private static void Trace(BotOwner bot, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} {msg}");
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F1},{v.y:F1},{v.z:F1})";
        }

        private static Vector3 BuildLookPoint(BotOwner bot, Vector3 sourceWorldPos, float distance)
        {
            Vector3 botPos = bot.GetPlayer?.Transform?.position ?? bot.Position;
            Vector3 dir = sourceWorldPos - botPos;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = bot.LookDirection.sqrMagnitude > 0.01f ? bot.LookDirection : Vector3.forward;
            }
            return botPos + dir.normalized * distance;
        }

        private static bool TryAutoAcquireCloseThreat(BotOwner bot, Player enemy, float distance, string source)
        {
            if (bot == null || enemy == null) return false;
            if (distance > CloseThreatAutoAcquireDistance)
            {
                Trace(bot, $"AutoAcquire ignore source={source} tooFar dist={distance:F1}");
                return false;
            }

            EnemyInfo enemyInfo = Enemy.MakeEnemy(bot, enemy);
            enemyInfo?.SetVisible(true);
            bool success = enemyInfo != null;
            Trace(bot, $"AutoAcquire source={source} success={success} enemy={enemy.Profile?.Nickname ?? enemy.ProfileId} dist={distance:F1}");
            return success;
        }

        private static bool TryAcquireVisibleHostileOfBossGroup(BotOwner bot, Player enemy, string source)
        {
            if (bot == null || enemy == null) return false;
            EnemyInfo enemyInfo = Enemy.MakeEnemy(bot, enemy);
            enemyInfo?.SetVisible(true);
            bool success = enemyInfo != null;
            Trace(bot, $"AutoAcquire source={source} success={success} enemy={enemy.Profile?.Nickname ?? enemy.ProfileId}");
            return success;
        }

        public static bool IsHostileToBossGroupForReaction(BotOwner followerBot, Player enemyPlayer)
        {
            return IsHostileToBossGroup(followerBot, enemyPlayer);
        }

        private static bool IsHostileToBossGroup(BotOwner followerBot, Player enemyPlayer)
        {
            if (followerBot == null || enemyPlayer == null || !enemyPlayer.IsAI) return false;
            BotOwner enemyBot = enemyPlayer.AIData?.BotOwner;
            if (enemyBot == null) return false;

            var followerData = BossPlayers.Instance?.GetFollower(followerBot);
            var boss = followerData?.GetBoss();
            if (boss?.realPlayer == null) return false;

            if (boss.Followers != null)
            {
                foreach (BotOwner member in boss.Followers)
                {
                    if (member == null || member.IsDead) continue;
                    if (member.ProfileId == enemyBot.ProfileId) continue;
                    if (enemyBot.BotsGroup?.IsEnemy(member.GetPlayer) == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CanBotShootEnemy(BotOwner bot, Player enemy)
        {
            try
            {
                if (bot?.GetPlayer?.MainParts == null || enemy?.PlayerBones?.WeaponRoot == null) return false;
                if (!bot.GetPlayer.MainParts.TryGetValue(BodyPartType.head, out var headPart)) return false;
                return Utils.CanShootToTarget(
                    new ShootPointClass(headPart.Position, 1f),
                    enemy.PlayerBones.WeaponRoot.position,
                    bot.LookSensor.Mask
                );
            }
            catch
            {
                return false;
            }
        }

        private static State GetState(BotOwner bot)
        {
            if (bot == null) return null;
            if (!States.TryGetValue(bot.ProfileId, out State state))
            {
                state = new State();
                States[bot.ProfileId] = state;
            }
            return state;
        }

        private static float CalcTurnSpeed(Vector3 currentLookDirection, Vector3 targetDirection)
        {
            const float min = 125f;
            const float max = 360f;
            const float maxAngle = 150f;
            const float minAngle = 5f;

            float angle = Vector3.Angle(currentLookDirection, targetDirection.normalized);
            if (angle >= maxAngle) return max;
            if (angle <= minAngle) return min;

            float ratio = (angle - minAngle) / (maxAngle - minAngle);
            return Mathf.Lerp(min, max, ratio);
        }
    }
}
