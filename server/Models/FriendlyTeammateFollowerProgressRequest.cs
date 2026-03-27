using System.Text.Json;

namespace friendlySAIN.Server.Models;

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
