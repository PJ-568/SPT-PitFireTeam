using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils.Json;
using System.IO;
using pitTeam.Server.Services;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace pitTeam.Server;

public record PitFireTeamServerMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "xyz.pit.fireteam.server";
    public override string Name { get; init; } = "pitFireTeam.Server";
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
public class PitFireTeamServerPlugin(
    ISptLogger<PitFireTeamServerPlugin> logger,
    DatabaseService databaseService
) : IOnLoad
{
    public Task OnLoad()
    {
        EnsureCourierTraderRegistered();
        EnsureCourierTraderLocales();
        EnsureCourierAvatarIsServed();
        EnforcePmcArmbands();
        logger.Info("pitFireTeam.Server loaded");
        return Task.CompletedTask;
    }

    private void EnsureCourierTraderRegistered()
    {
        try
        {
            var traders = databaseService.GetTraders();
            if (traders.ContainsKey(FriendlyCourierTraderProfile.CourierTraderId))
            {
                return;
            }

            traders[FriendlyCourierTraderProfile.CourierTraderId] = FriendlyCourierTraderProfile.CreateTrader();
            logger.Info($"Registered courier trader '{FriendlyCourierTraderProfile.CourierTraderIdValue}'");
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to register courier trader: {ex.Message}");
        }
    }

    private void EnsureCourierTraderLocales()
    {
        try
        {
            string traderId = FriendlyCourierTraderProfile.CourierTraderIdValue;
            foreach (var (locale, lazyGlobal) in databaseService.GetLocales().Global)
            {
                lazyGlobal.AddTransformer(localized =>
                {
                    if (localized == null)
                    {
                        return localized;
                    }

                    FriendlyCourierTraderProfile.GetLocalizedIdentity(
                        locale,
                        out string nickname,
                        out string location,
                        out string description);

                    localized[$"{traderId} Nickname"] = nickname;
                    localized[$"{traderId} FirstName"] = nickname;
                    localized[$"{traderId} FullName"] = nickname;
                    localized[$"{traderId} Location"] = location;
                    localized[$"{traderId} Description"] = description;
                    return localized;
                });
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to inject courier trader locale keys: {ex.Message}");
        }
    }

    private void EnsureCourierAvatarIsServed()
    {
        try
        {
            string serverRoot = AppContext.BaseDirectory;
            string sourcePath = Path.Combine(
                serverRoot,
                "user",
                "mods",
                "pitFireTeam-ServerMod",
                "Resources",
                "avatars",
                "courier.png");
            if (!File.Exists(sourcePath))
            {
                logger.Warning($"Courier avatar source missing: {sourcePath}");
                return;
            }

            string targetDirectory = Path.Combine(serverRoot, "user", "sptappdata", "files", "trader", "avatar");
            Directory.CreateDirectory(targetDirectory);

            string targetPath = Path.Combine(targetDirectory, FriendlyCourierTraderProfile.CourierAvatarFileName);
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to publish courier avatar: {ex.Message}");
        }
    }

    private void EnforcePmcArmbands()
    {
        var botTypes = databaseService.GetBots().Types;

        ApplyForcedArmband(botTypes, new[] { "usec", "pmcusec" }, ItemTpl.ARMBAND_BLUE);
        ApplyForcedArmband(botTypes, new[] { "bear", "pmcbear" }, ItemTpl.ARMBAND_RED);
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
