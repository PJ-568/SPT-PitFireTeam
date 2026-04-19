# Combat Tactics Notes

## Scope

- Reviewed path: core/vanilla follower combat under `client/BigBrain`
- SAIN addon path is currently disabled and was not part of this review

## Current Status

### Team Effort: Team Search

Follower combat has a team-level coordination path. It should not be described as default and marksman making fully independent parallel choices.

Authoritative model:

- `AIBossPlayer` owns the shared team coordination object through `CombatEvents`.
- `CombatEvents` currently exposes an active push/search event:
    - owner follower
    - enemy profile id
    - enemy anchor position
    - push/search destination
    - reason
    - `IsSearchPush`
- `FollowerCombatDefault` can initiate the team action:
    - when a push decision is committed, `CommitPush(...)` calls `TryEmitPushEvent(...)`
    - `TryEmitPushEvent(...)` publishes through `boss.CombatEvents.TryEmitPush(...)`
    - when the committed push clears, `ReleasePushEvent(...)` releases the active event
- `FollowerCombatSniper` reacts to the team action:
    - `TryGetActivePushEvent(...)` reads `boss.CombatEvents.TryGetActivePushFor(...)`
    - the event owner is ignored, so the emitting follower does not react to itself
    - if the event enemy differs from the sniper's current goal enemy, sniper tries to promote the event enemy as its goal
    - if `IsSearchPush` is true and the sniper is close to the push owner, sniper joins via `EnemySearch("sniper.closeSearch")`
    - otherwise sniper tries to commit a support firing cover with `TryCommitPushSupportCover(...)` and enters `sniper.FireSupport.push`

This behavior is the current **Team Search** pattern:

- default/balanced follower can become the active search/push owner
- marksman/sniper supports that team search instead of independently choosing a normal default-style push
- close team-search support can collapse into `sniper.closeSearch`
- non-close team-search support should prefer firing-position support cover

Important policy rule:

- Shared primitives can live in `FollowerCombatCommon`, but coordinated behavior must remain event-aware.
- A shared helper such as grenade suppression may be callable by both default and sniper, but the decision to call it should not bypass the `CombatEvents` team-search context when the behavior is meant to be coordinated support.

### Completed

- Phase 1 is implemented: `FollowerCombatLayer` no longer force-ends actions just because the action enum changed between ticks.
- Marksman now has a shared committed-cover phase helper, `CommittedCoverPhaseState`, used for support and reposition travel/hold ownership.
- Marksman `fireSupport` no longer re-picks support cover while a support phase is active, and on arrival it enters a short `sniper.fireSupportHold` settle phase instead of immediately rethinking.
- Marksman boss-distance break during `fireSupportHold` now only stays locked when there is a real immediate shot from the current cover, not just a generally visible/shootable enemy.
- Default `shootCover` / `retreatShootCover` now enter a short `shootCoverHold` settle phase on arrival before regroup/advance/rethink is allowed to win.
- Cover pose behavior now uses committed cover intent:
    - the decision that selected/reached the cover records whether the cover was chosen to shoot or to hide
    - shooting-cover intents may stand if a standing-height lane exists
    - safe/hiding-cover intents keep the vanilla crouch/hold behavior
    - this applies to both marksman fire-support/reposition cover and default `shootCover` / `retreatShootCover`

### In Progress

- Cover settle behavior exists, but it is still tactic-local instead of being owned centrally by `FollowerCombatCommon`.
- Default has committed cover pose intent, but still does not have a full branch-level committed-intent model; broader support/protect/fallback ownership is still mostly derived from branch order plus committed cover/push.
- Search-target commitment is still unresolved; `EnemySearch(...)` and other tactical-point flows can still drift.

### BUGS:

## Findings

### 1. Cover hold is not truly protected after arrival

The current cover hold flow arms a hold timer, but default hold end logic can still break almost immediately for non-emergency reasons such as ally support, boss-support pressure, regroup pressure, generic advance, or any visible enemy.

