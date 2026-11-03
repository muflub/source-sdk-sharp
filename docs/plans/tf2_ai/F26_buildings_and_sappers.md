# F26: damage vs buildings and sapper removal (the Homewrecker family)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F26`, branch `tf2ai-F26`. Size S, two
commits. Fit: Qwen. Depends on F17. Unblocks: Pyro Airblast guard
(Homewrecker). 3 behaviours carry `mult_dmg_vs_buildings`, 2 carry
`set_dmg_apply_to_sapper`.

## Stock, read this session

- `tf_obj.cpp:1927` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pWeapon, flDamage,
  mult_dmg_vs_buildings )`: damage to a building scaled by the weapon's
  attribute (read `:1915-1935` for the condition: a weapon-caused hit).
- `tf_weaponbase_melee.cpp:473` `CALL_ATTRIB_HOOK_INT( iDmgSappers,
  set_dmg_apply_to_sapper )`: a melee hit on a friendly sapped building
  damages the sapper instead (read `:460-500` whole: the sapper lookup,
  the damage applied, and what happens to the building).

## Code today

- Building damage: `src/tf2/server/Buildings/TFBaseObject.cs:68-134` (the
  take-damage path; grep `TakeDamage`); sappers: `TFSapper.cs:52`,
  `TFSapperUse.cs` (wrench hits on sappers: read it, the Homewrecker's hit
  is the same path with a different gate).

## Build

The two reads at those sites. Facts in `src/tf2/tests/Buildings/` (the
sapper facts' file; grep `Sapper`): `DamageVsBuildingsScalesAWeaponsHitOnABuilding`,
`AMeleeWithDmgApplyToSapperHitsTheSapperOnAFriendlyBuilding`,
`AMeleeWithoutItHitsTheBuilding`. Flip the Homewrecker's row (and the
others whose only missing attributes were these).

## Red first

`AMeleeWithDmgApplyToSapperHitsTheSapperOnAFriendlyBuilding` before the
read: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~Sapper'` → `Failed: 0`; `make
test`; `tools/valve-header check` 0.
