# friendlySAIN SAIN Addon: Architecture & Combat Flow

**File:** `addon/` directory  
**Plugin ID:** `xyz.pit.friendlysain.sainaddon`  
**Dependencies:** BigBrain, SAIN (hard), core friendlySAIN (hard)  
**Entry Point:** `SAINAddonPlugin.cs` → `SAINRegroupBootstrap.cs`

---

## 1. Runtime Bridge System

### `SAINAddonPlugin.cs` — Addon Lifecycle

**Init Path (Awake):**

1. Registers four core→addon bridge callbacks in `SainAddonBridge`:
    - `IsReadyForPatrolAfterCombat` → `SAINFollowerRuntimeBridge.IsReadyForPatrolAfterCombat`
    - `ForceReleaseFollowerCombatState` → `SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState`
    - `TrySyncFollowerEnemyState` → `SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState`
    - `TryResetFollowerDecisionState` → `SAINFollowerRuntimeBridge.TryResetFollowerDecisionState`
    - `GetFollowerDebugState` → `SAINFollowerRuntimeBridge.GetFollowerDebugState`
2. Subscribe `SAINFollowerRecoilPatch.OnFollowerLifecycleEvent` to lifecycle event handler
3. Call `SAINRegroupBootstrap.Initialize(harmony, Logger)` to wire all patches and register combat layer

**Cleanup (OnDestroy):**

- Unsubscribe all bridge callbacks (fail-safe reference equality check)
- Unregister lifecycle event handler

---

### `SAINFollowerRuntimeBridge.cs` — Patrol Readiness & Combat State Release

**Core Responsibilities:**

#### `IsReadyForPatrolAfterCombat(BotOwner owner)` → bool

Called by core plugin when SAIN is installed to check if a follower is safe to exit combat.

**Stale Decision Detection:**

- Tracks bot state transitions in three dictionaries:
    - `LastSoloCombatSeenAtByBot` — when bot was last in solo combat/layer/self-action
    - `StaleDecisionStartedAtByBot` — timer start for stuck decisions
    - `StaleDecisionTypeByBot` — which decision is stuck
    - `SoloLayerNoDecisionStartedAtByBot` — solo layer with no decision/enemy

**Decision Release Thresholds:**

- **Search**: 3 seconds with no enemy
- **SeekCover** (standalone): 3 seconds with no enemy
- **Retreat**: 4 seconds with no enemy
- **ShiftCover**: 3.5 seconds with no enemy
- **Solo layer + no decision + no enemy**: 2.5 seconds

**Post-Stale Release Actions:**

- Clears all stale decision trackers
- Calls `ForceReleaseFollowerCombatState(owner)` explicitly
- Applies cool-down crouch nudge (`pose = 0.1f` every `0.3s`) to prevent sprint-thrash recovery delay
- Returns `true` to allow patrol layer activation

**Solo Combat Grace Period:**

- After combat layer/self-action deactivates, gives `1.5s` grace for SAIN to fully settle
- Still applies crouch nudge during grace to prevent sprint transitions

---

#### `ForceReleaseFollowerCombatState(BotOwner owner)` → void

Hard-reset when combat readiness check fails or attention command is issued.

**Steps:**

1. **Clear SAIN search state** via `ClearFollowerSearchState(bot)`:
    - Access internal SAIN `SAINSearchClass` and clear active target/path
    - Bypass side effect via direct field manipulation

2. **Expire known enemy timers** via `ExpireKnownEnemyTimers(bot)`:
    - Walk all `EnemyController.KnownEnemies`
    - Set `TimeLastKnownUpdated` to very old time via reflection
    - Forces SAIN to forget enemy context immediately

3. **Clear enemy controller**:
    - `bot.EnemyController.ClearEnemy()`

4. **Reset SAIN decisions**:
    - `bot.Decision.ResetDecisions(false)`

5. **Hard-release layer ownership**:
    - `bot.ActiveLayer = ESAINLayer.None` — exit combat layer
    - `bot.BotActivation?.SetCurrentAction(null)` — freeze any action

6. **Clear vanilla request/movement state**:
    - `owner.BotRequestController.CurRequest.Complete()` + clear
    - `owner.StopMove()`
    - `owner.GoToSomePointData?.SetPoint(owner.Position)` + stop movement

---

