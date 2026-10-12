# `sprays-stock.tsv.gz`: provenance

The impact sprays as stock made them, shell_impact_effects.cpp's simple
sheets: `FX_DebrisFlecks`' dust trail, puffs and capper behind its flecks,
`FX_DustImpact`, `FX_Blood` and `FX_BugBlood` (each effect's inputs and block,
every random draw, every particle built), and the dust emitter
(`CDustParticle`, c_impact_effects.h) stepping and drawing.
`SpraysOracleTests` replays it against
`SourceSharp.Client.Effects.ImpactSprays`/`DustParticleRules`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/shell_impact_effects.cpp.probe.diff` (its
`sdi`/`sti`/`sbi`/`sui`/`spp` rows and the `fkr` draws) and
`particles_simple.cpp.probe.diff` (its `dg*` rows and the dust's `xrd`), with
the other probe copies probe_copies.cmake lists, the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-sprays.steps`
(`tools/probes/c0-session OUT STEPS --port 27646 --arg=+map --arg=dm_lockdown`):
SMG bullets into the floor and a wall, then `c5p_spray` debris and dust at two
scales, blood in two colours and the bug blood. Kept: every spray unit (its
input row and the `fkr`, `fki`, `fkp` and `spp` rows inside it), every dust
step and every dust draw.

| tag | columns |
|---|---|
| `sdi` | trace end xyz, normal xyz, surface colour rgb, scale, throttle, block length, the block |
| `sti` | origin xyz, normal xyz, surface colour rgb, scale, throttle, block length, the block |
| `sbi` | position xyz, direction xyz, r g b a, block length, the block |
| `sui` | position xyz, direction xyz, world mins xyz, world maxs xyz, block length, the block |
| `fkr` | `f`, `i` or `v` (a `Vector::Random` component), min, max, value |
| `fki` / `fkp` | the flecks' input and each fleck (README-fleck-stock.md) |
| `spp` | effect, burst, position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material slot |
| `dgs` | the emitter, the particle, dt, flags, position xyz, velocity xyz, lifetime, die time, roll, roll delta |
| `dgso` | the particle, position xyz, velocity xyz, lifetime, roll, roll delta, died |
| `xrd` | `dust`, lifetime, die time, rgb, start and end alpha, start and end size, near clip min and max, `\|`, colour xyz, alpha, scale, view z |

The block past its bursts is the native stack's garbage; the facts compare the
written part. The debris dust's particle flags are left uninitialised by stock
(`SimpleParticle newParticle` reused, copied whole into the singleton), so a
stray `SIMPLE_PARTICLE_FLAG_WINDBLOWN` bit is possible there; the port's flags
are zero.
