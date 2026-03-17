using friendlySAIN.Server.Callbacks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Routers.Static;

[Injectable(typePriority: int.MaxValue)]
public class FriendlyTeammateSocialRouter(JsonUtil jsonUtil, FriendlyTeammateSocialCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<EmptyRequestData>(
                "/client/friend/list",
                async (url, info, sessionId, output) => await callbacks.MergeFriendList(url, info, sessionId, output)
            ),
            new RouteAction<GetOtherProfileRequest>(
                "/client/profile/view",
                async (url, info, sessionId, output) => await callbacks.MergeProfileView(url, info, sessionId, output)
            ),
            new RouteAction<DeleteFriendRequest>(
                "/client/friend/delete",
                async (url, info, sessionId, output) => await callbacks.DeleteFriend(url, info, sessionId, output)
            ),
        ]
    ) { }
