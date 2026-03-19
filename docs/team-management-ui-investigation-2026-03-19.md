# Team Management UI Investigation

Date: 2026-03-19

## Goal

Document the stock EFT 4.x UI surfaces that friendlySAIN already touches, plus the stock trader/settings UI patterns that are relevant for a future dedicated Team Management screen.

Target feature direction:

- add a dedicated Team screen
- move friendlySAIN runtime/user settings out of BepInEx config and into a Team `Settings` tab
- add a Team `Roster` tab for teammate/member management

This is a planning and documentation pass only. No runtime UI behavior is proposed here yet.

## Scope Of This Investigation

Covered:

- current friendlySAIN screen integration points
- stock 4.x profile screen, account creation flow, and raid-prep screens already used by the mod
- stock trader portrait pattern in the trader screen
- stock settings screen structure, with focus on `Game` and `PostFX`

Not covered:

- Unity prefab hierarchy / exact RectTransform coordinates
- asset bundle inspection
- final implementation details for a new Team screen

Important limitation:

- decompiled C# gives reliable screen composition, fields, events, and flow
- exact visual layout beyond serialized field composition is not fully visible without prefab/asset inspection

## Current friendlySAIN UI Touchpoints

### 1. Friends / social entry point

friendlySAIN injects a custom `+ Add teammate` row into the friends panel instead of creating a new screen from scratch.

Relevant files:

- `friendlySAIN/client/Patches/ChatFriendsPanelPatch.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Chat/ChatFriendsPanel.cs`

Observed pattern:

- stock `ChatFriendsPanel` has:
  - caption panel
  - friends and requests buttons
  - search input
  - friends list panel
  - requests panel
  - members panel
- friendlySAIN inserts a new button under the search input by creating a plain `GameObject` with:
  - `RectTransform`
  - `LayoutElement`
  - `Image`
  - `Button`
  - child `TextMeshProUGUI`

Implication for Team screen:

- the game tolerates lightweight runtime UI injection into existing panels
- for a major new screen, cloning stock button/text styles is safer than inventing a totally custom control language

### 2. Teammate creation via stock account-side-selection flow

friendlySAIN reuses the stock account creation side/head/nickname flow and converts it into teammate creation.

Relevant files:

- `friendlySAIN/client/Modules/AddTeammateCreationFlow.cs`
- `friendlySAIN/client/Patches/AddTeammateCreationFlowPatch.cs`
- `friendlySAIN/client/Patches/AddTeammateHeadSelectionPatch.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/AccountSideSelectionScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/EftAccountSideSelectionScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/HeadSelectionState.cs`

Verified stock flow:

- `AccountSideSelectionScreen` has two stock states:
  - `_sideSelectionState`
  - `_headSelectionState`
- it tracks current state with `int_0`
- `_nextButton` and `_backButton` drive state switching
- final submit path goes through nickname validation and then `ScreenController.ShowNextScreen()`

What friendlySAIN does:

- creates `EftAccountSideSelectionScreen.GClass3905` manually
- preloads stock default bear/usec preview profiles
- forces the player side from the current session profile
- auto-skips side selection and lands directly on head selection
- hides skipped side-selection visuals
- rewires next/back button behavior for teammate create/return
- validates nickname locally through stock `NicknameField`
- posts `{ nickname, voice, head }` to `/singleplayer/friendlysain/teammate/create`

Important UI pattern:

- this is not a new screen, it is a stock screen with:
  - a forced initial state
  - some hidden stock visuals
  - button rewiring
  - alternate completion side effects

Implication for Team screen:

- stock screens are reusable if their controller and state model already match most of the desired workflow
- for Team Management, this pattern is good for one-off flows like create teammate
- it is not ideal for a long-lived main management surface with multiple tabs

### 3. Other profile screen customization

friendlySAIN repurposes the stock other-player profile screen for teammate management.

Relevant files:

- `friendlySAIN/client/Patches/OtherPlayerProfileScreenPatch.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/OtherPlayerProfileScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/InventoryPlayerModelWithStatsWindow.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/InventoryClothingSelectionPanel.cs`

Verified stock profile composition:

