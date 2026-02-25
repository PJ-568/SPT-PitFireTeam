using Comfort.Common;
using EFT;
using UnityEngine;

namespace friendlySAIN.Utils
{
    internal static class FollowerAwareness
    {
        private const bool EnableReactionTrace = false;
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
                Trace(bot, $"FakeShot skip visibleGoal goal={bot.Memory.GoalEnemy.ProfileId}");
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

            Trace(bot, $"FakeShot turn target={Fmt(lookPoint)} recentHitUntil={state.GotShotUntil:F2}");
            bot.Steering.LookToPoint(lookPoint, CalcTurnSpeed(bot.LookDirection, targetDirection));
        }

        public static void SoundHeard(BotOwner bot, Player enemy, Vector3 position, float distance, AISoundType type)
        {
            if (bot == null || enemy == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            var state = GetState(bot);
            if (state == null) return;
            Trace(bot, $"SoundHeard type={type} enemy={enemy.Profile?.Nickname ?? enemy.ProfileId} dist={distance:F1} pos={Fmt(position)} haveEnemy={bot.Memory?.HaveEnemy}");

            bool gunSound = type == AISoundType.silencedGun || type == AISoundType.gun;
            if (gunSound && Time.time < state.LastGunshotTime + 3f)
            {
                Trace(bot, $"SoundHeard skip gun cooldown until={state.LastGunshotTime + 3f:F2}");
                return;
            }

            if (gunSound && !WasRecentlyHit(bot))
            {
                Vector3 lookPoint2 = BuildLookPoint(bot, position, 20f);

                if (distance <= 35f)
                {
                    Trace(bot, $"SoundHeard gun close -> FakeShot + maybe autoAcquire navToSound={Utils.GetNavDistance(bot.Position, position):F1}");
                    FakeShot(bot, lookPoint2);
                    bool autoAcquired = false;
                    if (Utils.GetNavDistance(bot.Position, position) <= 15f)
                    {
                        autoAcquired = TryAutoAcquireCloseThreat(bot, enemy, distance, "gunClose");
                    }
                    Trace(bot, $"SoundHeard result=gunClose {(autoAcquired ? "autoAcquire" : "turnOnly")}");
                }
                else if (Utils.CanShootToTarget(
                    new ShootPointClass(bot.GetPlayer.MainParts[BodyPartType.head].Position, 1f),
                    enemy.PlayerBones.WeaponRoot.position,
                    bot.LookSensor.Mask
                ))
                {
                    Trace(bot, "SoundHeard gun distant LOS -> FakeShot");
                    FakeShot(bot, lookPoint2);
                    state.LastGunshotTime = Time.time;
                    Trace(bot, "SoundHeard result=gunDistantLOS turnOnly");
                }
                else
                {
                    Trace(bot, "SoundHeard gun distant no LOS");
                    Trace(bot, "SoundHeard result=gunDistantNoLOS ignore");
                }
                return;
            }
            else if (gunSound)
            {
                Trace(bot, "SoundHeard gun ignored because WasRecentlyHit");
                Trace(bot, "SoundHeard result=gun ignoreRecentlyHit");
            }

            if (type != AISoundType.step)
            {
                Trace(bot, $"SoundHeard ignore unsupported type={type}");
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
                Trace(bot, $"SoundHeard step skip zoneCooldown dt={Time.time - state.LastSoundTime:F1}");
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
                Trace(bot, "SoundHeard step close -> FakeShot + autoAcquire");
                FakeShot(bot, lookPoint);
                bool autoAcquired = TryAutoAcquireCloseThreat(bot, enemy, distance, "stepClose");
                Trace(bot, $"SoundHeard result=stepClose {(autoAcquired ? "autoAcquire" : "turnOnly")}");
            }
            else
            {
                state.LastSoundTime = Time.time;
                Trace(bot, "SoundHeard step far -> FakeShot only");
                FakeShot(bot, lookPoint);
                Trace(bot, "SoundHeard result=stepFar turnOnly");
            }
        }

        public static void BulletFelt(BotOwner bot, EftBulletClass bullet, Vector3? impactPoint = null)
        {
            if (bot == null || bullet == null || bot.IsDead || bot.BotState != EBotState.Active) return;
            if (bot.Memory.HaveEnemy)
            {
                Trace(bot, "BulletFelt skip alreadyHaveEnemy");
                return;
            }

            var state = GetState(bot);
            if (state == null) return;
            if (Time.time < state.NextBulletReactionAt)
            {
                Trace(bot, $"BulletFelt skip cooldown until={state.NextBulletReactionAt:F2}");
                return;
            }

            Player shooter = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(bullet.PlayerProfileID);
            if (shooter == null)
            {
                Trace(bot, "BulletFelt skip shooterNotFound");
                return;
            }

            bool isEnemy = bot.EnemiesController.IsEnemy(shooter) ||
                (bullet.Player?.iPlayer != null && bot.BotsGroup.IsEnemy(bullet.Player.iPlayer));
            if (!isEnemy)
            {
                Trace(bot, $"BulletFelt skip nonEnemy shooter={shooter.Profile?.Nickname ?? shooter.ProfileId}");
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
                Trace(bot, $"BulletFelt skip tooFar dist={Mathf.Sqrt(distanceSqr):F1}");
                return;
            }

            float dispersion = distanceSqr / BulletImpactDispersionSqr;
            Vector3 random = Random.onUnitSphere;
            random.y = 0f;
            random = random.normalized * dispersion;
            Vector3 estimatedShooterPos = shooter.Transform.position + random;

            Trace(bot, $"BulletFelt react shooter={shooter.Profile?.Nickname ?? shooter.ProfileId} impact={Fmt(impact)} est={Fmt(estimatedShooterPos)}");
            if (TryAutoAcquireCloseThreat(bot, shooter, Mathf.Sqrt(distanceSqr), "bulletClose"))
            {
                Trace(bot, "BulletFelt result=autoAcquire");
                return;
            }
            FakeShot(bot, estimatedShooterPos);
            Trace(bot, "BulletFelt result=turnOnly");
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
                Trace(bot, $"AutoAcquire source={source} skip tooFar dist={distance:F1}");
                return false;
            }

            EnemyInfo enemyInfo = Enemy.MakeEnemy(bot, enemy);
            enemyInfo?.SetVisible(true);
            bool success = enemyInfo != null;
            Trace(bot, $"AutoAcquire source={source} success={success} enemy={enemy.ProfileId} dist={distance:F1}");
            return success;
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
