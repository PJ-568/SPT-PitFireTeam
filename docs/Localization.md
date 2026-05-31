# Localization

Last updated: 2026-05-31

## Scope

This document is the source of truth for pitFireTeam language text and fallback behavior across the client and server.

Read this before adding or changing user-facing text in:

- `client/Components`
- `client/Patches`
- `client/Modules`
- `server/Services`
- `server/Resources/lang`

## Runtime Model

The client owns the embedded English fallback.

- `client/Localization/EmbeddedEnglishLanguageProvider.cs` builds the canonical in-DLL English `LanguageOptions` object.
- `client/friendlyPlugin.cs` loads the active game language through `/singleplayer/pitfireteam/lang`.
- The request sends the active locale plus serialized embedded English to the server.
- `server/Services/FriendlyLanguageService.cs` writes or repairs `server/Resources/lang/en.json` from embedded English, merges selected locale files over English, and returns the merged language JSON.
- If loading language JSON fails, the client must keep using embedded English. It must not fall back to scattered hardcoded strings.
- The server keeps a cloned embedded-English fallback after the language request so server-side message arrays/maps can still resolve text if `en.json` is missing or broken.

## Authoritative Files

- Embedded runtime fallback: `client/Localization/EmbeddedEnglishLanguageProvider.cs`
- Client lookup facade: `client/friendlyPlugin.cs`
- Editable English resource: `server/Resources/lang/en.json`
- Optional translations: `server/Resources/lang/<locale>.json`
- Server merge/fallback service: `server/Services/FriendlyLanguageService.cs`
- Router/callback path:
    - `server/Routers/Static/FriendlyLanguageRouter.cs`
    - `server/Callbacks/FriendlyLanguageCallbacks.cs`

## Client Lookup Rules

Do not add local string fallback arguments at callsites.

Use the centralized lookup helpers:

- Social/My Squad/profile/loadout UI: `pitFireTeam.GetSocialUiText("Key")`
- Gesture text: `pitFireTeam.GetGestureText("Key")`
- Bot status text: `pitFireTeam.GetBotStatusText("Key")`
- Tactic option array text: `pitFireTeam.GetTacticOptionText(index)`
- Top-level section labels: `pitFireTeam.GetLanguageText(language => language.someProperty)`

The helper order is:

1. loaded/merged active language
2. embedded English fallback
3. the key name only if no value exists anywhere

Avoid patterns like:

```csharp
GetSocialUiText("AddTeammate", "+ Add teammate")
pitFireTeam.optionsLang.socialUi["AddTeammate"]
pitFireTeam.optionsLang.gestures["TeamStatus"]
pitFireTeam.optionsLang?.baseSettings ?? "Base Settings"
```

Those duplicate fallback text, bypass embedded English, or can throw when language data is not ready.

## Adding Or Renaming Text

When adding a new user-facing key:

1. Add it to `LanguageOptions` in `client/friendlyPlugin.cs` when it needs a new top-level object/property.
2. Add the English value to `EmbeddedEnglishLanguageProvider.Create()`.
3. Add the same English value to `server/Resources/lang/en.json`.
4. Use the centralized client lookup helper at the callsite.
5. Add optional translations to other locale files only when available. Do not block on them; the language service merges missing values from English.

For nested UI text, prefer the existing `socialUi` map unless the text belongs to another existing language group such as `gestures`, `botStatus`, or a setting entry.

For config entries, prefer a structured `{ "Name": "...", "Description": "..." }` entry so both ConfigurationManager and My Squad Settings can reuse the same data.

## Server Usage

Server systems that send teammate messages should use `FriendlyLanguageService` methods:

- `GetStringArray(sessionId, key)`
- `GetStringMap(sessionId, key)`

Do not hardcode message bodies in post-raid or courier flows when the text belongs in the language model.

Server log messages and developer diagnostics do not need localization.

## Maintenance Checks

Before finishing localization work, run:

```powershell
rg -n 'GetSocialUiText\([^\)]*,|GetLocalizedSocialUi\([^\)]*,|GetGestureText\([^\)]*,|optionsLang\.(socialUi|gestures|botStatus|tacticOptions)' client -S
```

Expected result: no matches except intentional docs/tests.

Also build:

```powershell
dotnet build pitFireTeam.sln -c Debug
```
