# `shards-stock.tsv.gz`: provenance

The shards as stock ran them: `FX_GlassImpact` (the GlassImpact client
effect: the impact block it read, the light, every draw, every Simple3D shard,
the collision set-up, every dust particle) and `C_TEShatterSurface`'s
PostDataUpdate (the decoded pane, the light, every draw, every shard, the
collision's direction and speed band). `ShardsOracleTests` replays it against
`SourceSharp.Client.Effects.ShardEffects`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copies `tools/probes/client/shell_impact_effects.cpp.probe.diff` (its
`gl*` rows; the draws are its `fkr` rows) and
`shell_te_backends_client.cpp.probe.diff` (`sh*`), with the other probe copies
probe_copies.cmake lists, the client built with `-DSS_CLIENT_PROBES=ON`, over
`tools/probes/sessions/c5p-shards.steps`: `c5p_dispatch GlassImpact` at three
places and normals (five impacts in all) and `cs_te_fire shattersurface` at
three places (four panes). Kept: every `gl*` and `sh*` row and the `fkr`
draws inside a glass unit.

| tag | columns |
|---|---|
| `gli` | position xyz, normal xyz, the light ComputeLighting answered xyz, the block's size, the block (past its bursts, the native stack's garbage) |
| `fkr` / `shr` | `f` or `i`, min, max, value (`Vector::Random` per component) |
| `glp` | material (0/1), life remaining, velocity xyz, size, angles xyz, angular speed, front rgb, back rgb |
| `glc` | angular spray, collision min and max speed, gravity, dampen |
| `gld` | burst, position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta, material slot |
| `shi` | origin xyz, angles, force xyz, force position xyz, shard size, wide, high, surface type, front rgb, back rgb, the light xyz |
| `shp` | material (0/1), position xyz, life remaining, velocity xyz, size, angular speed, front rgb, back rgb |
| `shc` | the collision's direction xyz, min and max speed |
