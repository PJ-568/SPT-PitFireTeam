using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla secondary-weapon watch behavior. Used out of combat when the follower
    /// should visually monitor with the secondary weapon without taking over combat logic.
    /// </summary>
    internal class WatchSecondWeaponAction : CustomLogic
    {
        private readonly GClass271 baseLogic;

        public WatchSecondWeaponAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass271(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(data);
        }
    }
}
