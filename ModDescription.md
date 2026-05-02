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
- Select a loadout from **Default** or your saved player equipment builds.
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

Commands use Tarkov's existing phrase and gesture system. Depending on voice and side, some phrases may appear in different places or may not be available for every voice.
Some of the commands can be applied to individual followers by looking directly at them when issuing the command.
Commands influence follower behavior but do not force exact actions. Followers will adapt based on combat conditions and may not always respond immediately if engaged or under threat.

**In COMMAND:**

- **Follow Me / Cooperative** - recruit an eligible same-side bot or tell existing followers to resume following.
- **Attention / Look** - clears command pressure and makes followers focus on the boss or indicated direction.
- **Regroup** - tells followers to converge near the boss. In combat, this becomes a combat regroup objective.
- **Hold Position** - in combat, temporarily behaves like setting follower aggression to 0%. The override resets after combat ends or when replaced by another command. Can be applied to an individual follower by looking at him.
- **Go Go Go** - clears the temporary Hold Position combat-aggression override and returns followers to their saved aggression. Can be applied to an individual follower by looking at him.
- **Go Forward** - orders followers with an enemy to push or pressure that enemy. Outside combat, it can send followers toward the pointed location. Can be applied to an individual follower by looking at him.
- **Stop** - stops followers out of combat without forcing crouch. If the boss moves too far away, followers resume normal follow behavior. Can be applied to an individual follower by looking at him.
- **Suppress** - orders non-Marksman followers to suppress the current enemy. The follower must have a suitable suppress-capable weapon: full-auto or a magazine capacity of at least 25 rounds. If ordered without a suitable weapon, he will say "negative" and continue normal combat decisions.

**In HELP:**

- **Need Sniper** - urge Marksman to provide sniper support against the closest enemy to you. He will say "negative" if no suitable spot is found.
- **Need Help** - urge your followers to provide combat support against the closest enemy to you.

**In CONTACT:**

- **Contact** - makes followers look toward the boss aim direction and can help them acquire a visible enemy.
- **Over There** - gesture-based contact/attention toward the pointed direction.
- **Front / Left / Right / On Six** - directional look commands relative to the boss look direction.
- **Status Report** - shows follower status, distance, health summary, and tactic information.

**Implemented gesture/interaction commands:**

- **Come To Me** - the looked-at follower moves close to the boss.
- **There Direction** - sends a selected or nearby follower toward the pointed location.
- **Stop gesture** - tells nearby followers to hold position, including crouch behavior.
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

**Loot Carrying**

Have followers carry your loot: look at an item and press the interaction prompt shown in the lower-left of the screen. The follower will pick it up if he has space and is not in combat. You must successfully extract with him for the loot to be returned after the raid via a SquadDelivery message. If he dies or you do not extract, the loot is lost.

**Basic combat advice:**

- Use **Regroup** when followers are too far away or need to return to the boss.
- Use **Hold Position** when you want them to stop pushing and stay more defensive for the current fight.
- Use **Go Go Go** when you want them to return to their saved aggression.
- Use **Go Forward** when you want active combat pressure on the current enemy.
- Use **Contact**, **Over There**, or directional calls to point followers toward a threat or suspected threat direction.

## Upcoming

**Planned commands:**

- **Get Back** - increases following distance as well as the distance at which bots auto-regroup near the boss during combat. Command resets on **Regroup**.
- **On Your Own** - followers will no longer care about boss position and will fight the enemy on their own. When out of combat, followers will patrol around you at a configurable distance using Patrol Radius. Resets on **Cover Me**.
- **Spread Out** - in combat, tells followers who are not actively engaged to find cover.
- **Silence** - tells followers to stop talking. Maximum time will be controlled via settings.

**Planned settings:**

**Loadout Management:**

- **Simple** - edit or choose a follower loadout without requiring the gear to be in the player's inventory. Still limited to gear currently in the stash and not equipped on the player. Gear is not lost on death.
- **Immersive** - any gear used for a follower loadout will be taken from the player's stash. Gear is not lost on death.
- **Hardcore** - same as Immersive, but followers equipement get damaged and if they die, their gear is also lost.

**Squad Budget** - restricts the maximum number of teammates you can add to your squad based on available Command Points. Command Points are gained by leveling up, keeping followers alive, and keeping picked-up raid allies alive. Points are lost if you kill followers or allies.

**Planned features:**

- Adding a third follower tactic.
- Porting over grenade launcher support from the old plugin.
- Porting the Goons playthrough from the old plugin.
- Being able to play with Scav followers.

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
