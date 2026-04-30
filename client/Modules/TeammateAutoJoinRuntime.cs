using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class TeammateAutoJoinRuntime
    {
        private static readonly HashSet<string> SuppressedForCurrentCycle = new HashSet<string>(StringComparer.Ordinal);

        public static IReadOnlyList<string> FilterInviteCandidates(IEnumerable<string> accountIds)
        {
            if (accountIds == null)
            {
                return Array.Empty<string>();
            }

            return accountIds
                .Where(accountId => !string.IsNullOrWhiteSpace(accountId) && !SuppressedForCurrentCycle.Contains(accountId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static void MarkSuppressed(string accountId)
        {
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                SuppressedForCurrentCycle.Add(accountId);
            }
        }

        public static void ClearSuppression(string accountId)
        {
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                SuppressedForCurrentCycle.Remove(accountId);
            }
        }

        public static void ClearAllSuppression()
        {
            SuppressedForCurrentCycle.Clear();
        }
    }
}
