# `bloodspray-stock.tsv.gz`: provenance

fx_blood.cpp as stock ran it: `FX_BloodSpray` (the light, every draw, the
drops, the gore blossom, the cloud), `FX_BloodBulletImpact` (the core, the
gore, the colliding drops and their collision setup),
`BloodImpactCallback`'s choice of particle system and its angles,
`HunterDamageCallback`'s flecks and their collision, and every step of every
`CBloodSprayEmitter` in the order the process ran them (its
`UpdateVelocity` keeps the decay in process-wide statics, so the steps are
replayed through one shared `BloodDecay`). `BloodOracleTests` replays it
against `SourceSharp.Client.Effects.BloodEffects` / `BloodSprayRules`, bit
for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/fx_blood.cpp.probe.diff`,
`particles_simple.cpp.probe.diff` (the `bloodgore` emitters' steps),
`fx_sparks.cpp.probe.diff`, `particle_simple3d.cpp.probe.diff` and
`particle_collision.cpp.probe.diff`, and the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-blood.steps`:
c5p_dispatch (tools/probes/client/c5particles_probes_debug.cpp, probe builds
only) ran bloodspray with each flag and colours red, yellow and mech at two
scales, bloodimpact with every colour (mech reaches the bullet impact), and
HunterDamage, locally on the client. Kept: every spawn unit and every gore
step: 48 sprays (912 drops, 288 gore and cloud particles), 4 bullet impacts,
12 impact choices, 2 hunter hits (64 flecks), 8,948 gore steps.

| tag | columns |
|---|---|
| `bse` | origin xyz, normal xyz, scale, rgb, flags, low violence |
| `bbe` | origin xyz, normal xyz, scale, rgb, maxClients, low violence |
| `bl` | the world light xyz |
| `br` | `f` or `i`, min, max, value (`RandomVector`, `Vector::Random` and `RandomAngle` logged per component) |
| `btp` | drop: position xyz, lifetime, die time, width, length, velocity xyz, rgba |
| `bsp` | position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material |
| `bgc` | a blood spray emitter and its gravity |
| `bic` | colour, the system named, origin xyz, normal xyz, the angles it was dispatched along |
| `bhd` | origin xyz, normal xyz |
| `b3p` | fleck: position xyz, velocity xyz, angles xyz, spin, life remaining, size, front rgb, back rgb |
| `bgs` | emitter, particle, dt, flags (stock's uninitialised byte), position xyz, velocity xyz, lifetime, die time, roll, roll delta |
| `bgso` | particle, position xyz, velocity xyz, lifetime, roll, roll delta, dies |
| `pc*` | as particlecollision-stock.tsv.gz |

The flags byte is stock's accident: `SimpleParticle::m_iFlags` is never set,
so a gore particle whose reused memory has the no-decay bit set neither
decays nor falls (2,018 of the steps here). The port spawns 0 (decaying), as
the other simple-emitter ports do; the step fact replays the recorded byte.
