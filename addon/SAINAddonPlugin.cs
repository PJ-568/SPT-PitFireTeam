using BepInEx;
using friendlySAIN.Modules;
using HarmonyLib;

namespace friendlySAIN.SAINAddon
{
    [BepInPlugin("xyz.pit.friendlysain.sainaddon", "friendlySAIN SAIN Addon", "1.0.0")]
    [BepInDependency("xyz.pit.friendlysain", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
    public class SAINAddonPlugin : BaseUnityPlugin
    {
        internal static SAINAddonPlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            var harmony = new Harmony("xyz.pit.friendlysain.sainaddon");
            Logger.LogInfo("[Init] friendlySAIN SAIN addon loaded.");

            // Register direct core->addon runtime bridge callbacks.
            SainAddonBridge.IsReadyForPatrolAfterCombat = SAINFollowerRuntimeBridge.IsReadyForPatrolAfterCombat;
            SainAddonBridge.ForceReleaseFollowerCombatState = SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState;
            SainAddonBridge.TrySyncFollowerEnemyState = SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState;
            SainAddonBridge.TryResetFollowerDecisionState = SAINFollowerRuntimeBridge.TryResetFollowerDecisionState;
            SainAddonBridge.GetFollowerDebugState = SAINFollowerRuntimeBridge.GetFollowerDebugState;

            // Placeholder bootstrap for future SAIN regroup layer/action registration.
            // Keep this as the dedicated integration point so core plugin can remain vanilla-safe.
            SAINRegroupBootstrap.Initialize(harmony, Logger);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(SainAddonBridge.IsReadyForPatrolAfterCombat, SAINFollowerRuntimeBridge.IsReadyForPatrolAfterCombat))
            {
                SainAddonBridge.IsReadyForPatrolAfterCombat = null;
            }

            if (ReferenceEquals(SainAddonBridge.ForceReleaseFollowerCombatState, SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState))
            {
                SainAddonBridge.ForceReleaseFollowerCombatState = null;
            }

            if (ReferenceEquals(SainAddonBridge.TrySyncFollowerEnemyState, SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState))
            {
                SainAddonBridge.TrySyncFollowerEnemyState = null;
            }

            if (ReferenceEquals(SainAddonBridge.TryResetFollowerDecisionState, SAINFollowerRuntimeBridge.TryResetFollowerDecisionState))
            {
                SainAddonBridge.TryResetFollowerDecisionState = null;
            }

            if (ReferenceEquals(SainAddonBridge.GetFollowerDebugState, SAINFollowerRuntimeBridge.GetFollowerDebugState))
            {
                SainAddonBridge.GetFollowerDebugState = null;
            }
        }
    }
}
