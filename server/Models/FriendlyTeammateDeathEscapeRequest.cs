using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateDeathEscapeRequest : IRequestData
{
    [JsonPropertyName("Entries")]
    public List<FriendlyTeammateDeathEscapeEntry> Entries { get; set; } = [];
}

public record FriendlyTeammateDeathEscapeEntry
{
    public string Aid { get; set; } = string.Empty;

    public string ProfileId { get; set; } = string.Empty;

    public string Nickname { get; set; } = string.Empty;

    public bool Escaped { get; set; }

    public double Chance { get; set; }

    public string ExtractName { get; set; } = string.Empty;

    public double Distance { get; set; }

    public double HealthRatio { get; set; }

    public double EquipmentPower { get; set; }

    public double EnemyAveragePower { get; set; }

    public int AliveSquadmates { get; set; }

    public bool HasSecureMeds { get; set; }
}

public record FriendlyTeammateDeathEscapeSummary
{
    public List<string> EscapedNames { get; set; } = [];

    public List<string> LostNames { get; set; } = [];

    public string ExtractName { get; set; } = string.Empty;
}
