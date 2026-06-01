# Command System Notes

Last updated: 2026-05-04

## Scope

This document summarizes boss-issued follower commands as implemented in the client runtime.

Authoritative files:

- `client/Components/AIBossPlayer.cs` - boss-side phrase/gesture router and command producers.
- `client/Components/BotFollowerPlayer.cs` - per-follower command state.
- `client/BigBrain/FollowerRequestLayer.cs` - out-of-combat command layer gate.
- `client/BigBrain/Actions/GestureCommandAction.cs` - out-of-combat command execution.
- `client/BigBrain/FollowerCombatLogicBase.cs` - core combat objective/command handoff.
- `client/BigBrain/FollowerCombatDefault.cs` - rifleman/default combat handling.
- `client/BigBrain/FollowerCombatSniper.cs` - marksman combat handling.
- `client/BigBrain/FollowerCombatRegroupObjective.cs` - core combat regroup.
- `client/BigBrain/FollowerCombatSuppressionObjective.cs` - core ordered suppression.
- `client/BigBrain/FollowerCombatNeedSniperObjective.cs` - core marksman support order.
- `addon/SAINFollowerCombatLayer.cs` - optional SAIN addon regroup/hold-override handling.
- `client/Patches/BotReceiverPhraseOverridePatch.cs` and `client/Patches/BotReceiverGestureOverridePatch.cs` - vanilla receiver suppression for mod-owned commands.
- `client/Patches/GestureMenuPatch.cs` - command menu injection/localization/filtering.

## Runtime Ownership

Command input starts in `pitAIBossPlayer.PhraseSaid(...)` or `pitAIBossPlayer.GestusShown(...)`.

Most durable commands are stored on `BotFollowerPlayer` as a single active `FollowerCommandType`. This state is intentionally simple: one command, one optional target, one timeout. New timed commands usually replace any previous active command unless they are the same command type.

There are three execution paths:

1. **Request layer / out of combat**
   - `FollowerRequestLayer` activates only when the follower has an active command and is ready for patrol after combat.
   - It rejects combat-only orders such as `PushEnemy`, `SuppressEnemy`, and `NeedSniper`.
   - It clears most request-layer commands when the follower acquires a known enemy.
   - `GestureCommandAction` executes the actual hold, move, regroup, loot, and door behavior.

2. **Core combat**
   - `FollowerCombatLogicBase` owns objective selection and command handoff.
   - `RegroupNearBoss`, `SuppressEnemy`, and `NeedSniper` are consumed into combat objectives.
   - `PushEnemy` is handled inside the active tactic's primary objective.
   - Combat gesture commands (`CombatComeToBossCover`, `CombatMoveToPointTactical`) break hold commitments, but are dropped if the follower is already moving.

3. **SAIN addon combat**
   - Only active when SAIN plugin and the pitFireTeam SAIN addon are both present.
   - `RegroupNearBoss` is seen by `SAINFollowerCombatLayer` and translated into SAIN `ESquadDecision.Regroup`.
   - Temporary `HoldPosition` combat aggression override is also treated as regroup/protection intent by the SAIN addon.

## Command State

`FollowerCommandType` currently contains:

| Command | Stored target | Timeout behavior | Primary consumer |
| --- | --- | --- | --- |
| `HoldPosition` | none | infinite | `GestureCommandAction` |
| `MoveToPoint` | sampled world/nav point | infinite | `GestureCommandAction` |
| `ComeCloser` | boss position snapshot owned by action | timed unless resuming a hold | `GestureCommandAction` |
| `RegroupNearBoss` | none | timed | `GestureCommandAction`, core combat regroup, or SAIN addon |
| `TakeLootItem` | reserved current loot item, not `_commandTarget` | timed | `GestureCommandAction` |
| `OpenDoor` | reserved current door, not `_commandTarget` | timed | `GestureCommandAction` |
| `PushEnemy` | current combat enemy | timed | core combat default/marksman routing |
| `SuppressEnemy` | current combat enemy | timed | core rifleman suppression objective |
| `NeedSniper` | current/support enemy | timed | core marksman support objective |
| `CombatComeToBossCover` | none | timed | core combat gesture handoff |
| `CombatMoveToPointTactical` | sampled world/nav point | timed | core combat gesture handoff |

`TryGetActiveCommand(...)` is not a pure read. It clears the current command when:

- the follower is actively using first aid or surgery
- the current BigBrain decision is `heal`
- the command timeout has expired

