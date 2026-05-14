# Loadout Management

Date: 2026-05-13

## Scope

This document tracks the current `Loadout Management` setting and the first implementation phase around default teammate loadouts.

Current phase focus:

- expose the mode in `My Squad -> Settings`
- confirm before switching modes
- preserve each teammate's saved `Default` gear and select `Default` when the mode changes
- test default-loadout spawn preparation for the four modes
- test Immersive-style death gear loss for teammates using `Default`

This is not yet the full real-stash economy implementation. Default-loadout real item ownership transfer is implemented for `Restricted`, `Immersive`, and `Realistic` (internal mode value: `Extreme`). The non-`Simple` profile UI intentionally hides the saved-loadout dropdown and uses `Default` as the editable real-gear surface, with `KIT LOADOUTS` reserved as the way to acquire a full kit.

See `docs/Buy Screen.md` for the current stock `EquipmentBuildsScreen` reuse, buy-mode UI changes, and kit price-display behavior.

## UI Behavior

`Loadout Management` is a dedicated settings group in the `My Squad` settings tab, placed after `Combat Settings`.

The group is hidden while the settings panel is opened from a raid-restricted context, including the in-raid `Squad Settings` overlay.

It contains four mutually exclusive mode choices:

- `Simple`
- `Restricted`
- `Immersive`
- `Realistic` (stored internally as `Extreme`)

The UI uses cloned Ragfair `UIAnimatedToggleSpawner` controls under a Unity `ToggleGroup`. Each option is rendered as its own vertical row: description text on the left, radio-style toggle on the right.

When the user clicks a different mode:

1. the current visible selection is kept unchanged
2. a confirmation overlay opens
3. `Continue` applies the new mode
4. the overlay `X` cancels and keeps the previous mode

Confirmation text:

- title: `SWITCH LOADOUT MANAGMENT`
- question: `Switching loadout management will switch all teammates to their Default loadout.`
- confirm button: `Continue`

After confirmation, the client saves the config, syncs the mode to the server, and updates the visible toggle state in place.

## Modes

### Simple

`Simple` is the current baseline behavior.

The user can edit or choose a follower loadout without requiring the gear to be physically consumed from the player's inventory.

Current intended constraints:

- `Edit Loadout` works against the stash-side editor surface, so the player's currently worn gear is not part of that editor view
- equipment preset dropdown availability must exclude items currently equipped by the player or already reserved/equipped by other teammates
- spawned follower gear is protected
- gear is not lost on follower death
- spawned follower gear cannot be looted

### Restricted

Target behavior:

- any gear used for a follower loadout is taken from the player's stash
- gear is not lost on follower death
- spawned follower gear cannot be looted

Current phase behavior:

- mode exists in UI and server settings
- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit
- editing `Default` uses real player stash item ids instead of cloned ids
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- spawned follower gear is protected from raid loss and cannot be looted
- raid outcome does not persist durability damage or death loss

### Immersive

Target behavior:

- same stash-consumption rules as `Restricted`
- follower equipment can be damaged
- if a follower dies, their gear is lost
- dead follower gear can be looted

Current phase behavior for `Default` only:

- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit
- editing `Default` uses real player stash item ids instead of cloned ids
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- if a teammate dies while using `Default`, saved default equipment is stripped down to equipment root plus `Scabbard`/knife and armband
- if a teammate extracts while using `Default`, the live in-raid equipment state is saved back as the new `Default`
- the scabbard/knife and armband are preserved because they match the player-like non-loss exceptions
- secure container is ignored for Immersive death loss and will be regenerated by the normal spawn preparation path
- if a future edit removes the knife, spawn preparation injects a fallback knife before the teammate is used

### Realistic

`Realistic` is stored internally as `Extreme`. It is Immersive-like with an additional secure-container restriction.

Target behavior:

- same death/loss direction as `Immersive`
- the secure container slot is no longer auto-managed
- no protected meds or spare ammo are injected into the secure container

