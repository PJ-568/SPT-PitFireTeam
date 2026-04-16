using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    /// <summary>
    /// Traces main-weapon slot switches for followers to help debug weapon selection behavior.
    /// </summary>
    internal sealed class FollowerWeaponSwitchTracePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotWeaponSelector), nameof(BotWeaponSelector.TryChangeToSlot));
        }

        [PatchPrefix]
        private static void PatchPrefix(BotWeaponSelector __instance, EquipmentSlot slot)
        {
            try
            {
                // Extract BotOwner from BotWeaponSelector
                BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
                if (botOwner == null)
                {
                    return;
                }

                // Only trace for followers
                if (!BossPlayers.IsFollower(botOwner))
                {
                    return;
                }

                // Log if switching to primary weapon
                if (slot == EquipmentSlot.FirstPrimaryWeapon && botOwner.Memory.GoalEnemy != null)
                {
                    Modules.Logger.LogTrace($"[FollowerWeaponSwitch] {botOwner.Profile?.Nickname ?? botOwner.ProfileId} switching to main (FirstPrimaryWeapon)");
                }
            }
            catch (System.Exception e)
            {
                Modules.Logger.LogError($"[FollowerWeaponSwitchTracePatch] Error: {e}");
            }
        }
    }
}