Impact:

- follower reaches cover, then exits the plan too quickly
- cover hold does not behave like a real "settle and scan" phase
- this contributes directly to mid-execution changes of mind

Relevant files:

- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatCommon.cs`

### 2. Default combat commits geometry more than intent

Default combat has good committed movement primitives for push and cover travel, and cover commitments now preserve a narrow shoot-vs-hide pose intent. However, non-push branches still do not hold broader ownership cleanly after movement completes. Support and protect flows can dissolve back into generic routing as soon as the bot reaches the destination or enemy memory shifts.

Impact:

- ally support can start, then collapse into generic search/hold behavior
- boss-under-attack reactions can lose continuity after arrival
- default tactic does not yet have durable branch ownership comparable to marksman reposition/support state
- current cover pose intent only answers "stand to shoot or crouch to hide"; it is not a complete support/protect/fallback intent latch

Relevant files:

- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatLogicBase.cs`
- `client/BigBrain/FollowerCombatSniper.cs`

### 3. Search-style movement re-picks targets too often

`EnemySearch(...)` writes a fresh tactical point whenever that branch is selected again. Search-like movement therefore stays committed to the action type, but not necessarily to the actual destination.

Impact:

- follower starts moving to search, then drifts toward a slightly different point
- tactical movement can look indecisive even when the same branch keeps winning
- marksman close-search inherits the same issue

Relevant files:

- `client/BigBrain/FollowerCombatCommon.cs`
- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`

### 4. Phase 1 confirmed: layer-level action change force-end causes churn

This is now verified against BigBrain and EFT base flow. The combat layer stores `lastDecision = currentDecision` during `GetNextAction()`, then on the following update `IsCurrentActionEnding()` force-ends the current action whenever the action enum differs from the previous one.

BigBrain/EFT ordering:

1. previous action end check runs first
2. if ending, next decision is requested
3. new logic starts
4. next tick, the new action is evaluated

So the force-end is not a framework ordering issue. It is local combat-layer behavior.

Impact:

- transitions like `runToCover -> holdPosition`, `holdPosition -> goToPointTactical`, and `runToCover -> shootFromCover` are vulnerable to being killed one tick after starting
- this amplifies every other source of decision churn

Relevant files:

- `client/BigBrain/FollowerCombatLayer.cs`
- `F:/Projects/SPT-Tarkov/SPT-BigBrain-master/Internal/CustomLayerWrapper.cs`
- `F:/Projects/SPT-Tarkov/Client-Decompiled-4.x/Assembly-CSharp/AICoreLayerClass.cs`

### 5. Cover pose must follow the reaching decision intent

Vanilla `holdPosition` (`GClass278`) crouches while holding in cover. That is correct for hiding/safe cover, but wrong for shooting cover that only has a lane if the bot stands. Relying only on `GoalEnemy.CanShoot` fails in this case because a crouched bot may see the enemy while EFT still reports the shot as unavailable from the current pose.

Current rule:

- `CommitCover(...)` records the cover id and whether the reaching decision reason was a shooting-cover intent.
- Standing is allowed only if the bot is still in that same committed shooting cover and a standing-height line check succeeds.
- Hiding/safe cover keeps the vanilla crouch behavior.

Shooting-cover examples:

- `shootCover`
- `retreatShootCover`
- `committedFire`
- `coverVisibleFire`
- `sniper.FireSupport`
- `sniper.reposition`
- `sniper.protectBossShootCover`
- `sniper.startPosition`

Hiding/safe-cover examples:

- `safeCover`
- `retreatSafeCover`
- `bossCover`
- `protectBossCover`
- generic `coverHold` reached from a non-shooting cover

Implementation notes:

- `FollowerCombatCommon.CanShootFromCurrentCoverOrStandingIntent(...)` first tries the normal cover shot check, then tries the standing-lane handoff only when the committed cover intent permits it.
- `CombatHoldPositionAction` reapplies the standing pose after vanilla hold logic runs, but the shared helper refuses unless the committed cover intent says this cover was reached to shoot.
- Default and marksman both use the same committed-cover intent gate; SAIN addon behavior is not covered by this core-path change.

Relevant files:

- `client/BigBrain/FollowerCombatCommon.cs`
- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`
- `client/BigBrain/Actions/CombatHoldPositionAction.cs`

