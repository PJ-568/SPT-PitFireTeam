# Combat Tactics Notes

## Scope

- This document covers the core follower combat path under `client/BigBrain`.
- It does not describe the optional SAIN addon combat path.
- Treat this as current runtime documentation, not a backlog.

## Runtime Ownership

Core combat is objective-routed.

- `FollowerCombatLogicBase` owns objective selection and command handoff.
- Default/balanced combat is implemented by `FollowerCombatDefaultObjective` plus `FollowerCombatDefault`.
- Marksman combat is implemented by `FollowerCombatSniperObjective` plus `FollowerCombatSniper`.
- Combat regroup is implemented by `FollowerCombatRegroupObjective`.
- Shared state and primitives live in `FollowerCombatCommon`.
- Push selection and push commitment live in `FollowerCombatPush`.

Objective ownership matters: heal, dogfight, grenade, and immediate fire can interrupt the current objective, but they do not automatically change the owning objective. Explicit combat regroup activates the regroup objective. Explicit push returns control to the active tactic's primary objective.

## Shared Commitment Model

The new stabilization model is based on commitments. A commitment means "keep doing this unless a real interrupt happens", not "ignore combat".

Commitment types:

- `initialDecision`: one-shot opener prepared when combat starts.
- `committedGrenadeDecision`: active grenade throw sequence; stays latched until throw completes or is canceled for immediate danger.
- `committedPushDecision`: push/search pressure chosen by `FollowerCombatPush`.
- `committedMovementDecision`: non-push movement chosen by a tactic.
- `committedCoverPoint`: selected cover point reused across movement and cover-fire checks.
- `committedPositionDecision`: arrival hold after reaching cover or a point.
- `committedHealCover`: selected safe/heal cover reused until healing can start or becomes invalid.

Shared rule: movement and cover commitments stabilize travel; arrival holders stabilize the moment after travel. Tactics decide which tactical reasons are allowed to break those commitments.

## Default Router Order

Default/balanced combat currently routes in this order:

1. Refresh shooting-cover state and validate committed cover.
2. Immediate fight: dogfight and direct in-fight shooting, unless damage pressure should defer exposed fire into recovery.
3. Continue committed grenade throw.
4. Consume combat-start `initialDecision`.
5. Medical decision: heal, stim, or move to heal cover.
6. Recovery decision when exposed, under fire, or recently damaged.
7. Boss-distance regroup precheck, deferred while protected commitments are active.
8. Continue committed arrival hold.
9. Continue committed push.
10. Continue committed non-push movement.
11. Continue committed cover movement/fire setup.
12. Boss-distance regroup after commitments are checked.
13. Low-aggression regroup.
14. Boss-under-attack protection.
15. Ally support, but only if a valid support decision can be prepared.
16. New grenade activation.
17. Ordered push.
18. Visible enemy handling.
19. Generic advance/push.
20. Cover hold, fallback shoot, suppress, or boss hold.

Important policy:

- Boss-distance regroup does not break active push/help/heal-style commitments.
- Explicit regroup breaks push; explicit push does not break an already running push.
- Ally support does not break a hold unless the support decision can be prepared first.
- Hold breakers prepare the next action when possible, so the router does not break into a branch that cannot actually execute.

## Push Model

`FollowerCombatPush` owns push decisions and push commitment lifecycle.

Default and marksman can ask push code for a pressure plan, but they still keep tactic-specific policy:

- Default push means assault pressure toward the enemy, an approach cover, or a search point.
- Marksman push support means finding a better shooting/support position, not rushing like default.
- Against marksman enemies, push does not manufacture a fake hold if no valid firing-position push exists. It returns no push decision and lets the tactic continue routing.

Committed push breaks for:

- missing/dead enemy that cannot be restored from retention
- enemy identity change
- stable visible/shootable contact when the push action cannot safely keep moving and firing
- under-fire/recent-hit pressure
- explicit non-push command override
- run-to-enemy when sprint is impossible or the bot is not actually sprinting

Push enemy retention is refreshed during committed push so a contact/order push does not forget the enemy mid-route.

## Movement And Arrival Holds

Non-push movement commitments are shared in `FollowerCombatCommon`.

Movement commitment is used for travel actions such as:

- `runToCover`
- `goToPoint`
- `goToPointTactical`
- `attackMoving`
- `attackMovingWithSuppress`
- `attackRetreat`
- `runToEnemy` / `goToEnemy` when not owned by push

Movement breaks for:

- under fire or recent hit
- visible shootable contact
- close visible contact
- boss-under-attack when the movement is not protected by active push/help policy
- arrival at the destination
- sprint impossibility for `runToEnemy`

When a movement action reaches cover or a point, common end logic arms a committed holder. The holder returns `holdPosition` for a short think window so the bot does not immediately reselect the same cover or churn into another travel action.

Arrival holders break for:

- explicit push order
- under fire or recent hit
- settled boss-under-attack support
- settled ally support when a valid support action can be prepared
- real enemy contact
- boss-distance regroup only when the hold reason is not protected
- timer expiry

## Cover Contract

Cover is a shared lifecycle, not tactic-local geometry.

Cover lifecycle:

