# AI Role: friendlySAIN AI Mod Engineer

You are an AI engineering agent working on `friendlySAIN`, a C# mod for Single Player Tarkov built with BepInEx, Harmony, BigBrain, and optional SAIN integration.

Your job is to make safe, context-aware changes that preserve runtime stability, respect current architecture, and avoid assumptions about Tarkov/SPT/SAIN internals.

You must think like a maintainer of a fragile gameplay-AI integration project, not like a generic C# assistant.

## Working Rules

Read code first. Assume nothing.

If a method, class, property, or runtime behavior is unclear:

- inspect the project source code
- inspect SAIN or BigBrain source if involved
- inspect decompiled EFT/SPT references when necessary

Never invent APIs, properties, or behaviors that do not exist. Only reference methods, properties, and classes that are verified in the source code.

Separate vanilla and SAIN reasoning. Every behavior should be classified as one of:

- vanilla / core plugin path
- SAIN addon / SAIN-owned path

Do not mix these paths unless the code clearly bridges them.

When fixing bugs or implementing changes:

- make the smallest correct change
- avoid broad refactors unless explicitly requested
- preserve current architecture and naming style
- prefer stability over elegance

## Decision Priority

When multiple approaches are possible, prefer:

1. runtime stability
2. preserving existing architecture
3. minimal code changes
4. improved clarity or debugging
5. improved elegance

# friendlySAIN: Current Implementation Summary

Last updated: 2026-03-08  
Scope: runtime behavior currently present in `friendlySAIN/client` and `friendlySAIN/addon` (based on active code paths in `friendlyPlugin.cs` and addon bootstrap/patches).

## BE / Server State (Important)

- Backend (BE) support for this plugin is **not implemented yet**.
- Do **not** add new runtime paths that depend on **plugin-owned/custom** BE profile generation/fetch endpoints for debug/runtime bot spawn flows.
- Using existing **game-side** profile loading flow (`ISession.LoadBots`) is allowed.
- If a spawn flow requires BE profile data and no local/prefetched profile exists, it should fail fast with a clear reason instead of attempting BE fallback.
- `InteractableObjects` BE return-items endpoint call is currently disabled in client runtime (`/singleplayer/returnitems` is not posted).

## 0) Project Context

- Old plugin codebase: `F:/Projects/SPT-Tarkov/friendlypmc`
- Old client reference (3.11): `F:/Projects/SPT-Tarkov/Client-Decompiled-3.11`
- New client reference (4.x): `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x`
- SAIN plugin reference: `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN`
- Positioning:
    - `friendlySAIN` is both:
        - a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment,
        - and an alternative plugin implementation with new BigBrain-native follower layers/actions.

## 1) Core Runtime Model

- Plugin: `xyz.pit.friendlysain` (`client/friendlyPlugin.cs`)
- Dependency: BigBrain (`xyz.drakia.bigbrain`)
- Optional integration: SAIN (`me.sol.sain`) detected at runtime.
- Optional SAIN addon integration: `xyz.pit.friendlysain.sainaddon` (separate DLL in `addon/`)
- Follower control model:
    - Combat remains vanilla/SAIN-owned.
    - Friendly follow logic is implemented as a BigBrain custom layer/action (`FollowerPatrolLayer` + `FollowAction`).
    - Regroup request execution is split by runtime context:
        - vanilla regroup path for no-SAIN or out-of-combat,
        - SAIN combat path is handled by addon `SAINFollowerCombatLayer` (custom SAIN squad-layer replacement for followers).

## 2) BigBrain Follower Layer

Files:

- `client/BigBrain/FollowerPatrolLayer.cs`
- `client/BigBrain/FollowerRequestLayer.cs`
- `client/BigBrain/Actions/FollowAction.cs`
- `client/BigBrain/Actions/HealAction.cs`
- `client/BigBrain/Actions/GestureCommandAction.cs`
- Additional peace actions in `client/BigBrain/Actions/*` (peace/look/gesture/etc.)

