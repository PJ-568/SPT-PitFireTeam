using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal sealed class CommittedCoverPhaseState
    {
        private bool pendingArrival;
        private bool holding;
        private float cooldownUntil;
        private float holdStartedAt;
        private float nextScanAt;
        private float holdTimeoutAt;

        public bool IsPendingArrival => pendingArrival;

        public bool IsHolding => holding;

        public bool IsActive => pendingArrival || holding;

        public bool IsCooldownActive => Time.time < cooldownUntil;

        public bool HasActiveHold => holding && holdStartedAt > 0f;

        public bool CanScan => HasActiveHold && Time.time >= nextScanAt;

        public bool IsHoldExpired => HasActiveHold && holdTimeoutAt > 0f && Time.time >= holdTimeoutAt;

        public void BeginTravel()
        {
            pendingArrival = true;
            holding = false;
            holdStartedAt = 0f;
            nextScanAt = 0f;
            holdTimeoutAt = 0f;
        }

        public bool PromoteToHoldOnArrival()
        {
            if (!pendingArrival)
            {
                return false;
            }

            pendingArrival = false;
            holding = true;
            return true;
        }

        public void BeginHoldLifecycle(float initialSettleSeconds, float maxHoldSeconds)
        {
            if (!holding)
            {
                return;
            }

            holdStartedAt = Time.time;
            nextScanAt = Time.time + Mathf.Max(0f, initialSettleSeconds);
            holdTimeoutAt = maxHoldSeconds > 0f ? Time.time + maxHoldSeconds : 0f;
        }

        public void MarkScanned(float scanIntervalSeconds)
        {
            if (!holding)
            {
                return;
            }

            nextScanAt = Time.time + Mathf.Max(0f, scanIntervalSeconds);
        }

        public void StartCooldown(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            cooldownUntil = Mathf.Max(cooldownUntil, Time.time + seconds);
        }

        public void Clear()
        {
            pendingArrival = false;
            holding = false;
            holdStartedAt = 0f;
            nextScanAt = 0f;
            holdTimeoutAt = 0f;
        }

        public void Reset()
        {
            Clear();
            cooldownUntil = 0f;
        }
    }
}
