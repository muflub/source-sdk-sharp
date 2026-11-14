# F02 report: the TF map nav spike

## 1. The spike at map-tools 38446d1 (coordinator, 2026-11-10)

Run at game main 766ab7749, map-tools `maptools.lock` 38446d1 built in
Release from a clean worktree (`../mt-f02`), box nproc 32, load 3.7:
`ssmap nav3d <bsp> -out <nav> -game game/mod_tf2 -threads {8,1}` over
`bin/debug/content/tf/maps/<map>.bsp`. **None of the four maps built**, each
refused in under 0.4 s:

| map | refusal |
|---|---|
| ctf_2fort | `game lump 0 ("sprp") spans 84..213416 of a 98312-byte lump` |
| pl_badwater | `game lump 1 ("dprp") spans 24552..246796 of a 142616-byte lump` |
| cp_dustbowl | `game lump 0 ("sprp") spans 84..189056 of a 115440-byte lump` |
| koth_viaduct | `the map is 16688 units tall, 1043 voxels of 16; a .nav3d column holds at most 255` (`-voxel 24`: does not divide 256; `-voxel 32`: 522 voxels) |

Two causes: (1) compressed maps (each game lump LZMA on its own at a
file-absolute offset; the shipped maps also compress 49-50 lumps whole),
and (2) the column height.

## 2. Cause 1 fixed upstream: map-tools 9d600ed (coordinator's spike)

Map-tools main `3cec3d7` ("Read compressed maps: LZMA lumps, game lump
entries and pak entries", #139) reads compressed maps. The p6nav lane's own
decoder (tagged `archive/nav-tf-maps-pre-rebase`) was dropped for it. At
9d600ed, same command (`-game game/mod_tf2`), load about 4:

| map | 8 threads | max RSS | bytes | sha256 | 1 thread |
|---|---|---|---|---|---|
| ctf_2fort | 69.9 s | 535 MB | 4,333,359 | `1f7b2865db7153eb` | 7 m 54 s, identical |
| cp_dustbowl | 3 m 46 s | 1.09 GB | 9,943,803 | `63636fa86c0df67d` | 25 m 37 s, identical |
| pl_badwater | refused: 16,800 units tall | | | | |
| koth_viaduct | refused: 16,688 units tall | | | | |

## 3. Cause 2: the play space (map-tools `claude/nav-tf-maps`)

User, 2026-11-10: "those maps are tall but the playable area shouldnt be that
tall", and "should also be able to cull the nav generation by only the areas
the player can actually get to". The branch, on map-tools main 55bc945,
carries two commits:

- `e0495eb` Nav: keep a compiled map's grid to its play space, leaving out
  the 3D skybox.
- `eccb2c7` Nav: never voxelise space outside the play space;
  `-playspace off` builds it all.

**What the play space is** (`NavPlayableSpace`). Two vis clusters that share
a portal see each other, so the clusters joined through the PVS, taken
transitively, are a portal-connected region: everything a player can walk,
drop, climb, open a door or ride into from there. The play space is the
region of the player spawns (`info_player_teamspawn`, `info_player_deathmatch`,
`info_player_start`, the HL2MP and CS team spawns) and the teleport
destinations, leaving out the `sky_camera`'s region and TF's end-of-match
summary stage (spawns with a non-zero `MatchSummary` key: koth_viaduct and
pl_badwater keep one at z -14,600, which alone made them 16,700 units tall).
A map without vis, or with no spawn and no sky camera, has none, and is
built over the world as before.

**What the build does with it** (`LevelNavFromBsp`). The grid keeps to the
play space's leaf bounds plus two voxels (whole cells and voxel layers are
dropped; the grid lines do not move), and a voxel whose centre lies in a
leaf outside the play space is outside, exactly as a voxel in a solid leaf
is: never built and never a leaf, and a cell of nothing else is skipped.
`BspNavOptions.PlaySpace` / `ssmap nav3d -playspace on|off` (on by
default) turns both off. The `.nav3d` format is unchanged, and so is the
voxel (16).

**Why not #141's reachability.** Map-tools' `Movement/Reachability`
simulates walks and jumps from every 32-unit floor sample. It climbs no
ladder, opens no door and follows no teleport, so a cull driven by it would
drop floor a player does reach (dm_lockdown has 371 ladder leaves). The PVS
region is a superset of what the movement reaches that follows all of
those by construction, costs a few milliseconds, and its order is fixed, so
the build stays byte-identical at any thread count. The walkable-floor cull
inside the play space (built cells grown from the spawn cells) is a
further step, with the facts below as its floor.

### 3.1 Playable heights

`NavPlayableSpace.Of` at e0495eb (tool: a scratch program over
MapFormats/MapTools):