Behavior currently implemented:

- Registers `friendlySAIN.FollowerPatrol` layer for multiple brains (`PmcBear`, `PmcUsec`, `ExUsec`, `PMC`, `Assault`, `Knight`, etc.).
- Layer active only when:
    - bot is alive/active,
    - bot follows `pitAIBossPlayer`,
    - bot has no current enemy.
- Post-combat handoff to patrol is state-driven (no fixed timeout):
    - waits for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of forcing patrol after a fixed delay,
    - for SAIN-installed runtime, readiness is now resolved through addon-registered bridge callback (`SainAddonBridge.IsReadyForPatrolAfterCombat`),
    - when SAIN is installed and patrol bridge callback is unavailable, logic fails closed and logs explicit addon-missing bridge error once.
- Layer `Start()` performs recovery/reset:
    - pauses patrol data,
    - clears active request,
    - runs `FollowerRecovery.SoftReset`,
    - disposes current logic instance when possible.
- Action selection:
    - healing action while med work exists,
    - follow action otherwise,
    - includes out-of-combat reload handling.
- Healing action has timeout/cancel safety to prevent heal stuck states.

Follow movement:

- Follow logic is aligned toward old vanilla follower patrol style:
    - out-of-range chase toward leader,
    - in-range settle to cover/random nearby point using `GoToSomePointData`.
- Sprint run-stop mitigation:
    - `FollowerSprintStateDirectionPatch` modifies sprint-state direction under strict follower-chase conditions to avoid `Sprint -> Transition` thrash.
    - `FollowerSprintPatch` is only enabled when SAIN is **not** installed.

Request/gesture movement:

- Registers `friendlySAIN.FollowerRequest` custom layer (priority `73`) above patrol (`72`).
- `FollowerRequestLayer` activates when follower has an active command in `BotFollowerPlayer`.
- `GestureCommandAction` handles:
    - `HoldPosition`: stop, crouch pose, periodic random look-around, no command timeout (persists until replaced/cleared).
    - `ComeCloser`: move to boss until close (about `1m`).
    - `MoveToPoint` (`There`): move to projected/navmesh-validated target point (walk-only), then brief look-around on arrival.
    - `LootGeneric` / `LootWeapon` command route:
        - boss phrase selects closest eligible follower to the targeted loot object,
        - follower is assigned as taker through `InteractableObjects.SetTaker(...)`,
        - follower runs `FollowerCommandType.TakeLootItem` in `GestureCommandAction` (move to loot point + inventory transfer attempt),
        - BE item-return post is disabled; loot tracking remains local-only for now.
    - `OpenDoor` command route:
        - boss phrase selects closest eligible follower to the targeted door,
        - follower is assigned as opener through `InteractableObjects.SetOpener(...)`,
        - follower runs `FollowerCommandType.OpenDoor` in `GestureCommandAction` (move to door + `DoorOpener.Interact(..., Open)`),
        - opener/taker state is cleared when command clears, including combat-entry handoff.
    - `Regroup` (`EPhraseTrigger.Regroup`):
        - vanilla regroup is implemented and active for no-SAIN or out-of-combat cases,
        - SAIN combat regroup is executed through addon `SAINFollowerCombatLayer` -> `SAINFollowerCombatRegroupAction`,
        - regroup converges to boss-near cover/random point (not exact boss position) and supports boss-movement reanchor.
    - Regroup ignore/interruption safeguards:
        - ignored when follower is healing or already close enough (`~8m` nav-path distance on same level),
        - interrupted/released when follower can see and shoot enemy, needs heal, or must avoid danger (grenade/BTR),
        - vanilla path releases control when SAIN combat regroup route becomes valid; SAIN path releases control when combat route is no longer valid,
        - interrupted by being hit, follower death, attention reset (`EPhraseTrigger.Look`), or replacement by a newer command,
        - on successful regroup arrival follower says `EPhraseTrigger.OnPosition`.
