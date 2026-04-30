using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateDefaultEquipmentRequest : IRequestData
{
    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("items")]
    public List<Item>? Items { get; set; }
}