#### `TrySyncFollowerEnemyState(BotOwner owner, Enemy? currentEnemy)` → void

Synchronizes follower's SAIN enemy state after new acquisition via core calc-goal path.

**Hook:** Called from core `FollowerCalcGoalEnemyAcquire` when core performs forward-scan friend-fire assist

**Purpose:** Ensure follower's SAIN internal enemy matches the core-picked target

---

#### `TryResetFollowerDecisionState(BotOwner owner)` → void

Reset SAIN decision state without hard-releasing layer (softer than `ForceRelease`).

**Hook:** Called from core attention/clear command paths

---

#### `GetFollowerDebugState(BotOwner owner)` → string

Return combat/decision state snapshot for debug display

---

## 2. Combat Layer System

### `SAINFollowerCombatLayer.cs` — Priority 73 (above patrol @ 72)

**Registration:**

- Registered by `SAINRegroupBootstrap` via `BrainManager.AddCustomLayer()`
- Applied to: `PmcBear`, `PmcUsec`, `ExUsec`, `PMC`, `Assault`, `Obdolbs`, `CursAssault`, `Knight`, `BigPipe`, `BirdEye`

**Activity Gating:**

```
IsActive() → TryEvaluateFollowerDecision(out decision)
  ✓ follower is active + alive
  ✓ follower is in group (IsFollower check)
  ✓ can retrieve SAIN BotComponent
  ✓ not in self-action (reload/med/surgery) or DogFight
  ✗ skip self-protect contexts (SeekCover+med, etc)
  ✓ decision resolved (via decision calculator or command)
```

**Decision Priority (in TryEvaluateFollowerDecision):**

1. **Handle Regroup Command** (explicit RegroupNearBoss):
    - Latch to `ESquadDecision.Regroup` while command active or within `8s` grace from last enemy
    - Allows combat-phase regroup to deescalate naturally post-enemy-loss

2. **Squad Decision Calculator** (`SAINFollowerSquadDecisionCalculator.TryGetDecision`):
    - Returns follower-specific combat decision (see below)

3. **SAIN Squad Decision Fallback** (if calculator returns None):
    - Use SAIN's native `CurrentSquadDecision` if available
    - Grace period: allow reuse within `2s` of last-seen-enemy
    - Prevents reuse purely by distance after combat ends

4. **Fallback to Regroup**:
    - If follower still has current enemy, default to Regroup

**Action Mapping:**

```
GetNextAction() routes decision → Action type:
  ESquadDecision.Regroup        → SAINFollowerCombatRegroupAction
  ESquadDecision.Suppress       → SAINFollowerCombatSuppressAction
  ESquadDecision.Search         → SAIN SearchAction (reflection-resolved)
  ESquadDecision.Help           → SAIN SearchAction (reflection-resolved)
  ESquadDecision.GroupSearch    → SAINFollowerCombatFollowBossSearchAction
  ESquadDecision.PushSuppressed → SAIN RushEnemyAction (reflection-resolved)
  default                       → SAINFollowerCombatRegroupAction
```

**Layer Transition Guard:**

- `IsCurrentActionEnding()` checks if action decision has changed or layer should exit
- Early exit if decision calculation fails

---

### `SAINFollowerSquadDecisionCalculator.cs` — Decision Routing

**Entry:** Static `TryGetDecision(BotOwner owner, BotComponent bot) → ESquadDecision`

**Preconditions:**

- Owner/bot not null, bot active, follower is in group with valid boss
- Returns `false` if any precondition fails

**Decision Tree (priority order):**

#### 1. **PushSuppressedEnemy** (most aggressive)

- Enemy is visible & suppressed by squad member
- Follower is healthy/injured (not dying)
- Path distance < `75m` (or `100m` if can sprint)
- Not low on ammo (> 50%)
- Enemy is vulnerable (surgery) OR weakened (dying/injured/prone)
- **Tuning:**
    - Ammo threshold: `0.5f`
    - Distance sprint: `100m`
    - Distance walk: `75m`
    - Surgery modifier: `1.25x`

#### 2. **GroupSearch** (coordinate with teammates)

- Follower has goal enemy
- Any alive follower is searching same enemy
- **Purpose:** Stay coordinated when another teammate actively hunts

#### 3. **Suppress / Help** (support squad)

Loop through all followers within radio comms range:

