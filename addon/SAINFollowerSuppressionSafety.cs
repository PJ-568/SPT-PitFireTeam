using EFT;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerSuppressionSafety
    {
        public static bool IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 targetPosition)
        {
            return SAINFollowerShotSafety.IsFriendlyInShotLane(shooter, targetPosition);
        }
    }
}
