namespace friendlySAIN.Server.Models;

public record FriendlyTeammateFollowerDetailsResponse
{
    public string Aid { get; set; } = string.Empty;

    public string Tactic { get; set; } = string.Empty;

    public string Equipment { get; set; } = string.Empty;

    public string Voice { get; set; } = string.Empty;

    public string Head { get; set; } = string.Empty;
}
