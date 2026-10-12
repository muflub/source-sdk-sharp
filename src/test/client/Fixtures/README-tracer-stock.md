# `tracer-stock.tsv.gz`: provenance

Stock `Tracer_ComputeVerts` (c_tracer.cpp, with its `ClipTracer`), recorded
before the geometry moved to C#. `TracerGeometryOracleTests` replays it
against `SourceSharp.Client.Effects.TracerGeometry`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree, with
`tools/probes/client/c_tracer.cpp.probe.diff` applied to a probe copy of the
fork's c_tracer.cpp and the client built with `-DSS_CLIENT_PROBES=ON`, over
`tools/probes/sessions/c5p-collision.steps` (the same SMG bursts and two
`env_explosion`s as particlecollision-stock.tsv.gz; the explosions' and the
impacts' spark trails draw through it). 1,059 calls: 1,005 drawn, 54 refused.

Rows (`trv`, tab-separated, floats %.9g): the view-space start xyz, delta xyz
and width, `|`, 1 and the four corners xyz in stock's order, or 0.
