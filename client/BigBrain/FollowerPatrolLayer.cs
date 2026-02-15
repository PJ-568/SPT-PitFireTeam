using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal static class FollowerLayerRegistry
    {
        private static bool initialized;
        private const int FollowerRequestLayerPriority = 73;
        private const int FollowerLayerPriority = 72;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;

            List<string> brains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault",
                "Knight",
                "BigPipe",
                "BirdEye"
            };

            List<string> vanillaLayersToDisable = new List<string>
            {
                "FightReqNull",
                "PeacecReqNull"
            };

            try
            {
                BrainManager.RemoveLayers(vanillaLayersToDisable,brains);
                BrainManager.AddCustomLayer(typeof(FollowerRequestLayer), brains, FollowerRequestLayerPriority);
                BrainManager.AddCustomLayer(typeof(FollowerPatrolLayer), brains, FollowerLayerPriority);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to register follower patrol layer for brains: {string.Join(", ", brains)}");
                Modules.Logger.LogError(ex);
            }
        }

    }

    internal sealed class FollowerPatrolLayer : CustomLayer
    {

        private float healSoftTimeoutAt = 0f;
        private float healStartAt = 0f;
        private bool isHealing = false;
        private bool triedSwitchToMainWeapon = false;
        private bool triedFillMagazines = false;
        private bool reloadingInProgress = false;
        private bool triedReloadSecondaryWeapon = false;
        private float nextReloadCheckAt = 0f;
        private float nextMagazineFillCheckAt = 0f;

        private Action? selectedAction = null;
        public FollowerPatrolLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "friendlySAIN.FollowerPatrol";
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;
            if (BotOwner.Memory.HaveEnemy) return false;

            return true;
        }

        public override void Stop()
        {
            isHealing = false;
            selectedAction = null;
            ResetReloadState();
            base.Stop();
        }

        public override void Start()
        {
            base.Start();
            isHealing = false;
            ResetReloadState();
            BotOwner.Mover.Pause = false;
            
            BotOwner.PatrollingData?.Pause();

            if (BotOwner.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }

            Utils.FollowerRecovery.SoftReset(BotOwner);

            BotLogicDecision logicDecision = BotOwner.Brain.Agent.LastResult().Action;
            if (BotOwner.Brain.Agent.Dictionary_0.TryGetValue(logicDecision, out var logicInstance))
            {
                logicInstance.Dispose();
            }
        }

        public override Action GetNextAction()
        {
            bool isUsingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool hasPendingHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            if (isUsingHeal || hasPendingHealWork)
            {
                if (!isHealing)
                {
                    healStartAt = Time.time;
                }

                isHealing = true;
                healSoftTimeoutAt = Time.time + 10f;
                selectedAction = new Action(typeof(HealAction), "Heal");
                return selectedAction;
            }

            isHealing = false;


            // put the weapon reload here
            TryHandleOutOfCombatReload();

            selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");

            return selectedAction;
        }

        public override bool IsCurrentActionEnding()
        {
            bool isHealAction = selectedAction?.Type == typeof(HealAction);
            bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;

            if (!isHealAction && !isHealDecision)
            {
                return !IsActive();
            }

            return EndHealing();
        }

        private bool EndHealing()
        {
            bool isHealAction = selectedAction?.Type == typeof(HealAction);

            bool isUsingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool hasPendingHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            // Old EndHeal equivalent: no pending heal work -> end heal action.
            if (!hasPendingHealWork && !isUsingHeal)
            {
                isHealing = false;
                HealBot();
                return true;
            }

            // If heal work is gone but med action is still "using", cancel to avoid stuck animation/state.
            if (isUsingHeal && !hasPendingHealWork)
            {
                CancelCurrentHeal();
                isHealing = false;
                HealBot();
                return true;
            }

            // end heal timeout.
            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartAt + healTimeout < Time.time || (isHealing && Time.time > healSoftTimeoutAt))
            {
                CancelCurrentHeal();
                isHealing = false;
                HealBot();
                return true;
            }

            if (!IsActive() && isHealAction)
            {
                CancelCurrentHeal();
                isHealing = false;
                return true;
            }
            return false;
        }

        private void HealBot()
        {
            if(BotOwner == null || BotOwner.GetPlayer == null || !BotOwner.HealthController.IsAlive) return;
            var player = BotOwner.GetPlayer;

            foreach (var part in GClass3058.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartBroken(part)) player.ActiveHealthController.RemoveNegativeEffects(part);
                if (player.ActiveHealthController.IsBodyPartDestroyed(part)) player.ActiveHealthController.RestoreBodyPart(part, 0);
            }

            BotOwner.AIData.Player.ActiveHealthController.RestoreFullHealth();

            BotOwner.WeaponManager.Selector.TakePrevWeapon();

            if (BotOwner.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                BotOwner.WeaponManager.Selector.TryChangeToMain();
            }
        }

        private void CancelCurrentHeal()
        {
            if (BotOwner.Medecine.FirstAid.Using)
            {
                BotOwner.Medecine.FirstAid.CancelCurrent();
            }
            else if (BotOwner.Medecine.SurgicalKit.Using)
            {
                BotOwner.Medecine.SurgicalKit.CancelCurrent();
            }
        }

        private void ResetReloadState()
        {
            triedSwitchToMainWeapon = false;
            triedFillMagazines = false;
            reloadingInProgress = false;
            triedReloadSecondaryWeapon = false;
            nextReloadCheckAt = Time.time + 5f;
            nextMagazineFillCheckAt = Time.time + 5f;
        }

        private void TryHandleOutOfCombatReload()
        {
            if (BotOwner?.WeaponManager == null) return;
            if (Time.time < nextReloadCheckAt) return;
            if (!BotOwner.WeaponManager.IsWeaponReady || BotOwner.WeaponManager.Reload.Reloading) return;

            var selector = BotOwner.WeaponManager.Selector;

            if (
                !triedReloadSecondaryWeapon &&
                selector.CanChangeToSecondWeapons &&
                selector.SecondPrimaryWeaponItem != null &&
                selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon &&
                selector.SecondPrimaryWeaponItem.GetCurrentMagazine() != null &&
                selector.SecondPrimaryWeaponItem is Weapon secondWeapon &&
                selector.SecondPrimaryWeaponItem.GetCurrentMagazine().MaxCount != secondWeapon.GetCurrentMagazineCount()
            )
            {
                selector.TryChangeWeapon(true);
                triedReloadSecondaryWeapon = true;
                reloadingInProgress = true;
                nextReloadCheckAt = Time.time + 5f;
                nextMagazineFillCheckAt = Time.time + 5f;
            }

            if (
                !reloadingInProgress &&
                !triedSwitchToMainWeapon &&
                (
                    selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon ||
                    selector.LastEquipmentSlot == EquipmentSlot.Holster
                )
            )
            {
                selector.TryChangeToMain();
                triedSwitchToMainWeapon = true;
                nextReloadCheckAt = Time.time + 5f;
                nextMagazineFillCheckAt = Time.time + 5f;
            }

            if (
                Time.time > nextReloadCheckAt &&
                (
                    selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon ||
                    selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon ||
                    selector.LastEquipmentSlot == EquipmentSlot.Holster
                ) &&
                BotOwner.WeaponManager.IsWeaponReady &&
                (float)BotOwner.WeaponManager.Reload.BulletCount / BotOwner.WeaponManager.Reload.MaxBulletCount < 0.35f
            )
            {
                nextReloadCheckAt = Time.time + 30f;
                reloadingInProgress = BotOwner.WeaponManager.Reload.TryReload();
                nextMagazineFillCheckAt = Time.time + 10f;
            }

            if (reloadingInProgress && !BotOwner.WeaponManager.Reload.Reloading)
            {
                reloadingInProgress = false;
            }

            if (
                Time.time > nextMagazineFillCheckAt &&
                BotOwner.WeaponManager.IsWeaponReady &&
                !BotOwner.WeaponManager.Reload.Reloading &&
                !triedFillMagazines
            )
            {
                BotOwner.WeaponManager.Reload.TryFillMagazines();
                triedFillMagazines = true;
                nextMagazineFillCheckAt = Time.time + 20f;
            }
        }
    }

}
