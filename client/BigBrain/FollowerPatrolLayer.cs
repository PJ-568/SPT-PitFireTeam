using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Patches;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

        internal static void EnsureRegisteredForBot(BotOwner bot)
        {
            if (bot?.Brain?.BaseBrain == null) return;

            string brainName = bot.Brain.BaseBrain.ShortName();
            if (string.IsNullOrWhiteSpace(brainName)) return;
            if (!RegisteredBrains.Add(brainName)) return;

            try
            {
                BrainManager.AddCustomLayer(typeof(FollowerPatrolLayer), new List<string> { brainName }, 12);
                Modules.Logger.LogInfo($"Registered follower patrol layer for brain '{brainName}'");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to register follower patrol layer for brain '{brainName}'");
                Modules.Logger.LogError(ex);
            }
        }

        private static void RegisterForBot(BotOwner bot)
        {
            EnsureRegisteredForBot(bot);
        }
    }

    internal sealed class FollowerPatrolLayer : CustomLayer
    {
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
            if (!(BotOwner.BotFollower.BossToFollow is pitAIBossPlayer)) return false;
            // Never own logic during combat. Let SAIN combat layers take over immediately.
            if (BotOwner.Memory.HaveEnemy) return false;
            return true;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(FollowerPatrolAction), "FollowerPatrol");
        }

        public override bool IsCurrentActionEnding()
        {
            return !IsActive();
        }
    }

    internal sealed class FollowerPatrolAction : CustomLogic
    {
        private float nextUpdateAt;
        private Vector3? settlePoint;
        private CustomNavigationPoint? holdCoverPoint;
        private BotFollowerPlayer followerData;
        private Vector3 holdAnchorBossPos;
        private bool hasAnchor;
        private float patrolReanchorAt;
        private bool isFollowingBoss;
        private Vector3 lastChasePoint;
        private float nextChaseRefreshAt;

        private const float FollowDistance = 12f;
        private const float FollowStartDistance = 14f;
        private const float FollowStopDistance = 9f;
        private const float SprintStartDistance = 20f;
        private const float ReanchorBossMoveDistance = 7f;
        private const float PatrolReanchorMin = 6f;
        private const float PatrolReanchorMax = 10f;

        public FollowerPatrolAction(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            bool canPatrolAroundBoss = followerData?.CanPatrol == true;

            if (Time.time < nextUpdateAt) return;
            nextUpdateAt = Time.time + 0.2f;

            Vector3 bossPosition = GetBossPosition();
            float distanceSqr = (bossPosition - BotOwner.Position).sqrMagnitude;

            // Hysteresis to avoid run/stop thrash near follow threshold.
            if (!isFollowingBoss && distanceSqr > FollowStartDistance * FollowStartDistance)
            {
                isFollowingBoss = true;
            }
            else if (isFollowingBoss && distanceSqr <= FollowStopDistance * FollowStopDistance)
            {
                isFollowingBoss = false;
            }

            if (isFollowingBoss)
            {
                ClearHoldAnchor();

                bool sprint = distanceSqr > SprintStartDistance * SprintStartDistance;
                float speed = sprint ? 1f : 0.75f;
                if (Time.time >= nextChaseRefreshAt || (bossPosition - lastChasePoint).sqrMagnitude > 3f * 3f)
                {
                    lastChasePoint = bossPosition;
                    nextChaseRefreshAt = Time.time + 0.55f;
                }
                MoveToPoint(lastChasePoint, sprint, speed);
                return;
            }

            // Close enough: find a hold anchor around boss (prefer cover) and stay until boss moves away.
            if (NeedNewHoldAnchor(bossPosition, canPatrolAroundBoss))
            {
                holdCoverPoint = TryPickCoverPointNearBoss(bossPosition);
                if (holdCoverPoint != null)
                {
                    // Reserve selected cover point for this bot.
                    BotOwner.Memory.SetCoverPoints(holdCoverPoint);
                    settlePoint = null;
                }
                else
                {
                    settlePoint = TryPickSettlePoint(bossPosition);
                }
                holdAnchorBossPos = bossPosition;
                hasAnchor = true;
                if (canPatrolAroundBoss)
                {
                    patrolReanchorAt = Time.time + UnityEngine.Random.Range(PatrolReanchorMin, PatrolReanchorMax);
                }
            }

            if (holdCoverPoint != null)
            {
                Vector3 coverPos = holdCoverPoint.Position;
                float coverDistSqr = (coverPos - BotOwner.Position).sqrMagnitude;
                if (coverDistSqr > 0.9f * 0.9f)
                {
                    MoveToPoint(coverPos, false, 0.55f);
                    return;
                }

                HoldClose();
                return;
            }

            if (settlePoint.HasValue)
            {
                float settleDistSqr = (settlePoint.Value - BotOwner.Position).sqrMagnitude;
                if (settleDistSqr > 0.8f * 0.8f)
                {
                    MoveToPoint(settlePoint.Value, false, 0.55f);
                    return;
                }
            }

            HoldClose();
        }

        private void MoveToPoint(Vector3 point, bool sprint, float moveSpeed = 1f)
        {
            BotOwner.Mover.Sprint(sprint, false);
            BotOwner.Mover.SetTargetMoveSpeed(moveSpeed);
            BotOwner.GoToPoint(point, true, -1f, false, false);
            BotOwner.Steering.LookToMovingDirection();
        }

        private void HoldClose()
        {
            BotOwner.Mover.Sprint(false, false);
            BotOwner.Mover.SetTargetMoveSpeed(0.5f);
            BotOwner.StopMove();
        }

        private bool NeedNewHoldAnchor(Vector3 bossPosition, bool canPatrolAroundBoss)
        {
            if (!hasAnchor) return true;

            if (canPatrolAroundBoss && Time.time >= patrolReanchorAt)
            {
                return true;
            }

            if ((bossPosition - holdAnchorBossPos).sqrMagnitude > ReanchorBossMoveDistance * ReanchorBossMoveDistance)
            {
                return true;
            }

            if (holdCoverPoint != null)
            {
                if (!holdCoverPoint.IsFreeById(BotOwner.Id) || holdCoverPoint.IsSpotted)
                {
                    return true;
                }
            }
            else if (!settlePoint.HasValue)
            {
                return true;
            }

            return false;
        }

        private void ClearHoldAnchor()
        {
            holdCoverPoint = null;
            settlePoint = null;
            hasAnchor = false;
        }

        private Vector3 GetBossPosition()
        {
            if (BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss && boss.realPlayer != null)
            {
                return boss.realPlayer.Transform.position;
            }
            return BotOwner.BotFollower.BossToFollow.Position;
        }

        private CustomNavigationPoint? TryPickCoverPointNearBoss(Vector3 bossPosition)
        {
            // Use EFT's own free-point selector so occupancy rules are respected.
            CustomNavigationPoint point = BotOwner.Covers.GetFreeClosePoint(bossPosition, 0f, false);
            if (point == null) return null;

            // Keep a small ring around boss; too close feels jittery/crowded.
            float distSqr = (point.Position - bossPosition).sqrMagnitude;
            if (distSqr < 2f * 2f || distSqr > (FollowDistance + 4f) * (FollowDistance + 4f))
            {
                return null;
            }

            return point;
        }

        private Vector3? TryPickSettlePoint(Vector3 bossPosition)
        {
            // Keep followers close but not overlapping boss: prefer ring of ~2-4m.
            for (int i = 0; i < 16; i++)
            {
                Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                if (dir.sqrMagnitude < 0.01f) continue;

                float radius = UnityEngine.Random.Range(2f, 4f);
                Vector3 sample = new Vector3(bossPosition.x + dir.x * radius, bossPosition.y, bossPosition.z + dir.y * radius);

                if (NavMesh.SamplePosition(sample, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }

            return null;
        }
    }
}