`TryPeekActiveCommand(...)` is a pure peek and does not clear timed-out or healing-interrupted state.

## Phrases And Gestures

### Team Status

Input:

- custom phrase `CustomPhrases.TeamStatus`

Behavior:

- Debounced in `AIBossPlayer`.
- Calls `PingTeamates.Instance.Ping(this)`.
- Nearby active followers without enemies play `FriendlyGesture`.
- Does not create `FollowerCommandType` state.

### Contact / Over There

Input:

- `EPhraseTrigger.OnRepeatedContact`
- custom gesture `CustomGestures.OverThere`

Behavior:

- Calls `ProcessContactCommand(...)`.
- Builds candidate enemies from interactable seen-enemy cache, boss visible enemies, SAIN contact fallback, and directed visible candidates.
- Registers valid enemies into follower memory and can promote a prioritized enemy as `GoalEnemy`.
- Applies a short look override toward the boss's look direction for followers without their own visible goal.
- Custom `OverThere` also forwards an `OnRepeatedContact` phrase event to visible followers so they can play normal voice/receiver feedback.
- This is a combat cue, not a `FollowerCommandType`.
- Contact injection clears most active request-layer commands after the follower now has an enemy, except `PushEnemy` and `SuppressEnemy`.

### Directional Look

Input:

- `EPhraseTrigger.InTheFront`
- `EPhraseTrigger.LeftFlank`
- `EPhraseTrigger.RightFlank`
- `EPhraseTrigger.OnSix`

Behavior:

- Applies a short command look override to active followers within phrase range.
- Direction is relative to the boss planar look direction.
- Does not create `FollowerCommandType` state.

### Attention / Look

Input:

- `EPhraseTrigger.Look`

Behavior:

- Debounced in `AIBossPlayer`.
- Clears active command state and temporary combat aggression override.
- Temporarily suppresses enemy enforcement.
- Clears current goal enemy and known enemy memory.
- Soft-resets follower recovery and forces upright pose.
- If SAIN addon combat is active, asks the addon bridge to force-release follower combat state.
- Followers answer `Roger`.
- Does not create new command state.

### Follow Me / Cooperation

Input:

- `EPhraseTrigger.FollowMe`
- `EPhraseTrigger.Cooperation`

Behavior:

- Calls `ClearFollowerCommands()`.
- Clears active `FollowerCommandType` state on all active followers.
- Disables patrol-radius mode by setting `CanPatrol` false.
- Does not otherwise change combat objective state directly.

### On Your Own

Input:

- `EPhraseTrigger.OnYourOwn`

State:

- Does not create `FollowerCommandType` state.
- Sets `BotFollowerPlayer.CanPatrol` true for selected out-of-combat followers.

Targeting:

- Broadcast to all active followers every time.
- Followers with active enemy state reject the command with `DontKnow` / `NoGesture`; combat behavior is unchanged.

Behavior:

- Clears current request command state and temporary combat aggression override.
- Enables patrol-radius mode in `FollowAction`.
- `FollowMe` / `Cooperation` clears this mode.
- This is out-of-combat only; core combat and SAIN addon combat do not consume it.

### Cover Me

Input:

- `EPhraseTrigger.CoverMe`

Behavior:

- This is intentionally not a protection/tactic command right now.
- Broadcast to all active followers.
- Disables patrol-radius mode by setting `CanPatrol` false.
- Does not clear active request command state.
- Does not set boss protection, combat aggression, regroup, or combat objective state.

Execution:

- `FollowAction` checks `followerData.CanPatrol` every update.
- When disabled, the action uses normal close follow/settle behavior.
- When enabled, the action uses the patrol camp logic inherited from the old plugin:
  - first ensure the follower is close enough to the boss
  - treat the boss as stationary only while the boss remains inside the same small movement grid
  - define a larger camp sector around the boss
  - choose random reachable nav points inside the configured `patrolRadius`
  - avoid points too close to the boss or other followers
  - walk slowly between patrol points and pause 6-10 seconds at each point
  - run peaceful look/actions while waiting when available
  - if the boss exits the camp sector, temporarily follow the boss, then initialize a new patrol camp

## Out-Of-Combat Commands

### Hold Gesture

Input:

- `EInteraction.HoldGesture`

Command state:

- `SetHoldPosition(float.PositiveInfinity, crouch: true)`

Targeting:

- If boss is looking at a valid follower within hold distance, only that follower is commanded.
- Otherwise broadcasts to nearby visible active followers without enemies.