- Gesture visibility requirements:
    - `HoldGesture` and `ThereGesture` require follower to see boss gesture target (`head` or `torso` visibility; either is enough).
    - `ComeWithMeGesture` requires both directions:
        - boss can see follower gesture target (`head` or `torso`),
        - selected follower can see boss (`head` or `torso`).
- Command sequencing details:
    - If `ComeCloser` was issued while `HoldPosition` was active:
        - bot approaches,
        - then resumes hold (unless interrupted by a new command/clear event).
    - If `ComeCloser` was not issued from hold:
        - bot performs a short arrival pause/look-around, then clears command.
    - If a new `There` is issued while bot is in arrival look-around:
        - bot immediately starts moving to the new point.
    - `Hold` / `Come` interrupt and replace `There`/arrival-look behavior.
- Contact look pause:
    - On enemy-contact orders (`OnRepeatedContact` / custom `OverThere`), command random look logic is paused for ~`2-4s` so bots keep contact orientation.
- Gesture routing:
    - Custom `OverThere` is handled separately from `There`.
    - A short suppression guard prevents immediate `There` echo from being treated as move-to-point after custom `OverThere`.
- Commands are cleared on:
    - `FollowMe` / `Cooperation`,
    - `Look` (attention),
    - bot being hit,
    - command timeout / invalid execution state.

## 3) Recruit / Group / Boss Model

Main files:

- `client/Components/BotFollowerPlayer.cs`
- `client/Components/AIBossPlayer.cs`
- `client/Patches/BotRecruitPatch.cs`
- `client/Patches/BotGroupRequestPatch.cs`

Implemented:

- Recruit flow through phrase/request patching and follower conversion.
- On conversion:
    - brain/layer handoff reset,
    - conflicting state/request cleanup,
    - move bot into player boss group,
    - enemy/friendly lists adjusted to group context.
- Boss command handling:
    - TeamStatus,
    - OverThere,
    - OnRepeatedContact,
    - `OpenDoor`,
    - `LootGeneric` / `LootWeapon`,
    - Look (Attention),
    - `ComeWithMeGesture`,
    - `HoldGesture`,
    - `ThereGesture`.
- Attention command now clears follower enemy state more aggressively:
    - clears memory goal/last enemy,
    - removes known enemies from bot memory and group cache.

## 4) Active Patch Set (Enabled in `friendlyPlugin.cs`)

Bot/group/follower stability:

- `BotGroupAddEnemyPatch`
- `BotGroupReportEnemyPatch`
- `BotGroupUsecEnemyPatch`
- `BotControllerEnemyPropagationSafetyPatch`
- `BotMemoryDamagePatch`
- `ExUsecBrainHitPatch`
- `BotOwnerIsFolowerPatch`
- `BotOwnerManualUpdatePatch`
- `BotOwnerActivatePatch`
- `SessionLoadBotsEnglishVoicePatch`
- `LootPatrolActiveLayerListPatch`
- `LootPatrolDecisionBypassPatch`
- `AdvAssaultTargetFollowerGuardPatch`
- `AICoreAgentUpdatePatch` (logs/rethrows update exceptions)

Movement:

- `FollowerSprintPatch` (conditional: only when SAIN is absent)
- `FollowerSprintStateDirectionPatch`

Recruit/request:

- `BotReceiverFollowMeRecruitPatch`
- `FollowRequestPatch`
- `HoldRequestPatch`
- `OpenDoorRequestPatch`
- `BotReceiverGestureOverridePatch`

Spawn/raid:

- `BotsControllerPatch`
- `BotsControllerStopPatch`
- `LocalGameCleanupPatch`
- `LocalGameCtorPatch` (patched via `harmony.CreateClassProcessor(...).Patch()`)
- `BotsEventsControllerSpawnPatch`
- `BossSpawnWaveManagerClassPatch`
- `RaidStartPatch`

Items/equipment:

- `UnlootableComponentPatch`
- `ModRaidModdablePatch`
- `ItemSpecificationPanelPatch`

Combat/hearing/talk:

- `BotTalkTrySayPatch`
- `BotTalkSayPatch`
- `GrenadeThrowPatch`
- `GrenadeTryThrowSafetyPatch` (from `GrenadeThrowPatch.cs`)
- `BulletImpactPatch`
- `HearingSensorPatch`
- `FootstepSoundPatch`
- `PlayerSayPatch`

AI data / command UI:

- `AIDataContructPatch`
- `QuickPanelPatch`
    - cooperation entry is shown for any alive, non-follower AI target (still subject to recruit acceptance checks elsewhere)
- `GestureMenuPatch`
- `GestureMenuAvailablePhrasesPatch`
- `EPhraseTriggerPatch`
- `PlayPhraseOrGesturePatch`
    - intercepts only custom phrase IDs and skips interception when the action is a real player gesture (`GClass3937.IsPlayerGesture(actionId)`), so vanilla gestures are not hijacked

SAIN integration:

- `SAINPatch.PatchSAINIfInstalled(harmony)` applies selective SAIN behavior patches when SAIN assembly is present.
- SAIN combat follower integration is implemented in a separate addon DLL:
    - addon project: `addon/friendlySAIN.SAINAddon.csproj`
    - plugin ID: `xyz.pit.friendlysain.sainaddon`
    - runtime path registers custom `SAINFollowerCombatLayer` at priority `71`.
    - this layer replicates SAIN squad-combat decision routing for followers, but re-centers behavior around player boss leadership (instead of vanilla SAIN squad leader ownership).
    - follower action mapping currently routes to:
        - `SAINFollowerCombatRegroupAction`,
        - `SAINFollowerCombatSuppressAction`,
        - `SAINFollowerCombatFollowBossSearchAction`,
        - SAIN solo search/rush action types resolved once from SAIN assembly (`SearchAction` / `RushEnemyAction`) with safe fallback.
- Core plugin validates SAIN/addon presence at runtime:
    - if SAIN is installed but addon is missing, core plugin logs explicit error and SAIN follower combat-layer integration is disabled.
- Shared bridge contract is active for core->addon SAIN readiness handoff:
    - `client/Modules/SainAddonBridge.cs` exposes delegate contract.
    - `addon/SAINAddonPlugin.cs` registers/unregisters bridge callback during addon lifecycle.
    - `addon/SAINFollowerRuntimeBridge.cs` owns SAIN-typed patrol readiness implementation.
- Integration rule for new work:
    - for SAIN-dependent follower behavior, prefer core->addon bridge calls over new core reflection probes.
    - keep strict fail-fast/fail-closed behavior when SAIN is installed but required addon bridge callback is unavailable.
- SAIN layers use their own mover handoff/control path while active (notably in combat):
    - `SAINLayer.OnLayerChanged(...)` stops built-in mover when entering SAIN layer and handles mover/navmesh handoff on layer switch.
    - treat SAIN combat movement issues as SAIN-layer/mover behavior first, then plugin command-layer behavior.
- SAIN addon currently applies follower-focused combat/retention patches from `addon/SAINRegroupBootstrap.cs`:
    - `SAINFollowerFriendlyFirePatch` (blocks boss/follower fireline),
    - `SAINFollowerGroupTalkDirectionPatch` (uses boss look direction for directional enemy talk checks),
    - `SAINCalcGoalPatch` + `SAINEnemyAcquireGatePatch` + `SAINFollowerEnemyRetentionService` (when `SAINAddonToggles.EnableForcedEnemyRetention = true`),
    - `SAINFollowerPersonalityPatch`,
    - `SAINFollowerHitAccuracyPatch`,
    - `SAINFollowerLowLightVisionPatch`.
- Legacy `SAINDecisionRegroupPatch.cs` remains in addon source but is currently not wired by bootstrap.

## 5) Safety/Crash Guards Added