## Incremental Fix Plan

### Phase 1: Remove layer-level action-enum auto-end

Status:

- Complete

Change:

- remove the `actionChanged` force-end from `FollowerCombatLayer.IsCurrentActionEnding()`

Goal:

- let actions end based on their own end logic
- preserve current combat activation and linger behavior

Expected result:

- new actions are allowed to survive their first update tick
- immediate churn caused by cross-tick action comparison is removed

### Phase 2: Add shared cover-settle lock

Status:

- Partially implemented, but still tactic-local

Current state:

- marksman `fireSupport` has an arrival settle hold
- default `shootCover` has an arrival settle hold
- both still use tactic-owned policy/state instead of one shared cover-settle path in `FollowerCombatCommon`

Change:

- add a short cover-settle window in shared combat common
- arm it on arrival in committed cover or equivalent arrival detection

Break rules during this window:

- allow immediate fight
- allow heal-cover handoff
- allow hard under-fire / recent-hit escape
- optionally allow explicit regroup order if that should remain absolute
- block ally support, boss support, regroup pressure, generic advance, and other non-emergency branch swaps until the lock expires

Goal:

- make "arrived in cover" behave like a real settle-and-scan phase

### Phase 3: Add committed intent for default combat

Status:

- Partially implemented for committed cover pose intent only

Current state:

- default has committed push
- default `shootCover` now has a short arrival-owned settle phase
- default committed cover now records whether the reaching decision selected shooting cover or hiding cover
- default can stand from a committed shooting cover when a standing-height shot lane exists
- default hiding/safe covers keep crouch behavior
- non-push support/protect/fallback branches still do not have a durable shared intent latch

Change:

- extend beyond cover pose into a lightweight branch intent latch for default tactic

Suggested intents:

- `None`
- `Initial`
- `AllySupport`
- `BossSupport`
- `Push`
- `FallbackHold`

Rules:

- when one of these branches wins, it owns subsequent movement/hold until completion or hard interrupt
- use cover commitment as the movement primitive under the intent, not as the only state

Goal:

- keep support/protect behavior coherent after movement completes

### Phase 4: Commit search targets

Status:

- Not started

Change:

- cache a stable target point for search-like phases
- reuse it until arrival, enemy reacquire, or hard interrupt

Apply first to:

- `push.search`
- `startWeakEnemyPush.tactical`
- `sniper.closeSearch`

Goal:

- stop tactical search drift caused by repeatedly mutating the target point

### Phase 5: Tune marksman separately

Status:

- Partially implemented

Current state:

- marksman support/reposition now use `CommittedCoverPhaseState`
- `fireSupport` arrival churn is reduced
- boss-distance break during support hold is more reliable
- close-search / tactical search destination commitment is still pending

Change:

- preserve marksman-specific support/reposition behavior
- keep boss-under-attack and ally-engaged responses marksman-safe
- ensure firing-position search and close-search are committed phases

Goal:

- keep sniper/marksman distinct from balanced/protector while reusing shared infrastructure

## Recommended Order

1. Phase 1: remove action-enum auto-end
2. Phase 2: shared cover-settle lock
3. Phase 3: default committed intent
4. Phase 4: committed search targets
5. Phase 5: marksman tuning

## Centralization Direction

As tactic count grows, especially with protector, marksman, future additional tactics, and Goons-specific variants, the system should centralize shared combat state and branch ownership rules instead of duplicating per-tactic micro-state.

The preferred direction is:

- shared commitment primitives in `FollowerCombatCommon`
- thin tactic policy classes that choose intent, not full bespoke lifecycle code
- objective/router ownership in `FollowerCombatLogicBase`
- tactic-local overrides only for real policy differences such as push bias, boss support bias, firing-position preference, and allowed break conditions

