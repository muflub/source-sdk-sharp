# plan_descent_mine_levels.md: the fifteen levels of the Descent mine shaft

Status: reviewed and committed 2026-11-10. Phases 0 to 2 done; Phase 3 (Act I) built and reviewed from its preview sheets on 2026-11-10 (section 1.3a, section 11's status table), waiting on the user's checkpoint.

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
| M16 | Levels are larger than M11 said (asked "is 3x3 too small?", then "increase by 50 %" on the larger sizes offered, then "try this and adjust if needed"). Rooms per level: 18 at level 1, 21 at 2, 27 at 3, 4 and 6, 32 at 7, 37 at 8, 9 and 11, 44 at 12, 51 at 13 and 14, on grids from 5x5 to 8x8. Boss levels keep their fixed rows. These are working numbers to adjust after play. |
| M17 | Every map an agent makes is reviewed from its preview sheets, with feedback, before it counts as done. |
| M15 | The plan moves to `docs/plans/tf2_descent/` with `plan_tf2_rpg.md` beside it, and is committed. |
| M18 | Limit dead-end hallways. A tunnel may end, but never in a hallway: a branch ends in a room worth the walk (a chamber, a hall, a treasure room, a shrine stub), and each depth caps how many dead ends a level has. |
| M19 | A level grows to its room budget (M16) in any shape, the way mine tunnels run, inside a bounding box, not on a fixed grid. Keep the room limit; let the outline be free. |
| M20 | Every tunnel ends in a room, and no door is capped: a door the layout does not join is never placed. |
| M21 | The chance of each kind of room adapts to what the level already holds: a kind or family short of its share is drawn more, one past it less, a family beside itself less. The same seed still makes the same level. |
| M22 | A room need not fill its cell. Only the doors are fixed; outlines vary, from variant to variant and from family to family. |
| M23 | Door frames are static props lining each room's half of a joined doorway, and the kit is cut to the frame. |
| M24 | Multi-cell rooms and fixed door slots are being added to the map tools. The Descent side adopts them after Act I: footprints in the layout, 2x2 and 3x3 caverns and arenas, and the boss levels off their oversized kits. |

### 1.3 Measured in Phase 0 (2026-11-10)

**Jumps.** From the map tools' movement simulator on its calibration branch
(`claude/blast-reach`, 9ffb401), which agrees with a TF2 game log on 34 of 35
lanes. Probe and outputs: `.scratch/p0-jump/` (untracked).

| Jump | Height | Gap or carry | Source |
|---|---|---|---|
| Any class, jump / crouch-jump | 50 / 72 | Heavy 210 / 228, Soldier 216 / 236, Demoman 246 / 268, Scout 328 / 360 | simulated; the Soldier's ledges match the game log |
| Scout, double jump | 97, crouched 119 | 604 | simulated with stock's dash speed (268.33) |
| Scout, triple jump | 144, crouched 166 | 878 | simulated; the simulator has no third jump of its own |
| Rocket jump, standing, no crouch | 308 | 0 | game log |
| Rocket jump, standing, crouched | 509 | 0 | game log |
| Rocket jump, running, crouched | 483 to 501 | 788 to 811 | simulated flight, computed push |
| Rocket jump, best height / best carry | 568 / 375 | 567 / 1,093 | simulated flight, computed push |
| One sticky, crouch-jump | 780 to 1,028 | 1,230 to 1,600 | computed; no Demoman game log |

Not measured: chained and wall rocket jumps, the Gunboats and Rocket Jumper,
air-strafing. The simulator's own Scout double jump uses the ground jump's
speed and is 15 % too high; that is a report for the map-tools session.

**The kit.** Commit 064d762ff (`src/mapgen/Descent/`), measured with the
tools at 9d600ed on a loaded box, so times are about twice noisy.

- **The tools refuse the plan's door.** At 9d600ed a door is centred up the
  cell face and must stand on the floor, which forces `door_height` to
  `cell - 2 x wall` (960 on a 1024 cell) and so allows no room lower than its
  cell. A two-line change puts the sill on the floor at any door height. It
  is committed on a local branch of our own clone
  (`../mt-descent-rooms`, `descent/floor-sill`, 2b3bd46) and everything in
  this plan is built with that clone. Ten of the tools' 2,390 room facts fail
  under it: three test the old rule itself, seven use test kits whose doors
  hang in mid-wall. Upstream needs it as a proper option; that is task H4.
- **Navigation allows 128 voxels along a cell.** The boss kits use a 32-unit
  voxel (`nav_voxel_size`); the normal kit keeps 16.
- **Cost.** A six-room normal library packs in 4 to 12 seconds into 17 MB;
  a 2x3 level links in about 2 seconds to a 1.9 MB map of 41 brushes and 12
  entities. A 3072 arena room packs in 5 to 9 seconds (9.9 MB), a 4096 one in
  5 to 10 seconds (15 MB), with no refusal. Most of a pack is the door light
  data. So 480 rooms is on the order of minutes and about 1.5 GB of packs.
- **Brightness figure.** `index.json`, `checks.lighting.mean`: the mean
  linear luminance over every luxel of the map. Halving a hall's two lamps
  moved the level from 1.41 to 0.97 and that hall's camera from 0.39 to 0.20,
  while a room not joined to it stayed at 0.09.
- **Doorways draw as gaps.** The sides, sill and head of a joined doorway
  have no faces. Each door needs a frame placed in the socket (task A15).
- **A corridor in a cell.** One plus-shaped shell: a middle square and an arm
  to each door. The rest of the cell is outside the seal and costs no faces
  or lightmap. A drift is 6 brushes.

**Doorways (Phase 1).** Nothing free covers a joined doorway's missing
faces: socket furniture keeps one side only, world and detail brushes inside
the plug are culled, and no shipped prop is a frame of the right depth. Each
room lines its half of a joined doorway with a timber `func_brush` kept only
where the door is joined (`room_needs joined_<wall>`). That is one entity per
joined door per room, about 110 on a 51-room level, inside the budget of
1,536. A capped door shows the plug's tool texture, so the plug gets a
material that looks like the wall. A way to frame a door at no entity cost
is task H6.

**The layout (Phase 2).** Over 10,000 seeds at each depth with a synthetic
77-room catalogue: no depth failed to lay out and no jump minimum fell back.
Level 1 is 70 % low connectors and 33 % jump rooms; level 9 is 36 % jump
rooms; every level from 3 down has loops on every seed.

**The asset database.** Permanent copy at `~/assetdb` (4.8 GB):
TF2 (31,035 materials, 4,571 models, 250 maps) and the SDK Base `hl2` folder
(5,107 materials, 1,882 models). Full Half-Life 2 (Steam app 220) is not
installed on this machine, so M13's extra content waits on that install and
a `gameinfo.txt` mount.

### 1.3a Measured in Phase 3, Act I (2026-11-10)

Act I's four levels on the free-form layouts (M19) with the real shapes and
the dressing, branch `descent-mine-plan`, map tools at
`../mt-descent-rooms` (`descent/floor-sill`), debug build, three seeds a
depth. Logs and the review of every sheet: `.scratch/p3-merge/` (untracked).

**Libraries.** 65 rooms a depth at levels 1 to 4 (19 families). Dressed,
linted and packed by `make descent-rooms` in about two and a half minutes a
depth with its three levels; the packs are 250 MB (level 1) to 390 MB
(level 2).

**Levels.** Generated in under a second; each links in about 3 seconds and
previews in about 10. 18 to 27 rooms on a free-form outline.

| Level | Map entities (of 1,536) | Brushes (of 8,192) |
|---|---|---|
| 1 | 117 to 143 | 689 to 752 |
| 2 | 95 to 97 | 1,175 to 1,249 |
| 3 | 167 to 171 | 1,603 to 1,757 |
| 4 | 163 to 166 | 1,597 to 1,635 |

**Brightness** (`make descent-brightness`, `checks.lighting.mean`, three
seeds, level 1 scaled to 100):

| Level | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| Mean (seeds 1, 2, 3) | 0.2417 (0.228, 0.253, 0.245) | 0.1876 (0.180, 0.189, 0.194) | 0.1511 (0.155, 0.144, 0.155) | 0.1349 (0.137, 0.133, 0.134) |
| Scaled | 100 | 77.6 | 62.5 | 55.8 |
| Target (section 6) | 100 | 85 | 70 | 60 |
| Most clipped | 0.0136 | 0.0175 | 0.0177 | 0.0012 |

Levels 2 to 4 sit 4 to 8 points under their targets, each step down at least
6 points: pushing levels 2 and 3 higher put their clipping over 0.02 (floods
over low drifts, lanterns by walls). Level 3's map entities fell from 315 to
329 a level to 167 to 171 when its lanterns went from a ring round every room
to pools.

Level 1's way in is the level's brightest view on all three seeds: the
spawn view from the cage, 0.173 (the preview's per-view mean), against 0.146
for the brightest other room; the shaft's sun does not reach the cage, its
work floods do.

**Reach.** `make descent-reach` reads every island (`tools/descent-islands`):
a Heavy reaches every room's floor on all twelve levels, with no trap and no
stuck spawn; the islands are the jump ledges (6 to 84 a level).

**The real catalogue.** 200 seeds a depth at every normal depth: no
generator error, no jump fallback, no hallway dead end, no dead-end
fallback, no capped or wasted door, the room budget exact, and every family
the mix counts a fewest of present.

### 1.2 Defaults I chose (overrule by editing this table)

| # | Gap | Default |
|---|---|---|
| E1 | Cell size and door kit | Normal levels: 1024-unit cell, door 256 wide by 192 high, wall depth 32. Boss levels use a larger cell (section 3.2). |
| E2 | Libraries | One room library per depth, fifteen in all, each made by the shared shape generators with that depth's theme. The host links one library per level today, and a library carries the level's fog and tone map, which is what makes the darkness a property of the depth. |
| E3 | Layout | The Descent rules module lays levels out (`TF2DescentRules.GenerateLayout`), not the tools' own generator, because boss placement, the low-room rule and the per-depth sizes are game rules. |
| E4 | R19's 60 % jump-room share | Held per act (levels 1-5, 6-10, 11-15), not per level, so level 1 can be hallways and level 9 can be low crypts. |
| E5 | Ladders | Not climbable. Stock TF2 has no ladder movement, so ladders are dressing and every descent is a ramp, stair, drop or lift. |
| E6 | Brightness target | The mean lightmap brightness of a level, as `ssmap preview` reports it, falls strictly from each level to the next (section 6). |
| E8 | Doors in the layout | The host's `RoomInfo` carries no doors, so a room's doors, height class and jump tiers ride in its name, `<family>_<variant>-<doors>-<height>[-<flags>]` (`gallery_b-nesw-h-j`), by one codec in the rules module (`TF2.Descent.Rules.Layout.RoomName`). The host contract change (G1) stays the cleaner follow-up. |
| E9 | Jump-spot minimums | Two Scout spots and two blast spots on a normal level, except level 1 (one Scout spot, no blast spot) and level 9 (one of each), so level 1 is hallways and level 9 low crypts. |
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

As built (`RoomKit.Normal`, `RoomKit.For`; M22, M23). The kit is cut to its
door frame: the opening is the frame's outer width and its height rounded
down to the navigation voxel, and the wall is half the frame's depth, so
each room's frame lines exactly its half of a joined doorway.

| Setting | Value | Why |
|---|---|---|
| `cell_size` | 1024 | R19's room: a rocket jump needs about 512 of clear air and a 768 run. |
| Door opening | 208 by 192 | TF2's `props_farm/doorframe002d`, the only framed doorway found that leaves a giant (92 by 156, R16) room under a 256 drift. |
| `wall_depth` | 16 | Half the frame's 16-unit depth rounded up to the voxel; a wall of 8 packed a library into twice the space and time. |
| Navigation voxel | 16 (32 on the boss kits) | The tools allow 128 voxels along a cell. Every height and every marker height is a whole number of voxels. |
| Doorway floor | let down 2 under the plug, sill planks over it | `props_gameplay/door_grate003_floorplate` covers the joined doorway's floor, whose faces the room compile culls. |
| Rooms | any outline inside the cell (M22) | Only the doors are fixed; a room is the union of rectangles, cut on the diagonal or rounded, with necks to its doors. |

Room heights come in four classes:

| Class | `room_height` | Used for |
|---|---|---|
| Low | 192 to 256 | Drifts, crawls, crypt passages. No jumping. |
| Chamber | 512 | Ordinary rooms. Scout tiers fit; a rocket jump touches the roof. |
| Hall | 1024 | The standard jump room: rocket tiers B1 and B2. |
| Shaft | 1536 to 2048 | Ore passes, caverns, the lift shaft. The sticky tier B3. |

### 3.2 Boss levels

A boss arena must be one room, because rooms never open into each other. So
each boss level has its own kit with a larger cell: 2048 for level 5, 3072
for level 15, and 4096 for level 10, whose Tank needs a long track (M14). The few other rooms of a boss level are generated on
that same kit by the same shape generators, so they are larger and grander
than a normal level's. Section 7 has the arenas.

Open areas wider than one cell need multi-cell rooms, which the map tools
are adding with fixed door slots (M24). The Descent side adopts them after
Act I: footprints in the layout, 2x2 and 3x3 caverns and arenas, and the
boss levels moved off their oversized kits.

## 4. Jump space

### 4.1 Tiers

Measured in Phase 0 (section 1.3). A ledge or gap sits inside what its class
clears with a margin, and above what the class below it can reach. The room
generators take these from one table.

| Tier | Who reaches it | Ledge height | Gap | Limit it is set against |
|---|---|---|---|---|
| T0 | Everyone, walking | up to 18 (a step) | none | |
| T1 | Everyone, crouch-jump | up to 64 | up to 160 | A crouch-jump reaches 72; a Heavy's best gap is 210. |
| S2 | Scout, double jump | 88 to 104 | 400 to 512 | Others stop at 72. The Scout reaches 97 plain, 119 crouched, and 604 across. |
| S3 | Scout, triple jump (Atomizer or an `air_dash_count` item) | 136 to 144 | 704 to 768 | A double jump stops at 119 and 636. A triple reaches 144 plain, 166 crouched, 878 across. |
| B1 | Soldier, a rocket jump without crouching; any sticky | 192 to 256 | up to 448 | Above the triple jump's 166; an uncrouched rocket jump peaks near 308. |
| B2 | Soldier, a crouched rocket jump | 320 to 448 | up to 640 | An uncrouched one stops at 309; a crouched one reaches 509. |
| B3 | Demoman, a sticky jump | 640 to 768 | up to 1024 | A rocket's best case is 568; a sticky set off late still peaks at 780. |

A ledge at height h needs clear air to h + 114 (the hull plus a margin). A
rocket-jump room needs 576 of clear air over half its floor (the crouched
jump's 509 plus the ducked hull), which replaces R19's placeholder of 512,
and a straight run of 768. A sticky spot needs 1,104 of clear air, so it
lives in shaft rooms only.

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
| Mezzanine | S2 | A deck at 96 along one wall; stairs at the far end for everyone else. |
| Stepping pillars | S2, S3 | Pillar tops at 96 and 140 across a pit; the floor route goes round. |
| Chasm leap | S2, S3 | A gap of 448 or 736 with a bridge further along. |
| Rocket shelf | B1 | A shelf at 224 with a container marker; no other way up. |
| Catwalk ring | B2 | A catwalk at 384 round a hall, reached by a lift or stair two rooms on, or directly by a jump. |
| Chimney | B2, B3 | A shaft room with ledges at 384 and 704. |
| Roof gallery | B3 | A ledge at 768 in a shaft room, for stickies. |

## 5. Room shapes

One generator per shape family, each its own file in `src/mapgen/Descent/Shapes/`
(`RoomCatalogue.Mine`, `RoomCatalogue.Boss`). Each takes the kit, a seed and a
theme and writes several variants that differ in outline as well as in doors
(M22: a fact refuses two variants of one outline). A depth's library holds
every family whose depth range holds the depth: 65 rooms from 19 families at
levels 1 to 4. A room's doors are the walls it opens on; every chamber, hall
and shaft family has a one-door variant, so a tunnel can end in it (M18, M20).
`RoomFamilies` (rules) is the one list of family names and tags that the
depth table, the layout's test catalogue and these generators are held to.

**Connectors (low, 256), tagged `corridor` or `junction`:**

| Shape | Variants and doors | As built |
|---|---|---|
| Drift | 3, opposite | A plain bore 256 to 320 wide; one with a loading bay; one swelling into a cut chamber with corner posts. |
| Bend | 3, adjacent | |
| Tee | 3, three doors | |
| Cross | 3, four doors | |
| Dogleg | 3, opposite | Breaks the sight line. |
| Stub | 3, one door | Tagged `shrine`, each ending at a shrine marker (M18: a dead end worth the walk). |
| Wide drift | 3, opposite | 448 to 512 wide, a centre row of posts between two tracks; a loading bay; a raised walk. |

**Chambers (512):**

| Shape | Variants and doors | As built |
|---|---|---|
| Pillar room | 5: one to four doors | Round pillars, square piers, an L round three pillars, an octagon with a ring, a cross. |
| Split level | 4: one to three | A mezzanine (S2) with stairs. |
| Pit room | 4: one to four | Terraces 128 up round a pit, stairs down, stepping pillars (S2). |
| Store | 3: one or two | A crate maze, crate steps, a deck. |
| Yard | 4: one to four | Rails on the floor, carts. |
| Pool | 4: one to four | From level 8 (§7): water in a basin walled by walkways, stepping pillars. |

**Halls and shafts (1024 and up):**

| Shape | Variants and doors | As built |
|---|---|---|
| Stope | 4: one to three | Terraces at 128, 256 and 384 up one side; a rocket shelf (B1). |
| Gallery | 4: one to four | A catwalk ring (B2) and a rocket shelf. |
| Chasm | 4: one or two (one at a corner) | A gap across the room; a bridge and a chasm leap (S2, S3). |
| Chimney | 3: one or two, 1536 to 1792 | Ledges up the walls (B2, B3). |
| Cavern | 4: one to four, one a shaft | From level 6: rounded corners, rock walls that bulge up to 96 over head height, a roof hanging in lumps, step-high mounds, stalactites, a high alcove (B2) or spires (B2, B3). |

**Low combat rooms (256):**

| Shape | Variants and doors | As built |
|---|---|---|
| Crypt | 4: one, two opposite, two at a corner, four | From level 6 (levels 9 and 14 ask for them): a niched passage, an L, a cross-vault with tombs, an ossuary with a shrine on a dais. Not a hallway. |

**Role rooms:**

| Shape | Role | As built |
|---|---|---|
| Arrival | `room_role up`, tag `start` | Level 1: the foot of the town's 192 shaft, open to the sky, the cage at its foot. Levels 2 to 5: a cage station. 7 to 10: a rubble ramp. 12 to 15: under a chute. One door, two opposite, two at a corner; the party faces the first door. |
| Exit | `room_role down`, tag `stairs` | Levels 1 to 4: a cage; 5: the collapsed floor; 6 to 9: a ramp; 11 to 14: a chute. |
| Treasure | tag `treasure` | 3 one-door vaults: a cut vault, a round vault behind a turn, a dais up crate steps. |
| Camp | `room_role up` | Levels 6 and 11 only (M10). |
| Antechamber | none | Boss levels: the room before the arena. |
| Arena | `boss_arena` | One per boss kit (§7); level 15's has one door, since it is the bottom. |

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
| Rooms, outline | 18, grown free-form inside 15 by 15 cells (M19) | 21, free-form | 27, free-form | 27, free-form |
| Shapes | Drifts, bends, tees; two chambers; one store | Wide drifts, yards, a first stope | Stopes, split levels, pit rooms | Galleries, a chimney, stores |
| Height | Low and chamber | Chamber, one hall | Hall | Hall, one shaft |
| Jumps | Crate steps, one mezzanine | Carts as steps, a rocket shelf | Terraces, stepping pillars | Catwalk ring, chimney |
| Walls (as built, `Themes/ActOne.cs`, `palettes/dNN.tsv`) | `concrete/computerwall003c` (a red painted band low), cream `metal/wall029` decks | `metal/wall016d` over `nature/rockwall013` below 192, `metal/ibeam001b` sets | `nature/rockwall012` under `wood/wood_wall006` lagging, `wood/wood_beam03` sets and posts | `metal/wall021` over machine-green `metal/metalpanel01` below 192, `metal/imetal002` decks, `props/hazardstrip001a` bands |
| Floor | `concrete/concretefloor002b` | `nature/gravelground001` with track on every door's axis | `coalmines/dirtground004` | `nature/gravelcoal001` (black coal) |
| Props | lockers, switchgear, signs, powder crates, cement sacks, cable drums, white pipes | sheets, hand trucks, rope drums, signs, cable conduits, ore carts on sidings, service pipes | lagging, barrels, crates, woodpiles | welders, switchgear, hazard drums, ducts, generators in large rooms only, triple pipes |
| Lamps | `props_mining/industrial_ceiling_lamp` every 256, bulkheads up tall walls; work floods at the shaft foot | `props_lights/flood_light_left001` as spots, side bays dark | `props_2fort/lantern001` in 3 to 5 pools a room, hung from sets or on crates, hung on walls where dark | `props_gameplay/security_fence_light01` sodium, one cold white flood per large room |
| Light | 100: warm white, daylight down the shaft | 85: white with shadowed side bays | 70: lantern pools | 60: sodium yellow, dusty fog |
| Way down | Cage | Cage | Cage | Cage |

**Level 5, the Powder Magazine. Boss: Sir Nukesalot.** Kit cell 2048.

- The level: the lift's last station, an antechamber, the arena. A fixed row
  of three or four rooms; the antechamber is drawn from several variants and
  the arena's dressing and cover are seeded.
- The arena: a round blasted chamber about 1900 across and 1024 high, the
  mine's explosives store. Stone pillars for cover from his cannonballs. A
  ring ledge at 96 (Scout) and shelves at 224 (blast jump) give ways out of
  his reach. Powder barrels on barrel markers.
- Light 52: red warning lamps, one white work light over the centre.
- The way down: his death blast collapses the floor of the lift shaft. The
  hole is the exit, and the reason the lift goes no further.

### Act II: the breakthrough (6 to 10). No lift; ramps and collapsed floors.

| | 6. The Breakthrough | 7. Dripstone | 8. Sunken Galleries | 9. The Buried Chapel |
|---|---|---|---|---|
| Idea | The miners broke into a natural cave and left. Half timber, half rock. | Pure cave. Tall chambers, stalactites, a waterfall. | The flooded level. Planks over murky water. | The cave opens into crypts older than the town. |
| Rooms, outline | 27, free-form (M19) | 32, free-form | 37, free-form | 37, free-form |
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
  robots have bored up into. Catwalks at 384 over the track let blast
  jumpers bomb the Tank from above; rock steps at 96 and 140 serve the
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
| Rooms, outline | 37, free-form (M19) | 44, free-form | 51, free-form | 51, free-form |
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
  melee boss, so height is safety: pillar tops at 96 and 140 for the Scout,
  ledges at 224 and 384 for blast jumpers. Sections of the platform are
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
| A2 | (First pass done, 064d762ff.) A room-library writer: cells with gaps, `info_room` and its keys, door plugs, `room_height`, `room_role`, water sockets, the library's sun, fog and tone map in the gaps, the skybox room | `Vmf.cs` writes boxes, panes and entities only |
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
| A15 | A door frame in every socket (socket furniture), so a joined doorway has sides, a sill and a head | New; found in Phase 0 |

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

### Status of A, B and C (2026-11-10, after Act I's integration)

What the branch `descent-mine-plan` holds against each task, checked by its
facts and the three gates. "Not done" is what is left.

| # | Done | Not done |
|---|---|---|
| A1 | `MineShaft`: shaft, cage, lining, lantern, sun and sky, used by the town and the level 1 way in. | |
| A2 | `RoomLibrary`: cells with gaps, `info_room`, plugs, `room_height`, roles, water, the library's sun, fog and tone map. | The tone map controller is written as key values, which the game's controller ignores (it takes inputs), and the maps are lit LDR, where it does nothing: inert until an HDR compile and a `logic_auto` that fires its inputs. |
| A3 | Displacement floors, walls and (new) roofs (`Brush.DisplacedBox`, `DisplacedWall`, `DisplacedUnder`). | |
| A4 | Wedges, stairs, prisms, ring walls, convex columns, chamfers and rounded corners. | |
| A5 | `JumpTable`, read by every jump feature. | |
| A6 | Every family of section 5, with a one-door variant of every chamber, hall and shaft; the crypt (new). | Act II and III dressing of them. |
| A7 | Ways in and down by act, the two camps, antechambers. | |
| A8 | Placeholder arenas on the boss kits (level 15's of one door). | The Phase 6 arenas, the Bore's track, the breakaway platform. |
| A9 | Act I's four themes (`Themes/ActOne.cs`, `palettes/d01-d04.tsv`). | Depths 5 to 15 are the grey. |
| A10 | The dressing pass: lamps by the route and a cover pass by the lint's own `lit` rule, pools hung from sets or on crates, work lamps, track on the doors' axes with sidings, sets, pilasters, trim bands and a lower wall band, clutter in groups, machines sized to the room, nothing cutting floor off. | Handrails on ledges and decks (they stand in the air a ledge's jump needs, which the dressing keeps clear); a door frame per theme (no TF2 frame of a 16 depth fits the kit but the farm frame; a surround is the way). |
| A11 | `info_poi` markers of every kind, map labels and markers. | |
| A12 | `library.json` with tags, weights, depths, jump, low, markers, doors, height, focus and a preview camera. | |
| A13 | `make descent-rooms` and `make descent-level`. | |
| A14 | Level 1's sky shaft under the town's sun and sky; work floods at its foot. | The sun still cannot reach the cage down 192 of shaft. |
| A15 | Door frames as static props lining each half of a joined doorway, sill planks, cap panels (M23). | |
| B1 | Lint facts per rule, plus a giant's reach of every pack spawn. | |
| B2 | `DescentPaletteTests` against the database at `~/assetdb`. | |
| B3 | `DescentMineShaftTests` holds the town to `MineShaft`; level 1's way in is built from the same class. | No fact reads level 1's library for the same values. |
| B4 | `make descent-brightness`. | Depths 5 to 15 (still grey). |
| B5 | `make descent-reach`, reading every island (`tools/descent-islands`) and accepting a trap only inside the exit's volume. | The per-class report (Scout, Soldier, Demoman floor) of Phase 7. |
| B6 | `make descent-budget`, with the fog and tone map controllers checked against the library. | |
| C1 | `LevelLayout`: free-form growth to the room budget (M19), no hallway dead ends (M18), a room at every tunnel's end and no capped door (M20), the adaptive mix (M21). | |
| C2 | `DepthTable`, with the mix of section 7 per depth. | |
| C3 | The boss levels' rows. | |
| C4 | 10,000 seeds a depth on the synthetic catalogue; 200 seeds a depth on the real one with no shortfall of any kind. | |
| C5 | `TownDescents`. | The host and picker wiring (G6, E1). |

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
| H3 | A full HL2 import into the asset database | TF2 and SDK Base only; needs HL2 installed |
| H4 | A door that stands on the floor at any height, as an option of the kit | A local two-line change in our clone (section 1.3) |
| H5 | The Scout's air dash at stock's speed in the simulator | 15 % too high |
| H6 | Faces for a joined doorway's sides, sill and head, or socket furniture kept on both sides | Each joined door costs two `func_brush` entities today |

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
