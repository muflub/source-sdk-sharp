# Plan: map authoring Claude can see, measure and improve

Status: DRAFT for user iteration (2026-10-11). Uncommitted until reviewed.

## Goal

Claude builds maps that are detailed, well textured and fun to play, not
grey-box valleys. Today it writes C# that places axis-aligned boxes
(`src/mapgen/Vmf.cs`: `Box`, `Pane`) from a hand-picked list of 7 materials and
7 models (`TownPalette`, `src/mapgen/TF2DescentTown.cs`). It never sees the
result and has nothing to measure it against.

Four things are missing, and each has a phase here:

| Gap | Phase |
|---|---|
| Claude cannot see what it built | M1, the viewer (the user is planning this tool) |
| Claude does not know what art exists or what it looks like | M2, the content catalogue |
| Claude has no reference for what good looks like | M3, the corpus of stock TF2 maps |
| Boxes are the only shape it can make | M4, the authoring library |
| "Fun" is never measured | M5, layout metrics |
| The steps are not one loop | M6, the `map-screen` skill |

## Baseline (measured 2026-10-11)

Stock maps (`grep -c` over the Valve sources in
`~/.steam/steam/steamapps/common/sourcesdk_content/tf/mapsrc`):

| map | solids | displacements | overlays | prop_static | lights | size |
|---|---:|---:|---:|---:|---:|---:|
| sdk_ctf_2fort | 5,863 | 232 | 237 | 2,273 | 288 | 13 MB |
| sdk_cp_granary | 5,100 | 124 | 282 | 1,548 | 129 | 11 MB |
| sdk_pl_badwater | 4,499 | 739 | 230 | 1,184 | 141 | 11 MB |
| sdk_arena_lumberyard | 2,345 | 64 | 143 | 587 | 38 | 4.6 MB |

tf2_descent_town is 339 lines of generator, with no displacements, overlays,
detail props or cubemaps. That gap in detail is what the user sees.

Inputs available on this box:

- 14 Valve TF2 VMF sources in `sourcesdk_content/tf/mapsrc` (2fort, granary,
  badwater, goldrush, dustbowl, gravelpit, hydro, badlands, lumberyard, ravine,
  mvm_example and others).
- 239 compiled TF2 maps in `Team Fortress 2/tf/maps` (11 GB): 59 cp, 45 koth,
  41 pl, 25 ctf, 13 arena, 9 plr, 7 mvm.
- map-tools (`../source-sdk-map-tools`) already has readers for VPK, VMT, VTF,
  MDL/PHY, BSP (static and detail props, displacements, entities), VMF and
  `.map2d`/nav3d. Nothing new has to be parsed from scratch.

## Answers to the user's questions

### Q2: what format avoids Claude reparsing the content every time?

Build an index once, then query it. Claude never reads a VPK, VMT or MDL; it
reads small answers.

- **The store is SQLite** (`content.db`), one table each for materials,
  models, map statistics and prefabs. It is keyed by
  `(vpk path, size, mtime)`, so a TF2 update rebuilds only what changed. map-tools
  already has `SourceSharp.MapTools.Cache.Sqlite`, so the dependency is
  already there.
- **Claude reads it through a CLI with compact output** (`tools/content
  find|show|similar|sheet`), at most about 40 rows, in TSV. Example:
  `tools/content find --kind material --tag wall --theme mining`. Claude does
  not run SQL by hand: the CLI is the stable surface, and its tests pin it.
- **Images are cached PNG contact sheets**, built on demand (for example a
  4×6 grid of 128 px tiles, each labelled) and kept under
  `bin/<config>/content-cache/`. A sheet is something Claude can look at with
  `Read`. A sheet is rebuilt only when its inputs' hash changes.
- **Two small digests are committed**: `docs/maps/content-digest.md` (themes,
  counts, the 200 most-used materials and models in stock maps) and
  `docs/maps/style-sets.tsv`. These are what a session reads first; the
  database answers anything more specific.

