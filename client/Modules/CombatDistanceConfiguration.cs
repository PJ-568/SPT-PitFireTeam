using System;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Centralized combat distance configuration that adapts based on map context.
    /// Factory maps require tighter distances for close-quarters gameplay.
    /// Larger maps (Customs, Woods, etc.) use standard open-engagement distances.
    /// </summary>
    internal sealed class CombatDistanceConfiguration
    {
        private static CombatDistanceConfiguration? instance;
        public static CombatDistanceConfiguration Instance
        {
            get
            {
                instance ??= new CombatDistanceConfiguration();
                return instance;
            }
        }

        private bool isFactoryMode;

        // Default map settings (Customs, Woods, Interchange, Reserve, etc.)
        private const float DefaultBossCoverSearchRadius = 30f;
        private const float DefaultStartCloseCoverDistance = 25f;
        private const float DefaultVisiblePushDistance = 18f;
        private const float DefaultBlindPushDistance = 32f;
        private const float DefaultCombatCoverMaxDistance = 120f;
        private const float DefaultHealCoverSearchRadius = 30f;
        private const float DefaultHealCoverMaxNavDistance = 35f;
        private const float DefaultHealCoverRetreatDistance = 14f;
        private const float DefaultCloseQuarterDistance = 25f;
        private const float DefaultClosePushDistance = 25f;
        private const float DefaultRegroupNeededDistanceMarksman = 35f;
        private const float DefaultBossSupportShootCoverRadius = 30f;

        // Factory map settings (compressed for close-quarters gameplay)
        private const float FactoryBossCoverSearchRadius = 15f;
        private const float FactoryStartCloseCoverDistance = 12f;
        private const float FactoryVisiblePushDistance = 10f;
        private const float FactoryBlindPushDistance = 18f;
        private const float FactoryCombatCoverMaxDistance = 60f;
        private const float FactoryHealCoverSearchRadius = 18f;
        private const float FactoryHealCoverMaxNavDistance = 20f;
        private const float FactoryHealCoverRetreatDistance = 8f;
        private const float FactoryCloseQuarterDistance = 12f;
        private const float FactoryClosePushDistance = 12f;
        private const float FactoryRegroupNeededDistanceMarksman = 20f;
        private const float FactoryBossSupportShootCoverRadius = 18f;

        public void SetFactoryMode(bool isFactory)
        {
            isFactoryMode = isFactory;
        }

        public void UpdateForCurrentMap(string? mapName)
        {
            bool isFactory = !string.IsNullOrEmpty(mapName) &&
                             (mapName!.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              string.Equals(mapName, "Factory4_day", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(mapName, "Factory4_night", StringComparison.OrdinalIgnoreCase));
            SetFactoryMode(isFactory);
        }

        // Boss and cover search radii
        public float GetBossCoverSearchRadius()
        {
            return isFactoryMode ? FactoryBossCoverSearchRadius : DefaultBossCoverSearchRadius;
        }

        public float GetBossRegroupTriggerDistance()
        {
            return GetBossCoverSearchRadius() * 0.6f;
        }

        // Close-quarter and starting distances
        public float GetStartCloseCoverDistance()
        {
            return isFactoryMode ? FactoryStartCloseCoverDistance : DefaultStartCloseCoverDistance;
        }

        public float GetCloseQuarterDistance()
        {
            return isFactoryMode ? FactoryCloseQuarterDistance : DefaultCloseQuarterDistance;
        }

        public float GetClosePushDistance()
        {
            return isFactoryMode ? FactoryClosePushDistance : DefaultClosePushDistance;
        }

        // Push distances
        public float GetVisiblePushDistance()
        {
            return isFactoryMode ? FactoryVisiblePushDistance : DefaultVisiblePushDistance;
        }

        public float GetBlindPushDistance()
        {
            return isFactoryMode ? FactoryBlindPushDistance : DefaultBlindPushDistance;
        }

        // Combat cover distances
        public float GetCombatCoverMaxDistance()
        {
            return isFactoryMode ? FactoryCombatCoverMaxDistance : DefaultCombatCoverMaxDistance;
        }

        public float GetCombatCoverMaxDistanceSqr()
        {
            float distance = GetCombatCoverMaxDistance();
            return distance * distance;
        }

        // Heal cover distances
        public float GetHealCoverSearchRadius()
        {
            return isFactoryMode ? FactoryHealCoverSearchRadius : DefaultHealCoverSearchRadius;
        }

        public float GetHealCoverMaxNavDistance()
        {
            return isFactoryMode ? FactoryHealCoverMaxNavDistance : DefaultHealCoverMaxNavDistance;
        }

        public float GetHealCoverRetreatDistance()
        {
            return isFactoryMode ? FactoryHealCoverRetreatDistance : DefaultHealCoverRetreatDistance;
        }

        // Marksman-specific distances
        public float GetRegroupNeededDistanceMarksman()
        {
            return isFactoryMode ? FactoryRegroupNeededDistanceMarksman : DefaultRegroupNeededDistanceMarksman;
        }

        public float GetBossSupportShootCoverRadius()
        {
            return isFactoryMode ? FactoryBossSupportShootCoverRadius : DefaultBossSupportShootCoverRadius;
        }

        public bool IsFactoryMode => isFactoryMode;
    }
}
