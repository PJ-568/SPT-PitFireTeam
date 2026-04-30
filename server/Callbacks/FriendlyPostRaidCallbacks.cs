using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyPostRaidCallbacks(
    HttpResponseUtil httpResponse,
    FriendlyPostRaidService postRaidService,
    FriendlyRecruitService recruitService
)
{
    public ValueTask<string> ReturnItems(string url, FriendlyPostRaidReturnItemsRequest request, MongoId sessionId)
    {
        postRaidService.HandleReturnItems(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> TeamEscaped(string url, FriendlyPostRaidTeamEscapedRequest request, MongoId sessionId)
    {
        postRaidService.HandleTeamEscaped(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> RecruitPickup(string url, FriendlyRecruitPickupRequest request, MongoId sessionId)
    {
        recruitService.QueueRecruitPickups(sessionId, request.Candidates);
        return new ValueTask<string>(httpResponse.NullResponse());
    }
}
