using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Utils;
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
        logger.Info("friendlySAIN.Server loaded");
        return Task.CompletedTask;
    }

    private void EnforcePmcArmbands()
    {
        var botTypes = databaseService.GetBots().Types;

        ApplyForcedArmband(botTypes, "assaultgroup", ItemTpl.ARMBAND_BLUE);
        ApplyForcedArmband(botTypes, "usec", ItemTpl.ARMBAND_BLUE);
        ApplyForcedArmband(botTypes, "bear", ItemTpl.ARMBAND_RED);
    }

    private void ApplyForcedArmband(
        Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Tables.BotType?> botTypes,
        string botType,
        MongoId armbandTpl
    )
    {
        if (!botTypes.TryGetValue(botType, out var bot) || bot is null)
        {
            logger.Warning($"Unable to enforce armband for missing bot type '{botType}'");
            return;
        }

        bot.BotChances.EquipmentChances["Armband"] = 100;
        bot.BotInventory.Equipment[EquipmentSlots.ArmBand] = new Dictionary<MongoId, double>
        {
            [armbandTpl] = 1,
        };
    }
}
