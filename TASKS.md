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

# (NOT STARTED) - Implement follower fight behavior for combat

Reference behavior from EFT 4.x (PMCBear/PMCUSEC follower combat):

- `pmcBEAR` uses `GClass348` and `pmcUSEC` uses `GClass350` in `StandartBotBrain`.
- Their combat layers are `GClass141` (`PmcBear`) and `GClass145` (`PmcUsec`).
- These layers are follower-aware in combat (`ShallUseNow`: has enemy + have boss OR is boss).
- Boss anchor is dynamic and re-read every check via `BotFollower.BossToFollow.Position`.
- Cover search is centered around the boss position (`CoverSearchType.closerToSelectedPoint` with selected point = boss).
- Candidate shoot cover is rejected if too far from boss (`Boss.MAX_DIST_COVER_BOSS_SQRT`).
- Cover/position evaluation is refreshed about every 1 second (`Float_8 = Time.time + 1f`).
- End checks force re-decision/reposition when bot drifts too far from boss:
- `EndHoldPosition`: end when current position exceeds close-to-boss distance.
- `EndGoToPoint`: end when destination is too far from boss or goal reached.
- Reposition logic picks nearby cover/nav points around boss and returns `runToCover` / `attackMoving` / `goToPoint`.
- Net result: bot can take combat actions, but repeatedly snaps back into a boss-centered envelope ("leash").

For friendlySAIN:

- We can "prefixpatch "getdecision" of sain to give the bot a different decision based on conditions similar to vanilla
