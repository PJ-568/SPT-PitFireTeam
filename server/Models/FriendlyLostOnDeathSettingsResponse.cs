using System.Text.Json.Serialization;

namespace pitTeam.Server.Models;

public record FriendlyLostOnDeathSettingsResponse
{
    [JsonPropertyName("equipment")]
    public Dictionary<string, bool> Equipment { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
