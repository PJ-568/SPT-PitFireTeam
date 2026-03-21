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

Do not leave left overs. When going with a different approach, clean up (or revert) the old approach

## Decision Priority

When multiple approaches are possible, prefer:

1. runtime stability
2. preserving existing architecture
3. minimal code changes
4. improved clarity or debugging
5. improved elegance

---

# friendlySAIN: Current Implementation Summary

**Last updated:** 2026-03-21  
**Scope:** Runtime behavior across `friendlySAIN/client`, `friendlySAIN/addon`, and `friendlySAIN/server`.

## Project Overview

**friendlySAIN** is a three-tier modular architecture:

1. **CLIENT** (`client/`) — Game-side follower control and team UI.
    - Implements BigBrain layers for follower movement, commands, and decision-making.
    - Patches game systems (bot recruitment, group handling, loot/door interaction).
    - Manages UI for team management and teammate creation.

2. **SERVER** (`server/`) — Backend teammate management and social integration.
    - REST API for teammate CRUD operations.
    - Social list/profile patching to merge teammates with stock friends.
    - Group invite and raid-spawning routes.
    - Post-raid item/escape handling.

3. **SAIN ADDON** (`addon/`) — SAIN-specific combat layer and retention system.
    - Custom follower combat layer replacing SAIN squad layer.
    - Decision routing for suppression, search, help, and regroup actions.
    - Enemy retention gating and forced acquisition assistance.
    - Follower-specific aim/vision/hearing tuning patches.

---

## Architecture Key Constraint

- Server backend is **limited to teammate profile creation/storage/social flows** and is **not** a general bot profile generator.
- For debug/runtime follower spawn: use existing game-side `ISession.LoadBots` profile loading (not BE-dependent).
- If a spawn flow needs BE profile data and local profile is unavailable, fail fast with a clear reason.

---

# CLIENT SIDE: Follower AI & Team Management

**Plugin ID:** `xyz.pit.friendlysain`  
**Main entry:** `client/friendlyPlugin.cs`

## 0a) Teammate System Status (In Progress)

Current custom teammate feature state:

- Friends list has a localized `Add Teammate` entry injected into the social UI.
- Pressing it opens a custom teammate creation flow built on the stock appearance screen:
    - name entry,
    - player-side forced automatically,
    - head and voice selection,
    - localized validation/prompt text overrides,
    - custom back/submit handling.
- On submit, the client posts `{ nickname, voice, head }` to the server endpoint `/singleplayer/friendlysain/teammate/create`.
- Server generates a PMC bot of the player side, overwrites name/voice/head, and saves it as mod-owned JSON under:
    - `user/mods/friendlySAIN-ServerMod/Resources/teammates/<sessionId>/<aid>.json`
- Stock social flows are now bridged for teammates:
    - teammates are merged into `/client/friend/list`
    - teammate profile view is merged into `/client/profile/view`
    - teammate deletion is bridged through `/client/friend/delete`
- Social/update details:
    - add-success path refreshes the social list so newly created teammates appear without reopening the game
    - teammate ids now use stock server-side `HashUtil.GenerateAccountId()` collision-checked allocation, not a custom max-id allocator
    - 4.x invite popup is patched separately because stock `Commando` and `SPT` chat bots share the same `Aid` and break popup row-keying
- Team grouping flow is partially active:
    - teammate appears in right-click invite/group flows
    - teammate can accept group invite
    - pre-raid ready screen and loading screen can show player + teammate
    - local/offline raid guard is now enforced late in `TarkovApplication` and `MainMenuController.method_52()`
    - teammate path must preserve the normal PMC insurance screen before the custom ready screen
- Dedicated Team Management FE is now active:
    - main menu now has a localized `My Squad` entry
    - the Team screen currently has `Roaster` and `Settings` tabs plus stock-style back navigation
    - roster tab currently supports add/remove teammate flows, teammate portrait tiles, teammate profile open/return, and scrolling roster layout for larger squads
    - settings tab now exposes the main friendlySAIN config set in a stock-style scrollable UI using EFT toggle/slider controls for checkbox and ranged settings
    - settings entries are grouped/reordered for the current squad-management UX and the duplicated BepInEx ConfigurationManager view is hidden for those settings
    - old friends-list `Add Teammate` entry has been removed in favor of the Team screen entry point
- Server teammate routes now also include legacy follower spawn/settings compatibility used by the current client:
    - `/client/game/bot/followergenerate`
    - `/client/game/bot/followerdetails`
- Teammate profile view customization is in progress:
    - hideout/report are hidden,
    - stock clothes dropdowns are reused,
    - custom loadout dropdown is injected below,
    - clothes/loadout persistence routes exist on the server,
    - teammate rename from profile view is implemented through a custom overlay + backend rename route,
    - UI layout tuning is still active.
