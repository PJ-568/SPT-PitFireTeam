# Combat Tactics Notes

Last updated: 2026-05-03

## Scope

- This document covers the core follower combat path under `client/BigBrain`.
- It only mentions the optional SAIN addon combat path where a boss command crosses the core/addon boundary.
- Treat this as current runtime documentation, not a backlog.

## Runtime Ownership

Core combat is objective-routed.

- `FollowerCombatLogicBase` owns objective selection and command handoff.
- Rifleman/default combat is implemented by `FollowerCombatDefaultObjective` plus `FollowerCombatDefault`.
- Marksman combat is implemented by `FollowerCombatSniperObjective` plus `FollowerCombatSniper`.
- Combat regroup is implemented by `FollowerCombatRegroupObjective`.
- Ordered suppression is implemented by `FollowerCombatSuppressionObjective`.
- Ordered marksman support is implemented by `FollowerCombatNeedSniperObjective`.
- Shared state and primitives live in `FollowerCombatCommon`.
- Push selection and push commitment live in `FollowerCombatPush`.

Objective ownership matters: heal, dogfight, grenade, and immediate fire can interrupt the current objective, but they do not automatically change the owning objective. Explicit combat regroup activates the regroup objective. Explicit suppression activates the suppression objective for Rifleman/default followers. Explicit Need Sniper activates the marksman support objective for Marksman followers. Explicit push returns control to the active tactic's primary objective and lets that tactic build a push plan.

## Boss Combat Commands

Combat command state lives on `BotFollowerPlayer` and is intentionally separate from persisted profile settings.

