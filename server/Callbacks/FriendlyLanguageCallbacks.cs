using friendlySAIN.Server.Models;
using friendlySAIN.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace friendlySAIN.Server.Callbacks;

[Injectable]
public class FriendlyLanguageCallbacks(FriendlyLanguageService languageService)
{
    public ValueTask<string> Get(string url, FriendlyLanguageRequest request, MongoId sessionId)
    {
        return new ValueTask<string>(languageService.GetLanguageJson(request.Locale, request.EnglishJson));
    }
}