Rejected: one big JSON or JSONL file. It is cheap to write, but every question
would mean reading megabytes, which is exactly the reparsing to avoid.

### Q3: would a Python VMF library be better?

No. The language is not the bottleneck; the vocabulary is. Reasons to stay
with C#:

- The readers and writers that already exist (map-tools' `Vmf.cs` with
  displacements, the BSP and MDL readers, the town's facts) are C#. A Python
  library would duplicate them, and the duplicate would drift.
- The CLAUDE.md rule is to maximise C#, and the map facts run in `make test`
  already.
- **.NET 10 file-based apps** (`dotnet run map.cs`) give the scripting feel
  Python would: one file per map, no project file, referencing the library
  with `#:project`. That is the authoring surface (M4).

What does matter is a declarative, higher-level API (rooms, buildings, trims,
terrain, prop scatter) that Claude can write in about 200 lines per map,
rather than raw coordinates.

**Ruled (U3): C#.** No Python front end.

### Q4: can we learn from the existing TF2 maps?

Yes, as a reference corpus read locally (M3). Two kinds of data:

- **The 14 VMF sources** hold what the BSPs have lost: brush groups,
  `func_detail`, how textures are aligned, visgroups, and the instance and
  prefab structure. These are the source for prefabs and for texture-pairing
  rules (which trim goes with which wall, how a floor meets a wall).
- **The 239 BSPs** are for statistics: prop density per square metre, light
  spacing, how often each material is used, displacement share, and room and
  corridor dimensions drawn from the nav mesh and `.map2d`.

**Licensing rule:** Valve content is read in place and never copied into
either repo. Generated maps reference TF2 assets by path, as tf2_descent_town
already does. Mined prefabs are stored as our own parameterised descriptions
(dimensions, materials by role) with a source citation, not as copied brush
data.

**Ruled (U4): the corpus can grow beyond the stock maps.** The corpus is
open-ended: a source is any folder of `.vmf` and/or `.bsp` files plus a row in
`corpus/sources.tsv` (name, origin URL, licence or permission note, date
fetched). Ingest is by source, so adding a pack is one row and one
`corpus add <source>`. The read-in-place rule holds for every source: maps
live outside the repo (under `~/.local/share/<project>/corpus/<source>/`),
and the repo commits only `sources.tsv`, statistics and parameterised
prefabs with citations. Which extra packs to fetch first is open (O2).

## Phases

Each phase commits after every step, with facts for the logic, under the
repository rules.

**Ruled (U1): a new project that uses map-tools.** M2 to M6 live in a new
repository beside the others (working name `source-sdk-map-studio`, O1). It
references map-tools' libraries (`SourceSharp.MapFormats`, `SourceSharp.MapTools`,
`SourceSharp.MapGen`) and does not edit them; a gap in map-tools goes to its
owner as a request. map-tools stays the compiler and the file formats; the new
project holds the catalogue, corpus, authoring library, metrics and skill.
This repo keeps only tf2_descent_town's facts and its `make map` hook, which
calls the new project's built tool once M4.8 lands.

Layout of the new project:

| Path | What |
|---|---|
| `src/Studio.Content` | M2 indexer, `content.db`, query CLI, contact sheets |
| `src/Studio.Render` | the headless renderer (U2): MDL meshes and VTF textures to PNG |
| `src/Studio.Corpus` | M3 ingest, statistics, style sets, prefab mining |
| `src/Studio.Authoring` | M4 library (shapes, textures, terrain, blocks) |
| `src/Studio.Metrics` | M5 navigation, sight lines, heatmaps |
| `maps/*.cs` | one file-based C# app per map |
| `tests/` | xUnit facts for each of the above |
| `.claude/skills/map-screen` | M6 |

### M1: the viewer (owned by the user's own plan)

What the rest of this plan needs from the viewer:

1. **Named viewpoints:** screenshots from `info_target cam_*` entities in the
   map, plus a free camera position, returned as JPEGs at a fixed size.
