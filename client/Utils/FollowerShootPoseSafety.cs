using EFT;
using UnityEngine;

namespace pitTeam.Utils
{
    internal static class FollowerShootPoseSafety
    {
        private const float VanillaCrouchProbeHeight = 0.6f;
        private const float CrouchWeaponProbeHeight = 0.95f;

        public static bool HasReliableCrouchLane(BotOwner botOwner, Vector3 target)
        {
            return HasReliablePoseLane(botOwner, target, VanillaCrouchProbeHeight) &&
                   HasReliablePoseLane(botOwner, target, CrouchWeaponProbeHeight);
        }

        public static bool HasReliablePoseLane(BotOwner botOwner, Vector3 target, float probeHeight)
        {
            if (botOwner == null || !IsFinite(target))
            {
                return false;
            }

            Vector3 origin = botOwner.Position + Vector3.up * probeHeight;
            LayerMask mask = botOwner.LookSensor != null
                ? botOwner.LookSensor.Mask
                : LayerMaskClass.HighPolyWithTerrainMask;

            if (!HasExactClearLine(origin, target, mask))
            {
                return false;
            }

            return botOwner.ShootData == null ||
                   !botOwner.ShootData.CheckFriendlyFire(origin, target);
        }

        private static bool HasExactClearLine(Vector3 origin, Vector3 target, LayerMask mask)
        {
            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            return !Physics.Raycast(new Ray(origin, direction), distance, mask);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
