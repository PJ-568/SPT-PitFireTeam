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

# (IN PROGRESS) - Implement follower fight behavior for combat

Phase 1 plan (in testing):

- Build a new SAIN layer: `CombatFollowerLayer`, functionally similar to `CombatSquadLayer`.
- Keep SAIN baseline behavior where possible by reusing squad decisions that do not depend on SAIN squad leader/member context.
- For decisions that depend on SAIN squad leader (`Bot.Squad.SquadInfo?.LeaderComponent`) or squad members:
- Replicate decision logic and swap leader/member references to follower-player model:
- leader -> `BotFollower.BossToFollow`
- members -> `BotFollower.BossToFollow.Followers`
- Ensure this layer becomes active for recruited followers and SAIN default combat layers do not run for those followers while `CombatFollowerLayer` is active.
- Keep current SAIN `CombatSoloLayer`/`CombatSquadLayer` priorities in mind (`20` solo, `22` squad) and integrate follower layer with explicit activation/gating rules rather than ad-hoc decision overrides.

Phase 2 plan (on hold):

- Iterate and tune `CombatFollowerLayer` decisions from gameplay tests.
- Adjust/override specific decisions as needed for follower combat feel and reliability.
- Continue replacing squad-context-sensitive branches with boss/follower-aware variants when test results show mismatch.

# (NOT STARTED) - Implement follower run-ahead feature

Goal:

- Add a Dogmeat-style run-ahead behavior for followers while following the player.

Behavior target:

- When a follower lags behind, it should path/sprint to a projected point ahead/near the player movement direction (not exact feet-follow).
- When close enough, follower should settle back into normal local follow/idle behavior.
- Add fallback handling for path failure or excessive separation (safe catch-up/teleport logic as needed).
- Keep this compatible with both vanilla and SAIN runtime paths.

# (NOT STARTED) - IMPROVE PingTeams directional enemy callout

Improvement target:

- On ping command, if any follower has enemy info, the closest follower to the boss should say the enemy direction phrase.
- Direction phrase must be computed relative to the boss look direction, not the speaking follower look direction.

Sub-task:

- SAIN also has direction-speaking paths for bots; investigate SAIN code first to identify those call sites.
- Current SAIN direction phrases are based on bot look direction; patch/override them so follower direction callouts are relative to boss player look direction.
