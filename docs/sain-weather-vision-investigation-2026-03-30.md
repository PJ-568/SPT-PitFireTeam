# SAIN Weather Vision Investigation (2026-03-30)

## Scope

Investigate whether SAIN has weather or time related settings that can make bots weaker than vanilla.

No code changes were made as part of this investigation.

## Summary

Verified result: yes, SAIN can make bots weaker than vanilla in weather-related and time-of-day-related conditions.

This is SAIN-owned behavior, not friendlySAIN addon logic.

## Verified SAIN-owned behavior

### 1. SAIN replaces vanilla weather visibility handling

File:

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Patches/VisionPatches.cs`

Relevant behavior:

- `WeatherVisionPatch` prefixes `EnemyInfo.method_11`
- for SAIN bots it forces `__result = 1f` and skips the original method

Why it matters:

- vanilla `EnemyInfo.method_11` is the stock weather visibility path
- SAIN bypasses that path and substitutes its own system

### 2. SAIN applies its own weather vision distance modifier

Files:

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/BotManager/SAINWeatherClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/Bot/Sense/SAINVisionClass.cs`

Relevant behavior:

- `SAINWeatherClass` computes `VisionDistanceModifier`
- `SAINVisionClass.UpdateVisionDistance()` applies:
  - weather distance modifier
  - time-of-day distance modifier
  - minimum caps from SAIN global settings
- final result is assigned into `LookSensor.ClearVisibleDist` and `LookSensor.VisibleDist`

Why it matters:

- SAIN directly controls effective visible distance for SAIN bots

### 3. SAIN applies extra gain-sight slowdown on top of distance changes

File:

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/Bot/EnemyClasses/Vision/EnemyGainSightClass.cs`

Relevant behavior:

- `GetGainSightModifier()` includes:
  - `weatherMod`
  - `timeMod`
  - angle, movement, pose, third-party, elevation, parts-visible, gear, and not-looking modifiers
- `VisionSpeedPatch` in `VisionPatches.cs` divides vanilla `EnemyInfo.method_9` result by SAIN’s gain-sight modifier

Why it matters:

- SAIN is not only reducing vision distance
- it also increases time-to-spot under bad weather / bad light conditions

### 4. SAIN defaults are harsher than vanilla in several weather/time cases

File:

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Preset/GlobalSettings/Categories/Look/TimeSettings.cs`

Verified defaults:

- `NightTimeVisionModifier = 0.2`
- `NightTimeVisionModifierSnow = 0.35`
- `TIME_GAIN_SIGHT_SCALE_MAX = 3f`
- `VISION_WEATHER_MIN_COEF = 0.33`
- `VISION_WEATHER_MIN_DIST_METERS = 30f`
- `VISION_WEATHER_FOG_MAXCOEF = 0.4`
- `VISION_WEATHER_RAIN_SRINKLE_COEF = 0.9`
- `VISION_WEATHER_RAIN_LIGHT_COEF = 0.65`
- `VISION_WEATHER_RAIN_NORMAL_COEF = 0.5`
- `VISION_WEATHER_RAIN_HEAVY_COEF = 0.45`
- `VISION_WEATHER_RAIN_DOWNPOUR_COEF = 0.4`
- `VISION_WEATHER_CLOUDY_COEF = 0.8`
- `VISION_WEATHER_OVERCAST_COEF = 0.7`

Interpretation:

- SAIN can reduce night distance heavily
- SAIN can reduce fog and rain distance more aggressively than vanilla
- SAIN can also make spotting take up to 3x longer at bad times of day

### 5. SAIN also reduces hearing in rain

Files:

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/BotManager/SAINWeatherClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Components/PlayerComponent.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Preset/GlobalSettings/Categories/HearingSettings.cs`

Relevant behavior:

- rain sound modifiers are calculated in `SAINWeatherClass`
- `PlayerComponent` multiplies incoming sound volume by those rain modifiers

Verified defaults:

- `RAIN_SOUND_COEF_OUTSIDE = 0.5`
- `RAIN_SOUND_COEF_INSIDE = 0.75`

Why it matters:

- SAIN can also weaken hearing during rain, especially outdoors

## Vanilla comparison points

### 1. Vanilla already has weather penalties

Files:

- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/EnemyInfo.cs`
- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/BotGlobalLookData.cs`

Relevant behavior:

- vanilla `EnemyInfo.method_11()` calls `LookSensor.WeatherVisibilityK(...)`
- vanilla look settings include:
  - `RAIN_DEBUFF_MAXVISIBILITY_MULTIPLYER = 0.7`
  - `FOG_DEBUFF_MAXVISIBILITY_MULTIPLYER = 0.8`
  - `RAIN_DEBUFF_SEENCOEFF_MULTIPLYER = 0.5`
  - `FOG_DEBUFF_SEENCOEFF_MULTIPLYER = 0.8`

Interpretation:

- vanilla already weakens bots in rain/fog
- SAIN is not inventing the concept
- SAIN is replacing the stock path and uses different tuning

### 2. Vanilla also has runtime vision effect multipliers

Files:

- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/EnemyInfo.cs`
- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/BotDifficultySettingsClass.cs`

Relevant behavior:

- vanilla visibility speed path multiplies by `settings.Current.RuntimeVisionEffectsK`
- this is part of the stock visibility formula

Interpretation:

- vanilla has a generic runtime vision effect mechanism
- SAIN adds its own parallel gain-sight and distance systems on top of or instead of parts of the stock behavior

## Practical conclusion

If bots feel weaker than vanilla in rain, fog, clouds, dusk, dawn, or night with SAIN installed, that is a credible and source-verified explanation.

The most likely future tuning targets are:

- SAIN global look time settings
- SAIN weather vision coefficients
- SAIN hearing rain coefficients
- SAIN gain-sight scaling at night / low visibility

## Files to revisit later

- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Patches/VisionPatches.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/BotManager/SAINWeatherClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/BotManager/TimeClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/Bot/Sense/SAINVisionClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Classes/Bot/EnemyClasses/Vision/EnemyGainSightClass.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Preset/GlobalSettings/Categories/Look/TimeSettings.cs`
- `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN/Preset/GlobalSettings/Categories/HearingSettings.cs`
- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/EnemyInfo.cs`
- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/BotGlobalLookData.cs`