| map | world z | world height | play space z | play height |
|---|---|---|---|---|
| ctf_2fort | -632 .. 1,856 | 2,488 | -496 .. 1,728 | 2,224 |
| cp_dustbowl | -2,640 .. 1,344 | 3,984 | -640 .. 1,280 | 1,920 |
| pl_badwater | -14,728 .. 2,064 | 16,792 | -640 .. 1,920 | 2,560 |
| koth_viaduct | -14,728 .. 1,952 | 16,680 | -128 .. 1,920 | 2,048 |

### 3.2 The table at eccb2c7

Game p6nav at 8285c8c13, ssmap from `../mt-p6nav` (Release),
`-game bin/debug/mods/tf2` (see §4), script `.scratch/p6nav-f02-run2.sh`.
**The box was loaded** (load 34-65 from other lanes), so the wall times are
pessimistic and only the on/off pairs, run back to back, compare. "off" is
`-playspace off`, the build before the clip and the cull.

| map | build | wall (8 t) | max RSS | bytes | cells | voxels up | leaves | walkable floors | jump links | sha256 |
|---|---|---|---|---|---|---|---|---|---|---|
| ctf_2fort | play space | 1 m 45 s | 632 MB | 4,371,310 | 659 of 888 | 143 | 1,394,193 | 223,989 | 4,611 | `aa78a5d1ee568f32` |
| ctf_2fort | off | 2 m 04 s | 629 MB | 4,872,664 | 740 of 1,221 | 156 | 1,467,203 | 253,184 | 5,139 | `0f40b86cf8d08d11` |
| cp_dustbowl | play space | 1 m 39 s | 1.17 GB | 10,324,663 | 1,054 of 1,353 | 124 | 3,348,662 | 420,606 | 5,505 | `ae73c8b1fa647962` |
| cp_dustbowl | off | 7 m 57 s | 1.21 GB | 10,909,935 | 1,343 of 2,450 | 249 | 3,540,685 | 507,207 | 5,562 | `f0a397a8356f7f89` |
| pl_badwater | play space | 4 m 43 s | 1.57 GB | 10,417,448 | 855 of 1,209 | 164 | 3,471,333 | 200,876 | 3,531 | `06979536314e2b36` |
| pl_badwater | off | refused: 16,800 units tall | | | | | | | | |
| koth_viaduct | play space | 57 s | 1.90 GB | 20,202,374 | 1,136 of 1,360 | 132 | 5,183,968 | 507,977 | 4,858 | `0bc325b2b0e36636` |
| koth_viaduct | off | refused: 16,688 units tall | | | | | | | | |
| dm_lockdown | play space | 6.3 s | 187 MB | 731,432 | 177 of 253 | 83 | 155,494 | 41,639 | 864 | `22f01c6f77b69a8d` |
| dm_lockdown | off | 11.8 s | 180 MB | 733,923 | 177 of 264 | 83 | 156,159 | 41,960 | 874 | `84740bf87513da51` |

- dm_lockdown "off" is byte-identical to the nav `make ai-nav` builds at
  `maptools.lock` 38446d1 (sha256 `84740bf87513`): `-playspace off` is the
  old build.
- ss_ai_tdm is a linked level: its nav comes from the room pack
  (`ssmap link`), not `LevelNavFromBsp`, and the cull does not touch it
  (7,164 cells, 54,144 edges, as before).
- 1 thread against 8: see §3.4.

### 3.3 The play content is unchanged

`navcmp` (scratch: every leaf a body of the game's hull stands on at its
centre, keyed by column and floor height, and whether the floor is in a
play-space leaf), play space against off:

| map | hull | standable floors, play space / off | only in off | of those in the play space | only in play space |
|---|---|---|---|---|---|
| ctf_2fort | 48 x 82 | 158,607 / 174,329 | 15,722 | 0 | 0 |
| cp_dustbowl | 48 x 82 | 302,288 / 379,852 | 77,564 | 0 | 0 |
| dm_lockdown | 32 x 72 | 33,898 / 34,135 | 237 | 0 | 0 |

Every floor the off build has in the play space, the play-space build has
too; what it drops is the 3D skybox and sealed space no spawn joins.

**Every place the game sends a player keeps its floor** (`navtargets`,
scratch: each spawn, flag, capture zone, control point, capture area, cart
path node and pickup, a standable floor within 96 units under it, both
builds). Dropped by the cull: **0** on all three maps.

| map | targets | on a floor, play space | on a floor, off |
|---|---|---|---|
| ctf_2fort | 30 spawns, 2 flags, 2 capture zones, 12 packs | all 46 | all 46 |
| cp_dustbowl | 80 spawns, 6 control points, 6 capture areas, 1 path_track, 48 packs | 122 of 141 | 122 of 141 |
| dm_lockdown | 9 spawns, 59 items, 18 weapons | 60 of 86 | 60 of 86 |

The misses are the same in both builds (cp_dustbowl: 9 spawns, 9 packs and
the one path_track; dm_lockdown: chargers on walls, items on shelves), not
the cull's. Map-tools holds the same property in a fact
(`NavPlaySpaceCullTests.EveryPlaceTheGameSendsAPlayerKeepsAFloorAPlayerStandsOn`:
two play rooms with a team spawn and a flag each, a skybox between them).

