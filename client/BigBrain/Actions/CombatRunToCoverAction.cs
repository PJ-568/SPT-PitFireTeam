using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRunToCoverAction : FollowerCombatActionBase
    {
        private readonly GClass228 baseLogic;

        public CombatRunToCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass228(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass31>(data));
        }
    }
}
