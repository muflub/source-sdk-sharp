# F29: medigun charge types (Kritzkrieg, Quick-Fix)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F29`, branch `tf2ai-F29`. Size M, two
or three commits. Fit: Qwen. Depends on F17. Unblocks: Medic Kritz, Medic
Quick-Fix (the Vaccinator's resistances are a later stage: three charge
types with a cycling HUD; leave its row `partial`).

## Stock, read this session

- `tf_weapon_medigun.cpp:821` `CALL_ATTRIB_HOOK_INT( iTmp, set_charge_type )`:
  the medigun's charge type from the item (read `:810-830` and the
  `medigun_charge_types` enum in `tf_weapon_medigun.h`: invuln, critboost,
  megaheal, resist; cite the values).
- `:1442` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pOwner, flUberTime, add_uber_time )`:
  the charge's duration plus the attribute (read `:1435-1450`).
- What each type does when popped: invuln is the existing über; critboost
  gives the target `TF_COND_CRITBOOSTED` for the duration (grep the
  critboost arm near the pop: `tf_weapon_medigun.cpp`, grep
  `MEDIGUN_CHARGE_CRITICALBOOST`); megaheal (Quick-Fix) gives
  `TF_COND_MEGAHEAL` (grep it) with its heal multiplier and no knockback.
  Read each arm and cite; port exactly those two.

## Code today

- `src/tf2/server/Medigun/TFMedigunWeapon.cs:66` (the meter `:199`, the pop
  and `player_invulnerable` `:512`); `src/tf2/shared/TFSharedDefs.cs` `TFCond`
  (confirm `Critboosted` and `MegaHeal` names exist; `Critboosted` does per
  the carrier notes); `src/tf2/tests/Medigun/`.

## Build

1. `set_charge_type` read at the medigun's setup; the pop branches by type:
   critboost → the target gets the crit-boost condition for the duration;
   megaheal → the mega-heal condition with stock's heal multiplier.
   `add_uber_time` on the duration.
2. Facts in `src/tf2/tests/Medigun/` (the pop facts' file; grep `Invulnerable`):
   `AKritzkriegPopCritBoostsTheTarget`, `AQuickFixPopMegaHealsTheTarget`,
   `AddUberTimeExtendsTheCharge`, `AStockMedigunStillPopsInvulnerability`.
   Flip the Kritzkrieg's and Quick-Fix's rows; the Vaccinator stays
   `partial` ("resist type and its HUD cycle not ported").

## Red first

`AKritzkriegPopCritBoostsTheTarget` before the branch: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~Medigun'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; a Medic boot with the heal gate
step: `tools/tf2-gate --steps join,teams,classes,heal` every row PASS (the
`classes` step is red on the tip for an unrelated speed read: say if it is).
