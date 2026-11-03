# F18a / F18b / F18c: the item audit, filled per class group

Base: the main tip after F17 lands (coordinator writes it: `________`).
Three lanes, one each: **F18a** Scout, Soldier, Pyro (worktree
`ss-tf2ai-F18a`, branch `tf2ai-F18a`); **F18b** Demoman, Heavy, Engineer;
**F18c** Medic, Sniper, Spy. Size M each, one or two commits. Fit: Qwen.
Depends on F17 (`tools/tf2-items-audit`, `game/mod_tf2/scripts/items/
items_working.tsv` with `status unknown` rows; grep first).

## Goal

For every `unknown` row whose `classes` contains one of your classes (a
multi-class item is owned by the lane whose group comes first in
Scout/Soldier/Pyro/Demoman/Heavy/Engineer/Medic/Sniper/Spy order), decide
`works`, `partial` or `missing` from the code, and write one line of
evidence. **You change no code.** A row is a fact about the mod; a wrong
`works` lets a bot equip a weapon that does nothing, and the F19 fact
`NoDrawnItemIsPartialOrMissing` is only as good as this table.

## May touch

Only your group's rows in `game/mod_tf2/scripts/items/items_working.tsv`
(the three lanes edit disjoint rows; the integrator merges). Nothing else.

## How to decide, per row

1. `item_class`: the mod's behaviour row for the kind: grep the class in
   `src/tf2/shared/Weapons/TFWeaponKind.cs` and `TFWeaponTypes.cs` (and
   `TFItemClassTranslation.cs:38` for the per-class translation). No row →
   `missing` ("no behaviour for item_class X").
2. Each behaviour attribute class in `attributes`: grep it as a string
   under `src/tf2` (`grep -rn '"mult_clipsize"' src/tf2 --include=*.cs`).
   Named in a hook read (`AttributeHooks.Float/Int/String`, `src/tf2/shared/
   Econ/AttributeHooks.cs:187-208`) at a site that applies it → counts as
   present; named only in a test fixture or a comment → absent.
3. All present → `works`; some → `partial` (evidence names the absent
   ones); none and the kind exists → `partial`; the kind is missing →
   `missing`.
4. Evidence: one line, `file:line` of the kind row and of each attribute's
   hook site, or the grep that was empty (`grep empty: provide_on_active`).

Stock is not read in this stage: the question is what the mod does.
Anything you are unsure of is `partial`, never `works`.

## Red first

There is no code fact in this stage; the guard is F17's
`NoRowIsWorksWithoutEvidenceExceptNoAttributeStockRows`: set one of your
rows to `works` with empty evidence, run `make managed-test
FILTER='FullyQualifiedName~ItemsWorkingTable'`, see `Failed: 1`, fill the
evidence, see `Failed: 0`. Paste both.

## Done condition

`awk -F'\t' '$8=="unknown"' game/mod_tf2/scripts/items/items_working.tsv |
grep -c -E '<your classes regex>'` is 0 (paste the command and the 0);
counts of `works/partial/missing` for your group in the commit body;
`tools/tf2-items-audit --write` leaves the file unchanged (`git diff
--exit-code` rc 0: the tool never overwrites a filled status); `make test`
`Failed: 0`.
