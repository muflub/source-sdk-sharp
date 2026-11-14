# Descent rooms: writing a shape family

The mine levels of TF2 Descent are linked by map-tools from room libraries
that this folder writes (`docs/plans/tf2_descent/plan_descent_mine_levels.md`,
sections 3 to 5 and 10). A **shape family** is one static method that
returns every variant of one shape:

```csharp
public static IEnumerable<Room> Variants(RoomKit kit, RoomTheme theme, ulong seed)
```

and it joins the catalogue as a `RoomShape("family", MyShape.Variants)` in
`RoomCatalogue`. `Shapes/DriftShape.cs` and `Shapes/HallShape.cs` are the two
worked examples; copy one. The rest of `Shapes/` is Phase 1's grey
placeholders, one family a file, for a real shape to replace in place.

## Rules a family keeps

- **Name every room `<family>_<variant>`** (`hall_we`), the variant lower-case
  letters and digits. The catalogue refuses anything else, and the names must
  be stable for a seed. It then renames the room by the rules module's codec
  (`TF2.Descent.Rules.Layout.RoomName`, plan E8) from the room's own facts:
  `hall_we-ew-h-j.s2.b1` is its doors, its height class (`room_height` up to
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

## Building a room

```csharp
var b = new RoomBuilder("hall_we", kit, theme)
    .WithHeight(1024)                      // room_height; any whole number of nav voxels >= 256
    .WithWidth(kit.Interior)               // the interior square; 256..384 makes a corridor
    .WithDoors(CellFace.West, CellFace.East)
    .WithEntry(new RoomEntry(["junction"], Weight: 2, MinDepth: 1, MaxDepth: 9, Jump: true));
```

- **The kit** (`RoomKit.Normal`: cell 1024, door 208 by 192, wall 16,
  navigation voxel 16) is shared by every room of a level. It is cut to its
  door frame model (`RoomKit.For`, `FrameModel`): the wall is one voxel, and
  each room's 16-deep frame fills its half of the doorway; the opening is
  the frame's outer width and its height under the head. Write every size from the kit, never as a number:
  the boss kits (2048, 3072, 4096) have walls of 16 and 32. Doors stand on
  the floor (z = the wall's depth, room-local) centred on their wall; the
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
  model and sill planks (as many as cover the wall's depth) as non-solid
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
  (`HallShape.HallLamp`), and dim a corridor's (`DriftShape.DriftLamp`), whose
  walls are close. The demonstration level measures `checks.lighting` mean
  0.32, clipped 0.001; aim for a mean well under 1 and clipped under 0.02.

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
| `frame_clear` | a door without one frame and one sill prop kept where it is joined, a solid frame, or a kit whose frame leaves less than a giant's 92 by 156 |

## Testing and looking

- Facts live in `src/test/server/Descent*Tests.cs`. For a new family add a
  fact that its rooms lint clean (`RoomLint.Check`) at a few seeds, and one
  per thing the family promises.
- `make managed-test FILTER='FullyQualifiedName~Descent'`.
- `make descent-rooms DEPTH=n MAPTOOLS_DIR=<map tools checkout>` writes depth
  n's library (`DescentRooms`: the `RoomCatalogue.Grey` families on the normal
  kit, or a boss depth's `RoomCatalogue.Boss` row on its own kit) and its
  `library.json` into `bin/<config>/descent-rooms/dNN/` and packs it with
  `ssmap room`, reading the mod's own game directory.
- `make descent-level DEPTH=n SEED=s` lays a level out of it with the rules'
  generator (`MapGen descent-level`), links it as the host does and again for
  the preview, and previews it with `-movement heavy`. Open
  `dNN/tf2_descent-n-ss-preview/sheet.png` and look before you ship a family.
