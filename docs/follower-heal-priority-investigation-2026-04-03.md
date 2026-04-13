# Follower Heal Priority Investigation (2026-04-03)

## Context

Observed in raid: a follower started healing a blacked stomach while combat was still active and enemies were close.

Goal of this investigation: verify current behavior and identify decision points for changing heal priority (combat-time triage) without making code changes yet.

## Current Behavior Summary

1. Both first-aid and surgical systems use a fixed body-part scan order.
2. Stomach is evaluated before arms and legs in that order.
3. Patrol layer will enter heal action whenever there is any pending first-aid or surgical work.
4. Combat layer can also select heal while in cover, based on first-aid state and recent enemy-seen timing.

Result: blacked stomach can trigger heal at times where movement-critical parts (legs) should arguably be prioritized for combat survival.

## Verified Technical Details

### 1) Body part priority order in vanilla bot medicine base class

From decompiled 4.x client (`GClass485` constructor), body-part order is:

- Head
- Chest
- Stomach
- LeftArm
- RightArm
- LeftLeg
- RightLeg

This order is used by both:

- `BotFirstAidClass.FindDamagedPart()` (low HP parts)
- `GClass489.FindDamagedPart()` (surgical/destroyed part checks)

Implication: stomach is naturally picked before legs when multiple parts are candidates.

### 2) Patrol layer heal trigger (friendlySAIN)

In [client/BigBrain/FollowerPatrolLayer.cs](client/BigBrain/FollowerPatrolLayer.cs#L219):

- `isUsingHeal = FirstAid.Using || SurgicalKit.Using`
- `hasPendingHealWork = FirstAid.Have2Do || SurgicalKit.HaveWork`
- If either is true -> `HealAction`

There is no combat-aware body-part triage at this decision point.

### 3) Combat layer heal trigger (friendlySAIN)

In [client/BigBrain/FollowerCombatLayer.cs](client/BigBrain/FollowerCombatLayer.cs#L712):

- Heal can be selected if bot is in cover and `FirstAid.Have2Do` is true
- Additional guard uses `LastEnemy` null/seen-time logic (`PROTECT_DELTA_HEAL_SEC`)

Important nuance:

- If bot has no personal last enemy reference, this branch can still allow healing even when combat context is still active nearby.

## Likely Explanation For The Observed Raid Case

Most probable flow:

1. Bot had no personal goal enemy at that moment (or lost it).
2. Combat layer was not driving behavior at that instant.
3. Patrol layer saw pending surgical work from blacked stomach.
4. Patrol selected `HealAction` immediately.

This matches the reported behavior where enemies were still close but the bot started healing stomach anyway.

## Decision Direction (Design, Not Implemented)

Recommended combat-time triage policy:

Urgent in combat (allow healing):

- Bleeding (light/heavy)
- Legs damaged/destroyed (movement/sprint impact)
- Optional: arms if your design treats aim stability as immediate-critical

Deferrable until post-combat:

- Stomach-only damage/destroyed (unless no immediate threat)
- Non-critical arm damage (if not configured as urgent)

## Candidate Implementation Points (for later)

1. Add a shared urgency evaluator used by both layers.
2. In patrol layer, gate heal-start by urgency when enemy threat is active/recent.
3. In combat layer in-cover heal branch, apply same urgency gate.
4. Keep existing safety timeout behavior in `HealAction` path.

## Key Files

- [client/BigBrain/FollowerPatrolLayer.cs](client/BigBrain/FollowerPatrolLayer.cs)
- [client/BigBrain/FollowerCombatLayer.cs](client/BigBrain/FollowerCombatLayer.cs)
- Decompiled reference: `BotFirstAidClass`, `GClass489`, `GClass485` in Client-Decompiled-4.x

## Status

Investigation complete. No runtime code changes made in this step.
