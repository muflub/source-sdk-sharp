# `funnel-stock.tsv.gz`: provenance

The large funnel temp entity as stock drew it: `C_TELargeFunnel::CreateFunnel`
(carried verbatim in shell_te_backends_client.cpp's
`SourceSharp_ClientLargeFunnel`), every random draw and every particle.
`FunnelOracleTests` replays it against `SourceSharp.Client.Effects.LargeFunnel`,
bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copy `tools/probes/client/shell_te_backends_client.cpp.probe.diff` (its
`lf*` rows; the draws are its `shr` rows) and the other probe copies
probe_copies.cmake lists, the client built with `-DSS_CLIENT_PROBES=ON`, over
`tools/probes/sessions/c5p-funnel.steps`
(`tools/probes/c0-session OUT STEPS --port 27646 --arg=+map --arg=dm_lockdown`):
`cs_te_fire largefunnel` twice (its demo sends reversed) and `c5p_funnel`
forward and reversed. Kept: each funnel's rows.

| tag | columns |
|---|---|
| `lfi` | origin xyz, the wire's reversed, reversed as the backend read it |
| `shr` | `f` or `i`, min, max, value |
| `lfp` | grid i, grid j, position xyz, velocity xyz, lifetime, die time, rgb, start and end alpha, start and end size, roll, roll delta |

The grid is 22 x 22 (-256 to 256 in steps of 24), 484 particles a funnel;
stock's comment says 21 x 21.
