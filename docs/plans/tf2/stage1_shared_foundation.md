# TF2 port — Stage 1: shared foundation (`src/tf2/shared`, compiled into both sides)

Status: DRAFT, uncommitted. Part of `docs/plans/plan_tf2_port.md` (read §0, §2,
§6 there first). Requires Stage 0 complete (`docs/plans/tf2/seams.tsv` exists).
Stock paths are under `../source-sdk-2013/src/game/` (read-only; never write
to the fork). Sizes measured 2026-10-11; method line numbers are at the fork's
pinned sha (`sdk-root.lock`) — re-grep before reading.

Everything here is plain C# with no engine calls except file reads through
`IShimEngine.FilesReadGameFile`. Each phase is independently testable with
xUnit facts in `src/tf2/tests/`; fixtures are extracted with
`tools/vpk-extract` (Stage 0.2) from `bin/debug/content/tf/tf2_misc_dir.vpk`
and committed under `src/tf2/tests/TestData/` (trim to the needed keys; note
the source path and date in a `README` beside them).

Order: 1.1 → 1.2 → 1.3 can run as one lane; 1.4 → 1.5 → 1.6 is the economy
lane; 1.7 → 1.8 → 1.9 is the gameplay lane and needs 1.1, 1.2 and 1.5.

---

## Phase 1.1 — Constants and enums (generated from the stock headers, checked by oracle facts)

**Stock**
- Teams: `shared/tf/tf_shareddefs.h:33-34` (`TF_TEAM_RED = LAST_SHARED_TEAM+1` = 2, `TF_TEAM_BLUE` = 3).
- Classes: `tf_shareddefs.h:203-220` `enum ETFClass` — `TF_CLASS_UNDEFINED=0, SCOUT, SNIPER, SOLDIER, DEMOMAN, MEDIC, HEAVYWEAPONS, PYRO, SPY, ENGINEER, CIVILIAN, TF_CLASS_COUNT_ALL` (note the order: Sniper is 2, Soldier 3).
- Conditions: `tf_shareddefs.h:689-832` `TF_COND_AIMING = 0 … TF_COND_LAST` (133 ids).
- Weapon ids: `tf_shareddefs.h:407-523` `TF_WEAPON_NONE … TF_WEAPON_COUNT` (111 ids); names table `g_aWeaponNames[]` in `tf_shareddefs.cpp`; damage types `g_aWeaponDamageTypes[]`.
- Loadout slots: `shared/tf/tf_item_constants.h:52-96` `loadout_positions_t` (`LOADOUT_POSITION_PRIMARY = 0 …`, `CLASS_LOADOUT_POSITION_COUNT` :81, `LOADOUT_MAX_WEARABLES_COUNT` :103).
- Damage constants: `tf_shareddefs.h:146-157` (`TF_DAMAGE_CRIT_CHANCE 0.02f`, `_RAPID 0.02f`, `TF_DAMAGE_CRIT_DURATION_RAPID 2.0f`, `TF_DAMAGE_CRIT_CHANCE_MELEE 0.15f`, `TF_DAMAGE_CRIT_MULTIPLIER 3.0f`, `TF_DAMAGE_MINICRIT_MULTIPLIER 1.35f`).
- Hitgroups: `shared/shareddefs.h` `HITGROUP_*` (already pinned by Stage 0.8).

**Make**: `src/tf2/shared/TfSharedDefs.cs` — `enum TfTeam`, `enum TfClass`,
`enum TfCond`, `enum TfWeaponId`, `enum TfLoadoutSlot`, `static class TfDamage`
(consts only; SS0010 allows `const`). Generate the long enums with a throwaway
script from the header (do not commit the script); the oracle facts are what
keep them honest.

**Facts** (`src/tf2/tests/TfSharedDefsTests.cs`)
- Oracle: parse `tf_shareddefs.h` from the pristine clone (`RepoLayout`,
  `src/test/oracle/` conventions, `valve-sdk.lock`) and assert the C# enum
  has the same member count and the same name→value for every member (strip
  `TF_COND_` → PascalCase; keep a small explicit rename table).
- Same for `ETFClass` and `loadout_positions_t`.
- `TfDamage` constants equal the header's `#define` values (parse the six lines).
- Negative control: the oracle parser fails loudly on a missing header (a
  missing input is a failure, never a pass).

