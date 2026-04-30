using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.Talk;
using pitTeam.Components;
using pitTeam.Modules;
using System.Reflection;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerGroupTalkDirectionPatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(GroupTalk), "IsEnemyInDirection", new[] { typeof(Vector3), typeof(float), typeof(float) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN GroupTalk.IsEnemyInDirection for follower direction patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerGroupTalkDirectionPatch), nameof(Postfix_IsEnemyInDirection)));
            Modules.Logger.LogInfo("[Init] SAIN follower group-talk direction patch applied.");
        }

        private static void Postfix_IsEnemyInDirection(GroupTalk __instance, Vector3 enemyPosition, float angle, float threshold, ref bool __result)
        {
            BotOwner botOwner = __instance?.BotOwner;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return;
            }

            Vector3 enemyDirectionFromBot = enemyPosition - botOwner.Transform.position;
            enemyDirectionFromBot.y = 0f;
            if (enemyDirectionFromBot.sqrMagnitude <= 0.0001f)
            {
                __result = false;
                return;
            }

            Vector3 bossLookDirection = boss.realPlayer.MovementContext?.PlayerRealForward ?? boss.realPlayer.LookDirection;
            bossLookDirection.y = 0f;
            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                bossLookDirection = boss.realPlayer.Transform.forward;
                bossLookDirection.y = 0f;
            }

            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                __result = false;
                return;
            }

            Vector3 referenceDirection = Quaternion.Euler(0f, angle, 0f) * bossLookDirection.normalized;
            __result = Vector3.Dot(enemyDirectionFromBot.normalized, referenceDirection) > threshold;
        }
    }
}
