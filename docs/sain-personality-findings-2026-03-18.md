# SAIN Personality Findings (2026-03-18)

Scope: confirm whether pitFireTeam followers switch between Normal and Chad-like SAIN behavior personalities, and separate this from aiming/bot-settings template work.

## Confirmed Results

1. SAIN has explicit behavior personalities, including:

- Normal
- Chad
- GigaChad
- Wreckless
- Rat
- Timmy
- Coward

Source:

- SAIN enum and personality classes under `SAIN-4.4.0/SAIN/Models/Preset/Personalities` and `SAIN-4.4.0/SAIN/Preset/Personalities`.

2. SAIN personality assignment is done by SAIN itself via personality dictionary logic.

- `SAINBotInfoClass.GetPersonality(...)` calls `PersonalityDictionary.GetPersonality(...)`.
- Fallback path for PMCs can assign Chad with a chance, otherwise Normal.

Source:

- `SAIN-4.4.0/SAIN/Classes/Bot/Info/SAINBotInfoClass.cs`
- `SAIN-4.4.0/SAIN/Models/Preset/Personalities/PersonalityDictionary.cs`

3. pitFireTeam addon personality patch does NOT currently force a behavior personality (e.g. force Chad).

- It applies a per-follower clone of SAIN `followerBigPipe` bot settings template (`_fileSettings`).
- It rebuilds SAIN info from that template and aligns difficulty modifiers.
- It does not call `SetPersonality(...)` or hard-assign `EPersonality.Chad`/`Normal`.

Source:

- `pitFireTeam/addon/SAINFollowerPersonalityPatch.cs`

4. The active behavior baseline in addon is still template-driven, not personality-forced.

- `ApplyFollowerTemplateFineTuning(...)` exists as the single hook for extra follower tuning.
- It is intentionally empty right now.

Source:

- `pitFireTeam/addon/SAINFollowerPersonalityPatch.cs`

5. Some old SAIN follower tuning patches exist in source but are not currently wired by bootstrap.

- Example files include aim-target/random-look/hit-accuracy patches.
- Current bootstrap wiring includes `SAINFollowerPersonalityPatch`, not those legacy tuning patches.

Source:

- `pitFireTeam/addon/SAINRegroupBootstrap.cs`

## Normal vs Chad Behavior Defaults (SAIN)

From SAIN personality defaults:

- Chad:
    - lower search base time than Normal
    - more sprint while searching
    - aggressive search/chase settings enabled
    - rush reload/heal allowed

- Normal:
    - longer search base time
    - lower sprint while searching
    - more baseline/average behavior profile

Source:

- `SAIN-4.4.0/SAIN/Preset/Personalities/BasePersonality/PersonalityDefaultsClass.cs`

## Practical Interpretation for pitFireTeam

- If follower behavior feels more/less aggressive, personality can still vary through SAIN assignment logic.
- pitFireTeam currently does not force follower personality to Chad or Normal.
- pitFireTeam does force bot settings template to `followerBigPipe` and adjusts difficulty modifiers.

## Related Logging Note

Recent raid logs showed repeated AI exceptions from vanilla follower patrol update path (`BotBoss.get_MoveSpeed`).
A guard was added in pitFireTeam `PatrolDataFollowerUpdateGuardPatch` to avoid repeated exception spam/perf hit when AI boss MoveSpeed access is invalid.

Source:

- `pitFireTeam/client/Patches/FollowerVanillaSafetyPatch.cs`