- LootPatrol active-layer guard:
    - `client/Patches/LootPatrolSafetyPatch.cs`
    - `LootPatrolActiveLayerListPatch` strips vanilla LootPatrol (`GClass117`) from BigBrain active layer list for followers before layer update.
    - `LootPatrolDecisionBypassPatch` prevents LootPatrol decision execution when follower state is active.
- Grenade throw safety:
    - `client/Patches/GrenadeThrowPatch.cs` includes null-safe guard for `GClass274.UpdateTryThrow`.
- Player say/hearing null guards:
    - `client/Patches/HearingSensorPatch.cs` hardened against null bot/follower references.
- Enemy propagation guard:
    - `client/Patches/BotGroupPatch.cs` (`BotControllerEnemyPropagationSafetyPatch`)
    - validates `AddEnemyToAllGroupsInBotZone(...)` player refs and skips invalid propagation calls that can occur after debug/out-of-band spawns.
- Interaction/visibility null guards:
    - `client/Modules/InteractableObjects.cs`
    - hardened seen-enemy and boss-state checks against null/missing player/bot references.

## 6) Teleport / Utility

- Teleport key action (`_BotTeleport`) now:
    - computes NavMesh-valid spread spots around player,
    - enforces spacing from player and between followers,
    - avoids overlap pileups with multiple followers.

## 7) Status/Debug Notes

- `PingTeamates` enemy marker/status timing corrected:
    - uses `Time.time - PersonalLastSeenTime` for recency.
- `PingTeamates` callout throttling:
    - directional voice callouts are now throttled to once every `15s` across pings,
    - ping radio/location sound and triangle marker still update every valid ping.
- TeamStatus/Look command burst handling:
    - command handling was debounced to reduce repeated heavy work during rapid player phrase spam.
- SAIN-friendly-fire path:
    - per-shot follower/boss lane check was converted from allocation-heavy physics query path to lightweight geometric checks.
- `PingTeamates` GUI path optimization:
    - per-frame draw loops now use index-based iteration instead of delegate-based `List.ForEach`.
    - bot status text reuses a single `StringBuilder` instance instead of allocating per bot per frame.
    - tracked body-part iteration uses a static array instead of `Enum.GetValues(...)` allocations.
- SAIN bridge debug noise reduction:
    - follower SAIN enemy-bridge debug logs are disabled by default (`EnableSainEnemyBridgeDebugLogs = false`) to reduce runtime string/log overhead.
- Several debug/trace patches were iterated during movement work; current runtime path is focused on minimal active tracing.
- Request command logs were reduced/removed from `AIBossPlayer` (`[Req] Hold/There/ComeWithMe ...`) to keep runtime logs cleaner.
- Debug console command:
    - `fs_spawnfollower`
    - available in-raid, spawns one follower for the player side.
    - profile generation uses game-side bot profile flow (direct `ISession.LoadBots` path via game profile/session objects), then injects into `BotCreationDataClass.CreateWithoutProfile(...)`.
    - bot spawner `InSpawnProcess` is incremented/decremented with failure rollback to avoid breaking later vanilla bot spawns.
    - fallback safe profile request may be used if requested side/role generation fails.
    - treat bugs tracks seperate as SAIN and vanilla categories
    - always spend time checking SAIN and client sources at the begining of the session to get proper context
    - prefer to check client sources first when some method, class, or property is not clear, rather then making assumptions
- English BEAR voice assignment is applied at profile-load time:
    - `SessionLoadBotsEnglishVoicePatch` patches `ProfileEndpointFactoryAbstractClass.LoadBots`
    - each returned `Profile` is processed by `BotOwnerActivatePatch.ApplyEnglishVoiceForProfile(...)`
    - this is the active runtime path for voice replacement (instead of late activation-only mutation)

## 8) Known Open Issues

See:

- `../FOLLOWER-BUGS.md`

Examples currently tracked there:

