using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Models;

public record FriendlyTeammateBodyResponse<T>
{
    [JsonPropertyName("err")]
    public BackendErrorCodes? Err { get; set; }

    [JsonPropertyName("errmsg")]
    public string? ErrMsg { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}