**What changes for HL2MP.** The dm_lockdown graph changes (237 floors fewer,
all outside the play space), so the committed live recording
`recordings/live/dm_lockdown-4bots.airec` no longer replays byte-identical
over the play-space nav: `LiveRecordingTests` fail 2 of 2 with it in place
(DmLockdownNavTests and ChatHelpTests pass). At the lock (38446d1) they
pass. When `maptools.lock` moves past eccb2c7, either re-record that session
or build dm_lockdown with `-playspace off` (which the lock's ssmap does not
accept, so not before).

### 3.4 Determinism

1 thread against 8, same command, sha256 of the file:

| map | 1 thread | result |
|---|---|---|
| ctf_2fort | 13 m 26 s, 522 MB | byte-identical (`aa78a5d1ee568f32`) |
| cp_dustbowl | 12 m 23 s, 1.12 GB | byte-identical (`ae73c8b1fa647962`) |
| pl_badwater | 18 m 13 s, 1.51 GB | byte-identical (`06979536314e2b36`) |
| koth_viaduct | 5 m 14 s, 1.77 GB | byte-identical (`0bc325b2b0e36636`) |

The cull's order is the cell index and the play space is a fixed function
of the map, so neither depends on the threads; map-tools'
`TheSameMapGivesTheSameBytesEveryTime` holds it for any degree.

### 3.5 `-check` (ground under every place to stand)

| map | places a player stands | without ground within 18 units |
|---|---|---|
| ctf_2fort | 177,402 | 1 (728 3560 18.65) |
| cp_dustbowl | 332,590 | 29 |
| pl_badwater | 165,797 | 26 |
| koth_viaduct | 442,498 | 20 |

These are the builder's own (the off builds of ctf_2fort and cp_dustbowl
share them), not the cull's; not investigated here.

## 4. The props: mount the mod as the game does

F02's `-game game/mod_tf2` mounted **no TF content**: the TF gameinfo's
`|gameinfo_path|../../content/tf` is written for the run-time mod dir
`bin/<config>/mods/tf2` and points nowhere from `game/mod_tf2`. That is the
whole of the "no hull the content gives" warnings (ctf_2fort: 215 static
prop models, 974 solid props, and the 226 `prop_dynamic` lines such as
`props_gameplay/resupply_locker.mdl`). With `-game bin/debug/mods/tf2`:
ctf_2fort 0 missing, 108 obstacles (67 before).

The remaining two misses were pl_badwater's `props_c17/concrete_barrier001a`
(4 solid props) and `props_ui/comp_stage_cam` (also koth_viaduct's, in the
summary stage): HL2 content. ssmap resolves `|all_source_engine_paths|`
against the `-game` dir's parent, the engine against its own install. The
game's `tools/ai-nav-build` now passes a mount root,
`bin/<config>/nav-mounts/<mod>`, with the mods and the engine install's `hl2`
and `platform` beside it (`$SOURCESHARP_ENGINE_DIR`, else Steam's Source SDK
Base 2013 Multiplayer). pl_badwater through it: 0 missing, 3,591 jump links
(3,531 without the barriers), 10,411,041 bytes, 271.5 s. ctf_2fort needs no
HL2 content (its nav is byte-identical without the hl2 link).

## 5. Building a TF nav in the game

`make ai-nav-tf [MAP=ctf_2fort] [MAPTOOLS_DIR=/path/to/map-tools]` builds
`bin/<config>/mods/tf2/maps/MAP.nav3d` from the map in the content, stamped.
`MAPTOOLS_DIR` from the command line or the environment builds with that
tree instead of `maptools.lock`'s (no lock check; the stamp names the tree's
commit); without it the build is the lock's. Until the lock carries
eccb2c7, pl_badwater and koth_viaduct need `MAPTOOLS_DIR`; ctf_2fort and
cp_dustbowl build at 55bc945 without the clip.

**Live, TF2 headless on ctf_2fort** (game p6nav on main cd42875b8, with
p6a's SourceSharp.Ai.TF2; the nav above, aa78a5d1ee56): without the nav
`ai_bot_add` is refused, `no navigation for ctf_2fort: expected
maps/ctf_2fort.nav3d`; with it the loader logs `navigation for ctf_2fort:
160435 cells, 1161590 edges, 108 obstacles (108 matched, 64 blocking);
derived in 1357 ms; sight 4328 nodes, 551883 pairs in 2066 ms`, the bot is
added (`bot Pia Wrecker (Medium) added: client 2`), the human joins red as a
soldier (`deployed tf_weapon_rocketlauncher`), and nothing threw. The map's
entity lump is compressed, so the loader warns that the bots know none of
its lump entities: TF2's knowledge comes from the level's placed entities
(p6a's `TFPlacedEntities`), and the team solids will too once they are read
from there rather than from the lump (or from MapFormats 1.0.3's reader).