Execution:

- `GestureCommandAction.HandleHoldPosition()`
- Stops movement.
- Forces crouch when configured by command state.
- Applies command look override if present; otherwise random look-around.
- Persistent until replaced, cleared, or interrupted by command management.

### Stop Phrase

Input:

- `EPhraseTrigger.Stop`

Command state:

- `SetHoldPosition(float.PositiveInfinity, crouch: false)`

Targeting:

- Similar focused-then-broadcast routing to hold gesture, but phrase range is larger and uses phrase reaction checks.

Execution:

- Same `HoldPosition` request-layer action as hold gesture.
- Does not force crouch.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers, so pitFireTeam owns the command.

### Come With Me Gesture

Input:

- `EInteraction.ComeWithMeGesture`

Out-of-combat command state:

- `SetComeCloser(10f)`

Targeting:

- Focused only. Boss must be looking at one valid follower within max distance and gesture visibility gates.

Execution:

- `GestureCommandAction.HandleComeCloser()`
- Snapshots boss position and boss pose at command start.
- Moves to within about 1.5m of the snapshotted boss position.
- If issued while the follower was in `HoldPosition`, `CompleteComeCloser()` restores `HoldPosition` after arrival.
- Otherwise it clears the command after a short arrival pause.

Combat variant:

- If the selected follower has an active combat enemy, the gesture stores `CombatComeToBossCover` instead of `ComeCloser`.

### There Gesture

Input:

- `EInteraction.ThereGesture`

Out-of-combat command state:

- `SetMoveToPoint(commandTarget, 0f)`

Targeting:

- Chooses the closest active follower within gesture command distance that can react to the boss gesture.
- Samples the boss interaction ray or planar look direction to a nav point.
- Uses `pitFireTeam.goToDistance` for normal out-of-combat target range.

Execution:

- `GestureCommandAction.HandleMoveToPoint(target)`
- Walks to the target.
- Validates path periodically.
- On arrival, stops and performs a short look-around before clearing the command.
- If a command look override exists, it looks at that override instead of random scanning.

Combat variant:

- If the selected follower has an active combat enemy, the gesture stores `CombatMoveToPointTactical`.
- Combat target range is hard-limited to 30m from the boss and does not use `goToDistance`.

### Go Forward Phrase Outside Combat

Input:

- `EPhraseTrigger.GoForward`

Out-of-combat command state:

- Falls back to `SetMoveToPoint(commandTarget, 0f)` when the follower does not have an active combat enemy.

Targeting:

- Optional focused follower if boss is looking at one within phrase range.
- Otherwise iterates active followers.
- Uses the same point sampling as normal `There`.

Combat variant:

- Becomes `PushEnemy` when the follower has active combat enemy state.

### Regroup Phrase Outside Combat

Input:

- `EPhraseTrigger.Regroup`

Command state:

- `SetRegroup(20f)`
- Disables patrol-radius mode by setting `CanPatrol` false.

Targeting:

- Broadcast to active followers.
- Ignored for followers already close enough to boss on the same level, or healing.
- Patrol-radius mode is disabled before this ignore check, so `Regroup` still returns followers to normal follow mode even when no regroup movement command is created.

Execution:

- `GestureCommandAction.HandleRegroupNearBoss()`
- Picks a boss-near regroup target, preferring cover points near boss and falling back to spread destinations.
- Reserves regroup destinations through `CombatEvents` to reduce crowding.
- Runs when far enough, otherwise walks.
- Completes when within close nav distance and same-level tolerance.

Combat variant:

- Core combat consumes `RegroupNearBoss` into `FollowerCombatRegroupObjective`.
- SAIN addon can consume it as `ESquadDecision.Regroup` when SAIN route is enabled.

### Loot Phrases

Input:

- `EPhraseTrigger.LootGeneric`
- `EPhraseTrigger.LootWeapon`

Command state:

- `SetTakeLootItem(35f)`

Targeting:

- Requires `InteractableObjects.GetCurLootItem()`.
- Chooses closest active follower to the loot item.
- Ignores followers with enemies.
- Reserves taker ownership through `InteractableObjects.SetTaker(...)`.

Execution:

- `GestureCommandAction.HandleTakeLootItem()`
- Moves to loot.
- Checks inventory space and executes pickup transaction.
- Stores item through `InteractableObjects.StoreItem(...)` for squadmates.
- Clears command on success/failure/invalid state.

### Open Door Phrase

Input:

