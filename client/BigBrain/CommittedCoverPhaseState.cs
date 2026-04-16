using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal sealed class CommittedCoverPhaseState
    {
        private bool pendingArrival;
        private bool holding;
        private float cooldownUntil;

        public bool IsPendingArrival => pendingArrival;

        public bool IsHolding => holding;

        public bool IsActive => pendingArrival || holding;

        public bool IsCooldownActive => Time.time < cooldownUntil;

        public void BeginTravel()
        {
            pendingArrival = true;
            holding = false;
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
        }

        public void Reset()
        {
            Clear();
            cooldownUntil = 0f;
        }
    }
}
