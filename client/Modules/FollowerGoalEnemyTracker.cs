using EFT;
using System;
using System.Diagnostics;

namespace pitTeam.Modules
{
    internal static class FollowerGoalEnemyTracker
    {
        [ThreadStatic]
        private static GoalEnemySetContext? currentContext;

        public static GoalEnemySetScope Begin(string source, string reason)
        {
            GoalEnemySetContext? previous = currentContext;
            currentContext = new GoalEnemySetContext(source, reason);
            return new GoalEnemySetScope(previous);
        }

        public static string CurrentSource => currentContext?.Source ?? InferSource();

        public static string CurrentReason => currentContext?.Reason ?? "unscopedSetter";

        public static void RecordSetter(
            BotOwner? botOwner,
            EnemyInfo? previous,
            EnemyInfo? next,
            bool allowed,
            string? blockedReason = null)
        {
            if (botOwner == null || pitFireTeam.battleRecorderEnabled?.Value != true)
            {
                return;
            }

            if (allowed &&
                string.Equals(previous?.ProfileId, next?.ProfileId, StringComparison.Ordinal))
            {
                return;
            }

            GoalEnemySetContext? context = currentContext;
            string source = context?.Source ?? InferSource();
            string reason = blockedReason ?? context?.Reason ?? "unscopedSetter";

            BattleRecorder.RecordGoalEnemyTransition(
                botOwner,
                previous,
                next,
                source,
                reason,
                allowed);
        }

        private static string InferSource()
        {
            try
            {
                StackTrace trace = new StackTrace(false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    Type? declaringType = method?.DeclaringType;
                    if (method == null || declaringType == null)
                    {
                        continue;
                    }

                    string fullName = $"{declaringType.FullName}.{method.Name}";
                    if (ShouldSkipFrame(fullName))
                    {
                        continue;
                    }

                    if (fullName.StartsWith("pitTeam.", StringComparison.Ordinal))
                    {
                        return fullName;
                    }

                    if (fullName.StartsWith("SAIN.", StringComparison.Ordinal) ||
                        fullName.IndexOf(".SAIN", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return $"SAIN:{fullName}";
                    }

                    return $"vanilla:{fullName}";
                }
            }
            catch
            {
            }

            return "unknownGoalEnemySetter";
        }

        private static bool ShouldSkipFrame(string fullName)
        {
            return fullName.StartsWith(typeof(FollowerGoalEnemyTracker).FullName ?? string.Empty, StringComparison.Ordinal) ||
                   fullName.IndexOf("FollowerGoalEnemyClearRetentionPatch", StringComparison.Ordinal) >= 0 ||
                   fullName.IndexOf("BotMemoryClass.set_GoalEnemy", StringComparison.Ordinal) >= 0 ||
                   fullName.IndexOf("BotMemoryClass::set_GoalEnemy", StringComparison.Ordinal) >= 0 ||
                   fullName.IndexOf("DMD<BotMemoryClass", StringComparison.Ordinal) >= 0 ||
                   fullName.StartsWith("HarmonyLib.", StringComparison.Ordinal);
        }

        internal sealed class GoalEnemySetContext
        {
            public GoalEnemySetContext(string source, string reason)
            {
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            }

            public string Source { get; }
            public string Reason { get; }
        }

        public readonly struct GoalEnemySetScope : IDisposable
        {
            private readonly GoalEnemySetContext? previousContext;

            internal GoalEnemySetScope(GoalEnemySetContext? previousContext)
            {
                this.previousContext = previousContext;
            }

            public void Dispose()
            {
                currentContext = previousContext;
            }
        }
    }
}
