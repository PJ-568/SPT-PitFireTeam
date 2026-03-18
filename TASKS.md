# (NOT STARTED) - Implement Enemy Push command

Old Plugin Push Flow:

- Triggered by `EPhraseTrigger.GoForward` in `FollowerReceiver`.
- If follower has enemy:
- Creates `FollowerRushEnemy` request (`BotRequestType.attackClose`) with ~4s timeout.
- Main behavior is decided by `FollowerFightLayer` + `FollowerPusherLayer.EngageEnemy(pushOrdered: true)`.
- Push is gated, not blind:
- Ordered push only if enemies at location `< 4`.
- Non-ordered/aggressive push generally needs close range and enemies at location `< 2`.
- Enemy density is computed with `Utils.Enemy.GetEnemiesAtLocation(...)`.
- If unsafe/not favorable, logic falls back to cover/search/attack-moving decisions instead of hard rush.
- `IsEnemyLowThreat()` gates default push tendency using `Memory.AttackImmediately` + local enemy count threshold.
- No explicit plugin-level direct gear-score comparison found; “weaker enemy” behavior is effectively mediated via `AttackImmediately` and enemy-count pressure.
- Marksman/assist-style tactics suppress push behavior.
- If no enemy on command, command path uses `FollowerGoCheck` (move/check forward) instead of rush.

New Plugin Push Flow:

Very similar to old flow, but with some adjustments:

- Triggered by `EPhraseTrigger.GoForward` but in the "transmision flow" is in the matter the others are in friendlySAIN (like EPhraseTrigger.OnRepeatedContact or EPhraseTrigger.Regroup)
- When out of combat `EPhraseTrigger.GoForward` acts like ` EInteraction.ThereGesture` except it will be for all nearby followers instead of just the closest one.

# (DONE) - Implement follower fight behavior for combat

Phase 1 plan (finished):

- Build a new SAIN layer: `CombatFollowerLayer`, functionally similar to `CombatSquadLayer`.
- Keep SAIN baseline behavior where possible by reusing squad decisions that do not depend on SAIN squad leader/member context.
- For decisions that depend on SAIN squad leader (`Bot.Squad.SquadInfo?.LeaderComponent`) or squad members:
- Replicate decision logic and swap leader/member references to follower-player model:
- leader -> `BotFollower.BossToFollow`
- members -> `BotFollower.BossToFollow.Followers`
- Ensure this layer becomes active for recruited followers and SAIN default combat layers do not run for those followers while `CombatFollowerLayer` is active.
- Keep current SAIN `CombatSoloLayer`/`CombatSquadLayer` priorities in mind (`20` solo, `22` squad) and integrate follower layer with explicit activation/gating rules rather than ad-hoc decision overrides.

Phase 2 plan (finished):

- Iterate and tune `CombatFollowerLayer` decisions from gameplay tests.
- Adjust/override specific decisions as needed for follower combat feel and reliability.
- Continue replacing squad-context-sensitive branches with boss/follower-aware variants when test results show mismatch.
- x - Implement enemy push behavior when target is close enough and weak enough.
- Determine enemy weakness using `IsEnemyLowThreat()` behavior from the old plugin.
- Enemy push implementation in this phase must work with and without SAIN runtime.
- For non-SAIN runtime, complete this in Phase 3 by replicating the relevant vanilla PMC combat layer as a BigBrain layer to gain full control.

# (NOT STARTED) - Combat commands

Description:

- Add support for boss commands during combat such as `GoForward` (Push), `Suppress`, `On your own` (stop regrouping near the boss), `Regroup` (cancel on your own), and other combat commands supported by the old plugin.

# (NOT STARTED) - Implement follower run-ahead feature

Goal:

- Add a Dogmeat-style run-ahead behavior for followers while following the player.

Behavior target:

- When a follower lags behind, it should path/sprint to a projected point ahead/near the player movement direction (not exact feet-follow).
- When close enough, follower should settle back into normal local follow/idle behavior.
- Add fallback handling for path failure or excessive separation (safe catch-up/teleport logic as needed).
- Keep this compatible with both vanilla and SAIN runtime paths.

# (IN PROGRESS) - Implemnent follower spawn

The old plugin has an entire Back End system (in node js) to allow for adding custom followers via a terminal command and then they would spawn with the player. Investigate the old plugin and create a robust pland for implementing the BE service so we can have add and spawning behavior. Added followers appeared in the friends list and player would add them by right-click and selecting "add to group". You will need to investigate the client + the old Front End plugin for this. You could also view the follower and customize it. In Phase 1 we do not enable customization, just viewing the follower. Instead of a terminal command to add followers, we will have an "add teamate" button in the friends list. You will investigate the friends list component on FE to figure out how to properly add the buttom. Ask me for screenshots of the friends list to help.
