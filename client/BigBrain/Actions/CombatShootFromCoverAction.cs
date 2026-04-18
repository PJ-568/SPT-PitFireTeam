using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromCoverAction : FollowerCombatActionBase
    {
        private readonly GClass277 baseLogic;
        private float aimAlignStartedAt;
        private string? aimAlignEnemyId;

        public CombatShootFromCoverAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass277(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            bool oldCanThrowFromAnyPlace = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE;
            bool oldCanThrowStraightContact = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = false;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = false;
            try
            {
                if (WaitForEnemyAimAlignment(ref aimAlignStartedAt, ref aimAlignEnemyId))
                {
                    return;
                }

                baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
            }
            finally
            {
                BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = oldCanThrowFromAnyPlace;
                BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = oldCanThrowStraightContact;
            }
        }
    }
}
