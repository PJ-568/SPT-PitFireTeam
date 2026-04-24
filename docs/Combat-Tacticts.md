# Combat Tactics Notes

## Scope

- This document covers the core follower combat path under `client/BigBrain`.
- It does not describe the optional SAIN addon combat path.
- Treat this file as a current-state runtime note, not a design backlog.

## Current Runtime Model

### Ownership

Core combat is objective-routed.

- `FollowerCombatLogicBase` owns the combat router.
- The router owns three objective stacks:
  - `FollowerCombatDefaultObjective`
  - `FollowerCombatSniperObjective`
  - `FollowerCombatRegroupObjective`
- Default and marksman keep their own tactic trees, but regroup is shared and objective-owned.
- Objective ownership is stateful. Shared interrupt actions such as heal or dogfight do not automatically change the owning objective.

Relevant files:

- `client/BigBrain/FollowerCombatLogicBase.cs`
- `client/BigBrain/FollowerCombatDefaultObjective.cs`
- `client/BigBrain/FollowerCombatSniperObjective.cs`
- `client/BigBrain/FollowerCombatRegroupObjective.cs`

### Combat Start

Combat start still seeds an opening decision through `PrepareStartDecision(...)`.

Current opening priorities are:

1. visible close-cover pressure
2. unseen under-fire response
3. ally-engagement support opener
4. low-threat blind push
5. far-cover fallback

If none match, the tactic falls through to its normal decision tree.

Relevant file:

- `client/BigBrain/FollowerCombatCommon.cs`

### Default / Balanced Combat

`FollowerCombatDefault` is the main balanced combat tree.

Current important behavior:

- committed push is sticky until hard interrupt or completion
- explicit `PushEnemy` (`GoForward` in combat) preempts passive hold/cover routing
- visible contact prefers immediate fire before passive hold
- committed cover is reused instead of constantly selecting new cover
- committed cover arrival prefers:
  1. immediate fire
  2. visible fire
  3. pressure / reaction
  4. passive `coverHold`
- passive hold reasons are mainly:
  - `coverHold`
  - `bossHold`
- boss-distance pressure can switch default combat into the regroup objective

Relevant file:

- `client/BigBrain/FollowerCombatDefault.cs`

### Marksman Combat

`FollowerCombatSniper` is a separate marksman tree, not a variant inside default combat.

Current important behavior:

- marksman prefers firing-position support and reposition logic over generic rush behavior
- explicit push does not make marksman acknowledge or behave like balanced assault logic
- marksman can join team push/search support through `CombatEvents`
- close-quarter handling can temporarily prefer a full-auto secondary
- boss-distance pressure hands off to the shared regroup objective

Relevant file:

- `client/BigBrain/FollowerCombatSniper.cs`

### Regroup Objective

Combat regroup is objective-owned.

Current behavior:

- explicit regroup command in combat activates the regroup objective
- explicit `PushEnemy` can override regroup and return to the tactic's primary objective
- hot contact regroup stays combat-active and moves bossward with withdraw-style movement
- cooled contact regroup becomes an urgent bossward run through `CombatRegroupRunAction`
- regroup completion is based on reaching the boss / bossward objective, not on re-entering default hold logic

Relevant files:

- `client/BigBrain/FollowerCombatRegroupObjective.cs`
- `client/BigBrain/Actions/CombatRegroupRunAction.cs`

## Cover Contract

Cover is shared infrastructure, not tactic-local geometry only.

Current cover lifecycle:

1. select cover
2. commit cover
3. move to committed cover
4. detect arrival through cover/proximity checks
5. either fire immediately or enter a short hold path
6. break hold when a stronger reason wins

Current important rules:

- committed cover is follower-local and reused until invalid or intentionally cleared
- cover intent tracks whether the cover was reached to shoot or to hide
- shooting-cover intent may stand if a standing-height lane exists
- hiding/safe cover keeps crouch behavior
- default and marksman both use the same shoot-vs-hide committed cover intent gate

Relevant files:

- `client/BigBrain/FollowerCombatCommon.cs`
- `client/BigBrain/FollowerCoverCommitment.cs`
- `client/BigBrain/Actions/CombatHoldPositionAction.cs`

## Team Coordination

Core combat has a team push/search coordination path.

Current behavior:

- `AIBossPlayer` owns shared `CombatEvents`
- default combat can emit active push/search events
- marksman can react to those events with support fire or close search
- this is the current team-search pattern; marksman is not intended to act like an independent second default attacker

Relevant files:

- `client/Components/AIBossPlayer.cs`
- `client/BigBrain/FollowerCombatDefault.cs`
- `client/BigBrain/FollowerCombatSniper.cs`

## Out-of-Combat Commands That Affect Combat Handoff

These are part of the practical combat model because they often hand off into combat behavior.

- out-of-combat regroup runs through `GestureCommandAction`
- out-of-combat regroup now keeps stable run intent instead of fighting EFT sprint downgrades
- `OverThere` / non-combat `GoForward` resolve a same-level-biased target point instead of broad vertical nav snapping
- `Attention` clears enemy state and now also blocks group enemy re-report during the suppression window

Relevant files:

- `client/BigBrain/Actions/GestureCommandAction.cs`
- `client/Components/AIBossPlayer.cs`
- `client/Modules/FollowerEnemyEnforceSuppression.cs`

## Known Remaining Gaps

These are the main gaps still worth tracking from the core path.

### 1. Cover settle is still partly tactic-local

There is shared committed-cover infrastructure, but some settle/hold behavior is still implemented in tactic-specific paths instead of one shared cover-settle system.

Impact:

- cover arrival behavior is improved, but not fully centralized
- future tactics can still duplicate local hold/settle state

