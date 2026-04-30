using System.Text.Json;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateFollowerProgressBatchRequest : IRequestData
{
    public List<FriendlyTeammateFollowerProgressRequest> Entries { get; set; } = [];
}

public record FriendlyTeammateFollowerProgressRequest
{
    public string Aid { get; set; } = string.Empty;

    public double BotExperienceSession { get; set; }

    public List<FriendlyTeammateSkillProgressRequest> Skills { get; set; } = [];
}

public record FriendlyTeammateSkillProgressRequest
{
    public JsonElement Id { get; set; }

    public double Current { get; set; }

    public double Progress { get; set; }

    public double PointsEarnedDuringSession { get; set; }
}