**Gate**: `make tf2-test FILTER=TfSharedDefs`; `tools/valve-header check`
passes with the new row in `docs/valve-header-new.tsv`
(`src/tf2/shared/TfSharedDefs.cs<TAB>ports tf_shareddefs.h enums and tf_item_constants.h loadout_positions_t`).

## Phase 1.2 — Class data from `scripts/playerclasses/*.txt`

The first draft said "static table"; stock reads KeyValues files. Port the reader.

**Stock**: `shared/tf/tf_classdata.cpp:141-200` (`TFPlayerClassData_t::Parse`):
keys `name`, `model`, `model_hwm`, `model_hands`, `localize_name`,
`speed_max`, `health_max`, `armor_max`, `weapon1..weaponN` (`GetWeaponId`),
`grenade1/2`, `buildable1..N` (`GetBuildableId`), `DontDoAirwalk`,
`DontDoNewJump`, `cameraoffset_forward/right/up`, `sound_death`,
`sound_crit_death`, `sound_melee_death`, `sound_explosion_death` (defaults at
:197-200). File names: `scripts/playerclasses/<classname>.txt` where
`classname` comes from `g_aPlayerClassNames_NonLocalized` in `tf_shareddefs.cpp`
(`scout, sniper, soldier, demo, medic, heavy, pyro, spy, engineer, civilian`
— verify the exact strings; `demo` and `heavy` are not the enum names).

**Make**: `src/tf2/shared/Classes/TfClassData.cs` (record: the fields above),
`TfClassDataParser.cs` (over `KeyValuesText.Parse`, `src/shared/managed/KeyValuesText.cs:307`),
`TfClassTable` as `[SideService(Side.Server)]` and `[SideService(Side.Client)]`
(one class, two attributes) that loads the ten files once per level through
`IShimEngine` file reads (the model is `WeaponScriptDatabase.ReadScript`,
`src/shared/managed/WeaponScriptDatabase.cs:288`). Expose `this[TfClass]`.
Missing file → log + a `TfClassData.Missing` sentinel, never a crash.

**Fixtures**: `tools/vpk-extract ... scripts/playerclasses/{scout,soldier,heavy,demo}.txt`
→ `src/tf2/tests/TestData/playerclasses/`.

**Facts**: scout `speed_max` 400 / `health_max` 125; soldier 240/200; heavy
230/300; demo 280/175 (read the fixture, assert the parsed value, and assert
the fixture's raw text contains the same number so a stale fixture is
caught); `weapon1..3` resolve to `TfWeaponId`s; unknown key ignored; a
file with no `speed_max` yields 0 and a log line (stock `GetFloat` default).
Table fact: every `TfClass` except Undefined/Civilian has data after load
(RecordingEngine serving the fixtures).

**Gate**: `make tf2-test FILTER=TfClass`; valve-header row added
(`ports TFPlayerClassData_t::Parse (tf_classdata.cpp)`).

## Phase 1.3 — ConVars

**Stock**: grep `ConVar ` across `shared/tf/tf_weaponbase.cpp` (:83 `tf_weapon_criticals_nopred`, :277 `tf_weapon_criticals`),
`tf_weaponbase_melee.cpp:26` (`tf_weapon_criticals_melee`),
`shared/basecombatweapon_shared.cpp:60-61` (`tf_weapon_criticals_bucket_cap 1000`, `_bottom`, `_default` — read the defaults),
`tf_gamemovement.cpp` (`tf_*` movement convars), `shared/teamplayroundbased_gamerules.cpp`
(`mp_*` the round machine reads: `mp_respawnwavetime`, `mp_restartround`, `mp_waitingforplayers_time`, `mp_stalemate_*`, `mp_tournament`…),
`server/team_train_watcher.cpp:413-414` (`tf_escort_recede_time 30`, `_overtime 5`),
`shared/tf/entity_capture_flag.cpp:57` (`tf_flag_return_time_credit_factor`), `tf_gamerules.cpp` (`tf_flag_caps_per_round`, `tf_birthday`… take only what the milestone uses).

**Make**: `src/tf2/shared/TfConVars.cs`: one `[ConVar(name, default, Help, Flags)]`
class per convar (`src/shared/interop/ConVar.cs:18`; `FloatConVar`/`IntConVar`/`BoolConVar`),
injected where read (`ConVarRegistry.Get<T>()`, pattern `GameRules.cs:24-32,162-165`).
Replicated convars must have the same default on both sides — shared source
guarantees it; check the attribute exposes a replicated flag and use it where
stock has `FCVAR_REPLICATED`.

