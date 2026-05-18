using EFT;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerSuppressionSafety
    {
        public static bool IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 targetPosition)
        {
            return FollowerShotSafety.IsFriendlyInSuppressionLane(shooter, targetPosition);
        }
    }
}
