using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Utils;
using friendlySAIN.Server.Services;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace friendlySAIN.Server;

public record FriendlySainServerMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "xyz.pit.friendlysain.server";
    public override string Name { get; init; } = "friendlySAIN.Server";
    public override string Author { get; init; } = "pit";
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class FriendlySainServerPlugin(
    ISptLogger<FriendlySainServerPlugin> logger,
    DatabaseService databaseService
) : IOnLoad
{
    public Task OnLoad()
    {
        EnforcePmcArmbands();
        RegisterSquadCourierTrader();
        logger.Info("friendlySAIN.Server loaded");
        return Task.CompletedTask;
    }

    private void EnforcePmcArmbands()
    {
        var botTypes = databaseService.GetBots().Types;

        ApplyForcedArmband(botTypes, new[] { "usec", "pmcusec" }, ItemTpl.ARMBAND_BLUE);
        ApplyForcedArmband(botTypes, new[] { "bear", "pmcbear" }, ItemTpl.ARMBAND_RED);
    }

    private void RegisterSquadCourierTrader()
    {
        var traders = databaseService.GetTraders();
        if (!traders.ContainsKey(FriendlyCourierTraderProfile.CourierTraderId))
        {
            traders[FriendlyCourierTraderProfile.CourierTraderId] = FriendlyCourierTraderProfile.CreateTrader();
        }

        foreach (var locale in databaseService.GetLocales().Global.Values)
        {
            var entries = locale.Value;
            if (entries == null)
            {
                continue;
            }

            entries[$"{FriendlyCourierTraderProfile.CourierTraderIdValue} FullName"] = FriendlyCourierTraderProfile.CourierNickname;
            entries[$"{FriendlyCourierTraderProfile.CourierTraderIdValue} FirstName"] = FriendlyCourierTraderProfile.CourierNickname;
            entries[$"{FriendlyCourierTraderProfile.CourierTraderIdValue} Nickname"] = FriendlyCourierTraderProfile.CourierNickname;
            entries[$"{FriendlyCourierTraderProfile.CourierTraderIdValue} Location"] = FriendlyCourierTraderProfile.CourierLocation;
            entries[$"{FriendlyCourierTraderProfile.CourierTraderIdValue} Description"] = FriendlyCourierTraderProfile.CourierDescription;
        }
    }

    private void ApplyForcedArmband(
        Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Tables.BotType?> botTypes,
        IEnumerable<string> botTypeKeys,
        MongoId armbandTpl
    )
    {
        foreach (var botTypeKey in botTypeKeys)
        {
            if (!botTypes.TryGetValue(botTypeKey, out var bot) || bot is null)
            {
                logger.Warning($"Unable to enforce armband for missing bot type '{botTypeKey}'");
                continue;
            }

            bot.BotChances.EquipmentChances["Armband"] = 100;
            bot.BotInventory.Equipment[EquipmentSlots.ArmBand] = new Dictionary<MongoId, double>
            {
                [armbandTpl] = 1,
            };
        }
    }
}