### 2. Default combat still commits movement better than higher-level intent

Default has strong committed movement primitives, but non-push support/protect ownership is still weaker than a full intent model.

Impact:

- support/protect decisions can still dissolve back into generic routing after movement completes
- branch continuity is better than before, but not fully explicit

### 3. Search-style tactical movement is better, but not fully committed

Search-like movement now has better stall handling, but target ownership is still lighter than committed push or committed cover.

Impact:

- tactical search movement can still drift or repick
- this affects both default search-like movement and marksman close-search style support

## Candidate Work Plan

These are candidate core-combat changes discussed during recent review. This section is a planning note, not current runtime behavior.

### A. Visible-Fire Posture Policy

Goal:

- own tactical posture and movement choice around visible contact
- leave EFT in control of burst length, cadence, recoil handling, and fire mode

Reason:

- current visible-contact handling still routes too many cases into stationary `shootFromPlace` / `shootFromCover`
- the better question is not "always move and shoot" versus "always stop and shoot"
- the real decision is when the bot should:
  1. plant and trade
  2. move while pressuring
  3. stay standing
  4. crouch

Proposed implementation shape:

- add a shared visible-trade posture helper in `FollowerCombatCommon`
- have default combat ask that helper before choosing `shootFromPlace`, `shootFromCover`, or `attackMoving`
- keep action validity strict: if the policy wants `attackMoving`, a real destination must already exist

Required rule:

- never emit `attackMoving` unless there is already a verified move point through committed cover, assigned cover, tactical point, regroup vector, or other action-owned destination

Candidate outcome buckets:

- `shootFromCover`
  - current cover is valid
  - lane is valid
  - posture should stay planted
- `shootFromPlaceStanding`
  - bot is exposed or semi-exposed
  - current trade is favorable enough to plant briefly
  - no crouch unless there is an actual geometry advantage
- `attackMoving`
  - bot needs movement as protection
  - a valid destination already exists
  - movement is tactical, not stale/no-target

Likely affected files:

- `client/BigBrain/FollowerCombatCommon.cs`
- `client/BigBrain/FollowerCombatDefault.cs`
- possibly `client/BigBrain/FollowerCombatSniper.cs` later, after default behavior is stable

### B. Immediate/Visible Fire Delay Reduction

Status:

- partly implemented already

Current direction:

- alignment wait was reduced so `shootImmediately` no longer waits for tight weapon-root alignment before firing
- the gate is now closer to a coarse threat-facing check than a full aim-rig alignment gate

Remaining question:

- after the delay fix, do visible-fire branches still overuse planted fire when a tactical move would be better?

This depends on item A.

### C. Stronger Shoot-From-Place Pose Policy

Goal:

- remove low-value prone/crouch transitions before planted fire
- only allow a lower posture if the bot can actually shoot effectively from that posture

Reason:

- exposed crouch-fire often makes the follower easier to kill
- prone is currently too available at moderate distance and can stall responsiveness

#### C1. Prone Restriction

Proposed rule:

- no proning for planted fire unless enemy distance is `80m+`
- even at `80m+`, require a precheck that the chosen prone posture can still produce a real shooting lane

Important constraint:

- this cannot be a simple distance gate only
- the bot must determine whether the prone shot position is actually viable before committing to the pose

#### C2. Crouch Restriction

Proposed rule:

- add the same kind of precheck for crouching
- do not crouch before planted fire unless crouch still preserves a viable shot lane from the actual fire position

Important constraint:

- the precheck must evaluate the position the bot will actually fire from, not an abstract current-state guess
- otherwise the system will still choose a pose first and only discover after crouching that the shot lane is gone

### D. Fire-Position Precheck Investigation

This needs investigation before implementation.

Problem:

- to validate prone or crouch correctly, the code must know the actual firing origin or an acceptable approximation of it before the pose change is committed
- that is different from simply checking "enemy visible now"

Questions to answer first:

1. what pose-dependent origin should be treated as authoritative for standing, crouch, and prone checks on the core path?
2. can current EFT helpers already answer "can shoot from this pose here" without manually simulating each pose?
3. for cover-fire, should the precheck use committed cover shoot data rather than raw body-part visibility?
4. for exposed `shootFromPlace`, is current position plus pose offset sufficient, or does EFT shift actual firing enough that a stronger check is needed?

Likely code to inspect before implementation:

- `client/BigBrain/Actions/CombatShootFromPlaceAction.cs`
- `client/BigBrain/FollowerCombatCommon.cs`
- EFT cover / shoot-point helpers already used by `CanShootFromCurrentCoverOrStandingIntent(...)`
- any EFT pose or shoot-origin helpers that differ between standing, crouch, and prone

Recommended order:

1. investigate pose-dependent shoot viability checks
2. implement prone restriction with precheck
3. implement crouch restriction with precheck
4. then fold those results into the shared visible-fire posture helper from item A

### E. Ownership Boundary

Current recommendation:

- friendlySAIN should own:
  - movement versus planted-fire choice
  - posture choice
  - cover versus exposed trade choice
  - whether a tactical move is allowed
- EFT should continue owning:
  - actual firing cadence
  - burst length
  - semi/full-auto handling
  - recoil and hit-response weapon behavior

Reason:

- the current high-value problems are tactical and positional
- taking over fire-mode behavior now would broaden risk without first fixing the higher-level decision quality

## Guidance For Future Changes

- keep routing and lifecycle in objective/router code
- keep tactic policy in `FollowerCombatDefault` and `FollowerCombatSniper`
- keep shared primitives in `FollowerCombatCommon`
- do not add tactic-local state when the behavior is really shared cover/search/regroup infrastructure
- when documenting behavior, prefer current runtime rules over historical findings or future plans
