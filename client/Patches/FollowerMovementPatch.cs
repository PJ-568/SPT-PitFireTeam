using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal class FollowerSprintPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotMover), nameof(BotMover.Sprint));
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool PatchPrefix(BotMover __instance, bool val)
        {
            try
            {
                if (__instance?.BotOwner_0 == null || !BossPlayers.IsFollower(__instance.BotOwner_0))
                {
                    return true;
                }

                if (__instance.Sprinting == val)
                {
                    return false;
                }

                if (__instance.NoSprint)
                {
                    __instance.Sprinting = false;
                    __instance.Player.EnableSprint(false);
                    return false;
                }

                __instance.Sprinting = val;
                if (val)
                {
                    __instance.BotOwner_0.SetTargetMoveSpeed(1f);
                }
                __instance.Player.EnableSprint(val);
                return false;
            }
            catch (System.Exception ex)
            {
                Logger.LogError(ex);
                return true;
            }
        }
    }

    // Low-overhead fix: keep a tiny forward Direction in SprintState update for peaceful follower chase.
    internal class FollowerSprintStateDirectionPatch : ModulePatch
    {
        private static readonly Dictionary<string, float> NextLogAt = new Dictionary<string, float>();
        private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(MovementContext), "_player");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SprintStateClass), "ManualAnimatorMoveUpdate");
        }

        [PatchPrefix]
        [HarmonyPriority(Priority.First)]
        private static void PatchPrefix(SprintStateClass __instance, float deltaTime)
        {
            try
            {
                if (__instance?.MovementContext == null)
                {
                    return;
                }

                Player player = PlayerField?.GetValue(__instance.MovementContext) as Player;
                BotOwner bot = player?.AIData?.BotOwner;
                if (bot == null || !BossPlayers.IsFollower(bot))
                {
                    return;
                }

                MovementContext movementContext = __instance.MovementContext;
                if (!movementContext.IsSprintEnabled || !movementContext.CanSprint || !movementContext.CanWalk)
                {
                    return;
                }

                Vector2 direction = __instance.Direction;
                if (Mathf.Abs(direction.y) >= 1E-45f)
                {
                    return;
                }

                // Only keep sprint alive when there's clear forward chase intent.
                if (movementContext.MovementDirection.y <= 0.6f || movementContext.ClampedSpeed <= 0.55f)
                {
                    return;
                }

                __instance.Direction = new Vector2(direction.x, 0.01f);

                string key = bot.ProfileId ?? bot.Id.ToString();
                float now = Time.time;
                if (!NextLogAt.TryGetValue(key, out float nextAt) || now >= nextAt)
                {
                    NextLogAt[key] = now + 0.5f;
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError(ex);
            }
        }
    }
}
