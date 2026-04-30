# My Squad Current State Review

Date: 2026-04-28

## Goal

Document the current verified implementation of the `My Squad` experience as it exists today in `pitFireTeam`, split into:

1. `Roster`
2. `Settings`
3. `Profile Screen`

This is a current-state review, not a target design doc. It should be read alongside:

- `docs/team-management-ui-investigation-2026-03-19.md` for the earlier stock-UI investigation and target direction

## High-Level Shape

`My Squad` is not one single screen implementation.

Today it is split across two UI hosts:

- `MatchMakerSideSelectionScreen` in a custom "squad mode" for the `Roster` and `Settings` tabs
- `OtherPlayerProfileScreen` for the selected teammate `Profile Screen`

That means the current flow is:

1. main menu `My Squad` button
2. open stock side-selection screen in squad mode
3. hide native PMC/Scav selection widgets
4. inject pitFireTeam roster/settings panels
5. open teammate profile from roster tile
6. patch stock other-player profile into teammate management UI
7. return back into `My Squad`

Authoritative files:

- `client/Patches/MenuScreenSquadControlPatch.cs`
- `client/Modules/SquadSideSelectionFlow.cs`
- `client/Patches/MatchMakerSideSelectionScreenPatch.cs`
- `client/Components/SquadControlMenuUi.cs`
- `client/Components/SquadControlMenuUi.Roster.cs`
- `client/Components/SquadControlMenuUi.Settings.cs`
- `client/Components/SquadControlMenuUi.ContextMenu.cs`
- `client/Components/SquadControlMenuUi.Backend.cs`
- `client/Patches/OtherPlayerProfileScreenPatch.cs`
- `client/Patches/OtherPlayerProfileScreenPatch.LoadoutUi.cs`
- `server/Resources/lang/*.json`
- `server/Services/FriendlyLanguageService.cs`

## Entry Flow

### Main menu entry

`MenuScreen.Show(...)` is patched so `SquadControlMenuUi` is attached to the live `MenuScreen`.

The mod clones the stock player button to create a new `My Squad` button and positions it in the same left-side menu stack. The button calls `SquadSideSelectionFlow.Open()`.

Verified behavior:

- the button is a cloned `DefaultUIButton`, not a custom prefab
- it uses a custom icon and localized title
- reconnect/minimized menu states re-sync its visibility

### Squad mode host

`SquadSideSelectionFlow.Open()` uses reflection into `MainMenuControllerClass.method_44()` to open the stock `MatchMakerSideSelectionScreen`.

Before the screen opens it:

- marks `SquadModeActive = true`
- captures the current matchmaker group snapshot
- hides the side-selection alpha label
- enables temporary `PlayerModelView.Show(...)` suppression so the stock side-selection player model views do not render

When squad mode closes or is aborted it:

- clears squad-mode flags
- restores the alpha label
- clears the opening group snapshot

### Side-selection patching

`MatchMakerSideSelectionScreen.Show(...)` is patched to detect `SquadModeActive`.

In squad mode it:

- hides native side-selection widgets such as PMC/Scav panels, health/random controls, descriptions, and stock model views
- rewrites the main caption to `My Squad`
- spawns two stock-style animated tabs by cloning Ragfair toggles:
    - `Roster`
    - `Settings`
- injects the pitFireTeam panels into the live side-selection screen transform
- rewires the stock back button so squad-mode back always exits to root and disables squad mode

On close it restores the hidden stock elements and retracts the injected panels.

## Localization

`My Squad` UI text is now loaded through the shared pitFireTeam language model.

Current behavior:

- client reads the active game language through `SharedGameSettingsClass`
- client posts the normalized locale plus embedded English fallback JSON to `/singleplayer/pitfireteam/lang`
- server creates or repairs `server/Resources/lang/en.json` from the embedded English when it is missing, corrupted, or missing keys
- server returns `server/Resources/lang/<locale>.json` merged with the editable English fallback
- built-in client fallback comes from `EmbeddedEnglishLanguageProvider`
- language is checked periodically at runtime and reloaded when the game language changes

