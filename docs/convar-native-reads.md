# ConVars a native site reads, and managed code declares

`plan_shim.md` §5 3e's last bullet, and §10's ConVar ledger (Phase 5) is what
this is for. **Nothing here was moved.** Each row is a NATIVE DECISION that
reads a value the managed side owns, and the point of listing them is that no
census can find them for you.

## Why a census cannot see these

A ConVar is reached **by name**. `CManagedConVarRef r_SnowEnable( "r_SnowEnable" )`
takes a string and asks `ICvar` for it at first use; there is no symbol for a
link-line join to work with, and `tools/shim-census` has nothing to report.
That is the same blind spot that made the "dead" physics-world scaffolding look
removable when it had eight tools and twenty-seven sites driving it, and the
same one that keeps `skill` where it is.

So the instrument is `tools/convar-ref-census`: a text scan whose SUBJECT is
the sources of every object on the ninja link line, plus every reachable
header. Reproduce this table with

    tools/convar-ref-census

and check the instrument itself with `tools/convar-ref-census --self-test`.

## What was measured

| | |
|---|---|
| sites | 44 |
| files | 10 |
| distinct ConVars | 43 |

`plan_shim.md` says forty. The number is **44**, over **10** files. The
difference is not drift: the plan's figure counted every line in the tree
naming `CManagedConVarRef`, and two of those are in
`game/client/c_baseanimating.cpp`, which is on NEITHER link line — a site in an
uncompiled translation unit is not a site. The rest of the gap is comments that
name the type while explaining it.

## The rows

Grouped by the file the read is in, because that is who owns the decision.
`sides` is which DLL the translation unit compiles into.

### `src/game/client/viewrender.cpp` — 1 site

*Compiled into: client.*

| line | ConVar | declaration |
|---|---|---|
| 129 | `vcollide_wireframe` | `static CManagedConVarRef vcollide_wireframe( "vcollide_wireframe" );` |

### `src/sourcesharp/game/client/shell_clientdll.cpp` — 1 site

*Compiled into: client.*

| line | ConVar | declaration |
|---|---|---|
| 599 | `—` | `CManagedConVarRef		m_r_lightmap_bicubic_set;` |

### `src/sourcesharp/game/client/shell_effects_sim.cpp` — 31 sites

*Compiled into: client.*

| line | ConVar | declaration |
|---|---|---|
| 68 | `cl_winddir` | `static CManagedConVarRef cl_winddir( "cl_winddir" );` |
| 69 | `cl_windspeed` | `static CManagedConVarRef cl_windspeed( "cl_windspeed" );` |
| 75 | `r_RainSplashPercentage` | `static CManagedConVarRef r_RainSplashPercentage( "r_RainSplashPercentage" ); // N% chance of a rain particle m` |
| 87 | `r_RainHack` | `static CManagedConVarRef r_RainHack( "r_RainHack" );` |
| 88 | `r_RainRadius` | `static CManagedConVarRef r_RainRadius( "r_RainRadius" );` |
| 89 | `r_RainSideVel` | `static CManagedConVarRef r_RainSideVel( "r_RainSideVel" );` |
| 91 | `r_RainSimulate` | `static CManagedConVarRef r_RainSimulate( "r_RainSimulate" );` |
| 92 | `r_DrawRain` | `static CManagedConVarRef r_DrawRain( "r_DrawRain" );` |
| 93 | `r_RainProfile` | `static CManagedConVarRef r_RainProfile( "r_RainProfile" );` |
| 425 | `r_SnowEnable` | `static CManagedConVarRef r_SnowEnable( "r_SnowEnable" );` |
| 426 | `r_SnowParticles` | `static CManagedConVarRef r_SnowParticles( "r_SnowParticles" );` |
| 427 | `r_SnowInsideRadius` | `static CManagedConVarRef r_SnowInsideRadius( "r_SnowInsideRadius" );` |
| 428 | `r_SnowOutsideRadius` | `static CManagedConVarRef r_SnowOutsideRadius( "r_SnowOutsideRadius" );` |
| 429 | `r_SnowSpeedScale` | `static CManagedConVarRef r_SnowSpeedScale( "r_SnowSpeedScale" );` |
| 430 | `r_SnowPosScale` | `static CManagedConVarRef r_SnowPosScale( "r_SnowPosScale" );` |
| 431 | `r_SnowFallSpeed` | `static CManagedConVarRef r_SnowFallSpeed( "r_SnowFallSpeed" );` |
| 432 | `r_SnowWindScale` | `static CManagedConVarRef r_SnowWindScale( "r_SnowWindScale" );` |
| 433 | `r_SnowDebugBox` | `static CManagedConVarRef r_SnowDebugBox( "r_SnowDebugBox" );` |
| 434 | `r_SnowZoomOffset` | `static CManagedConVarRef r_SnowZoomOffset( "r_SnowZoomOffset" );` |
| 437 | `r_SnowStartAlpha` | `static CManagedConVarRef r_SnowStartAlpha( "r_SnowStartAlpha" );` |
| 438 | `r_SnowEndAlpha` | `static CManagedConVarRef r_SnowEndAlpha( "r_SnowEndAlpha" );` |
| 439 | `r_SnowColorRed` | `static CManagedConVarRef r_SnowColorRed( "r_SnowColorRed" );` |
| 440 | `r_SnowColorGreen` | `static CManagedConVarRef r_SnowColorGreen( "r_SnowColorGreen" );` |
| 441 | `r_SnowColorBlue` | `static CManagedConVarRef r_SnowColorBlue( "r_SnowColorBlue" );` |
| 442 | `r_SnowStartSize` | `static CManagedConVarRef r_SnowStartSize( "r_SnowStartSize" );` |
| 443 | `r_SnowEndSize` | `static CManagedConVarRef r_SnowEndSize( "r_SnowEndSize" );` |
| 444 | `r_SnowRayLength` | `static CManagedConVarRef r_SnowRayLength( "r_SnowRayLength" );` |
| 445 | `r_SnowRayRadius` | `static CManagedConVarRef r_SnowRayRadius( "r_SnowRayRadius" );` |
| 446 | `r_SnowRayEnable` | `static CManagedConVarRef r_SnowRayEnable( "r_SnowRayEnable" );` |
| 475 | `r_rainalpha` | `static CManagedConVarRef r_rainalpha( "r_rainalpha" );` |
| 476 | `r_rainalphapow` | `static CManagedConVarRef r_rainalphapow( "r_rainalphapow" );` |

