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

            RaycastHit[] hits = Physics.SphereCastAll(firePort, 0.2f, fireDir, distance, LayerMaskClass.PlayerMask);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            string shooterId = shooter.ProfileId;
            string bossId = boss.realPlayer.ProfileId;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider? collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                Player? player = GameWorldComponent.Instance?.GameWorld?.GetPlayerByCollider(collider);
                if (player == null || string.IsNullOrEmpty(player.ProfileId))
                {
                    continue;
                }

                string targetId = player.ProfileId;
                if (string.Equals(targetId, shooterId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(targetId, bossId, System.StringComparison.Ordinal))
                {
                    return true;
                }

                var followers = boss.Followers;
                if (followers == null || followers.Count == 0)
                {
                    continue;
                }

                for (int j = 0; j < followers.Count; j++)
                {
                    BotOwner follower = followers[j];
                    if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
                    {
                        continue;
                    }

                    if (string.Equals(targetId, follower.ProfileId, System.StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
