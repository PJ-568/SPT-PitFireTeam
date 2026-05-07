This is the official successor of the Friendly PMC mod that was available for SPT 3.x.

Pit Fire Team makes it possible to have bots follow you around and fight alongside you against enemies. You can create a customizable PMC squad, bring selected teammates into raids, recruit eligible same-side bots during a raid, and use Tarkov's existing phrase and gesture system to command your followers.

---

If you would like to show your appreciation, you can support me at [Ko-Fi](https://ko-fi.com/n00bish).

---

**Beta release note:** This is the first beta of the new version. Not every feature from the previous version has been ported yet. Features that are planned but not currently available are listed under Upcoming.

---

# Tabs {.tabset}

## Description

You can manage your teammates from the in-game **My Squad** screen. From there, you can build your roster, adjust squad settings, customize individual teammates, invite them into your raid group, and decide who should automatically join future raids.

**Notable features:**

- **Dedicated squad screen** - manage your roster, customize teammates, and settings from a separate My Squad interface.
- **Teammate customization** - change teammate name, appearance, voice, tactic, aggression, and loadout.
- **Raid group support** - invite teammates into your group manually or use Auto Join to preload selected teammates into your next PMC raid.
- **Follower commands** - issue combat, movement, attention, loot, and door commands through existing Tarkov phrases and gestures.
- **Map transitions** - teammates who you spawned with can follow you through map transitions.
- **Progression system** - followers gain raid experience and common-skill progress that persists between raids.
- **Quest assist** - follower kills can count toward player kill quests when the kill meets the quest criteria.
- **Loot return** - teammates who you spawned with can return items after the raid.

**Compatibility tested with:**

- SAIN installed
- Looting Bots installed
- Acid's Bot Placement System
- Acid's Progressive Bot System

The mod is still sensitive to other mods that heavily change bot AI, grouping, perception, hostility, or spawning. If followers become hostile, ignore enemies, or behave strangely, test with fewer bot-related mods first.

## Installation

**Required dependency:**

- [BigBrain](https://forge.sp-tarkov.com/mod/902/bigbrain)

**Recommended:**

- [WAYPOINTS - EXPANDED NAVMESH](https://forge.sp-tarkov.com/mod/827/waypoints-expanded-navmesh), because followers can have a harder time navigating without expanded navmesh data.

Extract the downloaded archive into your SPT install directory. It should add files under both **BepInEx** and **user** / **SPT/user**, depending on your SPT layout.

## My Squad Screen

![My Squad screen](https://iili.io/BsjHsql.png)

**My Squad** is the main management screen.

It has two main tabs:

- **Roster** - create teammates, view existing teammates, invite them to the group, toggle Auto Join, open their profile, or remove them.
- **Settings** - configure squad options from an in-game screen instead of relying only on the BepInEx configuration window.

**Teammate portrait actions (Right-click):**

- **Invite to group** - adds the teammate to your current pre-raid group.
- **View profile** - opens the teammate profile for customization.
- **Auto Join on/off** - controls whether this teammate automatically joins your next PMC raid setup.

During a raid, the settings view can also be opened from the pause menu through the My Squad settings entry.

## Squad Customization

Teammates can be customized from their profile screen.

**Currently available:**

- Rename teammate.
- Change clothing using the stock clothing selectors.
- Select a loadout from **Default** or your saved player equipment builds.twi
- Edit a cloned teammate loadout with the current loadout editor.
- Select a combat tactic.
- Adjust aggression for Rifleman and Marksman tactics.
- View follower-relevant skills.

**Tactics available:**

- **`Rifleman`** - the default balanced combat style. Riflemen stay useful near the boss when there is no good attack opportunity, but can push, search, and pressure when the enemy state and aggression allow it.
- **`Marksman`** - ranged-focused behavior for sniper-style followers. Marksmen prefer firing positions and distance, avoid generic assault pushes, and can switch to an automatic secondary for close fights when appropriate.

**Aggression slider:**

Aggression controls how willing a follower is to leave boss-local safety for proactive pressure. Lower aggression keeps followers more defensive and boss-local. Higher aggression allows more search, push, and pressure when combat conditions justify it. At 0%, followers avoid proactive pressure and prefer to stay around the boss. The combat **Hold Position** command temporarily behaves like 0% aggression until combat ends or **Go Go Go** clears it.

**Rifleman aggression:** Rifleman uses 50% as its default balanced baseline. Lower values bias toward cover, support, and regroup. Higher values make Riflemen more willing to push or search farther from the boss when threat checks allow it.

**Marksman aggression:** Marksman uses 30% as its default baseline. Marksman aggression is tactic-relative: it mainly controls proactive automatic-weapon close-search/auto-search pressure. It does not turn Marksman into a generic Rifleman, and it does not block defensive automatic secondary use when enemies get close. At 0%, Marksman avoids proactive auto-search and stays range/position focused. Higher values make Marksman more willing to use automatic-weapon offensive search when distance and threat checks are safe.

**Loadout editing note:**

The current loadout editor uses cloned/local items. Editing a teammate loadout does not consume items from the player's real stash and gear is not lost on follower death in this beta.

## Squad Commands

![Gestures Menu](https://iili.io/BQdlFv1.png)

Commands use Tarkov's existing phrase and gesture system. Depending on voice and side, some phrases may appear in different places or may not be available for every voice.
Some of the commands can be applied to individual followers by looking directly at them when issuing the command.
Commands influence follower behavior but do not force exact actions. Followers will adapt based on combat conditions and may not always respond immediately if engaged or under threat.

**In COMMAND:**

- **Follow Me / Cooperative** - recruit an eligible same-side bot or tell existing followers to resume following.
- **Attention / Look** - clears command pressure and makes followers focus on the boss or indicated direction.
- **Regroup** - tells followers to converge near the boss. In combat, this becomes a combat regroup objective (within 18 meters radius of the boss, Marksman within 24m).
- **Hold Position** - in combat, temporarily behaves like setting follower aggression to 0%. The override resets after combat ends or when replaced by another command. Can be applied to an individual follower by looking at him.
- **Go Go Go** - clears the temporary Hold Position combat-aggression override and returns followers to their saved aggression. Can be applied to an individual follower by looking at him.
- **Go Forward** - orders followers with an enemy to push or pressure that enemy. Outside combat, it can send followers toward the pointed location. Can be applied to an individual follower by looking at him.
- **Stop** - stops followers out of combat without forcing crouch. If the boss moves too far away, followers resume normal follow behavior. Can be applied to an individual follower by looking at him.
- **Suppress** - orders non-Marksman followers to suppress the current enemy. The follower must have a suitable suppress-capable weapon: full-auto or a magazine capacity of at least 25 rounds. If ordered without a suitable weapon, he will say "negative" and continue normal combat decisions.
- **On Your Own** - lets followers spread out and act more independently instead of staying tied to your position. Outside combat, they patrol around you using Patrol Radius. In combat, they fight from their own area or stay near another squadmate instead of constantly trying to return to you.
    - **Regroup** during combat still calls them back to you for that order, but it does not cancel On Your Own. Use **Cover Me** during combat if you want them to start watching your position again. Outside combat, **Cover Me**, **Regroup**, or **Follow Me** returns them to normal follow behavior.

**In HELP:**

- **Need Sniper** - urge Marksman to provide sniper support against the closest enemy to you. He will say "negative" if no suitable spot is found.
- **Need Help** - urge your followers to provide combat support against the closest enemy to you.

**In CONTACT:**

- **Contact** - makes followers look toward the boss aim direction and can help them acquire a visible enemy.
- **Front / Left / Right / On Six** - directional look commands relative to the boss look direction.
- **Status Report** - shows follower status, distance, health summary, and tactic information.

**Implemented gesture/interaction commands:**

- **Come To Me Gesture** - the looked-at follower moves close to the boss.
- **There Direction Gesture** - sends a selected or nearby follower toward the pointed location.
- **Stop Gesture** - tells nearby followers to hold position, including crouch behavior.
- **Over There Gesture** - gesture-based contact/attention toward the pointed direction.
- **Open Door** - the closest eligible follower opens the targeted door.
- **Loot This** - the closest eligible follower picks up the targeted loot item.

Recruited allies are temporary and limited in behavior. Saved teammates are fully supported squad members with customization, progression, and reliable command response.

## Gameplay Guide

---

Saved teammates automatically have ammo and medical supplies available and do not require these items in their loadout. Recruited allies found during a raid do not receive this behavior and rely on their existing equipment.
Followers still use Tarkov bot movement and navigation. They can choose cover or movement paths that are not exactly where you expected, especially in complex interiors.

---

**Adding teammates to a raid:**

- Open **My Squad**.
- Create or select a teammate from the roster.
- Use the roster portrait/context action **Invite to group** to add them to the current raid group.
- Use **Auto Join** if you want that teammate to be preloaded into future PMC raid setup automatically.
- If you remove a teammate from the current group, they will not auto-join again until manually re-added or toggled.

Followers are not scripted companions with exact RTS-style control. Commands influence their priorities and intent, but followers still react to danger, visibility, healing, cover, and survival. A follower under pressure may delay or ignore a command if executing it would be dangerous.

Think of commands as:

- tactical guidance
- combat priorities
- movement intent

—not direct movement control.

### Squad Roles

#### Rifleman

Rifleman is the default all-purpose combat role.

Riflemen:

- stay useful near the boss
- support nearby teammates
- suppress enemies
- push when aggression and combat conditions allow it
- regroup more aggressively around the player

Best used for:

- close and medium-range combat
- indoor fights
- aggressive pushes
- general squad support

#### Marksman

Marksman is focused on ranged support and firing positions.

Marksmen:

- prefer distance and sightlines
- avoid generic assault pushes
- reposition for firing opportunities
- can switch to automatic secondary weapons in close fights

Best used for:

- overwatch
- outdoor maps
- long sightlines
- supporting Rifleman pushes

Do not expect Marksman followers to rush enemies like Riflemen.

### Recommended Beginner Setup

For a stable beginner squad:

- 1 Rifleman
- 1 Marksman
- Rifleman aggression around `50%`
- Marksman aggression around `30%`

### Basic Combat Usage

#### Regroup

One of the most important commands.

Followers move back toward the boss and nearby cover.

Use it:

- after long chases
- when followers spread too far
- before crossing dangerous areas
- before entering a new fight

#### Hold Position

Hold Position does **not** mean:

> "stand perfectly still."

In combat, it temporarily makes followers behave much more defensively by reducing aggressive push behavior.

Followers can still:

- shoot
- reposition for survival
- defend themselves
- react to danger

Good for:

- holding buildings
- healing
- defensive fights
- stopping overextension

#### Go Go Go

Clears the temporary Hold Position combat behavior and returns followers to their saved aggression settings.

Use it after:

- defensive holds
- regrouping
- recovering from dangerous fights

#### Go Forward

Orders followers to pressure or push their current enemy.

Best used when:

- enemies are pinned
- enemies are already engaged
- the squad is ready to advance

This is not a suicide rush command. Followers still evaluate danger and cover before pushing.

#### Suppress

Orders Riflemen to provide suppressive fire.

Useful for:

- pinning enemies behind cover
- helping another teammate reposition
- supporting a push

Marksmen generally ignore suppression because their role is precision support, not volume fire.

#### Need Sniper

Urges Marksman followers to actively search for a firing position against the closest threat.

Useful because Marksmen naturally prefer sitting on good positions and waiting for opportunities instead of constantly searching for new ones.

Use it when:

- enemies are far away
- enemies are holding angles
- you need overwatch support
- the sniper has become too passive during a fight

Marksmen may reject the order if:

- no useful firing position exists
- the fight is too close-range
- survival or healing takes priority

#### Contact / Directional Commands

Commands like:

- **Contact**
- **Front**
- **Left**
- **Right**
- **On Six**
- **Over There**

help followers orient toward threats or suspected enemy locations.

These are especially useful before enemies become fully visible.

### Important Combat Advice

Do not constantly pull followers back toward you while they are actively fighting another enemy.

In fights with multiple enemies, you can accidentally disrupt their current engagement and create unstable combat behavior as enemy priorities constantly change.

Followers generally perform better when:

- they are allowed to finish their current engagement
- they lead the push
- you support them instead of constantly repositioning them

Over-commanding followers can:

- interrupt movement
- reset positioning
- confuse enemy prioritization
- create unstable combat behavior

Use commands deliberately instead of continuously micromanaging.

### Loot Carrying

Look at an item and use the lower-left interaction prompt to order a follower to pick it up.

The follower:

- must not be in combat
- must have inventory space
- must be able to reach the item

You must successfully extract with that teammate for the loot to be returned after the raid. Note that only followers you spawned with, will return the loot.

If the follower dies, the loot is lost.

## Upcoming

The following are planned features in reaching a release version (1.0.0)

**Planned commands:**

- **Spread Out** - in combat, tells followers who are not actively engaged to find cover.
- **Silence** - tells followers to stop talking. Maximum time will be controlled via settings.
- Using **Come Here** and **There** gestures while in combat for short movement of the followers

**Planned settings:**

- **Loadout Management:**
    - **Simple** - edit or choose a follower loadout without requiring the gear to be in the player's inventory. Still limited to gear currently in the stash and not equipped on the player. Gear is not lost on death, spawned follower gear cannot be looted.
    - **Immersive** - any gear used for a follower loadout will be taken from the player's stash. Gear is not lost on death. Makes spawned follower's gear lootable.
    - **Hardcore** - same as Immersive, but followers equipment gets damaged and if they die, their gear is lost.

- **Squad Budget** - restricts the maximum number of teammates you can add to your squad based on available Command Points. Command Points are gained by leveling up, keeping followers alive, and keeping picked-up raid allies alive. Points are lost if you kill followers or allies.

- **Patrol Radius** - to be used together with out of combat command **On Your Own** where it triggers patrol mode for followers

**Planned features:**

- Posibility for your team to make it out the raid with the loot, despite you dying
- Posibility to check teammate backpack during raid.
- Adding a third follower tactic.
- Porting over grenade launcher support from the old plugin.
- Being able to play with Scav followers.
- Porting the Goons playthrough from the old plugin.

## Known Issues and Conflicts

The mod changes bot grouping, follower ownership, commands, and combat routing. Mods that heavily change bot AI, spawning, hostility, senses, or group behavior can conflict with it.

- Followers can linger after combat. Use **Attention** to reset them.
- Followers might not heal their health all the way. It is a game issue, use the Heal key to force heal.
- Teleporting followers while they are interacting with doors or other objects can leave them in a bad state.
- The game has navigation problems that even SAIN is not able to fully resolve. If your bots get stuck, use teleportation. In other situations, their movement is in teleportation-like bursts.
- SAIN can interfere with teleportation, teleporting the bot back to previous location. You may need to trigger teleportation multiple times for it to stick.
- Followers can occasionally have registration delay on enemies. This is buggy behavior within the game that I am not able to fix completely.
- Followers may have shaky aiming during some executions. It does not affect their performance, but can be an annoying visual glitch.
- Bushes are cursed with SAIN. You followers can stand in a bush and not shoot while having visibility of the enemy.
- If you have problems with My Squad screen and are not on English lanuage, switch to it, to see if that works. If so, post the issue along with the language that you originally tried.

If a follower appears stuck, try Attention or teleportation before assuming the raid is unrecoverable.

{.endtabset}
