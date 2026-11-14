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
kept in `Room.Water`, not solid to the lint), `WithSkyCeiling(area)` (the
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
  casts shadows. `WithoutDoorFrames()` is for test fixtures only.
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
and its open floor is the whole interior (`Body`). `WithSockets((face,
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
5's 2x2, 10's 3x3, 15's 4x4).

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
- `WithLamp(at, lamp?)`: `Lamp.Default` (warm white, 200, half at 256, gone
  at 768) is measured to light a grey room without clipping; `new Lamp(colour,
  brightness, half, zero)` for a theme's own. A lamp lights its room and the
  next; space them about 256 apart. A lamp near a 1024 ceiling does not reach
  the floor: hang a hall's lamps about 448 up with a longer falloff
  (`Parts.HallLamp`), and dim a corridor's (`Parts.ConnectorLamp`), whose
  walls are close. The demonstration level measures `checks.lighting` mean
  0.32, clipped 0.001; aim for a mean well under 1 and clipped under 0.02.

## Themes and dressing (`Themes/`, `Dressing/`)

- **A depth's theme** (`RoomTheme.ForDepth`, `DescentRooms.ThemeFor`):
  Act I's four in `Themes/ActOne.cs` (surfaces, door frame, lamp kinds,
  props by role, fog, tone map, wetness, sun and sky, the way in's theme),
  Act II's four in `Themes/ActTwo.cs` (levels 6 to 9: the Breakthrough,
  Dripstone, the Sunken Galleries, the Buried Chapel); depths 5 and 10 to 15
  are `RoomTheme.Grey` until their acts are themed. A theme may name its
  own water (`Water`, a `water` shader material: the pools' and flooded
  galleries'), water standing on its rooms' floors (`Standing`: a depth,
  kept clear of every door, in some families or all), and a larger room's
  wall drawn larger (`LargeWall`: a 2048 wall at a cell's scale tiles into
  bricks). Every
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
- **Preview cameras** (`PreviewCamera`): each room's `library.json` entry
  carries a camera at a player's eye in a corner of its body that sees its
  focus; `descent-level` turns it with the room. The preview's own spawn
  views are cut to one (`tools/descent-preview-spawns` keeps the arrival).
- **The gates** (`tools/descent-gate`, `make descent-brightness`,
  `descent-reach`, `descent-budget`, `DESCENT_DEPTHS=1,2,3,4,6,7,8,9
  DESCENT_SEEDS=1,2,3`; level 5's boss row stays out of the brightness order
  until its arena is themed): brightness falls strictly from depth to depth with
  nothing clipped, in the level's lightmap and in every room's view (its
  rendered pixels under 2 % white), a Heavy reaches every room's floor,
  entities and brushes stay in budget. The preview gets a camera on one
  joined doorway (`DescentLevel.DoorwayCamera`, a tracked room's first).

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
| `marker_count` | a combat room without 2 to 4 pack spawns at least 192 from every door and 1 or 2 pieces of furniture on its floor; a corridor or junction with more than one pack |
| `lit` | walkable floor beyond every light's reach (half way from its half-light to its zero distance; a `light_spot` only inside its outer cone): the path is lit (§6) |

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
