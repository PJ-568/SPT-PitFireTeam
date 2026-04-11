using EFT;
using EFT.InventoryLogic;

namespace friendlySAIN.Utils
{
    internal static class FollowerMedical
    {
        public static void CompleteHealing(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelActiveMedical(bot);

            Player player = bot.GetPlayer;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartBroken(part))
                {
                    player.ActiveHealthController.RemoveNegativeEffects(part);
                }

                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    player.ActiveHealthController.FullRestoreBodyPart(part);
                }
            }

            player.ActiveHealthController.RestoreFullHealth();
            bot.AIData?.Player?.ActiveHealthController?.RestoreFullHealth();

            RefreshMedicalWork(bot);

            bot.Mover.Pause = false;
            bot.Mover.SetTargetMoveSpeed(1f);
            bot.GetPlayer.EnableSprint(true);

            TryReturnToMainWeapon(bot);
        }

        public static void CancelActiveMedical(BotOwner bot)
        {
            if (bot?.Medecine == null)
            {
                return;
            }

            if (bot.Medecine.FirstAid?.Using == true)
            {
                bot.Medecine.FirstAid.CancelCurrent();
            }

            if (bot.Medecine.SurgicalKit?.Using == true)
            {
                bot.Medecine.SurgicalKit.CancelCurrent();
            }

            if (bot.Medecine.Stimulators?.Using == true)
            {
                bot.Medecine.Stimulators.CancelCurrent();
            }
        }

        private static void TryReturnToMainWeapon(BotOwner bot)
        {
            if (bot?.WeaponManager?.Selector == null)
            {
                return;
            }

            bot.WeaponManager.Selector.TakePrevWeapon();

            if (bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                bot.WeaponManager.Selector.TryChangeToMain();
            }
        }

        private static void RefreshMedicalWork(BotOwner bot)
        {
            try
            {
                bot.Medecine.RefreshCurMeds();
                bot.Medecine.GetDamaged();
                bot.Medecine.SurgicalKit.FindDamagedPart();
                bot.Medecine.FirstAid.CheckParts();
            }
            catch
            {
                // Medical state can be mid-transition while a vanilla heal node is being cancelled.
                // Limb/health restoration already happened; stale flags will refresh on the next tick.
            }
        }
    }
}
