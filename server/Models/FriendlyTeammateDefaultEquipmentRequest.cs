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

    // Present only for real default commits. This is the staged player stash after the editor's drag/drop
    // operations, and is committed server-side together with teammate equipment.
    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }

    [JsonPropertyName("realItemCommit")]
    public bool RealItemCommit { get; set; }
}

public record FriendlyTeammateDefaultEquipmentResponse
{
    [JsonPropertyName("realItemCommit")]
    public bool RealItemCommit { get; set; }

    [JsonPropertyName("playerStashItems")]
    public List<Item>? PlayerStashItems { get; set; }
}
