using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class FollowerNightVisionActivatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), nameof(BotNightVisionData.Activate));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?.BotOwner_0 == null || !BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                return true;
            }

            try
            {
                __instance.SlotHeadwear = __instance.BotOwner_0.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Headwear);
                if (__instance.SlotHeadwear?.ContainedItem is not CompoundItem headwear)
                {
                    return false;
                }

                NightVisionComponent nightVision = headwear.GetItemComponentsInChildren<NightVisionComponent>(true).FirstOrDefault();
                if (nightVision == null)
                {
                    return false;
                }

                __instance.HaveNightVision = true;
                __instance.NightVisionItem = nightVision;
                __instance.TradableItem = nightVision.Item;
                __instance.NightVisionAtPocket = false;
                __instance.StopTryingMove = false;
                __instance.NextTimeCheck = Time.time + 10f;
                __instance.method_0();
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to initialize follower night vision without stow behavior.");
                Logger.LogError(ex);
            }

            return false;
        }
    }

    internal class FollowerNightVisionOffPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), "method_1");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?.BotOwner_0 == null || !BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                return true;
            }

            try
            {
                TogglableComponent togglable = __instance.NightVisionItem?.Togglable;
                if (togglable?.On == true)
                {
                    togglable.Set(false, false, false);
                }

                __instance.UsingNow = false;
                __instance.NightVisionAtPocket = false;
                __instance.StopTryingMove = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to toggle follower night vision off.");
                Logger.LogError(ex);
            }

            return false;
        }
    }

    internal class FollowerNightVisionOnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotNightVisionData), "method_5");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotNightVisionData __instance)
        {
            if (__instance?.BotOwner_0 == null || !BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                return true;
            }

            try
            {
                TogglableComponent togglable = __instance.NightVisionItem?.Togglable;
                if (togglable?.On == false)
                {
                    togglable.Set(true, false, false);
                }

                __instance.UsingNow = true;
                __instance.NightVisionAtPocket = false;
                __instance.StopTryingMove = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("[NVG] Failed to toggle follower night vision on.");
                Logger.LogError(ex);
            }

            return false;
        }
    }
}