- `OtherPlayerProfileScreen.Show(...)` configures:
  - `_playerModelWithStatsWindow`
  - `_overallStatsPanel`
  - favorite items blocks
  - `_reportPanel`
  - `_hideoutButton`
- `InventoryPlayerModelWithStatsWindow` contains:
  - `_nicknameLabel`
  - `_playerModelView`
  - `_clothingPanel`
  - level, side, prestige, short stats
- `InventoryClothingSelectionPanel` is a two-dropdown wrapper:
  - `_upperButtonDropDown`
  - `_lowerButtonDropDown`

What friendlySAIN does:

- hides stock report/hideout behavior for teammate view
- reuses the stock clothing panel for body/feet changes
- clones that clothing panel to create a second dropdown row for loadouts
- replaces stock hideout button behavior with `EDIT NAME`
- builds a full-screen overlay for rename
- refreshes the profile render after backend changes

Important UI pattern:

- the profile screen is a strong example of "augment stock, do not replace all stock"
- the mod succeeds by:
  - locating existing child panels
  - cloning a compatible stock panel
  - reusing stock controls and callbacks
  - keeping backend persistence separate from visual refresh

Implication for Team screen:

- a future Team `Roster` detail pane can likely reuse the same ideas:
  - stock dropdown rows
  - stock nickname validation field
  - `PlayerModelView` / `InventoryPlayerModelWithStatsWindow` style model presentation
- this screen also proves overlay-based modal editing is acceptable for narrow edit actions

### 4. Rename overlay pattern

The nickname edit path in friendlySAIN is a fully custom overlay built on top of a stock screen.

Relevant file:

- `friendlySAIN/client/Patches/OtherPlayerProfileScreenPatch.cs`

Observed pattern:

- overlay root covers full screen
- custom dark panel is centered
- stock `NicknameField` is cloned from an existing live instance
- stock `DefaultUIButton` is cloned for save/cancel
- stock validation callbacks are preserved
- backend persistence happens on save, then the profile label and social list are refreshed

Why this matters:

- this is the clearest proof that the mod can safely do small modal UI without needing a new stock screen controller every time

Recommendation:

- keep this pattern for narrow edit dialogs inside Team Management:
  - rename member
  - maybe change call sign / label
  - maybe confirm delete
- do not use this overlay pattern as the main Team screen container

### 5. Raid preparation screens already touched

friendlySAIN already modifies multiple stock pre-raid screens to keep synthetic teammates compatible with offline/local flow.

Relevant files:

- `friendlySAIN/client/Patches/RaidStartPatch.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchMakerAcceptScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchmakerTimeHasCome.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/PartyInfoPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchMakerPlayerPreview.cs`

Observed stock composition:

- `MatchMakerAcceptScreen` contains:
  - `_playerModelView` for the local player preview
  - `_groupPreview` for group slots
  - ready/list controls
  - location/conditions widgets
- `MatchmakerTimeHasCome` contains:
  - `_partyInfoPanel` for the player list
  - `_playerModelView` for the selected player preview
  - banners, sub-caption, deploying text
- `PartyInfoPanel` is basically a list panel with click-through to inspect equipment

What friendlySAIN does:

- keeps stock ready-screen gating working by temporarily hiding synthetic teammates from local-only checks
- repopulates group preview and loading preview with synthetic teammates
- fetches `GetOtherPlayerProfile(...)` to supply visual state for teammate previews
- does not replace the whole stock screen controller; it patches around it

Implication for Team screen:

- the matchmaker UI gives a useful stock reference for a Team `Roster` tab:
  - left list of members
  - right preview/detail area
  - click row to inspect selected member

## Stock Trader UI: Top-Right Player Portrait Pattern

The top-right player image in trader UI is not a live 3D `PlayerModelView`.

Relevant files:

- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/TraderScreensGroup.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/TradingPlayerPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/PlayerIcons/PlayerIconImage.cs`

Verified behavior:

- `TraderScreensGroup` owns `_playerPanel`
- `_playerPanel` is `TradingPlayerPanel`
- `TradingPlayerPanel.Set(profile, traderInfo)` calls `_playerIconImage.SetPresetIcon(profile)`
- `PlayerIconImage.SetPresetIcon(profile)` creates an icon request from:
  - `profile.Customization`
  - `profile.Inventory.Equipment.CloneVisibleItem<InventoryEquipment>()`
- the UI finally shows a generated sprite through `_pmcPreview`, not a 3D scene object

Why this matters:

- this is a cheap portrait solution
- it avoids live `PlayerModelView` lifetime, loading, rotation, and scene setup
- it is well-suited for small persistent portrait areas in menus

Recommendation for Team screen:

- use `PlayerIconImage` for list rows, compact headers, and top-right summary portraits
- use `PlayerModelView` only in detail/edit contexts where:
  - clothing
  - equipment preview
  - live body rotation
  - exact appearance confirmation
  matter more than cost and simplicity

## Stock Settings Screen Structure

Relevant files:

- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/SettingsScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/SettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/MainMenuControllerClass.cs`

Verified top-level structure:

- one `SettingsScreen` with five tab groups:
  - `Game`
  - `Screen`
  - `PostFX`
  - `Sound`
  - `Control`
- each tab is a `SettingsTab`
- save/back/default buttons are screen-level, not per-tab
- the screen maintains temp settings separate from original manager settings

Important controller variants:

- `SettingsScreen.GClass3895`
  - normal menu settings controller
- `SettingsScreen.GClass3896`
  - in-raid variant
  - enables environment visibility and UI blockers
- `SettingsScreen.GClass3897`
  - searching-for-game variant
  - also uses UI blockers

Important architectural pattern:

- stock settings does not write directly into final live settings while the user edits
- it keeps temporary cloned settings and only applies them on save
- it also handles "leave with unsaved changes" via `CloseScreenInterruption`

Implication for Team screen:

- this temp-edit/save/cancel pattern is the correct mental model for Team `Settings`
- moving friendlySAIN settings out of BepInEx should follow a temporary view-model + explicit save model, not immediate mutation on every click

## Game Tab Investigation

Relevant files:

- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/GameSettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/GClass1085.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/GClass1076.cs`

### Stock input types used by Game tab

Dropdowns:

- `_interfaceLanguage`
- `_interfaceEnvironmentType`
- `_profileIcon`
- `_quickSlotsDropdown`
- `_staminaDropdown`
- `_healthDropdown`
- `_healthColorDropdown`
- `_highlightScopeDropdown`
- `_priorityWindowMode`
- `_notificationTypeDropdown`
- `_itemUseTypeDropdown`
- `_autoVaultingTypeDropdown`
- `_continuousHealDropdown`
- `_connectionTypeDropdown`
- `_wishlistNotificationsDropdown`
- `_autoAddToWishlistDropdown`
- `_questItemNotificationDropdown`

Toggles:

- `_subtitles` (currently fake/blocked)
- `_tutorialHints` (currently fake/blocked)
- `_dontAllowToAddMe` (currently fake/blocked in this tab; actual restriction is handled via profile settings path)
- `_clearRAM`
- `_setAffinityToLogicalCores`
- `_enableHideoutPreload`
- `_enableStreamerMode`
- `_enableBlockInvites`
- `_malfunctionVisability`
- `_traderIntermediateScreen`
- `_blood` (fake/blocked)
- `_badLanguage` (fake)

Sliders:

- `_aimingDeadzone` (fake)
- `_fov`
- `_headbobbing`

Special controls:

- `_nicknameInput`
- `_changeNicknameButton`
- `_clearWishlistButton`
- `_editInterfaceLayout`

### Backing Game settings currently exposed by stock data model

`GClass1085` currently contains:

- `Language`
- `SelectedMemberCategory`
- `QuickSlotsVisibility`
- `StaminaVisibility`
- `HealthVisibility`
- `HealthColor`
- `NotificationTransportType`
- `ConnectionType`
- `HighlightScope`
- `ItemQuickUseMode`
- `PriorityWindowMode`
- `AutoVaultingMode`
- `ContinuousHealMode`
- `QuestItemNotificationMode`
- `WishlistNotificationsType`
- `AutoAddToWishlist`
- `TacticalInputMode`
- `FieldOfView`
- `HeadBobbing`
- `AutoEmptyWorkingSet`
- `SetAffinityToLogicalCores`
- `EnableHideoutPreload`
- `StreamerModeEnabled`
- `BlockGroupInvites`
- `MalfunctionVisability`
- `TradingIntermediateScreen`
- `RagfairLinesCount`
- `EnvironmentUiType`

### Layout observations from code

What is verifiable:

- the Game tab is a mixed-form settings page
- it expects a blend of dropdowns, toggles, sliders, and a few special action controls
- some controls are grouped behind blockers rather than removed
- the tab is large enough that it already tolerates many unrelated setting rows

What is not fully verifiable from decompiled code alone:

- exact section order on screen
- exact spacing, separators, and anchoring
- which controls share the same row prefab versus unique prefab layouts

### Relevance for Team Settings

The Game tab is the best stock reference for a Team `Settings` tab because:

- it already handles many heterogeneous control types in one tab
- it uses stock dropdown/toggle/slider controls we can imitate
- it keeps secondary actions like nickname change separate from normal settings rows

Recommended Team settings mapping style:

- booleans -> stock-style `UpdatableToggle`
- ranged numeric values -> `NumberSlider`
- enum-like behavior modes -> `DropDownBox`
- hotkeys -> probably a second-phase problem, since stock game tab here does not expose keybind capture directly

## PostFX Tab Investigation

Relevant files:

- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/PostFXSettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/GClass1083.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Screens/PostFXPreviewScreen.cs`

