namespace pitTeam.Server.Models;

public record FriendlyTeammateProfileOptionsResponse
{
    public string CurrentLoadoutId { get; set; } = string.Empty;

    public string CurrentTactic { get; set; } = string.Empty;

    public float Aggression { get; set; } = 50f;

    public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; } = [];

    public List<FriendlyTeammateTacticOption> Tactics { get; set; } = [];
}

public record FriendlyTeammateLoadoutOption
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public record FriendlyTeammateTacticOption
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
