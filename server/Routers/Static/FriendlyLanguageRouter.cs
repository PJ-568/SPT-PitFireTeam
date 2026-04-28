using friendlySAIN.Server.Callbacks;
using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Routers.Static;

[Injectable]
public class FriendlyLanguageRouter(JsonUtil jsonUtil, FriendlyLanguageCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyLanguageRequest>(
                "/singleplayer/friendlysain/lang",
                async (url, info, sessionId, output) => await callbacks.Get(url, info, sessionId)
            ),
            new RouteAction<FriendlyLanguageRequest>(
                "/singleplayer/pitlang",
                async (url, info, sessionId, output) => await callbacks.Get(url, info, sessionId)
            ),
        ]
    )
{ }
