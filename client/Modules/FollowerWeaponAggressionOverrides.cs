using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class FollowerWeaponAggressionOverrides
    {
        private static readonly Dictionary<string, WeaponAggressionOverride> WeaponOverrides =
            new Dictionary<string, WeaponAggressionOverride>(StringComparer.OrdinalIgnoreCase)
            {
                // SR-2M Veresk 9x21 submachine gun.
                { "62e14904c2699c0ec93adc47", new WeaponAggressionOverride(0.6f) }
            };

        public static float Apply(BotOwner? botOwner, float aggression)
        {
            if (!TryGetActiveWeaponRatio(botOwner, out float ratio))
            {
                return Mathf.Clamp(aggression, 0f, 100f);
            }

            return Mathf.Clamp(aggression * ratio, 0f, 100f);
        }

        private static bool TryGetActiveWeaponRatio(BotOwner? botOwner, out float ratio)
        {
            ratio = 1f;
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item ??
                                   botOwner?.WeaponManager?.CurrentWeapon;
            if (activeWeapon == null)
            {
                return false;
            }

            string? templateId = activeWeapon.TemplateId.ToString();
            if (string.IsNullOrWhiteSpace(templateId) ||
                !WeaponOverrides.TryGetValue(templateId, out WeaponAggressionOverride weaponOverride))
            {
                return false;
            }

            ratio = weaponOverride.Ratio;
            return true;
        }

        private readonly struct WeaponAggressionOverride
        {
            public WeaponAggressionOverride(float ratio)
            {
                Ratio = Mathf.Clamp(ratio, 0f, 2f);
            }

            public float Ratio { get; }
        }
    }
}
