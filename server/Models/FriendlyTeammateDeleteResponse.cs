using System.Text.Json.Serialization;

namespace friendlySAIN.Server.Models;

public record FriendlyTeammateDeleteResponse
{
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
}
