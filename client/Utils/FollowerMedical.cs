using EFT;
using EFT.HealthSystem;
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

            RestoreUsableBodyPartsToCurrentMaximum(player);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RestoreUsableBodyPartsToCurrentMaximum(bot.AIData.Player);
            }

            RefreshMovementHealthPenalty(player);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RefreshMovementHealthPenalty(bot.AIData.Player);
            }

            RefreshMedicalWork(bot);

            bot.Mover.Pause = false;
            bot.Mover.SetTargetMoveSpeed(1f);
            if (!HasDamagedLeg(player))
            {
                bot.GetPlayer.EnableSprint(true);
            }
            if (
                bot.WeaponManager?.Selector != null &&
                bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                 bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon
            )
            {
                TryReturnToMainWeapon(bot);
            }
        }

        private static void RestoreUsableBodyPartsToCurrentMaximum(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    continue;
                }

                ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
                float missingHealth = health.Maximum - health.Current;
                if (missingHealth.Positive())
                {
                    player.ActiveHealthController.ChangeHealth(part, missingHealth, GClass3051.MedKitUse);
                }
            }
        }

        private static void RefreshMovementHealthPenalty(Player player)
        {
            if (player?.HealthController == null || player.MovementContext == null)
            {
                return;
            }

            bool rightLegDamaged = player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) ||
                                   player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg);
            bool leftLegDamaged = player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) ||
                                  player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg);

            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.RightLegDamaged, rightLegDamaged);
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.LeftLegDamaged, leftLegDamaged);
            player.UpdateSpeedLimitByHealth();

            if (!rightLegDamaged && !leftLegDamaged)
            {
                player.EnableSprint(true);
            }
        }

        private static bool HasDamagedLeg(Player player)
        {
            if (player?.HealthController == null)
            {
                return false;
            }

            return player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) ||
                   player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) ||
                   player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) ||
                   player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg);
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
                // Stale flags will refresh on the next tick.
            }
        }
    }
}
