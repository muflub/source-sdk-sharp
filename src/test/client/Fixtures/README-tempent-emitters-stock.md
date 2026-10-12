# `tempent-emitters-stock.tsv.gz`: provenance

The temp entities' muzzle-flash emitters as stock ran them: the shotgun NPC
flash's `CEmberEffect` embers (`muzzle_embers`), every step with the wander
`Vector::Random` drew, and every draw with the near clip the emitter rendered
with. `TempEntEmittersOracleTests` replays it against
`SourceSharp.Client.Effects.EmberRules` / `SimpleEmitter`, bit for bit.

Recorded at base `a9d8f9fd` (client-novalve) in a scratch worktree with the
probe copy `tools/probes/client/particles_simple.cpp.probe.diff` (its `eg*`
rows and `xrd` for `muzzle_embers`; the other probe copies listed in
probe_copies.cmake built with it), the client built with
`-DSS_CLIENT_PROBES=ON`, over `tools/probes/sessions/c5p-te-emitters.steps`:
`c5p_te_muzzle 1 1 0 20` four times, the shotgun NPC flash on the local
player's attachment 1 (the map has no NPC to fire one). Kept: every `eg*`
row and every `muzzle_embers` draw, nothing sampled.

| tag | columns |
|---|---|
| `egs` | emitter, particle, dt, flags, position xyz, velocity xyz, lifetime, die time, roll, roll delta |
| `egr` | the step's `Vector::Random( -0.125, 0.125 )`, xyz |
| `egso` | particle, position xyz, velocity xyz, lifetime, roll, roll delta, died |
| `xrd` | `muzzle_embers`, lifetime, die time, rgb, start and end alpha, start and end size, near clip min and max, `\|`, colour xyz, alpha, scale, view z |
