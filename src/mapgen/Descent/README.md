# Descent rooms: writing a shape family

The mine levels of TF2 Descent are linked by map-tools from room libraries
that this folder writes (`docs/plans/tf2_descent/plan_descent_mine_levels.md`,
sections 3 to 5 and 10). A **shape family** is one static method that
returns every variant of one shape:

```csharp
public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
```

and it joins the catalogue as a `RoomShape("family", MyShape.Variants)` in
`RoomCatalogue` (`Mine` for a normal depth, `Boss` for a boss level's row).
Each family of plan §5 is its own file in `Shapes/`: the connectors
(`drift`, `bend`, `tee`, `cross`, `dogleg`, `stub`, `widedrift`), the chambers
(`pillar`, `split`, `pit`, `store`, `yard`, `pool`), the halls and shafts
(`stope`, `gallery`, `chasm`, `chimney`, `cavern`), the low crypts of Act II
and below (`crypt`), and the role rooms
(`arrival`, `exit`, `camp`, `treasure`, `antechamber`, `arena`). `Parts.cs`
holds what they share (door necks, lamps by height class, diagonal cuts,
rounded corners, pillars, stairs) and `Stations.cs` the ways in and down (a
lift cage in an alcove, a raised floor broken by a hole). `DriftShape` and
`GalleryShape` are short worked examples; copy one.

## Rules a family keeps

- **Name every room `<family>_<variant>`** (`gallery_b`), the variant lower-case
  letters and digits. The catalogue refuses anything else, and the names must
  be stable for a seed. It then renames the room by the rules module's codec
  (`TF2.Descent.Rules.Layout.RoomName`, plan E8) from the room's own facts:
  `gallery_b-ew-h-j.b1.b2` is its doors, its height class (`room_height` up to
  256 low, 512 chamber, 1024 hall, above that shaft), `j` when its entry says
  jump, and a flag per tier whose feature (below) it holds
  (`LayoutNames`). Never type the suffix.
- **Vary only through `RoomSeed`** (`new RoomSeed(seed, "family")`: `Int`,
  `Fraction`, `Chance`, `Pick`). Never `System.Random`, never the clock. The
  same seed writes the same bytes; a fact checks it.
- **Never write a height or gap as a literal** for a jump: take it from
  `JumpTable.Of(JumpTier.X).Ledge(t)` / `.Gap(t)`, or use a feature below.
- **Keep the doors clear.** Nothing but the shell may stand in the 96 units in
  front of a door (`RoomBuilder.ApproachBox`); `Build` refuses it.
- **The critical path is T0 and T1** (M8): a Heavy must walk from every door
  to every other. Jump spots are extras.
- **A giant's box crosses the floor flat** (M8, brief p7): the level's
  navigation keeps no solid's top, so anything solid above the floor, a rail
  two units high included, is a wall to a box wider than the gap beside it.
  A 96 by 160 box on the floor must walk from every door to every other
  (`Dressing.FlatHullWalk`, `DescentGiantNavTests`). Draw low rails,
  sleepers and planks with `WithStrips` (one non-solid `func_brush`, which
  still casts shadows and which dressing keeps out of), and keep posts and
  pillars a box's width apart from each other and from stairs. Rooms whose
  way between doors climbs stairs (the pools) wait on the navigation
  raising a hull up a flight (docs/todo.md).
- **Every jump spot pays** (§4.2): a ledge only a jump above T1 reaches
  carries a reward marker (container, shrine, dispenser), or has a second way
  up (stairs, a ramp, crate steps). The features below do this for you.
- **Lint it**: `RoomLint.Check(room)` must return nothing (see Testing).

## Outline: a room need not fill its cell

Only the doors are fixed (owner ruling 2026-11-10). `WithOutline(rects...)`
makes the room's open floor the union of rectangles inside the cell's walls;
the rest of the cell is wall, cut into as few boxes as `Rects.Subtract` gives.
Every door's approach must be open floor, and a door's neck (`Parts.Neck`)
joins it to the room's body. Bend the outline off the square with
`Parts.Chamfer`/`Chamfers` (a diagonal cut), `RoundCorner`/`RoundCorners` (a
fan of triangles), `Solid` (any convex column) and `Brush.Column`.
`WithBody(rect)` names the main space whose walls the jump features stand
against (the interior square by default), and `WithFocus` where a preview's
camera looks (the body's middle by default). A family's variants differ in
outline as well as in doors: a fact refuses two variants with one outline.

Also on the builder: `WithWater(area, z0, z1, material)` (a water volume,
kept in `Room.Water`, not solid to the lint), `WithHazard(kind, mins, maxs)`
(a hazard volume of a `hazards.json` kind: a `trigger_multiple` nothing fires,
named `tf2_descent_hazard_<kind>`, a global name the link keeps and the game's
`HazardService` finds; levels 1 to 11 hold none), `HazardFloor(area)` (the
theme's hazard floor there: Act III's slime standing as a liquid or lava laid
as a non-solid skin, with its volume; pools, flooded galleries and, at level
13, a chasm leap's gap take it), `WithSkyCeiling(area)` (the
ceiling slab is sky there and the library gets the town's sun), and for a
transition room `WithTransitionBox`, `WithButton` and `WithHallTrigger`, which
put its volume and what fires it where the station wants them.

## Building a room

```csharp
var b = new RoomBuilder("gallery_b", kit, theme)
    .WithHeight(1024)                      // room_height; any whole number of nav voxels >= 256
    .WithWidth(kit.Interior)               // the interior square; 256..384 makes a corridor
    .WithDoors(CellFace.West, CellFace.East)
    .WithEntry(new RoomEntry(["junction"], Weight: 2, MinDepth: 1, MaxDepth: 9, Jump: true));
```

- **The kit** (`RoomKit.Normal`: cell 1024, door 208 by 192, wall 16,
  navigation voxel 16) is shared by every room of a level. It is cut to its
  door frame model (`RoomKit.For`, `FrameModel`): the wall is one voxel, and
  each room's 16-deep frame fills its half of the doorway; the opening is
  the frame's outer width and its height under the head. Write every size from the kit, never as a number.
  Every depth builds on this kit (the boss arenas are rooms of several cells). Doors stand on
  the floor (z = the wall's depth, room-local) centred on their wall, which
  every `info_room` asks of the tools with `door_sill floor`; the
  shell, door plugs, jambs and lintels are written for you. Room-local
  coordinates: the cell is `[0, cell]` in x and y, `[0, height]` in z; east
  is +x, north is +y; the floor's top is `b.Floor`, the ceiling's underside
  `b.CeilingUnderside`, the interior square `b.InteriorLow` to
  `b.InteriorHigh`. Heights and marker heights are whole navigation voxels
  above the cell's floor: the tools stand a marker in the voxel its height
  falls in.
- **Height classes** (§3.1): low 192 to 256 (drifts, crawls), chamber 512,
  hall 1024 (the rocket room), shaft 1536 to 2048 (the sticky tier).
- **Door frames and caps** are automatic: every door gets the kit's frame
  model (or the theme's own frame of brushes, `RoomTheme.FrameBrushes`: one
  non-solid `func_brush` per socket kept only where it is joined, posts and
  a head lining the doorway with a flange, a cap or a stripe round it and,
  at level 2, planks across the threshold; Act II's add haunches in the top
  corners (`Fillet`, an arch), a keystone and plinths (level 9) or a head
  cracked and sagging (`Sag`, level 6's broken set); one map entity per joined socket)
  and sill planks (as many as cover the wall's depth) as non-solid
  `prop_static`s lining its half of the doorway, the floor let down 2 under
  the plug (`RoomBuilder.DoorwayDip`, short of the cell face) so the planks
  have the room's air to stand in and the doorway's floor is never the void, kept by the link only where the socket is joined (`room_needs
  joined_<wall>`), which cost no entity; and a wall panel `func_brush` 2 in
  front of the plug kept only where it is capped (a plug's face cannot be
  lit: `RoomBuilder.CapThickness`), one entity per capped socket. Neither
  casts shadows. The faces inside a doorway were in the plug when the room was
  lit, so a brush frame carries its theme's `_minlight` (`BrushFrame.MinLight`,
  about one and a half times its walls' light: Act I 0.35, levels 2 and 4 0.3,
  6 0.25, 7 0.11, 8 0.12, 9 0.06), and a slab over the let-down floor under the
  plug, in the post's surface, a unit proud of the sill (`BrushFrame.SillSlab`);
  a model frame's doorway takes the same slab as its own non-solid `func_brush`
  (`RoomBuilder.SillSlabPiece`, Act I's floor of light, one entity per joined
  socket). The dip and the sill plates on it drew as a black strip across every
  joined doorway; the farm frame model's head still draws dark at level 1 (a
  prop lit from inside the plug: map tools H9). `WithoutDoorFrames()` is for test fixtures only.
- **Brushes**: `WithBox(mins, maxs, surface?)` for a box in the theme's block
  surface, `WithBrush` / `WithBrushes` for anything from `Brush`:
  `Brush.Wedge` (a ramp rising toward a `Compass` heading), `Brush.Stairs`,
  `Brush.Prism` (a round pillar), `Brush.RingWall` (a round chamber wall,
  with segments left out for door arms), `Brush.DisplacedBox` /
  `Brush.DisplacedWall` (cave floors and walls, power 2 or 3; keep their
  edges out of the plug boxes). Every face is textured at its `Surface`'s
  scale on world axes from the cell's corner, so rooms line up.
- **Surfaces**: take them from the theme (`theme.Floor`, `Wall`, `Ceiling`,
  `Block`, `Frame`). `RoomTheme.Grey` is Phase 1's measuring set (a grid
  line every 64 units); `RoomTheme.Mine` the town's timber and rock.

## Rooms larger than one cell (M24, `Shapes/BigShapes.cs`, `Shapes/ArenaShape.cs`)

`WithFootprint(n)` makes a room `n` by `n` cells (2 to 4; map tools'
`room_footprint "n n"`, from 7cdfe4d): its box is `[0, n x cell]`, it has a
socket on every cell edge of its perimeter (`east0` to `east{n-1}` and so
on, counted from the room's south on east and west walls and its west on
north and south walls), each framed, sill-planked and capped as any door,
and its open floor is the whole interior (`Body`) unless it takes an outline (level 5's rooms, below). `WithSockets((face,
edge)...)` keeps only some of them (a boss arena opens where its row joins
it, which packs in a third of the time and space): the other edges are
plain wall, with no brush the shape of a door box (the tools count one as a
socket). The shell's walls run whole sides with an opening per socket, so
no brush ends on an internal cell face; **no face of a brush may lie on
one** (the tools refuse it, and `Build` throws first): put a pillar at 704,
not 1024, or let the brush cross the face; the dresser moves its own
brushes off one, and the dress lint refuses one there (`dress_cell_face`).
Its name carries the footprint, and chosen sockets as numbers after their
side's letter (`hall_a-nesw-h-2x2-j.b1`, `arena_b-e1w1-s-3x3-j.s2.b2`,
`TF2.Descent.Rules.Layout.RoomName`); the layout lays a hall or grotto off a
fill room, counts it once against the budget, lays a boss arena unturned in
its row, and the level file writes `+` in its other cells
(`DescentLevel.Write`). The room lint, the dresser and the dress lint read
the whole footprint and every socket (`Room.Sockets`, `PlugBox`,
`DoorInside`, `SocketName`). Families: `hall` (2x2, levels 2 to 4: a
loading dock, a plinth yard, a gallery over the doors), `grotto` (2x2 and
3x3 caverns, 6 on: rounded corners, rough rock along the walls between the
sockets, a roof hanging in lumps, stalactites), `flood` (2x2 flooded gallery, 8 on), `arena` (level
5's 2x2, 10's 3x3, 15's 4x4; below, Boss arenas).

## Boss arenas (`Shapes/ArenaShape.cs`, `Arena.cs`, brief p13)

Each arena lights itself (`WithOwnLamps`: the dresser keeps the shape's
fills, hangs each lamp it asks for with `WithLampSite`, a kind of its theme's
`ArenaLamps`, and none of the theme's own; the dress lint's `dress_lamp_site`
refuses a lamp asked for that did not fit), stands its theme's `SetPieces`
(`WithSetPiece`, placed first; a non-solid one, a hollow frame like the
robots' lift, is held to the cell and its support, not to the clearances) and
keeps its `KeepClear` boxes free of dressing (`dress_keep_clear`). It tells
the server's boss encounter (the game-logic session's contract) where the boss
stands and faces (`WithBossSpot`, also the arena's first pack marker, the
server's fallback), the Tank's track (`WithTrack`, `TrackPoint`: stations and
the final one) and the robots' mouths by leg (`WithMouth`): `library.json`'s
`arena` entry, which `descent-level` turns into the level's frame as
`<level>.arena` (`DescentLevel.ArenaFile`) and `tools/descent-room-light`
writes into the `.map2d` as the section `ARNA` (`tools/descent-arena`). The
brushes the encounter switches are `func_brush`es named by `BossNames`
(`WithNamedBrush`; the exit cover over a boss level's way down, the Bore's
pillars and their rubble, the Pit's platform sections) and staged hazard
volumes (`WithStagedHazard`). `TankTrack.Collisions` sweeps the MvM Tank's
hull along the track against everything solid at any time;
`tools/descent-breakables obstacles` and `reach` read a built level's
`.nav3d`: its named obstacles, and whether a hull walks to points with the
pillars falling one by one. The server's graph keeps one obstacle per nav
leaf, so no leaf may lie inside two switched brushes' boxes: each pillar's
rubble lies on one side of its box, 32 or more off it, its heaps
overlapping into one heap (brief p15, `DescentArenaNavTests` over the built
`.nav3d`). A pillar and the exit cover cast no shadow
(`WithNamedBrush(..., castsShadow: false)`): a casting pillar left a dark
disc on the floor when it fell, and a casting cover was lit a shade under
the floor round it. The Bore's cover and rubble are `Parts.Rock` lumps (an
uneven polygon drawn in toward a point off its middle), its track a spiral
winding in counter-clockwise, its lift turned to open on the first leg and
lit from before it. Dressing keeps `BossNames.PlatformEdge` off every
platform section that breaks away (the dresser refuses it; level 15's
torches stood at the core's edge and floated once a section dropped).
The Magazine (level 5) is narrowed to 2 by 2 cells of the 512 kit, 992
inside, at the centre of the maze (brief p18, above); it is 768 high, not 1,024 (brief p16): Sir Nukesalot,
at the largest scale the nav's hulls hold, read small in the taller hall,
and 768 still holds the rocket air (576) over the floor and 114 over its
ledges at 96 and 224 (`DescentBossArenaTests`). Its warm fill hangs
`MagazineFillUnderRoof` under the roof; the pilasters and the work light's
cord follow the roof by themselves.

## Level 5's maze (`MazeLevel.cs`, `Shapes/MazeShape.cs`, brief p18, M35, M36)

Level 5 is the one level on another kit: `RoomKit.MazeCell`, a 512 cell with the
normal door (208 by 192, framed). Its other rooms keep their size as rooms of 2
by 2 cells (`Parts.Seat`, `Parts.SpanCells`: the way in, the quiet lobby, the
antechambers and the way down, each socket on its wall's first cell edge; a
larger room may take an outline, `WithOutline`, like a room of one cell). The
level is laid by the map generator, not the rules module's boss row
(`DescentLevel.Lay` sends depth 5 to `MazeLevel.Plan`): the row's three rooms
west of a 10 by 10 block whose centre 2 by 2 is the arena, one room of the
family `arena` (doors north and south into the maze, east into the way down);
the way down 2 by 2 east of it, its one door into the arena; every other cell
of the block a maze room of its own, so the automap shows the maze cell by cell.
The maze is a seeded spanning tree over the cells (randomised Kruskal) with
`MazeLevel.Loops` loop doors between cells at least `LoopSpan` apart along the
tree; the antechamber enters it on a seeded row of its west side; leaves are its
dead ends (M35 lifts M18 here). A maze cell is one corridor junction,
`MazeShape.Width` (256) wide from its middle to each door, rock in its corners,
a solid roof at 384 (`Height` 400); its shape is its door mask's, so the family
builds every mask once a variant, unturned, in the connector family its doors
make (`stub`, `drift`, `bend`, `tee`, `cross`, variant `mazea`/`mazeb`): `a`
the bare tunnel, `b` a pack (a dead end's container) and a breakable in a niche
off a closed side (`WithBreakable`; the dresser adds none to a maze cell, so 92
cells share the level's 96). A maze cell lights itself: a dim warm fill, and a
red warning lamp at a dead end, a bend and a tee. Maze cells are tunnels to the jump
rules (`RoomLint.JumpSpaceExempt`: a corridor or junction is passed over by the
jump air rule).

## Jump features (`JumpFeatures`, plan §4.3)

Each is a builder step taking its tier from the table (`t` picks within it):

| Step | Tier | What |
|---|---|---|
| `CrateSteps(x, y, heading, steps)` | T1 | crates each a crouch-jump higher |
| `Mezzanine(wall, from, to, depth)` | S2 | a deck at 96 with stairs beyond its `to` end |
| `SteppingPillars(x, y, heading, S2 or S3, count)` | S2/S3 | round pillars a crouch-jump apart, a reward on the last |
| `ChasmLeap(x, y, heading, width, S2 or S3)` | S2/S3 | two decks the tier's gap apart, a bridge and stairs |
| `RocketShelf(wall, from, to)` | B1 | a shelf at 224 with a container |
| `CatwalkRing(depth)` | B2 | a catwalk at 384 round the four walls, with a container |
| `ChimneyLedges(lowWall, highWall)` | B2, B3 | ledges at 384 and 704 (shaft rooms) |
| `RoofGallery(wall, from, to)` | B3 | a ledge at 768 (shaft rooms) |

`from` and `to` run along the wall (y for east and west, x for north and
south). A feature refuses a ledge without 114 of clear air above it.

## Markers, lamps, labels

- `WithMarker(MarkerKind.PackSpawn, at, yaw, radius)`; also `Shrine`,
  `Container`, `Dispenser`, `Barrel`, `Trap`, `Spawn`. They write `info_poi`
  (`descent_spawn_pack`, `descent_mark_<kind>`), cost no entity, and go in
  the room's `library.json` entry. Stand them on the floor or a ledge
  (`at.Z` is the surface's height) with a standing player's room above.
- `WithMapMarker(at, icon, label)` and `WithMapLabel("Hall")` for the level
  map.
- **Breakables** (ruling M34, brief p12, `Breakables.cs`): crates and
  barrels a hit breaks belong to one manager entity per level on the
  server, which draws, collides and breaks them, so a room writes no prop
  for one. A theme's prop names the breakable it becomes
  (`PropModel.Breakable`: Act I's powder crate and oil drum, level 8's open
  drum); where the dresser stands one as clutter (a corner pile, a wall or
  floor piece, a companion; never a lantern's stand) it writes an
  `info_poi` `descent_mark_breakable_crate` or `_barrel` instead, its
  `origin` the server's model's foot (a crate's origin; the floor under a
  barrel's middle, the server lifting its model 30), `angles` `0 yaw 0`,
  fitted by the server's box (`Breakables.Crate`, `Breakables.Barrel`:
  `miningcrate001`, `wooden_barrel`). A room takes at most
  `Breakables.CapAt(depth)`: 8, or the level cap of 96 shared over the
  depth's exact room budget if that is less (5 at level 1, 4 at 2, 3 at 3,
  4, 6 and 7, 2 at 8 and 9), so no level of the depth can hold more than
  96 whatever rooms it draws; neither link can turn a point back into a
  prop, so the cap is held here. Past it the theme's prop stays.
  `library.json` counts each room's (`breakables`) and `descent-level`
  refuses a level over 96. A breakable is solid to every rule (the dress
  lint's pieces, the giant's flat walk, the cut-off rules) and stands
  where the room compile's standing agent (32 by 72) fits in its voxel,
  which the compile asks of every point (`dress_breakable`); `marker_floor`
  leaves it to that rule.
- `WithLamp(at, lamp?)`: `Lamp.Default` (warm white, 200, half at 256, gone
  at 768) is measured to light a grey room without clipping; `new Lamp(colour,
  brightness, half, zero)` for a theme's own. A lamp lights its room and the
  next; space them about 256 apart. A lamp near a 1024 ceiling does not reach
  the floor: hang a hall's lamps about 448 up with a longer falloff
  (`Parts.HallLamp`), and dim a corridor's (`Parts.ConnectorLamp`), whose
  walls are close. The demonstration level measures `checks.lighting` mean
  0.32, clipped 0.001; aim for a mean well under 1 and clipped under 0.02.

## Themes and dressing (`Themes/`, `Dressing/`)

- **A depth's theme** (`RoomTheme.ForDepth`, `DescentRooms.ThemeFor`; a
  theme's `Caves` draws and dresses its `CaveFamilies` instead, level 11's
  grottos and flooded galleries in rock and concrete, brief p15):
  Act I's four in `Themes/ActOne.cs` (surfaces, door frame, lamp kinds,
  props by role, fog, tone map, wetness, sun and sky, the way in's theme),
  Act II's four in `Themes/ActTwo.cs` (levels 6 to 9: the Breakthrough,
  Dripstone, the Sunken Galleries, the Buried Chapel), the boss levels' three
  in `Themes/Bosses.cs` (5 the Powder Magazine, 10 the Bore, 15 the Pit, brief
  p13), Act III's four in `Themes/ActThree.cs` (levels 11 to 14: Robot Works,
  the Sump, Brimstone, the Rot, brief p14). A theme may name its
  own water (`Water`, a `water` shader material: the pools' and flooded
  galleries'), water standing on its rooms' floors (`Standing`: a depth,
  kept clear of every door, in some families or all), and a larger room's
  wall drawn larger (`LargeWall`: a 2048 wall at a cell's scale tiles into
  bricks, and a larger room's rough rock is drawn in it). A theme's own
  materials beside its plug (`Extra`, `OwnMaterial`, written into
  `game/mod_tf2_descent/materials/descent` by `MapGen descent-materials`):
  `OwnMaterial.Water` is a water that reads as water in an LDR map with no
  cubemap (the scene reflected, `_rt_WaterReflection` without entities, a
  short tinting fog, TF2's rippling normal map; levels 7 and 8; keep its fog
  colour near the level's darks: within a few hundred units the water a
  player sees is mostly that fog, and level 8's at `30 44 30` drew a lit
  teal sheet that read 35 in game for its 24, brief p11), and
  `OwnMaterial.Plain` a lit brush material copied without its `$envmap` (the
  map tools' cubemap fix-up throws on one in a room with no cubemap); each is
  listed in its palette as the TF2 material it is made from
  (`RoomTheme.AssetOf`): level 8's floor is such a copy of TF2's wet rock,
  the bottom its water and the lamp show (a bed brush under the water, a unit
  proud of the floor, was tried and dropped: a giant's flat walk reads any
  solid above the floor as a wall). `RoutePools` sets a second lamp kind in pools along each
  room's route (level 9's candle clusters on the floor by the walls). Every
  asset a theme names is a row of `Themes/palettes/dNN.tsv` and of the asset
  database (`DescentPaletteTests`); add the row when you add the asset.
- **A shape draws in the theme's surfaces and nothing else.** It may place
  lamps where it wants light; a themed depth replaces them. The dressing
  pass (`Dressing.RoomDresser`, run by `DescentRooms.Dressed`) reads a built
  room's own brushes, floor, walls, doors and markers, and adds the theme's
  sets, lamps (along the giant's route from door to door), rails, ceiling
  runs, corner piles and wall and floor clutter. It keeps 96 clear of
  doors, off markers, out of every raised surface's 114 of ledge air and a
  jump room's open floor, and keeps a giant's 92 by 156 route between doors
  (a rail or kerb a step high is floor a giant walks over);
  `Dressing.DressLint` holds every library to that. After the route's lamps
  it covers whatever walkable floor is still dark by the lint's own `lit`
  rule (an alcove, a ledge, a floor under a deck, a chamber whose roof is out
  of a ceiling lamp's reach), hanging the theme's lamp that lights most of it
  where it fits, of any of its kinds (`Lamps`, `TallLamps`, `CoverLamps`).
  Floor clutter stands only in rooms, never in a hallway (`corridor` or
  `junction`).
- **Structure and placed pieces** (`Dressing/RoomDresser.Structure.cs`, the
  review of 2026-11-10): `RoomTheme.WallLow` cuts every tall wall at
  `WallBand` into a second material (`RoomBuilder` does it in the shell);
  `Pilasters` stand sets or posts up walls of rooms 768 high and more;
  `Bands` run a skirting (a step at most), a dado, belts every 512 and a beam
  under the roof along flat walls, last, round whatever stands there. A
  lamp kind with `Pools` lights a room in three to five pools along its
  route, hung from a set's cap or stood on its `Stand` (a crate) against a
  wall; `WorkLamps` hang a cold flood in each large room, or at a room's
  focus. `PropSet.Rail` runs on each door's axis in the `RailFamilies`
  (tunnels and yards) from the cell face inward: the doorway piece is kept
  only where the door is joined (`room_needs joined_<wall>`), a `RailStop`
  (buffer) where it is capped (`!joined_<wall>`), two doors at a corner are
  joined by `RailBend`, a third or fourth door is a spur ending in a stop,
  a run is finished with a short piece set back to its end (no gap), and a
  blocked run ends in a stop; `StubFamilies` (stores, stopes) get one stub
  with a cart. `Hung` pieces (signs, boards, a clock) hang on flat walls at
  their own heights; `WallSeam` cuts a band of a second material through
  tall walls; `CoolFill` spreads dim cold light under the roof; `Hazard`
  stripes only door heads and deck edges; `RoofBeams` span halls and shafts.
  `Companions` group clutter; `FloorShare` keeps a machine out of a room too
  small for it. A jump room's floor takes clutter as any room's does (user
  ruling M28, 2026-11-10), but not within `RoomDresser.TakeOff` (128) of a
  ledge only a jump reaches (`JumpAirRefuses`), and a dressing brush may
  stand in its rocket air only while the room keeps §4.1's clear air
  (`RoomLint.CheckJumpClearAir`, which the dress lint reads). `Pilasters.Cap`
  joins neighbouring pilasters with a wall plate into sets lining the wall,
  and wall clutter stands first in the bays between them; a larger room's
  lamp pools stand along its walls (`WallPools`). `SetFrame.Broken` lets
  some sets fall in (a post left, level 6); `PropSet.Hanging` hangs pieces
  upside down from a tall room's roof (stalactites, level 7), their tips two
  players' heights over the floor and over a jump room's rocket air. A lamp standing on the floor (a lamp
  post, a brazier) may not wall floor off, and clutter set beside it keeps a
  player's width from it or stands flush (a pump 30 from a post walled a trap). `Pendants` hang a lamp on a cord of stacked cable in a grid
  over a hall's body, its bottom above a jump room's rocket air, and are the
  cover pass's choice where a larger room's middle is still dark. A larger
  room's set pieces (`Dressing/RoomDresser.Halls.cs`): a `Centrepiece` on its
  focus (a hall's plinth: an ore heap, a generator), a `Headframe` of the
  trim over it (level 3), a `Gantry` (two girders wall to wall and a bridge
  with a hook, levels 2 and 4), a second pipe run at `WallRunHeight` along
  a tall wall, and level 2's track through the yard hall, each socket's run
  kept only where it is joined and the stops where two halves meet kept
  only when one is missing (`room_needs "joined_west0,!joined_east0"`). Sets stand only in hallways and
  low rooms. Clutter keeps off decks' and pits' edges, and a solid piece that
  would cut floor off a giant's walk is refused or taken out again
  (`dress_cut_off`).
- **Quiet rooms** (brief p9): `RoomCatalogue.QuietTwins` adds a twin of the
  first room of the drift, bend, tee, cross and stub families with no pack
  spawn (`RoomEntry.Quiet`, the codec's flag `q`: `drift_aq-ew-l-q`; the lint
  refuses a pack in one), and the layout places them beside the way in and
  nowhere else, so no pack spawns in the arrival room or a room one door from
  it; a boss level's row takes the quiet drift as a lobby between its way in
  and its antechamber. A family's preview camera looks at its ordinary room
  before its quiet twin.
- **Caves** (`CavernShape.RockAround`, `RockOverDoor`): rough rock runs beside
  each door of a cavern and from over its frame to the roof, and over each of a
  grotto's sockets, so no cave wall stands flat floor to roof round its doors
  (cavern d's walls are its rounded corners and its necks, and keep them).
- **Act III's hazards and failed lamps** (brief p14, `Dressing/RoomDresser.ActThree.cs`):
  a theme's `HazardFloor` (a `hazards.json` kind, its surface, liquid or a
  skin, its glow, its families) lies in its pools' basins, its flooded
  galleries' water and, when it says `Chasms`, a chasm leap's gap (the shapes
  lay it with `RoomBuilder.HazardFloor`), and the dresser lays it on the floor
  of its families' rooms that the way between the doors does not need: the
  largest rectangles left free by the giant's lane and two columns more, the
  doors' approaches, every marker (`HazardFromMarker`) and a blast ledge's
  take-off, at most `MaxAreas` a room, each kept only while a giant still
  walks dry from door to door and to every pack (`KeepsWay`); each under its
  volume, nothing standing on it, its own light (`Glow`, a fill with no
  fixture) over it every `GlowPitch`. The room lint's `hazard_path` holds it.
  A lava skin is one non-solid `func_brush` of the self-lit material with a
  `_minlight`, so the preview, which draws by the lightmap alone, sees it lit.
  `LampKind.Dead` lets a share of the theme's own lamps along the path hang
  with no light (level 11, marked `descent_dead`, which the dress lint's
  `dress_lamp_light` lets through); the floor they leave dark is lit first by
  the theme's `CoverLamps` alone (its red emergency lamps).
  `HazardFloor.Lips` sets low lumps of rock (a step high at most) round each
  area the dresser lays, so a pool reads as a pool in a cave floor rather
  than a laid slab (level 13's lava, brief p15).
  `LampKind.FreeStanding` lets the cover pass stand a floor lamp on open floor
  (level 14's fungi in a cave's middle); a floor lamp never stands in a
  ledge's air or a solid one in a corridor's lane, so a theme of floor lamps
  needs wall cover lamps hung over a walkway's air (13's sconces, 14's wall
  fungi at 128).
- **Breakables in the preview**: neither linked map holds a breakable, so
  `tools/descent-preview-spawns` (run on the preview's map) has
  `tools/descent-breakables preview` read the points from the preview's
  `.nav3d`, as the server reads them, and stand the server's models there
  as solid static props casting no shadow (the lightmaps are baked without
  them, as the game's are), and rebinds the preview's `.map2d` to the new
  map; the sheets and the reach gate's walker see them.
  `tools/descent-breakables list <map.nav3d>` prints a level's breakables.
- **Preview cameras** (`PreviewCamera`): each room's `library.json` entry
  carries a camera at a player's eye in a corner of its body that sees its
  focus, in open air (`PreviewCamera.InOpenAir`: a larger room's corner
  camera walks in toward its focus out of whatever solid stands there, brief
  p15); `descent-level` turns it with the room. `descent-level` also renders
  every placed room's camera (`.roomcams`, `DescentLevel.RoomCameras`) and
  `tools/descent-room-light` writes each room's light, on the gate's scale,
  into the level's `.map2d` as a section `LITE` that today's readers skip:
  per room in the `.map2d`'s room order a float (level 1 = 100) and flags
  (1 dark by design: no light of its own; 2 measured). The preview's own spawn
  views are cut to one (`tools/descent-preview-spawns` keeps the arrival).
- **The gates** (`tools/descent-gate`, `make descent-brightness`,
  `descent-reach`, `descent-budget`, `DESCENT_DEPTHS=1,2,3,4,6,7,8,9
  DESCENT_SEEDS=1,2,3`; name 5, 10 and 15 for the boss levels, each held to
  its own target, 52, 22 and 7, and left out of the falling order, `BOSS`;
  name 11 to 14 for Act III, 16, 13, 11 and 9):
  brightness is what a player sees, offline: the
  mean over the preview's room views of each tile's luma re-encoded to the
  game's 2.2 gamma (the preview's label and orange scale figure left out),
  over seeds, level 1 = 100 (`LEVEL1_VIEW`); each depth within 3 of its
  in-game target (plan section 6, M30) and falling strictly, the figure first
  multiplied by its act's `CALIBRATION` (brief p15: the game's own reading of
  each act over the gate's, from the in-game measurements of p10 to p14: Act
  I 1.019, Act II 0.969, Act III 0.769; the table prints the raw figure
  beside the `game` one it predicts), nothing clipped
  in the lightmap or in any room's view (its rendered pixels under 2 % white),
  a Heavy reaches every room's floor,
  entities and brushes stay in budget. The preview gets a camera on one
  joined doorway (`DescentLevel.DoorwayCamera`, a tracked room's first).
  What the gate cannot see (brief p11, plan 1.3e): the preview draws no
  water surface and no fog, and draws a face from inside a solid. A
  worldvertextransition material on a plain brush draws its second layer in
  game (the Rot's and the Pit's mud floors drew twigs): use a lit one. The game
  read level 8 9 over the gate while its water's fog was light, and reads
  level 6 6 under it, half of that a doorway camera standing inside a
  stair block and half a per-level residual (level 4 2.5 under, level 1
  1.7 over); fog moved levels 4 and 6 in game by under a point. Act III reads
  25 to 30 % darker in game than in the gate (brief p14: game over gate 0.76,
  0.90, 0.70, 0.78 at 11 to 14), so a dark level's gate sits toward the top of
  its tolerance. Check a level's change in game, not only in the gate.

## Lint (`RoomLint.Check`)

Each rule is measured from the brushes and markers, on 16-unit columns:

| Rule | Refuses |
|---|---|
| `sealed` | open air from a door reaching the cell's edge |
| `doors_on_kit` | a door without exactly one plug filling the kit's opening, or a plug brush anywhere else |
| `marker_floor` | a marker with no floor within a step below it, or no 48 by 82 standing room |
| `jump_clear_air` | a room whose entry says `jump` without 576 of clear air over half its reachable floor and a straight 768 run |
| `ledge_pays` | a ledge higher than T1 that no T1 route reaches and that has no reward marker on it |
| `inside_cell` | a brush or entity outside the cell |
| `frame_clear` | a door without one frame and its sill planks kept where it is joined, a solid frame, or a kit whose frame leaves less than a giant's 92 by 156 |
| `door_path` | two doors a giant (92 by 156) cannot walk between on steps of 18 (M8: no jump, not even a crouch-jump; stairs and ramps are walked), or a pack spawn on the floor no giant walks to from a door |
| `marker_count` | a combat room without 2 to 4 pack spawns at least 192 from every door and 1 or 2 pieces of furniture on its floor; a corridor or junction with more than one pack; a quiet room with any |
| `lit` | walkable floor beyond every light's reach (half way from its half-light to its zero distance; a `light_spot` only inside its outer cone): the path is lit (§6) |
| `hazard_path` | two doors a giant (92 by 156) walks between only through an always-on hazard volume (slime, lava): a hazard floor is never the only way through (brief p14) |

## Testing and looking

- Facts live in `src/test/server/Descent*Tests.cs`. For a new family add a
  fact that its rooms lint clean (`RoomLint.Check`) at a few seeds, and one
  per thing the family promises.
- `make managed-test FILTER='FullyQualifiedName~Descent'`.
- `make descent-rooms DEPTH=n` writes depth
  n's library (`DescentRooms`: the `RoomCatalogue.Mine` families, or a boss
  depth's `RoomCatalogue.Boss` row, on the normal kit) and its
  `library.json` into `bin/<config>/descent-rooms/dNN/` and packs it with
  `ssmap room` from the map tools at `maptools.lock` (the checkout `make
  setup` recorded in `maptools.lock.local`), reading the mod's own game
  directory.
- `make descent-level DEPTH=n SEED=s` lays a level out of it with the rules'
  generator (`MapGen descent-level`), links it as the host does and again for
  the preview, and previews it with `-movement heavy`. Open
  `dNN/tf2_descent-n-ss-preview/sheet.png` and look before you ship a family.
  The preview draws a face from either side; the game does not. After each
  link `tools/descent-face-sides` sets every face's side flag from its BSP
  node, because the linker writes it wrong in every turned room and the game
  then shows that room's walls only from behind (sky or black through them).
- `make descent-install DEPTH=n SEED=s` links the level's `.bsp`, `.nav3d`
  and `.map2d` into the tf2_descent mod's `pool/maps`, where `map
  tf2_descent-n-ss` finds it in game.
