# F22: deploy-time attributes (the Degreaser family)

Base: the main tip after F17 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F22`, branch `tf2ai-F22`. Size S, one or
two commits. Fit: Qwen. Depends on F17. Unblocks: Pyro Combo (Degreaser +
Axtinguisher; `mult_dmg_vs_burning` already exists in the mod).

## Stock, read this session

- `tf_weaponbase.cpp:1262` (inside the deploy-time computation; read the
  enclosing function, cite its name and the lines): the comment says
  `mult_switch_from_wep_deploy_time` is not applied when the last weapon was
  a certain kind; `mult_single_wep_deploy_time` scales this weapon's own
  deploy time. Read both reads (`CALL_ATTRIB_HOOK_FLOAT`) and the condition
  on the previous weapon exactly.

## Code today

- Deploy time is a constant: `src/tf2/shared/Weapons/TFWeaponTypes.cs:41`
  `DeployTime = 0.5f`; the deploy path: grep `DeployTime` under
  `src/tf2/shared/Weapons` and `src/tf2/server` for where a switch's delay is
  applied. `AttributeHooks.Float` (`src/tf2/shared/Econ/AttributeHooks.cs:187`)
  is how a weapon's attribute scales a number.

## Build

At the deploy delay: scale by `mult_single_wep_deploy_time` of the incoming
weapon and `mult_switch_from_wep_deploy_time` of the outgoing one under
stock's condition. Facts in `src/tf2/tests/Weapons/` (the file that holds
deploy facts: grep `Deploy` there; else a new `TFDeployTimeTests.cs`):
`ASingleWeaponDeployTimeAttributeScalesItsOwnDeploy`,
`ASwitchFromAttributeScalesTheNextWeaponsDeploy`,
`TheSwitchFromAttributeIsSkippedUnderStocksCondition`,
`NoAttributeKeepsTheStockDeployTime`. Flip the Degreaser's row and any row
whose only missing attributes were these to `works` with evidence.

## Red first

`ASingleWeaponDeployTimeAttributeScalesItsOwnDeploy` before the change:
`Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFDeployTime'` (or the file's
class) → `Failed: 0`; `make managed-test FILTER='FullyQualifiedName~ItemsWorkingTable'`
→ `Failed: 0`; `make test`; `tools/valve-header check` 0; a boot
`lane-boot.sh tf2 ctf_2fort <port> 'cmd jointeam red' 'cmd joinclass pyro'`
with `MISPREDICTION` count unchanged against the base's boot (deploy time
is predicted).
