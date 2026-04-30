using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace friendlySAIN.Server.Models;

public record FriendlyLanguageRequest : IRequestData
{
    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("englishJson")]
    public string? EnglishJson { get; set; }
}
