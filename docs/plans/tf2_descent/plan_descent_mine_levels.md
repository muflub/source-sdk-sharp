# plan_descent_mine_levels.md: the fifteen levels of the Descent mine shaft

Status: reviewed and committed 2026-11-10. Not started.

This plan designs the fifteen dungeon levels of TF2 Descent as one mine shaft
under the town: what each level looks and plays like, the room shapes they are
built from, how the levels differ on every play-through, where the jumping
classes get their space, how the light falls away with depth, and how level 1
joins the town. It is the content design for Phase 6 (levels from rooms) and
Phase 11 (fifteen levels of content) of `plan_tf2_rpg.md`.

That plan is beside this one, `plan_tf2_rpg.md`, copied unchanged on
2026-11-10 from `../sharp-fresh/docs/plans/` (where it was never committed).
Its rulings are cited below as R-numbers and its defaults as D-numbers.
Where this plan and that one differ on the levels (M4, M10), this one is the
later ruling.

Out of scope: robots and their AI, the three bosses' behaviour, loot,
furniture entities (shrines, containers, traps), and the miner's lamp code.
This plan gives each of those the places and markers it needs in the maps and
nothing more.

## 1. Decisions

### 1.1 Rulings (user, 2026-11-10)

| # | Ruling |
|---|---|
| M1 | All fifteen levels are the mine shaft under the town. Level 1 is light and clean; level 15 is dark and slimy. Each level feels different from the one before, and the mood darkens going down. |
| M2 | Levels are built from generated room layouts, so every level is a different map on every play-through. Rooms are separate shapes the map tools combine; level 1 is mostly hallways. |
| M3 | Levels have spaces for the Soldier and Demoman to rocket and sticky jump, and for the Scout to double and triple jump. |
| M4 | Theme arc: the mine breaks into things as it goes down, and ends in hell. This replaces D14's three tilesets (catacombs, mines, robot works) but keeps their flavours inside the one shaft. |
| M5 | Bosses stay as D13: level 5 Sir Nukesalot, level 10 Major Crits with a Tank, level 15 Captain Punch. Their levels are unique. |
| M6 | The way down: the town's cage lift serves levels 1 to 5. Below the first boss the lift is wrecked. Levels 6 to 10 descend by ramps, stairways and collapsed floors; 11 to 15 by drops, flooded sumps and slides. |
| M7 | Rooms are written by C# generators, as the town is, not built by hand in Hammer. |
| M8 | Jump spots are always optional. A Heavy can walk every critical path; jump spots give shortcuts, treasure ledges, high ground and escape routes. |
| M9 | Every hero carries a miner's lamp, so the deep levels can be close to black. The lamp is game code and a separate piece of work (section 9). |
| M10 | Deep entry: the town cage offers levels 1 to 5. Killing the level 5 boss opens a camp at level 6, and the level 10 boss a camp at 11, which the town can send a party to. The levels between camps are walked down on each run. This narrows R6 (any level reached). |
| M11 | Levels grow with depth: 3x3 to 4x4 grids for levels 1 to 4, 4x4 to 5x5 for 6 to 9, 5x5 to 6x6 for 11 to 14. |
| M12 | The shaft ties into the town (section 8). |
| M13 | The mod may mount any Half-Life 2 content. Levels may use HL2 caves, antlion hives, canals and sewers as well as TF2's. |
| M14 | The level 10 Tank fight lasts a good 10 to 20 minutes. |
| M15 | The plan moves to `docs/plans/tf2_descent/` with `plan_tf2_rpg.md` beside it, and is committed. |

### 1.2 Defaults I chose (overrule by editing this table)

| # | Gap | Default |
|---|---|---|
| E1 | Cell size and door kit | Normal levels: 1024-unit cell, door 256 wide by 192 high, wall depth 32. Boss levels use a larger cell (section 3.2). |
| E2 | Libraries | One room library per depth, fifteen in all, each made by the shared shape generators with that depth's theme. The host links one library per level today, and a library carries the level's fog and tone map, which is what makes the darkness a property of the depth. |
| E3 | Layout | The Descent rules module lays levels out (`TF2DescentRules.GenerateLayout`), not the tools' own generator, because boss placement, the low-room rule and the per-depth sizes are game rules. |
| E4 | R19's 60 % jump-room share | Held per act (levels 1-5, 6-10, 11-15), not per level, so level 1 can be hallways and level 9 can be low crypts. |
| E5 | Ladders | Not climbable. Stock TF2 has no ladder movement, so ladders are dressing and every descent is a ramp, stair, drop or lift. |
| E6 | Brightness target | The mean lightmap brightness of a level, as `ssmap preview` reports it, falls strictly from each level to the next (section 6). |
| E7 | Asset choice | Every material and model a level uses is a row in the asset database, recorded in a per-level palette file that a test checks. |

## 2. What exists today

Each line was read or queried this session.

