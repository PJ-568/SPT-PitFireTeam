using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.EnemyClasses;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINEnemyAcquireGatePatch
    {
        private const bool EnableAcquireGateDebugLogs = true;
        private static readonly Dictionary<string, float> NextLogAtByBot = new Dictionary<string, float>();

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(SAINEnemyController), nameof(SAINEnemyController.CheckAddEnemy));
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAINEnemyController.CheckAddEnemy for acquire gate patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINEnemyAcquireGatePatch), nameof(Prefix_CheckAddEnemy)));
            Modules.Logger.LogInfo("[Init] SAIN enemy acquire gate patch applied.");
        }

        private static bool Prefix_CheckAddEnemy(SAINEnemyController __instance, IPlayer IPlayer, ref Enemy __result)
        {
            if (!SAINAddonToggles.EnableForcedEnemyRetention)
            {
                return true;
            }

            BotOwner? owner = __instance?.BotOwner;
            if (owner == null || !BossPlayers.IsFollower(owner))
            {
                return true;
            }

            // Out of combat we rely on vanilla EFT memory/acquisition paths.
            // SAIN acquisition is allowed once vanilla has committed to an enemy.
            if (!SAINFollowerEnemyRetentionService.ShouldAllowAcquire(owner, out string reason))
            {
                TryLogGate(owner, IPlayer, blocked: true, reason: reason);
                __result = null;
                return false;
            }

            TryLogGate(owner, IPlayer, blocked: false, reason: reason);
            return true;
        }

        private static void TryLogGate(BotOwner owner, IPlayer source, bool blocked, string reason)
        {
            if (!EnableAcquireGateDebugLogs || owner == null) return;

            string botId = owner.ProfileId ?? owner.name ?? "<null>";
            float now = Time.time;
            if (NextLogAtByBot.TryGetValue(botId, out float nextAt) && now < nextAt)
            {
                return;
            }
            NextLogAtByBot[botId] = now + 0.5f;

            string srcId = source?.ProfileId ?? "<null>";
            string goalId = owner.Memory?.GoalEnemy?.ProfileId ?? "<none>";
            Modules.Logger.LogInfo(
                $"[SAIN AcquireGate] bot={owner.Profile?.Nickname ?? owner.name} blocked={blocked} reason={reason} " +
                $"haveEnemy={owner.Memory?.HaveEnemy} goal={goalId} src={srcId}");
        }
    }
}
