using EFT;
using HarmonyLib;
using SAIN;
using SAIN.Components;
using SAIN.SAINComponent.Classes;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerFriendlyFirePatch
    {
        private const float FriendlyBodyRadius = 0.35f;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(SAINFriendlyFireClass),
                nameof(SAINFriendlyFireClass.CheckFriendlyFire),
                new[] { typeof(Vector3), typeof(float), typeof(Vector3), typeof(BotComponent) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINFriendlyFireClass.CheckFriendlyFire for follower FF patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerFriendlyFirePatch), nameof(Postfix_CheckFriendlyFire)));
            Modules.Logger.LogInfo("[Init] SAIN follower friendly-fire patch applied.");
        }

        private static void Postfix_CheckFriendlyFire(
            Vector3 weaponFirePort,
            float distance,
            Vector3 weaponPointDirection,
            BotComponent bot,
            ref FriendlyFireStatus __result)
        {
            if (__result == FriendlyFireStatus.FriendlyBlock)
            {
                return;
            }

            if (bot?.BotOwner == null || !BossPlayers.IsFollower(bot.BotOwner))
            {
                return;
            }

            if (distance <= 0.05f || weaponPointDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (HasBossOrFollowerInFireLine(bot.BotOwner, weaponFirePort, distance, weaponPointDirection.normalized))
            {
                __result = FriendlyFireStatus.FriendlyBlock;
            }
        }

        private static bool HasBossOrFollowerInFireLine(BotOwner shooter, Vector3 firePort, float distance, Vector3 fireDir)
        {
            if (shooter?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            // Fast no-allocation segment checks are significantly cheaper than per-shot SphereCastAll + collider lookup.
            Vector3 bossCenter = boss.realPlayer.Transform.position + Vector3.up * 1.2f;
            if (IsPointNearFireSegment(firePort, fireDir, distance, bossCenter, FriendlyBodyRadius))
            {
                return true;
            }

            string shooterId = shooter.ProfileId;
            var followers = boss.Followers;
            if (followers == null || followers.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower.IsDead)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(follower.ProfileId) || string.Equals(follower.ProfileId, shooterId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 followerCenter = follower.Position + Vector3.up * 1.2f;
                if (IsPointNearFireSegment(firePort, fireDir, distance, followerCenter, FriendlyBodyRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointNearFireSegment(Vector3 origin, Vector3 directionNormalized, float maxDistance, Vector3 point, float radius)
        {
            Vector3 toPoint = point - origin;
            float along = Vector3.Dot(toPoint, directionNormalized);
            if (along <= 0f || along >= maxDistance)
            {
                return false;
            }

            Vector3 closest = origin + directionNormalized * along;
            float radiusSq = radius * radius;
            return (point - closest).sqrMagnitude <= radiusSq;
        }
    }
}
