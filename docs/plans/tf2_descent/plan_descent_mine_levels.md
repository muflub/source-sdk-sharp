# plan_descent_mine_levels.md: the fifteen levels of the Descent mine shaft

Status: reviewed and committed 2026-11-10. Brief p9 (2026-11-14): the brightness gate on the game's scale, Act II's second look pass, and the logic session's asks (section 1.3e). Phases 0 to 2 done; Phase 3 (Act I) built and reviewed from its preview sheets on 2026-11-10 (section 1.3a, section 11's status table), a third review pass the same day (brief p3c: the great halls' interiors, a door frame per theme, the boss arenas on the 1024 kit), waiting on the user's checkpoint; Phase 4 (Act II, levels 6 to 9) built and reviewed from its sheets in three rounds and two more on levels 8 and 9 (brief p4, section 1.3c), with the three Act I leftovers of that brief.

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
| M25 | (2026-11-10, the coordinator's brief p3b for the user) Map tools #149 is adopted: `maptools.lock` at 7cdfe4d (SourceSharp.MapFormats 1.0.5, MapTools 1.0.2), the kit asks for `door_sill floor`, and multi-cell rooms join the levels: a footprint in the room name, 2x2 halls from level 2, 2x2 and 3x3 grottos from 6, 2x2 flooded galleries from 8, at most one at 2-4, two at 6-9, three at 11-14 (`DepthTable.BigRoomsAt`). Door slots are adopted in the tools' kit but not yet used by Descent (section 3.2). |
| M27 | (2026-11-10, the coordinator's brief p3c for the user) Act I's third review: the 2x2 halls become set pieces (a loading dock, a plinth yard, a gallery over the doors) dressed by their theme (track through level 2's yard, a timber headframe at 3, machines and an overhead crane at 2 and 4); tall rooms hang lamps on long cords; one door frame per theme (level 1 the farm frame, 2 a steel channel with planks across its thresholds, 3 a timber set, 4 a heavy steel frame with the stripe on its head); the boss arenas move to the 1024 kit as 2x2, 3x3 and 4x4 rooms that open only where their row joins them, and the oversized kits go. |
| M28 | (user, 2026-11-10, relayed by the coordinator during brief p4) "dont need to keep floor totally clear for rocket jumping": in any room with jump spots the dresser may put ordinary clutter on the floor (crates, carts, machines, heaps, not only low pieces). Kept clear only what a jump physically needs: the take-off patch under or beside each ledge (about 128 across, `RoomDresser.TakeOff`), each ledge's landing floor and the air between them, and the giant's lane door to door. A jump room's brushes still keep §4.1's clear air, measured by `RoomLint.CheckJumpClearAir`. Applies to Acts I and II. |
| M26 | (2026-11-10, same brief) Act I's second review: level 1's way in is the Collar's concrete with the town's rock as one band; level 1 hangs boards, a clock and signs on its walls; level 2's track is one line through the tunnels and yards, meeting at the cell faces, with curves and buffer stops, and concrete stairs and square piers; level 3 is broken by a grey seam, paler timber, a darker floor and a cool fill; level 4 keeps one hazard stripe at door heads and deck edges, under paler sodium. |
| M29 | (user, 2026-11-13, relayed by the coordinator in brief p6) "build the miner lamp (which can have brightness increase and range increase from skills and itmes) then remeasure". The levels are not relit until the real lamp (HE10) is measured in game; its brightness and range upgrades are the game-logic session's. Measured in section 1.3d (brief p6). |
| M30 | (user, 2026-11-14) The base lamp is dimmer. The coordinator put "a dimmer base of about 480 range with upgrades reaching 720-1,080, so the levels keep their look" (brightness a minor stat) against keeping 720; the user answered "yes". The game-logic session sets `lamp.json`; the levels are reshot at the new base and its ceiling before the brightness table for levels 2 to 9 and 11 to 14 is set (the table shown to the user: levels near what they measure in game now, level 9 from 17 to about 20, Act III from about 16 down to 9, level 1 = 100). |

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

- **The tools refused the plan's door.** At 9d600ed a door is centred up the
  cell face and must stand on the floor, which forces `door_height` to
  `cell - 2 x wall` (960 on a 1024 cell) and so allows no room lower than its
  cell. Map tools #149 (7cdfe4d, `maptools.lock` since 2026-11-10) added the
  kit key `door_sill floor`, which stands every door on the floor at any
  height; every Descent `info_room` writes it (`RoomKit.SillKey`), and the
  rooms build with the locked tools (task H4, done).
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
the dressing, branch `descent-mine-plan`, map tools at 7cdfe4d (the kit's
`door_sill floor`), debug build, three seeds a depth. Logs and the review of every sheet: `.scratch/p3-merge/` (untracked).

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
| Mean (seeds 1, 2, 3) | 0.2281 (0.213, 0.240, 0.231) | 0.1856 (0.181, 0.187, 0.189) | 0.1682 (0.169, 0.164, 0.172) | 0.1429 (0.139, 0.144, 0.146) |
| Scaled | 100 | 81.4 | 73.7 | 62.6 |
| Target (section 6) | 100 | 85 | 70 | 60 |
| Most clipped | 0.0094 | 0.0131 | 0.0161 | 0.0010 |

After the second review (M26, 2026-11-10, `.scratch/p3b-REVIEW.md`):
level 2 sits 3.6 under its target, 3 and 4 just over theirs, with more,
weaker lamps (level 2's floods 140 every 288, level 3's cool fill, level
4's two work floods at 100). Before it (step 2, map tools 7cdfe4d) the row
read 100 / 77.7 / 62.6 / 55.9. Level 3 spends 412 to 426 map entities, up
from 167 to 171 with the cool fill's lights; every level of 2 to 4 holds a
2x2 hall.

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

### 1.3b Measured in the third review (brief p3c, 2026-11-10)

Branch `descent-mine-plan`, map tools 7cdfe4d, debug, three seeds a depth,
four depths built at once on a loaded box (`.scratch/p3c-build.sh`; review
log `.scratch/p3c-REVIEW.md`).

**Libraries.** Act I's libraries dress in 100 to 290 s each and pack in 36
to 175 s, to 242 MB (level 1) and 548 to 634 MB (2 to 4); the larger rooms
and their dressing roughly doubled packs 2 to 4 (390 MB before).

**Levels** (`make descent-budget`): map entities 105-131 / 167-182 /
227-241 / 268-287 (levels 1 to 4), brushes 729-790 / 1650-1899 / 2192-2255 /
2350-2404; level 3 from 412-431, the cool fill one wide light a cell
instead of one every 384 (its lights 337 of 407 classes before). The
brush door frames cost one map entity per joined socket per room.

**Brightness** (`make descent-brightness`, at 3c68dd05b; now also every
room's view under 2 % white, which level 1's shaft foot failed at 0.043):

| Level | 1 | 2 | 3 | 4 |
|---|---|---|---|---|
| Mean (seeds 1, 2, 3) | 0.2162 (0.201, 0.229, 0.219) | 0.1884 (0.178, 0.189, 0.198) | 0.1634 (0.168, 0.163, 0.160) | 0.1313 (0.134, 0.129, 0.132) |
| Scaled | 100 | 87.1 | 75.6 | 60.7 |
| Target (section 6) | 100 | 85 | 70 | 60 |
| Most clipped | 0.0065 | 0.0091 | 0.0113 | 0.0004 |

Level 1's spawn view 0.0132 white (mean 0.222), still its brightest. Level
3 sits over its target: below about 75 its tall rooms read black.

**Boss levels** on the 1024 kit (`make descent-reach` and `descent-budget`
at depths 5, 10, 15, seeds 1-3, all pass): libraries pack in 9.9 s / 26 MB
(5), 17.0 s / 33 MB (10), 14.4 s / 34 MB (15); levels link in 3 to 9 s to
maps of 2 to 3 MB with 45, 65 and 85 map entities.

**The Bore's track on a 3x3** (interior 3040 square, the four outer
pillars at 896 from the middle, radius 144, the roof pillar radius 224): a
spiral one lap round the arena 1232 from the middle (clear of the outer
pillars by 192 for the Tank) is four legs of 2,464, then a fifth of about
860 in to the roof pillar: about 10,700 units, over section 7's 9,000. The
spiral still fits; at section 7's 30 units a second the moving time is
about six minutes instead of five, or the legs run 1,800 with the stations
nearer the corners.

### 1.3c Measured in Act II's first pass (brief p4, 2026-11-10)

Branch `descent-mine-plan`, map tools 7cdfe4d, debug, three seeds a depth, eight depths built at
once on a box loaded to 40-65 (`.scratch/p4-build.sh`; review log `.scratch/p4-REVIEW.md`, three
rounds and two more on levels 8 and 9).

**Libraries** (dress and lint, then pack; loaded box): level 6 358 s / 248 s, 975 MB; 7 158 s /
370 s, 954 MB; 8 513 to 1,715 s / 348 to 438 s, 1,279 MB; 9 531 to 1,657 s / 306 to 359 s, 1,154 MB (the longer under load 60) (79 to 86 rooms each). Act
II's packs are about twice Act I's: the grottos (2x2, 3x3) and the standing water.

**Levels**: link 8 to 30 s; maps of 17 to 37 MB.

| Level | 6 | 7 | 8 | 9 |
|---|---|---|---|---|
| Map entities (of 1,536) | 298-370 | 764-816 | 421-496 | 395-503 |
| Brushes (of 8,192) | 1,343-1,413 | 1,663-1,796 | 2,420-2,523 | 2,710-2,884 |

Level 7 spends most on light: a string of caged bulbs every 288 along every wall and a cold fill
every 512, each a `light`.

**Brightness** (`make descent-brightness`, now depths 1-4 and 6-9; level 5's boss row is left
out until its arena is themed):

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Mean (seeds 1, 2, 3) | 0.2162 | 0.1880 | 0.1710 | 0.1314 | 0.0858 (0.088, 0.086, 0.083) | 0.0782 (0.079, 0.078, 0.078) | 0.0716 (0.069, 0.074, 0.072) | 0.0564 (0.063, 0.047, 0.059) |
| Scaled | 100 | 86.9 | 79.1 | 60.8 | 39.7 | 36.2 | 33.1 | 26.1 |
| Target (section 6) | 100 | 85 | 70 | 60 | 42 | 35 | 30 | 25 |
| Most clipped | 0.0065 | 0.0091 | 0.0131 | 0.0004 | 0.0013 | 0.0000 | 0.0003 | 0.0000 |

Every room view under 2 % white. Level 3 rose from 75.6 to 79.1 with its great halls' lantern
pools (brief p4, Part A); levels 1, 2 and 4 are where they were.

**Reach** and **budget**: ok at all 24 levels. The reach gate and the preview's movement have no
water (`PlayerMoveSim`, `Reachability`): a Heavy walks a pool's or a flooded room's floor as dry
ground, which the 12 to 48 of water Act II stands is in game too (wading, never swimming); deeper
water would need the simulator to swim. The preview draws a water surface as a flat translucent
sheet of its material's fog colour and draws no map fog, so Act II's water reads black on the
sheets and its fog is checked only by the budget gate's controller check.

**The map tools' cubemap fix-up** (7cdfe4d) throws on a material with `$envmap` in a room with no
`env_cubemap` when a side has no winding (`CubemapFixups.FindClosestCubemap` answers sample 0 of
none): `cp_manor/wood_floor01` stopped level 6's pack. Every Act II material was read from the
VPKs for `$envmap` (`.scratch/p4-vmt.py`).

### 1.3d Seen in the game (brief p5, 2026-11-11)

Levels 1-4, 6-9 (seed 1) and the grey 5, 10, 15 loaded in a tf2_descent
listen server (debug, 6ab691c1d), `make descent-install` linking each into the
mod's `pool/maps`, and shot from the preview's own cameras at fov 90
(`.scratch/p5-shots/`, 196 shots). Two linker behaviours the preview hides
broke every level in game, and are worked round here until the map tools fix
them (H7, H8):

- **Turned rooms drawn from behind** (H7): `ssmap link` writes each face's
  side flag inverted in every room the layout turns; the engine culls by it,
  so most of a turned room's walls showed the sky or black. Level 1: 1,063 of
  3,312 faces, all in turned rooms. `tools/descent-face-sides` sets the flag
  from the face's node after each link.
- **Joined doorways shut** (H8): the link keeps a joined socket's plug faces,
  flagged nodraw, and the engine draws them. The plug material is now
  `$no_draw`.

Brightness, mean Rec.709 luma of the sRGB pixels over each camera's 4:3
frame, the same measure on the preview's own tile beside it (the preview's
`checks.lighting.mean`, a lightmap mean, in the first row):

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Preview lightmap mean | 0.2008 | 0.1776 | 0.1787 | 0.1336 | 0.0882 | 0.0786 | 0.0688 | 0.0630 |
| Preview tiles | 0.340 | 0.234 | 0.180 | 0.121 | 0.104 | 0.075 | 0.056 | 0.043 |
| Game | 0.362 | 0.251 | 0.206 | 0.131 | 0.119 | 0.098 | 0.091 | 0.062 |
| Game with the flashlight | | | | | 0.128 | 0.124 | 0.109 | 0.077 |
| Game, level 1 = 100 | 100 | 69 | 57 | 36 | 33 | 27 | 25 | 17 |

The game is 6 to 15 % brighter than the preview in Act I and 14 to 62 % in
Act II, probably the fog lifting the blacks and the water drawn as a surface
(the preview draws it black); not measured apart.
Fog works (the controller reaches the client; level 6 measured start 384,
end 2304, density 0.5). The maps are lit LDR (no HDR lump), so the tone map
controller does nothing. The flashlight (`impulse 100` with
`cs_sv_allow_flashlight 1`) lights a small pool. Brush frames (levels 2-9)
take lightmaps three to five times their walls' (level 9's stone trim 0.176
against 0.034 on the same material in the world), so arches read pale in game
as in the preview: every frame `func_brush` carries `_minlight 0.35`
(`BrushFrame.MinLight`, for its faces inside the doorway, which were in the
plug when the room was lit), a floor far above a dark level's walls; not
changed here (a look decision: a per-depth floor, or the doorway faces split
off). Every joined doorway's let-down floor under the plug is lit black (H9).
Eight Act II models placed solid have no collision model (boulder piles, wood
debris, the wooden barrier, `rock006`, candle clusters): the engine loads
them non-solid ("SOLID_VPHYSICS static prop with no vphysics model!", 51 at
level 6, 5 at 7, 39 at 9). They are non-solid in the themes now, and the
palette fact refuses a solid model without collision; the libraries built
before it still carry the old flag until rebuilt.

**With the miner's lamp (brief p6, 2026-11-13, M29).** Levels 1-4 and 6-9
(seed 1) were rebuilt (libraries and levels) and shot again in a tf2_descent
listen server (debug, `abb6628f3`, unhosted: the player is a hero, red
soldier) from the same 14 cameras, each twice: the hero's lamp off and on
(`tf2_descent_lamp 1 off|on`, the server's own switch; `tf2_descent_lamps`
reports the wire lit before every lit pass). Every horde robot is killed
first (their fire set the hero alight and killed him five times in the first
pass, and a dead hero's lamp is dark). Shots: `.scratch/p6-shots/` (504, index
`index.tsv`). The measure is p5's (mean Rec.709 luma over the 4:3 centre),
plus the share of pixels under luma 0.02 (black crush) and the mean of the
frame's lower half (the floor). Levels 4 and 6-9 were shot again with the
lamp at radius x1.5 and x2 (1080, 1440) and brightness x1.5 and x2 (below).
Two fixes the game-logic session asked for came first: `make descent-install`
copies the level into `pool/maps` (its links failed the setup check every
build runs, and `make setup` deleted them), and `tools/descent-face-sides`
writes the rewritten map's checksum into the `.map2d` (every level had shipped
a stale one; `LevelReader` does not check it today).

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| p5, no lamp (same measure, p5's shots) | 0.359 | 0.251 | 0.206 | 0.131 | 0.119 | 0.099 | 0.091 | 0.062 |
| Lamp off | 0.357 | 0.251 | 0.205 | 0.131 | 0.111 | 0.098 | 0.089 | 0.062 |
| Lamp on | 0.495 | 0.366 | 0.297 | 0.204 | 0.213 | 0.201 | 0.191 | 0.242 |
| Radius x1.5 | | | | 0.261 | 0.272 | 0.257 | 0.231 | 0.330 |
| Radius x2 | | | | 0.309 | 0.322 | 0.305 | 0.264 | 0.396 |
| Brightness x1.5 | | | | 0.226 | 0.237 | 0.223 | 0.206 | 0.279 |
| Brightness x2 | | | | 0.245 | 0.256 | 0.240 | 0.218 | 0.307 |
| Lamp off, level 1 = 100 | 100 | 70 | 57 | 37 | 31 | 27 | 25 | 17 |
| Lamp on, level 1 lamp off = 100 | 138 | 102 | 83 | 57 | 60 | 56 | 53 | 68 |
| Radius x1.5 / x2 | | | | 73 / 86 | 76 / 90 | 72 / 85 | 65 / 74 | 92 / 111 |
| Brightness x1.5 / x2 | | | | 63 / 69 | 66 / 72 | 62 / 67 | 58 / 61 | 78 / 86 |
| Black crush, off / on (% of pixels) | 0.1 / 0.1 | 0.2 / 0.1 | 0.2 / 0.1 | 0.8 / 0.3 | 2.7 / 0.4 | 7.5 / 0.3 | 2.1 / 1.0 | 12.0 / 1.9 |
| Floor luma, off / on | 0.363 / 0.606 | 0.272 / 0.451 | 0.162 / 0.330 | 0.107 / 0.224 | 0.076 / 0.233 | 0.047 / 0.187 | 0.066 / 0.237 | 0.045 / 0.289 |

The scaled runs' black crush stays within 0.4 points of the base lamp's; their floor luma, x1.5 / x2 radius, then x1.5 / x2 brightness: level
4 0.308 / 0.369, 0.261 / 0.291; 6 0.321 / 0.387, 0.271 / 0.303; 7 0.255 /
0.307, 0.217 / 0.241; 8 0.283 / 0.314, 0.259 / 0.275; 9 0.409 / 0.495, 0.343 /
0.385.

Lamp off matches p5 to 2 % on every level but 6 (0.111 against 0.119): there
two cameras now see other rooms (cam6 and cam7; the rebuilt library dresses
them differently), and the other twelve agree to about 3 %. The rebuilt libraries 6,
7 and 9 load with no "SOLID_VPHYSICS static prop with no vphysics model" line
(51, 5 and 39 in p5).

- **The lamp equalises Act II.** Lit, levels 4 and 6-9 all land at 53 to 68,
  and level 9 (narrow crypts, walls close) is the brightest of them: near the
  hero the lamp, not the level, sets the brightness. The level's own light
  decides only what is seen beyond about 500 units, and the colour.
- **Which level needs it.** 1-4 read without it. 6 is dim but readable
  (crush 2.7 %). 7 is the first that needs it: its caverns' floor is 0.047
  and 7.5 % of the frame is black. 8 is borderline (floor 0.066). 9 is
  unplayable without it (12 % black) and comfortable with it.
- **It washes out the level's colour near the hero.** Level 7's blue rock
  turns warm grey within the lamp (and the far walls too at radius x2), level
  8's green survives only beyond the lamp and round its lanterns, level 9's
  candle brown becomes neutral grey cobble at the hero's feet. Walls within
  about 150 units clip to near white (sand or beige) at every depth; the door
  arches, already pale (`_minlight 0.35`), go white.
- **What it looks like.** No cone: the world's lightmaps ignore a dlight's
  cone angles (stock engine, `gl_lightmap.cpp` `AddSingleDynamicLight` only
  skips a surface facing the light's own direction; the angles reach models
  only, `lightcache.cpp`), so on walls and floor the lamp is a hemisphere in
  front of the hero, a broad pool with no edge that fades with distance. The
  ceiling is unlit whenever the view pitches down (it faces along the light).
  Props and barrels take the cone. Water (7, 8) takes no lamp light: the
  surface stays dark, and level 8's lit floor reads as brown mud.
- **Range matters more than brightness.** The world dlight's light at
  distance d is the colour times 2^exponent times ((radius / d)^2 - 1) / 256
  (the same file; maps of version 20), so a larger radius brightens every lit
  point as well as reaching further: x1.5 radius gives at least 2.25 times the
  light everywhere, more than x2 brightness, and the shots agree (radius x1.5
  beats brightness x2 on every level). Brightness x2 buys only 14 to 27 % more
  luma because the near walls already clip; to the eye it brightens what was
  lit and leaves the far half of the room dark. Range x2 lights the far floor
  and the far walls of mid-sized rooms (and greys them). How brightness was
  scaled: the colour bytes are clamped to 255 (`shell_standalone_client.cpp`,
  `DynamicLightUpdate`) and 255/226/178 is at the cap, so x2 is exponent 4 and
  x1.5 is exponent 4 with the colour at three quarters (191/170/134).
  `DynamicLight` has no intensity of its own, only colour and exponent. No
  radius cap in this path; the falloff scale is capped at 2, which binds only
  within about 32 units.
- **Frame rate**, lamp off / on, medians of 10 alternations of 4 s, fps_max 0,
  debug, `abb6628f3`: level 4's hall 283 / 262 (load 21 to 33), level 7's
  grotto 369 / 332 (load 20). Under a box load of 50 to 135 the cost looked
  larger and noisier (229 / 193 and 331 / 138 at the same spots).
- **A party's lamps** were not shown: a bot is never a hero
  (`HeroComponents`), so another hero's lamp needs a second client.

**The brightness table, given the lamp** (a recommendation, not applied): in
game, the level's own light (lamp off), level 1 = 100: 2 70, 3 55, 4 40, 6
32, 7 27, 8 24, 9 20, 11 16, 12 13, 13 11 (lava glow on top), 14 9. Act I
stays near what it measures, since the lamp is on from depth 1 and lifts 4 to
57. Act II stays near what it measures too: near the hero it reads right
with the lamp, and the dark beyond the lamp is the mood. Level 9 rises from
17 to 20 because 12 % of its frame is black beyond the lamp. Act III may fall
to about half of 9 because the lamp guarantees the floor near the hero (lit
floor luma 0.18 or more on every level measured); 11 to 14 are not built, so
those four are extrapolated. The lamp is the bigger lever: at base it already
washes the levels' colour near the hero, so a dimmer base lamp with its
upgrades spent on range keeps more of each level's look than more brightness
would.

### 1.3e Act II's second look pass and the logic session's asks (brief p9, 2026-11-14)

Branch `descent-mine-plan` from `c7b689ffe`, map tools 3551fa8, debug, box load 50 to 150 throughout. Builds:
`.scratch/p9-round.sh` (r0 the base, r1 to r4 the passes; previews, logs and level files kept in
`.scratch/p9-rN/`); review log `.scratch/p9-REVIEW.md`; shots `.scratch/p9-shots/dNN/<run>/` (the 14
preview cameras in game, lamp off, robots killed first; `lamp.json` is still radius 720 here, so no
lamp-on pass at M30's base).

**The brightness gate measures what a player sees** (`tools/descent-gate brightness`, Part A). Each
level's figure is the mean over the preview's room views (the spawn view and every room camera) of
each tile's mean Rec.709 luma after re-encoding its sRGB pixels to a pure 2.2 power (the game's LDR
path multiplies texture and lightmap in gamma space; the preview applies the sRGB curve, whose
linear toe draws the darks darker than the game), the label band and the orange scale figure left
out; a depth's figure is the mean over its seeds, scaled by level 1's (`LEVEL1_VIEW` 0.3308, r0).
Candidates against p6's in-game shots (the same cameras, seed 1, lamp off), level 1 = 100:

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Game (p6) | 100 | 70 | 57 | 37 | 31 | 27 | 25 | 17 |
| Lightmap mean (the old gate) | 100 | 89 | 89 | 64 | 44 | 39 | 34 | 32 |
| Lightmap median | 100 | 85 | 77 | 64 | 47 | 40 | 28 | 16 |
| Floor lightmap mean (faces up) | 100 | 97 | 87 | 57 | 38 | 33 | 30 | 27 |
| Albedo (texdata reflectivity) x lightmap at 2.2 | 100 | 62 | 65 | 44 | 36 | 37 | 27 | 30 |
| Preview tile, sRGB luma (p5's) | 100 | 69 | 52 | 31 | 27 | 21 | 15 | 12 |
| Preview tile at 2.2 | 100 | 72 | 56 | 37 | 34 | 28 | 21 | 19 |
| **Preview tile at 2.2, figure out (chosen)** | 100 | 71 | 56 | 36 | 33 | 27 | 20 | 18 |

No lightmap statistic can tell level 2 from 3 (the same light, darker materials); a fitted mapping
from the lightmap mean cannot either. The chosen figure is within 2 of the game at every level but 8,
where the preview draws the water as a dark sheet (5 under). The targets are the in-game table of
M30: 100 / 70 / 55 / 40 / - / 32 / 27 / 24 / 20, and 16 / 13 / 11 / 9 for 11 to 14 (5, 10 and 15
have none until their arenas are themed); tolerance 3 points: r0's seeds 1 to 3 read 102.4 98.2 99.4
/ 74.4 72.8 77.7 / 57.0 57.0 56.6 / 37.2 40.7 38.5 / 34.0 33.7 32.8 / 27.4 26.2 27.2 / 20.4 22.9 19.0 /
18.3 16.8 18.9, a pooled standard deviation of 1.6 a seed, 0.9 for the three seeds' mean the gate
holds; 3 is three of those. The order still falls strictly (E6). `tools/descent-room-light` measures
every room the same way (below).

The final build (seeds 1 to 3; levels 1 to 4 at r3, 6 to 9 at r4 and r5, every level from the tip's
code; `.scratch/p9-final/`, `.scratch/p9-gate-*.log`): brightness ok, reach ok, budget ok at all 24.

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Gate, r0 (the base, seeds 1-3) | 100 | 75.0 | 56.8 | 38.8 | 33.5 | 26.9 | 20.8 | 18.0 |
| Gate, final | 100.5 | 69.1 | 57.4 | 39.8 | 32.3 | 27.4 | 24.7 | 22.1 |
| Seeds, final | 98.3 102.9 100.5 | 70.7 69.6 66.9 | 59.6 58.4 54.1 | 38.8 41.2 39.3 | 34.0 31.0 31.9 | 28.5 26.5 27.1 | 25.7 24.9 23.5 | 22.5 21.5 22.3 |
| Target (M30) | 100 | 70 | 55 | 40 | 32 | 27 | 24 | 20 |
| Map entities, final | 146-164 | 180-190 | 243-251 | 257-278 | 300-372 | 309-320 | 439-504 | 455-573 |
| Map entities, r0 | 107-136 | 175-190 | 244-246 | 266-277 | 299-374 | 765-816 | 423-498 | 402-505 |

Level 1's rise is the lit slab in each joined doorway (one entity each); level 9's the candles' lights.

**The look** (Part B; before and after shots in `.scratch/p9-shots/`):

- **Door frames take their room's light.** `BrushFrame.MinLight` per theme (Act I 0.35, level 2 and 4
  0.3, 6 0.25, 7 0.11, 8 0.12, 9 0.06): a frame's lightmap over its level's wall median, seed 1, was
  1.72 / 1.79 / 2.11 / 2.71 / 3.93 / 8.14 at levels 2, 4, 6, 7, 8, 9 (`.scratch/p9-frames.py`);
  final (seed 1): 1.57 at 2, 1.51 at 4, 1.99 at 6, 1.74 at 7, 1.54 at 8, 1.63 at 9 (over the same material in
  the world: 1.82, 1.66, 1.44, 1.39, 2.11, 1.22). Level 6's walls fell with its dimmer lamps after its
  floor was set; it stays the highest.
- **The doorway's black strip.** A slab over the let-down floor under the plug, a unit proud of the
  sill: part of a brush frame (`BrushFrame.SillSlab`, the post's surface), and for the farm frame
  model of level 1 a non-solid `func_brush` of its own with Act I's frame light (one entity per joined
  socket: level 1 links 146 to 164 map entities, was 107 to 136). The farm frame's head still draws dark
  in game (the model is lit from inside the plug): H9.
- **Water that reads as water** (levels 7 and 8). Tried in game on patched copies of the installed
  maps: a reflecting water (`_rt_WaterReflection`, entities not reflected) with a short green fog over a
  wet rock bed reads as murky water with the gallery mirrored in it and the bed showing near; refraction
  alone over gravel reads as a flat green floor; TF2's water_murky over the mud read as a mud floor.
  Level 8's water is `descent/sunken_water` (made from water_murky: fog 30 44 30 to 64, reflection
  tinted .6 .7 .6) over a floor of `descent/sunken_bed` (TF2's rockfloor003a without its `$envmap`);
  level 7's `descent/dripstone_water` (water_well's blue, fog to 80, reflecting). A bed brush under
  the water was tried and dropped (a giant's flat walk reads any solid above the floor as a wall).
- **Fog that fades far rooms** rather than blanking them: the grey panels in level 6's doorways were
  the next room washed to the fog's grey (58 60 66). Each Act II fog is now near its level's darks and
  starts later, still closing in from level 4's: 6 `22 23 27` 640-2400 0.55, 7 `16 20 28` 576-2240 0.6,
  8 `16 22 16` 512-2048 0.65, 9 `12 10 9` 448-1856 0.7 (tried in game: `fog_override`, shots
  `fog-f1`, `fog-f2`, `fog-new`).
- **Cave walls.** Rough rock beside each door of cavern c and over its frames to the roof, and over every
  socket of a grotto, in the theme's larger wall in rooms of several cells (`CavernShape.RockAround`,
  `RockOverDoor`). Cavern d keeps its walls: they are its rounded corners and its necks, and rock over
  its necks took its one stalactite and cut a giant's way through it.
- **Level 7's entities**: one row of caged bulbs every 448 (was every 288, a row every 640 up the tall
  walls) and one cold fill a cell (was every 512): 309 to 320 map entities (764 to 816).