**The room pipeline** (map tools, `../mt-asset-db/README.md` section "`room`,
`rooms`, `link` and `layout`", and `docs/rooms-full-features.md`):

- A room library is one VMF of rooms, each in its own cell, marked by an
  `info_room`. `ssmap room` compiles and lights every room once into a
  `.roompack`. `ssmap link` joins a level's rooms into a `.bsp` with its
  `.nav3d` and `.map2d` in well under the time of a compile.
- Every room is one cell (map tools decision D30). A room may be taller or
  lower than its cell (`room_height`). Floors at doors stay at z = 0 (D27):
  a room may hold pits, decks and storeys inside itself, but all its doors
  are on one floor.
- There is no open space wider than one cell. Adjacent rooms always meet at
  a doorway of the kit's size. This sets the size of a boss arena (3.2).
- Libraries placed in one level must share the cell size and door kit.
  Different levels may use different kits.
- One sun per level. Fog, tone map and the other controllers are library-wide.
  A sky ceiling and a 3D skybox room are supported (the `shaft` room of
  `samples/rooms-multi`).
- Light crosses one doorway only: a lamp lights its own room and the next.
- Rooms carry brush entities, props, displacements, water (joined through
  declared water sockets), overlays, detail props and `info_poi` markers.
- Transition rooms: `room_role up` and `down`, a `trigger_room_transition`,
  something that fires it, and an `arrival` point. With `-mod-entities` the
  link writes `logic_level_transition`, which the mod implements
  (`src/tf2_descent/server/LevelTransition.cs`).
- A level may use `2048 - 512` entities by default; the link reports the
  headroom.
- `ssmap preview` renders a compiled map to a sheet of labelled views with
  brightness figures, and `-movement <class>` adds where a player of that TF2
  class can walk, plus 2,000 fuzzed blast jumps (`AirborneFuzzer`).

**The asset database** (`ssassets`, `../mt-asset-db/src/SourceSharp.AssetDb`,
merged to the map tools' GitHub main at 9d600ed):

- One SQLite file `assets.db` plus PNGs. It is not YAML. It is searched with
  `ssassets query --out <dir> "<sql>"` through the views `material_view`,
  `model_view`, `material_usage` and `model_usage`.
- Each material has its category, surface property, size, average colour and
  the number of shipped maps that use it. Each model has its size, whether it
  is a static prop, and a sheet of six views.
- The only export on this machine is in another session's `/tmp` scratchpad:
  TF2 (31,035 materials, 4,571 models, 250 maps read) and the SDK Base `hl2`
  folder. It can disappear. Phase 0 makes a permanent one.
- The full Half-Life 2 cave, antlion and sewer content is not in it, only
  the SDK Base subset. The mod's `gameinfo.txt` already mounts `hl2` and
  `hl2_complete`, so Phase 0 imports a full HL2 install as well (M13). Some
  skies that would fit are missing (`sky_hell_01`, koth_slime's sky).

**The host** (`../source-sdk-sharp-host`, at 9a70c1c):

- The host links every level in process and keeps a pool of ready levels per
  depth (`Host.MapPool/MapPoolWorker.cs`). `MapPoolOptions.Depths` is 15 and
  `LibraryManifest.BossDepths` is 5, 10, 15.
- A library comes with a `library.json`: per room its tags (`start`,
  `stairs`, `boss_arena`, `treasure`, `corridor`, `junction`, `shrine`),
  weight, depth range, `jump`, `low` and markers
  (`TF2.Descent.MapForge/LibraryManifest.cs`).
- Layout today is the tools' generator on a fixed 3x3 grid
  (`MapPoolLayoutOptions.ToolLayout = true`). It does not place a boss arena
  and takes no per-depth size. The other path calls the rules module's
  `GenerateLayout(LayoutKey)`, but `RoomInfo` carries no door sockets, so a
  rules layout cannot know which rooms fit together.
- No Descent room library exists in any repo.

**The game** (this repo):

- `TF2DescentRules.GenerateLayout` is a placeholder: arrival, one room, exit,
  in a row (`src/tf2_descent/rules/TF2DescentRules.cs`).
- From the town, travel always asks for depth 1
  (`src/tf2_descent/server/HostLink.Travel.cs`). The depth picker screen
  exists with sample data only.
- Nothing in the game reads `.map2d` or the `.nav3d` arrival points yet, and
  `logic_room` is not implemented.
- No robot, boss or furniture entity exists.
- The language file has tile names Catacombs, Mines and Robot Works
  (`game/mod_tf2_descent/resource/tf2_descent_english.txt`).

**The town** (`src/mapgen/TF2DescentTown.cs`, built by `make town`):

- Map `tf2_descent_town`: a valley 3072 by 2048 under `sky_badlands_01`.
- The shaft is a 192 by 192 opening at x = 1300, 384 deep, timber-lined, with
  a headframe over it and a 128 by 128 cage beside it.
- `func_button mine_cage_button` fires `logic_level_transition mine_descent`
  (`direction down`, map `tf2_descent-1`).
- Eight spawns named `tf2_descent_arrival` stand round the shaft. Nothing
  uses the name yet.
- The dressing work in `../ss-descent-town` (six commits, tip b1bb81a36) is
  not on main. It replaces the brush headframe with
  `models/props_mining/minehead.mdl` and adds a rail spur.

**Movement constants** (this repo's TF2 port):

- Jump height 52.2 units, step 18, hull 48 by 48 by 82, ducked 62
  (`src/tf2/shared/Movement/TFViewVectors.cs`).
- The Scout's air dash sets an upward speed of 268.33
  (`src/tf2/shared/Movement/TFJump.cs`), which is 45 units of rise.
- A blast's push is capped at 1000 units a second
  (`src/tf2/server/Projectiles/TFRadiusDamage.cs`), which is 625 units of
  rise at most from one blast.
- No measured apex or carry of a rocket or sticky jump exists. The map tools
  have a branch in progress for it (`claude/blast-reach`).

## 3. The kit

### 3.1 Normal levels

| Setting | Value | Why |
|---|---|---|
| `cell_size` | 1024 | R19's room: a rocket jump needs about 512 of clear air and a 768 run. |
| `door_width`, `door_height` | 256, 192 | A giant robot at scale 1.9 is 92 wide and 156 tall (R16). A wide door also lets more light and sight through, which the one-doorway light rule needs. |
| `wall_depth` | 32 | |
| Navigation voxel | 16 | The tools' default; every height below is a multiple of it. |

Room heights come in four classes:

| Class | `room_height` | Used for |
|---|---|---|
| Low | 192 to 256 | Drifts, crawls, crypt passages. No jumping. |
| Chamber | 512 | Ordinary rooms. Scout tiers fit; a rocket jump touches the roof. |
| Hall | 1024 | The standard jump room. |
| Shaft | 1536 to 2048 | Ore passes, caverns, the lift shaft. Sticky-jump height. |

### 3.2 Boss levels

A boss arena must be one room, because rooms never open into each other. So
each boss level has its own kit with a larger cell: 2048 for level 5, 3072
for level 15, and 4096 for level 10, whose Tank needs a long track (M14). The few other rooms of a boss level are generated on
that same kit by the same shape generators, so they are larger and grander
than a normal level's. Section 7 has the arenas.

Open areas wider than one cell would need the map tools' dropped multi-cell
rooms (their D30). This plan does not ask for them.

## 4. Jump space

### 4.1 Tiers

The heights and gaps below are worked from the constants in section 2. They
are estimates until Phase 0 measures them with the movement simulator; the
room generators take them from one table, so a measured number changes every
room at once.

| Tier | Who reaches it | Ledge height | Gap |
|---|---|---|---|
| T0 | Everyone, walking | up to 18 (a step) | none |
| T1 | Everyone, crouch-jump | up to 64 | up to 160 |
| S2 | Scout, double jump | 96 to 112 | 256 to 448 |
| S3 | Scout, triple jump (Atomizer or an `air_dash_count` item) | 136 to 152 | 480 to 640 |
| B1 | Soldier or Demoman, an easy blast jump | 256 to 320 | 512 |
| B2 | A good rocket jump, a sticky jump | 448 to 512 | 768 |
| B3 | A double sticky or a chained jump | 768 and up | over 1024 |

A ledge at height h needs clear air to h + 114 (the hull plus a margin). A
blast-jump room needs 512 of clear air over half its floor and a straight
run of 768 (R19's placeholder).

### 4.2 Rules

- **The critical path is T0 and T1 only** (M8). The layout check walks it as
  a Heavy.
- **Every jump spot pays.** Each ledge above T1 ends in one of: a shortcut
  across a loop, a container marker, a vantage over the next room, or a way
  out of a fight. A ledge that leads nowhere fails lint.
- **Each level offers each class something.** A normal level places at least
  two S-tier spots and two B-tier spots, counted by the layout generator.
- **Robots fit.** Jump volumes are also where giants fit and where they land.
- **Decoration does not eat the volume.** Clear air is measured from brushes
  and props, as R19 says, not taken from the room's tag.

### 4.3 Jump features

The shape generators build these into rooms:

| Feature | Tier | What it is |
|---|---|---|
| Crate steps | T1 | Stacked crates or carts up to a low deck. |
| Mezzanine | S2 | A deck at 104 along one wall; stairs at the far end for everyone else. |
| Stepping pillars | S2, S3 | Pillar tops at 104 and 144 across a pit; the floor route goes round. |
| Chasm leap | S2, S3 | A gap of 384 or 512 with a bridge further along. |
| Rocket shelf | B1 | A shelf at 288 with a container marker; no other way up. |
| Catwalk ring | B2 | A catwalk at 448 round a hall, reached by a lift or stair two rooms on, or directly by a jump. |
| Chimney | B2, B3 | A shaft room with ledges at 320, 640 and 960. |
| Roof gallery | B3 | A ledge near the roof of a shaft room, for stickies. |

## 5. Room shapes

One generator per shape family. Each takes the kit, a seed and a theme, and
writes several variants (different pillar counts, deck sides, pit positions),
so a library holds about 32 rooms from about 20 families. A room's doors are
listed as the walls it opens on.

**Connectors (low):**

| Shape | Doors | Notes |
|---|---|---|
| Drift | 2, opposite | A straight hallway. Width 256 to 384. |
| Bend | 2, adjacent | |
| Tee | 3 | |
| Cross | 4 | |
| Dogleg | 2, opposite | Breaks the sight line. |
| Stub | 1 | A dead end with a treasure or shrine marker. |
| Wide drift | 2 | A double-track rail hallway with a centre row of posts. |

**Chambers (512):**

| Shape | Doors | Notes |
|---|---|---|
| Pillar room | 2 to 4 | Cover for the ground game. |
| Split level | 2 to 3 | A mezzanine (S2). |
| Pit room | 2 to 4 | A sunken centre 128 deep with ramps; stepping pillars. |
| Store | 1 to 2 | Crate maze; crate steps. |
| Yard | 3 to 4 | Rails, carts and a turntable in levels 1 to 5. |

**Halls and shafts (1024 and up):**

| Shape | Doors | Notes |
|---|---|---|
| Stope | 2 to 3 | Terraces at 128, 256 and 384 up one side. |
| Gallery | 2 to 4 | A catwalk ring (B2) and a rocket shelf. |
| Chasm | 2 | A gap across the room; a bridge and a chasm leap. |
| Chimney | 1 to 2 | Shaft height; ledges up the walls. |
| Cavern | 2 to 4 | Displacement floor and walls; stalactites; a high alcove. |
| Pool | 2 to 4 | Water through water sockets, levels 8, 12, 14. |

**Role rooms:**

| Shape | Role | Notes |
|---|---|---|
| Arrival | `room_role up` | The way in, in the act's style (section 8). |
| Exit | `room_role down` | The way down, in the act's style. |
| Camp | `room_role up` | Levels 6 and 11 only (M10). |
| Antechamber | none | Boss levels: the room before the arena. |
| Arena | `boss_arena` | One per boss level, unique (section 7). |

Every combat room declares markers as `info_poi` points, which cost no
entity: robot pack spawns, and the furniture markers of R24 (shrine,
container, dispenser, barrel, trap). What stands on a marker is decided when
the level loads, so a room is never furnished the same way twice.

## 6. Light and mood

The mood is carried by five things that all move one way with depth:

| Thing | Level 1 | Level 15 |
|---|---|---|
| Brightness | Bright, even, no dark corners | Near black between lamps |
| Colour | Warm white, painted steel, pale concrete | Black-green, wet |
| Wetness | Dry and swept | Standing slime, dripping |
| Order | Straight, square, signed | Broken, organic, no right angles |
| Lamps | Electric lamps every 256 | A few fires and glows |

**The brightness target.** `ssmap preview` reports lightmap statistics for a
map. Taking level 1's mean as 100, each level's target is:

| Level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Target | 100 | 85 | 70 | 60 | 52 | 42 | 35 | 30 | 25 | 22 | 19 | 16 | 13 | 10 | 7 |

The gate is that the measured figure falls strictly from each level to the
next, over several seeds of each. The lava level (13) is the one to watch:
its lava glows, so its fill light has to be lower than 12's.

**How each level gets there:**

- Lamp kind, colour, strength and spacing are theme settings of the depth's
  library, applied by the generators.
- Each library has its own fog and tone map. Fog is off at level 1 and closes
  to about 900 units at level 15, in the level's colour.
- Wall and floor materials are chosen within a brightness band per level,
  using the database's `average_colour`. Level 1's surfaces are pale; level
  15's are near black.
- The path is lit. On every level the critical path has a lamp, fire or glow
  in sight from the last one. With the miner's lamp (M9) side rooms may be
  fully dark from level 11 down.

## 7. The fifteen levels

Asset names below are rows of the asset database (TF2). None has been looked
at yet; Phase 3 onward opens each one's preview before using it.

### Act I: the Mann Co. mine (1 to 5). The lift works.

| | 1. The Collar | 2. Haulage | 3. Timber Stopes | 4. The Mill |
|---|---|---|---|---|
| Idea | The pit bottom, just under the town. New, painted, signed. | The rail level. Long straight tunnels and loading bays. | Older workings held up by timber. | Ore crushing. Machines, hoppers, dust. |
| Grid | 3x3 | 3x4 | 4x4 | 4x4 |
| Shapes | Drifts, bends, tees; two chambers; one store | Wide drifts, yards, a first stope | Stopes, split levels, pit rooms | Galleries, a chimney, stores |
| Height | Low and chamber | Chamber, one hall | Hall | Hall, one shaft |
| Jumps | Crate steps, one mezzanine | Carts as steps, a rocket shelf | Terraces, stepping pillars | Catwalk ring, chimney |
| Walls | `concrete/concretewall011`, `metal/wall027`, `coalmines/red_wall1_middle` | `pl_barnblitz/metalroof_panels001`, `nature/rockwall012` | `nature/rockwall013`, `wood/wood_wall006` | `metal/wall026`, `coalmines/rockwall012` |
| Floor | `concrete/concretefloor003` | `nature/gravelground001`, `metal/metaltrack001` | `coalmines/dirtground004` | `metal/metalgrate011a`, `nature/gravelcoal001` |
| Props | `props_mining/support_wall001a`, `props_2fort/miningcrate001`, `props_trainyard/handrail128` | `props_mining/track_straight_256`, `props_vehicles/mining_cart_supplies001`, `props_trainyard/cart_base_separate` | `props_mining/tunnel_truss01`, `props_coalmines/wood_support_256`, `props_farm/wooden_barrel` | `props_mining/generator01`, `props_mining/factory_pipe003`, `props_mining/crane001_small` |
| Lamps | `props_mining/industrial_ceiling_lamp`, close together | `props_mining/industrial_ceiling_lamp02`, `props_lights/flood_light_left001` | `props_2fort/lantern001`, `props_lights/hangingbulb` | `props_hydro/construction_light`, few and yellow |
| Light | 100: warm white, daylight down the shaft | 85: white with shadowed side bays | 70: lantern pools | 60: sodium yellow, dusty fog |
| Way down | Cage | Cage | Cage | Cage |

**Level 5, the Powder Magazine. Boss: Sir Nukesalot.** Kit cell 2048.

- The level: the lift's last station, an antechamber, the arena. A fixed row
  of three or four rooms; the antechamber is drawn from several variants and
  the arena's dressing and cover are seeded.
- The arena: a round blasted chamber about 1900 across and 1024 high, the
  mine's explosives store. Stone pillars for cover from his cannonballs. A
  ring ledge at 104 (Scout) and shelves at 288 (blast jump) give ways out of
  his reach. Powder barrels on barrel markers.
- Light 52: red warning lamps, one white work light over the centre.
- The way down: his death blast collapses the floor of the lift shaft. The
  hole is the exit, and the reason the lift goes no further.

### Act II: the breakthrough (6 to 10). No lift; ramps and collapsed floors.

| | 6. The Breakthrough | 7. Dripstone | 8. Sunken Galleries | 9. The Buried Chapel |
|---|---|---|---|---|
| Idea | The miners broke into a natural cave and left. Half timber, half rock. | Pure cave. Tall chambers, stalactites, a waterfall. | The flooded level. Planks over murky water. | The cave opens into crypts older than the town. |
| Grid | 4x4 | 4x5 | 5x5 | 5x5 |
| Shapes | Camp, drifts into caverns, a chasm | Caverns, chimneys, chasms | Pools, pit rooms, drifts with water | Low crypt drifts, stubs, two tall naves |
| Height | Chamber and hall | Hall and shaft: the most vertical level | Chamber and hall | Low; the deliberate ground-game level |
| Jumps | Chasm leap, rubble steps | Every tier; roof galleries | Stepping pillars over water | Few: nave ledges only |
| Walls | `nature/rockwall006`, `coalmines/blendgroundtowall_coalmines` | `nature/rockwall001_island`, `nature/blendrockgroundwallisland_interior` | `nature/rockwall006a`, `swamp/nature/karst_rock001` | `medieval/stonewall001a`, `medieval/stonetrim001a`, `cp_manor/wood_wall002_burnt` |
| Floor | `nature/blendrockground004` | `nature/dirtfloor005b` | `nature/blendgroundtomud001`; water `water/water_well` | `koth_viaduct_event/viaduct_cobblestone001` |
| Props | `props_mining/rock003`, `props_coalmines/boulder_pile1`, `props_mining/wooden_barrier01` | `props_island/stalagtites_large`, `props_island/stalagtites_med`, `props_mvm/cave_waterfall` | `props_swamp/karst_rock_01`, `props_island/island_drainage_pipe`, `props_coalmines/wood_debris2` | `props_viaduct_event/coffin_simple_closed`, `props_manor/gravestone_07`, `props_halloween/cobweb_01` |
| Lamps | `props_2fort/lantern001`, far apart; some `lantern001_off` | `props_mining/tracklights_4` along a rope line | `props_swamp/lamp_post001`, light off the water | `props_manor/wall_candelabra_01`, `props_halloween/candle_cluster` |
| Light | 42: cold grey with warm lantern pools | 35: blue-grey | 30: dim green | 25: candle orange in black |
| Way down | Rubble ramp into the cave | Rope-line stair down a shaft | A sump: a drained shaft with a stair | A broken crypt floor |

Level 6's arrival is a camp (M10): a lit, dressed room with a hoist to the
town, opened by the level 5 kill.

**Level 10, the Bore. Boss: Major Crits with a Tank.** Kit cell 4096.

- The level: arrival, antechamber, arena.
- The arena: a cavern about 3900 across and 1280 high that Gray Mann's
  robots have bored up into. Catwalks at 448 over the track let blast
  jumpers bomb the Tank from above; rock steps at 104 and 144 serve the
  Scout. `props_mvm/mine_elevator` and
  `props_medieval/tank_entrance_rottenburg` dress the robots' end.
- The track, sized for a 10 to 20 minute fight (M14): a spiral of about
  9,000 units that winds inward from the bore hole to the central pillar
  that holds the roof, past four outer roof pillars. The Tank stops at each
  outer pillar to drill it down, and the fight is lost if it brings down
  the central one. As a first budget for 15 minutes: five legs of 1,800
  units at 30 units a second (5 minutes moving), four drilling stops of 2
  minutes, and 2 minutes at the centre. Stock MvM's Tank moves at 75; the
  speed, the stop times and the Tank's health are the encounter's to tune.
  The map's part is the track length, the five pillar stations, cover along
  each leg, and robot spawn mouths that open leg by leg, so the fight moves
  round the arena instead of staying in one place.
- Each fallen pillar drops rubble that changes the arena: new cover, a
  blocked catwalk, a new ramp. They are brush entities the encounter
  switches.
- Light 22: the candles are gone; cold white robot work lights and the
  Tank's own glow.
- The way down: the bore hole the Tank came up.

### Act III: below (11 to 15). Drops, sumps and slides.

| | 11. Robot Works | 12. The Sump | 13. Brimstone | 14. The Rot |
|---|---|---|---|---|
| Idea | Gray Mann's factory, abandoned and failing. | The works' waste level. Pipes, toxic slime channels, corrosion. | The diggings broke into the underworld. Lava and black rock. | Hell's cesspit. Organic, wet, almost no light. |
| Grid | 5x5 | 5x6 | 6x6 | 6x6 |
| Shapes | Camp, galleries, yards, wide drifts | Pools, pit rooms, chimneys | Chasms over lava, caverns, stopes | Caverns, pools, doglegs; no straight drift |
| Height | Hall | Chamber and hall | Hall and shaft | Mixed; low crawls between tall caverns |
| Jumps | Catwalk rings, rocket shelves | Stepping pillars over slime, pipe tops | Chasm leaps over lava, chimneys | Roof galleries, pillars in slime |
| Walls | `metal/wall026`, `swamp/metal/swampmetal001` | `swamp/concrete/swampcrete001`, `swamp/brick/swampbrickwall001` | `nature/underworld_rockwall001`, `nightfall/cliffwall` | `nature/blendrockgroundwallforest004`, `swamp/nature/swampmud004` |
| Floor | `metal/metalgrate011a`, `swamp/tile/floor_tile_002a_worn` | `swamp/nature/swampmud003`; slime `nature/toxicslime002a` | `nature/blendcobblestone_underworld001`; lava `nature/underworld_lava002` | `nature/blendmudmud001a`; water `water/water_underworld` |
| Props | `props_trainyard/blast_furnace002`, `props_powerhouse/powerhouse_turbine`, `props_well/overhead_crane` | `props_mining/industrial_pipe01_256_01`, `props_farm/sewer_grate_end01`, `props_swamp/oildrum_open` | `props_viaduct_event/underworld_bridge04`, `props_halloween/halloween_rock001`, `props_manor/gargoyle_01` | `props_swamp/shroom_ref_01_cluster`, `props_swamp/rock003_swamp`, `props_halloween/skull_island_horns` |
| Lamps | `props_spytech/hanging_ceiling_lamp`, half of them dead; red emergency light | `props_swamp/bug_zapper`; the slime's own green | `props_viaduct_event/underworld_torch01`; lava glow | A few glows only |
| Light | 19: flicker and red | 16: sick green | 13: red on black | 10: black-green |
| Way down | A waste chute (slide) | A dive into a sump | A fissure drop | A slide of slime into the pit |

Level 11's arrival is the second camp (M10), opened by the level 10 kill.

**Level 15, the Pit. Boss: Captain Punch.** Kit cell 3072. `down: none`.

- The level: arrival by the slime slide, a short antechamber, the arena.
- The arena: a black slime lake about 2900 across under a roof lost in the
  dark, with a stone platform in the middle and islands round it. He is a
  melee boss, so height is safety: pillar tops at 104 and 144 for the Scout,
  ledges at 288 and 448 for blast jumpers. Sections of the platform are
  separate brush entities so the encounter can break them away between
  phases, leaving islands; shallow slime is a hazard volume the encounter
  can switch on in stages.
- Light 7: the darkest place in the game. Torches on the platform only.
- This is the bottom. What his death opens is the RPG plan's business.

## 8. The town and the ways down

**Level 1 is the bottom of the town's shaft.**

- Level 1's arrival room is a shaft room whose shaft is the town's: 192 by
  192, the same timber lining, the same 128 by 128 cage and button. Its
  ceiling is sky, lit by a sun equal to the town's `light_environment`, under
  the same `sky_badlands_01`, so daylight falls down the shaft onto the cage.
- The town generator and the room generators take the shaft size, cage size,
  lining material, lantern and sun from one shared C# class in `src/mapgen`.
  A test holds the two maps to it.
- Level 1's first rooms use the town's own materials and lanterns, then hand
  over to the level's concrete and steel.
- Going up from level 1 returns to the town at the `tf2_descent_arrival`
  spawns round the shaft. The game has to start using that name.

**The cage and the camps.**

- The cage button opens the depth picker. It lists levels 1 to 5 as reached,
  then Camp 6 and Camp 11 once their bosses are dead (M10).
- Each of levels 1 to 4 has a cage station as its arrival and a different
  cage as its exit: you walk the level from one winze to the next. The
  stations are the same shape family dressed for the depth, so the lift reads
  as one thing getting older and dirtier.
- Level 5's exit is the hole Sir Nukesalot leaves. Below it, each level's
  exit is listed in section 7, and the camp rooms at 6 and 11 carry a hoist
  back to the town.

**What the town gains** (small changes to `TF2DescentTown.cs`):

- A depth board at the headframe naming the levels reached.
- The hoist house and rope for the two camps, shown once they are open.
- These wait on the dressing work in `../ss-descent-town` landing first,
  since it replaces the headframe.

## 9. Work in other places

| Where | What | Why |
|---|---|---|
| Host | `RoomInfo` gains each room's doors, height class, `jump` and `low` | A rules layout cannot fit rooms together without them. |
| Host | Per-depth library and kit; boss depths on their own kit | Sections 3.2 and E2. |
| Host | `KnownTags` gains `camp` and `antechamber` | New role rooms. |
| Host and game | The picker's depths: 1 to 5, Camp 6, Camp 11 | M10. Travel from the town asks for depth 1 only today. |
| Game | The miner's lamp: a light on each hero | M9. Needs its own plan; the maps do not depend on it until level 11. |
| Game | Read arrival points from `.nav3d`; use `tf2_descent_arrival` in the town | Section 8. |
| Map tools | The blast-jump reach work (`claude/blast-reach`) landing | Phase 0's measurements. A request to that session, not a change this plan makes. |
| Map tools | A double-jump step in the movement simulator, if it has none | The Scout tiers need measuring too. To be confirmed in Phase 0. |

## 10. Phases

Each phase ends with a commit per step and the tests it names. Phases 3 to 6
each end at a checkpoint: the user looks at the preview sheets before the
next act starts.

**Phase 0: measure.** No product code.
- Import a permanent asset database outside the repo with `ssassets import`
  (TF2 and a full Half-Life 2 install with its episodes, M13), and record
  its path. No full HL2 folder was found on this machine by name; finding
  or installing it is the first step.
- Measure the jump tiers with `ssmap preview -movement scout|soldier|demoman`
  on a test map of ledges and gaps, and replace section 4.1's estimates.
- Build one drift and one hall on the 1024 kit and one arena on the 3072 kit
  and one on the 4096 kit by hand-written generator code; run `ssmap room`,
  `link` and `preview`.
  Record compile time, pack size, brush and entity counts.
- Confirm the preview's brightness figure moves when the lamps do.
- Exit: the numbers are in this plan's Decisions section.

**Phase 1: the kit and the shapes, in grey.**
- The shape generators of section 5 in `src/mapgen`, with dev materials, the
  markers, and each room's `library.json` entry.
- Lint, one fact per rule: sealed, doors on the kit, every marker on
  walkable floor, clear air of jump rooms measured, every ledge above T1
  leads somewhere.
- Exit: one grey library links at every grid size of section 7, and the
  movement report shows every critical path walkable as a Heavy.

**Phase 2: the layout.**
- `TF2DescentRules.GenerateLayout` for real: critical path from arrival to
  exit, branches and loops, treasure off the path, never more than two low
  combat rooms in a row, the per-depth sizes, and the boss levels' fixed row.
- The host changes of section 9 that it needs.
- Exit: facts over 10,000 seeds per depth (connected, every door matched or
  capped, the jump-spot counts of 4.2, same input gives the same level).

**Phase 3: Act I and the town.** Levels 1 to 4, their palettes from the
database, the shared shaft class, level 1's sky shaft. Checkpoint.

**Phase 4: Act II.** Levels 6 to 9, displacement caves, water, the camp.
Checkpoint.

**Phase 5: Act III.** Levels 11 to 14, slime and lava, the second camp.
Checkpoint.

**Phase 6: the boss levels.** The three arenas as geometry, cover, ledges
and markers. Checkpoint.

**Phase 7: the whole shaft.**
- All fifteen levels link from five seeds each, inside the entity and brush
  budgets.
- The brightness figure falls strictly from 1 to 15.
- The movement report for each of Scout, Soldier and Demoman shows floor only
  they reach on every normal level, and no trap.
- A walk-through list for the user to play in game.

## 11. Task list: everything needed in managed code

All of it is C#. Nothing here needs new C++: the levels are ordinary Source
maps, and the entities they use already have managed classes in this repo
unless a row says otherwise. "Exists" was checked by a search this session;
it means the class or file is there, not that it has been tried on a Descent
level.

### A. Room generators (`src/mapgen`, this repo)

| # | Task | Today |
|---|---|---|
| A1 | A shared class of shaft, cage, lining, lantern and sun values, used by the town generator and the room generators | Values are private constants in `TF2DescentTown.cs` |
| A2 | A room-library writer: cells with gaps, `info_room` and its keys, door plugs, `room_height`, `room_role`, water sockets, the library's sun, fog and tone map in the gaps, the skybox room | `Vmf.cs` writes boxes, panes and entities only |
| A3 | A displacement writer for cave floors and walls | `Vmf.cs` has none |
| A4 | Non-box brushes: ramps, wedges, round pillars and chamber walls | Boxes and panes only |
| A5 | The jump-tier table as data, read by every shape generator | New |
| A6 | The shape generators of section 5: 7 connectors, 5 chambers, 6 halls and shafts, with seeded variants | New |
| A7 | The role rooms: cage station, the act II and III exits, the two camps, antechambers, each with its transition volume, its trigger or button and its arrival point | New; the town's cage and button are the pattern |
| A8 | The three arenas: the Powder Magazine, the Bore with its track (`path_track`), pillar stations and rubble brushes, the Pit with its breakaway platform and staged hazard volumes | New |
| A9 | Theme data for the fifteen depths: materials, props, lamp kind, colour, strength and spacing, fog, tone map, wetness | New |
| A10 | The dressing pass: lamps and their `light` entities, props from the theme, kept clear of jump volumes and inside the cell | New |
| A11 | Markers as `info_poi`: pack spawns, shrine, container, dispenser, barrel, trap, map labels and map markers | New |
| A12 | `library.json` written beside each library: tags, weight, depths, `jump`, `low`, markers, doors | New |
| A13 | A `descent-rooms` verb in `src/mapgen/Program.cs` and a `make descent-rooms` target that writes the fifteen libraries and packs them with `ssmap room` | `make town` is the pattern |
| A14 | Level 1's sky shaft and skybox room matching the town | New |

### B. Checks on the rooms (`src/test`, this repo)

| # | Task | Today |
|---|---|---|
| B1 | Lint facts per rule: sealed, doors on the kit, markers on walkable floor, measured clear air in jump rooms, every ledge above T1 leads somewhere, props inside the cell | New |
| B2 | A palette fact: every material and model a theme names is a row in the asset database, and a brush material has brush usage | New; skips when the database is absent |
| B3 | A town-and-level-1 fact: both maps hold the shared shaft values | `TF2DescentTownTests.cs` covers the town alone |
| B4 | A brightness gate: link each depth from several seeds, run `ssmap preview`, and require the figure to fall strictly from 1 to 15 | New |
| B5 | A reach gate: `ssmap preview -movement` per class; the critical path walkable as a Heavy, floor only Scouts and blast jumpers reach on each normal level, no traps | New |
| B6 | A budget fact: every depth links inside the entity, brush, model and displacement limits at its largest grid | New |

### C. Layout and level rules (`src/tf2_descent/rules`, this repo)

| # | Task | Today |
|---|---|---|
| C1 | `GenerateLayout` for real: critical path, branches, loops, treasure off the path, doors matched or capped, quarter turns | A three-room placeholder |
| C2 | The per-depth table: grid size, shape weights, share of jump rooms, low-room rule, jump-spot counts | `LevelTable` is a stub |
| C3 | Boss levels: the fixed row of arrival, antechamber, arena | New |
| C4 | Facts over 10,000 seeds per depth | New |
| C5 | Which depths the town may send a party to: 1 to 5, Camp 6 after the level 5 kill, Camp 11 after the level 10 kill | New; R6 allowed any reached depth |

### D. The game server (`src/tf2_descent/server`, this repo)

| # | Task | Today |
|---|---|---|
| D1 | Place an arriving party at the level's arrival and spawn points from the `.nav3d` | Not done. A `.nav3d` reader exists for the bots (`src/ai/server/Nav/Nav3dNavSource.cs`) |
| D2 | Returning to the town arrives at the `tf2_descent_arrival` spawns | Name unused |
| D3 | Travel from the town takes the picked depth, not always 1 | `HostLink.Travel.cs` asks for depth 1 |
| D4 | Camps: record the boss kills, offer the camps, enable the camp hoists | New |
| D5 | Read a level's markers and hand them to whatever spawns robots and furniture | New; the spawners are other plans |
| D6 | A boss level's exit starts closed and opens on the kill, with the level's change (the collapsed lift floor, the bore hole) | `logic_level_transition` has `StartDisabled`; the rest is new |
| D7 | The miner's lamp: on for every hero in a level | A flashlight exists (`src/server/managed/PlayerFlashlight.cs`, drawn by the client); whether the TF2 hero can carry it, and what a party of four lamps costs, is unmeasured |
| D8 | Hazards: slime and lava hurt by damage type (fire for lava; slime to be ruled) | `trigger_hurt` is known to the TF2 damage layer; the Descent types are new |
| D9 | Travel notices per way down (cage, rubble, sump, chute, slide) | One text today, "The cage drops into the dark..." |
| D10 | `logic_room`, only if a room needs per-placement logic the link cannot fold away | Not implemented; avoided in the first libraries |
| D11 | The Tank's track and pillar stations as things the encounter can drive | `path_track` and `func_tracktrain` have managed classes; there is no Tank or boss entity |

### E. Client and UI (`src/tf2_descent/client`, `src/tf2_descent/ui`, this repo)

| # | Task | Today |
|---|---|---|
| E1 | The depth picker on live data: levels 1 to 5 and the camps | Screen exists with sample data only |
| E2 | The automap from the level's `.map2d`, revealed room by room | Screen exists; nothing reads `.map2d` |
| E3 | Fifteen level names and three act names as language tokens | Three tile names exist |
| E4 | The level's name on arrival | New |
| E5 | The lamp's light on the client for every hero in sight, not only the local player | The client draws the local player's flashlight |

### F. Core managed code, game-agnostic (this repo)

To be confirmed on a first linked level in Phase 0, and ported where missing
(R15: port what is required, never Descent-flavoured C++):

| # | Task | Today |
|---|---|---|
| F1 | Fog and tone map controllers per level | Class names exist |
| F2 | Water and swimming for a TF2 hero | Untested in Descent |
| F3 | Brush entities the arenas switch: `func_brush`, `func_breakable`, `func_door` | Class names exist |
| F4 | Triggers the ways down use: `trigger_once`, `trigger_multiple`, `trigger_push` for slides, `trigger_teleport` | Class names exist |
| F5 | Ambient sound per level (`env_soundscape`, `ambient_generic`) | Class names exist; the sounds are not chosen |

### G. The host (`../source-sdk-sharp-host`, C#)

| # | Task | Today |
|---|---|---|
| G1 | `RoomInfo` carries doors, height class, `jump` and `low` to the rules layout | Name, tags, weight and depths only |
| G2 | Rules layout on by default (`ToolLayout = false`) once C1 lands | Tools' generator on a 3x3 |
| G3 | A library and a kit per depth, boss depths on their own kit | One library key per link |
| G4 | `KnownTags` gains `camp` and `antechamber` | Seven tags |
| G5 | Take the fifteen libraries and their packs into the pool | Its intake was not read for this plan |
| G6 | Descent to a camp depth, refused until the boss kill is on record | `RequestDescent` takes a depth |

### H. The map tools (`../source-sdk-map-tools`, C#)

Requests to that session; this plan changes nothing there.

| # | Task | Today |
|---|---|---|
| H1 | Blast-jump reach in the movement simulator | On the branch `claude/blast-reach` |
| H2 | Scout double and triple jumps in the simulator | To be confirmed |
| H3 | A full HL2 import into the asset database | TF2 and SDK Base only |

### Order

Phase 0 needs H1 to H3 and a first pass of A2. Phase 1 is A1 to A7, A11,
A12 and B1. Phase 2 is C1 to C4, G1 to G4. Phases 3 to 5 are A9, A10, A13,
A14, B2 to B6 and F1 to F5, act by act. Phase 6 is A8, D6 and D11. D1 to D5,
D7 to D9, E1 to E5, C5, G5 and G6 can run beside the phases, since they need
only one linked level to work against.

## 12. Open questions

1. **Level names.** The names in section 7 are mine. The picker shows three
   tile names today; fifteen level names need language tokens.
2. **Level 13's light.** Lava is bright. If the brightness gate cannot hold
   with lava in it, 13 and 12 swap, or the lava is kept to cracks.
3. **HL2 palettes.** Section 7's asset names are TF2's, chosen before M13.
   Phase 0's import of full HL2 comes first; then the cave levels (6, 7),
   the flooded and slime levels (8, 12, 14) are re-picked with HL2's caves,
   antlion hive and canal content in the pool.
4. **Boss arena size.** 2048, 3072 and 4096 are guesses until the bosses
   exist. A 4096 room is the largest single room anything here has compiled;
   Phase 0 builds one to see what it costs.
5. **Room count.** About 32 rooms a library is 480 rooms to compile and
   keep. Phase 0's timing says whether that is cheap.