- `EPhraseTrigger.HoldPosition` applies a temporary `0%` effective combat-aggression override in combat.
- `EPhraseTrigger.Gogogo` clears that temporary override and returns each follower to its saved aggression.
- `EPhraseTrigger.GoForward` becomes `PushEnemy` when the follower has an active combat enemy.
- `EPhraseTrigger.Suppress` becomes `SuppressEnemy` for Rifleman/default combat.
- `EPhraseTrigger.NeedSniper` becomes `NeedSniper` for Marksman combat.
- `EPhraseTrigger.NeedHelp` fakes a boss-under-attack event against the closest valid enemy.
- Core combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`.
- Tactic changes reset saved aggression to that tactic's default: Rifleman uses `50%`, Marksman uses `30%`.
- The override is cleared when the follower is safely out of combat and patrol can resume.
- `BotReceiverPhraseOverridePatch` suppresses vanilla follower receiver handling for `Stop`, `HoldPosition`, and `Gogogo`, so `AIBossPlayer` owns these commands.

SAIN addon note:

- `SAINFollowerCombatLayer` treats the temporary HoldPosition aggression override as boss-protection/regroup intent.
- This keeps the command behavior aligned with core `0%` aggression even though SAIN addon decisions do not use `FollowerCombatCommon`.

### Hold Position

Combat `HoldPosition` is not a normal movement hold. It temporarily sets effective aggression to `0%`.

Expected behavior:

- Rifleman suppresses proactive push/search pressure.
- Marksman suppresses proactive automatic close-search/auto-search.
- Defensive behavior still works: immediate fire, dogfight, heal, boss protection, and survival can still interrupt.
- The saved aggression value is not changed.
- `Gogogo` clears the override immediately.
- The override also clears after combat when the follower is safe to return to patrol.

Out of combat, `HoldPosition` is handled by the request layer as a normal hold command and can crouch/hold in place depending on how the command was issued.

### Go Forward / Push Enemy

Combat `GoForward` becomes `PushEnemy` if the follower already has an active enemy.

Rifleman/default behavior:

- The command returns control to the primary Rifleman objective.
- Ordered push first tries to build a committed firing-position move using the enemy's current body position.
- If no firing position exists, it falls back to `FollowerCombatPush.EngageEnemy(true, ...)`.
- The selected push is committed as `push.*`, so the bot should finish the chosen push phase unless interrupted by real danger, enemy loss, enemy change, or another explicit command.
- Ordered push refreshes enemy retention during the committed push so it should not forget the enemy mid-route.
- If the enemy becomes visible/shootable during direct movement, the push can convert into immediate fire or short suppression rather than churn into unrelated movement.
- Other nearby Riflemen can react to the push event as support instead of starting a duplicate independent push: shoot from current cover, take a support shot, move to push-support cover, or move to a firing point.

Marksman behavior:

- Generic `GoForward`/push is not a marksman assault order.
- Marksman supports team push/search by finding a shooting position or support cover.
- If close automatic support is allowed by marksman aggression and safety checks, marksman can use automatic secondary behavior.
- If a nearby Rifleman can reasonably take the push, marksman should prefer support/reposition instead of becoming the primary pusher.

### Suppression Order

Combat `Suppress` becomes `SuppressEnemy` and is consumed by `FollowerCombatSuppressionObjective` for Rifleman/default followers.

Objective behavior:

- The command is consumed into an objective, like regroup, so it is not polled inside the normal Default decision tree.
- It does not interrupt active healing or an already active fight action; it waits until that action's normal end logic allows a switch.
- It targets the current enemy's best known shoot/suppress point.
- If the bot already has a safe lane, it suppresses from place.
- If direct fire is blocked but suppression was explicitly ordered, it may still suppress the obstructed known point, as long as friendly shot safety passes.
- If a better suppress-from point exists, the action can move there and suppress from that point.
- The objective has an ordered-suppression protected window so one short obstruction or controller completion does not instantly cancel it.
- It completes when suppression completes, target data disappears, friendly shot safety blocks the lane beyond the protected window, or the enemy is gone.

Autonomous suppression remains separate:

- Default can still choose `autoSuppress.*` as a normal tactical branch.
- Autonomous suppression is shorter and more conservative than ordered suppression.

### Need Sniper Order

`NeedSniper` is a marksman-only combat objective implemented by `FollowerCombatNeedSniperObjective`.

Objective behavior:

- The command is consumed by `FollowerSniperCombatLogic`, not by `FollowerCombatSniper`'s normal autonomous router.
- It does not pollute normal marksman support/reposition branches.
- The sniper rejects or ignores the order when self-preservation must win: active/pending heal work, under fire, recent hit, or point-blank visible shootable threat.
- If accepted, it tries immediate fire first.
- If current cover can shoot, it shoots from cover.
- Otherwise it finds a firing cover or firing position using the enemy's current position when possible.
- It is allowed to use closer/forward/lateral firing positions than autonomous marksman support, because this is an explicit player support request.
- On arrival, it arms a short committed `sniper.NeedSniper.positionHold` settle window before reassessing.
- It completes when it reaches a valid shot, settles without a lane, the enemy disappears, or a stronger interruption takes over.

### Need Help Order

`NeedHelp` is not a normal follower command and does not activate its own combat objective. It is a boss-side emergency support signal handled in `AIBossPlayer`.

Runtime behavior:

- The boss selects the closest valid enemy relative to the boss/player.
- Candidate enemies are gathered from boss-tracked enemies, boss-group enemies, visible contact enemies, and SAIN contact fallback enemies when SAIN is installed.
- Invalid candidates are ignored: dead/inactive bots, the player, followers, and friendly bot roles.
- The boss logic is marked as manually under attack by that enemy.
- Each active follower is told to prioritize that enemy through `PrioritizeEnemy(...)`, which either promotes an existing `EnemyInfo` or adds the enemy to follower memory.
- The normal combat router then reacts through existing boss-under-attack support/protection logic.

Expected behavior:

- Rifleman/default followers can break passive hold into boss protection/support if a valid protection decision can be prepared.
- Marksman followers can react through their boss-under-attack/support scan, unless self-preservation, healing, or a stronger current fight wins.
- Because this is a fake boss-under-attack signal, it intentionally reuses existing protection/support decision rules instead of creating a separate "NeedHelp" movement mode.
- If no valid enemy can be found, the phrase does nothing and does not disturb current follower decisions.

## Shared Commitment Model

The new stabilization model is based on commitments. A commitment means "keep doing this unless a real interrupt happens", not "ignore combat".

Commitment types:

- `initialDecision`: one-shot opener prepared when combat starts.
- `committedGrenadeDecision`: active grenade throw sequence; stays latched until throw completes or is canceled for immediate danger.
- `suppressionObjective`: ordered Rifleman suppression state; owns ordered suppress-fire setup and completion.
- `needSniperObjective`: ordered Marksman support state; owns ordered firing-position search and settle.
- `committedPushDecision`: push/search pressure chosen by `FollowerCombatPush`.
- `committedMovementDecision`: non-push movement chosen by a tactic.
- `committedCoverPoint`: selected cover point reused across movement and cover-fire checks.
- `committedPositionDecision`: arrival hold after reaching cover or a point.
- `committedHealCover`: selected safe/heal cover reused until healing can start or becomes invalid.

Shared rule: movement and cover commitments stabilize travel; arrival holders stabilize the moment after travel. Tactics decide which tactical reasons are allowed to break those commitments.

## Rifleman Router Order

Rifleman/default combat currently routes in this order:

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
13. Low-aggression regroup, including temporary boss `HoldPosition` combat override.
14. Boss-under-attack protection.
15. Ally support, but only if a valid support decision can be prepared.
16. Autonomous suppression.
17. New grenade activation.
18. Ordered push.
19. Visible enemy handling.
20. Generic advance/push.
21. Cover hold, fallback shoot, suppress, or boss hold.

Explicit command objectives sit above this tactic router in `FollowerCombatLogicBase`:

- explicit regroup can switch to regroup objective
- explicit Rifleman suppression can switch to suppression objective
- explicit Need Sniper can switch to NeedSniper objective for Marksman
- explicit push returns from regroup/suppression/NeedSniper to the primary tactic objective
- NeedHelp does not switch objectives directly; it marks boss-under-attack and lets each active tactic's protection/support branch respond.

Important policy:

- Boss-distance regroup does not break active push/help/heal-style commitments.
- Explicit regroup breaks push; explicit push does not break an already running push.
- Explicit suppression is objective-owned and does not live as a Default-local router branch.
- Ally support does not break a hold unless the support decision can be prepared first.
- Hold breakers prepare the next action when possible, so the router does not break into a branch that cannot actually execute.

## Push Model

`FollowerCombatPush` owns push decisions and push commitment lifecycle.

Default and marksman can ask push code for a pressure plan, but they still keep tactic-specific policy:

- Default push means assault pressure toward the enemy, an approach cover, or a search point.
- Marksman push support means finding a better shooting/support position, not rushing like default.
- Rifleman push support uses the same push event but keeps Rifleman policy: eligible nearby helpers support from cover or a nearby firing point and avoid duplicating the active pusher's destination.
- Push events are globally locked: one emitter owns the active push event, helpers cannot emit their own push while that event is active, and a short cooldown prevents emit/release/emit chains.
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

## Rifleman Combat Behavior

Rifleman/default tactic is built around two main objectives:

- stay in useful cover near the boss when there is no good attack opportunity
- push/search/pressure when enemy state and aggression justify it

Rifleman aggression:

- `50%` is the default balanced baseline.
- Lower values bias toward boss-local cover, support, and regroup.
- `0%` suppresses proactive push/search pressure and is also the temporary behavior used by the combat `HoldPosition` command.
- Higher values allow farther and more frequent push/search/pressure decisions when threat checks allow them.

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

Marksman aggression is tactic-relative:

- `30%` is the default marksman baseline.
- `0%` blocks proactive automatic close-search/auto-search behavior, but does not block defensive automatic secondary use in close-quarter danger.
- Around `50%` keeps the current marksman support/reposition style but allows more willing close automatic support when conditions are safe.
- Higher values can make marksman use automatic-weapon offensive search more like Rifleman in distance and threat tolerance.
- Aggression affects proactive marksman automatic search/pressure only; normal marksman firing-position selection, support cover, and defensive secondary switching remain marksman policy.

Marksman behavior:

- prefers firing positions and support cover
- uses committed movement and arrival holds like default
- uses prepared-break decisions so support/protection only breaks hold when the next action is valid
- ignores generic assault push behavior unless marksman policy asks for close support/search
- can switch to automatic secondary for close-quarter fights
- does not run proactive automatic close-search while temporary boss `HoldPosition` aggression override is active
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

`E:\SPTarkov\BepInEx\plugins\pitFireTeam\BattleRecords`

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
