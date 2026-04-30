namespace pitTeam.SAINAddon
{
    internal static class SAINAddonToggles
    {
        // Forced calc-goal enemy retention was exploratory and can reacquire invalid targets.
        // Keep it off unless actively testing that path.
        public static readonly bool EnableForcedEnemyRetention = false;
    }
}
