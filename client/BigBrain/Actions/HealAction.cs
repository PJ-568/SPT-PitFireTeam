using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla first-aid/surgery logic. Layer end conditions own timeouts and
    /// stuck-state cleanup; this action only lets the medical node update.
    /// </summary>
    internal class HealAction : CustomLogic
    {
        private GClass197 baseLogic;
        public HealAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass197(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
