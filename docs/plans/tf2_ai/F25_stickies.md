# F25: sticky attributes (the Scottish Resistance)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F25`, branch `tf2ai-F25`. Size M, two
commits. Fit: Qwen. Depends on F17. Unblocks: Demoman Trapper.

## Stock, read this session

- `tf_weapon_pipebomblauncher.cpp:382` `CALL_ATTRIB_HOOK_INT( nMaxPipebombs,
  add_max_pipebombs )`: the live sticky cap (stock 8) plus the attribute
  (read the enclosing function: the oldest sticky is removed past the cap).
- `tf_weapon_grenadelauncher.cpp:484` `CALL_ATTRIB_HOOK_INT( iMode,
  set_detonate_mode )`: the detonate mode (read `:478-500` and the mode
  enum's values; mode 1 is "detonate only the stickies under the
  crosshair", which the Scottish Resistance uses; cite the exact meaning
  from the code, not from memory).
- `tf_weapon_grenade_pipebomb.cpp:1190` `CALL_ATTRIB_HOOK_FLOAT_ON_OTHER(
  GetLauncher(), flLiveTime, sticky_arm_time )`: the arm time before a
  sticky may detonate (read `:1185-1195`).

## Code today

- Stickies and detonate: `src/tf2/shared/Weapons/TFShellBehaviours.cs:24-43,
  100`; `src/tf2/server/Projectiles/TFProjectileEffects.cs:23`; the live
  count cap: grep `MaxPipebombs` or `8` in those files.

## Build

The three reads at those sites; the crosshair-aimed detonate mode (a trace
from the eyes; stickies within the mode's radius of the hit point only:
the number from the stock read). Facts in
`src/tf2/tests/Projectiles/` (the sticky facts' file; grep `Sticky`):
`AddMaxPipebombsRaisesTheLiveCap`, `StickyArmTimeDelaysDetonation`,
`DetonateModeOneFiresOnlyTheAimedStickies`,
`DetonateModeZeroFiresThemAll`. Flip the Scottish Resistance's row.

## Red first

`DetonateModeOneFiresOnlyTheAimedStickies` before the mode exists:
`Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~Sticky'` → `Failed: 0`; `make
test`; `tools/valve-header check` 0; a Demoman boot with `MISPREDICTION`
unchanged.
