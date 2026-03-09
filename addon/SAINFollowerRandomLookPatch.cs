using EFT;
using HarmonyLib;
using friendlySAIN.Modules;
using System;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerRandomLookPatch
    {
        private static PropertyInfo? _botOwnerProperty;
        private static PropertyInfo? _botProperty;

        public static void Apply(Harmony harmony)
        {
            Type? randomLookType = AccessTools.TypeByName("SAIN.SAINComponent.Classes.Mover.RandomLookClass");
            if (randomLookType == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN RandomLookClass for follower random-look patch.");
                return;
            }

            MethodInfo? target = AccessTools.Method(randomLookType, "UpdateRandomLook", Type.EmptyTypes);
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find RandomLookClass.UpdateRandomLook for follower random-look patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerRandomLookPatch), nameof(Prefix_UpdateRandomLook)));
            Modules.Logger.LogInfo("[Init] SAIN follower random-look suppression patch applied.");
        }

        private static bool Prefix_UpdateRandomLook(object __instance, ref Vector3? __result)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                BotOwner? owner = ResolveOwner(__instance);
                if (owner == null || !BossPlayers.IsFollower(owner))
                {
                    return true;
                }

                // Suppress SAIN random-look jitter only when follower has a visible target.
                EnemyInfo? goalEnemy = owner.Memory?.GoalEnemy;
                if (owner.Memory?.HaveEnemy == true && goalEnemy?.IsVisible == true)
                {
                    __result = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower random-look suppression patch.");
                Modules.Logger.LogError(ex);
            }

            return true;
        }

        private static BotOwner? ResolveOwner(object instance)
        {
            _botOwnerProperty ??= AccessTools.Property(instance.GetType(), "BotOwner");
            if (_botOwnerProperty?.GetValue(instance) is BotOwner owner)
            {
                return owner;
            }

            _botProperty ??= AccessTools.Property(instance.GetType(), "Bot");
            object? bot = _botProperty?.GetValue(instance);
            if (bot == null)
            {
                return null;
            }

            PropertyInfo? ownerProperty = AccessTools.Property(bot.GetType(), "BotOwner");
            return ownerProperty?.GetValue(bot) as BotOwner;
        }
    }
}
