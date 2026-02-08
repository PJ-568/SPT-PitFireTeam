using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Patches;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal static class FollowerLayerRegistry
    {
        private static readonly HashSet<string> RegisteredBrains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;

            BotOwnerActivatePatch.AddOnActivate(RegisterForBot);
        }

        private static void RegisterForBot(BotOwner bot)
        {
            if (bot?.Brain?.BaseBrain == null) return;

            string brainName = bot.Brain.BaseBrain.ShortName();
            if (string.IsNullOrWhiteSpace(brainName)) return;
            if (!RegisteredBrains.Add(brainName)) return;

            try
            {
                BrainManager.AddCustomLayer(typeof(FollowerRegroupLayer), new List<string> { brainName }, 86);
                Modules.Logger.LogInfo($"Registered follower regroup layer for brain '{brainName}'");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to register follower regroup layer for brain '{brainName}'");
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal sealed class FollowerRegroupLayer : CustomLayer
    {
        private const float CombatDistanceSqr = 25f * 25f;
        private const float PeaceDistanceSqr = 45f * 45f;

        public FollowerRegroupLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "friendlySAIN.FollowerRegroup";
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }
            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (!(BotOwner.BotFollower.BossToFollow is pitAIBossPlayer)) return false;

            float distanceSqr = (BotOwner.BotFollower.BossToFollow.Position - BotOwner.Position).sqrMagnitude;
            float allowedDistanceSqr = BotOwner.Memory.HaveEnemy ? CombatDistanceSqr : PeaceDistanceSqr;
            return distanceSqr > allowedDistanceSqr;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(FollowerRegroupAction), "RegroupBoss");
        }

        public override bool IsCurrentActionEnding()
        {
            return !IsActive();
        }
    }

    internal sealed class FollowerRegroupAction : CustomLogic
    {
        private float nextUpdateAt;

        public FollowerRegroupAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;

            if (Time.time < nextUpdateAt) return;
            nextUpdateAt = Time.time + 0.2f;

            Vector3 bossPosition = BotOwner.BotFollower.BossToFollow.Position;
            float distanceSqr = (bossPosition - BotOwner.Position).sqrMagnitude;
            bool sprint = distanceSqr > 35f * 35f;

            BotOwner.Mover.Sprint(sprint, false);
            BotOwner.Mover.SetTargetMoveSpeed(1f);

            // Reuse vanilla follower target handling so SAIN remains the combat authority.
            BotOwner.BotFollower.PatrolDataFollower.SetCloseToTarget(true);
            BotOwner.BotFollower.PatrolDataFollower.SetTarget(new GStruct25(bossPosition), null);
        }
    }
}
