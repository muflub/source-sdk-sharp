# `precipitation-stock.tsv`: provenance

This is stock's client weather, run offline:
- CClient_Precipitation's rain, snow and ash;
- CSnowFallManager's snowfall.

The code is the PRISTINE `game/client/c_effects.cpp` from the clone at
`valve-sdk.lock`, compiled unedited by
`src/test/oracle/native/precipitation_oracle_test.cpp` into a stand-in world
(`precipitation_stub/precip_harness.h` says what is stock and what is
stand-in). That oracle asserts stock produces this file exactly.
`PrecipitationOracleTests` replays it against the managed port.

## Why offline

In this build every emission path of those classes is dead. Each one asks
`C_BasePlayer::GetLocalPlayer()`, which is NULL in this mod: ComputeEmissionArea,
EmitParticles, CreateAshParticle and CreateSnowFall. A live recording records
nothing. Under the "port dead stock arms" rule they are ported, so the
behaviour comes from stock run where it is alive.

## Regenerating

Do not regenerate it to make a red test green. To regenerate on purpose:
1. Touch `src/test/oracle/native/precipitation_golden.regen`.
2. Run `farm/debug/src/buildcpptests --oracle precipitation_oracle_test`.
3. Delete the marker.
4. Say why in the commit.

## The scenario

**World.**
- Solid below z = 0, plus a pillar at x 300..400, y -100..100.
- Water surface at z = 5 where y > 0.
- Over the emission traces: a sky ceiling at z = 500 where y > 400, and a plain
  ceiling at z = 450 where x > 600.
- A wall at x = 1500 for the ash's forward trace.

**Player.**
- Starts at (100, 50, 0) with eye height 64.
- Moves +x at 240 u/s until frame 180.
- Yaw 0, then 30 from frame 200.
- FOV 45 from frames 180 to 219.

**Wind.**
- cl_winddir 225, cl_windspeed 60.
- The particle wind (GetWindspeedAtTime) is (30, -10, 0), then (-20, 15, 0)
  from frame 150.

**ConVars.** r_RainRadius 900, r_SnowParticles 40.

**Frames.** 60 Hz, except frame 90, which is 0.125 s (the emission clamp), and
frame 91, which is 0 s (no wind recompute).

**Draws.** Two seeded streams, every draw written down:
- `e` is the client's `random->` (seed 0x5eed);
- `g` is vstdlib's free functions (seed 0x5eee);
- `c` is the C library's rand(), which mathlib's RandomVector draws.

**Phase `glibc`.** 240 frames with real glibc rand() (srand 1). The volumes
are:
- rain: type 0, alpha 60, box ±1200, z 0..600;
- snow: type 1, alpha 30, box ±800, z 0..500;
- ash: type 2, box -2000..3000 × ±2000, z 0..800;
- two snowfall volumes: type 3, box A ±500, z 0..400, and box B at x
  2000..3000.

**Phase `msvc`.** A fresh ash volume alone for 300 frames, with the MSVC CRT's
rand() (x = x * 214013 + 2531011, (x >> 16) & 0x7fff, seed 1).

## Findings the replay encodes

- **Ash does not fall on Linux.** RandomVector computes
  `min + rand() / VALVE_RAND_MAX * (max - min)` with VALVE_RAND_MAX 0x7fff.
  glibc's rand() reaches 2^31-1, so each component lands up to about 65,000
  times outside ±256. Every candidate fails the volume test: phase `glibc`
  adds 0 ash particles in 240 frames. Phase `msvc` adds them, along with the
  ember arm.
  - Ruling (integrator, 2026-10-05): the port uses MSVC semantics, from a
    per-system seeded stream, never process-global rand. Both phases stay in
    the oracle.
- **The snowfall manager is destroyed by ANY precipitation volume's
  destructor.** `~CClient_Precipitation` calls `SnowFallManagerDestroy()`.
- **CSnowFallManager's per-volume `m_pEffect` emitters are created and never
  used or freed.**

## Rows

| tag | columns |
|---|---|
| `#`, `P`, `D` | comment; phase name and frame count; the deletions between the phases |
| `E` | a volume: index, type, alpha, mins xyz, maxs xyz |
| `F` | frame, curtime, dt, player origin xyz, velocity xyz, yaw, FOV, default FOV, particle wind xyz |
| `R` | a draw: stream (`e`, `g`, `c`), kind (`f` float, `i` int, `x` float-exp, `r` rand), args, result |
| `L` | UTIL_TraceLine: mask, start xyz, end xyz, fraction, surface flags |
| `M` | UTIL_TraceModel against a volume: index, start xyz, end xyz, hit, fraction, startsolid |
| `C` | a rain or snow particle created this frame: volume, pos xyz, vel xyz, mass, ramp |
| `N` | a simple particle added this frame: emitter, id, material, pos xyz, vel xyz, roll, die time, lifetime, rgb, start/end alpha, start/end size, flags, roll delta |
| `X` | a simple particle died: id |
| `W` | FX_WaterRipple: origin xyz, scale, lifetime, alpha |
| `T` | a rain or snow tracer drawn, on snapshot frames: view-space start xyz, delta xyz, width, alpha |
| `Q`, `S` | rain or snow snapshot: volume, count, remainder, lifetime, wind xy; then each particle's pos, vel and ramp |
| `G`, `H` | simple emitter snapshot: emitter, name, count, sort origin; then each particle's id, pos, vel, roll, lifetime and UpdateAlpha |
| `B` | the snowfall emitter's SetBBox: mins xyz, maxs xyz |
