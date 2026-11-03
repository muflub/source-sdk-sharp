# F01: stale rows about bots and modes

Base: `dae055998` (`git log --oneline -1` must print it). Worktree
`../ss-tf2ai-F01`, branch `tf2ai-F01`. Size S, one commit. Fit:
GLM or Qwen. Depends on nothing.

## Goal

Four written claims about bots and modes are wrong against the code or
stock. Fix the text so no later lane ports a false premise. No behaviour
changes in this stage.

## May touch

- `docs/divergences.md`: the text of the one row that names
  `TFGameModeSelector` (today at line 40). Edit it in place; do not move it.
- `docs/plans/tf2/deferred.tsv`: rows 3, 11, 40, 71 (grep the first column
  and the stock citation below to find them: line numbers drift). Delete
  or edit as told; keep five tab-separated columns in every row you keep.
- `src/tf2/server/Rules/TFAutobalance.cs`: the doc comment at lines 32–35
  only.

May not touch: anything else.

## What is wrong, with the stock read this session

1. `docs/divergences.md:40` says a map with neither an `item_teamflag` nor a
   `team_train_watcher` gets no TF2 mode. The selector
   (`src/tf2/server/Modes/TFGameModeSelector.cs:66-81`) now answers `ctf`
   for a flag, `pl` for a watcher, `koth` for a control-point master with a
   `tf_logic_koth`, `cp` for a master without one, null for `sd_` maps and
   otherwise. Rewrite the row to say that, keeping its stock citation and
   the part that is still a divergence: stock goes on to arena and the
   other game types (`tf_gamerules.cpp:4355-4362`: escort, then CP when a
   master exists and the type is not arena), which this build does not.
2. `deferred.tsv` row with stock `tf_obj_sentrygun.cpp:976-996` (sentry
   "bots pass"): that loop is `TheNextBots().CollectAllBots`, a walk over
   NextBot entities only (read at `:976-996`: `botVector`, `ValidTargetBot`).
   This repo's bots are fake-client players (`src/server/managed/
   BotPlayers.cs:15` `IBotPlayers`), which the sentry's players pass already
   covers. The row is not applicable: delete it, and say why in the commit
   body with that citation.
3. Row with `tf_gamerules.cpp:16892-16985` (capture rules' bot arms): the
   arms are `IsInTraining() && IsBotOfType(TF_BOT_TYPE)` and
   `IsCreepWaveMode()` with `CTFBot::IS_NPC` (read at `:16893` and
   `:16933-16937`, `:16963-16967`). Training and creep wave are dropped modes
   (`docs/plans/tf2/stage5_bots.md` §5.3, D5). Delete the row, same reason.
4. Row with `tf_triggers.cpp:338-413` (`trigger_bot_tag`): tags `CTFBot`s
   (NextBot). Keep the row; change its stage column to `ai-binding` and its
   reason to "a NextBot tag; this repo's bots are players; the AI binding
   may answer the entity when a tree reads tags". No code.
5. Row with the medigun's "bots' arms": the one bot arm in
   `tf_weapon_medigun.cpp:1221` is `if ( !pOwner->IsBot() )` guarding a
   net-channel timing-out check (`:1222-1226`): a bot has no channel, so the
   check is skipped for it. This build never ported the timing-out check
   (`grep -rn 'TimingOut\|NetChan' src/tf2/server/Medigun` is empty), so
   there is nothing to skip. Rewrite the row's item to "the net-channel
   timing-out check (and its bot skip)" with stage `later` and the reason
   "no net-channel read exists here; port both arms together if it does".
6. `TFAutobalance.cs:32-35` says there are no bots in this build. There is
   a bot seam (`IBotPlayers`). Change the comment to say the `CanBeAutobalanced`
   bot refusal is stage F03's, citing `tf_player.cpp:19822-19833` (duel,
   `IsBot()`, coaching, coach: each returns false).

## Commands and the done condition

- `git diff --stat` shows only the three files.
- `awk -F'\t' 'NF!=5' docs/plans/tf2/deferred.tsv | grep -v '^#'` prints
  nothing (every row has five columns).
- `make test`: every leg `Failed: 0` (the checks leg reads the ledgers).
- Red first: there is no fact for a docs change; the mechanical check is the
  `awk` line above, which prints a row if you break a column, and the
  checks leg, which fails a malformed ledger. Show both outputs in the
  hand-back.
