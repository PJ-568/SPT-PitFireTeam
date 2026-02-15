# friendlySAIN: Current Implementation Summary

Last updated: 2026-02-15  
Scope: runtime behavior currently present in `friendlySAIN/client` (based on active code paths in `friendlyPlugin.cs` and related components).

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
  - `HoldPosition`: stop, crouch pose, periodic random look-around.
  - `ComeCloser`: move to boss until close (about `1m`).
  - `MoveToPoint` (`There`): move to projected/navmesh-validated target point.
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
- `client/Patches/BotReceiverPatch.cs`

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
- `GestureMenuPatch`
- `GestureMenuAvailablePhrasesPatch`
- `EPhraseTriggerPatch`
- `PlayPhraseOrGesturePatch`

SAIN integration:
- `SAINPatch.PatchSAINIfInstalled(harmony)` applies selective SAIN behavior patches when SAIN assembly is present.

## 5) Safety/Crash Guards Added

- LootPatrol follower guard:
  - `client/Patches/LootPatrolSafetyPatch.cs`
  - prevents follower bots from executing vanilla `GClass117.GetDecision` path that was throwing null refs.
- Grenade throw safety:
  - `client/Patches/GrenadeThrowPatch.cs` includes null-safe guard for `GClass274.UpdateTryThrow`.
- Player say/hearing null guards:
  - `client/Patches/HearingSensorPatch.cs` hardened against null bot/follower references.

## 6) Teleport / Utility

- Teleport key action (`_BotTeleport`) now:
  - computes NavMesh-valid spread spots around player,
  - enforces spacing from player and between followers,
  - avoids overlap pileups with multiple followers.

## 7) Status/Debug Notes

- `PingTeamates` enemy marker/status timing corrected:
  - uses `Time.time - PersonalLastSeenTime` for recency.
- Several debug/trace patches were iterated during movement work; current runtime path is focused on minimal active tracing.

## 8) Known Open Issues

See:
- `../FOLLOWER-BUGS.md`
- `../AI-PROGRESS.md`

Examples currently tracked there:
- SAIN post-combat idle/freeze behavior for followers.
- Enemy propagation consistency across all followers.
- Follow-up behavior when player is hit out of combat.

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
