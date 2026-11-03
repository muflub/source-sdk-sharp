# F38: quality names in item names (Strange, Unusual, Australium)

Base: `dae055998`. Worktree `../ss-tf2ai-F38`, branch
`tf2ai-F38`. Size S, one or two commits. Fit: Qwen. Depends on nothing.
Serves the looks-by-difficulty draw (main plan C.3.3): a player reads a
bot's "Strange Rocket Launcher" in the kill feed and target ID.

## What exists

- `src/tf2/shared/Econ/ItemSchema.cs:99` `Quality(string name)` (the
  schema's `qualities` table); `ItemDefinition.cs:31` `QualityAny`;
  `src/tf2/shared/Econ/TFItemStrings.cs:143` maps `"unusual"` to
  `AttributeEffectType.Unusual` (an attribute description class, not an
  item quality); `TFItemStrings` builds the item's display strings: read
  it whole and name the function that builds a name shown to players.
- Stock: the quality prefix on a name comes from the localisation
  (`#Strange`, `#Unusual`, `#vintage`, … in `tf_english.txt`, loaded per
  `game/mods.yaml:70`'s `localisation` rows) and the item's quality
  (`CEconItemView::GetItemName`'s quality arm: grep `GetItemName` and
  `Quality` in `../source-sdk-2013-valve/src/game/shared/econ/econ_item_view.cpp`,
  read and cite the prefix rule; Australium is an attribute
  (`is_australium_item`) that changes the name through `set_item_texture_wear`
  / the "Australium" prefix: grep and cite; if the name arm is not in the
  shared econ code this build reads, say so and port only the quality
  prefix).

## Build

Where `TFItemStrings` builds a displayed item name, prefix it by quality:
the quality's localised token from the schema's `qualities` (the token
names in `tf_english.txt`: grep `"Strange"`, `"Unusual"` there to confirm),
and "Australium" when the item carries `is_australium_item`. The scoreboard
and target ID read the same string (grep the callers of the name builder
under `src/tf2/ui` and `src/tf2/client`).
Facts in `src/tf2/tests/` beside the item-string facts (grep `TFItemStrings`
under `src/tf2/tests`): `AStrangeItemsNameIsPrefixedStrange`,
`AUniqueItemsNameHasNoPrefix`, `AnAustraliumItemsNameIsPrefixedAustralium`,
`TheTargetIdReadsThePrefixedName` (the target-ID source's string).

## Red first

`AStrangeItemsNameIsPrefixedStrange` before the prefix: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFItemStrings'` → `Failed: 0`;
`make test` (the ui leg if a golden changed); `tools/valve-header check` 0.
