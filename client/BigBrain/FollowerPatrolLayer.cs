using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal static class FollowerLayerRegistry
    {
        private static bool initialized;
        private const int FollowerRequestLayerPriority = 73;
        private const int FollowerLayerPriority = 71;
        private const int FollowerCombatLayerPriority = 72;

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

            List<string> pmcCombatBrains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault"
            };

            List<string> vanillaLayersToDisable = new List<string>
            {
                "FightReqNull",
                "PeacecReqNull"
            };

            try
            {
                BrainManager.RemoveLayers(vanillaLayersToDisable, brains);
                BrainManager.AddCustomLayer(typeof(FollowerCombatLayer), pmcCombatBrains, FollowerCombatLayerPriority);
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
        private float _nextErrorLogAt;

        private float healSoftTimeoutAt = 0f;
        private float healStartAt = 0f;
        private bool isHealing = false;
        private bool triedSwitchToMainWeapon = false;
        private bool triedFillMagazines = false;
        private bool reloadingInProgress = false;
        private bool triedReloadSecondaryWeapon = false;
        private float nextReloadCheckAt = 0f;
        private float nextMagazineFillCheckAt = 0f;
        private float nextHealWorkRefreshAt = 0f;
        private bool stoppedForHealDecision = false;
        private bool sawEnemyDuringCurrentCycle = false;
        private BotFollowerPlayer? followerData;

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

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            bool isHealAction = selectedAction?.Type == typeof(HealAction);
            bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;

            // let bot finish healing
            if (isHealAction || isHealDecision)
            {
                return true;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;

            if (BotOwner.Memory.HaveEnemy)
            {
                sawEnemyDuringCurrentCycle = true;
                return false;
            }

            if (HasVisibleKnownEnemy())
            {
                sawEnemyDuringCurrentCycle = true;
                return false;
            }

            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            if (!followerData.IsReadyForPatrolAfterCombat())
            {
                return false;
            }

            if (sawEnemyDuringCurrentCycle)
            {
                sawEnemyDuringCurrentCycle = false;
            }

            return true;
        }

        private bool HasVisibleKnownEnemy()
        {
            try
            {
                var infos = BotOwner?.EnemiesController?.EnemyInfos;
                if (infos == null || infos.Count == 0) return false;

                foreach (var kv in infos)
                {
                    var info = kv.Value;
                    if (info == null) continue;
                    if (info.IsVisible) return true;
                }
            }
            catch
            {
                // Ignore transient enemy-info enumeration issues and keep vanilla behavior.
            }
            return false;
        }

        public override void Stop()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            selectedAction = null;
            ResetReloadState();
            base.Stop();
        }

        public override void Start()
        {
            base.Start();
            isHealing = false;
            stoppedForHealDecision = false;
            ResetReloadState();
            BotOwner.Mover.Pause = false;
            if (BotOwner.Mover.TargetPose < 0.85f)
            {
                BotOwner.SetPose(1f);
            }

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
            if (BotOwner.Mover.Pause)
            {
                BotOwner.Mover.Pause = false;
            }

            try
            {
                RefreshHealWorkIfNeeded();

                bool isUsingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
                bool hasPendingHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

                if (isUsingHeal || hasPendingHealWork)
                {
                    if (!isHealing)
                    {
                        healStartAt = Time.time;
                        StopMovementForHealDecision();
                    }

                    isHealing = true;
                    healSoftTimeoutAt = Time.time + 10f;
                    selectedAction = new Action(typeof(HealAction), "Heal");
                    return selectedAction;
                }

                isHealing = false;
                stoppedForHealDecision = false;


                // put the weapon reload here
                TryHandleOutOfCombatReload();

                selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");

                return selectedAction;
            }
            catch (Exception ex)
            {
                LogLayerException("GetNextAction", ex);
                selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");
                return selectedAction;
            }
        }

        public override bool IsCurrentActionEnding()
        {
            try
            {
                bool isHealAction = selectedAction?.Type == typeof(HealAction);
                bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;

                if (!isHealAction && !isHealDecision)
                {
                    return !IsActive();
                }

                return EndHealing();
            }
            catch (Exception ex)
            {
                LogLayerException("IsCurrentActionEnding", ex);
                return true;
            }
        }

        private void RefreshHealWorkIfNeeded()
        {
            if (BotOwner?.Medecine == null ||
                BotOwner.GetPlayer?.ActiveHealthController == null ||
                !BotOwner.HealthController.IsAlive)
            {
                return;
            }

            if (BotOwner.Medecine.Using ||
                BotOwner.Medecine.FirstAid.Have2Do ||
                BotOwner.Medecine.SurgicalKit.HaveWork ||
                Time.time < nextHealWorkRefreshAt)
            {
                return;
            }

            bool hasDestroyedPart = false;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (!BotOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    continue;
                }

                hasDestroyedPart = true;
                break;
            }

            if (!hasDestroyedPart)
            {
                return;
            }

            nextHealWorkRefreshAt = Time.time + 1f;

            try
            {
                BotOwner.Medecine.RefreshCurMeds();
                BotOwner.Medecine.GetDamaged();
                BotOwner.Medecine.SurgicalKit.FindDamagedPart();
                BotOwner.Medecine.FirstAid.CheckParts();
            }
            catch (Exception ex)
            {
                LogLayerException("RefreshHealWorkIfNeeded", ex);
            }
        }

        private void LogLayerException(string where, Exception ex)
        {
            if (Time.time < _nextErrorLogAt) return;
            _nextErrorLogAt = Time.time + 1f;
            Modules.Logger.LogError($"FollowerPatrolLayer.{where} failed for bot={BotOwner?.Profile?.Nickname ?? BotOwner?.name ?? "<null>"}");
            Modules.Logger.LogError(ex);
        }

        private bool EndHealing()
        {
            bool isHealAction = selectedAction?.Type == typeof(HealAction);

            bool isUsingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool hasPendingHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            // Old EndHeal equivalent: no pending heal work -> end heal action.
            if (!hasPendingHealWork && !isUsingHeal)
            {
                CompleteHealing();
                return true;
            }

            // If heal work is gone but med action is still "using", cancel to avoid stuck animation/state.
            if (isUsingHeal && !hasPendingHealWork)
            {
                CompleteHealing();
                return true;
            }

            // end heal timeout.
            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartAt + healTimeout < Time.time || (isHealing && Time.time > healSoftTimeoutAt))
            {
                CompleteHealing();
                return true;
            }

            if (!IsActive() && isHealAction)
            {
                CompleteHealing();
                return true;
            }
            return false;
        }

        private void CompleteHealing()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            healStartAt = 0f;
            healSoftTimeoutAt = 0f;
            Utils.FollowerMedical.CompleteHealing(BotOwner);
        }

        private void StopMovementForHealDecision()
        {
            if (stoppedForHealDecision || BotOwner == null) return;

            BotOwner.Mover?.Stop();
            if (BotOwner.Mover?.Sprinting == true)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.StopMove();
            stoppedForHealDecision = true;
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
                return;
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
                return;
            }

            if (
                (
                    selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon ||
                    selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon ||
                    selector.LastEquipmentSlot == EquipmentSlot.Holster
                ) &&
                ShouldReloadCurrentWeaponOutOfCombat()
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

        private bool ShouldReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null)
            {
                return false;
            }

            if (!BotOwner.WeaponManager.HaveBullets)
            {
                return true;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            MagazineItemClass currentMagazine = currentWeapon?.GetCurrentMagazine();
            if (currentWeapon == null || currentMagazine == null)
            {
                return false;
            }

            int capacity = currentMagazine.MaxCount;
            if (capacity <= 0)
            {
                return false;
            }

            int currentBullets = currentWeapon.GetCurrentMagazineCount();
            return (float)currentBullets / capacity < 0.35f;
        }
    }

}
