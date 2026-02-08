using Comfort.Common;
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
        public static bool Patch(BotOwner ___BotOwner_0, ref bool __result, BotGrenadeController __instance, ref GrenadeActionType ___GrenadeActionType, ref bool ___CheckStop, ref float ___ClearTime, ThrowWeapItemClass ___Grenade)
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
                        ___CheckStop = true;
                        ___ClearTime = Time.time + 4f;
                        ___GrenadeActionType = GrenadeActionType.change2grenade;
                        if (___Grenade == null)
                        {
                            __instance.method_6(null);
                            return false;
                        }
                        if (__instance.AIGreanageThrowData.GrenadeType != null)
                        {
                            __instance.method_1(__instance.AIGreanageThrowData.GrenadeType.Value);
                        }
                        BotPersonalStats botPersonalStats = ___BotOwner_0.BotPersonalStats;
                        if (botPersonalStats != null)
                        {
                            botPersonalStats.GrendateThrow(null);
                        }
                        __instance.ThrowindNow = true;
                        ___BotOwner_0.GetPlayer.SetInHands(___Grenade, new Callback<IHandsThrowController>(__instance.method_9));
                        break;
                    }
            }
            return false;
        }
    }
}
