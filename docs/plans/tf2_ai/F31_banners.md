# F31: banners (`set_buff_type`, rage, the buff conditions)

Base: the main tip after F17 and F23 land (coordinator writes it:
`________`). Worktree `../ss-tf2ai-F31`, branch `tf2ai-F31`.
Size M–L, three commits. Fit: Qwen (the largest of the item stages; if the
rage meter's read is not found in a session, commit the buff conditions
alone and say so). Depends on F17 and F23 (the item meter is the rage
bar's HUD). Unblocks: Soldier Banner. 6 behaviours carry `set_buff_type`.

## Stock, read this session

- `tf_weapon_buff_item.cpp:183` `CALL_ATTRIB_HOOK_INT( iBuffType,
  set_buff_type )`: the banner's buff type (read the file's enum of buff
  types and the three the stock banners use: the Buff Banner's mini-crits,
  the Battalion's Backup's damage resistance, the Concheror's heal-on-hit
  and speed; cite the lines).
- Rage: the soldier's rage meter builds with damage dealt (grep
  `m_flRageMeter` / `AddRage` in `tf_player_shared.cpp`; read how much per
  damage point and the cap; cite), the deploy drains it over the buff's
  duration (`tf_weapon_buff_item.cpp`, grep `m_flBuffDuration` or the
  deploy think), and the buff is a condition on every teammate within a
  radius (grep the radius constant; `TF_COND_OFFENSEBUFF`,
  `TF_COND_DEFENSEBUFF`, `TF_COND_REGENONDAMAGEBUFF`: confirm the names in
  `tf_shareddefs.h`).

## Code today

- `tf_weapon_buff_item` is a weapon kind with no behaviour (`grep -rn
  buff_item src/tf2 --include=*.cs`); the conditions enum
  `src/tf2/shared/TFSharedDefs.cs` `TFCond` (grep the three buff names: if
  absent, add them with stock's values, cited); the condition effects live
  in `src/tf2/server/Player/` (grep `Critboosted` for the pattern of a
  condition with a damage effect); the item meter from F23.

## Build

1. The rage meter on the player (a float on the TF player state within the
   tier) built from damage dealt, capped, networked for the HUD (F23's bar).
2. The banner deploy (`IN_ATTACK` with a full meter): the buff condition on
   teammates within stock's radius for stock's duration, draining the meter.
3. The three buffs' effects: mini-crits on the attacker's shots
   (`CurrentAttackIsCrit`'s mini form: grep `MiniCrit` under `src/tf2/
   server/Combat`), damage taken scaled for the defence buff, heal-on-hit
   for the Concheror.
   Facts in `src/tf2/tests/Weapons/TFBannerTests.cs` (new):
   `DamageDealtBuildsRage`, `AFullRageDeploysTheBuffToTeammatesInRange`,
   `TheBuffBannerMiniCritsTheBuffedPlayersShots`,
   `TheBackupScalesDamageTaken`, `TheBuffEndsAfterStocksDuration`,
   `ADeployWithAnEmptyMeterDoesNothing`. Flip the three banners' rows.

## Red first

`AFullRageDeploysTheBuffToTeammatesInRange` before the deploy: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFBanner'` → `Failed: 0`;
`make managed-test FILTER='FullyQualifiedName~TFNetStateTier'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; a Soldier boot with
`MISPREDICTION` unchanged.
