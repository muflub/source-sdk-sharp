# F19: the bots' item source behind `TFLoadoutProvider`

Base: the main tip after F17 lands (F18's rows make it real; coordinator
writes the sha: `________`). Worktree `../ss-tf2ai-F19`,
branch `tf2ai-F19`. Size M, two commits. Fit: Qwen. Depends on F17 (the
table and `ItemsWorkingTableTests`).

## Goal

A bot player's loadout comes from an in-memory source that offers every
item its class can equip whose table row is `works`, with no SteamID file,
no drops, no ownership; `ItemIsAllowed` still binds; a seeded choice per
slot so the same seed gives the same items (ruled 2026-11-02: "bots can use
any item their class supports"; the difficulty never weights the draw).

## What exists

- `src/tf2/server/Econ/TFLoadoutProvider.cs:46` `TFLoadoutProvider(LocalInventory
  inventory, ItemSchema schema, ITFPlayerClasses? classes, ...)`: `:191`
  `owned.ItemInLoadout(cls, slot, schema) is { } item && ItemIsAllowed(item,
  slot, cls)`; `:198` `ItemIsAllowed` ports `CTFPlayer::ItemIsAllowed`
  (`tf_player.cpp:4416-4485`).
- `src/tf2/server/Econ/LocalInventory.cs:17` (`Of(int entIndex)` →
  `PlayerInventory?`, filled on `OnClientActive`); `InventoryStore.cs:20`
  (per SteamID64 on disk).
- The bot seam: `src/server/managed/BotPlayers.cs:15` `IBotPlayers.IsBot`.
- The table: `game/mod_tf2/scripts/items/items_working.tsv` (F17), read
  through the engine file seam as `TFClassTable` reads `scripts/playerclasses`.

## Build

1. `src/tf2/server/Econ/BotInventory.cs` (commit 1; original code):
   `[SideService(Side.Server)] public sealed class BotInventory(ItemSchema
   schema, IEngineFiles files, IBotPlayers bots, ILog log)` that reads the
   table once per level (`IGameLifecycle`, band `999_0xx` near
   `LocalInventory.Order` `999_050`; `TF2LifecycleOrderTests`), keeps the
   `works` defindexes per (class, slot), and answers
   `PlayerInventory? ForBot(int entIndex, ulong seed)`: a `PlayerInventory`
   (the same type `LocalInventory.Of` returns) holding, per class and slot,
   one item drawn uniformly by a seeded `Random`-free hash (`hash(seed,
   class, slot) % count`; no `System.Random` so replay holds) from the
   `works` set, the stock item when the set is empty. The bot's seed is the
   roster's `SeedIndex` later; this stage takes it as a parameter and the
   provider passes `0` for every bot until the AI binding supplies one (say
   so in a comment).
   `TFLoadoutProvider`: for a player `bots.IsBot(player)` is true, ask
   `BotInventory.ForBot` instead of `LocalInventory.Of`; everything after
   (`ItemInLoadout`, `ItemIsAllowed`) is unchanged.
2. Facts (commit 2), `src/tf2/tests/TFLoadoutServerTests.cs` (existing
   file; add) or `BotInventoryTests.cs` beside it:
   `ABotsLoadoutHoldsOnlyWorksRows` (every drawn defindex's row is `works`),
   `NoDrawnItemIsPartialOrMissing` (the negative: a fixture table with one
   `partial` row for the class and slot: never drawn over 200 seeds),
   `TheSameSeedDrawsTheSameItems`, `DifferentSeedsDrawDifferentItemsSomewhere`
   (over 50 seeds at least two differ, for a slot with ≥ 2 works rows),
   `AnEmptyWorksSetFallsBackToTheStockItem`,
   `ItemIsAllowedStillRefusesForABot` (a mode restriction refuses the drawn
   item and the stock one is equipped), `AHumansLoadoutIsUntouched`.

## Red first

`NoDrawnItemIsPartialOrMissing` with the filter on `status` left out:
`Failed: 1`; then `Failed: 0`.

## Commands

`make managed-test FILTER='FullyQualifiedName~BotInventory'` and
`FILTER='FullyQualifiedName~TFLoadoutServer'` → `Failed: 0`; `make test`;
`tools/valve-header check` 0; boot `lane-boot.sh tf2 ctf_2fort <port> 'cmd
jointeam red'` `first paint` 1, `threw` 0 (a new service composes).
