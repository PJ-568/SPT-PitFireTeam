using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyPostRaidTeamEscapedRequest : IRequestData
{
    [JsonPropertyName("member")]
    public FriendlyPostRaidMember? Member { get; set; }
}

public record FriendlyPostRaidMember
{
    [JsonPropertyName("_id")]
    public string? Id { get; set; }

    [JsonPropertyName("aid")]
    public string? Aid { get; set; }

    [JsonPropertyName("Info")]
    public UserDialogDetails? Info { get; set; }

    [JsonPropertyName("SquadInfo")]
    public FriendlyPostRaidSquadInfo? SquadInfo { get; set; }
}

public record FriendlyPostRaidSquadInfo
{
    [JsonPropertyName("Mate")]
    public bool Mate { get; set; }

    [JsonPropertyName("AllyBoss")]
    public string? AllyBoss { get; set; }

    [JsonPropertyName("Partial")]
    public bool Partial { get; set; }

    [JsonPropertyName("Lost")]
    public List<string> Lost { get; set; } = [];
}
