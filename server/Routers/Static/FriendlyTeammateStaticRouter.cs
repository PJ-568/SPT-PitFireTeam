using friendlySAIN.Server.Callbacks;
using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Routers.Static;

[Injectable]
public class FriendlyTeammateStaticRouter(JsonUtil jsonUtil, FriendlyTeammateCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyTeammateCreateRequest>(
                "/singleplayer/friendlysain/teammate/create",
                async (url, info, sessionId, output) => await callbacks.Create(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/friendlysain/teammates",
                async (url, info, sessionId, output) => await callbacks.List(url, info, sessionId)
            ),
            new RouteAction<GetOtherProfileRequest>(
                "/singleplayer/friendlysain/teammate/profile",
                async (url, info, sessionId, output) => await callbacks.GetProfile(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateProfileOptionsRequest>(
                "/singleplayer/friendlysain/teammate/profile/options",
                async (url, info, sessionId, output) => await callbacks.GetProfileOptions(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateSuitRequest>(
                "/singleplayer/friendlysain/teammate/profile/suit",
                async (url, info, sessionId, output) => await callbacks.SetSuit(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateLoadoutRequest>(
                "/singleplayer/friendlysain/teammate/profile/loadout",
                async (url, info, sessionId, output) => await callbacks.SetLoadout(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateDeleteRequest>(
                "/singleplayer/friendlysain/teammate/delete",
                async (url, info, sessionId, output) => await callbacks.Delete(url, info, sessionId)
            ),
        ]
    ) { }