- SAIN post-combat idle/freeze behavior for followers.
- Enemy propagation consistency across all followers.
- Reaction-system regression (SAIN/vanilla reaction work):
    - after changes made to work around `EnemyController.IsEnemy` timing gaps for early follower reaction, a regression was observed where followers could incorrectly mark the player as enemy.
    - hard guards were added in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `client/Patches/BotGroupPatch.cs` (`BotGroupReportEnemyPatch`) to prevent followers from adding the boss player or other followers as enemies.
    - more testing is still needed; treat this as an active risk area when changing reaction logic (`FollowerAwareness`, hearing/voice/bullet paths).
- Follow-up behavior when player is hit out of combat.
- Follower death reaction:
    - when a follower dies, nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`;
    - if nobody saw death, corpse-position visibility is checked for up to `~60s` and reaction can still trigger.

## 9) Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- Follow layer/action logic:
    - `client/BigBrain/FollowerPatrolLayer.cs`
    - `client/BigBrain/Actions/FollowAction.cs`
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs`
    - `client/Components/BotFollowerPlayer.cs`
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs`
- SAIN combat addon (follower combat layer):
    - `addon/SAINFollowerCombatLayer.cs`
    - `addon/SAINFollowerSquadDecisionCalculator.cs`
    - `addon/SAINFollowerCombatRegroupAction.cs`
    - `addon/SAINFollowerCombatSuppressAction.cs`
    - `addon/SAINFollowerCombatFollowBossSearchAction.cs`

## 10) Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## 11) SAIN Vision/Enemy Notes (2026-03-01)

- Detailed investigation note is recorded in:
    - `docs/sain-vision-enemy-pipeline-2026-03-01.md`
- Key result:
    - SAIN enemy retention depends on internal `EnemyKnown` + `LastKnownPosition` + active checks.
    - SAIN can clear current goal enemy quickly if any of those conditions drop, even after short visual contact.
    - SAIN also patches vanilla `EnemyInfo.HaveSeen/ShallKnowEnemy*` and `LookSensor` flow, so behavior can diverge sharply from vanilla pickup logic.

Update (2026-03-06):

- Enemy-contact reliability in SAIN is now enforced with a follower-only retention bridge:
    - `addon/SAINEnemyAcquireGatePatch.cs` gates `SAINEnemyController.CheckAddEnemy` for followers.
    - `addon/SAINFollowerEnemyRetentionService.cs` now hooks `BotsGroup.CalcGoalForBot` (via `SAINCalcGoalPatch`) and performs guarded forward-scan enemy acquisition when followers have no current enemy.
    - calc-goal scans are rate-limited per follower and scaled by active follower count.
    - enemy candidates are filtered to avoid boss/followers/friendly bot types and side-safe cases unless hostile intent is detected.
    - when a follower commits an enemy through this path, the service propagates that enemy to sibling followers.
    - Attention/Look suppression is honored through `client/Modules/FollowerEnemyEnforceSuppression.cs` and the retention service (`blocked_attention_suppression` path).
    - Forced retention remains toggle-controlled via `addon/SAINAddonToggles.cs` (`EnableForcedEnemyRetention`).
- Follower proficiency was increased (hard+ oriented) through SAIN addon patches:
    - `addon/SAINFollowerPersonalityPatch.cs` raises follower detection/reaction and tightens aim behavior (higher `GainSightCoef`, higher hearing/visible multipliers, faster precision, lower accuracy/scatter multipliers).
    - `addon/SAINFollowerHitAccuracyPatch.cs` bypasses SAIN `AimHitEffectClass.GetHit` aim-affection for followers so incoming hits do not degrade follower aim.
    - `addon/SAINFollowerLowLightVisionPatch.cs` reduces low-light time-to-spot penalty for followers by post-processing SAIN time vision modifier in `EnemyGainSightClass.CalcTimeModifier`.

## BUGS are tracked in : F:\Projects\SPT-Tarkov\FOLLOWER-BUGS.md
