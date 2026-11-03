# F33: the long tail of item families (generated from the audit)

Not one stage: a recipe for the 40–60 stages that follow F21–F32, written
after F18a–c have filled `game/mod_tf2/scripts/items/items_working.tsv`.
Fit: Qwen, one family per session. No bot waits on any of these: a bot
draws only `works` rows (F19).

## The numbers (this session's schema parse, main plan C.3.1)

179 distinct behaviours; 48 have every attribute named in the mod; F21–F32
cover about 22; about 110 remain, over about 200 attribute classes that
`src/tf2` does not name. Grouped by the attribute family they share (the
`attributes` column), that is 40–60 sessions. Honest calendar: three to six
months of six lanes at the current finish rate, interleaved with the bot
work, never blocking it.

## How a stage is generated (the coordinator, one command, no judgement)

1. From the tsv, take every `partial`/`missing` row; split each row's
   missing attribute classes (the evidence names them); group rows by
   their most frequent missing class; a group is a stage when it closes at
   least one row.
2. Order the groups by the sub-class table's needs first (main plan C.3.1),
   then by rows closed per attribute class, descending.
3. For each group write `F<nn>_<family>.md` from the F2x template: base
   (the tip), worktree, size S (1–2 classes) or M (3–6), the stock read
   sites found by `grep -rn '<attribute class>' ../source-sdk-2013-valve/
   src/game/shared/tf ../source-sdk-2013-valve/src/game/server/tf` (the
   coordinator runs the grep and writes `file:line` and one line of what
   the site does, after reading it: a lane is never handed an unread
   citation), the mod's site (the kind's behaviour file), the fact names
   (`<Attribute>ScalesX`, `AStock<Item>KeepsStocksX`), the rows to flip,
   the red-first fact and the commands.
4. Families that are not attribute reads but mechanics stay off this list
   and get their own briefs by a stronger model: swords/shields and the
   charge (`deferred.tsv:17,20,22`), the Dead Ringer's feign, the Wrangler,
   the Eureka Effect's teleport menu, the Sandman's stun, the Vaccinator's
   resist cycle, throwables (Jarate, Mad Milk: `tf_weapon_jar*`), energy
   weapons (`energy_weapon_*`: Bison, Pomson), the Huo-Long Heater's ring,
   lunchboxes and drinks beyond the meter, the Rocket Jumper/Sticky
   Jumper's no-damage rows (small, but they touch the blast-jump state
   P6 reads: after P6).

## The guard that holds across all of them

`ItemsWorkingTableTests.NoRowIsWorksWithoutEvidence…` (F17) and
`BotInventoryTests.NoDrawnItemIsPartialOrMissing` (F19): a stage that flips
a row without evidence, or a row that is flipped while an attribute still
does nothing, is caught by the first; a bot never equips a half-ported
item by the second.