Verified bundled language resources today:

- `en`
- `ru`

## Part 1: Roster

### What the roster tab is

The roster tab is the main `My Squad` landing tab. It is built by `SquadControlMenuUi` and hosted inside the squad-mode side-selection screen.

When stock trader chrome is available, the roster uses a cloned trader-card shell as its main panel background. If that template is not available, the code falls back to a plain custom panel.

### Data source

Roster entries are loaded from:

- `GET /singleplayer/pitfireteam/teammates`

Each tile is built from backend teammate data:

- `Aid` / account id
- social member id
- nickname
- level
- auto-join enabled flag

The roster is rebuilt on first injection and on explicit refresh requests. It also supports lighter tile-only refreshes for specific account ids after profile-side edits.

### Tile composition

Each roster entry is a runtime-created tile containing:

- background image + hover styling
- diagonal corner overlay
- portrait area
- level display
- nickname label
- delete button
- auto-join badge
- in-group badge

Portraits are loaded asynchronously and sequentially. The queue fetches `GetOtherPlayerProfile(accountId)` and then uses `PlayerIconImage.SetPresetIcon(...)`.

Important implementation details:

- portrait loading is deferred through a queue to avoid blasting the UI all at once
- loading indicators are tracked per account id
- tile rebuilds are versioned so stale portrait callbacks do not paint onto a replaced tile

### Empty state and add flow

If there are no teammates, the roster shows:

- an empty-state label
- a centered `+ Add teammate` button

The add button calls:

- `AddTeammateCreationFlow.Start(SquadSideSelectionFlow.Open)`

So teammate creation still reuses the stock account appearance flow, and successful completion returns back into `My Squad`.

### Tile interactions

Left click:

- opens teammate profile via `ItemUiContext.Instance.ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile)`

Right click:

- opens a context menu with:
    - `Invite to group` or `Remove from group`
    - `View profile`
    - `Auto join: On/Off`

The context menu prefers cloning the stock matchmaker/simple-context-menu template when available, and falls back to a custom runtime menu otherwise.

### Group integration

Roster group state is live-linked to `MatchmakerPlayerControllerClass`.

Supported group actions:

- invite teammate to group
- remove teammate from group through stock context interactions
- detect in-group state for badges
- show toast feedback for accepted/failed invite and removal flows

The roster also uses the opening group snapshot from `SquadSideSelectionFlow` so group badges can stay coherent while the side-selection screen is being opened.

### Auto-join integration

Auto-join toggles post to:

- `POST /singleplayer/pitfireteam/teammate/autojoin`

On success the roster updates the badge immediately and also updates `TeammateAutoJoinRuntime` suppression state locally.

### Delete flow

Delete is a modal confirmation overlay on top of the roster tab.

Confirmed behavior:

- the delete action resolves the live social member first
- it then removes the teammate through stock social/friends removal flow
- on success it refreshes the social list and rebuilds the roster

### Current roster limitations

- the roster itself does not contain a right-side detail pane; teammate detail still jumps into `OtherPlayerProfileScreen`
- portrait loading is sequential and intentionally delayed, so large rosters are stable but not instant
- delete still depends on social-list presence being valid at the moment of the action

## Part 2: Settings

### What the settings tab is

The settings tab is another `SquadControlMenuUi` panel injected into the same squad-mode side-selection host.

It is not a stock `SettingsScreen` controller. It is a custom panel that tries to clone stock controls where possible.

### Panel construction

The settings shell is sized to match the roster shell height so tab switching feels like one coherent screen.

The panel builds:

- a scrollable viewport
- section headers
- one row per config entry

Where possible it clones stock EFT controls from `GameSettingsTab`:

- `UpdatableToggle` for booleans
- `NumberSlider` for integer ranges

If a stock template cannot be found, it falls back to basic runtime-created controls.

