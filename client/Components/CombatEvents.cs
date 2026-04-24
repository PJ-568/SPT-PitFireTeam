using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.Components
{
    public sealed class CombatEvents
    {
        private const int DefaultBossSpreadSamples = 12;
        private readonly List<Action<PushEvent?>> pushSubscribers = new List<Action<PushEvent?>>();
        private readonly Dictionary<string, DestinationClaim> destinationClaims = new Dictionary<string, DestinationClaim>(StringComparer.Ordinal);
        private PushEvent? activePush;

        public PushEvent? CurrentPush => activePush;

        public IDisposable SubscribePush(Action<PushEvent?> handler)
        {
            if (handler == null)
            {
                return EmptySubscription.Instance;
            }

            pushSubscribers.Add(handler);
            handler(activePush);
            return new PushSubscription(pushSubscribers, handler);
        }

        public bool TryEmitPush(
            BotOwner owner,
            string enemyProfileId,
            Vector3 enemyPosition,
            Vector3 destination,
            string reason,
            bool isSearchPush)
        {
            if (!IsValidOwner(owner) || string.IsNullOrEmpty(enemyProfileId))
            {
                return false;
            }

            if (activePush.HasValue &&
                !string.Equals(activePush.Value.OwnerProfileId, owner.ProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            activePush = new PushEvent(
                owner.ProfileId,
                owner,
                enemyProfileId,
                enemyPosition,
                destination,
                reason ?? string.Empty,
                isSearchPush,
                Time.time);
            NotifyPushSubscribers();
            return true;
        }

        public bool TryReleasePush(BotOwner owner, string reason)
        {
            if (!activePush.HasValue || !IsOwner(owner, activePush.Value))
            {
                return false;
            }

            activePush = null;
            NotifyPushSubscribers();
            return true;
        }

        public bool TryGetActivePushFor(BotOwner listener, out PushEvent pushEvent)
        {
            pushEvent = default;
            if (!activePush.HasValue)
            {
                return false;
            }

            PushEvent currentPush = activePush.Value;
            if (!IsPushStillValid(currentPush))
            {
                activePush = null;
                NotifyPushSubscribers();
                return false;
            }

            if (IsOwner(listener, currentPush))
            {
                return false;
            }

            pushEvent = currentPush;
            return true;
        }

        public void Clear()
        {
            if (activePush == null)
            {
                ClearDestinationClaims();
                return;
            }

            activePush = null;
            ClearDestinationClaims();
            NotifyPushSubscribers();
        }

        public bool HasDestinationClaimConflict(
            BotOwner owner,
            Vector3 candidate,
            float spacing,
            bool includeFollowerPositions = true)
        {
            if (!TryGetBoss(owner, out pitAIBossPlayer? boss))
            {
                return false;
            }

            PruneStaleDestinationClaims();

            float spacingSqr = spacing * spacing;
            if (includeFollowerPositions)
            {
                List<BotOwner>? followers = boss.Followers;
                if (followers != null)
                {
                    for (int i = 0; i < followers.Count; i++)
                    {
                        BotOwner follower = followers[i];
                        if (follower == null ||
                            follower == owner ||
                            follower.IsDead ||
                            follower.BotState != EBotState.Active)
                        {
                            continue;
                        }

                        if ((follower.Position - candidate).sqrMagnitude < spacingSqr)
                        {
                            return true;
                        }
                    }
                }
            }

            string? selfId = owner?.ProfileId;
            foreach (KeyValuePair<string, DestinationClaim> kv in destinationClaims)
            {
                if (string.Equals(kv.Key, selfId, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((kv.Value.Position - candidate).sqrMagnitude < spacingSqr)
                {
                    return true;
                }
            }

            return false;
        }

        public bool UpsertDestinationClaim(BotOwner owner, Vector3 destination, float ttlSeconds)
        {
            if (!IsValidOwner(owner))
            {
                return false;
            }

            PruneStaleDestinationClaims();
            destinationClaims[owner.ProfileId] = new DestinationClaim(
                destination,
                Time.time + Mathf.Max(0.1f, ttlSeconds));
            return true;
        }

        public void ReleaseDestinationClaim(BotOwner owner)
        {
            if (!IsValidOwner(owner))
            {
                return;
            }

            destinationClaims.Remove(owner.ProfileId);
        }

        public bool TryFindBossSpreadDestination(
            BotOwner owner,
            Vector3 bossPosition,
            float minRadius,
            float maxRadius,
            float sameLevelTolerance,
            float spacing,
            out Vector3 target,
            int sampleCount = DefaultBossSpreadSamples)
        {
            target = Vector3.zero;
            if (!IsValidOwner(owner) ||
                !IsFinite(bossPosition) ||
                maxRadius <= 0f ||
                sampleCount <= 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(Mathf.Max(0f, minRadius), Mathf.Max(minRadius, maxRadius));
                Vector3 candidate = bossPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                {
                    continue;
                }

                if (Mathf.Abs(navHit.position.y - bossPosition.y) > sameLevelTolerance)
                {
                    continue;
                }

                if (HasDestinationClaimConflict(owner, navHit.position, spacing))
                {
                    continue;
                }

                if (!NavMesh.CalculatePath(owner.Position, navHit.position, NavMesh.AllAreas, pathBuffer) ||
                    pathBuffer.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathDistance = CalculatePathLength(pathBuffer);
                if (pathDistance >= bestDistance)
                {
                    continue;
                }

                bestDistance = pathDistance;
                target = navHit.position;
            }

            return target != Vector3.zero;
        }

        private void NotifyPushSubscribers()
        {
            for (int i = pushSubscribers.Count - 1; i >= 0; i--)
            {
                Action<PushEvent?> subscriber = pushSubscribers[i];
                try
                {
                    subscriber(activePush);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError(ex);
                    pushSubscribers.RemoveAt(i);
                }
            }
        }

        private void ClearDestinationClaims()
        {
            if (destinationClaims.Count == 0)
            {
                return;
            }

            destinationClaims.Clear();
        }

        private void PruneStaleDestinationClaims()
        {
            if (destinationClaims.Count == 0)
            {
                return;
            }

            float now = Time.time;
            List<string>? staleKeys = null;
            foreach (KeyValuePair<string, DestinationClaim> kv in destinationClaims)
            {
                if (kv.Value.ExpiresAt >= now)
                {
                    continue;
                }

                staleKeys ??= new List<string>(4);
                staleKeys.Add(kv.Key);
            }

            if (staleKeys == null)
            {
                return;
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                destinationClaims.Remove(staleKeys[i]);
            }
        }

        private static bool TryGetBoss(BotOwner owner, out pitAIBossPlayer? boss)
        {
            boss = owner?.BotFollower?.BossToFollow as pitAIBossPlayer;
            return boss != null;
        }

        private static bool IsPushStillValid(PushEvent pushEvent)
        {
            return IsValidOwner(pushEvent.Owner) &&
                   pushEvent.Owner.Memory?.GoalEnemy != null &&
                   pushEvent.Owner.GetPlayer?.HealthController?.IsAlive == true;
        }

        private static bool IsValidOwner(BotOwner owner)
        {
            return owner != null &&
                   !owner.IsDead &&
                   !string.IsNullOrEmpty(owner.ProfileId);
        }

        private static bool IsOwner(BotOwner owner, PushEvent pushEvent)
        {
            return owner != null &&
                   string.Equals(owner.ProfileId, pushEvent.OwnerProfileId, StringComparison.Ordinal);
        }

        private static float CalculatePathLength(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            if (corners == null || corners.Length == 0)
            {
                return float.MaxValue;
            }

            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }

        public readonly struct PushEvent
        {
            public PushEvent(
                string ownerProfileId,
                BotOwner owner,
                string enemyProfileId,
                Vector3 enemyPosition,
                Vector3 destination,
                string reason,
                bool isSearchPush,
                float emittedAt)
            {
                OwnerProfileId = ownerProfileId;
                Owner = owner;
                EnemyProfileId = enemyProfileId;
                EnemyPosition = enemyPosition;
                Destination = destination;
                Reason = reason;
                IsSearchPush = isSearchPush;
                EmittedAt = emittedAt;
            }

            public string OwnerProfileId { get; }
            public BotOwner Owner { get; }
            public string EnemyProfileId { get; }
            public Vector3 EnemyPosition { get; }
            public Vector3 Destination { get; }
            public string Reason { get; }
            public bool IsSearchPush { get; }
            public float EmittedAt { get; }
        }

        private readonly struct DestinationClaim
        {
            public DestinationClaim(Vector3 position, float expiresAt)
            {
                Position = position;
                ExpiresAt = expiresAt;
            }

            public Vector3 Position { get; }
            public float ExpiresAt { get; }
        }

        private readonly NavMeshPath pathBuffer = new NavMeshPath();

        private sealed class PushSubscription : IDisposable
        {
            private readonly List<Action<PushEvent?>> subscribers;
            private Action<PushEvent?>? handler;

            public PushSubscription(List<Action<PushEvent?>> subscribers, Action<PushEvent?> handler)
            {
                this.subscribers = subscribers;
                this.handler = handler;
            }

            public void Dispose()
            {
                if (handler == null)
                {
                    return;
                }

                subscribers.Remove(handler);
                handler = null;
            }
        }

        private sealed class EmptySubscription : IDisposable
        {
            public static readonly EmptySubscription Instance = new EmptySubscription();

            public void Dispose()
            {
            }
        }
    }
}
