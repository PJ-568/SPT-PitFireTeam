using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyTeammateCallbacks(
    HttpResponseUtil httpResponse,
    FriendlyTeammateService teammateService
)
{
    public ValueTask<string> Create(string url, FriendlyTeammateCreateRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.CreateTeammate(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> List(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(teammateService.ListTeammates(sessionId)));
    }

    public ValueTask<string> ListAutoJoin(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(teammateService.GetAutoJoinTeammateAccountIds(sessionId)));
    }

    public ValueTask<string> GetProfile(string url, GetOtherProfileRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.GetTeammateProfile(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> GetProfileOptions(string url, FriendlyTeammateProfileOptionsRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.GetProfileOptions(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetSuit(string url, FriendlyTeammateSuitRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateSuit(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> Rename(string url, FriendlyTeammateRenameRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.RenameTeammate(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetLoadout(string url, FriendlyTeammateLoadoutRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateLoadout(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SaveDefaultEquipment(string url, FriendlyTeammateDefaultEquipmentRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SaveTeammateDefaultEquipment(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetAggression(string url, FriendlyTeammateAggressionRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateAggression(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetTactic(string url, FriendlyTeammateTacticRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateTactic(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetAutoJoin(string url, FriendlyTeammateAutoJoinRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateAutoJoin(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> Delete(string url, FriendlyTeammateDeleteRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(
                httpResponse.GetBody(new FriendlyTeammateDeleteResponse { Deleted = teammateService.DeleteTeammate(sessionId, request) })
            );
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }
}
