using System;
using EFT;

namespace friendlySAIN.Modules
{
    public static class SainAddonBridge
    {
        // Addon registers SAIN-specific readiness callback here during startup.
        public static Func<BotOwner, bool>? IsReadyForPatrolAfterCombat { get; set; }
    }
}
