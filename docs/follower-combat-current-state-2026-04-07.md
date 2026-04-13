# Follower Combat: Current Runtime Behavior (2026-04-07)

This document describes how follower combat currently works in friendlySAIN across both runtime paths:

- Core/vanilla follower combat path (no SAIN addon combat layer)
- SAIN addon follower combat path (when SAIN + addon are both present)

## Runtime mode selection

Runtime flags in the client plugin define which combat path is active:

- Use SAIN follower combat when SAIN is installed and the addon is installed.
- Otherwise, followers use the core/vanilla BigBrain combat logic.
- If SAIN is installed but addon combat is unavailable, followers do not run SAIN follower combat takeover and remain on the core path.

## Layer ownership and lifecycle

### Core path

FollowerCombatLayer owns combat decisions for followers on the core path.

Activation constraints:

- Bot must be active and alive.
- Bot must be a follower with a valid player boss.
- SAIN follower combat path must be inactive.
- No pending regroup handoff that should transfer out of this layer.

Activity behavior:

- Layer treats combat as active if logic says active, or if there is a live visible/shootable goal enemy, or very recent under-fire signal.
- After enemy loss, layer keeps a short linger window (3s) using holdPosition with reason linger.

Lifecycle reset:

- On Start and Stop, combat logic Reset is called.
- This reset clears internal combat state, including escort state and failed-sector cache state.

### SAIN addon path

SAINFollowerCombatLayer owns follower squad combat behavior when active.

It maps squad decisions to follower actions:

- Regroup -> SAIN follower regroup/default boss action
- Suppress -> SAINFollowerCombatSuppressAction
- Search/Help -> SAIN search action
- GroupSearch -> leader search or boss-follow search action
- PushSuppressedEnemy -> SAIN rush action

If SAIN squad decision context disappears, layer exits back to other layers.

## Core combat decision flow (FollowerCombatDefault)

Decision order summary:

1. Pre-fight gates and opener decision come from shared common logic.
2. Core tree evaluates immediate smoke, short hold windows, push/want-kill branches.
3. Protector engagement branch can promote push-style actions.
4. Out-of-range reposition branch can move to a better boss-area shooting cover.
5. Style branch:
    - MoveForward style: biases into boss-position behavior.
    - HangBack style: biases hold/cover and controlled engagement.
6. If no branch resolves, decision falls back to boss-position handling.

## Escort behavior model (current)

Escort is now treated as a boss-sector anchored assignment instead of continuous enemy-drift optimization.

### Escort commit validity

Escort commit remains valid while:

- Same goal enemy id context remains.
- Boss has not moved beyond sector/anchor threshold (CombatAreaExitDistance).
- Committed escort cover is still free and within boss leash radius.

Escort commit is not invalidated by:

- Minor enemy anchor drift.
- Enemy angle drift relative to boss.
- Temporary shoot-lane wobble.

### Escort hold posture

When committed escort cover exists:

- Bot reuses the committed cover.
- If in cover, decision is holdPosition with reason escortHold.
- If not yet arrived, decision is runToCover or attackMoving (based on sprint availability).

### Failed-sector cache

When escort cover search fails for the current boss sector:

- Failed sector anchor is cached.
- Failed enemy id context is cached.
- Bot returns holdPosition with reason escortNoSafeCover.
- Rescan is skipped while boss remains in that same sector.

Rescan resumes when:

- Boss relocates out of the failed sector threshold, or
- Goal enemy id context changes.

This prevents repeated no-result rescans every short hold cycle.

## Out-of-range reposition behavior (new)

When enemy is out of direct push range, bot can reposition to a better boss-area cover from which enemy can be shot.

### Entry conditions

Reposition branch is only considered when all are true:

- No current shoot lane from current position (HaveCoverToShoot false).
- Reliable personal enemy location exists.
- Enemy distance is greater than push range threshold (60m baseline path).
- Bot is not critically wounded.
- Bot is not under fire and not recently hit.
- No active valid escort commit already in progress.

### Timer model

Reposition search is not continuous.

- Timer starts only while bot is in cover and not healing.
- Required wait before each search attempt: 3.0 seconds.
- Timer resets to inactive when:
    - Bot leaves cover.
    - Bot starts healing/medical action.
    - Bot is under fire/recently hit.
    - Bot gains a shoot lane.
    - Active escort commit is valid.
- After threshold is reached, one search attempt is made and timer resets regardless of success/failure.

### Candidate selection

Search space:

- Cover points within 60m around boss position.

Each candidate must pass:

1. Can shoot enemy from candidate cover.
2. Movement path to candidate is not exposed to enemy visibility.

If valid candidate exists:

- Bot moves via escort cover move decision (runToCover or attackMoving depending on sprint).

If none exists:

- Branch returns null and normal hold/escort logic remains in control.

## Path exposure check in Covers

Covers now includes a path exposure helper used by reposition logic.

Method behavior summary:

- Build complete NavMesh path from current bot position to candidate cover.
- Sample several evenly spaced points along path segments.
- For each sample, test enemy line-of-sight to sampled point using the provided mask.
- If enemy can see any sampled waypoint, path is considered exposed.
- Incomplete path is treated as exposed (fail closed).

## Hold and end-condition behavior relevant to waiting

- Stable no-cover waiting reason escortNoSafeCover is treated as a stable hold reason.
- While holding, normal break triggers still apply (enemy visible/shootable, under-fire pressure, boss-distance constraints, etc.).
- Linger hold after enemy clear is controlled by layer-level 3s post-enemy window.

## Practical summary

Current follower combat waiting behavior around boss is:

1. Try to keep a committed escort cover for current boss sector.
2. Once in that cover, hold posture until a real action trigger occurs.
3. If no escort cover exists for this sector, hold and do not rescan until sector changes.
4. If enemy is outside push range and follower has waited in-cover/not-healing for 3s, attempt a safe reposition to a boss-area shoot-capable cover.

This is the current intended balance between stability (no churn) and tactical adaptation.