2. **Reload:** reload the map in a running game (`changelevel` or a reload
   call), so one round of edit, compile and look takes seconds. ssmap
   compiles ss_sandbox in about 1.5 s (map-tools README, NativeAOT).
3. **A top view:** the `.map2d` SVG preview turned into a PNG, with overlays
   for spawns, objectives and the M5 heatmaps.
4. **A noclip walk** along a path at player eye height (64 units), to judge
   scale the way a player sees it.
5. **Reading values in the world:** light level at a point, and the material
   and prop under the crosshair, for the lint in M6.

### M2: the content catalogue

1. **Indexer** (`content index`): walks the mounted VPKs and loose files.
   - Materials: name, `$basetexture`, `$surfaceprop`, size, translucency,
     whether it has `$bumpmap` or `$envmap`, and the mean colour.
   - Models: bounding box, `$surfaceprop`, skins, whether it has physics, and
     its prop type (static, physics or dynamic, from the model's own keyvalues).
2. **Tags:** derived from paths (`nature/`, `wood/`, `props_mining/`) and from
   stock usage, such as "used as trim in 2fort". No hand curation at the start.
3. **Thumbnails, headless (ruled U2):**
   - Materials: decode the VTF to a PNG (map-tools' `VtfFile`).
   - Models: `Studio.Render`, a CPU rasteriser over the MDL/VVD/VTX mesh with
     the model's base textures, flat-lit, from three fixed angles (front,
     three-quarter, top) at 128 px. It needs no game, no GPU and no display,
     so the indexer runs anywhere and in CI.
   - The same renderer draws the M1-independent previews: a whole-map
     isometric or top view from the BSP, so M3 and M4 have pictures before the
     viewer lands.
   - Fact: a render of a known model has the expected silhouette bounds and is
     byte-stable across runs.
4. **Query CLI and contact sheets**, as described under Q2.
5. **Facts:** that the indexer finds a known material and model with known
   bounds, that a VPK change re-indexes only the changed rows, and that a
   missing VPK is a failure, not an empty catalogue.

### M3: the reference corpus from stock maps

1. **Map statistics** from the BSPs into `content.db`: per-map counts,
   densities, the material and model histogram, light count and spacing, and
   the displacement share.
2. **Style sets:** clusters of materials and props that appear together in
   stock maps (mining, alpine, farm, industrial, spytech), written to
   `docs/maps/style-sets.tsv`. They replace the hand-picked `TownPalette`.
3. **Prefab mining** from the 14 VMFs: find repeated brush groups (doorways,
   window frames, roof edges, stairs), record each as a parameterised
   description, and add a reference screenshot through M1.
4. **Design metrics:** room sizes, corridor widths, ceiling heights, cover
   spacing and height changes, from the nav mesh and `.map2d` of the stock
   maps. They become the reference ranges M5 checks against.
5. **Gate:** the corpus statistics for 2fort reproduce the `grep -c` counts in
   the baseline table above (5,863 solids, 2,273 props and so on).

### M4: the authoring library

`Studio.Authoring`, built on map-tools' VMF writer, goes from boxes to a
vocabulary:

1. **Shapes:** wedges, ramps, stairs (step 18 units, using the player
   constants from the game, not literals), arches, cylinders, and arbitrary
   convex brushes.
2. **Textures:** world-aligned and face-aligned UVs, scale by role, and trim
   strips where faces meet (floor/wall, wall/ceiling, corners).
3. **Terrain:** displacements with heightfield and noise, blended materials
   (two-texture blend with an alpha painted by slope or height), and cliffs.
4. **Detail:** overlays and decals, `func_detail`, detail props (grass, rocks),
   prop scatter with collision and spacing rules, and `env_cubemap` placed
   automatically per room.
5. **Lighting and atmosphere:** `light_environment` presets per style set,
   light placement driven by the props that emit light (a lantern model
   brings its `light`), fog, tonemapping, soundscapes.
6. **Building blocks:** `Room`, `Building(floors, roof, windows, doors)`,
   `Street`, `Bridge`, `Cliff`, `Courtyard`, each built from a style set and
   the mined prefabs.
7. **Authoring surface:** one `dotnet run maps/<name>.cs` file per map, which
   writes the VMF, compiles with ssmap, and reloads the game through M1.
8. **First target (ruled U5): tf2_descent_town.** Rebuild it as
   `maps/tf2_descent_town.cs` on the library. Its existing facts in this repo
   (contract entities, spawns, transition, palette present in the VPKs) stay
   green against the new output, and before and after renders are compared.
   Then `src/mapgen`'s `TF2DescentTown.cs` is retired in a separate commit.

### M5: layout metrics ("fun", measured)

1. **Rules, written down:** hull 49×49×83, step 18, jump and crouch-jump
   heights, door and corridor widths, from the game code and not invented.
   Kept in `docs/maps/design-rules.md`.
2. **Navigation analysis** on the nav mesh: shortest paths spawn to objective
   and spawn to spawn, the number of distinct routes (route diversity),
   chokepoint width, height variation along routes, and dead ends.
3. **Sight lines:** the distribution of sight-line lengths from sampled points,
   the share of long sight lines (sniper lanes), and cover density (the share
   of nav area within 2 m of cover).
4. **Comparison:** each metric is reported against the stock range for the
   same game mode from M3, as "inside, below or above the range of
   2fort/granary/badwater". There is no single "fun" score.
5. **Heatmaps** on the M1 top view: visibility, route traffic, cover.
6. **Later:** bot playtests (kill and death positions, time to first fight),
   if bots run on this port. Out of scope until then.

### M6: the `map-screen` skill

A skill built like `ui-screen`: **write → compile → look → measure → lint →
revise**.

1. Read the content digest and style sets, and pick a style.
2. Block out the layout, then run the M5 metrics before adding any detail
   (layout first, because detail is wasted on a layout that fails).
3. Detail pass with the M4 library, looking at screenshots from each
   viewpoint after each pass.
4. **Visual lint:**
   - large untextured or unbroken surfaces;
   - texture repetition;
   - a room with no light source to motivate its light;
   - props floating or intersecting;
   - per-room density below the stock range;
   - missing cubemaps.
5. **Critique rubric** in `docs/maps/design-guide.md`: landmarks for
   orientation, lighting that leads the player, framed views, the three-route
   rule, and asymmetry within symmetry. Claude scores its own screenshots
   against it and writes down what it changes.
6. **Goldens:** reference screenshots per map, to catch regressions, as the UI
   goldens do.

## Order and checkpoints

1. M2 and M3 can start now; they do not need the viewer.
2. M4.1 to M4.3 (shapes, textures, terrain) in parallel with M2.
3. **Checkpoint A (user):** contact sheets, style sets and the 2fort
   statistics are in front of the user before M4.6 builds on them.
4. M1 lands (user plan), then M5, M4.6 to M4.8, then M6.
5. **Checkpoint B (user):** tf2_descent_town rebuilt on the library, with
   before and after screenshots.

## Rulings (2026-10-11)

- **U1:** a new project that uses map-tools (see Phases).
- **U2:** the headless renderer for thumbnails and previews.
- **U3:** C#.
- **U4:** more maps can be added to the corpus; ingest by source.
- **U5:** tf2_descent_town is the first target.

## Open items

- **O1:** the new project's name and location (working name
  `../source-sdk-map-studio`), and whether it consumes map-tools as a project
  reference to a sibling checkout (pinned by a lock file, like
  `sdk-root.lock`) or as NuGet packages.
- **O2:** which extra map packs come first. Candidates: the other games'
  `sdk_*` VMFs already in `sourcesdk_content` (hl2mp, cstrike, dod), and
  community TF2 maps whose authors publish VMFs.
- **O3:** whether the user's M1 viewer lives in this repo (it drives the game)
  with the new project calling it, which is the assumption here.
