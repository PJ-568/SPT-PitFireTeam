using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Utils
{
    internal static class FollowerMedical
    {
        private const float FirstAidStuckTimeout = 15f;
        private const float StimulatorStuckTimeout = 3f;
        private const float SurgeryStuckTimeout = 40f;
        private const float HandsAfterMedicalStuckTimeout = 5f;
        private const float RecentMedicalWindow = 8f;
        private const float FirstAidMinVisibleNormalizedHealth = 0.06f;
        private const float EmergencySurgeryHealthPenalty = 0.5f;

        private static readonly EBodyPart[] SurgeryRecoveryParts =
        {
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg,
            EBodyPart.Stomach
        };

        private sealed class MedicalHandsWatchState
        {
            public string? Reason;
            public float StartedAt;
            public float Timeout;
            public float RecentMedicalUntil;
        }

        private static readonly Dictionary<string, MedicalHandsWatchState> HandsWatchStates = new Dictionary<string, MedicalHandsWatchState>();

        public static void ForceHeal(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelAllHealing(bot, recoverDestroyedSurgeryParts: true);

            Player player = bot.GetPlayer;
            RestoreAllBodyPartsToMaximum(player);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RestoreAllBodyPartsToMaximum(bot.AIData.Player);
            }

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: true);
            TryRecoverStuckMedicalHands(bot, "forceHeal");
            TryReturnToMainWeapon(bot);
        }

        public static void CancelAllHealing(BotOwner bot, bool recoverDestroyedSurgeryParts)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelActiveMedical(bot);

            if (recoverDestroyedSurgeryParts)
            {
                NormalizeSurgeryRecoveredPartsForFirstAid(bot.GetPlayer);
                if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }
            }

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: recoverDestroyedSurgeryParts);
            TryReturnToMainWeapon(bot);
        }

        public static void CompleteHealing(BotOwner bot)
        {
            if (bot == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            CancelAllHealing(bot, recoverDestroyedSurgeryParts: true);

            RefreshMedicalWork(bot);
            RefreshBotMovementAfterHealing(bot, ignoreBrokenLegPenalty: true);

            if (
                bot.WeaponManager?.Selector != null &&
                bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                 bot.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon
            )
            {
                TryReturnToMainWeapon(bot);
            }
        }

        public static void UpdateMedicalHandsWatchdog(BotOwner bot)
        {
            if (bot?.ProfileId == null || bot.GetPlayer == null || bot.HealthController?.IsAlive != true)
            {
                return;
            }

            MedicalHandsWatchState state = GetWatchState(bot.ProfileId);
            string? reason = GetMedicalBusyReason(bot, state, out float timeout);
            if (reason == null)
            {
                if (!IsHandsInteractionActive(bot.GetPlayer))
                {
                    ResetWatchState(state);
                }
                return;
            }

            if (!string.Equals(state.Reason, reason, StringComparison.Ordinal))
            {
                state.Reason = reason;
                state.StartedAt = Time.time;
                state.Timeout = timeout;
                return;
            }

            if (state.StartedAt <= 0f)
            {
                state.StartedAt = Time.time;
            }

            if (Time.time - state.StartedAt <= state.Timeout)
            {
                return;
            }

            TryRecoverStuckMedicalHands(bot, reason);
            state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
            ResetWatchState(state);
        }

        private static void RestoreAllBodyPartsToMaximum(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    player.ActiveHealthController.FullRestoreBodyPart(part);
                }

                ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
                float missingHealth = health.Maximum - health.Current;
                if (missingHealth.Positive())
                {
                    player.ActiveHealthController.ChangeHealth(part, missingHealth, GClass3051.MedKitUse);
                }
            }
        }

        private static void NormalizeSurgeryRecoveredPartsForFirstAid(Player player)
        {
            if (player?.ActiveHealthController == null)
            {
                return;
            }

            for (int i = 0; i < SurgeryRecoveryParts.Length; i++)
            {
                EBodyPart part = SurgeryRecoveryParts[i];
                ClampImpossibleBodyPartHealth(player, part);

                if (player.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    player.ActiveHealthController.RestoreBodyPart(part, EmergencySurgeryHealthPenalty);
                    ClampImpossibleBodyPartHealth(player, part);
                }

                ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
                if (health.Maximum <= 0f || health.Current <= 0f)
                {
                    continue;
                }

                float minHealth = GetFirstAidVisibleHealth(player, part);
                if (health.Current < minHealth)
                {
                    player.ActiveHealthController.ChangeHealth(part, minHealth - health.Current, GClass3051.MedKitUse);
                }
            }
        }

        private static float GetFirstAidVisibleHealth(Player player, EBodyPart part)
        {
            ValueStruct health = player.ActiveHealthController.GetBodyPartHealth(part, false);
            return Mathf.Max(1f, health.Maximum * FirstAidMinVisibleNormalizedHealth);
        }

        private static void ClampImpossibleBodyPartHealth(Player player, EBodyPart part)
        {
            if (player?.ActiveHealthController?.Dictionary_0 == null ||
                player.Profile?.Health?.BodyParts == null ||
                !player.Profile.Health.BodyParts.TryGetValue(part, out Profile.ProfileHealthClass.ProfileBodyPartHealthClass profilePart) ||
                profilePart?.Health == null ||
                profilePart.Health.Maximum <= 0f ||
                !player.ActiveHealthController.Dictionary_0.TryGetValue(part, out GClass3009<ActiveHealthController.GClass3008>.BodyPartState state) ||
                state?.Health == null)
            {
                return;
            }

            float profileMaximum = profilePart.Health.Maximum;
            if (state.Health.Maximum <= profileMaximum * 1.1f)
            {
                return;
            }

            float oldMaximum = state.Health.Maximum;
            float current = Mathf.Min(state.Health.Current, profileMaximum);
            float minimum = Mathf.Min(state.Health.Minimum, profileMaximum);
            state.Health = new HealthValue(current, profileMaximum, minimum);
            Modules.Logger.LogInfo($"[Medical] Clamped impossible follower {part} health max from {oldMaximum:0.##} to {profileMaximum:0.##}: {player.Profile?.Nickname ?? player.name}");
        }

        private static void RefreshBotMovementAfterHealing(BotOwner bot, bool ignoreBrokenLegPenalty)
        {
            if (bot?.GetPlayer == null)
            {
                return;
            }

            Player player = bot.GetPlayer;
            RefreshMovementHealthPenalty(player, ignoreBrokenLegPenalty);
            if (bot.AIData?.Player != null && bot.AIData.Player != player)
            {
                RefreshMovementHealthPenalty(bot.AIData.Player, ignoreBrokenLegPenalty);
            }

            if (bot.Mover != null)
            {
                bot.Mover.Pause = false;
                bot.Mover.SprintStopEnd = 0f;
                bot.Mover.SetTargetMoveSpeed(1f);
            }

            if (!HasDamagedLeg(player, ignoreBrokenLegPenalty))
            {
                player.EnableSprint(true);
                if (bot.AIData?.Player != null && bot.AIData.Player != player)
                {
                    bot.AIData.Player.EnableSprint(true);
                }
            }
        }

        private static void RefreshMovementHealthPenalty(Player player, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null || player.MovementContext == null)
            {
                return;
            }

            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.UsingMeds, false);
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.HealingLegs, false);

            bool rightLegDamaged = IsMovementDamagedLeg(player, EBodyPart.RightLeg, ignoreBrokenLegPenalty);
            bool leftLegDamaged = IsMovementDamagedLeg(player, EBodyPart.LeftLeg, ignoreBrokenLegPenalty);

            player.UpdateSpeedLimitByHealth();
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.RightLegDamaged, rightLegDamaged);
            player.MovementContext.SetPhysicalCondition(EPhysicalCondition.LeftLegDamaged, leftLegDamaged);

            if (!rightLegDamaged && !leftLegDamaged)
            {
                player.RemoveStateSpeedLimit(Player.ESpeedLimit.HealthCondition);
                player.EnableSprint(true);
            }
        }

        private static bool HasDamagedLeg(Player player, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null)
            {
                return false;
            }

            return IsMovementDamagedLeg(player, EBodyPart.RightLeg, ignoreBrokenLegPenalty) ||
                   IsMovementDamagedLeg(player, EBodyPart.LeftLeg, ignoreBrokenLegPenalty);
        }

        private static bool IsMovementDamagedLeg(Player player, EBodyPart part, bool ignoreBrokenLegPenalty)
        {
            if (player?.HealthController == null)
            {
                return false;
            }

            if (player.HealthController.IsBodyPartDestroyed(part))
            {
                return true;
            }

            return !ignoreBrokenLegPenalty && player.HealthController.IsBodyPartBroken(part);
        }

        private static string? GetMedicalBusyReason(BotOwner bot, MedicalHandsWatchState state, out float timeout)
        {
            timeout = 0f;

            if (bot.Medecine?.Stimulators?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                timeout = StimulatorStuckTimeout;
                return "stimulator";
            }

            if (bot.Medecine?.FirstAid?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                timeout = FirstAidStuckTimeout;
                return "firstAid";
            }

            if (bot.Medecine?.SurgicalKit?.Using == true)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
                timeout = SurgeryStuckTimeout;
                return "surgery";
            }

            BotLogicDecision currentDecision = bot.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.dogFight;
            bool isHealDecision = currentDecision == BotLogicDecision.heal ||
                                  currentDecision == BotLogicDecision.healStimulators;
            if (isHealDecision)
            {
                state.RecentMedicalUntil = Time.time + RecentMedicalWindow;
            }

            if ((isHealDecision || Time.time < state.RecentMedicalUntil) &&
                IsHandsInteractionActive(bot.GetPlayer))
            {
                timeout = HandsAfterMedicalStuckTimeout;
                return "medicalHands";
            }

            return null;
        }

        private static bool IsHandsInteractionActive(Player player)
        {
            try
            {
                return player?.HandsController != null &&
                       (player.HandsController.IsInInteraction() ||
                        player.HandsController.IsInInteractionStrictCheck());
            }
            catch
            {
                return false;
            }
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

        private static bool TryRecoverStuckMedicalHands(BotOwner bot, string reason)
        {
            Player player = bot?.GetPlayer;
            if (player == null && bot?.ProfileId != null)
            {
                player = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(bot.ProfileId);
            }

            if (player?.InventoryController == null)
            {
                return false;
            }

            try
            {
                CancelActiveMedical(bot);
                NormalizeSurgeryRecoveredPartsForFirstAid(player);
                if (bot?.AIData?.Player != null && bot.AIData.Player != player)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }

                try
                {
                    player.FastForwardCurrentOperations();
                }
                catch
                {
                    // Hands may already be mid-transition; continue with inventory-event cleanup.
                }

                GEventArgs1[] activeEvents = player.InventoryController.List_0.ToArray();
                for (int i = 0; i < activeEvents.Length; i++)
                {
                    player.InventoryController.RemoveActiveEvent(activeEvents[i]);
                }

                player.ProcessStatus = Player.EProcessStatus.None;
                player.SetInventoryOpened(false);
                player.TrySetLastEquippedWeapon(true, null);
                TryReturnToMainWeapon(bot);

                Modules.Logger.LogInfo($"[Medical] Recovered stuck follower hands after {reason}: {bot.Profile?.Nickname ?? bot.name}");
                return true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Medical] Failed to recover stuck follower hands after {reason}: {bot?.Profile?.Nickname ?? bot?.name ?? "<null>"}");
                Modules.Logger.LogError(ex);
                return false;
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

        public static void RefreshMedicalWork(BotOwner bot)
        {
            try
            {
                NormalizeSurgeryRecoveredPartsForFirstAid(bot.GetPlayer);
                if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
                {
                    NormalizeSurgeryRecoveredPartsForFirstAid(bot.AIData.Player);
                }

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

        private static MedicalHandsWatchState GetWatchState(string profileId)
        {
            if (!HandsWatchStates.TryGetValue(profileId, out MedicalHandsWatchState state))
            {
                state = new MedicalHandsWatchState();
                HandsWatchStates[profileId] = state;
            }

            return state;
        }

        private static void ResetWatchState(MedicalHandsWatchState state)
        {
            state.Reason = null;
            state.StartedAt = 0f;
            state.Timeout = 0f;
        }
    }
}
