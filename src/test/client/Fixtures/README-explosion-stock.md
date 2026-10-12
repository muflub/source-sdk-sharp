# `explosion-stock.tsv.gz`: provenance

fx_explosion.cpp as stock ran it: `C_BaseExplosionEffect`,
`C_WaterExplosionEffect` and `C_MegaBombExplosionEffect` (each one's inputs,
its six force probes, the water search, the light it read, every random
draw, the sounds, the core's smoke, ring, embers and fireballs, the sparks,
the cement flecks and their collision setup, the water shafts and splash),
and `CExplosionParticle`'s steps and draws. `ExplosionOracleTests` replays it
against `SourceSharp.Client.Effects.ExplosionEffects` / `ExplosionRules`, bit
for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/fx_explosion.cpp.probe.diff`,
`particles_simple.cpp.probe.diff` (the `exp_smoke` emitters' steps, `xg*`, and
draws, `xrd`), `fx_sparks.cpp`, `fx_fleck.cpp`, `fx_water.cpp` and
`particle_collision.cpp .probe.diff`, and the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-explosion.steps`:
c5p_dispatch ran Explosion with each flag (none, no fireball, no fireball
smoke, no particles, no sound) and at a scale of 0, HelicopterMegaBomb and
WaterSurfaceExplosion at three places (in the open, near the floor, by a
wall), and cs_te_fire fired one explosion temp entity. Kept: every
explosion's whole unit (25: 3 water), 1 in 20 smoke steps (and 1 in 5 of
those that died), 1 in 30 smoke draws. dm_lockdown has no water there, so the
water explosions found no surface and had no bubbles (depth 0).

| tag | columns |
|---|---|
| `xwt` | (water) the last water trace's startsolid, fraction, fractionleftsolid, the surface xyz |
| `xwd` | (water) the depth |
| `xe` | origin xyz, force, scale, flags |
| `xpt` | a force probe: start xyz, end xyz, fraction |
| `xf` | the direction xyz, the force |
| `xl` | a light read, xyz |
| `xs` | a sound's script |
| `xc` | an emitter made: `exp_smoke`, `sparks`, `flecks`, `bubbles`, `drops`, `splish` |
| `xr` | `f` or `i`, min, max, value (`Vector::Random` and `RandomVector` logged per component) |
| `xsp` | emitter (`core`, `bubbles`, `splish`), position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material |
| `xtp` | trail: position xyz, lifetime, die time, width, length, velocity xyz, rgba |
| `xfp` | fleck: sheet, position xyz, velocity xyz, lifetime, die time, size, roll, roll delta, rgb |
| `xln` | water shaft: start z, end z, end velocity z, die time, start alpha, start scale, end scale, start x, end x, end alpha |
| `xgs` / `xgso` | as the blood fixture's `bgs` / `bgso` |
| `xrd` | emitter, lifetime, die time, rgb, start and end alpha, start and end size, near clip min and max, `\|`, colour xyz, alpha, scale, view z |
| `fkc`, `pc*` | as fleck-stock and particlecollision-stock |

The mega bomb's unit is told by its shape (force and scale 1, no smoke,
fireballs only); the fixture does not name the effect.
