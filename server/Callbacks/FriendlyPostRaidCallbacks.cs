using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyPostRaidCallbacks(
    HttpResponseUtil httpResponse,
    FriendlyPostRaidService postRaidService,
    FriendlyRecruitService recruitService,
    FriendlyTeammateService teammateService
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

    public ValueTask<string> RecordKillMessage(string url, FriendlyPostRaidKillMessageRequest request, MongoId sessionId)
    {
        postRaidService.RecordKillMessage(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> RegisterProtectedItems(string url, FriendlyPostRaidProtectedItemsRequest request, MongoId sessionId)
    {
        postRaidService.RegisterProtectedRaidItems(sessionId, request);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> EndLocalRaid(string url, EndLocalRaidRequestData request, MongoId sessionId, string? output)
    {
        postRaidService.RemoveProtectedTeammateItemsFromExtractedProfile(sessionId, request);
        postRaidService.HandleEndLocalRaidKillMessages(sessionId, request);
        return new ValueTask<string>(output ?? httpResponse.NullResponse());
    }

    public ValueTask<string> DeathEscape(string url, FriendlyTeammateDeathEscapeRequest request, MongoId sessionId)
    {
        FriendlyTeammateDeathEscapeSummary summary = teammateService.PersistDeathEscapeOutcomes(sessionId, request.Entries);
        if (request.Notify)
        {
            postRaidService.HandleDeathEscapeSummary(sessionId, summary);
        }

        return new ValueTask<string>(httpResponse.NullResponse());
    }
}