Current phase behavior:

- switching to this mode preserves each teammate's current `Default` gear and selects `Default`
- switching into or out of this mode strips any existing secure-container tree from saved teammate `Default` loadouts to prevent carrying a hidden managed container into the editable Realistic slot
- the teammate profile hides the saved-loadout dropdown and shows the `KIT LOADOUTS` button
- buying a kit sends the teammate's previous active `Default` kit back through the pitFireTeam courier before equipping the newly purchased kit; the editable secure container is included in that delivery
- newly created teammates receive an initial editable secure container based on level: Beta below 15, Epsilon below 30, Gamma at 30+
- editing `Default` uses real player stash item ids instead of cloned ids
- the edit-loadout panel shows the teammate secure container slot so it can be edited
- pressing `Done` commits real item movement between the player stash and teammate default equipment
- items moved onto the teammate are removed from the player stash
- items moved back from the teammate are returned to the player stash
- if a teammate dies while using `Default`, saved default equipment is stripped down to equipment root plus `Scabbard`/knife, armband, and secure container contents
- if a teammate extracts while using `Default`, the live in-raid equipment state, including the editable secure-container tree, is saved back as the new `Default`
- spawn preparation does not add, replace, or fill the secure container
- any secure-container contents are whatever the saved `Default` currently has

## Server Behavior

The server receives the active mode through the normal server-settings sync path.

When `loadoutManagementMode` changes:

1. the server keeps every teammate's saved inventory and `Default` equipment snapshot unchanged
2. if the change moves into or out of `Realistic` / internal `Extreme`, the saved `Default` secure-container tree is removed and the default snapshot is overwritten
3. each teammate's selected loadout is set to `Default`
4. teammate settings are saved

This avoids on-the-fly ownership checks against non-default selected equipment when the economic rules change, without destroying the existing `Default` gear.

## Real Default Loadout Commit

Real item ownership transfer currently applies only when all of these are true:

- the active mode is `Restricted`, `Immersive`, or `Realistic`/`Extreme`
- the user is editing a teammate's `Default` loadout
- the user presses `Done`

The edit overlay itself is still staged. Dragging items in the editor does not change the saved profile or live stash until `Done`.

On `Done`, the client sends two item snapshots to the server:

- sanitized teammate equipment
- staged player stash

The server is authoritative for the commit:

1. validate the submitted player stash root matches the active profile stash
2. build the allowed moved-item set from the current player stash tree plus the current teammate inventory
3. reject submitted equipment or stash items outside that allowed set
4. reject overlap where the same item remains both in teammate equipment and player stash
5. reject player-equipped items being submitted as teammate equipment
6. replace the player stash tree in the profile with the submitted staged stash
7. replace teammate default equipment with the submitted staged equipment, preserving special follower-only items where needed
8. save the player profile, teammate profile, teammate settings, and default snapshot

After the save succeeds, the server returns the saved player stash snapshot to the client.

## Kit Purchase

`KIT LOADOUTS` is available in `Restricted`, `Immersive`, and `Realistic` / internal `Extreme`. `Simple` keeps the saved-loadout dropdown and does not use the buyout screen.

The buy screen reuses EFT's stock `EquipmentBuildsScreen` in a custom teammate-buy mode. The selected build is priced with market-facing item prices, including nested weapon mods, armor plates, armor inserts, magazine contents, and container contents.

When the user confirms a purchase:

1. if `Use items in stash` is enabled, the server consumes the exact stash template/count summary shown in the overlay
2. the server deducts the quoted rouble price
3. the server sends the teammate's previous active `Default` kit by courier delivery
4. because the previous kit is delivered by mail, stash space is not checked as part of the buy transaction
5. the selected build is cloned with fresh ids and saved as the teammate's current equipment and new `Default`
6. the player profile and teammate profile are saved together
7. the saved player stash snapshot is returned to the client for live refresh

