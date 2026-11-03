# F28: cloak meter rates (the Cloak and Dagger)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F28`, branch `tf2ai-F28`. Size S, one or
two commits. Fit: Qwen. Depends on F17. Unblocks: Spy Infiltrator. 3
behaviours carry `mult_cloak_meter_consume_rate`, 2 `mult_cloak_meter_regen_rate`.

## Stock, read this session

- `tf_weapon_invis.cpp:327` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER( pOwner,
  fCloakConsumeFactor, mult_cloak_meter_consume_rate )`: the drain per
  second scaled by the owner's attribute (read `:315-340` whole: the
  Cloak and Dagger's motion-based drain is a different attribute,
  `cloak_movement_drain_cost`, already a `deferred.tsv` row under `spy`:
  this stage ports the two rate multipliers only and leaves that row).
- `mult_cloak_meter_regen_rate`: grep it under `shared/tf/tf_player_shared.cpp`
  (the regen in the cloak think), read and cite.

## Code today

- `src/tf2/server/Player/TFSpy.cs:24` (cloak at `:573`), `CloakMeter` on
  `TFPlayerNetState`; the drain and regen numbers: grep `CloakMeter` in
  `TFSpy.cs`. Tests: `src/tf2/tests/Spy/TFCloakTests.cs`.

## Build

The two reads at the drain and regen sites through `AttributeHooks.Float`
with the owner as the provider. Facts in `TFCloakTests.cs`:
`ConsumeRateScalesTheDrain`, `RegenRateScalesTheRefill`,
`AStockWatchKeepsStocksRates`. Flip the rows whose only missing attributes
were these (the Cloak and Dagger stays `partial` while
`cloak_movement_drain_cost` is deferred: say so in its evidence).

## Red first

`ConsumeRateScalesTheDrain` before the read: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFCloak'` → `Failed: 0`; `make
test`; `tools/valve-header check` 0; a Spy boot (`cmd joinclass spy`, `cmd
cloak`) with `MISPREDICTION` unchanged (the meter is predicted).
