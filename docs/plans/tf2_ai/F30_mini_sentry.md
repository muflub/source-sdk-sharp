# F30: the mini-sentry (`wrench_builds_minisentry`, the Gunslinger)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F30`, branch `tf2ai-F30`. Size M, two
or three commits. Fit: Qwen. Depends on F17. Unblocks: Engineer Gunslinger
(the Gunslinger's third-punch crit and the robot-arm model are other
attributes: leave them `partial` with evidence; the Frontier Justice's
revenge crits are `tf_weapon_sentry_revenge`, a later stage).

## Stock, read this session

- `tf_weapon_wrench.cpp:255` `CALL_ATTRIB_HOOK_INT( iMiniSentry,
  wrench_builds_minisentry )`: the wrench's owner builds mini-sentries
  (read `:245-265`: how the flag reaches the builder).
- The mini-sentry itself: `tf_obj_sentrygun.cpp`, grep `IsMiniBuilding` /
  `m_bMiniBuilding`: build time, health, no upgrade, its damage and rate,
  its model and the cost (read each arm you port and cite the lines: at
  least the build time, health, "never upgrades", and the metal cost).

## Code today

- `src/tf2/server/Buildings/TFSentryGun.cs:48`, `TFBaseObject.cs:68-134`
  (levels at `:76-88`, max level 3), `TFObjectBuilder.cs:96-155` (metal
  cost), `TFBuildCommands.cs:45`; tests `src/tf2/tests/Buildings/`.

## Build

1. A `Mini` flag on the sentry set when the builder's wrench carries the
   attribute: the mini arms read from stock (build time, health, no
   upgrade, cost, damage/rate), the model name from the schema/stock
   (cite).
2. Facts in `src/tf2/tests/Buildings/` (the sentry facts' file; grep
   `SentryGun`): `AGunslingerWrenchBuildsAMiniSentry`,
   `AMiniSentryNeverUpgrades`, `AMiniSentryCostsStocksMetal`,
   `AMiniSentryBuildsInStocksTime`, `AStockWrenchBuildsALevelOneSentry`.
   Flip the Gunslinger's row to `partial` with the remaining attributes
   named, or `works` if the row lists only this one.

## Red first

`AMiniSentryNeverUpgrades` before the flag gates the upgrade: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~SentryGun'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; an Engineer boot (`cmd
joinclass engineer`, `build 2 0` after F19 equips the Gunslinger, else say
the live arm waits on F19): `first paint` 1, `threw` 0.
