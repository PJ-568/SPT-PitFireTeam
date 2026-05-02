using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla peaceful hard-aim behavior. Used as a non-combat watch animation,
    /// not as a combat aiming decision.
    /// </summary>
    internal class PeaceHardAimAction : CustomLogic
    {
        private readonly GClass267 baseLogic;

        public PeaceHardAimAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass267(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