The old-kit delivery intentionally does not include the teammate equipment root, dogtag, or special slots. Pocket contents are delivered as normal reward roots. Non-Realistic managed secure containers are skipped because they are generated support inventory, while Realistic secure containers and their contents are delivered because Realistic treats that slot as player-like gear.

## Repair

Repair is available from the teammate loadout editor for repairable teammate equipment in all loadout-management modes.

Repair follows the real teammate-equipment rule even in `Simple`:

- the repaired item is updated on the teammate's current `Default` equipment/profile
- player repair kits and repair-related player profile changes are consumed through the stock repair service
- saved player equipment presets are not changed by repair
- pressing `Done` remains the action that writes editor changes back to the selected player equipment preset in the clone/preset flow

## Live Stash Refresh

The client does not use live item-move transactions for this feature. Earlier experiments with direct `Discard`, `Add`, `Move`, or inventory-controller transactions were unsafe for bot-to-player returns.

Instead, the client computes a backend-style stash delta from:

- the current live player stash
- the server-saved player stash returned by the commit route

That delta is applied through EFT's profile updater (`GClass2331.UpdateProfile`) while the loading indicator is visible.

Important behavior:

- player-to-bot transfers usually become `del` entries from the live stash
- bot-to-player transfers become `new` entries into the live stash
- when a returned item is placed inside an existing nested container, the client replaces the containing top-level container tree: delete the live container tree, then add the server-saved container tree
- this nested-container replacement is required because EFT's backend updater can add a top-level item tree into an existing container, but cannot target an existing nested live container for a standalone `new` item
- if live refresh fails, the server save remains valid and the client falls back to the restart-required warning

## Spawn Preparation

Follower spawn preparation currently uses the saved teammate profile clone before returning follower details to the client.

For every mode:

- the teammate clone receives the configured health multiplier
- the teammate is guaranteed to have a `Scabbard` knife

For non-Realistic modes:

- saved `Default` snapshots do not keep a secure-container tree
- saved teammate profiles also strip the secure-container tree, so generated meds and ammo are not remembered between raids
- the spawn clone is guaranteed to have the mod-managed hidden secure container
- the secure container is prepared with the existing protected supply package
- this currently includes medical and ammo support items

For `Realistic` / internal `Extreme`:

- the secure container slot is not auto-managed during spawn preparation
- no protected meds or ammo are injected
- the edit-loadout panel shows the secure container slot so the player can add, remove, or change it through the staged `Default` editor

## Death Handling

Current death-loss logic applies only to teammates whose selected loadout is `Default`.

When raid outcome persistence sees a teammate who:

- did not escape
- is in `Immersive` or `Realistic` / internal `Extreme`
- is currently using `Default`

the server strips the saved default equipment to:

- equipment root
- `Scabbard`
- knife item under `Scabbard`
- armband
- for `Realistic` / internal `Extreme` only, `SecuredContainer` and its contents
- descendants of preserved items

The default equipment snapshot is then overwritten with that stripped equipment.

This makes a dead default-loadout teammate unable to regain full gear just by reusing the same saved `Default`, while still preserving the knife/scabbard baseline.

## Escape Handling

Current escape-state persistence applies only to teammates whose selected loadout is `Default`.

When an `Immersive` or `Realistic` / internal `Extreme` teammate survives/extracts:

- the client sends the teammate's live equipment snapshot to the server
- tracked follower-loot/player-given item ids are sent with the snapshot
- the server removes those tracked item trees before saving the new `Default`
- durability, remaining ammo, and consumed meds from the teammate's actual raid equipment are preserved
- non-Realistic modes still strip the secure-container tree before saving; Realistic keeps it

## Current Gaps

Non-`Simple` custom preset selection remains intentionally hidden so it does not conflict with real item ownership rules. The remaining planned loadout work is the future `KIT LOADOUTS` purchase hardening where consumed stash items can preserve their exact live durability/resource state when they become teammate equipment instead of using the saved build's item state.
