# UI Tasks:

- add settings option to disable all gestures that are not currently supported
- add a new gesture button with our own icon to symbolize "over there" gesture

# Combat commands

- (AFTER INITIAL RELEASE) "On your own" - bots no longer care about boss position and fight the enemy on ther own. Regroup resets this commad. On combat end, this command is also reset.
- (AFTER INITIAL RELEASE) - CoverMe - shrinks the radious of covers around the boss, makes agression 0 and makes them try to find covers as close as possible to the boss. GetBack resets this command. On combat end, this command is also reset.

# (INPROGRESS) - NON-COMBAT COMMANDS

- (DONE) - the "Stop" phrase should stop the bot from roaming around near the boss. It is similar to "hold" gesture, except the bot does not go into crouch. If boss gets out of range (25f) he should discard the command and resume following.
- (DONE) - GoForward, same as "There" command but it is for the hole team.
- (AFTER INITIAL RELEASE) "On your own" - makes bots enter patrol mode. Check old plugin for how that is done. Regroup resets this command.
- (AFTER INITIAL RELEASE) GetBack - increase following distance. Regroup resets the distance
- (AFTER INITIAL RELEASE) Silence - followers stop taking. Time should be set via settings, default is 1 minute. Max is 10 minutes. During combat, only "enemy spotted" is allowed on first engagement. And when they report enemy position during status report trigger.

# Combat Tactics

Description:

- (DONE) Marksman, as per old plugin where the follower is assumed to have a sniper rifle and thus will try to fight from a distance. This tactic is also aware of the second weapon, if automatic, for switching to it on close combat. Marksman, does not do enemy group search, it can join soround, but trying to find a safe spot at a distance instead of any. Does not do push. Can do supression if having secondary automatic weapons with decent range (200m+ effective range).
- (DONE) Default is what we have now.

- (AFTER INITIAL RELEASE) Non marksman can detect if having grenade launcher as secondary weapon and may choose to switch to it for supression and other decisions (see old plugin)
- (AFTER INITIAL RELEASE) Non marksman can also detect shotguns seconday weapon and may choose to use it for a close push (see old plugin)
- (AFTER INITIAL RELEASE) Even if follower is not marksman, if he detects single shot weapon as primary and auto as secondary, he may choose to switch to secondary for close combat.
    - Implementation:
        - keep this on the vanilla/core combat path only under `client/BigBrain`
        - do not merge marksman policy into default; keep marksman and default as separate tactic owners
        - reuse the existing shared weapon capability checks in `FollowerCombatCommon` and extend them only if needed for a "single-shot primary + automatic secondary" eligibility gate
        - hook default-owned close-pressure decisions first, not generic hold or visible-fire branches
        - prefer existing default push / advance / close-search style decisions as the ownership points for switching to secondary
        - gate the switch by close distance and local fight safety so default does not flip weapons during normal mid-range combat
        - add default-specific close-intent reasons only where needed so decision handoff can preserve secondary ownership the same way marksman currently does
        - add a default-specific switch-back rule after close pressure ends, recent contact cools off, or distance opens back up
        - use old `friendlypmc` marksman / guard secondary logic only as behavior reference, not as structure to copy
- (AFTER INITIAL RELEASE) Protector, sticks around the boss and is not afraid to get between the boss and the enemy, if the enemy is shooting the boss. Protector tries to put himself between the boss and the enemy. Something the old plugin did with "Guard" tactic.

# Vanilla Group Search combat decision (AFTER INITIAL RELEASE)

- SAIN has a search log, but so does vanilla. Yet SAIN also can group the bots to do group search instead of individual search. I would like to replicate the search party we tried with SAIN

# Vanilla Surround combat decision (AFTER INITIAL RELEASE)

Description:

- One bot starts the surround sequence, picks a valid cover spot first, then signals the others to allocate in sequence rather than all at once.
- Each bot should try to take a different direction around the enemy (`front`, `back`, `left`, `right`) from a cover point it can shoot from.
- If the ideal direction is not available, fallback to another direction is allowed, including duplicate directions, but not the same exact spot.
- Candidate spots must be validated by navigation path distance, not straight-line distance alone. A geometrically near point with a long path detour is not a valid surround point.
- If a bot in sequence cannot find any acceptable cover point, surround allocation ends there:
- bots that already got assignments continue to them
- remaining bots fall back to boss default/regroup behavior
- Surround must be interruption-safe because combat decisions can break at any time due to visibility, hits, enemy death, bot death, self-actions, or other SAIN combat changes.

