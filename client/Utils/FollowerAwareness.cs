using Comfort.Common;
using EFT;
using UnityEngine;

namespace friendlySAIN.Utils
{
    internal static class FollowerAwareness
    {
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

        public static void FakeShot(BotOwner bot, Vector3 lookPoint)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (bot.Memory.HaveEnemy && bot.Memory.GoalEnemy != null && bot.Memory.GoalEnemy.IsVisible)
            {
                return;
            }

            var state = GetState(bot);
            if (state == null) return;

            state.GotShotUntil = Time.time + 3f;
            Vector3 botPos = bot.GetPlayer?.Transform?.position ?? bot.Position;
            Vector3 targetDirection = lookPoint - botPos;
            if (targetDirection.sqrMagnitude < 0.01f)
            {
                targetDirection = bot.LookDirection.sqrMagnitude > 0.01f ? bot.LookDirection : Vector3.forward;
                lookPoint = botPos + targetDirection.normalized * 5f;
            }

            bot.Steering.LookToPoint(lookPoint, CalcTurnSpeed(bot.LookDirection, targetDirection));
        }

        public static void SoundHeard(BotOwner bot, Player enemy, Vector3 position, float distance, AISoundType type)
        {
            if (bot == null || enemy == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            var state = GetState(bot);
            if (state == null) return;
            bool gunSound = type == AISoundType.silencedGun || type == AISoundType.gun;
            if (gunSound && Time.time < state.LastGunshotTime + 3f)
            {
                return;
            }

            if (gunSound && !WasRecentlyHit(bot))
            {
                Vector3 lookPoint2 = BuildLookPoint(bot, position, 20f);

                if (distance <= 35f)
                {
                    FakeShot(bot, lookPoint2);
                    if (Utils.GetNavDistance(bot.Position, position) <= 15f)
                    {
                        TryAutoAcquireCloseThreat(bot, enemy, distance, "gunClose");
                    }
                }
                else if (Utils.CanShootToTarget(
                    new ShootPointClass(bot.GetPlayer.MainParts[BodyPartType.head].Position, 1f),
                    enemy.PlayerBones.WeaponRoot.position,
                    bot.LookSensor.Mask
                ))
                {
                    FakeShot(bot, lookPoint2);
                    state.LastGunshotTime = Time.time;
                }
                return;
            }

            if (type != AISoundType.step)
            {
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
                FakeShot(bot, lookPoint);
                TryAutoAcquireCloseThreat(bot, enemy, distance, "stepClose");
            }
            else
            {
                state.LastSoundTime = Time.time;
                FakeShot(bot, lookPoint);
            }
        }

        public static void BulletFelt(BotOwner bot, EftBulletClass bullet, Vector3? impactPoint = null)
        {
            if (bot == null || bullet == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (bot.Memory.HaveEnemy)
            {
                return;
            }

            var state = GetState(bot);
            if (state == null) return;
            if (Time.time < state.NextBulletReactionAt)
            {
                return;
            }

            Player shooter = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(bullet.PlayerProfileID);
            if (shooter == null)
            {
                return;
            }

            bool isEnemy = bot.EnemiesController.IsEnemy(shooter) ||
                (bullet.Player?.iPlayer != null && bot.BotsGroup.IsEnemy(bullet.Player.iPlayer));
            if (!isEnemy)
            {
                return;
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
                return;
            }

            float dispersion = distanceSqr / BulletImpactDispersionSqr;
            Vector3 random = Random.onUnitSphere;
            random.y = 0f;
            random = random.normalized * dispersion;
            Vector3 estimatedShooterPos = shooter.Transform.position + random;

            if (TryAutoAcquireCloseThreat(bot, shooter, Mathf.Sqrt(distanceSqr), "bulletClose"))
            {
                return;
            }
            FakeShot(bot, estimatedShooterPos);
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
                return false;
            }

            EnemyInfo enemyInfo = Enemy.MakeEnemy(bot, enemy);
            enemyInfo?.SetVisible(true);
            return enemyInfo != null;
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
