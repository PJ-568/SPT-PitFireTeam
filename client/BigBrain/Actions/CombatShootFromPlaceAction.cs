using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private readonly GClass276 baseLogic;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            bool oldCanThrowFromAnyPlace = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE;
            bool oldCanThrowStraightContact = BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE = false;
            BotOwner.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT = false;
            try
            {
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
