using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace friendlySAIN.Server.Models;

public record FriendlyPostRaidReturnItemsRequest : IRequestData
{
    [JsonPropertyName("items")]
    public List<Item>? Items { get; set; }
}
