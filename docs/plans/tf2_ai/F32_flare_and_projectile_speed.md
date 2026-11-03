# F32: extinguish-on-hit and projectile speed (flare guns, rocket variants)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F32`, branch `tf2ai-F32`. Size S, two
commits. Fit: Qwen. Depends on F17. Unblocks: Pyro Combo's flare rows, the
Soldier's rocket variants (5 behaviours carry `mult_projectile_speed`, 5
`extinguish_restores_health`, 4 `extinguish_reduces_cooldown`).

## Stock, read this session

- `tf_weapon_flaregun.cpp:558` `CALL_ATTRIB_HOOK_INT( iRestoreHealthOnExtinguish,
  extinguish_restores_health )`: a flare (or airblast: read the enclosing
  function and cite which weapon's hit this is) that extinguishes a burning
  teammate restores health to the shooter when set; `extinguish_reduces_cooldown`
  is beside it: grep it in the same file and `tf_weapon_flamethrower.cpp`
  (the airblast's extinguish), read and cite.
- `tf_weaponbase_rocket.cpp:275` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pLauncher,
  flLaunchSpeed, mult_projectile_speed )`: the rocket's launch speed scaled
  by the launcher's attribute (read `:265-285`).

## Code today

- Flare: `src/tf2/shared/Weapons/TFShellBehaviours.cs` (the flare row;
  grep `Flare`), `src/tf2/server/Combat/TFBurn.cs` (burning and
  extinguish: grep `Extinguish`); rockets: `src/tf2/server/Projectiles/`
  (the launch speed: grep `Speed` in the rocket's launch).

## Build

The three reads at those sites: extinguish-on-hit restores stock's health
and cuts the cooldown by stock's factor; projectile speed scaled at launch.
Facts in `src/tf2/tests/Projectiles/` (the flare and rocket facts' files;
grep `Flare`, `Rocket`): `ExtinguishingATeammateRestoresHealthWhenSet`,
`ExtinguishingReducesTheCooldownWhenSet`, `AStockFlareRestoresNothing`,
`ProjectileSpeedScalesTheLaunch`. Flip the rows whose only missing
attributes were these.

## Red first

`ProjectileSpeedScalesTheLaunch` before the read: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~Flare'` and
`FILTER='FullyQualifiedName~Rocket'` → `Failed: 0`; `make test`;
`tools/valve-header check` 0; a Pyro boot with `MISPREDICTION` unchanged.
