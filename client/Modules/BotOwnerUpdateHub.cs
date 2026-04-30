using EFT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace pitTeam.Modules
{
    public static class BotOwnerUpdateHub
    {
        private static readonly Dictionary<string, Action<BotOwner>> Subscribers = new Dictionary<string, Action<BotOwner>>();
        private static readonly object SyncRoot = new object();
        private static int _subscriberCount;

        internal static bool HasSubscribers => Volatile.Read(ref _subscriberCount) > 0;

        public static void Register(string id, Action<BotOwner> callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null) return;

            lock (SyncRoot)
            {
                Subscribers[id] = callback;
                _subscriberCount = Subscribers.Count;
            }
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (SyncRoot)
            {
                Subscribers.Remove(id);
                _subscriberCount = Subscribers.Count;
            }
        }

        internal static void Invoke(BotOwner owner)
        {
            if (!HasSubscribers) return;

            Action<BotOwner>[] callbacks;
            lock (SyncRoot)
            {
                if (Subscribers.Count == 0)
                {
                    _subscriberCount = 0;
                    return;
                }

                callbacks = Subscribers.Values.ToArray();
            }

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
    }
}
