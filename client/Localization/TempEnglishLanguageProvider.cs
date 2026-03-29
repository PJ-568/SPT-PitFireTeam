using System.Collections.Generic;

namespace friendlySAIN.Localization
{
    internal static class TempEnglishLanguageProvider
    {
        private static Dictionary<string, string> Entry(string name, string description)
        {
            return new Dictionary<string, string>
            {
                ["Name"] = name,
                ["Description"] = description
            };
        }

        public static LanguageOptions Create()
        {
            return new LanguageOptions
            {
                baseSettings = "Base Settings",
                inputSettings = "Input Settings",
                miscSettings = "Miscellaneous",
                testSettings = "Testing",
                raidSettings = "Raid Settings",
                equipOptions = new[] { "Default" },
                tacticOptions = new[] { "Default", "Support", "Marksman", "Pusher", "Holder", "Assist" },

                statusSound = Entry(
                    "Enemy Location Volume",
                    "Volume of the ping sound for the enemy location marker during combat"),
                enemyMarker = Entry(
                    "Enemy Marker",
                    "Show enemy position when reporting status. If disabled, the enemy marker sound will also be disabled"),
                scanDistance = Entry(
                    "Maximum scan distance",
                    "Maximum distance to pick up any visible enemy that the player is signaling when issuing 'Contact' phrase"),
                patrolRadius = Entry(
                    "Patrol Radius",
                    "Maximum distance from the player the followers will patrol around"),
                enemyRemember = Entry(
                    "Time to forget about the enemy (in sec.)",
                    "Maximum time a follower will remember an enemy. This is applied only at the beginning of a raid"),
                healthMultiplier = Entry(
                    "Squad Health Multiplier",
                    "Health multiplier for the followers you spawn with. This is applied per each body part."),
                pickup = Entry(
                    "Pickup",
                    "Enable or disable the ability to recruit same-side bots during a raid."),
                tieredPickup = Entry(
                    "Tiered Pickup",
                    "Use the player-vs-bot level difference rules when deciding whether a bot accepts a pickup request."),
                maximumPickup = Entry(
                    "Maximum Pickup",
                    "Maximum number of non-squad same-side bots you can pick up during a raid."),
                recruitPickup = Entry(
                    "Recruit Pickup",
                    "Allow picked-up followers that were successfully extracted with to send friend requests. This uses player-vs-bot level difference rules when deciding."),
                npcSendMessage = Entry(
                    "Raid End Messages",
                    "Followers will send message at the end of the raid based on conditions such as if all made it out or if you picked up a follower and kept him alive. Return items messages are excluded"),
                friendlySAIN = Entry(
                    "Friendly PMC Side",
                    "Should PMC Bots of the same side always be friendly to each other"),
                badGuy = Entry(
                    "Bad Guy",
                    "Should the player be hostile to all PMC bots, regardless of faction"),
                pmcArmbands = Entry(
                    "PMC Arm Bands",
                    "Should PMC bots have armbands (red for BEARs, blue for USECs). (Can conflict with mods that modify bot equipment, rendering this setting ineffective.)"),
                englishBear = Entry(
                    "BEARs speak English",
                    "Should BEAR bots speak English only"),
                pingSquad = Entry(
                    "Status Report Shortcut",
                    "Alternative shortcut key for the Status Report quick phrase"),
                pingRadioVolume = Entry(
                    "Report Status Volume",
                    "Volume of the radio sound when triggering report status"),
                pingTime = Entry(
                    "Report Status Display Time",
                    "Time in seconds to display the followers status"),
                enemyContact = Entry(
                    "Enemy Contact Shortcut",
                    "Alternative shortcut key for the Contact quick phrase"),
                overThere = Entry(
                    "Over There Shortcut",
                    "Alternative shortcut key for the Over There quick gesture"),
                botTeleport = Entry(
                    "Teleport Followers",
                    "Teleport all followers to the player's position"),
                botHeal = Entry(
                    "Heal Followers",
                    "Heal all followers. This will not restore their black limbs to full health."),
                botPrefetch = Entry(
                    "Prefetch follower data",
                    "Prefetch follower data to reduce the time it takes to spawn them. If you experience problems with followers spawning, disable this option and try again. Take note that this can increase the spawn time."),
                botGrenades = Entry(
                    "Followers use grenades",
                    "Allow followers to use grenades"),
                botTalk = Entry(
                    "Followers trash talk",
                    "Frequency at which a follower will trash talk during combat. Set to 0 to disable."),
                spawnPoint = Entry(
                    "Use Coop Spawn Point",
                    "Use Coop Spawn Points when spawning with followers."),

                gestures = new Dictionary<string, string>
                {
                    ["OverThere"] = "Over There",
                    ["TeamStatus"] = "Status Report",
                    ["OnRepeatedContact"] = "Contact"
                },
                botStatus = new Dictionary<string, string>
                {
                    ["Dead"] = "Dead",
                    ["Engaged"] = "In Combat",
                    ["Alerted"] = "Enemy Detected",
                    ["Heal"] = "Healing",
                    ["WantToHeal"] = "Wants to heal"
                },
                socialUi = new Dictionary<string, string>
                {
                    ["AddTeammate"] = "+ Add teammate",
                    ["AddTeammateInProgress"] = "{0} was added to your friends list",
                    ["AddTeammateConfirm"] = "Add Teammate",
                    ["AddTeammateFlowActive"] = "Add teammate flow is already open.",
                    ["AddTeammateOpenFailed"] = "Could not open teammate creation screen.",
                    ["AddTeammateUnsupportedSide"] = "Add teammate only supports PMC profiles right now.",
                    ["EnterNickname"] = "Enter player nickname",
                    ["NicknameTooShort"] = "Nickname too short",
                    ["RenameTeammateTitle"] = "Rename teammate",
                    ["RenameSave"] = "Save",
                    ["RenameCancel"] = "Cancel",
                    ["RenameChange"] = "EDIT NAME",
                    ["EditLoadout"] = "EDIT LOADOUT",
                    ["EditLoadoutTitle"] = "Edit Loadout",
                    ["EditLoadoutSubtitle"] = "Loadout editor shell for {0}. Inventory integration comes in the next phase.",
                    ["PlayerStash"] = "Player Stash",
                    ["PlayerStashPlaceholder"] = "Failed to load cloned stash view.\n{0}",
                    ["BotInventory"] = "Follower Inventory",
                    ["BotInventoryPlaceholder"] = "Failed to load cloned follower inventory.\n{0}",
                    ["ProfileTactic"] = "Default",
                    ["RenameFailed"] = "Could not rename teammate",
                    ["RenameClose"] = "x",
                    ["RemoveTeammateTitle"] = "Remove teammate",
                    ["RemoveTeammatePrompt"] = "Are you sure you want to delete member {0}? Process cannot be undone.",
                    ["RemoveTeammateConfirm"] = "Remove",
                    ["SquadControlDeleteTooltip"] = "Delete",
                    ["SquadControlInviteToGroup"] = "Invite to group",
                    ["SquadControlViewProfile"] = "View profile",
                    ["SquadControlAutoJoinOn"] = "Auto join: On",
                    ["SquadControlAutoJoinOff"] = "Auto join: Off",
                    ["SquadControlAutoJoinTooltip"] = "Auto-join",
                    ["SquadControlInGroupTooltip"] = "In group",
                    ["SquadControlAutoJoinEnabledToast"] = "Enabled PMC raid auto-join for {0}.",
                    ["SquadControlAutoJoinDisabledToast"] = "Disabled PMC raid auto-join for {0}.",
                    ["SquadControlAutoJoinEnableFailedToast"] = "Failed to enable auto-join for {0}",
                    ["SquadControlAutoJoinDisableFailedToast"] = "Failed to disable auto-join for {0}",
                    ["SquadControlButton"] = "My Squad",
                    ["SquadControlTitle"] = "My Squad",
                    ["SquadControlRaidSettingsButton"] = "Squad Settings",
                    ["SquadControlRaidSettingsTitle"] = "My Squad Settings",
                    ["SquadControlBack"] = "Back",
                    ["SquadControlClose"] = "Close",
                    ["SquadControlRosterTab"] = "Roaster",
                    ["SquadControlSettingsTab"] = "Settings",
                    ["SquadControlRosterPlayer"] = "PLAYER",
                    ["SquadControlRosterLeader"] = "Leader",
                    ["SquadControlRosterMemberA"] = "TEAMMATE 01",
                    ["SquadControlRosterMemberB"] = "TEAMMATE 02",
                    ["SquadControlRosterRole"] = "Squad Member",
                    ["SquadControlEmptyRoster"] = "You have not created any team members yet, press the add button below to get started"
                }
            };
        }
    }
}
