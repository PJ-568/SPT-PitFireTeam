using EFT;
using System;
using System.Collections.Generic;
using System.Threading;

namespace pitTeam.Modules
{
    public static class BotOwnerUpdateHub
    {
        private static readonly Dictionary<string, Action<BotOwner>> Subscribers = new Dictionary<string, Action<BotOwner>>();
        private static readonly object SyncRoot = new object();
        private static Action<BotOwner>[] _callbackSnapshot = Array.Empty<Action<BotOwner>>();
        private static int _subscriberCount;

        internal static bool HasSubscribers => Volatile.Read(ref _subscriberCount) > 0;

        public static void Register(string id, Action<BotOwner> callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null) return;

            lock (SyncRoot)
            {
                Subscribers[id] = callback;
                RefreshSnapshot();
            }
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (SyncRoot)
            {
                Subscribers.Remove(id);
                RefreshSnapshot();
            }
        }

        internal static void Invoke(BotOwner owner)
        {
            if (!HasSubscribers) return;

            Action<BotOwner>[] callbacks = Volatile.Read(ref _callbackSnapshot);
            if (callbacks.Length == 0) return;

            foreach (Action<BotOwner> callback in callbacks)
            {
                try
                {
                    callback(owner);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Exception in BotOwnerUpdateHub callback");
                    Logger.LogError(ex);
                }
            }
        }

        private static void RefreshSnapshot()
        {
            Action<BotOwner>[] snapshot = CopyCallbacks();
            Volatile.Write(ref _callbackSnapshot, snapshot);
            Volatile.Write(ref _subscriberCount, snapshot.Length);
        }

        private static Action<BotOwner>[] CopyCallbacks()
        {
            if (Subscribers.Count == 0)
            {
                return Array.Empty<Action<BotOwner>>();
            }

            Action<BotOwner>[] callbacks = new Action<BotOwner>[Subscribers.Count];
            Subscribers.Values.CopyTo(callbacks, 0);
            return callbacks;
        }
    }
}
