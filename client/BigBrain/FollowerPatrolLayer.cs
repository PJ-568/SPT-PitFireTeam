using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
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
                "CursAssault"
            };

            List<string> vanillLayers = new List<string>
            {
                "FightReqNull",
                "PeacecReqNull"
            };

            try
            {
                
                BrainManager.AddCustomLayer(typeof(FollowerPatrolLayer), brains, FollowerLayerPriority);
                Modules.Logger.LogInfo($"Registered follower patrol layer for brains: {string.Join(", ", brains)}");
                BrainManager.RemoveLayers(vanillLayers,brains);
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
            BotOwner.PatrollingData.Pause();
        }

        public override Action GetNextAction()
        {
            if (BotOwner.Medecine.FirstAid.Using || BotOwner.Medecine.SurgicalKit.Using)
            {
                float_4 = Time.time + 10f;
            }

            if (BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork)
            {
                healing = true;
                float_4 = Time.time + 10f;
                return new Action(typeof(HealAction), "Heal");   
            }

            return new Action(typeof(FollowAction), "FollowerPatrol");
        }

        public override bool IsCurrentActionEnding()
        {
            if(healing && Time.time > float_4)
            {
                healing = false;
                HealBot();
                return true;
            }
            return !IsActive();
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
    }

}
