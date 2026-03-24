using EFT;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINFollowerCombatDefaultBossAction : SAINFollowerCombatRegroupAction
    {
        protected override bool UseVanillaBossFallbackMode => true;

        public SAINFollowerCombatDefaultBossAction(BotOwner botOwner)
            : base(botOwner)
        {
        }
    }
}