## Centralization Opportunities

### Opportunity 1: centralize tactic lifecycle boilerplate

Current duplication:

- `Reset()` and `DecisionChanged()` bookkeeping is duplicated in default and marksman
- both tactics call shared combat-common handlers, then tack on tactic-local state updates

Relevant files:

- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`

Suggested direction:

- add a shared tactic base that owns:
    - `ResetSharedState()`
    - `HandleDecisionChangedShared(...)`
    - standard committed-cover update behavior
- let concrete tactics override only the extra local state they need

Why it matters:

- every new tactic will otherwise repeat the same reset and transition plumbing

### Opportunity 2: centralize committed-cover phase ownership

Current duplication:

- default and marksman both implement their own committed-cover arrival / move / hold / break flow
- both decide:
    - what happens when committed cover exists
    - what happens on arrival
    - when hold should continue
    - when boss pressure or visibility breaks the phase

Relevant files:

- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`
- `client/BigBrain/FollowerCombatCommon.cs`

Suggested direction:

- build on the new `CommittedCoverPhaseState` helper and move committed-cover lifecycle into shared phase helpers in `FollowerCombatCommon`
- have tactic code provide policy hooks such as:
    - `OnCommittedCoverArrived`
    - `CanBreakCommittedCoverForBossPressure`
    - `CanBreakCommittedCoverForAllySupport`
    - `SelectHoldReason`

Why it matters:

- this is the highest-risk area for duplication once more tactics exist
- current status already shows the shape of the problem:
    - marksman has `supportPhase` and `repositionPhase`
    - default now has `shootCoverSettlePhase`
    - more tactics will keep adding their own local phase booleans/helpers unless this is centralized soon

### Opportunity 3: centralize boss-pressure and regroup gating

Current duplication:

- default and marksman both compute boss-distance regroup pressure
- both contain near-duplicate logic for:
    - safe regroup distance
    - boss-under-attack break decisions
    - explicit regroup command detection

Relevant files:

- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`
- `client/BigBrain/FollowerCombatLogicBase.cs`

Suggested direction:

- move command and boss-pressure gates into a shared evaluator owned by objective/router level
- leave only policy thresholds in tactic code

Good split:

- router/objective layer:
    - explicit regroup command consumption
    - boss-distance state
    - boss-under-attack raw signal
- tactic layer:
    - whether that signal may interrupt current intent
    - what support action to prefer

Why it matters:

- Goons or protector-style tactics will need the same signals but different policy responses

### Opportunity 4: centralize search-target commitment

Current duplication:

- search-like behavior is built from shared helpers, but no shared committed search target exists
- marksman and default both rely on `EnemySearch(...)` and tactical-point movement without target ownership

Relevant files:

- `client/BigBrain/FollowerCombatCommon.cs`
- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`

Suggested direction:

- add a shared committed tactical target primitive in `FollowerCombatCommon`
- let tactics request:
    - commit search target
    - reuse search target
    - clear search target on hard interrupt or arrival

Why it matters:

- this becomes a reusable building block for balanced, protector, marksman, and future assault/goon tactics

### Opportunity 5: centralize intent-state representation

Current issue:

- default currently relies mostly on branch order plus local push commitment
- marksman now uses shared phase objects for support/reposition, but they are still tactic-local intent fragments
- default now also has a tactic-local `shootCover` settle phase
- regroup has its own objective-owned state

Suggested direction:

- define one shared intent-state model, likely at objective level
- examples:
    - `Push`
    - `SupportAlly`
    - `ProtectBoss`
    - `Reposition`
    - `Regroup`
    - `FallbackHold`

Then let each tactic decide:

- which intents it is allowed to enter
- which intents can interrupt which others
- which movement/hold primitive each intent uses

Why it matters:

- this is the main scalability point for supporting more tactics without every class growing its own custom boolean mesh