- Check if they share the same enemy
- **Suppress if:**
    - Enemy unseen but known
    - Squad member in retreat
    - Distance to member < `30m` (start) or `50m` (maintain)
    - Ammo > `10%` (maintain) or `50%` (start)
    - No friendlies in suppression lane
- **Help if:**
    - Enemy visible to squad member
    - Enemy distance < `30m` (start) or `45m` (maintain)
    - Enemy seen recently (< `8s` for maintain)

#### 4. **Search** (hunt lost enemy)

- Follower has enemy but not visible
- Enemy was seen (Seen = true)
- Enemy seen within last `20s`

#### 5. **Regroup** (default combat fallback)

- Only in combat context (presence of enemy)
- Not when purely distance-based (out of combat regroup stays on vanilla)

**Radio Comms Check:**

- If follower has earpiece, no range limit
- Otherwise, limit squad interaction to ~`34.6m` (sqrt of `1200`)

---

## 3. Combat Actions

### `SAINFollowerCombatRegroupAction.cs` — Dynamic Regrouping

**Purpose:** Converge follower to boss-near position with spacing for multiple followers

**State Management:**

- Tracks destination claims per boss (prevent stacking)
- `DestinationClaim`: { position, updatedAt }
- Stale claim timeout: `4s`

**Target Selection Loop:**

- **Refresh every:** `0.8s` or on boss movement > `2.5m`
- **Try `TrySelectRegroupTarget`:**
    - Find boss-adjacent cover/random points via NavMesh sampling
    - Check each point isn't already claimed by other follower
    - Claim winning point for `4s`
    - Register claim in `DestinationClaimsByBossId[bossId][followerId]`
- **Fallback:** Direct boss position if no adjacent target viable

**Movement Logic:**

```
leadDist = distance to target
enemyDist = distance to last known enemy (if exists)
sprint = (leadDist > 20m AND !enemy visible AND enemyDist > 50m)

Issue movement every 0.75s:
  sprint → Bot.Mover.RunToPoint(target)
  close to boss (within 3m + 1.5m arrival) → Bot.Mover.Stop()
  else → Bot.Mover.WalkToPoint(target)
```

**Pose & Speed:**

- Always set pose to `1f` (stand)
- Always set move speed to `1f` (normal)

**Combat Integration (OnSteeringTicked):**

- Try shoot any visible enemies
- Fallback: suppress any known enemies
- Fallback: look to movement direction

---

### `SAINFollowerCombatSuppressAction.cs` — Fire Support

**Purpose:** Keep suppressed enemy pinned while squad acts

**Movement:**

- Walk along known path to enemy (via `enemy.Path.PathToEnemy`)
- Pathed movement uses `Bot.Mover.WalkToPointByWay` (follows calculated route)

**Fire Control:**

- Check friendly-fire safety via `SAINFollowerSuppressionSafety.IsFriendlyInSuppressionLane`
- If friendly in lane: reset suppression, look to last-known, skip shoot
- Otherwise:
    - Shoot if visible enemy
    - Suppress if known enemy position
    - Look to last-known if neither

**Cleanup (Stop):**

- Always reset suppression on action end

---

### `SAINFollowerCombatFollowBossSearchAction.cs` — Coordinated Search

**Purpose:** Follow boss while searching for enemy last-known position

**Movement (every 0.25s-1s):**

- Maintain position near boss using SAIN `Bot.Search.ToggleSearch` context
- Only move if boss position changes significantly (> `1m` change)
- Calculate nearby reachable move position via `GetPosNearBoss`

**Search Context:**

- Toggle SAIN search behavior on/off
- Re-acquire enemy target from `Bot.GoalEnemy` each cycle

**Combat (OnSteeringTicked):**

- Shoot if visible
- Suppress if suppression available
- Look to movement direction if nothing to shoot

---

## 4. Enemy Retention & Acquisition Gating

### `SAINEnemyAcquireGatePatch.cs` — Enemy Addition Filter

**Applied:** Only if `SAINAddonToggles.EnableForcedEnemyRetention = true`

**Target:** Patches `SAINEnemyController.CheckAddEnemy` with prefix

**Gate Logic:**

1. **Non-followers pass through** (vanilla SAIN behavior)
2. **Bypass checks for teammates** (allow friendly acquisition as allies)
3. **Gate #1: `ShouldAllowAcquire`:**
    - If retention disabled → allow
    - Block if attention-suppressed via `FollowerEnemyEnforceSuppression`
    - Block if target is player boss
    - Block if target is allied follower
