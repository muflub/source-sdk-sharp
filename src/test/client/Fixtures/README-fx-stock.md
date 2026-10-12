# `fx-stock.tsv.gz`: provenance

fx.cpp as stock ran it: `FX_MuzzleEffect`, `FX_GunshipMuzzleEffect`,
`FX_StriderMuzzleEffect` (each one's inputs, every random draw and every
particle), `FX_MuzzleEffectAttached`, `FX_Smoke` (the angled form and the
puffs it makes through the inner one), `CSmokeEmitter` (every spurt with its
clock, and its particles), `FX_GunshipImpact`'s inputs and
`FX_RicochetSound`'s. `FxOracleTests` replays it against
`SourceSharp.Client.Effects.FxEffects`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copy `tools/probes/client/fx.cpp.probe.diff` (with the fx_sparks and
particle_collision probe copies, which the build needs together), the client
built with `-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-fx.steps`
on dm_lockdown: three rounds (yaw 40, 80, 120) of the client's IEffects
called locally by `c5p_ieffects` (MuzzleFlash of types 1, 2, 4 and 3, at
scales 1 and 2.5; Smoke at scales 3, 6 and 9; Ricochet),
`c5p_muzzle_attached` on the local player's attachments 1 and 2 (scales 1,
0.5 one-frame, 1.5 and 0.75), `c5p_dispatch` of CommandPointer with colours 0
to 4 and of GunshipImpact, then one Smoke effect left to spurt for three
seconds. Type 3 is no muzzle flash and colour 4 no commander colour: stock
logged nothing for either. Kept: every `q*` row, nothing sampled.

| tag | columns |
|---|---|
| `qme` | FX_MuzzleEffect: origin xyz, angles, scale, has flash colour |
| `qmp` / `qgp` / `qap` / `qsp` / `qbp` | a particle (muzzle, gunship muzzle, attached muzzle, smoke puff, spurt): position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material |
| `qge` | FX_GunshipMuzzleEffect: origin xyz, angles, scale |
| `qte` | FX_StriderMuzzleEffect: origin xyz, angles, `\|`, the direction xyz |
| `qma` | FX_MuzzleEffectAttached: scale, attachment, has flash colour, one frame |
| `qse` | FX_Smoke: origin xyz, angles, scale, count, has colour, rgb, alpha |
| `qbs` | CSmokeEmitter::CreateSpurtParticles: emitter, curtime, death time, last spawn time, spurt forward x and z |
| `qie` | FX_GunshipImpact: position xyz, normal xyz, rgb |
| `qrs` | FX_RicochetSound: position xyz |
| `qr` | `f` or `i`, min, max, value (`Vector::Random` logged per component) |

The attached flash's positions are in the attachment's space, as stock's
local-space emitter held them.
