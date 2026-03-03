using EFT;
using System;
using System.Collections.Generic;
using System.Linq;

namespace friendlySAIN.Modules
{
    public static class BotOwnerUpdateHub
    {
        private static readonly Dictionary<string, Action<BotOwner>> Subscribers = new Dictionary<string, Action<BotOwner>>();
        private static readonly object SyncRoot = new object();

        public static void Register(string id, Action<BotOwner> callback)
        {
            if (string.IsNullOrEmpty(id) || callback == null) return;

            lock (SyncRoot)
            {
                Subscribers[id] = callback;
            }
        }

        public static void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (SyncRoot)
            {
                Subscribers.Remove(id);
            }
        }

        internal static void Invoke(BotOwner owner)
        {
            Action<BotOwner>[] callbacks;
            lock (SyncRoot)
            {
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
