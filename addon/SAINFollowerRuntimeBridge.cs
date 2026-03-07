using EFT;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerRuntimeBridge
    {
        // Core plugin calls this bridge when SAIN is installed so SAIN-specific runtime gating stays addon-owned.
        public static bool IsReadyForPatrolAfterCombat(BotOwner owner)
        {
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active)
            {
                return false;
            }

            if (SAINFollowerCombatLayer.IsFollowerCombatLayerActive(owner))
            {
                return false;
            }

            return owner.Memory?.HaveEnemy != true;
        }
    }
}
