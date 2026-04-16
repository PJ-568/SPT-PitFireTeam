using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatSearchAction : FollowerCombatActionBase
    {
        private readonly GClass235 baseLogic;

        public CombatSearchAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass235(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