### Stock input types used by PostFX tab

Toggle:

- `_overallToggle`

Sliders:

- `_brightness`
- `_saturation`
- `_clarity`
- `_colorfulness`
- `_lumaSharpen`
- `_adaptiveSharpen`
- `_filterIntensity`
- `_colorblindnessIntensity`

Dropdowns:

- `_colorGrading`
- `_colorblindnessType`

Action button:

- `_visualizeButton`
  - only active in raid
  - returns to root and opens `PostFXPreview`

### Backing PostFX settings currently exposed by stock data model

`GClass1083` currently contains:

- `EnablePostFx`
- `Brightness`
- `Saturation`
- `Clarity`
- `Colorfulness`
- `LumaSharpen`
- `AdaptiveSharpen`
- `ColorFilterType`
- `Intensity`
- `ColorBlindnessType`
- `ColorBlindnessIntensity`

### Layout observations from code

The PostFX tab is much simpler than the Game tab:

- one master enable toggle
- a set of related controls that are visually locked/unlocked together through `_toggleRelatedCanvases`
- two dropdowns plus mostly sliders
- one optional preview action

### Relevance for Team Settings

The PostFX tab is a good reference for:

- a compact tab with a master enable toggle
- grouped controls that should gray out when disabled
- a clear "all controls belong to one feature" layout

This pattern may fit friendlySAIN features such as:

- team markers enabled/disabled
- teammate ping enabled/disabled
- follower utility features enabled/disabled

## What Should Move From BepInEx Config

Current friendlySAIN BepInEx settings registered in `friendlyPlugin.cs`:

- `scanDistance`
- `patrolRadius`
- `enemyRemember`
- `heatlhMultiplier`
- `statusSound`
- `enemyMarker`
- `npcSendMessage`
- `friendlySAINFLAG`
- `badGuy`
- `pmcArmbands`
- `englishBear`
- `botGrenades`
- `pingKey`
- `pingRadioVolume`
- `pingTime`
- `contactKey`
- `teleportKey`
- `healKey`
- `botPrefetch`
- `botTalk`
- `spawnPoint`

Practical split for a future Team screen:

Likely Team `Settings` tab candidates:

- `scanDistance`
- `patrolRadius`
- `enemyRemember`
- `heatlhMultiplier`
- `statusSound`
- `enemyMarker`
- `npcSendMessage`
- `friendlySAINFLAG`
- `badGuy`
- `pmcArmbands`
- `englishBear`
- `botGrenades`
- `pingRadioVolume`
- `pingTime`
- `botPrefetch`
- `botTalk`
- `spawnPoint`

Probably not phase-1 Team settings candidates:

- keybinds:
  - `pingKey`
  - `contactKey`
  - `teleportKey`
  - `healKey`

Reason:

- stock Game settings investigated here does not provide a simple reusable keybind capture control pattern
- keybind migration likely needs a separate input-binding investigation

## Recommended Team Screen Shape

### Top-level screen

