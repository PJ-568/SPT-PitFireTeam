using friendlySAIN.Server.Models;
using friendlySAIN.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Callbacks;

[Injectable]
public class FriendlyTeammateSocialCallbacks(
    FriendlyTeammateService teammateService,
    HttpResponseUtil httpResponseUtil,
    JsonUtil jsonUtil
)
{
    public ValueTask<string> MergeFriendList(string url, EmptyRequestData _, MongoId sessionId, string? previousOutput)
    {
        var body = DeserializeBody<GetFriendListDataResponse>(previousOutput);
        var response = body?.Data ?? new GetFriendListDataResponse
        {
            Friends = [],
            Ignore = [],
            InIgnoreList = [],
        };

        response.Friends ??= [];
        foreach (var teammate in teammateService.ListTeammateDialogs(sessionId))
        {
            if (response.Friends.Any(existing => existing.Id == teammate.Id))
            {
                continue;
            }

            response.Friends.Add(teammate);
        }

        return new ValueTask<string>(httpResponseUtil.GetBody(response, body?.Err ?? 0, body?.ErrMsg));
    }

    public ValueTask<string> MergeProfileView(string url, GetOtherProfileRequest request, MongoId sessionId, string? previousOutput)
    {
        if (!teammateService.TryGetTeammateProfile(sessionId, request.AccountId, out var teammateProfile))
        {
            return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
        }

        return new ValueTask<string>(httpResponseUtil.GetBody(teammateProfile));
    }

    public ValueTask<string> DeleteFriend(string url, DeleteFriendRequest request, MongoId sessionId, string? previousOutput)
    {
        teammateService.DeleteTeammateByProfileId(sessionId, request.FriendId);
        return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
    }

    private FriendlyTeammateBodyResponse<T>? DeserializeBody<T>(string? previousOutput)
    {
        if (string.IsNullOrWhiteSpace(previousOutput))
        {
            return null;
        }

        return jsonUtil.Deserialize<FriendlyTeammateBodyResponse<T>>(previousOutput);
    }
}
