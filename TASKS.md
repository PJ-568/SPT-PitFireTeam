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

- Tactic management:
    - add the second dropdown next to loadout for tactic
    - persist tactic to BE
    - restore old plugin tactic meanings where still applicable (`Default`, `Support`, `Marksman`, `Holder`, `Pusher`)
- Profile customization parity:
    - add voice and head customization from profile view
    - verify clothing/loadout/tactic UI layout and iconography
    - continue polishing rename/profile UI layout
- Pre-raid flow parity:
    - ensure teammate flow matches solo flow exactly up to insurance
    - ensure only the ready screen and loading screen are customized
    - keep group state clean across raid end, abort, and solo/follower transitions
- Old plugin investigation still needed:
    - review old pre-raid/group-state handling in `friendlypmc` to compare against the current alternative implementation
    - review old team-management behavior from `moddescription.html` and old FE/BE paths for anything user-facing still missing
- Optional later scope:
    - evaluate which old `Squad Manager` chat features still matter in the new model (`info`, `restrictions`, `autojoin`, `recruit`, scav-squad variants, static default equipment)

Next phase plan:

- Phase 1: planning and documentation for a dedicated Team Management screen
    - document the stock EFT UI surfaces already touched by friendlySAIN:
        - other profile screen
        - raid preparation / ready / loading screens
        - teammate creation flow based on the stock account side/head selection screen
        - nickname edit overlay pattern
    - investigate the trader top-right player portrait pattern for a lightweight team portrait/header option
    - investigate the stock `Settings` screen with focus on:
        - `Game` tab
        - `PostFX` tab
    - use that investigation to plan a dedicated Team screen with:
        - `Settings` tab for friendlySAIN settings currently living in BepInEx config
        - `Roster` tab for teammate/member management
    - keep this phase implementation-free beyond documentation/tracking updates

Current implementation follow-up:

- Phase 1 planning is complete:
    - UI investigation doc written
    - trader portrait and settings screen references documented
- Phase 2 roster implementation is functionally in place:
    - main menu now gains a localized `My Squad` entry
    - dedicated Team screen now includes:
        - `Roaster` tab
        - `Settings` tab
        - back navigation
    - roster work now includes:
        - centered multi-row roster layout
        - max 5 members per row
        - scrolling for larger squads
        - add teammate from the Team screen
        - remove teammate confirmation flow
        - teammate profile open from roster tile
        - return from teammate profile back into the Team screen
- Phase 3 settings implementation is functionally in place:
    - Team screen `Settings` tab now displays the main friendlySAIN config set
    - settings use a stock-style scrollable layout
    - checkbox settings use stock EFT toggle controls
    - ranged settings use stock EFT slider controls
    - settings have been regrouped/reordered for the current squad UX
    - duplicated ConfigurationManager entries are hidden from the BepInEx config UI
    - keybind/input-option rows are still skipped for now

Next active FE focus:

- settings tab polish and any later parity gaps, not first-pass implementation
- preserve the completed roster/settings flow while remaining teammate profile and pre-raid parity work continues

Notes from old plugin / description:

- Old plugin exposed squad management through a `Squad Manager` messenger/chatbot.
- Right-click `Invite to group` was the main way to bring a saved member into the next raid.
- Profile view supported clothes, tactic, equipment, voice, and head.
- The intended user experience is that teammate raids feel like the normal solo pre-raid flow, except the ready/loading screens show the squad instead of only the player.