- Current backend/social/profile/runtime limitations:
    - tactic persistence/UI is not implemented yet (`followerdetails` currently returns `Default`)
    - voice/head customization from profile screen is not implemented yet
    - Team screen `Settings` tab is functionally in place for checkbox/ranged friendlySAIN settings; keybind/input-option parity is still pending if needed later
    - teammate invite/group flow still needs more parity with old plugin around pre-raid screen sequencing and group state handling
    - broader team-management/chatbot behaviors from old `friendlyPMC` are not ported
    - teammate profiles remain mod-owned bot JSON, not full stock `SptProfile` accounts

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

## 0b) Follower Core Components

**State & Management:** (`client/Components/`)

- `BotFollowerPlayer.cs` — Per-follower state container (active command, healing status, lifecycle)
- `AIBossPlayer.cs` — Boss command handler (TeamStatus, gestures, loot/door requests, attention)
- `BossFollowerPlayer.cs` — Boss-side follower roster management
- `FollowerCombatManager.cs` — Combat state coordination across followers
- `SquadControlMenuUi.cs` — Team Management UI (My Squad roster/settings screens)

**Follower Lifecycle:**

- Recruit flow through `BotReceiverFollowMeRecruitPatch`
- On conversion: brain/layer reset, conflict cleanup, group assignment, enemy/friendly list adjustment
- Dismiss: trigger `OnFollowerDismiss` event for addon cleanup
- English voice assignment applied at profile-load time via `SessionLoadBotsEnglishVoicePatch`

## 0c) BigBrain Follower Decision System

**Layer Stack (Priority Order):**

1. **FollowerRequestLayer** (priority 73) — Active command execution (hold/there/come/loot/door)
2. **FollowerPatrolLayer** (priority 72) — Idle follow patrol toward boss
3. **FollowerCombatLayer** (priority 71, vanilla only) — Fallback vanilla combat

**Core Files:**

- `client/BigBrain/FollowerPatrolLayer.cs` — Follow logic, state recovery, action selection
- `client/BigBrain/FollowerRequestLayer.cs` — Command request detection and activation
- `client/BigBrain/Actions/FollowAction.cs` — Chase and cover-settle movement
- `client/BigBrain/Actions/GestureCommandAction.cs` — Command execution pipeline (12 action types)
- `client/BigBrain/Actions/HealAction.cs` — Medical work with timeout safety
- Additional actions: `PeacefulAction.cs`, `PeaceHardAimAction.cs`, `GoToCoverPointAction.cs`, `EatDrinkAction.cs`

**Patrol Readiness (Post-Combat Handoff):**

- Wait for `BotFollowerPlayer.IsReadyForPatrolAfterCombat()` instead of fixed timeout
- SAIN-installed: uses addon-registered bridge callback (`SainAddonBridge.IsReadyForPatrolAfterCombat`)
- Fails closed with explicit error log if SAIN present but bridge unavailable
- Sprint thrash prevention via `FollowerSprintStateDirectionPatch` (SAIN-aware)

## 0d) Command Execution Pipeline

**Request Layer activates when `BotFollowerPlayer.CurrentCommand != null`**

Supported commands via `GestureCommandAction`:

- **HoldPosition** — Stop, crouch, periodic look-around (no timeout, persists until replaced)
- **ComeCloser** — Move within ~1m of boss, then resume prior hold
- **MoveToPoint** ("There") — Walk to NavMesh-validated target, brief arrival look-around
- **LootGeneric** / **LootWeapon** — Closest eligible follower assigned via `InteractableObjects.SetTaker(...)`, moves + inventory transfer
- **OpenDoor** — Closest eligible follower assigned via `InteractableObjects.SetOpener(...)`, moves + `DoorOpener.Interact(Open)`
- **Regroup** — Vanilla: converge to boss-near cover; SAIN: via addon `SAINFollowerCombatRegroupAction`
- **Attention** (Look) — Clear enemy state, release command, force attention to boss/point

**Visibility Requirements:**

- `HoldGesture` / `ThereGesture` — Follower sees boss gesture target (head or torso visibility sufficient)
- `ComeWithMeGesture` — Bidirectional visibility (boss sees follower AND follower sees boss)

**Interruption/Clearing:**

- Hit while executing command
- New command received (replaces prior)
- Attention/Look command (overrides)
- `FollowMe` / `Cooperation` phrase (clears request)
- Timeout or invalid execution state
- On regroup success: follower says `EPhraseTrigger.OnPosition`

## 0e) Recruit & Group Patching

**Files:**

