using EFT;
using HarmonyLib;
using friendlySAIN.Modules;
using SAIN.SAINComponent.Classes.WeaponFunction;
using System;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerRecoilPatch
    {
        private static PropertyInfo? _botOwnerProperty;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(Recoil), nameof(Recoil.ApplyRecoil), new[] { typeof(Vector3) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN Recoil.ApplyRecoil for follower recoil patch.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SAINFollowerRecoilPatch), nameof(Prefix_ApplyRecoil)));
            Modules.Logger.LogInfo("[Init] SAIN follower recoil patch applied.");
        }

        private static bool Prefix_ApplyRecoil(object __instance, Vector3 lookdirection, ref Vector3 __result)
        {
            try
            {
                if (__instance == null)
                {
                    return true;
                }

                _botOwnerProperty ??= AccessTools.Property(__instance.GetType(), "BotOwner");
                BotOwner? owner = _botOwnerProperty?.GetValue(__instance) as BotOwner;
                if (owner == null || !BossPlayers.IsFollower(owner))
                {
                    return true;
                }

                __result = lookdirection;
                return false;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower recoil patch.");
                Modules.Logger.LogError(ex);
            }

            return true;
        }
    }
}
