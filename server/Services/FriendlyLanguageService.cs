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

    public string GetLanguageJson(string? requestedLocale, string? embeddedEnglishJson)
    {
        string locale = NormalizeLocale(requestedLocale);
        string languageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            ModFolderName,
            "Resources",
            LanguageFolderName);

        JsonObject? embeddedEnglish = ParseLanguageJson(embeddedEnglishJson, "embedded English language");
        EnsureEnglishLanguageFile(languageDirectory, embeddedEnglish);

        JsonObject fallback = LoadLanguageFile(languageDirectory, "en")
            ?? CloneObject(embeddedEnglish)
            ?? [];
        JsonObject selected = string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : LoadLanguageFile(languageDirectory, locale) ?? [];

        if (!ReferenceEquals(fallback, selected))
        {
            MergeMissingValues(selected, fallback);
        }

        return selected.ToJsonString(SerializerOptions);
    }

    private void EnsureEnglishLanguageFile(string languageDirectory, JsonObject? embeddedEnglish)
    {
        if (embeddedEnglish == null || embeddedEnglish.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(languageDirectory);

        string path = Path.Combine(languageDirectory, "en.json");
        JsonObject? current = LoadLanguageFile(languageDirectory, "en");
        if (current == null)
        {
            WriteLanguageFile(path, embeddedEnglish);
            return;
        }

        if (!MergeMissingValues(current, embeddedEnglish))
        {
            return;
        }

        WriteLanguageFile(path, current);
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
            return ParseLanguageJson(File.ReadAllText(path), path);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to load friendlySAIN language file '{path}': {ex.Message}");
            return null;
        }
    }

    private JsonObject? ParseLanguageJson(string? json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is JsonObject language)
            {
                return language;
            }

            logger.Warning($"friendlySAIN language source '{source}' is not a JSON object.");
            return null;
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to parse friendlySAIN language source '{source}': {ex.Message}");
            return null;
        }
    }

    private void WriteLanguageFile(string path, JsonObject language)
    {
        try
        {
            File.WriteAllText(path, language.ToJsonString(SerializerOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to write friendlySAIN language file '{path}': {ex.Message}");
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

    private static bool MergeMissingValues(JsonObject target, JsonObject fallback)
    {
        bool changed = false;
        foreach (KeyValuePair<string, JsonNode?> fallbackEntry in fallback)
        {
            if (!target.TryGetPropertyValue(fallbackEntry.Key, out JsonNode? targetValue) || targetValue == null)
            {
                target[fallbackEntry.Key] = fallbackEntry.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (!IsCompatibleLanguageNode(targetValue, fallbackEntry.Value))
            {
                target[fallbackEntry.Key] = fallbackEntry.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (targetValue is JsonObject targetObject && fallbackEntry.Value is JsonObject fallbackObject)
            {
                changed |= MergeMissingValues(targetObject, fallbackObject);
            }
        }

        return changed;
    }

    private static bool IsCompatibleLanguageNode(JsonNode target, JsonNode? fallback)
    {
        if (fallback == null)
        {
            return true;
        }

        return fallback switch
        {
            JsonObject => target is JsonObject,
            JsonArray => target is JsonArray,
            JsonValue => target is JsonValue,
            _ => true
        };
    }

    private static JsonObject? CloneObject(JsonObject? value)
    {
        return value?.DeepClone() as JsonObject;
    }

}
