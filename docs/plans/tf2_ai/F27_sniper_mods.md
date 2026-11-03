# F27: sniper rifle mods (zoomed-only fire, tracer, penetration)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F27`, branch `tf2ai-F27`. Size S, two
commits. Fit: Qwen. Depends on F17. Unblocks: the Sniper Marksman's rifle
variants (2 behaviours each carry `sniper_only_fire_zoomed`,
`sniper_fires_tracer`, `sniper_penetrate_players_when_charged`).

## Stock, read this session

- `tf_weapon_sniperrifle.cpp:711` `CALL_ATTRIB_HOOK_INT( iModOnlyFireZoomed,
  sniper_only_fire_zoomed )`: the rifle refuses to fire unzoomed when set
  (read `:705-720` for what it does instead: a dry click or nothing).
- `sniper_fires_tracer` and `sniper_penetrate_players_when_charged`: grep
  each under `../source-sdk-2013-valve/src/game/shared/tf/` (the tracer is
  in the rifle's fire effect; the penetration in the bullet trace:
  `tf_fx_shared.cpp` or `tf_weapon_sniperrifle.cpp`), read and cite. The
  penetration needs the bullet trace to continue through a player when the
  charge is full: if the mod's bullet path (`src/tf2/server/Combat/
  TFDamageLayer.cs`, the managed `FireBullets` surface trace) cannot
  continue a trace, mark that attribute `partial` with the reason and port
  the other two.

## Code today

- `src/tf2/shared/Weapons/TFSniperRifleBehaviour.cs:14` (charge `:30-39`),
  `src/tf2/server/Combat/TFDamageLayer.cs:39-46` (headshots).

## Build

The reads at those sites. Facts in `src/tf2/tests/Weapons/` (the sniper
facts' file; grep `SniperRifle`): `OnlyFireZoomedRefusesAnUnzoomedShot`,
`AStockRifleFiresUnzoomed`, `FiresTracerMarksTheShot` (whatever observable
the tracer has: an effect dispatch count), and the penetration fact or
its `partial` row. Flip the rows.

## Red first

`OnlyFireZoomedRefusesAnUnzoomedShot` before the read: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~SniperRifle'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; a Sniper boot with
`MISPREDICTION` unchanged.