**Facts**: one fact per convar: name, default and flags equal the stock
declaration (quote the stock line in the fact's comment); a scan fact that
every `[ConVar]` in `TF2.*` has a `tf_` or `mp_` prefix.

**Gate**: `make tf2-test FILTER=TfConVars`; live: `make headless RUN_MOD_ID=tf2`,
`cvarlist tf_` through `tools/debug-api console` lists them.

## Phase 1.4 — Item schema (offline econ core)

**Stock**: `shared/econ/econ_item_schema.cpp` is **7,192** lines (the draft's
2,680 is `shared/tf/tf_item_schema.cpp`, the TF overlay). Read only:
`CEconItemSchema::BInitSchema`, `BInitPrefabs`, `BInitItems`, `BInitAttributes`,
`CEconItemDefinition::BInitFromKV`, `CEconItemAttributeDefinition::BInitFromKV`,
prefab merge (`prefab` key: space-separated list, applied in order, child keys
override), and the TF overlay's `CTFItemDefinition::BInitFromKV` /
`CTFItemSchema` for `used_by_classes`, `item_slot`, `item_class`,
`model_player`, `model_player_per_class`, `attach_to_hands`, `baseitem`.
Data: `scripts/items/items_game.txt` inside `tf2_misc_dir.vpk` (sections
`prefabs`, `items`, `attributes`, `item_classes`? — list with
`tools/vpk-extract --list 'scripts/items/*'`).

**Make**: `src/tf2/shared/Econ/` — `ItemSchema.cs` (service, both sides,
loaded once per level via `FilesReadGameFile`; API `Item(int defindex)`,
`BaseItems(TfClass, TfLoadoutSlot)`, `Attribute(int defindex)`, `Attribute(string name)`),
`ItemDefinition.cs` (defindex, name, item_class, item_slot per class,
used_by_classes, model_player, baseitem, attributes: list of (attribute def,
value)), `AttributeDefinition.cs` (defindex, name, attribute_class,
description_format, effect_type, stored_as_integer, hidden),
`SchemaParser.cs` (prefab resolution first, then items). Unknown keys are
kept in a `KeyValuesText` tail for later phases; never throw on them.

**Fixtures**: a trimmed `items_game.txt` keeping `prefabs` + the ~40 base
items (`baseitem 1`) + their prefab chains + the attributes they reference,
under `src/tf2/tests/TestData/items/items_game.trimmed.txt`, plus a 3-item
synthetic file for prefab tests.

**Facts**: prefab chain `a b` applies `a` then `b` then the item (three
synthetic items prove the order with one key per layer); `used_by_classes`
parses to a `TfClass` set; `item_slot` maps to `TfLoadoutSlot`; `BaseItems(Scout, Primary)`
is the scattergun defindex; attribute `stored_as_integer` honoured (bits of
a float vs integer — stock stores integers in the float's bit pattern:
check `CEconItemAttribute` and replicate exactly, with a fact on one known
integer attribute); unknown defindex → null; malformed file → exception with
line number (and a fact that a *missing* file is reported, not an empty schema).

**Gate**: `make tf2-test FILTER=ItemSchema`; live smoke: console command
`tf2_schema_dump <defindex>` (in `src/tf2/server/Commands/`, `[ConCommand]`)
prints the parsed item against the real VPK. valve-header rows for the
parser files (`ports CEconItemSchema::BInitItems/BInitPrefabs (econ_item_schema.cpp)` etc.).

## Phase 1.5 — Attribute hooks (`CALL_ATTRIB_HOOK_*` semantics)

**Stock**: `shared/econ/attribute_manager.cpp` (881): `CAttributeManager::ApplyAttributeFloat/String`,
providers (`m_Providers`), `CAttributeContainer`, and the per-format rule in
`CEconItemSchema`/`attribute_manager.cpp`: `description_format`
`value_is_additive`/`value_is_additive_percentage` → add,
`value_is_percentage`/`value_is_inverted_percentage` → multiply,
`value_is_or` → bitwise or, others pass through; `CALL_ATTRIB_HOOK_FLOAT( value, attr_class )`
macro in `econ/attribute_manager.h` resolves by `attribute_class` string
across the entity's providers (item → wearables → player → … in order).

**Make**: `src/tf2/shared/Econ/AttributeHooks.cs` — pure: `float Float(float baseValue, string attributeClass, IAttributeProviders providers)`,
`int Int`, `string String`, `IAttributeProvider { IEnumerable<(AttributeDefinition, float)> Attributes }`.
No allocation per call (providers return spans/pooled lists; a fact holds
allocation at zero over 1,000 calls, `GC.GetAllocatedBytesForCurrentThread`).
Resolve `attribute_class` → definitions once at schema load (dictionary).

**Facts**: additive adds, percentage multiplies, inverted percentage
multiplies, `or` ors, two providers compose (add then multiply order equals
stock — read `ApplyAttributeFloat` for the exact pass order and cite it),
unknown class returns base unchanged, zero-alloc.

**Gate**: `make tf2-test FILTER=AttributeHooks`.

## Phase 1.6 — Local inventory and the loadout provider (server; offline, D2)

**Stock**: `shared/econ/econ_item_inventory.cpp` (2,405) and
`shared/tf/tf_item_inventory.cpp` (2,123) — port only the *shape*: a per-user
set of item instances and a per-class loadout (slot → item id); everything GC/
Steam (`CPlayerInventory::SOCreated/Updated`, web API, `INVENTORY_VIA_WEBAPI`
arms) is a divergence row.

**Make**: `src/tf2/server/Econ/LocalInventory.cs` (`[SideService(Side.Server)]`,
`IClientLifecycle` to load/save per connect/disconnect): item instances
(`ItemInstance { long id; int defindex; quality; List<(attrDef, value)> }`),
per-class loadout, JSON at `<mod game dir>/inventory/<steamid>.json`
(resolve the game dir the way `UiComposition.cs:31` resolves `mod.GameDir`;
one file per user). Default loadout = schema base items per class/slot.
Implements Stage 0.7's `ILoadoutProvider`: the stock `Loadout` arrays are
replaced by "give the TF weapons for the player's class and loadout" — the
actual weapon giving lands in Stage 2.3; here the provider returns the item
instances and a fact checks the mapping.
Console: `tf2_give <player> <defindex>`, `tf2_loadout <player>` (dev, `[ConCommand]`).

**Facts**: default loadout for each class fills every weapon slot with a base
item; grant → present; save → reload → present (write to a temp dir;
differential: a fresh instance without the file has no item); unknown
defindex refused with a log line; the file name is the SteamID, never the
entity index.

**Gate**: `make tf2-test FILTER=LocalInventory`; divergence rows for the
GC/Steam arms (`docs/divergences.md`: `| TF2 inventory (LocalInventory.cs) | no GC/Steam inventory; JSON per SteamID | econ_item_inventory.cpp:… | D2 |`).

## Phase 1.7 — Conditions (pure state machine + networked carrier)

**Stock**: `shared/tf/tf_condition.cpp` (397) + `tf_condition.h` (`CTFConditionList`),
and in `shared/tf/tf_player_shared.cpp`: `AddCond`/`RemoveCond`/`InCond`
(grep), `OnConditionAdded` L1571 (321 lines), `OnConditionRemoved` L1892
(343), `ConditionThink` L3108 (197), `ConditionGameRulesThink` L2334 (759 —
per-condition tick effects; port only the milestone conditions' arms and
record the rest as deferred rows). Bits: stock keeps five 32-bit masks
(`m_nPlayerCond`, `m_nPlayerCondEx`…`Ex4`); durations in `m_flCondExpireTimeLeft[]`;
`PERMANENT_CONDITION` sentinel = -1.

Milestone set: `Aiming`, `Zoomed`, `Disguising`, `Disguised`, `Stealthed`,
`Invulnerable` (+`_Wearingoff`), `Teleported`, `Taunting`, `Burning`,
`HealthBuff`/overheal flag, `Slowed`, `CritBoosted` (+ `_Pumpkin` no),
`MarkedForDeath` (if the Scout's Fan o' War ships — else later),
`Bleeding` (later), `Kritzkrieged` (later), `Jarated`/`Milked` (later),
`Stunned` (later). A `docs/plans/tf2/conditions.tsv` row per `TfCond` with
`milestone|later|never` and the stock line of its add/remove/tick arm.

**Make**: `src/tf2/shared/Conditions/TfConditions.cs` — pure class over the
five masks + expiry array; `Add(cond, duration, provider)`, `Remove`,
`Has`, `Tick(now)` returning the expired set into a caller span (no alloc);
`TfConditionEffects.cs`: static-free table of per-condition modifiers
(damage-taken ×, damage-dealt ×, speed ×, heal-blocked, cloak-blocked…) read
by movement (1.8) and combat (Stage 2). The networked carrier
(`TfPlayerNetState` with the five masks `[Predicted]`) is defined in
Stage 2.3 with the player state entity; 1.7 stays pure.

**Facts**: expiry at the tick (not before, not one tick late); permanent
never expires; re-add refreshes vs ignores per the stock rule
(`AddCond` with a longer/shorter duration — cite); `Has` after `Remove` is
false; `Tick` over 1,000 cycles allocates zero bytes; `TfConditionEffects`
gives ×1.35 for mini-crit outgoing and marked-for-death incoming
(`TF_DAMAGE_MINICRIT_MULTIPLIER`), heal-blocked while burning only if stock
says so (check `CTFPlayerShared::CanRecieveMedigunChargeEffect`/heal rules —
the first draft asserted this without a citation; verify or drop).

**Gate**: `make tf2-test FILTER=TfConditions`.

## Phase 1.8 — Movement rules (D8: an `IMovementRules` owner, not a subclass)

**Stock**: `shared/tf/tf_gamemovement.cpp` (3,493): `CheckJumpButton` L1160
(192 — scout air dash entry, `m_bJumping`, duck-jump), `AirDash` L992
(100), `WalkMove` L1751, `AirMove` L2101, `GetAirSpeedCap` L2033 (68 —
condition-dependent caps), `FullWalkMove` L2622, `Duck` L3389, `CategorizePosition`
L2334, `CheckWater`/`WaterMove` L1452/L1537; `tf_player_shared.cpp
TeamFortress_CalculateMaxSpeed` L10856 (293 — **the** per-class/per-condition
speed function: class base speed, aiming slow, disguise speed, charge,
stun, speed buffs); view vectors `shared/tf/tf_gamerules.cpp:1310`
(`g_TFViewVectors`: view 72, hull ±24 × 0..82, duck hull 62, duck view 45,
dead view 14) and `:1326` `g_TFClassViewVectors[11]` (per-class eye height).

**Make**: `src/tf2/shared/Movement/TfMovementRules.cs` implementing Stage 0.3's
`IMovementRules` (one owner; registered `[SideService]` on both sides):
- `Constants` = `g_TFViewVectors` (hull/view/duck/dead) — this replaces the
  core's HL2 hull for the whole mod.
- `MaxSpeedScale` = `TfMaxSpeed.Calculate(classData, conditions, weaponState) / 320`
  where `TfMaxSpeed.cs` is a pure port of `TeamFortress_CalculateMaxSpeed`
  taking a `TfMoveInputs` struct (class, conditions snapshot, active weapon
  flags: aiming/minigun spun up/zoomed, health fraction for the GRU/…
  later). The inputs must come **only** from predicted state (the per-player
  `TfPlayerNetState`, Stage 2.3); for 1.8 the struct is supplied by a test.
- `JumpScale` + air dash: port `CheckJumpButton`/`AirDash` rules into a pure
  `TfJump.cs` with a per-player `AirDashState` (count, last tick) kept in the
  predicted state; scout gets one air dash while airborne after a jump,
  reset on ground (`m_iAirDash`, cite). If the existing `JumpRequest` hook
  cannot express "consume this jump as an air dash", add the needed input to
  `JumpRequest` in core (Stage 0.3 owner) — do not fake it.
- Per-class eye height via the per-player `ViewOffsetZ` from Stage 0.3,
  set on class change (Stage 2.3) from `g_TFClassViewVectors`.

**Facts**: `TfMaxSpeed`: scout 400, soldier 240, heavy 230, heavy aiming
(minigun) 110 (= 230 × 47/100? — read the stock constant and cite; do not
guess), zoomed sniper 80 (cite), unknown class → class table's value with
no scaling; `TfJump`: scout second press while airborne → air dash once,
third refused; soldier refused; grounded resets. Determinism: the same
input stream twice → identical outputs (byte-compare a recorded result list).
Side parity: the rules object constructed in a server container and a client
container give identical answers for 100 random inputs.

**Gate**: `make tf2-test FILTER='TfMaxSpeed|TfJump|TfMovementRules'`; live:
`make headless RUN_MOD_ID=tf2 MAP=ctf_2fort`, `tools/debug-api` sets class
(dev command from Stage 2.3; until then a dev convar `tf2_dev_class`) and
`getpos` over 2 s shows ~400 u/s for scout and ~230 for heavy (quote the
measured numbers), scout double-jumps a gap no soldier clears.

## Phase 1.9 — Weapon base: TF weapon types, crit bucket, the milestone weapons

**Stock**: `shared/tf/tf_weaponbase.cpp` (7,026), `tf_weaponbase_gun.cpp` (1,115),
`tf_weaponbase_melee.cpp` (1,152), `tf_weapon_parse.cpp` (231 — TF2 weapon
script keys), weapon scripts `scripts/tf_weapon_*.txt` in the VPK. Crits:
`CalcIsAttackCritical` :1529, `CalcIsAttackCriticalHelperNoCrits` :1582,
`CalcIsAttackCriticalHelper` :1608 (rapid-fire window vs single shot;
`TF_DAMAGE_CRIT_DURATION_RAPID`; seed = prediction seed ^ (weapon entindex<<8 | player entindex)),
`CanFireCriticalShot` :4598, `CanFireRandomCriticalShot` :4630;
bucket in `shared/basecombatweapon_shared.cpp` `AddToCritBucket` :1593,
`IsAllowedToWithdrawFromCritBucket` :1609 (cost = damage × 3 × mult, where
mult = 0.5 for melee else `RemapValClamped(requests/checks, …)` — copy the
four remap constants), convars `tf_weapon_criticals_bucket_cap 1000`,
`_bottom`, `_default` (:60-62); melee `tf_weaponbase_melee.cpp:1040/1068`
(`TF_DAMAGE_CRIT_CHANCE_MELEE 0.15`).

**Make**
- `src/tf2/shared/Weapons/TfWeaponTypes.cs`: an `IWeaponTypeSource` (Stage 0.4)
  producing one `WeaponType` row per milestone weapon from the stock weapon
  script (`WeaponScriptDatabase` extended by `TfWeaponScript.cs` for TF2
  keys: `WeaponType` primary/secondary/melee/pda/building, per-mode
  `Damage`, `BulletsPerShot`, `Spread`, `TimeFireDelay`, `TimeIdle`,
  `ProjectileType`, `clip_size` by class, `ammo_type`, `HasTeamSkins_*`) and
  the schema's attributes. Ammo rows (`IAmmoTypeSource`): `TF_AMMO_PRIMARY/
  SECONDARY/METAL/GRENADES1/2` with max-carry per class from the class scripts.
- `TfWeaponBehaviour.cs` (`IWeaponBehaviour` base for every TF weapon), itself
  composed (D10): it runs an **`ITfWeaponHook` chain** (`ModifyClip`,
  `ModifyInterval`, `ModifyDamage`, `BeforeFire`, `AfterFire`, `CritPolicy`)
  whose first members are TF2's own attribute hooks (1.5) and crit policy,
  and into which a further mod (Descent's item effects) registers without
  touching TF2's files. Chain order: assembly name, then declared order;
  a fact pins it. The base provides
  attribute-hooked clip size, fire interval, reload time, damage; crit roll;
  the `CritBucket.cs` pure class (`Add(damage)`, `TryWithdraw(damage, isMelee, requests, checks)`,
  clamp to `[bottom, cap]`), seeded by the prediction seed so client and
  server roll the same (cite the seed formula). Melee behaviour
  (`TfMeleeBehaviour.cs`): swing trace, crit chance 0.15, damage.
- Milestone weapons, one `IWeaponBehaviour` each (shared, predicted), reading
  these stock files — port the behaviour, not the class hierarchy:

  | Class | Stock loadout (verify in `scripts/playerclasses/<class>.txt` `weapon1..N`) | Stock files (lines) |
  |---|---|---|
  | Scout | scattergun, pistol, bat | `tf_weapon_shotgun.cpp` 522 (scattergun is a shotgun subclass), `tf_weapon_pistol.cpp` 259, `tf_weapon_bat.cpp` 1,324 (plain bat arm only) |
  | Soldier | rocket launcher, shotgun, shovel | `tf_weapon_rocketlauncher.cpp` 788, `tf_weaponbase_rocket.cpp` 667 (projectile base), `tf_weapon_shovel.cpp` 220 |
  | Pyro | flamethrower, shotgun, fire axe | `tf_weapon_flamethrower.cpp` 3,206, `tf_flame.cpp` 1,216 (**Stage 2.11**; 1.9 ships the behaviour shell that consumes ammo and asks the server arm to spawn flames), `tf_weapon_fireaxe.cpp` 41 |
  | Demoman | grenade launcher, stickybomb launcher, bottle | `tf_weapon_grenadelauncher.cpp` 722, `tf_weapon_pipebomblauncher.cpp` 649, `tf_weaponbase_grenadeproj.cpp` 798, `tf_weapon_grenade_pipebomb.cpp` 1,434, `tf_weapon_bottle.cpp` 355 |
  | Heavy | minigun, shotgun, fists | `tf_weapon_minigun.cpp` 1,516 (spin-up states, `TF_COND_AIMING`), `tf_weapon_fists.cpp` 262 |
  | Engineer | shotgun, pistol, wrench, PDA build, PDA destroy, builder | `tf_weapon_wrench.cpp` 593, `tf_weapon_pda.cpp` 479, `server/tf/tf_weapon_builder.cpp` 1,285 (**Stage 2.10**) |
  | Medic | syringe gun, medigun, bonesaw | `tf_weapon_syringegun.cpp` 122 (+ its projectile: grep `CTFProjectile_Syringe` in `server/tf`), `tf_weapon_medigun.cpp` 3,178 (**Stage 2.9**), `tf_weapon_bonesaw.cpp` 237 |
  | Sniper | sniper rifle, SMG, kukri | `tf_weapon_sniperrifle.cpp` 2,076 (charge, zoom → `TF_COND_ZOOMED`, headshot via Stage 0.8 hitgroup), `tf_weapon_smg.cpp` 197, `tf_weapon_club.cpp` 44 |
  | Spy | revolver, knife, disguise kit (PDA), invis watch, sapper | `tf_weapon_revolver.cpp` 351, `tf_weapon_knife.cpp` 743 (backstab), `tf_weapon_invis.cpp` 372, `tf_weapon_pda.cpp`, sapper via `tf_weapon_builder.cpp` (**Stage 2.12**) |

  Behaviours whose *effects* need the server (projectile spawn, flames,
  beams, buildings) call `IWeaponArms` arms added in Stage 0.4 / 2.x; the
  shared behaviour must still run on the client for prediction of ammo,
  clip, timing and the crit roll.
- Weapon pickup/give: TF2 has no weapon pickups; weapons come from the
  loadout (Stage 2.3). No `[ScriptedEntity(..., Item)]` pickup rows.

**Fixtures**: the stock scripts for the 25 milestone weapons
(`tools/vpk-extract --list 'scripts/tf_weapon_*'`) under
`src/tf2/tests/TestData/weapons/`.

**Facts**
- `CritBucket`: add-then-withdraw math equals a hand-computed stock
  sequence (three steps, constants quoted from :1593-1625); clamps at
  `cap` and `bottom`; melee multiplier 0.5; `tf_weapon_criticals 0` → never
  crit (`CalcIsAttackCritical` early-out; cite).
- Crit roll seed: two behaviours with the same seed and inputs roll the same
  result (prediction parity); different weapon index → different seed.
- Every milestone script parses: fire delay, damage, bullets per shot,
  clip size per class, ammo type — one fact per weapon pinning three values
  against the fixture text.
- Type catalogue: the TF rows land after the core rows and both sides agree
  (reuse Stage 0.4's parity fact with the TF source).
- Minigun spin-up: `TF_COND_AIMING` set while firing/spun; speed via 1.8.
- Sniper charge curve: damage at 0/50/100% charge equals stock
  (`tf_weapon_sniperrifle.cpp` `TF_WEAPON_SNIPERRIFLE_DAMAGE_MIN/MAX`, cite).

**Gate**: `make tf2-test FILTER='CritBucket|TfWeapon'`; live on
`ctf_2fort`: `tf2_give` the scattergun and the shotgun, fire until empty,
reload — clip/reserve numbers match the script's `clip_size`/ammo max and
the HL2 HUD ammo panel shows them (the TF2 HUD is Stage 3).

---

## Stage 1 exit

Under `make headless RUN_MOD_ID=tf2 MAP=ctf_2fort` with a `tf2_dev_class`
convar: the player has the TF2 hull, moves at class speed, scout air-dashes,
holds TF weapons given by console with correct clip/ammo/fire-rate/crit
behaviour (server log shows crit rolls and bucket), the schema and class
tables load from the real VPK, and `make test` (all legs incl. `tf2`) is
green with zero warnings. Record the measured speeds and one crit sequence
in the final commit message.
