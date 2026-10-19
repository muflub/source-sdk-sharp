# `particlemgr-stock.tsv.gz`: provenance

The old-style particle manager as stock ran it: `CParticleMgr` and
`CParticleEffectBinding` (game/client/particlemgr.cpp) driving every
executor `src/client/native/shell_managed_particles.cpp` registers. C7
particles phase 2's replay holds the managed manager to it.

Recorded at `45f51596a` (client-r-c7, on main) with the probe copies
`tools/probes/client/particlemgr.cpp.probe.diff` and
`tools/probes/client/shell_managed_particles.cpp.pm.probe.diff`, the client
configured with `-DSS_PM_PROBES=ON`, over
`tools/probes/sessions/c7-particlemgr.steps`:
- ss_sandbox (`make map MAP=ss_sandbox` in the same tree) from two angles at
  the smoke stack's plot;
- then dm_lockdown, firing an SMG for two seconds (the muzzle flashes'
  local-space executors and the impacts).

Every row is kept (549 updates). Pointers name a binding until its `remove`;
a later `add` of the same pointer is a new binding. A particle's id is
managed's, read from ManagedParticle_t's field right after `m_Pos` (it sits in
Particle's tail padding).

| tag | columns |
|---|---|
| `add` | binding, flags, cull radius |
| `remove` | binding |
| `update` | framecount, the frame's dt before the 0.1 s clamp |
| `effect` | binding, flags, active particles, bbox counter, min, max, last min, last max, sort origin (before its update) |
| `firstbbox` | binding, still needs a bbox update, min, max |
| `cull` | binding, SetParticleCullRadius's radius |
| `setbbox` | binding, disable auto-update, min, max (SetBBox) |
| `setlocal` | binding, the 3x4 matrix, row-major (SetLocalSpaceTransform) |
| `spawn` | binding, id, position (linked at the material's head) |
| `simlist` | binding, material, count, then `id:x,y,z` in list order (before its step) |
| `kill` | binding, id (RemoveParticle) |
| `simbbox` | binding, full bbox update, a particle set it, bbox counter after |
| `changed` | binding (DetectChanges called RenderableChanged) |
| `after` | binding, flags, active, min, max, last min, last max (after DetectChanges) |
| `rnd` | lo, hi, the RandomInt answer (bbox update and bucket sort) |
| `framecode` | the manager's frame code after IncrementFrameCode |
| `draw` | binding, flags, active, CurrentViewID, binding frame code, manager frame code |
| `drawbefore`, `drawafter` | binding, material, count, `id:x,y,z` in list order around the material's draw |
| `drawmat` | binding, material, bucket sort, frame time |
| `key` | binding, id, the sort key the executor handed GetNext |
| `post` | binding, drawn this frame (PostRender) |

The probe switch (`SS_PM_PROBES`) left client.so's CMake with the eviction of
particlemgr.cpp and particles_new.cpp (C7 particles phase 4): to record again,
check out the recording's commit above.
