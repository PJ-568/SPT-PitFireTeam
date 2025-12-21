using friendlySAIN.Components;
using System.Collections.Generic;

namespace friendlySAIN.Modules
{
    internal class Receivers
    {
        private static Dictionary<string, FollowerReceiver> followerReceivers;

        private static Receivers Instance;

        private bool IsDisposed = false;
        public Receivers()
        {
            if (Instance == null)
            {
                if (followerReceivers == null)
                {
                    followerReceivers = new Dictionary<string, FollowerReceiver>();
                }
                Instance = this;
            }
        }

        public void Destroy()
        {
            if (IsDisposed) return;
            List<FollowerReceiver> receiversToDispose = new List<FollowerReceiver>(followerReceivers.Values);

            foreach (var receiver in receiversToDispose)
            {
                receiver.Dispose();
            }

            followerReceivers.Clear();

            IsDisposed = true;
        }

        public static void Dispose()
        {
            if (Instance != null)
            {
                Instance.Destroy();
                Instance = null;
            }
        }

        public static void AddReceiver(string id, FollowerReceiver receiver)
        {
            if (followerReceivers.ContainsKey(id))
            {
                if (followerReceivers.TryGetValue(id, out receiver))
                {
                    receiver.Destroy();
                }

                followerReceivers.Remove(id);
            }
            followerReceivers.Add(id, receiver);
        }

        public static void RemoveReceiver(FollowerReceiver receiver)
        {
            foreach (var item in followerReceivers)
            {
                if (item.Value == receiver)
                {
                    followerReceivers.Remove(item.Key);
                    break;
                }
            }
        }

        public static FollowerReceiver GetReceiver(string id)
        {
            if (followerReceivers.TryGetValue(id, out FollowerReceiver receiver)) return receiver;
            return null;
        }

        public static Dictionary<string, FollowerReceiver> GetReceivers()
        {
            return followerReceivers;
        }
    }
}