- `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase -> follower conversion
- `client/Patches/BotRecruitPatch.cs` — Request interception
- 35+ patches total across bot/group/follower/UI stability

**Core Bot/Group/Follower Stability Patches:**

- `BotGroupAddEnemyPatch`, `BotGroupReportEnemyPatch`, `BotGroupUsecEnemyPatch` — Enemy propagation safety
- `BotGroupCalcGoalPatch` — Enemy acquisition assist hook
- `BotControllerEnemyPropagationSafetyPatch` — Validate player refs before propagation
- `BotOwnerIsFolowerPatch`, `BotOwnerManualUpdatePatch`, `BotOwnerActivatePatch` — Bot activation/update flow
- `BotMemoryDamagePatch`, `ExUsecBrainHitPatch` — Damage/hit reaction safety
- `LootPatrolActiveLayerListPatch`, `LootPatrolDecisionBypassPatch` — Prevent LootPatrol interference
- `AdvAssaultTargetFollowerGuardPatch`, `PatrolDataFollowerUpdateGuardPatch`, `AvoidDangerFollowerGuardPatch` — Vanilla follower recovery guards
- `AICoreAgentUpdatePatch` — Log/rethrow update exceptions

**Movement & Sprint Patches:**

- `FollowerSprintPatch` (only when SAIN absent) — Sprint behavior tuning
- `FollowerSprintStateDirectionPatch` — Prevent sprint/transition thrash during boss chase

**Recruit/Request Patches:**

- `BotReceiverFollowMeRecruitPatch` — Convert recruit requests to follower
- `FollowRequestPatch`, `HoldRequestPatch`, `OpenDoorRequestPatch` — Request type routing
- `BotReceiverGestureOverridePatch` — Gesture override handling

**Spawn/Raid Patches:**

- `BotsControllerPatch`, `BotsControllerStopPatch` — Bot controller lifecycle
- `LocalGameCleanupPatch`, `LocalGameCtorPatch` — Local game init/cleanup
- `BotsEventsControllerSpawnPatch`, `BossSpawnWaveManagerClassPatch` — Wave/event spawning
- `RaidStartPatch` — Raid initialization

**UI/Command/Item Patches:**

- `AIDataContructPatch` — AI model data
- `QuickPanelPatch` — Cooperation UI entry (non-follower AI only)
- `GestureMenuPatch`, `GestureMenuAvailablePhrasesPatch` — Gesture availability
- `EPhraseTriggerPatch`, `PlayPhraseOrGesturePatch` — Custom phrase/gesture routing
- `ItemSpecificationPanelPatch`, `ModRaidModdablePatch`, `UnlootableComponentPatch` — Item interaction
- `BotTalkTrySayPatch`, `BotTalkSayPatch` — Speech routing
- `GrenadeThrowPatch`, `GrenadeTryThrowSafetyPatch` — Grenade safety
- `BulletImpactPatch`, `HearingSensorPatch`, `FootstepSoundPatch`, `PlayerSayPatch` — Sound/reaction flow

**Teammate/Social UI Patches:**

- `AddTeammateCreationFlowPatch`, `AddTeammateHeadSelectionPatch` — Teammate creation screen
- `SocialPatch`, `ChatFriendsPanelPatch`, `OtherPlayerProfileScreenPatch` — Social UI integration
- `MenuScreenSquadControlPatch` — Team Management screen activation

## 0f) Utility Modules & bridges

**File:** `client/Modules/`

**Addon Bridge Contract:**

- `SainAddonBridge.cs` — Delegate interface for SAIN addon callbacks
    - `IsReadyForPatrolAfterCombat(BotOwner)` — Patrol readiness query
    - `OnFollowerDismiss(BotOwner)` — Lifecycle event for addon cleanup

**Shared Utilities:**

- `BotOwnerUpdateHub.cs` — Centralized bot-owner update coordination
- `FollowerCalcGoalEnemyAcquire.cs` — Forward-scan enemy acquisition (runtime-neutral, assists vanilla + SAIN)
- `FollowerEnemyEnforceSuppression.cs` — Attention/Look suppression enforcement
- `InteractableObjects.cs` — Loot/door interaction state, boss visibility tracking, taker/opener assignment
- `AddTeammateCreationFlow.cs` — Teammate profile creation form state
- `BossPlayers.cs` — Boss/player roster management
- `PingTeamates.cs` — Teammate enemy marker UI & callout system (throttled callouts, radio/visual markers)
- `FollowerRecovery.cs`, `FollowerAwareness.cs`, `Enemy.cs`, `Utils.cs` — Helper utilities

**Localization:**

- `TempEnglishLanguageProvider.cs` — Custom English text (teammate creation, squad menu, validation)

---

# SERVER SIDE: Teammate Backend & Social Integration

**Plugin ID:** `xyz.pit.friendlysain` (server component)  
**Main entry:** `server/friendlySAIN.Server.cs`

## Backend Responsibilities

**Core System:**

- PMC armband enforcement: Forces all PMC-type bots to wear side armbands (BLUE for USEC/Assault, RED for BEAR)
- Plugin priority: `PostDBModLoader + 1` (applies after database loads)

**Profile Management:**

- Create mod-owned teammate profiles (PMC bot + custom nickname/voice/head)
- Store on disk: `user/mods/friendlySAIN-ServerMod/Resources/teammates/<sessionId>/<aid>.json`
- Fetch full profiles for team UI (roster, profile view)
- Persistence: clothes, head, voice, loadout per teammate
- Teammate IDs use stock `HashUtil.GenerateAccountId()` collision-checked allocation

## REST API Routes

**Teammate Management** (`/singleplayer/friendlysain/`):

- `POST /teammate/create` — Create custom teammate (nickname/voice/head from UI) → saves to disk
- `GET /teammates` — List all teammates for session
- `GET /teammate/profile` — Fetch full profile for profile view screen
- `GET /teammate/profile/options` — Available customization options (clothes/loadouts)
- `POST /teammate/profile/suit` — Update clothes/head selection
- `POST /teammate/profile/loadout` — Change loadout
- `POST /teammate/profile/rename` — Rename teammate
- `POST /teammate/delete` — Delete teammate permanently

**Social List Merging** (`/client/`):

- `GET /friend/list` — **Merge** teammates into stock friend list
- `GET /friend/request/list/inbox` — **Merge** recruit requests with stock friend requests
- `GET /profile/view` — **Intercept** teammate profile views (custom UI layout)
- `POST /friend/request/accept` — Accept friend/recruit request
- `POST /friend/request/accept-all` — Accept all requests
- `POST /friend/request/decline` — Decline request
- `POST /friend/delete` — **Intercept** to also delete teammates

**Group & Raid** (`/client/`):

- `POST /match/group/invite/send` — Send group invite; **auto-accept** for teammates + notify
- `POST /game/bot/followergenerate` — Generate teammate spawn profile for raid
- `GET /game/bot/followerdetails` — Fetch follower details (tactic/equipment/voice/head, currently "Default")

**Post-Raid** (`/singleplayer/`):

- `POST /returnitems` — Return teammate items via mail (NOT YET POSTED in client runtime)
- `POST /teamescaped` — Log escape/death outcome + notify teammates
- `POST /friendlysain/recruitpickup` — Queue defeated NPC candidates as friend requests

## Backend Services

**`FriendlyTeammateService`** — Core CRUD & Profile

- Create teammate from appearance form (name, voice, head)
- List/fetch teammates by session
- Get/set profile (full fetch, clothes/loadout persistence)
- Rename teammate
- Delete teammate + remove from social lists
- Generate spawn profile for raid
- Save/load disk I/O for mod-owned JSON

**`FriendlyTeammateSocialCallbacks`** — Social List Patching

- Inject teammates into `/client/friend/list` response
- Patch `/client/profile/view` to detect teammates and apply custom UI
- Add teammates to `/client/friend/request/list/inbox`
- Intercept `/client/friend/delete` to clean up teammates too

**`FriendlyTeammateMatchCallbacks`** — Raid & Group Invite

- Auto-accept group invites for teammates
- Provide follower spawn profiles on demand
- Support custom health overrides for spawn

**`FriendlyPostRaidService`** — Post-Raid Handling

- Return items via mail system (sends to player, randomized NPC sender)
- Log raid outcomes (all escaped / partial / death)
- Send mail notifications to teammates about raid results
- NPC sender identity, message templates

**`FriendlyRecruitService`** — NPC Recruitment

- Queue defeated NPC candidates as pending friend requests
- Calculate recruit approval chance based on player level
- Convert recruit to permanent teammate via `FriendlyTeammateService`

## Key Limitations (Current In-Progress)

- Tactic persistence not yet implemented (`followerdetails` hardcoded to "Default")
- Voice/head customization from profile screen not yet implemented
- Invite/group flow still needs parity with pre-raid screen sequencing
- Teammate profiles remain mod-owned bot JSON, NOT full stock `SptProfile` accounts
- Post-raid item-return endpoint (`/singleplayer/returnitems`) disabled in client runtime

---

# SAIN ADDON: Combat & Retention System

**Plugin ID:** `xyz.pit.friendlysain.sainaddon`  
**Main entry:** `addon/SAINAddonPlugin.cs`  
**Bootstrap:** `addon/SAINRegroupBootstrap.cs`

## SAIN Integration Model

**Conditional Path:**

- Only active when SAIN (`me.sol.sain`) is installed at runtime
- Core plugin validates SAIN/addon presence and logs explicit error if SAIN present but addon missing
- Shared bridge contract via `SainAddonBridge.cs` for core → addon communication

**Layer Stack in Combat:**

- Priority 71: `SAINFollowerCombatLayer` (custom SAIN squad replacement for followers, active in combat)
- Priority 72: `FollowerPatrolLayer` (vanilla follow, active out-of-combat)
- Mover handoff on layer switch via `SAINLayer.OnLayerChanged(...)`

## Combat Decision Routing

**`SAINFollowerCombatLayer` Evaluation Chain:**

1. **Regroup Command** — If `RegroupNearBoss` request active: → `SAINFollowerCombatRegroupAction` (8s grace post-enemy)
2. **Squad Calculator** — Dynamic decision scoring: → routing to specific action
3. **SAIN Fallback** — If available, use native SAIN squad decision (2s enemy grace)
4. **Default** — Regroup to boss if in combat

**`SAINFollowerSquadDecisionCalculator` Priority Order:**

| Decision           | Condition                                     | Action                   | Constraints                                                  |
| ------------------ | --------------------------------------------- | ------------------------ | ------------------------------------------------------------ |
| **PushSuppressed** | Enemy suppressed by ally + vulnerable + close | `RushEnemyAction`        | Path < 75m (100m sprint), ammo > 50%                         |
| **GroupSearch**    | Ally searching same enemy                     | `FollowBossSearchAction` | Coordinate hunt                                              |
| **Suppress**       | Ally in retreat from enemy                    | `SuppressAction`         | Distance < 30-50m, ammo > 10-50%, no friendlies in fire lane |
| **Help**           | Ally engaging visible enemy                   | `SearchAction`           | Distance < 30-45m, seen < 8s                                 |
| **Search**         | Enemy known but unseen                        | `SearchAction`           | Seen within last 20s                                         |
| **Regroup**        | In combat, no other decision                  | `RegroupAction`          | Boss-adjacent, avoid stacking                                |

**Action Types:**

- `SAINFollowerCombatRegroupAction` — Converge near boss with spacing
- `SAINFollowerCombatSuppressAction` — Fire at enemy with friendly-fire checks
- `SAINFollowerCombatFollowBossSearchAction` — Follow boss while searching
- SAIN native `SearchAction` / `RushEnemyAction` (resolved once with safe fallback)

## Enemy Retention & Acquisition

**Gate System** (if `EnableForcedEnemyRetention = true`):

- `SAINEnemyAcquireGatePatch` — Patches `SAINEnemyController.CheckAddEnemy`
- `ShouldAllowAcquire()` — Block attention-suppressed, boss, allied followers
- `ShouldAllowSameSideAcquire()` — Whitelist only same-side with hostile intent

**Hostile Intent Detection:**

- `FollowerCalcGoalEnemyAcquire.CandidateHasBossOrFollowerAsEnemy()` — Debounced per-bot via `HasDebouncedSameSideHostileIntent()`
- Prevents friendly-fire escalation

**Suppression Safety:**

- `SAINFollowerSuppressionSafety.IsFriendlyInSuppressionLane()` — Geometry cylinder check (0.55m radius)
- Blocks suppression if ally in fire path at any body height

**Patrol Readiness (Post-Combat Release):**

Stale Decision Grace Periods:

- Search: 3s
- SeekCover: 3s
- Retreat: 4s
- ShiftCover: 3.5s
- Solo layer + no decision: 2.5s

On Timeout:

- `ForceReleaseFollowerCombatState()` clears all combat context
- Clear SAIN search state, expire active enemies, invalidate known places
- Hard-release layer ownership
- Post-release grace: 1.5s before patrol activation
- Apply crouch nudge during grace (prevent sprint-thrash)

## SAIN-Specific Patches (9 Always + 1 Conditional)

**Always Applied:**

1. **`SAINFollowerSquadLayerDisablePatch`** — Disable SAIN native squad layer for followers
2. **`SAINFollowerAimSwayPatch`** — Aim sway tuning for followers
3. **`SAINFollowerHitAccuracyPatch`** — Block incoming hits from degrading follower aim (bypass `AimHitEffectClass.GetHit`)
4. **`SAINFollowerRecoilPatch`** — Recoil behavior tuning + `OnFollowerDismiss` listener for cache cleanup
5. **`SAINFollowerFriendlyFirePatch`** — Route SAIN shot blocking to vanilla `ShootData.CheckFriendlyFire()` (uses vanilla sphere settings)
6. **`SAINFollowerGroupTalkDirectionPatch`** — Voice callouts use boss look direction instead of squad leader
7. **`SAINFollowerPersonalityPatch`** — Clone `followerBigPipe` SAIN template per-follower, apply combat personality (Normal/Chad/GigaChad)
8. **`SAINFollowerSquadLeaderPatch`** — Force `IAmLeader = false` for all followers
9. **`SAINFollowerLowLightVisionPatch`** — Reduce low-light vision penalty via `EnemyGainSightClass.CalcTimeModifier`

**Conditional (if `EnableForcedEnemyRetention`):**

10. **`SAINEnemyAcquireGatePatch`** + **`SAINFollowerEnemyRetentionService`** — Gate + assist follower enemy acquisition

## Lifecycle & Bridge Events

**Plugin Initialization:**

- `SAINAddonPlugin` registers callback on load, unregisters on unload
- `SAINFollowerRuntimeBridge` owns SAIN-typed patrol readiness implementation
- Bridge exposes: `IsReadyForPatrolAfterCombat(BotOwner)` + stale decision timer management

**Lifecycle Events:**

- `Modules.OnFollowerDismiss` — Fired when follower dismissed; addon hooks for recoil cache cleanup
- `ForceReleaseFollowerCombatState()` — Clear search state, expire enemies, reset decisions on attention/look
- `TryResetFollowerDecisionState()` — Soft reset for decision state without full layer release

**Integration Rule:**

- Prefer core → addon bridge calls over new core reflection probes
- Keep strict fail-fast/fail-closed behavior when SAIN installed but bridge unavailable

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

## 0) Project Context & References

- Old plugin codebase: `F:/Projects/SPT-Tarkov/friendlypmc`
- Old client reference (3.11): `F:/Projects/SPT-Tarkov/Client-Decompiled-3.11`
- New client reference (4.x): `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x`
- SAIN plugin reference: `F:/Projects/SPT-Tarkov/SAIN-4.4.0/SAIN`

**Positioning:** `friendlySAIN` is both a conversion of legacy `friendlypmc` behavior to the 4.x/BigBrain environment and an alternative plugin implementation with new BigBrain-native follower layers/actions.

---

# Command/Gesture IDs & Practical Entry Points

## Command/Gesture IDs (Current)

- Custom phrases:
    - `CustomPhrases.TeamStatus = 10001`
    - `CustomPhrases.OverThere = 10002`
- Custom gesture:
    - `CustomGestures.OverThere = 220` (`EInteraction` is byte-backed, so stay within `0..255`)
- Vanilla 4.x gestures:
    - `Rock/Scissor/Paper/AllRight = 200..203`
- UI visibility note:
    - gesture buttons are created from `CustomizationSolverClass.GetAvailableGestures(side)`, so visibility is side/template-data dependent (e.g., can differ for PMC vs Savage).

## Practical Entry Points (for next edits)

- Startup patch wiring:
    - `client/friendlyPlugin.cs`
- BigBrain core logic:
    - `client/BigBrain/FollowerPatrolLayer.cs` — Follow patrol out-of-combat
    - `client/BigBrain/FollowerRequestLayer.cs` — Active command execution
    - `client/BigBrain/Actions/FollowAction.cs` — Chase and settle movement
    - `client/BigBrain/Actions/GestureCommandAction.cs` — Command pipeline (hold/there/come/loot/door/regroup)
- Recruit and follower conversion:
    - `client/Patches/BotGroupRequestPatch.cs` — Recruit phrase → follower conversion
    - `client/Components/BotFollowerPlayer.cs` — Follower state container
- Boss command/event behavior:
    - `client/Components/AIBossPlayer.cs` — Boss command handling
- SAIN combat addon (follower combat layer):
    - `addon/SAINFollowerCombatLayer.cs` — Combat decision routing
    - `addon/SAINFollowerSquadDecisionCalculator.cs` — Priority-based decision scoring
    - `addon/SAINFollowerCombatRegroupAction.cs` — Combat regroup execution
    - `addon/SAINFollowerCombatSuppressAction.cs` — Fire support logic
    - `addon/SAINFollowerCombatFollowBossSearchAction.cs` — Coordinated search

---

# Known Issues & Tracking

See detailed tracking: `../FOLLOWER-BUGS.md`

**Currently Active Risk Areas:**

- SAIN post-combat idle/freeze behavior for followers
- Enemy propagation consistency across all followers
- Reaction-system regression (early detection):
    - Hard guards in `client/Utils/Enemy.cs` (`Enemy.MakeEnemy`) and `BotGroupReportEnemyPatch` prevent followers from marking boss/followers as enemies
    - Still active risk area for hearing/voice/bullet reaction paths
- Follow-up behavior when player is hit out of combat
- Follower death reaction:
    - Nearest follower with visibility says `EPhraseTrigger.OnFriendlyDown`
    - Corpse-position visibility checked up to ~60s if nobody saw death

---

## 7) Status/Debug Notes

- `PingTeamates` enemy marker/status timing corrected:
    - uses `Time.time - PersonalLastSeenTime` for recency.
- `PingTeamates` callout throttling:
    - directional voice callouts are now throttled to once every `15s` across pings,
    - ping radio/location sound and triangle marker still update every valid ping.
- TeamStatus/Look command burst handling:
    - command handling was debounced to reduce repeated heavy work during rapid player phrase spam.
- SAIN-friendly-fire path:
    - current follower-only SAIN override no longer uses custom boss/follower geometry checks.
    - it asks vanilla `ShootData.CheckFriendlyFire(from, to)` directly so SAIN follower fire denial follows vanilla friendly-fire sphere settings (`settings.FileSettings.Aiming.SHPERE_FRIENDY_FIRE_SIZE`) and vanilla ally filtering.
- SAIN follower combat template:
    - recruited followers now use a per-bot cloned copy of SAIN `followerBigPipe` settings as their combat template.
    - the template is applied through addon-owned SAIN info/file-settings injection instead of follower-specific aim-target/random-look/hit-accuracy patching.
    - `addon/SAINFollowerPersonalityPatch.cs` is the single entry point for future follower SAIN fine-tuning on top of that template (`ApplyFollowerTemplateFineTuning(...)`).
- SAIN stale search cleanup:
    - stale `EnemyKnownPlaces` / `SAINSearchClass` state was identified as the source of repeated `EPhraseTrigger.Clear` / `LostVisual` after combat or attention.
    - addon release/reset bridge now explicitly clears active SAIN search state and invalidates known places during follower combat-state release/reset.
- `PingTeamates` GUI path optimization:
    - per-frame draw loops now use index-based iteration instead of delegate-based `List.ForEach`.
    - bot status text reuses a single `StringBuilder` instance instead of allocating per bot per frame.
    - tracked body-part iteration uses a static array instead of `Enum.GetValues(...)` allocations.
- SAIN bridge debug noise reduction:
    - follower SAIN enemy-bridge debug logs are disabled by default (`EnableSainEnemyBridgeDebugLogs = false`) to reduce runtime string/log overhead.
- SAIN navigation investigation result:
    - SAIN does not currently have one broad active non-mover "navigation fix" patch that generically recovers stuck bots.
    - active navigation-adjacent behavior is split across:
        - `Patches/MovementPatches.cs` global movement-context patches (`MovementContextIsAIPatch`, `CanBeSnappedPatch`) and mover-manual-update patches,
        - door handling outside the mover (`Classes/PlayerManager/Doors/DoorHandler.cs`, `Classes/Bot/Doors/DoorOpener.cs`),
        - `SAINBotUnstuckClass`, which contains vault/teleport unstuck logic but its coroutine body currently has the core unstuck calls commented out.
    - practical implication: treat SAIN door handling and SAIN layer/mover handoff as active navigation influences first; do not assume SAIN has an active generic unstuck system currently rescuing follower navigation.
- English BEAR voice assignment is applied at profile-load time:
    - `SessionLoadBotsEnglishVoicePatch` patches `ProfileEndpointFactoryAbstractClass.LoadBots`
    - each returned `Profile` is processed by `BotOwnerActivatePatch.ApplyEnglishVoiceForProfile(...)`
    - this is the active runtime path for voice replacement (instead of late activation-only mutation)

---

## DEBUGGING & INVESTIGATION PRACTICES

- treat bugs tracked separately as SAIN and vanilla categories
- always spend time checking SAIN and client sources at the beginning of the session to get proper context
- prefer to check client sources first when some method, class, or property is not clear, rather then making assumptions
- Debug console command: `fs_spawnfollower` spawns one follower in-raid using game-side `ISession.LoadBots` profile flow (NOT BE-dependent)

## BUGS are tracked in : F:\Projects\SPT-Tarkov\FOLLOWER-BUGS.md

- `BotGroupAddEnemyPatch`
- `BotGroupReportEnemyPatch`
- `BotGroupUsecEnemyPatch`
- `BotGroupCalcGoalPatch`
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
- `PatrolDataFollowerUpdateGuardPatch`
- `AvoidDangerFollowerGuardPatch`
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
    - `SAINFollowerFriendlyFirePatch` (for follower shooters, delegates SAIN shot blocking to vanilla `ShootData.CheckFriendlyFire(from, to)` using `WeaponRoot.position` -> `CurrentAiming.RealTargetPoint`),
    - `SAINFollowerGroupTalkDirectionPatch` (uses boss look direction for directional enemy talk checks),
    - `SAINEnemyAcquireGatePatch` + `SAINFollowerEnemyRetentionService` (when `SAINAddonToggles.EnableForcedEnemyRetention = true`),
    - `SAINFollowerPersonalityPatch` (injects a per-follower clone of SAIN `followerBigPipe` bot settings as the follower combat template and aligns SAIN difficulty modifier to that template),
    - `SAINFollowerLowLightVisionPatch`.
- Follower enemy acquisition split:
    - shared forward-scan acquire assist now lives in core and is triggered from `client/Patches/BotGroupCalcGoalPatch.cs` by patching `BotCalcGoal.CalcGoalForBot()` directly,
    - core handler lives in `client/Modules/FollowerCalcGoalEnemyAcquire.cs`,
    - this path is runtime-neutral and now assists both vanilla and SAIN follower enemy pickup when vanilla goal calculation runs,
    - SAIN addon only keeps the SAIN-specific `CheckAddEnemy` gating path (`SAINEnemyAcquireGatePatch` + `SAINFollowerEnemyRetentionService` same-side/ally filtering),
    - old addon-only wrapper `addon/SAINCalcGoalPatch.cs` was removed.
- Follower SAIN tuning rule:
    - current stable path prefers SAIN template settings (`followerBigPipe`) over follower-specific aim/look compensation patches.
    - legacy follower aim-target/random-look/hit-accuracy patch files still exist in addon source, but are not wired by bootstrap.
- SAIN attention/release reset now clears stale search state through the addon bridge:
    - `SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState(...)` and `TryResetFollowerDecisionState(...)` clear `SAINSearchClass` active target/path and invalidate `EnemyKnownPlaces` for all tracked SAIN enemies before resetting decisions/layer state.
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
- Vanilla follower/update crash guards:
    - `client/Patches/FollowerVanillaSafetyPatch.cs`
    - `PatrolDataFollowerUpdateGuardPatch` prevents vanilla follower patrol from running with a missing boss/player-follow backing object and attempts boss-link recovery through `BossPlayers`.
    - `AvoidDangerFollowerGuardPatch` blocks vanilla `GClass48.ShallUseNow()` for followers when required danger subsystems are not initialized, preventing repeated `AvoidDanger` NREs.

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
    - current follower-only SAIN override no longer uses custom boss/follower geometry checks.
    - it asks vanilla `ShootData.CheckFriendlyFire(from, to)` directly so SAIN follower fire denial follows vanilla friendly-fire sphere settings (`settings.FileSettings.Aiming.SHPERE_FRIENDY_FIRE_SIZE`) and vanilla ally filtering.
- SAIN follower combat template:
    - recruited followers now use a per-bot cloned copy of SAIN `followerBigPipe` settings as their combat template.
    - the template is applied through addon-owned SAIN info/file-settings injection instead of follower-specific aim-target/random-look/hit-accuracy patching.
    - `addon/SAINFollowerPersonalityPatch.cs` is the single entry point for future follower SAIN fine-tuning on top of that template (`ApplyFollowerTemplateFineTuning(...)`).
- SAIN stale search cleanup:
    - stale `EnemyKnownPlaces` / `SAINSearchClass` state was identified as the source of repeated `EPhraseTrigger.Clear` / `LostVisual` after combat or attention.
    - addon release/reset bridge now explicitly clears active SAIN search state and invalidates known places during follower combat-state release/reset.
- `PingTeamates` GUI path optimization:
    - per-frame draw loops now use index-based iteration instead of delegate-based `List.ForEach`.
    - bot status text reuses a single `StringBuilder` instance instead of allocating per bot per frame.
    - tracked body-part iteration uses a static array instead of `Enum.GetValues(...)` allocations.
- SAIN bridge debug noise reduction:
    - follower SAIN enemy-bridge debug logs are disabled by default (`EnableSainEnemyBridgeDebugLogs = false`) to reduce runtime string/log overhead.
- SAIN navigation investigation result:
    - SAIN does not currently have one broad active non-mover "navigation fix" patch that generically recovers stuck bots.
    - active navigation-adjacent behavior is split across:
        - `Patches/MovementPatches.cs` global movement-context patches (`MovementContextIsAIPatch`, `CanBeSnappedPatch`) and mover-manual-update patches,
        - door handling outside the mover (`Classes/PlayerManager/Doors/DoorHandler.cs`, `Classes/Bot/Doors/DoorOpener.cs`),
        - `SAINBotUnstuckClass`, which contains vault/teleport unstuck logic but its coroutine body currently has the core unstuck calls commented out.
    - practical implication: treat SAIN door handling and SAIN layer/mover handoff as active navigation influences first; do not assume SAIN has an active generic unstuck system currently rescuing follower navigation.
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
