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
        private const float OutOfCombatReloadSlotCooldown = 2f;
        private const float OutOfCombatReloadFullCycleCooldown = 30f;
        private const float HealNodeStartTimeout = 4f;

        private static readonly EquipmentSlot[] ReloadSlotOrder =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

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
        private readonly HashSet<string> reloadWeaponsProcessed = new HashSet<string>();
        private readonly Dictionary<EquipmentSlot, float> reloadSlotRetryAfter = new Dictionary<EquipmentSlot, float>();
        private EquipmentSlot? forcedTopOffSlot = null;
        private EquipmentSlot? returnAfterTopOffSlot = null;
        private string? reloadingWeaponId = null;
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

            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 45f : 15f;
            if (isUsingHeal)
            {
                if (healStartAt > 0f && healStartAt + healTimeout < Time.time)
                {
                    AbortHealing();
                    return true;
                }

                return false;
            }

            // Old EndHeal equivalent: no pending heal work -> end heal action.
            if (!hasPendingHealWork)
            {
                CompleteHealing();
                return true;
            }

            if (!canStartHeal && healNodeEnteredAt > 0f && healNodeEnteredAt + HealNodeStartTimeout < Time.time)
            {
                RefreshHealWorkForRetry();
                if (!CanStartVanillaHealNode())
                {
                    return false;
                }

                healNodeEnteredAt = Time.time;
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

        private void AbortHealing()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            healStartAt = 0f;
            healSoftTimeoutAt = 0f;
            healNodeEnteredAt = 0f;
            Utils.FollowerMedical.AbortHealing(BotOwner, recoverDestroyedSurgeryParts: true);
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
            // A patrol entry is one out-of-combat reload window. Exiting/re-entering patrol
            // is the only thing that allows the same carried weapon to be considered again.
            triedFillMagazines = false;
            reloadingInProgress = false;
            reloadSlotsTried.Clear();
            reloadWeaponsProcessed.Clear();
            forcedTopOffSlot = null;
            returnAfterTopOffSlot = null;
            reloadingWeaponId = null;
            nextReloadCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
            nextMagazineFillCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
        }

        private void TryHandleOutOfCombatReload()
        {
            try
            {
                TryHandleOutOfCombatReloadInternal();
            }
            catch (Exception ex)
            {
                LogLayerException("TryHandleOutOfCombatReload", ex);
                ResetReloadState();
                nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
            }
        }

        private void TryHandleOutOfCombatReloadInternal()
        {
            if (BotOwner?.WeaponManager == null) return;
            if (Time.time < nextReloadCheckAt) return;

            var selector = BotOwner.WeaponManager.Selector;
            if (reloadingInProgress)
            {
                // EFT reload completion is not just BotReload.Reloading. Wait for hands/weapon readiness
                // before returning to the previous slot or advancing to the next maintenance slot.
                if (BotOwner.WeaponManager.Reload.Reloading || !BotOwner.WeaponManager.IsWeaponReady)
                {
                    return;
                }

                reloadingInProgress = false;
                MarkReloadWeaponProcessed(reloadingWeaponId);
                reloadingWeaponId = null;
                TryReturnAfterTopOffSwitch(selector);
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (!BotOwner.WeaponManager.IsWeaponReady || BotOwner.WeaponManager.Reload.Reloading) return;
            if (AreAllReloadWeaponsProcessed())
            {
                // Every carried weapon has reached a terminal reload decision for this patrol window.
                nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            // First consume compatible loose ammo into inserted/spare magazines for all carried weapons.
            // Reload-switch decisions below then compare already-prepared magazines.
            if (!triedFillMagazines)
            {
                FollowerOutOfCombatReloadPolicy.TryFillCarriedWeaponMagazines(BotOwner);
                BotOwner.WeaponManager.Reload.TryFillMagazines();
                triedFillMagazines = true;
                nextReloadCheckAt = Time.time + OutOfCombatReloadCheckInterval;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (ShouldReloadCurrentWeaponOutOfCombat())
            {
                // A real reload/swap is starting for the currently selected weapon.
                // The weapon is only marked done after the reload animation has fully settled.
                EquipmentSlot currentSlot = selector.LastEquipmentSlot;
                string? currentWeaponId = GetReloadWeaponId(currentSlot);
                reloadSlotsTried.Add(currentSlot);
                reloadingInProgress = TryForceReloadCurrentWeaponOutOfCombat();
                reloadingWeaponId = reloadingInProgress ? currentWeaponId : null;
                if (forcedTopOffSlot == currentSlot)
                {
                    if (!reloadingInProgress)
                    {
                        MarkReloadSlotFailed(currentSlot);
                        MarkReloadWeaponProcessed(currentWeaponId);
                        TryReturnAfterTopOffSwitch(selector);
                    }

                    forcedTopOffSlot = null;
                }
                else if (!reloadingInProgress)
                {
                    MarkReloadWeaponProcessed(currentWeaponId);
                }

                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            if (TrySelectNextWeaponToTopOff(selector, out bool processedSlot))
            {
                // Switched to a weapon that still needs normal reload work; let EFT finish the switch,
                // then the next patrol tick will start reload from that weapon's slot.
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            if (processedSlot)
            {
                // This slot had no useful reload work after ammo/mag preparation. Wait before the
                // next slot so patrol does not churn through weapon checks in one frame.
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            reloadSlotsTried.Clear();
            returnAfterTopOffSlot = null;
            nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
        }

        private bool ShouldReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            if (currentWeapon == null)
            {
                return false;
            }

            if (IsReloadWeaponProcessed(currentWeapon))
            {
                return false;
            }

            bool forceTopOffCurrentSlot =
                forcedTopOffSlot.HasValue &&
                BotOwner.WeaponManager.Selector?.LastEquipmentSlot == forcedTopOffSlot.Value;

            // Chamber/OnlyBarrel support weapons do not always report magazine-style reload counts.
            if (currentWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                // Chamber, revolver, shotgun, and internal-mag weapons rely on EFT's normal
                // reload path once compatible loose ammo exists.
                return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, currentWeapon);
            }

            MagazineItemClass? currentMagazine = currentWeapon.GetCurrentMagazine();
            int maxBulletCount = currentMagazine?.MaxCount ?? currentWeapon.GetMaxMagazineCount();
            if (maxBulletCount <= 0)
            {
                return false;
            }

            float reloadThreshold = BotOwner.Settings?.FileSettings?.Boss?.PERCENT_BULLET_TO_RELOAD ?? 0.6f;
            float currentRatio = (float)currentWeapon.GetCurrentMagazineCount() / maxBulletCount;
            if (!forceTopOffCurrentSlot && currentRatio >= reloadThreshold)
            {
                return false;
            }

            // For external-magazine weapons, only reload-swap if there is actually a better magazine available.
            // Loose-ammo top-off is handled by TryFillMagazines before this branch runs.
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

            MagazineItemClass? bestMagazine = BotOwner.WeaponManager.Reload.GetMagazineForReload(currentWeapon);
            if (bestMagazine == null || bestMagazine.Count <= currentWeapon.GetCurrentMagazineCount())
            {
                return false;
            }

            if (!BotOwner.WeaponManager.ShootController.CanStartReload())
            {
                return false;
            }

            return BotOwner.WeaponManager.Reload.TryReload();
        }

        private bool TrySelectNextWeaponToTopOff(BotWeaponSelector selector, out bool processedSlot)
        {
            processedSlot = false;
            EquipmentSlot currentSlot = selector.LastEquipmentSlot;

            if (TrySelectWeaponToTopOff(selector, currentSlot, out processedSlot) || processedSlot)
            {
                return !processedSlot;
            }

            foreach (EquipmentSlot slot in ReloadSlotOrder)
            {
                if (slot == currentSlot)
                {
                    continue;
                }

                if (TrySelectWeaponToTopOff(selector, slot, out processedSlot) || processedSlot)
                {
                    return !processedSlot;
                }
            }

            return false;
        }

        private bool TrySelectWeaponToTopOff(BotWeaponSelector selector, EquipmentSlot slot, out bool processedSlot)
        {
            processedSlot = false;
            EquipmentSlot previousSlot = selector.LastEquipmentSlot;
            Weapon? weapon = GetWeaponInSlot(slot);
            if (weapon == null || IsReloadWeaponProcessed(weapon))
            {
                return false;
            }

            if (reloadSlotsTried.Contains(slot))
            {
                return false;
            }

            if (!ShouldReloadWeaponInSlot(slot, weapon))
            {
                // No ammo, no better magazine, or already cooling down: this weapon is done for
                // the current patrol window and should not be reconsidered until patrol restarts.
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            if (previousSlot == slot)
            {
                if (ShouldReloadCurrentWeaponOutOfCombat())
                {
                    return true;
                }

                reloadSlotsTried.Add(slot);
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
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
                if (previousSlot != slot)
                {
                    returnAfterTopOffSlot = previousSlot;
                }
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

            return ShouldReloadWeaponInSlot(slot, weapon);
        }

        private bool ShouldReloadWeaponInSlot(EquipmentSlot slot, Weapon weapon)
        {
            if (IsReloadWeaponProcessed(weapon))
            {
                return false;
            }

            if (IsReloadSlotRetryCoolingDown(slot))
            {
                return false;
            }

            if (weapon.ReloadMode == Weapon.EReloadMode.ExternalMagazine)
            {
                return FollowerOutOfCombatReloadPolicy.HasBetterMagazine(BotOwner, weapon);
            }

            return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, weapon);
        }

        private void MarkReloadSlotFailed(EquipmentSlot slot)
        {
            reloadSlotRetryAfter[slot] = Time.time + OutOfCombatReloadFullCycleCooldown;
            reloadSlotsTried.Add(slot);
        }

        private bool IsReloadSlotRetryCoolingDown(EquipmentSlot slot)
        {
            return reloadSlotRetryAfter.TryGetValue(slot, out float retryAt) && Time.time < retryAt;
        }

        private bool AreAllReloadWeaponsProcessed()
        {
            // Missing slots count as processed; real weapons must be marked by id.
            foreach (EquipmentSlot slot in ReloadSlotOrder)
            {
                if (!IsReloadSlotProcessed(slot))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsReloadSlotProcessed(EquipmentSlot slot)
        {
            Weapon? weapon = GetWeaponInSlot(slot);
            return weapon == null || IsReloadWeaponProcessed(weapon);
        }

        private bool IsReloadWeaponProcessed(Weapon weapon)
        {
            return !string.IsNullOrEmpty(weapon.Id) && reloadWeaponsProcessed.Contains(weapon.Id);
        }

        private string? GetReloadWeaponId(EquipmentSlot slot)
        {
            return GetWeaponInSlot(slot)?.Id;
        }

        private void MarkReloadWeaponProcessed(EquipmentSlot slot)
        {
            MarkReloadWeaponProcessed(GetReloadWeaponId(slot));
        }

        private void MarkReloadWeaponProcessed(string? weaponId)
        {
            if (!string.IsNullOrEmpty(weaponId))
            {
                reloadWeaponsProcessed.Add(weaponId);
            }
        }

        private void TryReturnAfterTopOffSwitch(BotWeaponSelector selector)
        {
            if (!returnAfterTopOffSlot.HasValue ||
                selector == null ||
                selector.LastEquipmentSlot == returnAfterTopOffSlot.Value)
            {
                returnAfterTopOffSlot = null;
                return;
            }

            switch (returnAfterTopOffSlot.Value)
            {
                case EquipmentSlot.FirstPrimaryWeapon:
                    selector.ChangeToMain();
                    break;
                case EquipmentSlot.SecondPrimaryWeapon:
                    selector.TryChangeToSlot(EquipmentSlot.SecondPrimaryWeapon, false);
                    break;
                case EquipmentSlot.Holster:
                    selector.TryChangeToSlot(EquipmentSlot.Holster, false);
                    break;
            }

            returnAfterTopOffSlot = null;
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
        private const int MinimumBetterMagazineGain = 2;

        private static readonly EquipmentSlot[] WeaponSlotsToTopOff =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        public static bool CanTopOffWeapon(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            if (IsLauncherWeapon(weapon))
            {
                // EFT's launcher reload checks can trigger automatic weapon switching when a
                // one-shot launcher cannot reload. Launcher use stays owned by combat objectives.
                return false;
            }

            // External-mag weapons are allowed to reload only through a prepared better magazine.
            // Other reload modes only need compatible loose ammo and then use EFT's normal reload.
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

            if (IsLauncherWeapon(weapon))
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
                if (magazine == null || magazine.Count - currentCount < MinimumBetterMagazineGain)
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

        public static bool TryFillCarriedWeaponMagazines(BotOwner botOwner)
        {
            if (botOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment == null)
            {
                return false;
            }

            bool filledAny = false;
            foreach (EquipmentSlot slot in WeaponSlotsToTopOff)
            {
                Weapon? weapon = botOwner.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem as Weapon;
                if (weapon == null ||
                    IsLauncherWeapon(weapon) ||
                    weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
                {
                    // Chamber/internal non-launcher weapons have no magazine top-off stage; their normal reload consumes loose ammo.
                    continue;
                }

                MagazineItemClass? currentMagazine = weapon.GetCurrentMagazine();
                if (currentMagazine != null && currentMagazine.Count < currentMagazine.MaxCount)
                {
                    filledAny |= TryFillMagazineWithLooseAmmo(botOwner, currentMagazine);
                }

                filledAny |= TryFillReachableSpareMagazines(botOwner, weapon);
            }

            return filledAny;
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

        private static bool TryFillReachableSpareMagazines(BotOwner botOwner, Weapon weapon)
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

            bool filledAny = false;
            List<MagazineItemClass> magazines = new List<MagazineItemClass>();
            botOwner.GetPlayer.InventoryController.GetReachableItemsOfTypeNonAlloc<MagazineItemClass>(
                magazines,
                magazine =>
                    magazine != null &&
                    magazine.Count < magazine.MaxCount &&
                    InteractionsHandlerClass.CheckMoveIgnoringTargetItem(
                        magazine,
                        magazineSlot,
                        botOwner.GetPlayer.InventoryController).Succeeded);

            foreach (MagazineItemClass magazine in magazines)
            {
                // Partial fills are intentional: a 10-round stack should turn a 0/11 pistol mag into 10/11.
                filledAny |= TryFillMagazineWithLooseAmmo(botOwner, magazine);
            }

            return filledAny;
        }

        private static bool TryFillMagazineWithLooseAmmo(BotOwner botOwner, MagazineItemClass magazine)
        {
            if (botOwner?.GetPlayer?.InventoryController == null ||
                magazine?.Cartridges?.Filters == null ||
                magazine.MaxCount <= 0 ||
                magazine.Count >= magazine.MaxCount)
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
                    magazine.Cartridges.Filters.CheckItemFilter(ammo) &&
                    ammo.CheckAction(null).Succeeded,
                null);

            AmmoItemClass? bestAmmo = null;
            foreach (AmmoItemClass ammo in ammos)
            {
                if (bestAmmo == null || ammo.StackObjectsCount > bestAmmo.StackObjectsCount)
                {
                    bestAmmo = ammo;
                }
            }

            if (bestAmmo == null)
            {
                return false;
            }

            int roundsToMove = Math.Min(magazine.MaxCount - magazine.Count, bestAmmo.StackObjectsCount);
            if (roundsToMove <= 0)
            {
                return false;
            }

            var result = magazine.Apply(botOwner.GetPlayer.InventoryController, bestAmmo, roundsToMove, true);
            if (result.Failed)
            {
                return false;
            }

            botOwner.GetPlayer.InventoryController.TryRunNetworkTransaction(result, null);
            return true;
        }

        private static bool IsLauncherWeapon(Weapon weapon)
        {
            return weapon is GrenadeLauncherItemClass ||
                   weapon is RocketLauncherItemClass;
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
