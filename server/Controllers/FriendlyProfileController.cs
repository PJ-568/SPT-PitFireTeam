using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace pitTeam.Server.Controllers;

[Injectable(typeOverride: typeof(ProfileController))]
public class FriendlyProfileController(
    ISptLogger<ProfileController> logger,
    SaveServer saveServer,
    CreateProfileService createProfileService,
    ProfileFixerService profileFixerService,
    PlayerScavGenerator playerScavGenerator,
    ProfileHelper profileHelper,
    FriendlyTeammateService teammateService)
    : ProfileController(
        logger,
        saveServer,
        createProfileService,
        profileFixerService,
        playerScavGenerator,
        profileHelper)
{
    public override GetOtherProfileResponse GetOtherProfile(MongoId sessionId, GetOtherProfileRequest request)
    {
        if (teammateService.TryGetTeammateProfile(sessionId, request.AccountId, out var teammateProfile)
            && teammateProfile != null)
        {
            return teammateProfile;
        }

        return base.GetOtherProfile(sessionId, request);
    }
}
