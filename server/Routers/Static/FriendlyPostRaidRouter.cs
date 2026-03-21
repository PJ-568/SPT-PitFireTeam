using friendlySAIN.Server.Callbacks;
using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Routers.Static;

[Injectable]
public class FriendlyPostRaidRouter(JsonUtil jsonUtil, FriendlyPostRaidCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyPostRaidReturnItemsRequest>(
                "/singleplayer/returnitems",
                async (url, info, sessionId, output) => await callbacks.ReturnItems(url, info, sessionId)
            ),
            new RouteAction<FriendlyPostRaidTeamEscapedRequest>(
                "/singleplayer/teamescaped",
                async (url, info, sessionId, output) => await callbacks.TeamEscaped(url, info, sessionId)
            ),
            new RouteAction<FriendlyRecruitPickupRequest>(
                "/singleplayer/friendlysain/recruitpickup",
                async (url, info, sessionId, output) => await callbacks.RecruitPickup(url, info, sessionId)
            ),
        ]
    )
{ }
