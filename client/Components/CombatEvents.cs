using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace friendlySAIN.Components
{
    public sealed class CombatEvents
    {
        private readonly List<Action<PushEvent?>> pushSubscribers = new List<Action<PushEvent?>>();
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
                return;
            }

            activePush = null;
            NotifyPushSubscribers();
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