4. **Gate #2: `ShouldAllowSameSideAcquire`:**
    - Allow opposite-side acquisitions
    - Block same-side humans (they're friendly)
    - Block non-player same-side
    - Allow same-side with hostile intent:
        - Target has boss or follower as enemy
        - Target's goal-enemy is boss or follower
        - Debounced check via `FollowerCalcGoalEnemyAcquire.HasDebouncedSameSideHostileIntent`

---

### `SAINFollowerEnemyRetentionService.cs` — Acquisition Safety

**Two static gates** called by patch:

#### `ShouldAllowAcquire(BotOwner owner, IPlayer enemy) → bool`

- Checks: retention toggle, attention suppression, boss/ally filters
- Returns reason code for debug

#### `ShouldAllowSameSideAcquire(BotOwner owner, IPlayer enemy) → bool`

- Blocks friendly fire on same-side non-hostile bots
- Delegates hostile-intent detection to core `FollowerCalcGoalEnemyAcquire`
- Maintains whitelist only for bots that hostile to player/followers

---

## 5. Behavior Patches (Applied in Bootstrap)

### Always Applied:

1. **`SAINFollowerSquadLayerDisablePatch`**
    - Disables SAIN native `CombatSquadLayer` for all followers
    - Ensures follower combat goes through `SAINFollowerCombatLayer` instead

2. **`SAINFollowerAimSwayPatch`**
    - Modulates aim sway behavior for followers

3. **`SAINFollowerHitAccuracyPatch`**
    - Blocks `AimHitEffectClass.GetHit` for followers (incoming hits don't degrade aim)
    - Improves follower proficiency

4. **`SAINFollowerRecoilPatch`**
    - Tuning for recoil behavior + lifecycle event management

5. **`SAINFollowerFriendlyFirePatch`**
    - Routes follower weapon fire to vanilla `ShootData.CheckFriendlyFire(from, to)`
    - Uses vanilla ally sphere + filters instead of custom geometry

6. **`SAINFollowerGroupTalkDirectionPatch`**
    - Directional enemy voice callouts use boss look direction (not follower)
    - So followers report contacts relative to player perspective

7. **`SAINFollowerPersonalityPatch`**
    - Auto-applies `followerBigPipe` SAIN settings to spawned followers
    - Sets personality to `Chad` (for PMCs/BigPipe) or `GigaChad` (Knight) or `Normal` (BirdEye)
    - Clones and applies settings to bot's EFT file-settings
    - Re-evaluates difficulty modifiers based on bot profile difficulty
    - **Fine-tuning hook:** `ApplyFollowerTemplateFineTuning` (currently empty, extensible)

8. **`SAINFollowerSquadLeaderPatch`**
    - Forces `IAmLeader = false` for all followers
    - Redirects squad decision ownership to player boss

9. **`SAINFollowerLowLightVisionPatch`**
    - Reduces low-light vision penalty for followers
    - Post-processes SAIN `EnemyGainSightClass.CalcTimeModifier`

### Conditional (if `EnableForcedEnemyRetention = true`):

10. **`SAINEnemyAcquireGatePatch`** (see above)

---

## 6. Suppression Safety (`SAINFollowerSuppressionSafety.cs`)

**Called from:**

- `SAINFollowerCombatSuppressAction`
- `SAINFollowerSquadDecisionCalculator.ShallSuppressEnemy`

**Geometry Checks:**

**Method:** `IsFriendlyInSuppressionLane(BotOwner shooter, Vector3 targetPosition)`

1. Build fire-lane ray from shooter to target position
2. Dynamic lane radius: `0.55m`
3. Check if player or any follower intersects lane:
    - **Lateral distance < `0.55m`** from ray
    - **Forward distance < ray distance + `1m` padding**
    - **Close front exception:** If ally < `2.5m` AND angle > `0.25` dot product → include in lane (safer)

4. Test three body heights per person: feet, torso, head

**Returns:** `true` if any ally in lane → suppression blocked

---

## 7. Addon Toggles (`SAINAddonToggles.cs`)

```csharp
Enable/Disable:
  - EnableForcedEnemyRetention
    └─ Activates SAINEnemyAcquireGatePatch + retention service
  - EnableSainEnemyBridgeDebugLogs
    └─ Controls verbose logging from enemy bridge paths
  - (Other SAIN follower-specific toggles)
```

---

## 8. Flow Diagram: Combat Phase

```
┌─────────────────────────────────────────────────────────────┐
│ SAINFollowerCombatLayer.IsActive()                          │
│  → TryEvaluateFollowerDecision()                            │
└─────┬───────────────────────────────────────────────────────┘
      │
      ├─ [1] TryHandleRegroupCommand()
      │       (explicit RegroupNearBoss from core)
      │       → Decision = Regroup (latched 8s)
      │
      ├─ [2] SAINFollowerSquadDecisionCalculator.TryGetDecision()
      │       ├─ PushSuppressedEnemy (enemy suppressed + vulnerable)
      │       ├─ GroupSearch (ally searching same enemy)
      │       ├─ Suppress (support ally suppression)
      │       ├─ Help (support ally engagement)
      │       ├─ Search (lost visible enemy)
      │       └─ Regroup (in-combat default)
      │
      ├─ [3] SAIN Fallback
      │       (if calculator returns None)
      │       → Use SAIN CurrentSquadDecision (2s grace)
      │
      └─ [4] Default
              → Regroup (if has current enemy)

┌─────────────────────────────────────────────────────────────┐
│ GetNextAction() → Action(decision)                          │
│  SAINFollowerCombatRegroupAction                            │
│  SAINFollowerCombatSuppressAction                           │
│  SAINFollowerCombatFollowBossSearchAction                   │
│  (+ SAIN search/rush actions via reflection)               │
└─────┬───────────────────────────────────────────────────────┘
      │
      └─ Action.Update() + OnSteeringTicked()
         ├─ Shoot/suppress visible/known enemies
         ├─ Friendly-fire safety check
         └─ Steer to target / boss / movement

┌─────────────────────────────────────────────────────────────┐
│ On Combat Exit (IsReadyForPatrolAfterCombat via bridge)     │
│  Bridge.IsReadyForPatrolAfterCombat(owner)                 │
│  → Check stale decision timers                              │
│  → If stale: ForceReleaseFollowerCombatState()              │
│     ├─ Clear SAIN search state                              │
│     ├─ Expire known enemies                                 │
│     ├─ Reset decisions                                      │
│     ├─ Release layer (ActiveLayer = None)                   │
│     └─ Clear movement state                                 │
│  → Return true → Activate patrol layer                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 9. Initialization Order

```
SAINAddonPlugin.Awake()
  ├─ Register bridge callbacks (*after* core plugin is running)
  ├─ Subscribe lifecycle handler
  └─ SAINRegroupBootstrap.Initialize(harmony)
      ├─ Apply 9 behavior patches
      ├─ Conditionally apply enemy acquire gate
      └─ BrainManager.AddCustomLayer(SAINFollowerCombatLayer, priority=73)
```

---

## 10. Known Architecture Notes

- **Reflection Safe:** All SAIN type access uses `AccessTools` with null fallback
- **Non-invasive:** Patches are limited to follower-specific decision gates and behavior tweaks
- **Stateless Actions:** Combat actions don't maintain long-term state; decisions recalc every frame
- **Bridge Pattern:** Core plugin is SAIN-unaware; addon owns all SAIN integration via bridge callbacks
- **Squad Claim System:** Regroup action prevents multiple followers claiming same boss-adjacent spot
- **Stale Timer Tracking:** Combat readiness uses per-bot stale-decision timers to escape stuck states
- **Friendly Fire:** Uses vanilla sphere + SAIN exclusions + active lane geometry check for safety

---

## 11. Extension Points

1. **`ApplyFollowerTemplateFineTuning`** in `SAINFollowerPersonalityPatch.cs`
    - Add follower-specific aim/look/difficulty tweaks on top of `followerBigPipe`

2. **Decision Calculator** in `SAINFollowerSquadDecisionCalculator.cs`
    - Add new squad decision branches before Regroup fallback

3. **Custom Actions** in `GetNextAction()` → Action mapping
    - Register new BotAction types for new decisions

4. **Combat Action Updates**
    - Modify targeting, movement, or steering logic in individual action classes

5. **Enemy Retention Gates**
    - Extend `ShouldAllow*` logic in `SAINFollowerEnemyRetentionService`