- **Level 9's candles**: `RoutePools`, candle clusters on the floor by the walls in 3 to 5 pools along
  each room's route and round the naves (light 70, 128 to 640), and the arches at 0.06.

**The logic session's asks** (Part C; plan_descent_logic Y4, Y5 and the hazards):

- **A light level per room** (Y5). `descent-level` renders every placed room's own corner camera
  (`.roomcams`), and `tools/descent-room-light` writes, into the level's `.map2d`, a section `LITE` that
  every reader of today skips (Map2dReader skips tags it does not know): uint32 version (1), uint32 count
  (the `.map2d`'s room count), then per room in the ROOM section's order (a `LevelRoom.Id`) float32
  light (level 1 = 100 on the gate's scale; NaN unmeasured) and uint32 flags (1: dark by design, no
  light entity inside its cells; 2: measured). The game's `LevelReader` reads such a file unchanged
  (checked on a level of r1). The reader change, for the logic lanes: `LevelRoom` gains `float? Light`
  and `bool DarkByDesign`; `LevelReader` finds the `LITE` entry in the `.map2d`'s directory (after the
  96-byte header, 12-byte entries: tag, offset, length), checks version 1 and count, and sets each
  room's two. No room of levels 1 to 9 is dark by design (the `lit` lint lights every floor).
  Final seed 1, rooms measured / light range / mean (the level's figure for comparison): level 1 18 /
  71-142 / 95.3, 2 21 / 56-104 / 71.8, 3 27 / 29-96 / 56.8, 4 27 / 25-51 / 39.8, 6 27 / 20-52 / 31.0,
  7 32 / 17-41 / 25.9, 8 37 / 13-41 / 22.7, 9 37 / 10-31 / 21.4; no room dark by design.
- **Props in the nav** (Y4). Not true as asked: static props reach no room's nav, with the game's
  content mounted or not. The room compile builds each room's nav from its world brushes and its
  entity obstacles (`RoomLibraryCompiler` calls `RoomNavBuilder.Build` over `NavGeometry.FromBsp` with
  the hulls of `NavModelBounds.PropModels`, which lists door and physics entities only); only
  `LevelNavFromBsp` (`ssmap nav3d` on a whole map) takes the static prop lump. Measured on r1's linked
  levels (`.scratch/p9-propnav`): level 1 seed 1 holds 256 solid static props, and the nav is open 32
  above the origin of every oil drum (42 of 42) and mining crate (28 of 28), inside them; level 3 seed 1,
  51 of 51 crates. Without the content (the mod's gameinfo in a folder whose content paths hold
  nothing) the pack refuses all 70 of 70 rooms of depth 1 and of depth 3, so there is no build to
  compare. Nothing is missing from the build line; the fix is the map tools' (H10).
- **No pack in sight of the way in.** The arrival rooms held no pack; the rooms beside them did:
  level 3 seed 1's nearest was 846 from the arrival, in the gallery one door away. The layout now puts
  only quiet rooms beside the way in (twins of the first drift, bend, tee, cross and stub with no pack
  spawn, the codec's flag `q`), nowhere else, and a boss level's row takes the quiet drift as a lobby
  between its way in and its antechamber (whose packs were in the arrival's line of sight at all three
  boss depths). Nearest pack to the arrival, horizontal distance, seeds 1, 2, 3 (`.scratch/p9-packs.py`,
  before r0, after r1's levels):

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Before | 1008 801 961 | 737 785 1058 | 846 673 801 | 638 925 830 | 688 547 557 | 740 750 705 | 716 761 781 | 609 939 881 |
| After | 1864 1225 1131 | 1142 1539 1404 | 1224 1216 840 | 873 1429 1523 | 1205 1219 460 | 1739 1152 955 | 1269 1137 1475 | 1268 1766 1482 |

  Every one after is two doors away or more; level 6 seed 3's 460 stands in the next cell with a wall
  between (no door), level 3 seed 3's 840 in a hall two doors away. Facts: `DescentArrivalTests` (no pack in
  the way in or a room one door from it, every depth 1 to 15, 20 seeds), and the 200-seed sweep refuses a
  quiet fallback.
- **Hazard volumes** (M1 of the logic plan). `RoomBuilder.WithHazard(kind, mins, maxs)` writes a
  `trigger_multiple` nothing fires (spawnflags 0), named `tf2_descent_hazard_<kind>`: a global name the
  link keeps as written, which `HazardService` finds with `FindAllByName` and reads by its origin and
  bounds (`EngineHazardWorld.MapVolumes`). A theme's `PoolHazard` puts one over each pool's basin; no
  level 1 to 9 has one (the plan puts no lava or slime above the Sump); level 12's grey
  (`RoomTheme.GreySump`) holds slime in its pools as the proof: its seed 1, linked as the host links it,
  carries 6 `tf2_descent_hazard_slime` volumes (`trigger_multiple`, `*23` and on).

**In game** (brief p9, Part D: the final seed-1 levels installed, debug at 96e0f6e0e, the 14 preview
cameras, lamp off; mean Rec.709 luma over the 4:3 centre as p6; shots `.scratch/p9-shots/dNN/final/`,
before `.../r0/` for 6 to 9 and p6's for 1 to 4; `.scratch/p9-shots/summary.tsv`):

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Before (p6 / r0), level 1 = 100 | 100 | 70 | 57 | 37 | 31 | 27 | 25 | 17 |
| Final, luma | 0.348 | 0.244 | 0.219 | 0.126 | 0.097 | 0.100 | 0.120 | 0.079 |
| Final, level 1 = 100 | 100 | 70 | 63 | 36 | 28 | 29 | 35 | 23 |
| Black crush % before / final (6 to 9) | | | | | 2.8 / 5.9 | 7.5 / 7.2 | 2.0 / 1.9 | 11.9 / 5.4 |
| Floor luma before / final (6 to 9) | | | | | 0.077 / 0.068 | 0.047 / 0.053 | 0.066 / 0.124 | 0.044 / 0.070 |

The game and the gate part at level 8: the reflecting water draws the lit walls into the floor, which
the preview draws as a dark sheet, so level 8 reads 35 in game for the gate's 25 (target 24). Level 6
reads 28 for 32: its darker fog takes far rooms to black (crush 2.8 to 5.9 %). Level 3's cameras moved
with its new layouts (the quiet rooms change every level's draw), so its 63 is not the same views as
p6's 57. Level 9 rose from 17 to 23 with its candle pools, black crush halved. Not retuned here: the
lamp-on reshoot at M30's base comes next, with `lamp.json` at 480.

**The lamp at M30's base, in game (brief p10, 2026-11-15).** Branch `descent-mine-plan` at `91a97e9e1`,
debug, `lamp.json` radius 480, exponent 3, colour 255 226 178. The levels are p9's final seed-1 builds
(the installed `.map2d` files carry `LITE`, and each installed `.bsp` and `.map2d` is byte for byte its
build in `bin/debug/descent-rooms`), so nothing was rebuilt. The 14 preview cameras, robots killed first,
in five passes: lamp off; lit at `lamp_radius_pct` 0, 50 and 125 (radius 480, 720 and 1,080); and, on
levels 7 and 9, radius 0 with `lamp_brightness_pct` 100 (`tf2_descent_contrib 1 <stat> <n>` after
`tf2_descent_contrib_clear 1`; `tf2_descent_lamps` showed the hero alive and the wire at the radius and
brightness wanted before each pass and twice during it). Shots `.scratch/p10-shots/dNN/<pass>/` (476,
index `index.tsv`), measured as p6 (`.scratch/p10-measure.py`, `p10-table.py`). Lamp off is p9's
in-game figure exactly (level 1 0.3477 both; 100 / 70 / 63 / 36 / 28 / 29 / 35 / 23), so the passes
differ only in the lamp.

| Level | 1 | 2 | 3 | 4 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|
| Lamp off, level 1 = 100 | 100 | 70 | 63 | 36 | 28 | 29 | 35 | 23 |
| Radius 480 (the base) | 122 | 90 | 81 | 49 | 45 | 40 | 58 | 49 |
| Radius 720 (the old base) | 144 | 103 | 90 | 59 | 57 | 51 | 69 | 67 |
| Radius 1,080 (the ceiling) | 173 | 121 | 104 | 76 | 75 | 66 | 83 | 92 |
| Radius 480, brightness x2 | | | | | | 47 | | 61 |
| Black crush %, off / 480 / 720 / 1,080 | 0.0 / 0.0 / 0.0 / 0.0 | 0.2 / 0.1 / 0.1 / 0.1 | 0.3 / 0.1 / 0.0 / 0.0 | 2.1 / 0.3 / 0.2 / 0.2 | 5.9 / 2.9 / 2.4 / 2.1 | 7.2 / 1.0 / 0.3 / 0.1 | 1.9 / 0.9 / 0.7 / 0.5 | 5.4 / 1.5 / 0.8 / 0.5 |
| Floor luma, off / 480 / 720 / 1,080 | .344 / .474 / .594 / .752 | .254 / .372 / .434 / .519 | .179 / .293 / .348 / .419 | .100 / .170 / .226 / .307 | .068 / .162 / .223 / .305 | .053 / .115 / .164 / .234 | .123 / .256 / .307 / .362 | .070 / .212 / .300 / .418 |

At brightness x2 (radius 480) black crush is 0.7 % on level 7 and 1.3 % on 9, floor luma 0.151 and
0.278. The old base lands near p6's lamp-on figures for the levels as they stood then (4 59 for 57,
9 67 for 68; 7 51 for 56, its lights cut in p9).

- **Playable at the base, level by level.** 1 to 3 read without the lamp; it only warms them. 4 (49,
  crush 0.3 %) and 9 (49, floor 0.212: the crypts' walls are within reach) are comfortable. 8 reads 58,
  the brightest of Act II lit, because its lit mud floor and the reflecting water carry it; one camera
  (cam6) has a wooden step filling half the frame, which goes near white when lit. 6 (45) lights the
  floor near the hero, but 2.1 to 2.9 % of its frame stays black at every radius: that is the far rooms
  in its fog (and cam13's drop), beyond any lamp. **Level 7 first wants an upgrade**: lit it reads 40,
  the lowest, and its floor 0.115, the darkest lit floor of any level (half level 9's), because its
  caverns and grotto are wider than 480 reaches; the walls of the grotto stay as dark with the lamp as
  without (`d07/r0/cam1`). It is playable (crush 1.0 %), not comfortable.
- **What the ceiling buys over the base, to the eye.** Act II goes from 40-58 to 66-92 (x1.4 to x1.9),
  the floor near the hero doubles, and the far floor and the near walls of mid-sized rooms light; the
  walls of the big caverns stay dark. Close walls and stairs clip to white (level 9's cam2 and cam10,
  level 7's cam13), as at 720. Brightness x2 at 480 buys about two thirds of what radius 720 buys
  (7: 47 against 51; 9: 61 against 67) and keeps a little more colour.
- **The level's own colour.** Measured as the mean colour of the frame, and of its upper (far) and lower
  (near) halves. Level 7 stays blue at the base: blue over red 1.18 (1.49 lamp off), the far half 1.31,
  the floor near the hero neutral (1.02); at 1,080 the frame is neutral (0.96) and the near rock warm
  grey (`d07/r125/cam3`); it crosses neutral at about +95 %. Level 8 keeps its green only far off: green
  over red 1.03 at the base (1.21 off), the near half 1.02 (1.32 off), since the lit floor is grey-brown
  mud; 0.98 at 1,080. Level 9 stays candle brown above the floor at the base (blue over red 0.36 in the
  far half, 0.18 off), but its cobbles are grey at any radius (near half 0.67 at 480, 0.78 at 1,080).
- **Frame rate**, level 7's grotto (cam1, 3200 5248), `fps_max 0`, medians of 4 s alternations, debug,
  `91a97e9e1`, the box shared with two other games: off / 1,080 809 / 290 (10 pairs, load 92 falling to 27,
  GPU 99 % at the end); again at load 70 to 80, 261 / 142 (8 pairs). Smaller radii, at load about 20: 480
  678 / 442, 720 771 / 313 (6 pairs each). As frame time the lamp costs about 0.8 ms at 480, 1.9 ms at
  720 and 2.2 to 3.2 ms at 1,080. One more 1,080 run (load spiking to 85, off 169) was thrown out.

**Recommendations** (not applied):

- **The base stays 480.** It lights the floor near the hero on every level (0.115 or more), cuts Act II's
  black to 1 to 3 %, and keeps levels 7 and 9 their colours beyond about one room width.
- **The ceiling comes down from +125 % (1,080) to +90 % (912)** if it is reachable in Act II. At 1,080
  level 9 reads 92, brighter than level 2 lamp off (70) and level 3 lit at the base (81), level 7 loses
  its blue (0.96, past neutral at about +95 %), and the lamp costs about three times its base in frame
  time (2.2 against 0.8 ms in the grotto). If the top upgrades come only in Act III, where the levels are
  darker (16 to 9) and sight darkness starts (`lamp.json` `sight`), 1,080 can stay; measure there first.
- **Level 8's own light: in game 35 to 24** on the level 1 = 100 scale (agreeing with p9's read). Lit at
  the base it reads 58, above 6 (45), 7 (40) and 9 (49). The excess is the reflecting water (the gate's
  figure, which draws the water as a dark sheet, is already 24.7); so the lever is the water's reflection
  (`descent/sunken_water`'s tint), and only failing that the lights, which by themselves would need the
  gate's figure near 17.
- **Level 6's own light: in game 28 to 32** (also p9's read). Its gate figure is 32.3 already; what is
  missing is the far rooms going to black in its fog (crush 5.9 % off, still 2.1 % at 1,080), so the
  lever is the fog (start later than 640, or the colour lifted from `22 23 27`), not the lamps.
- **Level 4: in game 36 to 40** (gate 39.8); minor. Levels 7 (29 for 27) and 9 (23 for 20) are within
  the gate's tolerance of 3 and stay; level 7 is not raised for the lamp's sake, since the lamp's upgrade
  is its remedy.

**Levels 4, 6 and 8 to their light targets (brief p11, 2026-11-15).** The user ruled that these three
levels' own light moves to M30's in-game target. Branch `descent-mine-plan` from `6b09663d7`, debug, seed 1,
the 14 preview cameras, robots killed first, measured as p10 (mean Rec.709 luma over the 4:3 centre, level 1
= 100 at 0.3477; `.scratch/p11-cams.py` sets each camera's game figure beside its gate tile). Rebuilt at
the tip first (`.scratch/p11-round.sh r0`): every level of depths 1, 4, 6 and 8, seeds 1 to 3, came out byte
for byte the installed build, and the shots gave p10's figures again (level 1 0.3477; 4, 6, 8 at 36.3,
27.9, 34.6 lamp off). Shots `.scratch/p11-shots/dNN/<run>-<pass>/` (`before-`, the trials, and the final
`r1-` for 4, `r2-` for 6 and 8).

| Level | 4 | 6 | 8 |
|---|---|---|---|
| In game, lamp off, before / after | 36.3 / 38.0 | 27.9 / 30.0 | 34.6 / 26.0 |
| In game, lamp radius 480, before / after | 48.6 / 50.0 | 44.8 / 46.4 | 57.7 / 51.6 |
| Gate, before / after | 39.8 / 41.5 | 32.3 / 34.3 | 24.7 / 23.0 |
| Gate seeds 1 2 3, after | 40.5 42.9 41.0 | 35.9 33.0 33.9 | 24.0 23.2 21.9 |
| Black crush % lamp off, before / after | 2.1 / 1.9 | 5.9 / 5.1 | 1.9 / 3.6 |
| Floor luma lamp off, before / after | .100 / .105 | .068 / .074 | .123 / .073 |
| Target (M30) | 40 | 32 | 24 |

All three are inside their targets' 3 in game and in the gate, which still passes at every depth (levels 7 and
9 are untouched: their themes, libraries and builds did not change, so they were not reshot; the gate reads
them 27.4 and 22.1 as before). Reach and budget pass at 4, 6 and 8 (map entities unchanged: 257 to 278,
300 to 372, 439 to 504).

- **Level 8: the water's fog, not its reflection.** On the installed map with the material reloaded in game
  (`mat_reloadmaterial`): fog `16 24 16` and tint .4/.48/.4 read 29.4; fog `10 15 10` with tint .28/.34/.28
  read 27.3 and with .5/.6/.5 28.0; fog `6 9 6` 27.2 (black crush 10 %). The tint moves the figure under a
  point; the fog colour moved it six. At `30 44 30` the water drew its fog colour as a lit teal sheet over
  half of every flooded view (the near water read about (22, 34, 20)), since 16 units of water seen from a
  player's eye is mostly fog within a few hundred units. Now `descent/sunken_water` has fog `10 15 10` and
  its reflection tinted `[.5 .6 .5]`, and the Sunken lamps are a seventh under p9's (posts 74, cover and
  pendants 39, fill 15). It still reads as water: dark, rippling, the lanterns and lit walls mirrored in
  its middle distance, and the lamp shows the rock bed under it near the hero (`d08/r2-r0/cam5`). The worst
  camera, cam5 (the chasm's flooded floor, 15.5 over its gate tile), went from 33.5 to 20.9 (gate tile 16.7):
  `d08/before-off/cam5.jpg` and `d08/r2-off/cam5.jpg`. Lamp on it reads 51.6, still the brightest of Act II
  lit at the base, now by less (p10: 58).
- **Why the gate missed level 8.** The preview draws no water surface, so its flooded floors are black
  where the game drew the water's fog colour: per camera the game read 8.9 over the gate, up to 15.5 on the
  flooded views and 0 to 5 on the dry ones. With the water dark the game reads 2.0 over the gate (dry
  views +3 to +5, such as the spawn's walls round the lamp posts). The gate's figure for level 8 was right
  about the level's light; it could not see the water.
- **Level 6: fog cannot do it.** On the installed map with `fog_override`: no fog read 28.0, fog `34 36 42`
  448-2400 0.55 read 28.1, `40 42 48` 384-2400 0.6 28.3, `46 48 54` 512-2400 0.6 28.3 (from 27.9): the
  rooms the cameras see sit inside the fog's start, and the far end of a drift changes little
  (`.scratch/p11-cmp-d06-cam3-fog.jpg`). p10's black is not fog: 28 % of cam13 is the inside of a block
  (below) and the rest is near shadow in the caverns and the chasm. So the Breakthrough's lamps rose a
  seventh over p9's (floor lanterns 206, wall lanterns 155, work lamps 67, fill 31); the fog is unchanged.
  The darkest camera, cam6 (cavern c), went from 17.7 to 19.2 (black 10.6 to 8.3 %), the same look
  slightly lifted: `d06/before-off/cam6.jpg`, `d06/r2-off/cam6.jpg`.
- **Why the gate reads level 6 high.** The game is 6.0 under the gate at seed 1, in two parts. The
  doorway camera (cam13, `DescentLevel.DoorwayCamera`, 320 into the room from a joined door and 96 aside,
  at eye height whatever stands there) stands inside the stope's stair block: the game culls the block's
  faces from inside and draws void (21.5), the preview draws them and reads the tile 63.1. That is 3.0 of
  the 6.0 and an error of the instrument, not of the level (open below). The other 13 views read 3.4
  under their tiles, as level 4's read 2.5 under (and level 1 1.7 over, level 8's dry views 3 to 5 over):
  a per-level residual the gate's 2.2 re-encoding leaves (materials, props), not fog (level 4 without fog
  reads 35.4, its warm fog lifting it 0.9).
- **Level 4: lights.** The Mill's lamps a tenth over p9's (goosenecks 79, work floods 110, pendants 50):
  in game 36.3 to 38.0, the gate 39.8 to 41.5. Because the gate reads level 4 2.5 over the game, 40 in game
  would put the gate at about 44, past its 43; 38 / 41.5 leaves each inside. The darkest camera, cam4 (a
  stub, 23.8 to 25.1): `d04/before-off/cam4.jpg`, `d04/r1-off/cam4.jpg`; to the eye the far wall and the
  floor near the lamps lift a little, the look is the same.

Open: the doorway camera should stand in open air (level 6 seed 1's stands inside a stair block, and the
preview's tile then reads three times the game); level 6 sits 2 under its target in game and 2.3 over in
the gate, level 4 2 under and 1.5 over, so neither can reach its target in game without the gate leaving its
tolerance until the gate reads the game's per-level residual or the camera moves. Tests at `83bfb12e1`:
`make managed-test FILTER='FullyQualifiedName~Descent'` 1,072 passed; `make descent-test` 3,836 passed and
one failed, `HordeNavDataTests.Every_class_reaches_every_room_of_depth_9_seed_1_pools_included`, which
asserts that this tree's built level 9 seed 1 holds a pool room: it holds none (the layout from the d09
library at this code is byte for byte the built one), and this brief did not touch level 9.

### 1.2 Defaults I chose (overrule by editing this table)

| # | Gap | Default |
|---|---|---|
| E1 | Cell size and door kit | Every level: 1024-unit cell, door 208 wide by 192 high, wall depth 16 (section 3.1). Boss arenas are rooms of 2x2 to 4x4 cells (section 3.2). |
| E2 | Libraries | One room library per depth, fifteen in all, each made by the shared shape generators with that depth's theme. The host links one library per level today, and a library carries the level's fog and tone map, which is what makes the darkness a property of the depth. |
| E3 | Layout | The Descent rules module lays levels out (`TF2DescentRules.GenerateLayout`), not the tools' own generator, because boss placement, the low-room rule and the per-depth sizes are game rules. |
| E4 | R19's 60 % jump-room share | Held per act (levels 1-5, 6-10, 11-15), not per level, so level 1 can be hallways and level 9 can be low crypts. |
| E5 | Ladders | Not climbable. Stock TF2 has no ladder movement, so ladders are dressing and every descent is a ramp, stair, drop or lift. |
| E6 | Brightness target | What a player sees, offline (brief p9, section 1.3e): the preview's room views re-encoded to the game's 2.2 gamma, level 1 = 100, within 3 of each level's in-game target (section 6, M30) and falling strictly from each level to the next. Was the mean lightmap brightness as `ssmap preview` reports it, which cannot tell level 2 from 3. |
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
| Navigation voxel | 16 | The tools allow 128 voxels along a cell. Every height and every marker height is a whole number of voxels. |
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

A boss arena is one room larger than one cell on the normal kit (M24, M27):
level 5's 2x2, level 10's 3x3 and level 15's 4x4, each opening only where
its row joins it (`RoomBuilder.WithSockets`): level 5 in at its west wall's
south cell and out at its east wall's north cell, level 10 at the middle of
its west and east walls, level 15 at its west wall's second cell only (the
bottom). An arena with a socket on every perimeter edge packed in 14.8 s /
24.9 MB (2x2), 43.1 s / 65.4 MB (3x3) and 118.6 s / 111 MB (4x4); with only
its row's sockets in 5.8 s / 7.8 MB, 10.5 s / 14.0 MB and 19.0 s / 19.2 MB
(measured 2026-11-10, `.scratch/p3b-arena`). The name codec spells chosen
sockets after their side's letter (`arena_b-e1w1-s-3x3-j.s2.b2`), and the
boss row lays the arena unturned with the arrival and antechamber on its
west socket's row and the exit off its east socket. The other rooms of a
boss level are the normal ones. The 2048, 3072 and 4096 kits are gone.

Open areas wider than one cell elsewhere are the same multi-cell rooms
(M24, M25; map tools 7cdfe4d): 2x2 halls (levels 2-4), 2x2 and 3x3 grottos
(6 on) and 2x2 flooded galleries (8 on).

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
| Arena | `boss_arena` | One per boss depth, a room of 2x2, 3x3 or 4x4 cells (§3.2, §7); level 15's has one socket, since it is the bottom. |

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

**The brightness target.** The level's own light as the game shows it, lamp
off, level 1 = 100 (ruling M30; brief p9, section 1.3e: the gate reads it
offline from the preview's room views, re-encoded to the game's gamma):

| Level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Target | 100 | 70 | 55 | 40 | - | 32 | 27 | 24 | 20 | - | 16 | 13 | 11 | 9 | - |

The boss levels (5, 10, 15) get a target when their arenas are themed. The
gate holds each depth's mean over seeds 1 to 3 within 3 of its target and
falling strictly from each level to the next. The miner's lamp (M9, M29,
M30) lights the hero's surroundings on top. (The first table, on the
lightmap mean's scale: 100, 85, 70, 60, 52, 42, 35, 30, 25, 22, 19, 16, 13,
10, 7.) The lava level (13) is the one to watch: its lava glows, so its fill
light has to be lower than 12's.

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
| Light (in game, M30) | 100: warm white, daylight down the shaft | 70: white with shadowed side bays | 55: lantern pools | 40: sodium yellow, dusty fog |
| Way down | Cage | Cage | Cage | Cage |

**Level 5, the Powder Magazine. Boss: Sir Nukesalot.** A 2x2 arena on the 1024 kit.

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
| Walls (as built, `Themes/ActTwo.cs`, `palettes/d06-d09.tsv`) | `nature/rockwall003` over a pale `winter/rockwall014` band, burnt `cp_manor/wood_beam03_burnt` sets (some fallen in), `cp_manor/wood_floor01` decks | pale `nature/blendrockgroundwallisland_interior02` limestone over wet `nature/cliffface002a` at the foot, `nature/blendrockgroundwallisland` ledges and spires | mossy `nature/rockwall006a` over a green `swamp/concrete/swampcrete001` lining, `swamp/wood/swampwall015` plank decks, rusted `swamp/metal/swampmetal001` | `medieval/stonewall001a` over a burnt `cp_manor/wood_wall002_burnt` wainscot, `medieval/stonetrim001a` columns, bands and tombs, `medieval/cobblestone002` vault |
| Floor | `nature/blendgroundtogravel007` (dark rubble) | `nature/dirtfloor005b`; pools of `descent/dripstone_water` (water_well's, reflecting) standing in the caves | `descent/sunken_bed` (wet rock, TF2's `nature/rockfloor003a` without `$envmap`) under 16 of standing `descent/sunken_water` (water_murky's, green, reflecting); brief p9 | `koth_viaduct_event/viaduct_cobblestone001` |
| Props | boulders and rubble, broken boards, `wooden_barrier01`, level 4's fuse boxes and hazard drums left behind, dead lanterns on the walls, a collapse heaped in a grotto | flowstone columns and stalagmite bosses (`props_mining/rock001`, `rock002`), rock spills, `props_island/stalagtites_med` and `_small` hung from the roofs | hand pumps, drain grates, karst rocks, a rowing boat, open drums, rope | upright coffins, headstones, monuments, laid coffins, cobwebs, a statue or gargoyle on a nave's focus, burnt-down candles |
| Lamps | `props_2fort/lantern001` in 2 or 3 pools a room, far apart; `props_hydro/construction_light` work lamps hung on cables in tall rooms; a cold grey fill | `props_c17/light_cagelight02_on` in one row along the walls every 448, hung under the roof and on cables where the walls' string leaves the middle dark; a cold blue fill, one a cell (brief p9) | `props_swamp/lamp_post001` by the walls, `light_industrialbell01_on` over the middles and the tall galleries' water; a green fill off the water | `props_manor/wall_candelabra_01` on the walls, `chandelier_01` under the vault and on chains in the naves, a `props_medieval/brazier` in each large room, `props_halloween/candle_cluster` in 3 to 5 pools on the floor along each room's route (brief p9); no fill |
| Light (target, in game, M30) | 32: cold grey with warm lantern pools | 27: blue-white | 24: dim green | 20: candle orange in black |
| Way down | Rubble ramp into the cave | Rope-line stair down a shaft | A sump: a drained shaft with a stair | A broken crypt floor |

Level 6's arrival is a camp (M10): a lit, dressed room with a hoist to the
town, opened by the level 5 kill.

**Level 10, the Bore. Boss: Major Crits with a Tank.** A 3x3 arena on the 1024 kit (its track's fit: section 1.3b).

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
| Light (in game, M30) | 16: flicker and red | 13: sick green | 11: red on black | 9: black-green |
| Way down | A waste chute (slide) | A dive into a sump | A fissure drop | A slide of slime into the pit |

Level 11's arrival is the second camp (M10), opened by the level 10 kill.

**Level 15, the Pit. Boss: Captain Punch.** A 4x4 arena on the 1024 kit. `down: none`.

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
| A6 | Every family of section 5, with a one-door variant of every chamber, hall and shaft; the crypt (new); grottos with rounded corners, rough rock along the walls between their sockets, a lumpy roof and stalactites (brief p4). | Act III dressing of them; caverns c and d keep flat walls where their doors leave no span for rock. |
| A7 | Ways in and down by act, the two camps, antechambers. | |
| A8 | Placeholder arenas as 2x2, 3x3 and 4x4 rooms on the normal kit with only their row's sockets (M27); the oversized kits gone. | The Phase 6 arenas, the Bore's track, the breakaway platform. |
| A9 | Act I's four themes (`Themes/ActOne.cs`, `palettes/d01-d04.tsv`), after the third review (M27): a door frame per theme (the farm model at 1, brush frames at 2 to 4), pendants, the halls' set pieces. Act II's four (`Themes/ActTwo.cs`, `palettes/d06-d09.tsv`, brief p4): their own surfaces, lamps, light colour, fog, water and door frames (a broken timber set, a rock arch, a rusted steel flood door, a carved stone arch). | Depths 5 and 10 to 15 are the grey. |
| A10 | The dressing pass: lamps by the route and a cover pass by the lint's own `lit` rule, pools hung from sets or on crates, work lamps, pendants on cords in tall rooms, track on the doors' axes with sidings, sets, pilasters, trim bands and a lower wall band, clutter in groups, machines sized to the room, nothing cutting floor off; larger rooms dressed whole (M27) with their set pieces (a centrepiece on the plinth, a headframe, an overhead crane, a pipe run at height, track through level 2's yard kept per joined socket); a jump room's floor dressed but for 128 round each jump ledge (M28); posts lining a great hall's walls under a plate, lagging in the bays; lamp pools round a larger room's walls; Act II's standing water, roof-hung stalactites, broken sets. | Handrails on ledges and decks (they stand in the air a ledge's jump needs). |
| A11 | `info_poi` markers of every kind, map labels and markers. | |
| A12 | `library.json` with tags, weights, depths, jump, low, markers, doors, height, focus and a preview camera. | |
| A13 | `make descent-rooms` and `make descent-level`. | |
| A14 | Level 1's sky shaft under the town's sun and sky; work floods at its foot. | The sun still cannot reach the cage down 192 of shaft. |
| A15 | Door frames lining each half of a joined doorway (the farm model as a static prop, or a theme's brush frame as one non-solid `func_brush` per joined socket), sill planks, cap panels (M23, M27); level 2's frames lay planks across the threshold; Act II's (brief p4) a broken timber set with a cracked sagging head (6), a natural rock arch with haunched corners (7), a rusted steel flood-door frame with steel on its sill (8), a carved stone arch on plinths with a keystone (9). | |
| B1 | Lint facts per rule, plus a giant's reach of every pack spawn; the room lint and the dress lint read a larger room's whole footprint and every socket (M27). | |
| B2 | `DescentPaletteTests` against the database at `~/assetdb`, at depths 1-4 and 6-9, a water material held to the `water` usage. | |
| B3 | `DescentMineShaftTests` holds the town to `MineShaft`; level 1's way in is built from the same class. | No fact reads level 1's library for the same values. |
| B4 | `make descent-brightness`, with every room's view under 2 % white (M27: level 1's shaft foot was white while the level's share passed). | Depths 5 and 10 to 15 (still grey); the gate's default depths are 1-4 and 6-9. |
| B5 | `make descent-reach`, reading every island (`tools/descent-islands`) and accepting a trap only inside the exit's volume. | The per-class report (Scout, Soldier, Demoman floor) of Phase 7. |
| B6 | `make descent-budget`, with the fog and tone map controllers checked against the library. | |
| C1 | `LevelLayout`: free-form growth to the room budget (M19), no hallway dead ends (M18), a room at every tunnel's end and no capped door (M20), the adaptive mix (M21); rooms larger than one cell (M25, `DepthTable.BigRoomsAt`), laid off a fill room and counted once; chosen sockets in the name codec. | A larger room on the critical path of a normal level (they stand on branches); door slots. |
| C2 | `DepthTable`, with the mix of section 7 per depth. | |
| C3 | The boss levels' rows, round a larger arena (M27). | |
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
| H4 | A door that stands on the floor at any height, as an option of the kit | Done: `door_sill floor`, map tools 7cdfe4d (section 1.3) |
| H5 | The Scout's air dash at stock's speed in the simulator | 15 % too high |
| H6 | Faces for a joined doorway's sides, sill and head, or socket furniture kept on both sides | Each joined door costs two `func_brush` entities today |
| H7 | `ssmap link` writes a turned room's face side flags inverted against their nodes; the engine culls by them | Found in game (brief p5, section 1.3d) at 7cdfe4d and 3551fa8; `tools/descent-face-sides` rewrites them after each link |
| H8 | `ssmap link` keeps a joined socket's plug faces (nodraw-flagged); the engine draws any world face | Found in game (brief p5); the plug material is `$no_draw` |
| H9 | A joined doorway's let-down floor under the plug keeps the black lightmap of the room's own compile | Found in game (brief p5): a black strip across every doorway; relight or drop the dip. Worked round in the rooms (brief p9): a lit slab over the dip; the farm frame model's head (level 1) still draws dark, a prop lit from inside the plug |
| H10 | A room's navigation leaves out its static props: `RoomLibraryCompiler` builds it from the world and entity obstacles only (`NavGeometry.FromBsp`, `NavModelBounds.PropModels`); `LevelNavFromBsp` takes the static prop lump | Measured (brief p9, section 1.3e): the nav is open inside every solid crate and drum of levels 1 and 3; without the game content the pack refuses every room |

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
4. **Boss arena size.** 2x2, 3x3 and 4x4 cells (2048, 3072, 4096 across) are
   guesses until the bosses exist (they moved to the 1024 kit, M27; their
   costs are in section 1.3b).
5. **Room count.** About 32 rooms a library is 480 rooms to compile and
   keep. Phase 0's timing says whether that is cheap.