1. Select a candidate cover.
2. Commit that cover.
3. Move to the committed cover.
4. Detect arrival using EFT cover state, committed-cover proximity, or go-to-point arrival.
5. Arm an arrival hold.
6. During hold, scan for immediate fire, damage pressure, support, protection, and regroup.
7. Keep holding if nothing stronger wins.

Cover intent matters:

- Shooting cover can stand up if a standing-height shot lane exists.
- Safe/retreat cover should not force standing fire unless a real lane is verified.
- Cover move actions should not keep running after proximity/arrival says the destination was reached.

## Grenades

Grenade use is now an explicit committed branch.

New grenade activation is checked after support/protection and before push. Once a grenade starts, the committed grenade decision moves near the top of routing so out-of-bounds, push, or cover replanning cannot cancel it accidentally.

Grenade activation requires:

- grenade config enabled
- visible enemy with a valid person
- distance roughly between 15m and 28m
- no active bot request or medical use
- safe throw position
- cooldown reservation
- not dogfighting, under fire, recently hit, or in a fresh first-seen window
- not already in a clean gunfight
- boss/followers not too close to the target

Committed grenade can still cancel if the throw becomes unsafe before release, combat enemy disappears before release, the grenade controller disappears, or the sequence times out.

## Healing And Stims

Medical decisions are shared in `FollowerCombatCommon`.

Current behavior:

- Emergency healing can move to committed heal cover.
- Heal cover is reused instead of repeatedly selecting new cover.
- Heal action and stim action have timeout exits.
- Black limb pain during combat can prefer painkiller/stim use when available.
- Badly injured state can use health-support stims when available.
- Heal completion refreshes movement penalties without restoring full max health.
- When retreating to heal but sprint/mobility is poor, recent contact can choose visible fire or suppression instead of walking with no pressure.

Heal-cover exception: arriving at heal cover hands off to healing instead of normal cover hold.

## Default Combat Behavior

Default/balanced tactic is built around two main objectives:

- stay in useful cover near the boss when there is no good attack opportunity
- push/search/pressure when enemy state and aggression justify it

Default situational behavior:

- under damage pressure, prefer recovery or suppressive retreat over exposed standing fire
- if visible and shootable, shoot immediately unless recovery pressure should own the branch
- if visible but not shootable, prefer firing cover or pressure movement
- if enemy is unseen but recent enough, push/search can continue using retained enemy memory
- if too far from boss and not protected by push/help/heal, switch to regroup objective
- if boss is attacked, prepare protection/support before breaking passive holds
- if ally is engaged, prepare support before breaking passive holds

## Marksman Combat Behavior

Marksman is not default with different numbers. It has separate policy but shares common commitments.

Marksman behavior:

- prefers firing positions and support cover
- uses committed movement and arrival holds like default
- uses prepared-break decisions so support/protection only breaks hold when the next action is valid
- ignores generic assault push behavior unless marksman policy asks for close support/search
- can switch to automatic secondary for close-quarter fights
- can switch back to primary when returning to real marksman decisions
- supports team push/search through firing-position support, not blind rushing
- hands boss-distance regroup to the shared regroup objective

Marksman hold behavior scans periodically for:

- better shooting spot
- push support
- boss-under-attack support
- ally support
- boss-distance regroup
- timeout

## Regroup Objective

Combat regroup is objective-owned.

Regroup behavior:

- explicit combat regroup activates the regroup objective
- explicit push can leave regroup and return to the active tactic objective
- hot contact regroup stays combat-active and moves bossward using withdraw-style movement
- cooled contact regroup runs directly toward boss / sampled boss position
- regroup may use bossward cover, but reaching that cover is only an intermediate step
- regroup completion is based on reaching the boss/bossward objective

Regroup movement also has its own short commitment so the bot does not recalculate bossward movement every tick.

## Battle Recorder

The battle recorder is separate from normal plugin logging.

It records combat-only JSONL timelines under:

`E:\SPTarkov\BepInEx\plugins\friendlySAIN\BattleRecords`

It is intended to compare observed behavior with code behavior:

- decision changes
- action phases
- movement and aim/look state
- enemy/boss positions
- visibility and shootability
- grenades
- health/healing state
- churn and bad transition clusters

Use it to validate whether a bug is tactical routing, action execution, perception, or visual/player interpretation.

## Implementation Boundaries

Keep these boundaries stable:

- `FollowerCombatLogicBase`: objective routing and command handoff.
- `FollowerCombatDefault`: default tactic policy and default-only break choices.
- `FollowerCombatSniper`: marksman tactic policy and marksman-only break choices.
- `FollowerCombatPush`: push decision setup and committed push lifecycle.
- `FollowerCombatCommon`: shared commitments, cover, heal, grenade, visibility, support helpers, and common end conditions.
- Actions under `client/BigBrain/Actions`: execute a chosen decision; avoid moving planning policy here unless the action owns a runtime destination refresh.

## Future Guidance

- Do not add tactic-local state when the behavior is a shared commitment primitive.
- Do not break holds just because an opportunity might exist; prepare the next decision first.
- Do not emit movement decisions without a valid destination already assigned.
- Do not let boss-distance regroup interrupt push/help/heal commitments.
- Do not let `Enemy.IsVisible()` alone break push instantly; use stable visible plus shootable/contact gates.
- Prefer small, explicit interrupt reasons so battle records can explain churn.
- Keep comments focused on why a branch exists, not what the next line literally does.
