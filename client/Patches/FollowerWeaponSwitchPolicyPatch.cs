using EFT;
using EFT.InventoryLogic;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal static class FollowerWeaponSwitchPolicyRuntime
    {
        private const float EnemyNullReloadCooldownSeconds = 3.0f;
        private const float MidCombatRecentSeenSeconds = 2.5f;
        private static readonly Dictionary<string, float> EnemyLostAtByFollower = new Dictionary<string, float>();

        public static void UpdateEnemyState(BotOwner botOwner)
        {
            if (botOwner == null)
            {
                return;
            }

            string key = GetFollowerKey(botOwner);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy != null)
            {
                EnemyLostAtByFollower.Remove(key);
                return;
            }

            if (!EnemyLostAtByFollower.ContainsKey(key))
            {
                EnemyLostAtByFollower[key] = Time.time;
            }
        }

        public static bool IsInEnemyNullCooldown(BotOwner botOwner)
        {
            if (botOwner == null)
            {
                return false;
            }

            if (botOwner.Memory?.GoalEnemy != null)
            {
                return false;
            }

            string key = GetFollowerKey(botOwner);
            if (string.IsNullOrEmpty(key) || !EnemyLostAtByFollower.TryGetValue(key, out float lostAt))
            {
                return false;
            }

            return Time.time - lostAt <= EnemyNullReloadCooldownSeconds;
        }

        public static bool ShouldAllowSupportNoAmmoMainSwitch(BotOwner botOwner, BotReload reload)
        {
            if (botOwner == null || reload == null)
            {
                return false;
            }

            BotWeaponManager weaponManager = botOwner.WeaponManager;
            BotWeaponSelector selector = weaponManager?.Selector;
            if (weaponManager == null || selector == null)
            {
                return false;
            }

            // This policy is only for support/secondary -> main fallback routing.
            if (selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            int supportBulletCount = reload.BulletCount;
            if (supportBulletCount > 0)
            {
                return false;
            }

            int mainBulletCount = weaponManager.MainWeaponInfo?.Reload?.BulletCount ?? 0;
            if (mainBulletCount <= 0)
            {
                return false;
            }

            EnemyInfo goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            bool midCombat = goalEnemy.IsVisible || (Time.time - goalEnemy.TimeLastSeen <= MidCombatRecentSeenSeconds);
            return midCombat;
        }

        private static string GetFollowerKey(BotOwner botOwner)
        {
            return botOwner?.ProfileId ?? botOwner?.Profile?.Id ?? string.Empty;
        }
    }

    internal sealed class FollowerSupportNoAmmoMainSwitchPolicyPatch : ModulePatch
    {
        private struct SwitchPolicyState
        {
            public bool OverrodeSetting;
            public bool OriginalValue;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass461), "CanReload", new[]
            {
                typeof(bool),
                typeof(MagazineItemClass).MakeByRefType(),
                typeof(List<AmmoItemClass>).MakeByRefType(),
            });
        }

        [PatchPrefix]
        private static void PatchPrefix(GClass461 __instance, out SwitchPolicyState __state)
        {
            __state = default;

            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return;
            }

            FollowerWeaponSwitchPolicyRuntime.UpdateEnemyState(botOwner);

            bool original = botOwner.Settings?.FileSettings?.Shoot?.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO ?? false;
            if (!original)
            {
                return;
            }

            bool allowSwitch = FollowerWeaponSwitchPolicyRuntime.ShouldAllowSupportNoAmmoMainSwitch(botOwner, __instance);
            if (allowSwitch)
            {
                return;
            }

            __state.OverrodeSetting = true;
            __state.OriginalValue = original;
            botOwner.Settings.FileSettings.Shoot.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO = false;
        }

        [PatchPostfix]
        private static void PatchPostfix(GClass461 __instance, SwitchPolicyState __state)
        {
            if (!__state.OverrodeSetting)
            {
                return;
            }

            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null)
            {
                return;
            }

            botOwner.Settings.FileSettings.Shoot.CHANGE_TO_MAIN_WHEN_SUPPORT_NO_AMMO = __state.OriginalValue;
        }
    }

    internal sealed class FollowerHoldLingerReloadSuppressPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReload), "TryReload", Type.EmptyTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReload __instance, ref bool __result)
        {
            BotOwner botOwner = Traverse.Create(__instance).Field("BotOwner_0").GetValue<BotOwner>();
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            FollowerWeaponSwitchPolicyRuntime.UpdateEnemyState(botOwner);

            if (!FollowerWeaponSwitchPolicyRuntime.IsInEnemyNullCooldown(botOwner))
            {
                return true;
            }

            BotLogicDecision? lastDecision = botOwner.Brain?.LastDecision;
            if (lastDecision == null || lastDecision.Value != BotLogicDecision.holdPosition)
            {
                return true;
            }

#if DEBUG
            Logger.LogInfo($"[WeaponPolicy] suppress reload during hold-linger cooldown follower={botOwner.Profile?.Nickname ?? botOwner.name}");
#endif
            __result = false;
            return false;
        }
    }
}
