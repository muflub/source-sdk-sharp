# F23: item meters (`item_meter_charge_rate`, the recharge meter)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F23`, branch `tf2ai-F23`. Size M, two or
three commits. Fit: Qwen. Depends on F17. Unblocks: Scout Bonk runner, the
Pyro Combo's flare (its recharge), the banners' meter (F31 reads it).
8 distinct behaviours carry `item_meter_charge_rate` in the schema.

## Stock, read this session

- `tf_gamerules.cpp:21901` `case 874: // mult_item_meter_charge_rate`: the
  attribute's defindex in a switch (an attribute-hook listing). The meter
  itself lives in the player's shared code: grep `item_meter_charge_rate`
  and `GetItemChargeMeter`/`m_flItemChargeMeter` under
  `../source-sdk-2013-valve/src/game/shared/tf/tf_player_shared.cpp` and
  `tf_weaponbase.cpp` (the lunchbox and drink's "effect bar" is
  `EffectBarRegenTime`/`m_flEffectBarRegenTime` in `tf_weaponbase.cpp`:
  grep it). Read the regen rule whole: a bar that refills over
  `GetEffectBarRegenTime()` seconds scaled by the attribute, consumed by a
  use, shown as a HUD meter. Cite the lines you port; if the two systems
  (effect bar vs item meter) differ, port the effect bar first (drinks,
  flare guns, jarate use it) and say which the schema's rows need.

## Code today

- No meter: `grep -rn 'item_meter\|EffectBar\|RegenTime' src/tf2
  --include=*.cs` (run it; cite what you find).
- The HUD's per-weapon meters: `src/tf2/ui/screens/` (grep `Meter` there; the
  cloak meter is `TFPlayerNetState.CloakMeter`): a new bar needs a networked
  float on the weapon's state (the predicted weapon carrier: grep how
  `CloakMeter` is kept) under the 2048-byte tier (`TFNetStateTierTests`).

## Build

1. The effect bar on the weapon state: a float 0..1 refilled per tick by
   `1 / (regenSeconds × mult_item_meter_charge_rate)`, consumed to 0 on a
   use, with `kill_refills_meter` when the row lists it (stock's listen is
   an achievement at `achievements_tf_demoman.cpp:1823`: that is NOT the
   gameplay read; find the gameplay read by grepping the attribute class
   under `shared/tf` and cite it, else leave `kill_refills_meter` out and
   say so).
2. Facts in `src/tf2/tests/Weapons/TFItemMeterTests.cs` (new):
   `AUsedBarRefillsOverItsRegenTime`, `TheChargeRateAttributeScalesTheRefill`,
   `TheBarIsNetworkedWithinTheTier`, `ABarAtZeroRefusesTheUse`.
3. HUD: the meter drawn for a weapon that has one (one `ui-screen` loop per
   `docs/ui/authoring.md`; the bar's golden). Flip the rows whose only
   missing attributes were the meter ones.

## Red first

`TheChargeRateAttributeScalesTheRefill` before the attribute read:
`Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFItemMeter'` and
`FILTER='FullyQualifiedName~TFNetStateTier'` → `Failed: 0`; `make test`
(the ui leg for the golden); `tools/valve-header check` 0; a boot with a
Scout and the Bonk equipped if F19 has landed, else a Pyro with the Flare
Gun: `first paint` 1, `threw` 0, `MISPREDICTION` unchanged.