- `EPhraseTrigger.OpenDoor`

Command state:

- `SetOpenDoor(12f)`

Targeting:

- Requires `InteractableObjects.GetCurDoor()`.
- Locked doors produce a nearby `Negative` response and do not create a command.
- Chooses closest active follower to the door.
- Ignores followers with enemies.
- Reserves opener ownership through `InteractableObjects.SetOpener(...)`.

Execution:

- `GestureCommandAction.HandleOpenDoor()`
- Samples nav point near door, moves there, and calls `BotOwner.DoorOpener.Interact(...)`.
- Clears command when interaction ends, door is already open, path is invalid, timeout hits, or target disappears.

## Core Combat Commands

### Hold Position Phrase In Combat

Input:

- `EPhraseTrigger.HoldPosition`

State:

- Does not create `FollowerCommandType.HoldPosition`.
- Applies `BotFollowerPlayer.SetTemporaryCombatAggressionOverride(0f)`.

Behavior:

- Core combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`.
- Rifleman/default behavior becomes less proactive and more defensive/regroup-oriented.
- Marksman suppresses proactive close-search/auto-search behavior.
- Defensive survival behavior still wins: immediate fire, dogfight, healing, boss protection, and other urgent actions can still run.

SAIN addon:

- `SAINFollowerCombatLayer` treats the temporary override as regroup/protection intent.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers.

### Go Go Go Phrase In Combat

Input:

- `EPhraseTrigger.Gogogo`

State:

- Clears temporary combat aggression override.

Behavior:

- Returns followers to their saved combat aggression/tactic behavior.
- Does not create `FollowerCommandType` state.

Vanilla handling:

- Suppressed by `BotReceiverPhraseOverridePatch` for player-boss followers.

### Push Enemy

Input:

- `EPhraseTrigger.GoForward` while follower has an active combat enemy.

Command state:

- `SetPushEnemy(12f)`

Core behavior:

- `FollowerRequestLayer` refuses to consume it.
- Core combat primary objective handles it.
- Rifleman/default ordered push tries committed firing-position movement first, then falls back to `FollowerCombatPush.EngageEnemy(true, ...)`.
- Push movement is committed as `push.*` and keeps enemy retention refreshed.
- Regroup/suppression/need-sniper objectives return to primary objective when a push order appears.

Marksman behavior:

- Generic push is not a direct marksman assault.
- Marksman support logic may clear unsupported push/suppress commands or turn the situation into support/reposition behavior.

### Regroup In Combat

Input:

- `EPhraseTrigger.Regroup` while combat regroup context exists.

Command state:

- `SetRegroup(20f)`

Core behavior:

- `FollowerCombatLogicBase` consumes it into `FollowerCombatRegroupObjective`.
- Objective owns movement until complete or replaced.
- Hot contact uses `attackMoving` toward bossward cover or fallback boss destination.
- Cooled contact uses `goToPoint` through `CombatRegroupRunAction`.
- Completion is based on boss nav distance and same-level tolerance.
- Push or suppress orders can end regroup and return to primary/suppression behavior.

SAIN addon:

- `SAINFollowerCombatLayer.TryHandleRegroupCommand(...)` latches the command briefly and returns `ESquadDecision.Regroup`.

### Suppress Enemy

Input:

- `EPhraseTrigger.Suppress`

Command state:

- `SetSuppressEnemy(6f)`

Targeting:

- Optional focused follower if boss is looking at one; otherwise nearest valid Rifleman/default follower to the boss wins.
- Rifleman/default only; marksman followers are skipped.
- Requires a suppress-capable current weapon or a usable second-primary grenade launcher.
- Ensures a target by using the follower's current enemy, boss-visible enemies, or boss order-ray launcher targets.
- One additional Rifleman/default can also receive the order when it has a usable second-primary grenade launcher and is within `80m` of a hostile target on the boss order ray. This secondary order is launcher-only and does not fall back to rifle suppression.

Core behavior:

- `FollowerPmcCombatLogic` marks `SuppressEnemy` consumable.
- `FollowerCombatLogicBase` validates weapon/enemy and activates `FollowerCombatSuppressionObjective`.
- The objective tries dogfight/heal first, then ordered suppress-fire setup.
- Suppression can use obstructed known-point suppression when explicitly ordered, subject to shot safety.
- Command is cleared on consume, rejection, completion, missing enemy/target, blocked lane, or weapon rejection.

### Need Sniper

Input:

- `EPhraseTrigger.NeedSniper`

Command state:

- `SetNeedSniper(10f)`

Targeting:

- Boss first seeds contact through `ProcessContactCommand(...)`.
- Only marksman followers receive the command.
- Rejects with `Negative` when marksman is busy with own immediate fight or needs/heals medical work.
- Clears temporary combat aggression override when accepted.

Core behavior:

- `FollowerSniperCombatLogic` marks `NeedSniper` consumable.
- `FollowerCombatLogicBase` rejects if healing, under fire, recently hit, or point-blank visible shootable threat.
- Accepted orders activate `FollowerCombatNeedSniperObjective`.
- Objective tries immediate shoot, current cover fire, support firing cover, or firing-position movement.
- Arrival arms a short `sniper.NeedSniper.positionHold`.
- Completes/rejects when enemy disappears, no lane exists after retry, direct shot is available, or stronger survival interrupts.

### Need Help

Input:

- `EPhraseTrigger.NeedHelp`

State:

- Does not create `FollowerCommandType`.

Behavior:

- Finds closest valid enemy from boss-tracked enemies, boss group enemies, boss visible contact enemies, and SAIN contact fallback.
- Marks boss logic as manually under attack by that enemy.
- Calls `PrioritizeEnemy(...)` for each active follower.
- Core combat reacts through existing boss-under-attack protection/support routing.

### Combat Come With Me

Input:

- `EInteraction.ComeWithMeGesture` while selected follower has active combat enemy.

Command state:

- `SetCombatComeToBossCover(8f)`

Core behavior:

- This command is only accepted from hold/settle states.
- Hold end paths break for the command:
  - committed arrival holds
  - default cover holds
  - default committed holders
  - marksman holds
  - base combat hold
- If the follower is already in any movement decision, the command is cleared and ignored.
- On consume, `FollowerCombatCommon.TryCreateBossCoverAttackMovingDecision(...)` finds boss-local cover using `CombatDistanceConfiguration.GetBossCoverSearchRadius()`.
- The decision is forced to `BotLogicDecision.attackMoving` because the action expects a cover point.
- If no valid boss-local cover exists, the follower says `Negative` and plays `NoGesture`.

### Combat There

Input:

- `EInteraction.ThereGesture` while selected follower has active combat enemy.

Command state:

- `SetCombatMoveToPointTactical(commandTarget, 8f)`

Targeting:

- Closest active visible follower within gesture command distance.
- Command point is sampled from boss ray/look direction.
- Hard-limited to 30m from boss; it does not use `goToDistance`.

Core behavior:

- Same hold/settle and movement-ignore rules as combat `ComeWithMe`.
- On consume, `FollowerCombatCommon.TryCreateBossCommandTacticalPointDecision(...)` sets `GoToSomePointData` and returns `BotLogicDecision.goToPointTactical`.
- Invalid target produces `Negative` and `NoGesture`.

## Receiver Patches And Vanilla Forwarding

Mod-owned phrases suppressed from vanilla follower receiver handling:

- `Stop`
- `HoldPosition`
- `Gogogo`
- `Suppress`
- `NeedSniper`
- `NeedHelp`
- `OnYourOwn`
- `CoverMe`

Mod-owned gestures suppressed from vanilla follower receiver handling:

- `ComeWithMeGesture`
- `HoldGesture`
- `ThereGesture`
- `CustomGestures.OverThere`

Unhandled phrases and gestures are still forwarded to follower receivers from `AIBossPlayer`, so vanilla behavior can continue for commands pitFireTeam does not own.

## Menu Notes

`GestureMenuPatch` injects/modifies menu entries and labels for:

- custom `TeamStatus` phrase
- custom `OverThere` gesture
- `OnRepeatedContact` display text
- optional `hideUnsupportedCommands` filtering

The menu is not authoritative for command behavior. It only controls what the player can see/select and how labels are localized.

## Cleanup Rules

Common command cleanup cases:

- `TryGetActiveCommand(...)` clears command while healing or after timeout.
- `ContactEnemy:RegisterContactEnemyForFollower` clears most request commands when combat enemy state appears.
- `FollowerRequestLayer` clears most known-enemy request commands before combat takes over.
- `GestureCommandAction` clears movement commands on arrival, invalid path, invalid target, danger, healing, grenade/BTR avoidance, and interaction failure.
- Core combat objectives clear command state when consuming or rejecting objective commands.
- Combat gesture orders are cleared when consumed, invalid, or issued while the follower is already moving.
