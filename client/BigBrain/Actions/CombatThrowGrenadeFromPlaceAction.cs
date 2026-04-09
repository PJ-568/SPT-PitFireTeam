using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Modules;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatThrowGrenadeFromPlaceAction : FollowerCombatActionBase
    {
        private readonly GClass287 baseLogic;

        public CombatThrowGrenadeFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass287(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerGrenadeRuntimeGate.EnableExplicitThrow(BotOwner);
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