# CURIOSTY (AFTER INITIAL RELEASE)

- add CURIOSITY settings slider that will determine how likely the follower is to investigate a noise or when sensing an enemy.
- attention makes the follower drop the curiosity and also set the enemy that trigger the investigation to be ignored for 2 minutes.
- only Default has this setting

# ADD THE GOONS (AFTER INITIAL RELEASE)

Description:

- Old plugin allowed for the Goons to become followers if player had good karma with Knight trader. We should implement the same feature in the new plugin.

# ADD SCAV FOLLOWER BRAIN (AFTER INITIAL RELEASE)

- Old plugin had a special brain for Scav followers that made them behave differently than PMC followers. We should implement the same feature in the new plugin, with different behavior for Scav followers compared to PMC followers.

# ADD SCAV FOLLOWERS AT SPAWN (AFTER INITIAL RELEASE)

- Old plugin allowed for Scav followers to spawn with the player if the player had good karma with Fence. We should implement the same feature in the new plugin.

# FUTURE IDEAS

- Tracking setting where followers use Realistic or Simple tracking. This means they will rely heavily on enemy last known position instead of using actual enemy position. This will be visible even with the enemy tracker symbol.
- Add setting to control bot difficulty which will be measured in accuracy and reactions.

## Implement follower run-ahead feature

Goal:

- Add a Dogmeat-style run-ahead behavior for followers while following the player.

Behavior target:

- When a follower lags behind, it should path/sprint to a projected point ahead/near the player movement direction (not exact feet-follow).
- When close enough, follower should settle back into normal local follow/idle behavior.
- Add fallback handling for path failure or excessive separation (safe catch-up/teleport logic as needed).
- Keep this compatible with both vanilla and SAIN runtime paths.

# (DONE) LANGUAGE SUPPORT

- ensure new plugin gets all it's text from the language
- following the old plugin add language support for various language
- observe game language setting and update according to the changes of it

# (DONE) Add directional quick-phrase look commands

Goal:

- Enable directional quick phrases such as `Front`, `Left`, `Right`, and similar directional callouts so followers orient and look toward the called direction in the same manner they currently react to `Over There`.

Behavior target:

- Direction resolution must be relative to the player look direction at the time the phrase is issued.
- Followers should translate the directional phrase into an attention/look target using the same general flow currently used by `Over There`.
- Keep this behavior consistent with existing boss-to-follower gesture/phrase propagation rules.
- Avoid inventing a separate movement/command model if the existing `Over There` attention path can be reused safely.
- Keep the implementation compatible with both vanilla and SAIN runtime paths.
- should work in and out of combat, but during combat is it condition by if the follower is not already enganging a target.

# (DONE) - Team management

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
    - saves teammate as mod-owned JSON under `pitFireTeam-ServerMod`
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

- (DONE) Add right-click context for protrait where you can invite to group, view profile or toggle "auto join" on/off. Auto joins was implemented in the old plugin making follower automatically join the next raid. They would show up in the "match maker ready" screen. If player kicked them out, they would not join until either manually added again, raid finished or game restarted.
- (DONE) bring the stock `InventoryScreen` skills list (`Common UI/Common UI/InventoryScreen/SkillsAndMasteringPanel/BottomPanel/SkillsPanel/Lower Part/Scrollview/Content/Skill List`) into the `OtherPlayerProfileScreen` right-side area for teammate/bot profiles
- (DONE) Bot tactic implementation (runtime/brain):
    - implement tactic behavior in follower AI/brain logic (not only team-management UI).
    - keep tactic persistence wiring aligned with runtime behavior (`Default`, `Support`, `Marksman`, `Holder`, `Pusher`).

Next active FE focus:

- implement tactic behavior in the bot brain/runtime path and keep it in sync with management/persistence

Notes from old plugin / description:

