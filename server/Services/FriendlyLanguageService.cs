using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;

namespace friendlySAIN.Server.Services;

[Injectable]
public class FriendlyLanguageService(ISptLogger<FriendlyLanguageService> logger)
{
    private const string ModFolderName = "friendlySAIN-ServerMod";
    private const string LanguageFolderName = "lang";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public string GetLanguageJson(string? requestedLocale)
    {
        string locale = NormalizeLocale(requestedLocale);
        string languageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            ModFolderName,
            "Resources",
            LanguageFolderName);

        JsonObject fallback = LoadLanguageFile(languageDirectory, "en") ?? [];
        JsonObject selected = string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : LoadLanguageFile(languageDirectory, locale) ?? [];

        if (!ReferenceEquals(fallback, selected))
        {
            MergeMissingValues(selected, fallback);
        }

        return selected.ToJsonString(SerializerOptions);
    }

    private JsonObject? LoadLanguageFile(string languageDirectory, string locale)
    {
        string path = Path.Combine(languageDirectory, $"{locale}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            return node as JsonObject;
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to load friendlySAIN language file '{path}': {ex.Message}");
            return null;
        }
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        string normalized = locale.Trim().ToLowerInvariant();
        int separatorIndex = normalized.IndexOfAny(['-', '_']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return normalized switch
        {
            "de" => "ge",
            _ => normalized
        };
    }

    private static void MergeMissingValues(JsonObject target, JsonObject fallback)
    {
        foreach (KeyValuePair<string, JsonNode?> fallbackEntry in fallback)
        {
            if (!target.TryGetPropertyValue(fallbackEntry.Key, out JsonNode? targetValue) || targetValue == null)
            {
                target[fallbackEntry.Key] = fallbackEntry.Value?.DeepClone();
                continue;
            }

            if (targetValue is JsonObject targetObject && fallbackEntry.Value is JsonObject fallbackObject)
            {
                MergeMissingValues(targetObject, fallbackObject);
            }
        }
    }

}
