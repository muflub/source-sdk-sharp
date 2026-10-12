# `gauss-stock.tsv.gz`: provenance

FX_GaussExplosion as it ran before the port (shell_impact_effects.cpp, the
body stock's c_impact_effects.cpp had, its numbers from ImpactEffects.cs's
Gauss block): the wobbled direction, the ring emitter's collision setup,
every spark of the ring, every random draw, and the electric spark it ends
with (its emitters, draws and particles, as `sparks-stock.tsv.gz` records
them). `SparksOracleTests.Every_gauss_explosion_makes_stocks_ring_and_electric_spark`
replays it against `SparkEffects.GaussExplosion`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/shell_impact_effects.cpp.probe.diff` (its
`gx*` rows), `fx_sparks.cpp.probe.diff` and `particle_collision.cpp.probe.diff`
applied and the client built with `-DSS_CLIENT_PROBES=ON`, over
`tools/probes/sessions/c5p-gauss.steps`: eight gaussexplosion temp entities
fired by cs_te_fire in front of the player. Every row of each explosion's unit
is kept (8 explosions, 173 ring sparks).

| tag | columns |
|---|---|
| `gxe` | position xyz, direction xyz, type |
| `gxr` | `f` or `i`, min, max, value (`Vector::Random`'s three `rand()`s logged as `f` with the spray's range) |
| `gxc` | the ring emitter |
| `gxp` | emitter, position xyz, lifetime, die time, width, length, velocity xyz, rgba |
| `gxs` | the direction the electric spark is given (the last spark's) |
| `sk*`, `pc*` | as sparks-stock.tsv.gz and particlecollision-stock.tsv.gz |