- Old plugin exposed squad management through a `Squad Manager` messenger/chatbot.
- Right-click `Invite to group` was the main way to bring a saved member into the next raid.
- Old plugin had a few other commands available through the chatbot, biggest being the restriction mode that will also need to be implemented in the new plugin
- The intended user experience is that teammate raids feel like the normal solo pre-raid flow, except the ready/loading screens show the squad instead of only the player.

# (DONE) ADD SUPPORT FOR CUSTOM EQUIPMENT

In addition to the way we select equipment preset for followers, we should also allow for custom equipment to be done on the followers. We can do this by adding a "Edit Loadout" button next to the equipment dropdown where when press, it will open to the right the bot's inventory like when you loot a corpse. And to the left, instead of player investory we show the player's stash. Then user can move items from stash to bot inventory and vice versa. When user is done, they can press a "Done" button and this will be saved as "Custom" preset for the bot. It is important to note that once saved, the items the player picked will not actually be removed from his stash. This is a clone move. WHen player moves an item from the stash to the bot, it appears on the bot investory, but does not go away from the stash. When the player moves an item from the bot's inventory it goes away from that inventory and will temporarly appear in the player stash (until done is pressed), if that item was originally in the bot inventory (meaning it was part of bot's default loadout). When player press "done" such items disappear from the player stash and the bot's "default" loadout remains unchanged.
If player edit default's bot invetory or does a combination (as he can move back and forth in the screen) where among the items in the new loadout. when he saves, we must remember the "default" items so the player cannot comeback later and edit the custom layout and now, because it is custom and not default, he is able to perform an exploit and retain the itmes.
So in short, if we allow custom loadout, the bot cannot actually take items from the player stash, but only clone them. The player also cannot take bot's default items, but only move them as he make changes, because for example he may take out the default weapon and put one of this own. When he says, that default weapon will not stay in the stash and neither will the player's weapon be removed from the stash.

# (DONE) FOLLOWER LEVEL PROGRESS

Old plugin had follower level up his skills and level experience during raids. Needs to be implemented in the new plugin as well, with the same persistence and progression logic.

# (DONE) FOLLOWER QUESTS PROGRESS

Old plugin allowwed followers kills to be counted as progress for kill quest of the player, granted they meet the requirements. Needs to be implemented in the new plugin as well.

# (DONE) FOLLOWER TRANSIT

Old plugin made it possible for followers to transit with the user between maps. Needs to be implemented in the new plugin as well.

# (DONE) ADD ICON GROUP TO FOLLOWER PORTRAT

Add an icon similar to the "auto join" icon to the roaster tile of followers that will show when the follower is present in the group. THe resource to use is "icon_group.png". This one will be in the lower left corner of the title. You must keep it in sync with the status of the group followers. I can invite a follower to the group fia right click on the tile or on the invite group component. When follower is added or remove there, this icon must update if I am already on the screen.

# (DONE) ADD MY SQUAD SETTINGS BUTTON

Add a "my squad settings" button that show up only while in raid and allows the view of the settings page of the squad screen. We could centralize this so that his button opens a full screen overlay (like the old screen) and has just settings components. While the squad screen has the full squad management components. Should be, below "resume" button and with a 10px top offset

# (DONE) IMPROVE COVER SELECTION

Old plugin had several method for selecting covers and positions from where to shoot, rather than relying on vanilla selection whish sometimes can push the bot behind the boss or in a vulnerable position

# (DONE) ADD 'AGGRESSION' SETTING

Add a slider meant to determine the agressiveness of a follower. This will be seen in the "other profile" screen, after "Edit loadout". This controls how frequent he will try to push an enemy by going to him. With 100% this should mean that the bot should not care at all if enemy is outside the radius of the boss, he should still try to push him. With 50% it should act as it is now, with 0% it should mean no push, always stay around boss. Every increase, increases the the outside radius he can go to. Beyond 80%, bot does not care how far the enemy is.

# (DONE) Implement Enemy Push command

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

New Plugin Push Flow:

Very similar to old flow, but with some adjustments:

- Triggered by `EPhraseTrigger.Gogogo` but in the "transmision flow" is in the matter the others are in pitFireTeam (like EPhraseTrigger.OnRepeatedContact or EPhraseTrigger.Regroup)
- Set aggression to 100% until enemy is dead or boss issues new command
- Does nothing out of combat
