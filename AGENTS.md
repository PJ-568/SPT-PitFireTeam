# friendlySAIN: Current Implementation Summary

Last updated: 2026-02-16  
Scope: runtime behavior currently present in `friendlySAIN/client` (based on active code paths in `friendlyPlugin.cs` and related components).

## BE / Server State (Important)

- Backend (BE) support for this plugin is **not implemented yet**.
- Do **not** add new runtime paths that depend on **plugin-owned/custom** BE profile generation/fetch endpoints for debug/runtime bot spawn flows.
- Using existing **game-side** profile loading flow (`ISession.LoadBots`) is allowed.
- If a spawn flow requires BE profile data and no local/prefetched profile exists, it should fail fast with a clear reason instead of attempting BE fallback.

## 0) Project Context

- Old plugin codebase: `F:/Projects/SPT-Tarkov/friendlypmc`
- Old client reference (3.11): `F:/Projects/SPT-Tarkov/Client-Decompiled-3.11`
- New client reference (4.x): `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x`
- SAIN plugin reference: `F:/Projects/SPT-Tarkov/SAIN-master/SAIN`
- Positioning:
    - `friendlySAIN` is both:
        - a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment,
        - and an alternative plugin implementation with new BigBrain-native follower layers/actions.

## 1) Core Runtime Model

- Plugin: `xyz.pit.friendlysain` (`client/friendlyPlugin.cs`)
- Dependency: BigBrain (`xyz.drakia.bigbrain`)
- Optional integration: SAIN (`me.sol.sain`) detected at runtime.
- Follower control model:
    - Combat remains vanilla/SAIN-owned.
    - Friendly follow logic is implemented as a BigBrain custom layer/action (`FollowerPatrolLayer` + `FollowAction`).

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

- Registers `friendlySAIN.FollowerRequest` custom layer (priority `77`) above patrol (`75`).
- `FollowerRequestLayer` activates when follower has an active command in `BotFollowerPlayer`.
- `GestureCommandAction` handles:
    - `HoldPosition`: stop, crouch pose, periodic random look-around, no command timeout (persists until replaced/cleared).
    - `ComeCloser`: move to boss until close (about `1m`).
    - `MoveToPoint` (`There`): move to projected/navmesh-validated target point (walk-only), then brief look-around on arrival.
    - `Regroup` (`EPhraseTrigger.Regroup`):
        - in combat context: followers regroup near boss (run/sprint converge),
        - out of combat: command is treated as a clear/reset (`ClearFollowerCommands`), matching current follow-me style usage.
    - Regroup ignore/interruption safeguards:
        - ignored when follower is healing, has visible enemy, or is already close enough (`~8m` nav-path distance on same level),
        - interrupted by combat transition, being hit, follower death, attention reset (`EPhraseTrigger.Look`), or replacement by a newer command,
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
- `BotGroupUsecEnemyPatch`
- `BotControllerEnemyPropagationSafetyPatch`
- `BotMemoryDamagePatch`
- `ExUsecBrainHitPatch`
- `BotOwnerIsFolowerPatch`
- `BotOwnerManualUpdatePatch`
- `BotOwnerActivatePatch`
- `LootPatrolFollowerGuardPatch`
- `AICoreAgentUpdatePatch` (logs/rethrows update exceptions)

Movement:

- `FollowerSprintPatch` (conditional: only when SAIN is absent)
- `FollowerSprintStateDirectionPatch`

Recruit/request:

- `BotReceiverFollowMeRecruitPatch`
- `FollowRequestPatch`
- `HoldRequestPatch`
- `BotReceiverGestureOverridePatch`

Spawn/raid:

- `BotsControllerPatch`
- `BotsControllerStopPatch`
- `LocalGameCleanupPatch`
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
- SAIN layers use their own mover handoff/control path while active (notably in combat):
    - `SAINLayer.OnLayerChanged(...)` stops built-in mover when entering SAIN layer and handles mover/navmesh handoff on layer switch.
    - treat SAIN combat movement issues as SAIN-layer/mover behavior first, then plugin command-layer behavior.

## 5) Safety/Crash Guards Added

- LootPatrol active-layer guard:
    - `client/Patches/LootPatrolSafetyPatch.cs`
    - strips vanilla LootPatrol (`GClass117`) from BigBrain active layer list for followers before layer update, preventing `GClass117.GetDecision` null refs.
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
- Several debug/trace patches were iterated during movement work; current runtime path is focused on minimal active tracing.
- Debug console command:
    - `fs_spawnfollower`
    - available in-raid, spawns one follower for the player side.
    - profile generation uses game-side bot profile flow (direct `ISession.LoadBots` path via game profile/session objects), then injects into `BotCreationDataClass.CreateWithoutProfile(...)`.
    - bot spawner `InSpawnProcess` is incremented/decremented with failure rollback to avoid breaking later vanilla bot spawns.
    - fallback safe profile request may be used if requested side/role generation fails.
    - treat bugs tracks seperate as SAIN and vanilla categories
    - always spend time checking SAIN and client sources at the begining of the session to get proper context
    - prefer to check client sources first when some method, class, or property is not clear, rather then making assumptions

## 8) Known Open Issues

See:

- `../FOLLOWER-BUGS.md`

Examples currently tracked there:

- SAIN post-combat idle/freeze behavior for followers.
- Enemy propagation consistency across all followers.
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
