# `particlemgr-new-stock.tsv.gz`: provenance

The new-style particle effects as stock ran them: `CParticleMgr::UpdateNewEffects`
and its helpers (game/client/particlemgr.cpp) over every `CNewParticleEffect`
(game/client/particles_new.cpp) the client started. C7 particles phase 3's
replay holds the managed manager's decisions to it. The particle library
(libss_particles.so) simulates and renders; its answers (bounds, active
particles, sleep times, IsFinished) are inputs here, never decisions.

Recorded at `43171f0c1` (main) with the probe copies
`tools/probes/client/particlemgr.cpp.probe.diff` and
`tools/probes/client/particles_new.cpp.probe.diff`, the client configured with
`-DSS_PM_PROBES=ON`, over `tools/probes/sessions/c7-newparticles.steps`
(`tools/probes/c0-session OUT tools/probes/sessions/c7-newparticles.steps
--no-probe-build`; the probe strings were checked in client.so):
- ss_sandbox (`make map MAP=ss_sandbox` in the same tree) with the thirty
  env_fires of `c7-thirty-fires.json` started, looked away from and back,
  walked away and back, `r_particle_timescale 0.5`, `cl_particle_retire_cost 1`,
  then extinguished;
- then dm_lockdown with `cl_new_impact_effects 1`, firing an SMG for two
  seconds (the impacts' one-shot systems).

`r_threaded_particles 0` throughout: stock's single-threaded sim path, the one
the managed batch takes (the phase 3 spike's decision, docs/todo.md).

Only the `np` rows are kept: 1,489 updates, 57 effects. Pointers name an effect
from its `add` to its `remove`; a later `add` of the same pointer is a new
effect. Vectors are `x,y,z`.

| tag | columns |
|---|---|
| `add` | effect, system name, the definition is a view-model effect |
| `remove` | effect |
| `update` | framecount, dt in, r_particle_timescale, dt, curtime (the last simulation time set) |
| `effect`, `proc`, `simmed`, `firstbbox` | effect, name, first frame, needs bbox update, auto-update bbox, remove flag, should simulate, next sleep time, active particles, has moved, min, max, sort origin (`effect` before its Update, `proc` before ProcessPSystem, `firstbbox` after the first-box step, `simmed` after it) |
| `build` | the last simulation time BuildParticleSimList compares against |
| `list` | effect, bbox only (a skipped effect listed because it moved) |
| `retire` | effects listed, view width, height, cl_particle_retire_cost * 1000 (past the zero-size and zero-cost returns) |
| `area`, `replace`, `retired` | the retirement pass: definition, effects counted, area, budget; the effect retired and its replacement; the result |
| `sim` | effect, bbox only, the dt simulated (-1: none), IsFinished after |
| `recalc` | effect, RecalculateBoundingBox's answer, min, max |
| `detect` | effect, has a render handle, cl_particleeffect_aabb_buffer, min, max, last min, last max (before) |
| `changed` | effect (DetectChanges called RenderableChanged) |
| `drawin` | effect, flags, camera, squared distance to the box, max draw distance, view entity, skip-render control point, highest control point, last simulation time, no-draw time to sleep |
| `draw` | effect, flags, result, the return taken (drawparticles, clientmode, shadowdepth, distance, skip-self, skip-root, skip-in-eye, drawn), next sleep time after |
| `release` | effect, ref count after, allocated, IsFinished |
| `stop` | effect, StopEmission's three arguments |

Every draw in the recording took the `drawn` return, no definition had a cull
radius (no `area`), no effect asked for a first box, and no effect slept (every
fire was drawn each frame, and a drawn system's render keeps it awake; the
skips are effects flagged for removal). Those arms are held by facts derived
from stock instead (NewParticleEffectTests).
