# What a linked Descent level ships (WO1, Part 1)

Measured 2026-11-11 at `28e09a10d` (branch `descent-wo1`), map tools at
`maptools.lock` `3551fa8cf557` (SourceSharp.MapFormats 1.0.7), debug build.
Three levels built in this tree: `make descent-rooms DEPTH=1`, `make
descent-rooms DEPTH=2`, then `make descent-level DEPTH=1 SEED=1`, `DEPTH=1
SEED=2`, `DEPTH=2 SEED=1`. Each level's `.nav3d`, `.map2d` and `.bsp` was
dumped with a throwaway reader over the packaged MapFormats 1.0.7
(`.scratch/wo1-dump/Program.cs`, not committed; its output is quoted below).
Map-tools paths are in `../source-sdk-map-tools` at the lock.

This answers plan_descent_logic section 7, Y1 and Y2, from the files
themselves: everything WO1 needs ships, with one binding defect (item 7).

## 1. Files beside the `.bsp`

| File | Where `make descent-level` writes it | Reaches the game's `pool/maps` | Source |
|---|---|---|---|
| `<map>.bsp` | `dNN/maps/` | yes | `Makefile:794` `descent-install` links bsp, nav3d, map2d |
| `<map>.nav3d` | `dNN/maps/` | yes | same; the host ships the same three (`LocalInstanceHost.LevelExtensions`, host `src/Host.Instances/LocalInstanceHost.cs:42`) |
| `<map>.map2d` | `dNN/maps/` | yes | same |
| `<map>.yaml` (the level file) | `dNN/` | no | `Makefile:765`; the host's admin offers it for download only (`src/Host.Admin/AdminHost.cs:150`) |
| `<map>.cameras` | `dNN/` | no | `src/mapgen/Descent/DescentLevel.cs:351`, preview only |
| `library.json` | `dNN/` | no | the library's manifest; no copy at run time on the game server |

Sizes (bytes, `ls -la`): d1 s1 nav3d 94,022, map2d 15,584, bsp 4,237,248; d1
s2 118,859 / 15,760 / 4,376,948; d2 s1 455,525 / 35,388 / 15,945,004.

## 2. Markers

All in the `.nav3d`, as points of interest (`Nav3dPoi`, map tools
`src/SourceSharp.MapFormats/Nav/Nav3dLevel.cs:116`): position, yaw, radius,
`Type` (the `poi_type`), name, **`Cell`** (the cell of the room it belongs
to), flags and the room's transition role. Read through `Nav3dReader.Open`
then `Poi(i)` / `PoiCount` (present in the packaged 1.0.7: the server project
compiles against it). They are grouped by room through `Cell`, not only
positions. Counts:

| Level | POIs | `descent_spawn_pack` | barrel | container | dispenser | shrine | trap | `spawn` | `arrival` | `door` |
|---|---|---|---|---|---|---|---|---|---|---|
| d1 s1 | 89 | 26 | 8 | 4 | 1 | 2 | 2 | 8 | 2 | 36 |
| d1 s2 | 97 | 30 | 10 | 4 | 0 | 4 | 1 | 8 | 2 | 38 |
| d2 s1 | 128 | 43 | 10 | 9 | 2 | 1 | 4 | 8 | 2 | 49 |

No `descent_map_marker` occurs on these levels. The `.map2d`'s markers
(`Map2dMarker`, `Map2dLevel.cs:87`) are not every POI: only an author's
`info_poi` with a `map_marker` (`RoomMapView.MarkersOf`, `RoomMapView.cs:234`)
plus the linker's own `spawn`, `arrival`, `exit_up` and `exit_down`
(`LevelMapBuilder.cs:301-318`): 5 on each level. A map marker's `map_label`
is on the `.map2d` only.

## 3. Rooms

- **`.map2d`** (`Map2dRoom`, `Map2dLevel.cs:24`): one placement per room in
  link order, with its cell, footprint (`CellsX`, `CellsY`, turned), quarter
  turns, height in units, name and `map_label` ("Way in", "Way down",
  "Treasure", ...). Outlines are rings per placement (`Map2dRing`, line 48),
  each a floor band `ZLow..ZHigh` (several per room: decks, stairs; 301 rings
  on d1 s1, 669 on d2 s1). No storeys as such: one ring per floor height.
  Placement index = room id.
