using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.BigBrain
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
        private const float OutOfCombatReloadInitialCooldown = 1f;
        private const float OutOfCombatReloadCheckInterval = 3f;
        private const float OutOfCombatReloadActionCooldown = 5f;
        private const float OutOfCombatReloadWeaponSwitchCooldown = 0.75f;
        private const float OutOfCombatReloadFullCycleCooldown = 30f;
        private const float HealNodeStartTimeout = 4f;

        private float _nextErrorLogAt;

        private float healSoftTimeoutAt = 0f;
        private float healStartAt = 0f;
        private float healNodeEnteredAt = 0f;
        private bool isHealing = false;
        private bool triedFillMagazines = false;
        private bool reloadingInProgress = false;
        private float nextReloadCheckAt = 0f;
        private float nextMagazineFillCheckAt = 0f;
        private readonly HashSet<EquipmentSlot> reloadSlotsTried = new HashSet<EquipmentSlot>();
        private EquipmentSlot? forcedTopOffSlot = null;
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
            return "pitTeam.FollowerPatrol";
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

            if (followerData.IsBackpackInspectionActive)
            {
                return true;
            }

            if (HasRequestLayerCommand(followerData))
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

        private static bool HasRequestLayerCommand(BotFollowerPlayer followerData)
        {
            if (followerData == null ||
                !followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _))
            {
                return false;
            }

            return command != FollowerCommandType.PushEnemy &&
                   command != FollowerCommandType.SuppressEnemy &&
                   command != FollowerCommandType.NeedSniper;
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
            BossPlayers.Instance?.GetFollower(BotOwner)?.ClearCombatIndependent();
            if (BossPlayers.Instance?.GetFollower(BotOwner)?.IsBackpackInspectionActive != true)
            {
                BotOwner.Mover.Pause = false;
            }
            ResetTiltForPatrol();
            if (BossPlayers.Instance?.GetFollower(BotOwner)?.IsBackpackInspectionActive != true &&
                BotOwner.Mover.TargetPose < 0.85f)
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

        private void ResetTiltForPatrol()
        {
            try
            {
                BotOwner.Tilt?.Stop();
                BotOwner.GetPlayer?.MovementContext?.SetTilt(0f, true);
            }
            catch (Exception ex)
            {
                LogLayerException("ResetTiltForPatrol", ex);
            }
        }

        public override Action GetNextAction()
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (BotOwner.Mover.Pause && followerData?.IsBackpackInspectionActive != true)
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
                        healNodeEnteredAt = Time.time;
                        StopMovementForHealDecision();
                    }

                    isHealing = true;
                    if (healSoftTimeoutAt <= 0f)
                    {
                        healSoftTimeoutAt = Time.time + 20f;
                    }
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
                    if (!IsActive())
                    {
                        return true;
                    }

                    RefreshHealWorkIfNeeded();
                    if (HasPendingHealWork())
                    {
                        return true;
                    }

                    TryHandleOutOfCombatReload();
                    return false;
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

            bool hasHealRelevantDamage = BotOwner.GetPlayer.HealthStatus != ETagStatus.Healthy;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (!BotOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    continue;
                }

                hasHealRelevantDamage = true;
                break;
            }

            if (!hasHealRelevantDamage)
            {
                return;
            }

            nextHealWorkRefreshAt = Time.time + 1f;

            try
            {
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
            }
            catch (Exception ex)
            {
                LogLayerException("RefreshHealWorkIfNeeded", ex);
            }
        }

        private bool HasPendingHealWork()
        {
            return BotOwner?.Medecine != null &&
                   (BotOwner.Medecine.FirstAid?.Using == true ||
                    BotOwner.Medecine.SurgicalKit?.Using == true ||
                    BotOwner.Medecine.FirstAid?.Have2Do == true ||
                    BotOwner.Medecine.SurgicalKit?.HaveWork == true);
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
            bool canStartHeal = CanStartVanillaHealNode();

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

            if (!isUsingHeal && !canStartHeal && healNodeEnteredAt > 0f && healNodeEnteredAt + HealNodeStartTimeout < Time.time)
            {
                RefreshHealWorkForRetry();
                if (!CanStartVanillaHealNode())
                {
                    return false;
                }

                healNodeEnteredAt = Time.time;
            }

            // end heal timeout.
            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 45f : 15f;
            if (isUsingHeal && healStartAt + healTimeout < Time.time)
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
            healNodeEnteredAt = 0f;
            // Normal patrol healing should finish/cancel medical state without restoring all raid HP.
            Utils.FollowerMedical.CompleteHealing(BotOwner);
        }

        private bool CanStartVanillaHealNode()
        {
            try
            {
                if (BotOwner?.Medecine == null ||
                    BotOwner.WeaponManager?.Grenades?.ThrowindNow == true ||
                    BotOwner.Medecine.Using)
                {
                    return false;
                }

                return BotOwner.Medecine.FirstAid?.ShallStartUse() == true ||
                       BotOwner.Medecine.SurgicalKit?.ShallStartUse() == true;
            }
            catch (Exception ex)
            {
                LogLayerException("CanStartVanillaHealNode", ex);
                return false;
            }
        }

        private void RefreshHealWorkForRetry()
        {
            try
            {
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
                BotOwner.Medecine?.FirstAid?.Refresh();
                BotOwner.Medecine?.SurgicalKit?.Refresh();
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
            }
            catch (Exception ex)
            {
                LogLayerException("RefreshHealWorkForRetry", ex);
            }
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
            triedFillMagazines = false;
            reloadingInProgress = false;
            reloadSlotsTried.Clear();
            forcedTopOffSlot = null;
            nextReloadCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
            nextMagazineFillCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
        }

        private void TryHandleOutOfCombatReload()
        {
            if (BotOwner?.WeaponManager == null) return;
            if (Time.time < nextReloadCheckAt) return;
            if (!BotOwner.WeaponManager.IsWeaponReady || BotOwner.WeaponManager.Reload.Reloading) return;

            var selector = BotOwner.WeaponManager.Selector;

            // First top spare magazines with loose ammo. If no loose ammo exists, this no-ops, and
            // the reload pass below will still select the magazine with the most bullets.
            if (!triedFillMagazines)
            {
                BotOwner.WeaponManager.Reload.TryFillMagazines();
                triedFillMagazines = true;
                nextReloadCheckAt = Time.time + OutOfCombatReloadCheckInterval;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (ShouldReloadCurrentWeaponOutOfCombat())
            {
                reloadSlotsTried.Add(selector.LastEquipmentSlot);
                reloadingInProgress = TryForceReloadCurrentWeaponOutOfCombat();
                if (forcedTopOffSlot == selector.LastEquipmentSlot)
                {
                    forcedTopOffSlot = null;
                }

                nextReloadCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            if (reloadingInProgress && !BotOwner.WeaponManager.Reload.Reloading)
            {
                reloadingInProgress = false;
                BotOwner.WeaponManager.Reload.TryFillMagazines();
                nextReloadCheckAt = Time.time + OutOfCombatReloadCheckInterval;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (TrySelectNextWeaponToTopOff(selector))
            {
                triedFillMagazines = false;
                nextReloadCheckAt = Time.time + OutOfCombatReloadWeaponSwitchCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            reloadSlotsTried.Clear();
            triedFillMagazines = false;
            nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
        }

        private bool ShouldReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null)
            {
                return false;
            }

            BotReload reload = BotOwner.WeaponManager.Reload;
            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            if (currentWeapon == null)
            {
                return false;
            }

            int maxBulletCount = reload.MaxBulletCount;
            if (maxBulletCount <= 0)
            {
                return false;
            }

            float reloadThreshold = BotOwner.Settings?.FileSettings?.Boss?.PERCENT_BULLET_TO_RELOAD ?? 0.6f;
            float currentRatio = (float)reload.BulletCount / maxBulletCount;
            bool forceTopOffCurrentSlot =
                forcedTopOffSlot.HasValue &&
                BotOwner.WeaponManager.Selector?.LastEquipmentSlot == forcedTopOffSlot.Value;

            if (!forceTopOffCurrentSlot && currentRatio >= reloadThreshold)
            {
                return false;
            }

            // For external-magazine weapons, only reload-swap if there is actually a better magazine available.
            // Loose-ammo top-off is handled by TryFillMagazines before this branch runs.
            if (currentWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, currentWeapon);
            }

            return FollowerOutOfCombatReloadPolicy.HasBetterMagazine(BotOwner, currentWeapon);
        }

        private bool TryForceReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null ||
                BotOwner.WeaponManager.Reload.Reloading ||
                BotOwner.WeaponManager.ShootController == null)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            if (currentWeapon == null)
            {
                return false;
            }

            if (currentWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return BotOwner.WeaponManager.Reload.TryReload();
            }

            MagazineItemClass? currentMagazine = currentWeapon.GetCurrentMagazine();
            if (currentMagazine == null || currentMagazine.MaxCount <= 0)
            {
                return false;
            }

            MagazineItemClass? bestMagazine = BotOwner.WeaponManager.Reload.GetMagazineForReload(currentWeapon);
            if (bestMagazine == null || bestMagazine.Count <= currentWeapon.GetCurrentMagazineCount())
            {
                return false;
            }

            if (!BotOwner.WeaponManager.ShootController.CanStartReload())
            {
                return false;
            }

            BotOwner.WeaponManager.Reload.Reloading = true;
            BotOwner.WeaponManager.Reload.ReloadMagazine(bestMagazine);
            return true;
        }

        private bool TrySelectNextWeaponToTopOff(BotWeaponSelector selector)
        {
            EquipmentSlot currentSlot = selector.LastEquipmentSlot;

            if (TrySelectWeaponToTopOff(selector, currentSlot))
            {
                return true;
            }

            return TrySelectWeaponToTopOff(selector, EquipmentSlot.FirstPrimaryWeapon) ||
                   TrySelectWeaponToTopOff(selector, EquipmentSlot.SecondPrimaryWeapon) ||
                   TrySelectWeaponToTopOff(selector, EquipmentSlot.Holster);
        }

        private bool TrySelectWeaponToTopOff(BotWeaponSelector selector, EquipmentSlot slot)
        {
            if (reloadSlotsTried.Contains(slot) || !ShouldReloadWeaponInSlot(slot))
            {
                return false;
            }

            if (selector.LastEquipmentSlot == slot)
            {
                if (ShouldReloadCurrentWeaponOutOfCombat())
                {
                    return true;
                }

                reloadSlotsTried.Add(slot);
                return false;
            }

            bool switched = slot switch
            {
                EquipmentSlot.FirstPrimaryWeapon => selector.ChangeToMain(),
                EquipmentSlot.SecondPrimaryWeapon => selector.TryChangeToSlot(EquipmentSlot.SecondPrimaryWeapon, false),
                EquipmentSlot.Holster => selector.TryChangeToSlot(EquipmentSlot.Holster, false),
                _ => false,
            };

            if (switched)
            {
                forcedTopOffSlot = slot;
            }

            return switched;
        }

        private bool ShouldReloadWeaponInSlot(EquipmentSlot slot)
        {
            Weapon? weapon = GetWeaponInSlot(slot);
            if (weapon == null)
            {
                return false;
            }

            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            if (magazine == null || magazine.MaxCount <= 0)
            {
                return false;
            }

            return weapon.GetCurrentMagazineCount() < magazine.MaxCount &&
                   FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, weapon);
        }

        private Weapon? GetWeaponInSlot(EquipmentSlot slot)
        {
            if (BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment == null)
            {
                return null;
            }

            return BotOwner.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem as Weapon;
        }
    }

    internal static class FollowerOutOfCombatReloadPolicy
    {
        public static bool CanTopOffWeapon(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            if (HasBetterMagazine(botOwner, weapon))
            {
                return true;
            }

            MagazineItemClass? currentMagazine = weapon.GetCurrentMagazine();
            int currentCount = GetCurrentLoadedCount(weapon);
            int maxCount = GetCurrentCapacity(weapon, currentMagazine);
            if (maxCount <= 0 || currentCount >= maxCount)
            {
                return false;
            }

            return HasCompatibleLooseAmmo(botOwner, weapon, currentMagazine);
        }

        public static bool HasBetterMagazine(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null)
            {
                return false;
            }

            int currentCount = weapon.GetCurrentMagazineCount();
            List<MagazineItemClass> magazines = new List<MagazineItemClass>();
            botOwner.GetPlayer.InventoryController.GetReachableItemsOfTypeNonAlloc<MagazineItemClass>(magazines, null);
            foreach (MagazineItemClass magazine in magazines)
            {
                if (magazine == null || magazine.Count <= currentCount)
                {
                    continue;
                }

                if (InteractionsHandlerClass.CheckMoveIgnoringTargetItem(
                        magazine,
                        magazineSlot,
                        botOwner.GetPlayer.InventoryController).Succeeded)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCompatibleLooseAmmo(BotOwner botOwner, Weapon weapon, MagazineItemClass? currentMagazine)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            Slot? chamber = weapon.HasChambers && weapon.Chambers?.Length > 0
                ? weapon.Chambers[0]
                : null;
            if (chamber == null && currentMagazine == null)
            {
                return false;
            }

            List<AmmoItemClass> ammos = new List<AmmoItemClass>();
            botOwner.GetPlayer.InventoryController.GetAcceptableItemsNonAlloc<AmmoItemClass>(
                BotReload.AvailableEquipmentSlots,
                ammos,
                ammo =>
                    ammo != null &&
                    ammo.StackObjectsCount > 0 &&
                    ((chamber != null && chamber.CanAccept(ammo)) ||
                     currentMagazine?.Cartridges?.Filters?.CheckItemFilter(ammo) == true) &&
                    ammo.CheckAction(null).Succeeded,
                null);

            return ammos.Count > 0;
        }

        private static int GetCurrentLoadedCount(Weapon weapon)
        {
            if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                return weapon.ChamberAmmoCount;
            }

            return weapon.GetCurrentMagazineCount();
        }

        private static int GetCurrentCapacity(Weapon weapon, MagazineItemClass? currentMagazine)
        {
            if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel && weapon.Chambers != null)
            {
                return weapon.Chambers.Length;
            }

            return currentMagazine?.MaxCount ?? weapon.GetMaxMagazineCount();
        }
    }

}
