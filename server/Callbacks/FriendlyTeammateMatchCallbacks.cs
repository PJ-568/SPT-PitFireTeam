using friendlySAIN.Server.Models;
using friendlySAIN.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Callbacks;

[Injectable]
public class FriendlyTeammateMatchCallbacks(
    FriendlyTeammateService teammateService,
    HttpResponseUtil httpResponseUtil,
    NotificationSendHelper notificationSendHelper
)
{
    public ValueTask<string> SendGroupInvite(
        string url,
        MatchGroupInviteSendRequest request,
        MongoId sessionId,
        string? previousOutput)
    {
        if (!teammateService.TryGetRaidGroupCharacter(sessionId, request.To, out var teammate))
        {
            return new ValueTask<string>(previousOutput ?? httpResponseUtil.NullResponse());
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            var acceptedTeammate = teammate!;

            notificationSendHelper.SendMessage(
                sessionId,
                new WsGroupMatchInviteAccept
                {
                    EventType = NotificationEventType.groupMatchInviteAccept,
                    EventIdentifier = new MongoId(),
                    Id = acceptedTeammate.Id,
                    Aid = acceptedTeammate.Aid,
                    Info = acceptedTeammate.Info,
                    VisualRepresentation = acceptedTeammate.VisualRepresentation,
                    IsLeader = false,
                    IsReady = true,
                    Region = acceptedTeammate.Region,
                    LookingGroup = false,
                }
            );
        });

        return new ValueTask<string>(previousOutput ?? httpResponseUtil.GetBody("friendlysain-teammate-invite"));
    }

    public ValueTask<string> GenerateFollowerProfile(
        string url,
        FriendlyTeammateFollowerGenerateRequest request,
        MongoId sessionId)
    {
        if (!teammateService.TryGetSpawnProfile(sessionId, request.MemberId, request.Custom?.Health, out var teammate))
        {
            return new ValueTask<string>(httpResponseUtil.GetBody(Array.Empty<object>()));
        }

        return new ValueTask<string>(httpResponseUtil.GetBody(new[] { teammate }));
    }

    public ValueTask<string> GetFollowerDetails(string url, EmptyRequestData request, MongoId sessionId)
    {
        var details = teammateService.ListFollowerDetails(sessionId);
        return new ValueTask<string>(httpResponseUtil.GetBody(details));
    }
}