### `src/sourcesharp/game/client/shell_soundscape_client.cpp` — 1 site

*Compiled into: client.*

| line | ConVar | declaration |
|---|---|---|
| 156 | `soundscape_fadetime` | `static CManagedConVarRef soundscape_fadetime( "soundscape_fadetime" );` |

### `src/sourcesharp/game/client/shell_te_backends_client.cpp` — 1 site

*Compiled into: client.*

| line | ConVar | declaration |
|---|---|---|
| 1054 | `r_drawmodeldecals` | `static CManagedConVarRef r_drawmodeldecals( "r_drawmodeldecals" );` |

### `src/sourcesharp/game/server/server_util_library.cpp` — 1 site

*Compiled into: server.*

| line | ConVar | declaration |
|---|---|---|
| 380 | `sv_strict_notarget` | `static CManagedConVarRef sv_strict_notarget( "sv_strict_notarget" );` |

### `src/sourcesharp/game/shared/entity_shared_statics.cpp` — 1 site

*Compiled into: client+server.*

| line | ConVar | declaration |
|---|---|---|
| 117 | `sv_alternateticks` | `static CManagedConVarRef sv_alternateticks( "sv_alternateticks" );` |

### `src/sourcesharp/game/shared/util_shared_library.cpp` — 4 sites

*Compiled into: client+server.*

| line | ConVar | declaration |
|---|---|---|
| 265 | `violence_hblood` | `static CManagedConVarRef	violence_hblood( "violence_hblood" );` |
| 266 | `violence_hgibs` | `static CManagedConVarRef	violence_hgibs( "violence_hgibs" );` |
| 267 | `violence_ablood` | `static CManagedConVarRef	violence_ablood( "violence_ablood" );` |
| 268 | `violence_agibs` | `static CManagedConVarRef	violence_agibs( "violence_agibs" );` |

### `src/sourcesharp/public/bone_setup.cpp` — 2 sites

*Compiled into: client+server.*

| line | ConVar | declaration |
|---|---|---|
| 1872 | `anim_3wayblend` | `static CManagedConVarRef anim_3wayblend( "anim_3wayblend" );` |
| 4811 | `aim_constraint` | `static CManagedConVarRef aim_constraint( "aim_constraint" );` |

### `src/sourcesharp/public/studio.cpp` — 1 site

*Compiled into: client+server.*

| line | ConVar | declaration |
|---|---|---|
| 90 | `mod_load_showstall` | `static CManagedConVarRef mod_load_showstall( "mod_load_showstall" );` |

## What the Phase 5 ledger has to decide for each

Not "should this ConVar be managed" — it already is. The question is what the
NATIVE READ is:

* **A decision that belongs in C#.** The thirty-one weather rows in
  `shell_effects_sim.cpp` are one system's tuning, read by a native simulation
  that Phase 4 is porting anyway. When the simulation moves, the reads go with
  it and the reference disappears.
* **A read that must stay native and is correctly a `CManagedConVarRef`.**
  `sv_alternateticks` is read from `entity_shared_statics.cpp` on a path that
  runs per tick; asking managed for it would be a crossing for a value that
  does not change.
* **A read whose SITE is in a file another lane owns.** `g_Language` is the
  worked example, and it is why its declaration did not move in §5 3e: it has
  three native read sites (`world_adapter.cpp`, `server_util_library.cpp`,
  `util_shared_library.cpp`), none of them in this lane's files, and moving the
  declaration without all three is a link error.

Each row wants one of those three verdicts and the reason, in the ledger's own
format.
