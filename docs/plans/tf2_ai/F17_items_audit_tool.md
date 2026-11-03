# F17: the item audit tool and the working-items table

Base: `dae055998`. Worktree `../ss-tf2ai-F17`, branch
`tf2ai-F17`. Size S, two commits. Fit: Qwen. Depends on nothing; F18a–c,
F19 and every F2x item stage depend on it.

## Goal

A tool that reads the TF2 item schema and writes a machine-readable table
of every class-equippable weapon-slot item: one row per item, with its
behaviour attribute classes and a `status` column the audit (F18) and the
port stages fill in. The bots' item source (F19) draws only `works` rows.
Ruled 2026-11-02: "bots can use any item their class supports".

## Inputs, read this session

- The schema: `bin/debug/content/tf/scripts/items/items_game.txt`
  (8,251,383 bytes; KeyValues; `items_game` → `prefabs`, `items`,
  `attributes`, `qualities`). Items inherit from `prefab` entries (a
  space-separated list, the later prefab overriding the earlier). The mod's
  own parser is `src/tf2/shared/Econ/SchemaParser.cs:13` (`Parse`) with
  `ItemSchema.cs:17` and `ItemDefinition.cs` (`StaticAttributes`); prefer
  reading through them from a test over a second parser in python only if
  the tool can run as a repo test; a python tool is acceptable because it
  runs under `python3 -I` with no third-party module.
- A prefab-resolving parse made this session counted: 498 items with
  `item_slot` in {primary, secondary, melee, pda2, building}, an
  `item_class` starting `tf_` and a `used_by_classes` block (primary 203,
  secondary 156, melee 123, pda2 8, building 8); 70 `item_class` families;
  303 distinct attribute classes on them, 279 after dropping the cosmetic
  ones (`min_viewmodel_offset`, `weapon_stattrak_module_scale`,
  `weapon_allow_inspect`, `weapon_uses_stattrak_module`, `is marketable`,
  `paintkit_proto_def_index`, `texture_wear_default`, `kill_eater_*`,
  `disable_fancy_class_select_anim`, `special_taunt`, `has team color
  paintkit`, `limited quantity item`, `allowed_in_medieval_mode`,
  `item_meter_charge_type`, the `flame_*` defaults every flamethrower
  shares); 179 distinct behaviours (same `item_class`, same behaviour
  attribute set). Your tool must reproduce those counts within a few
  (prefab edge cases): print them and compare in the commit body.

## Build

1. `tools/tf2-items-audit` (commit 1; python, `#!/usr/bin/env python3`,
   run as `tools/tf2-items-audit [--schema PATH] [--write]`; no network, no
   third-party modules): parses the schema with prefab resolution, selects
   the items above, and writes
   `game/mod_tf2/scripts/items/items_working.tsv` with the header row
   `defindex	name	item_class	slot	classes	family	attributes	status	evidence`
   where `classes` is comma-separated, `family` is the `item_class` with a
   second word for the flagged families (`passive` for `tf_wearable*`
   items with `provide_on_active`, `shield` for `tf_wearable_demoshield`,
   `banner` for `tf_weapon_buff_item`, `meter` when `item_meter_charge_rate`
   is present, `drink`/`lunchbox`, `pda`, `builder`), `attributes` the
   behaviour attribute classes semicolon-separated (cosmetic ones dropped
   by the list above, kept in the tool as one list), `status` one of
   `works|partial|missing|unknown` (the tool writes `unknown`, except: an
   item with no behaviour attributes whose `item_class` has a behaviour row
   in `src/tf2/shared/Weapons/TFWeaponKind.cs`/`TFWeaponTypes.cs` → `works`;
   say how you decided "has a row": grep the kind table and cite), and
   `evidence` empty. Without `--write` the tool prints the table and the
   counts; with it, writes and is idempotent (a second run changes nothing:
   `git diff --exit-code` after two runs).
2. The guard fact (commit 2): `src/tf2/tests/ItemsWorkingTableTests.cs`
   (new): `TheTableHasOneRowPerSchemaItem` (count of data rows equals the
   tool's count re-derived through `SchemaParser` + `ItemSchema` over the
   same file: slot in the five, class `tf_*`, a used-by block),
   `EveryRowsStatusIsOneOfTheFour`, `NoRowIsWorksWithoutEvidenceExceptNoAttributeStockRows`.
   The table is read through the engine file seam the way other
   `scripts/` files are (grep `scripts/playerclasses` in
   `src/tf2/shared/Classes/TFClassTable.cs:25` for the pattern).

## Red first

`EveryRowsStatusIsOneOfTheFour` against a table with one row's status set
to `yes` by hand (in `.scratch/`, pointed at by the test's path parameter):
`Failed: 1`; then the real table: `Failed: 0`.

## Commands

`tools/tf2-items-audit` (prints 498-ish rows and the family/attribute
counts; paste the counts); `tools/tf2-items-audit --write` twice, `git diff
--exit-code game/mod_tf2/scripts/items/items_working.tsv` rc 0 after the
second; `make managed-test FILTER='FullyQualifiedName~ItemsWorkingTable'` →
`Failed: 0`; `make test`; `tools/valve-header check` 0 (the tool is
original work: no header).
