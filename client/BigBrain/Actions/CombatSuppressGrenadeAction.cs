using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Modules;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatSuppressGrenadeAction : FollowerCombatActionBase
    {
        private readonly GClass195 baseLogic;

        public CombatSuppressGrenadeAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass195(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerGrenadeRuntimeGate.EnableExplicitThrow(BotOwner);
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
