# F24: scattergun mods (Force-a-Nature)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F24`, branch `tf2ai-F24`. Size S, two
commits. Fit: Qwen. Depends on F17. Unblocks: Scout Harasser.

## Stock, read this session

- `tf_weapon_shotgun.cpp:302` `CALL_ATTRIB_HOOK_INT( iWeaponMod,
  set_scattergun_no_reload_single )`: the scattergun reloads its whole clip
  at once instead of shell by shell when set (read the enclosing function
  and the reload-mode switch it feeds; cite).
- `tf_fx_shared.cpp:305` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pWeapon,
  nBulletsPerShot, mult_bullets_per_shot )`: pellets per shot scaled (read
  `:295-315`).
- The Force-a-Nature's knockback on the shooter and victim are other
  attributes: read the item's row in the table and port only what this
  stage names; the rest stays `partial` with evidence.

## Code today

- Shotgun reload shell-by-shell: `src/tf2/shared/Weapons/TFWeaponBehaviour.cs`
  (gun/shotgun) and the core's shotgun reload (`Hl2mpWeaponBehaviours.cs:174-197`
  is HL2MP's; TF2's is in `TFWeaponBehaviour`: grep `Reload` there);
  pellets: grep `Pellets`/`BulletsPerShot` under `src/tf2/shared/Weapons`.

## Build

The two attribute reads at those sites through `AttributeHooks.Int/Float`.
Facts in `src/tf2/tests/Weapons/` (the shotgun facts' file; grep `Scattergun`):
`NoReloadSingleReloadsTheWholeClipAtOnce`, `BulletsPerShotScalesThePellets`,
`AStockScattergunReloadsShellByShell`. Flip the Force-a-Nature's row to
`partial` or `works` per what remains (evidence names the knockback
attributes if left).

## Red first

`BulletsPerShotScalesThePellets` before the read: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~<the file's class>'` →
`Failed: 0`; `make test`; `tools/valve-header check` 0; a Scout boot with
`MISPREDICTION` unchanged.
