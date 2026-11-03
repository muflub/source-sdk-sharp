# F21: passive items (`provide_on_active`, Gunboats, Razorback)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F21`, branch `tf2ai-F21`. Size M, two or
three commits. Fit: Qwen. Depends on F17 (the table rows you flip).
Unblocks: Soldier Roamer (Gunboats), Sniper Guarded (Razorback).

## Stock, read this session

- `tf_weaponbase.cpp:1374` `CALL_ATTRIB_HOOK_INT( iProvideMode,
  provide_on_active )`: an item's attributes reach the player only while
  the item is active when this is set (read the enclosing function whole
  and cite its name and lines: it decides whether the item's container
  provides to the player).
- `tf_gamerules.cpp:7456` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( info.GetAttacker(),
  flRealDamage, rocket_jump_dmg_reduction )`: self blast damage scaled by the
  attacker's attribute (the Gunboats). Read `:7440-7470` for the condition
  (self damage, blast) and cite.
- The Razorback: find its attribute classes in the table's row (F17) and
  read where stock reads them (grep each under `../source-sdk-2013-valve/
  src/game/shared/tf` and `server/tf`; the backstab block is in the knife's
  backstab path: `tf_weapon_knife.cpp`, grep `Razorback` or the attribute
  name; cite lines). If an attribute's read site is not found, the row is
  `partial` with that evidence, not ported by guesswork.

## Code today

- `src/tf2/shared/Econ/AttributeHooks.cs:63` `AttributeContainer` with
  `ProvideTo`/`StopProvidingTo` (`:120-133`): the provider graph exists;
  `provide_on_active` is named nowhere (`grep -rn provide_on_active src/tf2`
  empty).
- Self blast damage: `src/tf2/server/Projectiles/TFBlastApplier.cs:211-215,
  271-285` (self damage), `TFRadiusDamage.cs:385`.
- Backstab: `src/tf2/shared/Weapons/TFKnifeBehaviour.cs:17`.
- Wearables on the server: `src/tf2/server/Player/TFWearables.cs:58`.

## Build

1. `provide_on_active`: in the place a wearable's or weapon's container is
   made to provide to the player (grep `ProvideTo(` under `src/tf2/server`),
   honour the mode: provide only while active (for a weapon: when it is the
   active weapon; a wearable is always "active" in stock's sense: confirm in
   the read above). Fact `AProvideOnActiveWeaponsAttributesApplyOnlyWhileActive`
   in `src/tf2/tests/Player/TFPlayerAttributesTests.cs`.
2. `rocket_jump_dmg_reduction` at the self-damage site: fact
   `GunboatsReduceSelfBlastDamage` in `src/tf2/tests/Player/TFBlastJumpTests.cs`
   (a player with the attribute takes the scaled self damage; without it,
   stock's).
3. The Razorback's block, if its read site was found: fact
   `ARazorbackBlocksOneBackstab` in `src/tf2/tests/Spy/TFKnifeTests.cs`.
4. Flip the rows (Gunboats, Razorback, and any other item whose only
   missing attribute was one of these) to `works` with evidence in
   `game/mod_tf2/scripts/items/items_working.tsv`, same commit as the
   behaviour.

## Red first

`GunboatsReduceSelfBlastDamage` before step 2: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFBlastJump'`,
`FILTER='FullyQualifiedName~TFPlayerAttributes'`, `FILTER='FullyQualifiedName~TFKnife'`
→ `Failed: 0`; `make managed-test FILTER='FullyQualifiedName~ItemsWorkingTable'`
→ `Failed: 0`; `make test`; `tools/valve-header check` 0.
