using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal static class FollowerLayerRegistry
    {
        private static bool initialized;
        private const int FollowerLayerPriority = 75;

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

            List<string> vanillLayers = new List<string>
            {
                "FightReqNull",
                "PeacecReqNull"
            };

            try
            {
                BrainManager.RemoveLayers(vanillLayers,brains);
                BrainManager.AddCustomLayer(typeof(FollowerPatrolLayer), brains, FollowerLayerPriority);
                Modules.Logger.LogInfo($"Registered follower patrol layer for brains: {string.Join(", ", brains)}");
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

        private float float_4 = 0f;
        private float healStartAt = 0f;
        private bool healing = false;

        private Action? currentAction = null;
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
            healing = false;
            currentAction = null;
            base.Stop();
        }

        public override void Start()
        {
            base.Start();
            healing = false;
            BotOwner.Mover.Pause = false;
            
            BotOwner.PatrollingData?.LootData?.StopLootCluster();
            BotOwner.PatrollingData?.LootData?.SetTargetLootCluster(null);
            BotOwner.PatrollingData?.Pause();

            if (BotOwner.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }

            ResetSainCombatState();
        }

        public override Action GetNextAction()
        {
            bool usingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool haveHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            if (usingHeal || haveHealWork)
            {
                if (!healing)
                {
                    healStartAt = Time.time;
                }

                healing = true;
                float_4 = Time.time + 10f;
                currentAction = new Action(typeof(HealAction), "Heal");   
            }

            healing = false;
            currentAction = new Action(typeof(FollowAction), "FollowerPatrol");

            return currentAction;
        }

        public override bool IsCurrentActionEnding()
        {


            bool isHealAction = currentAction?.Type == typeof(HealAction);
            bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;

            if (!isHealAction && !isHealDecision)
            {
                return !IsActive();
            }

            return EndHealing();
        }

        private bool EndHealing()
        {
            bool isHealAction = currentAction?.Type == typeof(HealAction);

            bool usingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool haveHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            // Old EndHeal equivalent: no pending heal work -> end heal action.
            if (!haveHealWork && !usingHeal)
            {
                healing = false;
                HealBot();
                return true;
            }

            // If heal work is gone but med action is still "using", cancel to avoid stuck animation/state.
            if (usingHeal && !haveHealWork)
            {
                CancelCurrentHeal();
                healing = false;
                HealBot();
                return true;
            }

            // end heal timeout.
            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartAt + healTimeout < Time.time || (healing && Time.time > float_4))
            {
                CancelCurrentHeal();
                healing = false;
                HealBot();
                return true;
            }

            if (!IsActive() && isHealAction)
            {
                CancelCurrentHeal();
                healing = false;
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

        private void ResetSainCombatState()
        {
            try
            {
                if (BotOwner == null) return;

                var mgrType = AccessTools.TypeByName("SAIN.Components.BotManagerComponent");
                if (mgrType == null) return;

                var instProp = AccessTools.Property(mgrType, "Instance");
                var mgr = instProp?.GetValue(null);
                if (mgr == null) return;

                var getSain = AccessTools.Method(mgrType, "GetSAIN");
                if (getSain == null) return;

                object[] args = new object[] { BotOwner, null };
                bool hasSain = (bool)getSain.Invoke(mgr, args);
                if (!hasSain) return;

                var botComp = args[1];
                if (botComp == null) return;

                var botType = botComp.GetType();
                var decision = AccessTools.Property(botType, "Decision")?.GetValue(botComp);
                AccessTools.Method(decision?.GetType(), "ResetDecisions")?.Invoke(decision, new object[] { false });

                var enemyController = AccessTools.Property(botType, "EnemyController")?.GetValue(botComp);
                AccessTools.Method(enemyController?.GetType(), "ClearEnemy")?.Invoke(enemyController, null);

                var search = AccessTools.Property(botType, "Search")?.GetValue(botComp);
                AccessTools.Method(search?.GetType(), "Reset")?.Invoke(search, null);

                var suppression = AccessTools.Property(botType, "Suppression")?.GetValue(botComp);
                AccessTools.Method(suppression?.GetType(), "ResetSuppressing")?.Invoke(suppression, null);

                var manualShoot = AccessTools.Property(botType, "ManualShoot")?.GetValue(botComp);
                AccessTools.Method(manualShoot?.GetType(), "Reset")?.Invoke(manualShoot, null);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("FollowerPatrolLayer.Start: failed to reset SAIN combat state");
                Modules.Logger.LogError(ex);
            }
        }

    }

}
