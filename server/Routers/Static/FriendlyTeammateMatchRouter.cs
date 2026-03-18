using friendlySAIN.Server.Callbacks;
using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Routers.Static;

[Injectable(typePriority: int.MaxValue)]
public class FriendlyTeammateMatchRouter(JsonUtil jsonUtil, FriendlyTeammateMatchCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<MatchGroupInviteSendRequest>(
                "/client/match/group/invite/send",
                async (url, info, sessionId, output) => await callbacks.SendGroupInvite(url, info, sessionId, output)
            ),
            new RouteAction<EmptyRequestData>(
                "/client/game/bot/followerdetails",
                async (url, info, sessionId, output) => await callbacks.GetFollowerDetails(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateFollowerGenerateRequest>(
                "/client/game/bot/followergenerate",
                async (url, info, sessionId, output) => await callbacks.GenerateFollowerProfile(url, info, sessionId)
            ),
        ]
    ) { }
