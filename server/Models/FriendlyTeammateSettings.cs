namespace friendlySAIN.Server.Models;

public record FriendlyTeammateSettings
{
    public string SelectedLoadoutId { get; set; } = string.Empty;
    public bool AutoJoinEnabled { get; set; }
}
