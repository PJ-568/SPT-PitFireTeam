using System;
using EFT;
using friendlySAIN.Components;

namespace friendlySAIN.Modules
{
    public static class SainAddonBridge
    {
        // Addon registers SAIN-specific readiness callback here during startup.
        public static Func<BotOwner, bool>? IsReadyForPatrolAfterCombat { get; set; }

        // Addon can expose a hard recovery hook to clear follower SAIN combat-layer latch state.
        public static Action<BotOwner>? ForceReleaseFollowerCombatState { get; set; }

        // Addon owns SAIN enemy-controller sync so core contact paths stay reflection-free.
        public static Func<BotOwner, Player, bool>? TrySyncFollowerEnemyState { get; set; }

        // Addon owns SAIN decision resets so core command paths stay reflection-free.
        public static Func<BotOwner, bool>? TryResetFollowerDecisionState { get; set; }

        // Addon-owned SAIN runtime diagnostics for follower debug logging from core command paths.
        public static Func<BotOwner, string>? GetFollowerDebugState { get; set; }

        // Generic event that addon can hook into for follower lifecycle changes.
        public static event Action<BotOwner, FollowerLifecycleEvent>? OnFollowerLifecycleEvent;

        // Boss-group static update event so addon-owned SAIN sync can run on shared group context.
        public static event Action<pitAIBossPlayer>? OnBossGroupStaticUpdate;

        /// <summary>
        /// Raise the follower lifecycle event (called from core plugin paths).
        /// </summary>
        public static void RaiseFollowerLifecycleEvent(BotOwner bot, FollowerLifecycleEvent eventType)
        {
            OnFollowerLifecycleEvent?.Invoke(bot, eventType);
        }

        /// <summary>
        /// Raise the boss-group static update event (called from core boss-group paths).
        /// </summary>
        public static void RaiseBossGroupStaticUpdate(pitAIBossPlayer boss)
        {
            OnBossGroupStaticUpdate?.Invoke(boss);
        }
    }

    /// <summary>
    /// Follower lifecycle events that addons can subscribe to for custom cleanup/setup.
    /// </summary>
    public enum FollowerLifecycleEvent
    {
        /// <summary>Fired when a bot is recruited as a follower (after Init).</summary>
        OnRecruited,

        /// <summary>Fired when a follower is dismissed/converted back to regular bot.</summary>
        OnDismiss,

        /// <summary>Fired when raid cleanup occurs (all followers cleared).</summary>
        OnRaidEnd,
    }
}