### Current editing model

Important difference from the March design investigation:

- the current `Settings` tab writes directly to live BepInEx config entries
- it saves immediately through `pitFireTeam.Instance?.Config.Save()`
- it does not use a temporary view-model
- it does not have save/cancel/default buttons
- it does not prompt for unsaved changes

So the current tab is a runtime config editor, not a stock-style staged settings screen.

### Current section split

Verified sections built today:

- `Base Settings`
- `Follow Settings`
- `Combat Settings`
- `Input Settings`
- `Raid Settings`
- `Miscellaneous`

Verified entry groups:

- `Base Settings`
    - `spawnPoint`
    - `englishBear`
    - `pingRadioVolume`
    - `pingTime`
- `Follow Settings`
    - `goToDistance`
- `Combat Settings`
    - `botGrenades`
    - `enemyMarker`
    - `statusSound`
    - `enemyRemember`
    - `scanDistance`
    - `botTalk`
- `Input Settings`
    - `pingKey`
    - `contactKey`
    - `overThereKey`
- `Raid Settings`
    - `pickupEnabled`
    - `tieredPickup`
    - `maximumPickup`
    - `recruitPickup`
    - `npcSendMessage`
    - `pitFireTeamFLAG`
    - `badGuy`
- `Miscellaneous`
    - `teleportKey`
    - `healKey`
    - `heatlhMultiplier`
    - `botPrefetch`
    - debug builds also show `battleRecorderEnabled` and `battleRecorderSnapshotIntervalMs`

### Current control behavior

Supported control types:

- `bool` -> toggle
- ranged `int` -> slider
- `KeyboardShortcut` -> press-to-capture button
- everything else -> read-only text fallback

Shortcut capture behavior:

- opens capture mode when its action button is clicked
- `Escape` cancels capture
- `Backspace` or `Delete` clears the shortcut
- otherwise the next non-modifier key becomes the main key and current Ctrl/Shift/Alt state becomes modifiers

### Raid overlay path

There is also a separate in-raid-style access point for the settings panel:

- `Squad Settings` button cloned from the menu `hide/resume` button

This opens the same settings content inside `screenRoot` as a standalone overlay with a cloned back button. It shows only the settings tab and is separate from the side-selection-hosted `My Squad` entry flow.

### Current settings limitations

- no staged save/cancel flow
- no settings search/filter
- no per-setting dependency/disable logic beyond what each control directly supports
- still tightly coupled to BepInEx config entries rather than a dedicated persisted UI model

## Part 3: Profile Screen

### What the profile screen is

The profile screen is not part of the side-selection host. It is the stock `OtherPlayerProfileScreen`, patched when the viewed profile is a teammate rather than the local player.

This is the current detail/customization surface for a selected squad member.

### Entry and return path

Roster profile open calls:

- `ShowPlayerProfileScreen(accountId, EItemViewType.OtherPlayerProfile)`

Before opening, the code sets a pending back override. When the profile screen closes, that override re-opens `My Squad`, so the user returns to the roster rather than being dropped somewhere else in menu history.

### Teammate gating

The profile patch only activates when:

- the viewed profile is not the local player
- teammate profile options load successfully from:
    - `POST /singleplayer/pitfireteam/teammate/profile/options`
- at least one loadout option exists

If those conditions fail, the stock profile mostly stays in charge.

### What gets changed on teammate profile

For teammate profiles the patch:

- hides stock report actions
- clears the stock right-side profile content blocks
- reuses the stock clothing panel for suit selection
- injects a cloned second clothing-style row for loadout + tactic
- injects an aggression slider row below that
- injects an `Edit Loadout` button row below that
- clones and hosts a filtered `SkillsScreen`
- moves the faction badge down to fit the custom rows
- turns the stock hideout button into `EDIT NAME`

### Persisted profile actions

Verified persisted actions today:

- suit/body/feet change
    - `POST /singleplayer/pitfireteam/teammate/profile/suit`
