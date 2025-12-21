using EFT;
using EFT.InventoryLogic;

namespace friendlySAIN.Layers.GoonsLayer
{
    public class BirdEyeFollowLayer : BossFollowLayer
    {
        public BirdEyeFollowLayer(BotOwner bot, int priority) : base(bot, priority)
        {
        }

        public override string Name()
        {
            return "BirdEyeFLP";
        }

        public override bool ShallUseNow()
        {

            bool usage = base.ShallUseNow();
            // when switching back to follow layer, ensure BirdEye is using his sniper rifle
            if (usage && botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                botOwner_0.WeaponManager.Selector.TryChangeToMain();
            }

            return usage;
        }
    }
}
