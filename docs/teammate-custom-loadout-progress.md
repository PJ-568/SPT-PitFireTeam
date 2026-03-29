## In Progress: Teammate Custom Loadout Edit

Current checkpoint:

- Added teammate profile `Edit Loadout` entry point and modal shell scaffolding.
- Reworked teammate profile loadout UI so the cloned loadout selector uses:
  - upper dropdown for equipment
  - lower dropdown for tactic placeholder (`Default`)
- Added temporary tactic icon support with `brain.png`.
- Added english localization entries for the new profile loadout editor labels.
- Switched `Edit Loadout` row to a clothing-panel-based container inside `PlayerModelWithStats` so it can reserve vertical layout space.
- Teammate-only profile customization UI now resets correctly when viewing a normal player profile:
  - stock `VIEW HIDEOUT` button is restored using the game's original localized button text
  - teammate-only clothing/customization rows are hidden unless the viewed profile is a teammate
- Phase 1A modal shell is in place and uses stock-style EFT UI pieces.
- Phase 1B is partially implemented:
  - left side now renders a fake cloned stash in the loadout editor
  - right side now renders the follower equipment view from a fake cloned inventory/controller path
  - secure container and dogtag are hidden from the follower-side container list
  - top-bar drag for the overlay is implemented
  - overlay sizing is no longer full-width; it now uses tighter centered margins
- Current profile/editor layout is still being tuned in-game.

Known issues / next follow-up:

- Continue tuning modal width and section balance after more in-game passes.
- Confirm the right-side follower inventory render is fully stable across different teammate loadouts.
- The cloned follower header text is still not stable:
  - `friendlySAIN_LoadoutEditorEquipmentPanel/Header/Text` can still show `LABEL`
  - current rename approach is not reliable yet
- `ChracterGear` background/silhouette hide is still not actually resolved on the follower equipment panel.
- Clipping/masking on the follower-side panel is still unresolved:
  - dark/shadow-like element bleed appears above the visible gear area
  - the first attempted section-root masking approach was reverted because it clipped the tops of valid item panels
- Drag/drop behavior is still incomplete:
  - when dragging an item from the fake stash into follower storage, the item does not persist in the target container after release
  - this needs investigation in the fake controller/item-context path before Phase 2 persistence
- Drag/drop behavior is not finished yet; Phase 1B is still focused on stable fake local inventory rendering.
- Persistence is not started yet.

## Phase Breakdown

### Phase 1A: Profile Entry + Modal Shell

Goal:

- Add the teammate-profile `Edit Loadout` entry point.
- Get the modal shell and stock-style layout in place.

Status:

- In place.

Delivered:

- `Edit Loadout` button under the teammate loadout/tactic selectors.
- Tactic placeholder dropdown (`Default`) next to equipment.
- Modal shell with stock-style EFT framing and buttons.
- Teammate-only profile UI reset/cleanup when switching to non-teammate profiles.

### Phase 1B: Fake Inventory Editor Session

Goal:

- Replace placeholders with fake local inventory views only.
- Do not touch the real player stash or real teammate inventory.

Status:

- In progress.

Delivered so far:

- Left pane renders a fake cloned stash.
- Right pane renders a cloned follower inventory/equipment view through a fake inventory/controller path.
- Removed secure container and dogtag from follower-side display.
- Hid the `ChracterGear` silhouette image.
- Overlay sizing and split are being tuned in-game.

Remaining for Phase 1B:

- Finish layout polish.
- Confirm right-side render is reliable across equipment variations.
- Fix local-only interaction behavior for the editor session.
- Investigate why dragged stash items immediately snap back instead of remaining in follower storage.
- Fix the cloned follower header text so it reliably replaces `LABEL`.
- Properly hide the unwanted `ChracterGear` background/silhouette visual.
- Resolve follower-side clipping/masking bleed without cutting off valid gear panels.

### Phase 2: Save / Reload Contract

Goal:

- Persist custom teammate loadout editor state safely.
- Reopen and reconstruct the same custom state later.

Planned scope:

- Server load/save routes for custom teammate loadout editor data.
- Persist provenance for cloned items (`BotOwned` vs `PlayerClone`).
- Save custom loadout as teammate `Custom` selection without touching real player stash items.

### Phase 3: Hardening

Goal:

- Make the feature stable across real usage and edge cases.

Planned scope:

- Nested containers and weapon mod trees.
- Validation/sanitization of saved inventory trees.
- Profile refresh and raid-spawn correctness.
- Final UX cleanup and failure handling.