- rename
    - `POST /singleplayer/pitfireteam/teammate/profile/rename`
- selected loadout from saved player equipment builds
    - `POST /singleplayer/pitfireteam/teammate/profile/loadout`
- tactic
    - `POST /singleplayer/pitfireteam/teammate/profile/tactic`
- aggression
    - `POST /singleplayer/pitfireteam/teammate/profile/aggression`

After successful profile-side persistence the code marks the squad roster dirty so the next `My Squad` reopen can refresh changed tiles.

### Loadout and tactic selectors

The loadout/tactic row is still based on `InventoryClothingSelectionPanel`.

Upper dropdown:

- current teammate equipment selection
- populated from:
    - `Default`
    - player custom equipment builds returned by the backend

Lower dropdown:

- current tactic
- populated from backend tactic options, with a client fallback list of:
    - `Rifleman`
    - `Marksman`
- `Protector` is intentionally hidden for the beta release and old persisted values normalize back to `Rifleman`

Loadout selection persists immediately through the backend and refreshes the live profile visualization.

### Aggression row

Aggression is a cloned row containing a stock-style slider.

Behavior:

- current value comes from teammate profile options
- values are clamped to `0..100`
- persistence is delayed/debounced before posting to backend
- marksman tactic uses aggression as a tactic-relative offensive auto-search control

### Rename flow

Rename uses a custom overlay on top of the profile screen.

Confirmed implementation:

- clones a live stock `NicknameField`
- reuses stock nickname validation
- uses a cloned stock button template for save
- persists through the teammate rename route
- refreshes social list and squad roster state after success

### Skills panel

The profile patch also clones a stock `SkillsScreen`, builds a filtered skills profile snapshot, and shows follower-relevant skills inside the profile right side.

This is teammate-only UI and is destroyed/reset on profile close.

### Current profile limitations

- teammate profile still lives inside `OtherPlayerProfileScreen`, not a dedicated squad detail screen
- voice/head editing is not implemented in this screen
- right-side content is heavily patched and reset-sensitive, so this remains a fragile area

## Current Custom Loadout Editor Status

This is the status of the `Edit Loadout` overlay specifically.

### Current behavior

The `Edit Loadout` button opens a full-screen modal overlay on top of the teammate profile screen.

The overlay currently builds:

- draggable header bar
- subtitle explaining that this is a cloned/local editing surface
- left section: cloned fake player stash
- right section: cloned follower inventory/equipment view
- cancel button
- done button

Confirmed implementation details:

- the left stash is a fake stash created with `CreateFakeStash(...)` and populated by cloning the real player stash items
- the right follower inventory is built from cloned teammate equipment and a fake `TraderControllerClass`
- secure container is removed from the edited equipment before display/save
- the follower containers panel currently renders only:
    - `TacticalVest`
    - `Pockets`
    - `Backpack`
- the follower equipment tab currently renders:
    - scabbard
    - holster
    - both primary weapons
    - eyewear/face cover/headwear/earpiece
    - armor vest
    - armband
- dogtag is removed/hidden from the follower-side editor view
- the `ChracterGear` image is hidden by disabling its `Image`
- the header text is manually rewritten to the teammate name

Verified save behavior:

- the editor uses a cloned local stash + cloned follower equipment session
- item edits stay local until `Done`
- if the selected loadout is a custom player equipment build:
    - `Done` opens the stock preset naming dialog
    - saving with the same name overwrites that custom preset
    - saving with a new name creates a new custom preset
    - the teammate selected loadout is then updated to that saved preset
- if the selected loadout is `Default`:
    - the editor now opens the teammate's actual current default equipment instead of stale pre-switch profile equipment
    - `Done` does not show the preset naming dialog
    - it saves directly as the bot's default equipment and closes

### Current limitations

- the editor still uses cloned/local items and does not consume real player stash items
- immersive/real-item loadout editing is not implemented
