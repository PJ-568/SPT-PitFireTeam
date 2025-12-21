using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Patches
{
    /**
     * This is same as the SAIN Patch, but replicated since we marked our followers as excluded 
     */
    internal class GrenadeThrowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGrenadeController), "DoThrow");
        }

        [PatchPrefix]
        public static bool Patch(BotOwner ___botOwner_0, ref bool __result, BotGrenadeController __instance, ref GrenadeActionType ___GrenadeActionType, ref bool ____checkStop, ref float ____clearTime, ThrowWeapItemClass ___grenade)
        {
            if (__instance.AIGreanageThrowData == null)
            {
                return false;
            }
            if (__instance.CheckPeriodTime())
            {
                return false;
            }
            if (__instance.ThrowindNow == true)
            {
                return false;
            }
            __instance.method_5();
            switch (___GrenadeActionType)
            {
                case GrenadeActionType.ready:
                    {
                        ____checkStop = true;
                        ____clearTime = Time.time + 4f;
                        ___GrenadeActionType = GrenadeActionType.change2grenade;
                        if (___grenade == null)
                        {
                            __instance.method_6(null);
                            return false;
                        }
                        if (__instance.AIGreanageThrowData.GrenadeType != null)
                        {
                            __instance.method_1(__instance.AIGreanageThrowData.GrenadeType.Value);
                        }
                        BotPersonalStats botPersonalStats = ___botOwner_0.BotPersonalStats;
                        if (botPersonalStats != null)
                        {
                            botPersonalStats.GrendateThrow(null);
                        }
                        __instance.ThrowindNow = true;
                        ___botOwner_0.GetPlayer.SetInHandsForQuickUse(___grenade, __instance.method_9);
                        break;
                    }
            }
            return false;
        }
    }
}
