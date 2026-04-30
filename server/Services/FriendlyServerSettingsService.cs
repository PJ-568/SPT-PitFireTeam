using System.Text.Json;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace pitTeam.Server.Services;

#pragma warning disable CS0618 // ConfigServer is the stable config access path in the current SPT 4.08 target.
[Injectable]
public class FriendlyServerSettingsService(
    ISptLogger<FriendlyServerSettingsService> logger,
    ConfigServer configServer
)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public FriendlyServerSettingsRequest LoadSettings()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new FriendlyServerSettingsRequest();
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FriendlyServerSettingsRequest>(json, JsonOptions)
                ?? new FriendlyServerSettingsRequest();
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to load pitFireTeam server settings: {ex.Message}");
            return new FriendlyServerSettingsRequest();
        }
    }

    public void SaveAndApply(FriendlyServerSettingsRequest settings)
    {
        settings ??= new FriendlyServerSettingsRequest();
        SaveSettings(settings);
        ApplySettings(settings);
    }

    public void ApplyPersistedSettings()
    {
        ApplySettings(LoadSettings());
    }

    private void SaveSettings(FriendlyServerSettingsRequest settings)
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to save pitFireTeam server settings: {ex.Message}");
        }
    }

    private void ApplySettings(FriendlyServerSettingsRequest settings)
    {
        try
        {
            PmcConfig pmcConfig = configServer.GetConfig<PmcConfig>();
            pmcConfig.ForceArmband.Enabled = settings.PmcArmbands;

            if (settings.PmcArmbands)
            {
                pmcConfig.ForceArmband.Usec = ItemTpl.ARMBAND_BLUE;
                pmcConfig.ForceArmband.Bear = ItemTpl.ARMBAND_RED;
            }

            logger.Info($"pitFireTeam PMC armband enforcement: {(settings.PmcArmbands ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to configure PMC armbands: {ex.Message}");
        }
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            "pitFireTeam-ServerMod",
            "Resources",
            "settings.json");
    }
}
#pragma warning restore CS0618