- **`.nav3d`** (`Nav3dCell`, `Nav3dLevel.cs:94`): per grid cell the placed
  room's name, turn, transition role (`Up`, `Down`, `None`) and joined/capped
  door bits; a larger room's every cell names it (d2 s1's
  `hall_a-nesw-h-2x2-j.s2.b1` is in cells 27, 28, 35, 36). Cell heights in
  voxels. No regions.
- **Tags** (`start`, `stairs`, `boss_arena`, `treasure`, `low`, `jump`) are in
  `library.json` only. The room name carries the codec's facts (doors, height
  class `l`/`c`/`h`/`s`, footprint, `j` and tier flags; E8), and the family
  carries the role rooms (`arrival`, `exit`, `camp`, `treasure`,
  `antechamber`, `arena`), so role, low and jump are readable from what ships.
- **Re-running the layout**: the server cannot (no `library.json` at run time,
  and a hosted level's layout seed is not given to it: item 6). It does not
  need to: the rooms ship. The facts re-run it from the committed manifest and
  the plan matches the shipped placements exactly (name, cell, turn).
- The generator's own count is the level file's comment: `# rooms 18, joints
  18` (d1 s1), `# rooms 18, joints 19` (d1 s2), `# rooms 21, joints 22` (d2
  s1). The `.map2d` holds 18, 18 and 21 placements.

## 4. Doors

Shipped, both files:

- `.map2d` `Map2dDoor` (`Map2dLevel.cs:76`): per placement per socket, its
  name, `Open`, the `Neighbour` placement, the opening as a segment on the
  cell face (`X0,Y0`-`X1,Y1`) and its band `ZLow..ZHigh`. Every joined doorway
  appears twice (once per side). d1 s1: 36 records, all open (18 joints); d2
  s1: 49 records, 44 open (22 joints) and 5 closed sockets of the 2x2 hall.
  Every opening measured 208 wide, `z 16..208` (192 high).
- `.nav3d` `Nav3dDoor` (`Nav3dLevel.cs:102`): per socket its cell, world
  direction, joined flag, name and the facing socket's index; plus a `door`
  POI at each socket's centre (flags `Door`, `Joined`), yaw facing out.
- Normals are not stored; they follow from the segment (perpendicular, on the
  floor) and the two rooms. Widths need not be derived from the kit.

## 5. Arrival, spawn points, exit, boss arena

- Each transition room carries four `spawn` POIs and one `arrival` POI
  (d1 s1: up room cell 34, POIs 66-69 at (2416|2512, 4544|4672, 16) yaw 0,
  arrival POI 70 at (2336 4608 16) yaw 294; down room cell 4, POIs 8-12).
  `Nav3dLevel.SpawnPoi` = `UpArrivalPoi` = 70, `DownArrivalPoi` = 12. So a
  party of four has four distinct shipped points in the up room.
- The `.bsp` (with `-mod-entities`) holds two `logic_level_transition`
  entities and no `info_player_start`, `info_player_deathmatch` or
  `info_player_teamspawn` (string count over the entity lump). Nothing in the
  map places a player: before WO1 the hero spawned at the world origin.
- The exit: the `.map2d`'s `exit_down` marker, the centre of the way down's
  volume, labelled with the map it leads to (d1 s1: (4772 144 56) in
  placement 2, `tf2_descent-2`); `exit_up` likewise for the way back
  (`tf2_descent_town` on depth 1).
- Boss arena: a room of family `arena` (`boss_arena` tag in the library);
  not on these three levels (depths 5, 10, 15 only; not built here).
- The town (`src/mapgen/TF2DescentTown.cs:315-334`) has 16 unnamed
  `info_player_deathmatch` in the square and 8 named `tf2_descent_arrival`
  round the shaft. Before WO1 the mode placed nothing: TF2's team walk finds
  no `info_player_teamspawn` and the core's walk picks any of the 24 at random
  (`src/server/managed/SpawnPoints.cs:193`).

## 6. Seed and depth

- Unhosted: the map name, `tf2_descent-<d>-s<seed>` (`Makefile` `DESCENT_LEVEL`);
  depth by `LevelTransition.DepthOf` (`src/tf2_descent/server/LevelTransition.cs:49`).
- Hosted: the pool instance is `tf2_descent-<d>-<hash>`, so the name gives the
  depth only. Booting answers `BootInfo(Module, InstanceSeed, InstanceKind,
  Depth, LevelHash, Uploaded, LevelId)` (host `src/Host.Sdk/Models.cs:31`).
  `InstanceSeed` is the instance's seed, the one `KillRoll` replays from
  (`InstanceApi.cs:131`); the layout's seed is the link attempt's
  (`MapPoolWorker.SeedFor`, `MapPoolWorker.cs:218`) and is stored in the host's
  level record, not given to the game.

## 7. A defect found: every shipped `.map2d` reads as stale

The `.map2d` binds to its map by `MapChecksum` (`Map2dLevel.cs:119`), the
CRC of every lump but the entities (`BspMapChecksum`), and
`Map2dReader.Read(file, checksum)` (`Map2dReader.cs:309`) refuses a mismatch
as stale. `make descent-level` runs `tools/descent-face-sides` on the `.bsp`
after the link (`Makefile:770`), which rewrites face flags, so the shipped map's
checksum never matches: d1 s1 map `9f131970`, `.map2d` `df94918d`; d1 s2
`f8e4c611` / `6fe2edfa`; d2 s1 `46d2ed75` / `d55dbd82`. A relink of d1 s1 into
`.scratch/wo1-relink` without the face fix checksums `df94918d`, equal to
the `.map2d`'s, and writes byte-identical `.nav3d` and `.map2d` (`cmp`). The
`.nav3d` binds by `LevelId`, which the map's worldspawn `ss_level_id` matches
(`6a2187b1-...` on d1 s1), so it is unaffected. WO1 reads the `.map2d`
without the checksum and holds it to the `.nav3d` instead (same grid, the same
room in every placement's cell).

## What the server can know today, and from where

| Piece | Source at run time | Status |
|---|---|---|
| Rooms (id, name, cells, footprint, turn, height, label) | `.map2d` placements | shipped |
| Room bounds | `.map2d` cells x 1024 from the `.nav3d` origin, z to the room's height | shipped (the cells), bounds by rule |
| Room role (arrival, exit) | `.nav3d` cell role `Up` / `Down` | shipped |
| Room role (treasure, antechamber, arena), low, jump | the room name (codec E8) | shipped in the name |
| Room tags (`start`, `stairs`, ...) | `library.json` | not at run time (not needed: the name and role carry them) |
| Room outlines | `.map2d` rings | shipped (not read by WO1; UI5's) |
| Doors (pair, centre, normal, width, height) | `.map2d` open doors; `.nav3d` door records and door POIs | shipped; normal by rule from the segment |
| Neighbours | the doors | shipped |
| Markers (kind, position, yaw, room) | `.nav3d` POIs and their `Cell` | shipped |
| Map marker labels | `.map2d` markers | shipped |
| Arrival points for four | `.nav3d` `spawn` POIs of the up room, then its `arrival` POI | shipped (4 + 1) |
| Exit | `.map2d` `exit_down` marker; `.nav3d` down room | shipped |
| Boss arena | the room of family `arena` | shipped in the name (not built here) |
| Depth | map name; Booting | shipped |
| Seed | map name `-s<seed>` unhosted; `BootInfo.InstanceSeed` hosted | shipped (hosted: after Booting answers) |
| Layout seed (hosted) | host level record only | absent (not needed) |
| Town arrival spawns | the town map's `tf2_descent_arrival` entities | shipped |

## What is missing and who would have to ship it

- Map session: `tools/descent-face-sides` (H7's workaround) changes the map's checksum after the link, so every shipped `.map2d` fails `Map2dReader.Read(file, checksum)` as stale; rewrite the `.map2d`'s `MapChecksum` after the face fix, or move the fix into the linker (H7) so the link's own checksum is the shipped one.
- Map session (Y2, partly answered): door props and plugs are not in the navigation (plan 3.11 row 8); the doorway itself (position, width, height, joined pair) ships in both files, so nothing more is needed for WO1.
- Host: a hosted level's layout seed is not given to the game (only `InstanceSeed`); not needed by WO1, recorded in case a later package wants to re-run the layout.
- Host: a player arriving at the hub has no "came up from depth N" fact, so the hub cannot tell a return from a fresh join; WO1 uses `tf2_descent_arrival` only after an in-process hop from a level (D2).
