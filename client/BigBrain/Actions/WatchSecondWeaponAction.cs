using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
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
