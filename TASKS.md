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

# (NOT STARTED) - Combat commands

Description:

- Add support for boss commands during combat such as `GoForward` (Push), `Suppress`, `On your own` (stop regrouping near the boss), `Regroup` (cancel on your own), and other combat commands supported by the old plugin.

# (NOT STARTED) - Combat Tactics

Description:

- Implement combat tactics such as Marksman, Default, Holder, and Pusher that can be assigned to followers and influence their combat behavior in ways similar to the old plugin's tactics system.

# (NOT STARTED) - Surround combat decision

Description:

- Reuse SAIN `ESquadDecision.Surround` for followers.
- One bot starts the surround sequence, picks a valid cover spot first, then signals the others to allocate in sequence rather than all at once.
- Each bot should try to take a different direction around the enemy (`front`, `back`, `left`, `right`) from a cover point it can shoot from.
- If the ideal direction is not available, fallback to another direction is allowed, including duplicate directions, but not the same exact spot.
- Candidate spots must be validated by navigation path distance, not straight-line distance alone. A geometrically near point with a long path detour is not a valid surround point.
- If a bot in sequence cannot find any acceptable cover point, surround allocation ends there:
- bots that already got assignments continue to them
- remaining bots fall back to boss default/regroup behavior
- Surround must be interruption-safe because combat decisions can break at any time due to visibility, hits, enemy death, bot death, self-actions, or other SAIN combat changes.

# (NOT STARTED) - Implement follower run-ahead feature

Goal:

- Add a Dogmeat-style run-ahead behavior for followers while following the player.

Behavior target:

- When a follower lags behind, it should path/sprint to a projected point ahead/near the player movement direction (not exact feet-follow).
- When close enough, follower should settle back into normal local follow/idle behavior.
- Add fallback handling for path failure or excessive separation (safe catch-up/teleport logic as needed).
- Keep this compatible with both vanilla and SAIN runtime paths.

# (NOT STARTED) - Add directional quick-phrase look commands

Goal:

- Enable directional quick phrases such as `Front`, `Left`, `Right`, and similar directional callouts so followers orient and look toward the called direction in the same manner they currently react to `Over There`.

Behavior target:

- Direction resolution must be relative to the player look direction at the time the phrase is issued.
- Followers should translate the directional phrase into an attention/look target using the same general flow currently used by `Over There`.
- Keep this behavior consistent with existing boss-to-follower gesture/phrase propagation rules.
- Avoid inventing a separate movement/command model if the existing `Over There` attention path can be reused safely.
- Keep the implementation compatible with both vanilla and SAIN runtime paths.

# (IN PROGRESS) - Team management

Goal:

- Replace the old terminal/chatbot-heavy squad management flow with a proper FE + BE teammate management flow that still preserves the core old-plugin experience:
    - add teammate from social UI
    - customize/view teammate from profile screen
    - invite teammate to group
    - see player + teammates on the pre-raid ready and loading screens
    - spawn saved teammate bot in raid

Implemented so far:

- Friends list:
    - localized `Add Teammate` entry is injected into the friends list
    - button opens teammate creation flow
- Creation flow:
    - uses stock appearance UI
    - player side is forced automatically
    - collects nickname, head, and voice
    - posts data to BE
- Backend:
    - creates a same-side PMC bot profile
    - overwrites nickname, voice, and head
    - saves teammate as mod-owned JSON under `friendlySAIN-ServerMod`
    - exposes teammate social/profile/delete routes
    - exposes legacy-compatible `/client/game/bot/followergenerate` and `/client/game/bot/followerdetails`
    - stores generated default equipment snapshot separately so `Default` can restore the original generated kit
    - uses stock-style server account-id generation for teammate `aid` allocation instead of custom max-id logic
- Social/profile:
    - teammate appears in friends list
    - teammate can be viewed from profile
    - teammate can be deleted from friends list
    - friends list refreshes after teammate create
- Profile customization:
    - hideout/report hidden
    - clothes dropdowns active
    - loadout dropdown active and persisted
    - rename teammate overlay works from profile view and persists to backend
- Grouping/runtime:
    - teammate can be invited to group
    - teammate can appear on ready screen and loading screen
    - teammate can spawn in raid from saved backend profile
    - local/offline raid guard exists and has been adjusted to preserve solo flow
    - insurance screen must still appear before the custom teammate ready screen
    - 4.x invite popup now uses a filtered teammate-aware list so stock chat-bot `aid` collisions do not break it

Still to do:

- Other profile skills panel:
    - show bot skills in the right side of `OtherPlayerProfileScreen` (reuse stock InventoryScreen skills list path).
    - apply this when viewing teammate/bot profiles.
- Bot tactic implementation (runtime/brain):
    - implement tactic behavior in follower AI/brain logic (not only team-management UI).
    - keep tactic persistence wiring aligned with runtime behavior (`Default`, `Support`, `Marksman`, `Holder`, `Pusher`).

Next active FE focus:

- first: bring the stock `InventoryScreen` skills list (`Common UI/Common UI/InventoryScreen/SkillsAndMasteringPanel/BottomPanel/SkillsPanel/Lower Part/Scrollview/Content/Skill List`) into the `OtherPlayerProfileScreen` right-side area for teammate/bot profiles
- second: implement tactic behavior in the bot brain/runtime path and keep it in sync with management/persistence

Notes from old plugin / description:

- Old plugin exposed squad management through a `Squad Manager` messenger/chatbot.
- Right-click `Invite to group` was the main way to bring a saved member into the next raid.
- Old plugin had a few other commands available through the chatbot, biggest being the restriction mode that will also need to be implemented in the new plugin
- The intended user experience is that teammate raids feel like the normal solo pre-raid flow, except the ready/loading screens show the squad instead of only the player.

# LANGUAGE SUPPORT

- ensure new plugin gets all it's text from the language
- following the old plugin add language support for various language
- observe game language setting and update according to the changes of it

# ADD THE GOONS

Description:

- Old plugin allowed for the Goons to become followers if player had good karma with Knight trader. We should implement the same feature in the new plugin.

# FUTURE IDEAS

- Hide the main menu bottom navigation bar when `My Squad` screen is open (same as it is hidden when the Add Teammate character creation screen opens).
