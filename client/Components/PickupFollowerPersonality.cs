using UnityEngine;

namespace pitTeam.Components
{
    internal static class PickupFollowerPersonality
    {
        internal const float LevelRange = 15f;
        internal const float ProtectBossMinWillingness = 0.45f;
        internal const float RegroupMaxTriggerMultiplier = 2.35f;

        internal static float CalculateHoldAcceptanceChance(
            int followerLevel,
            int playerLevel,
            float independence)
        {
            float chance = 0.78f;
            chance += PlayerAuthority01(followerLevel, playerLevel) * 0.12f;
            chance -= FollowerCockiness01(followerLevel, playerLevel) * 0.42f;
            chance -= independence * 0.22f;
            return Mathf.Clamp(chance, 0.12f, 0.95f);
        }

        internal static float CalculatePushAcceptanceChance(
            int followerLevel,
            int playerLevel,
            float independence,
            float followerPower,
            float enemyPower)
        {
            float chance = 0.35f;
            chance += CalculateGearConfidence01(followerPower, enemyPower) * 0.42f;
            chance += (1f - independence) * 0.15f;
            chance -= PlayerAuthority01(followerLevel, playerLevel) * 0.28f;
            chance -= FollowerCockiness01(followerLevel, playerLevel) * 0.22f;
            return Mathf.Clamp(chance, 0.08f, 0.9f);
        }

        internal static float CalculateBossProtectionWillingness01(
            int followerLevel,
            int playerLevel,
            float independence)
        {
            float willingness = 0.35f;
            willingness += (1f - independence) * 0.35f;
            willingness += PlayerAuthority01(followerLevel, playerLevel) * 0.2f;
            willingness -= FollowerCockiness01(followerLevel, playerLevel) * 0.25f;
            return Mathf.Clamp(willingness, 0.08f, 0.85f);
        }

        internal static float CalculateIndependence01(string seed)
        {
            if (string.IsNullOrEmpty(seed))
            {
                return 0.5f;
            }

            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < seed.Length; i++)
                {
                    hash ^= seed[i];
                    hash *= 16777619u;
                }

                return (hash % 1000u) / 999f;
            }
        }

        private static float PlayerAuthority01(int followerLevel, int playerLevel)
        {
            return Mathf.Clamp01((playerLevel - followerLevel) / LevelRange);
        }

        private static float FollowerCockiness01(int followerLevel, int playerLevel)
        {
            return Mathf.Clamp01((followerLevel - playerLevel) / LevelRange);
        }

        private static float CalculateGearConfidence01(float followerPower, float enemyPower)
        {
            if (enemyPower <= 0f)
            {
                return 0.55f;
            }

            float ratio = followerPower / Mathf.Max(1f, enemyPower);
            return Mathf.InverseLerp(0.55f, 1.45f, ratio);
        }
    }
}
