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

            /* try
            {
                if (!BotOwner.Medecine.FirstAid.Using && !BotOwner.Medecine.SurgicalKit.Using)
                {
                    ClearActiveLayerPointers();
                }
                DisableExfiltrationLayers();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("FollowerPatrolLayer.Start: failed to reset brain state");
                Modules.Logger.LogError(ex);
            }

            ResetSainCombatState(); */
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
                return new Action(typeof(HealAction), "Heal");   
            }

            healing = false;
            return new Action(typeof(FollowAction), "FollowerPatrol");
        }

        public override bool IsCurrentActionEnding()
        {
            if (!healing)
            {
                return !IsActive();
            }

            bool usingHeal = BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using;
            bool haveHealWork = BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;

            // Old EndHeal equivalent: no pending heal work -> end heal action.
            if (healing && !haveHealWork && !usingHeal)
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

            // Old EndHeal timeout equivalent.
            float healTimeout = BotOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartAt + healTimeout < Time.time || (healing && Time.time > float_4))
            {
                CancelCurrentHeal();
                healing = false;
                HealBot();
                return true;
            }

            if (!IsActive() && healing)
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

        private void ClearActiveLayerPointers()
        {
            var brain = BotOwner?.Brain?.BaseBrain;
            var agent = BotOwner?.Brain?.Agent;
            if (brain == null || agent == null) return;

            var activeLayerField = AccessTools.Field(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "Gclass35_0");
            activeLayerField?.SetValue(brain, null);

            var agentActiveLayerField = AccessTools.Field(typeof(AICoreAgentClass<BotLogicDecision>), "Gclass35_0");
            agentActiveLayerField?.SetValue(agent, null);

            agent.UsingLayer = string.Empty;

            var lastResultField = AccessTools.Field(typeof(AICoreAgentClass<BotLogicDecision>), "Gstruct8_0");
            if (lastResultField != null)
            {
                var defaultResult = default(AICoreActionResultStruct<BotLogicDecision, GClass26>);
                lastResultField.SetValue(agent, defaultResult);
            }
        }

        private void DisableExfiltrationLayers()
        {
            var brain = BotOwner?.Brain?.BaseBrain;
            if (brain == null) return;

            var toDeactivate = new List<int>();
            foreach (var kvp in brain.Dictionary_0)
            {
                if (kvp.Value == null) continue;
                string name = kvp.Value.Name();
                if (string.IsNullOrEmpty(name)) continue;

                if (name.IndexOf("Exfil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Extract", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    toDeactivate.Add(kvp.Key);
                }
            }

            foreach (int index in toDeactivate)
            {
                brain.method_3(index);
                if (brain.Dictionary_0.TryGetValue(index, out var layer) && layer != null)
                {
                    layer.IsActive = false;
                }
            }
        }
    }

}
