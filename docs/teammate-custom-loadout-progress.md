## In Progress: Teammate Custom Loadout Edit

Current checkpoint:

- Added teammate profile `Edit Loadout` entry point and modal shell scaffolding.
- Reworked teammate profile loadout UI so the cloned loadout selector uses:
  - upper dropdown for equipment
  - lower dropdown for tactic placeholder (`Default`)
- Added temporary tactic icon support with `brain.png`.
- Added english localization entries for the new profile loadout editor labels.
- Switched `Edit Loadout` row to a clothing-panel-based container inside `PlayerModelWithStats` so it can reserve vertical layout space.

Known issues / next follow-up:

- Verify the `friendlySAIN_LoadoutEdit` row spacing and button alignment in-game.
- Replace placeholder editor shell with fake inventory-based loadout editor UI.
- Wire persistence only after the client-side fake editor session is stable.