Create a dedicated screen, not another patchwork extension of `OtherPlayerProfileScreen`.

Why:

- Team Management now spans:
  - teammate creation entry
  - roster browsing
  - teammate detail/customization
  - plugin settings
- that is broader than a profile screen customization

### Tab model

Start with two tabs:

- `Settings`
- `Roster`

Stock reference to emulate:

- `SettingsScreen`

Recommended behavior:

- temp data while editing
- explicit `Save`
- explicit `Back/Cancel`
- unsaved-changes confirmation before closing

### Settings tab

Use the stock Game/PostFX mental model:

- mixed rows of dropdowns, toggles, sliders
- optional master toggles for grouped features
- no keybind capture in phase 1

### Roster tab

Use a left-list / right-detail layout.

Stock references to emulate:

- `PartyInfoPanel`
- `MatchMakerGroupPreview`
- `OtherPlayerProfileScreen`

Recommended split:

- left side:
  - teammate/member list
  - compact portrait via `PlayerIconImage`
  - status tags / tactic / loadout summary
- right side:
  - selected teammate detail
  - appearance/loadout/tactic controls
  - rename and delete actions
  - optional live `PlayerModelView`

### Portrait strategy

Use both portrait modes intentionally:

- `PlayerIconImage`
  - roster rows
  - compact headers
  - cheap top-right summary portrait
- `PlayerModelView`
  - selected roster member detail view
  - appearance editing
  - loadout visualization

### Modal actions

Keep overlay pattern for small focused actions:

- rename
- confirm delete
- maybe create/import teammate helper dialogs

Do not use overlays as a replacement for the main Team screen structure.

## Implementation Advice For Phase 2+

### Recommended order

1. Build the Team screen shell with tabs only.
2. Implement Team `Settings` using temp view-model data, without removing BepInEx config yet.
3. Implement Team `Roster` list with `PlayerIconImage`.
4. Add selected-member detail pane.
5. Migrate existing profile-screen teammate edit actions into the Team screen.
6. Only after that, decide which old `OtherPlayerProfileScreen` patches can be retired.

### Runtime stability advice

- prefer a new dedicated screen/controller over forcing more behavior into `OtherPlayerProfileScreen`
- reuse stock controls and stock subpanels where possible
- keep backend persistence separate from immediate UI selection state
- do not migrate keybinds until input-binding UI is separately understood

## Useful File Index

friendlySAIN:

- `friendlySAIN/client/friendlyPlugin.cs`
- `friendlySAIN/client/Modules/AddTeammateCreationFlow.cs`
- `friendlySAIN/client/Patches/ChatFriendsPanelPatch.cs`
- `friendlySAIN/client/Patches/AddTeammateCreationFlowPatch.cs`
- `friendlySAIN/client/Patches/AddTeammateHeadSelectionPatch.cs`
- `friendlySAIN/client/Patches/OtherPlayerProfileScreenPatch.cs`
- `friendlySAIN/client/Patches/SocialPatch.cs`
- `friendlySAIN/client/Patches/RaidStartPatch.cs`

Decompiled EFT 4.x:

- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Chat/ChatFriendsPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/AccountSideSelectionScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/EftAccountSideSelectionScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/HeadSelectionState.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/OtherPlayerProfileScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/InventoryPlayerModelWithStatsWindow.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/InventoryClothingSelectionPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/TraderScreensGroup.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/TradingPlayerPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/PlayerIcons/PlayerIconImage.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/SettingsScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/SettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/GameSettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Settings/PostFXSettingsTab.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/GClass1085.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/GClass1083.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchMakerAcceptScreen.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchmakerTimeHasCome.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/PartyInfoPanel.cs`
- `Client-Decompiled-4.x/Assembly-CSharp/EFT/UI/Matchmaker/MatchMakerPlayerPreview.cs`

## Conclusion

The stock UI patterns suggest the future Team Management feature should not be a further extension of the current profile screen patches alone.

Best current direction:

- dedicated Team screen
- stock-style tabs
- Team `Settings` based on the Game/PostFX tab interaction model
- Team `Roster` based on matchmaker/profile list-detail patterns
- compact portraits via `PlayerIconImage`
- detail previews via `PlayerModelView`
- overlays reserved for narrow edit actions
