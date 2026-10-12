# plan_particles_so.md: Valve's particle library in its own shared object

Status: PARKED (user, 2026-10-12). Pick up once the rest of the client port
(C7-C10 in plan_client_novalve.md) is done. Start with the spike in section 5,
not a commit series.

## 1. Why

`particles.a` and `dmxloader.a` stay as a bound library (plan_client_novalve.md
section 3.4, user ruling 2026-10-04). They are now the only reason client.so
still links Valve libraries:

- tier1.a stays on the client link for their own imports (ruling (a), input
  lane, 2026-10-12). Measured at client-novalve 57a171399:
  - dmxloader.a pulls utlbufferutil 27, utlbuffer 20, strtools 8, uniqueid 7,
    utlsymbol 7, memstack 5, utlbinaryblock 3 and utlstring 3 symbols.
  - particles.a pulls utlsymbol 5, KeyValues 4, strtools 4, uniqueid 4,
    utlbuffer 4 and utlstring 3.
- The tier2 stand-ins (src/client/native/particles_tier2_stubs.cpp) exist only
  for particles.a: CBeamSegDraw::Start/NextSeg/End and RenderWireframeBox/
  Sphere. They are counted no-ops; the live counters read 0.
- particles.a and dmxloader.a import g_pMemAlloc and other tier0 symbols, which
  keeps libtier0.so and libvstdlib.so NEEDED by client.so.

Our own objects' imports from tier0, tier1, tier2 and vstdlib are going to
zero in C9. Without this plan, the census `imports:<lib><-particles` and
`<-dmxloader` rows are what stays.

## 2. The idea

Build a separate shared object, e.g. `libss_particles.so`, that:

- statically links particles.a and dmxloader.a, plus the tier1 members they
  need, and its own copy of the tier2 stand-ins (or real ones);
- has hidden visibility for everything except one exported entry point;
- exports one bound interface, generated like the rest of the ABI
  (docs/managed-interfaces.tsv / engineapi-areas.tsv rows, tools/vtable-layout).
  client.so's native shells and managed code both call it.

client.so then links no Valve library. The Valve particle code is fenced into
one named, swappable unit. Managed code gets a direct particle API (start,
stop, control points, query state, load or reload .pcf definitions) instead of
going through native shells.

## 3. What it must handle

1. **Callbacks inward.** The library asks the game about the world (traces,
   the lighting cube, control-point owners) through IParticleSystemQuery,
   which we already implement. It is handed to the .so at init.
2. **Rendering.** The library draws through IMatRenderContext. The engine's
   material system pointer is passed in at init, and drawing stays inside the
   .so. The client's per-effect renderable shell (CNewParticleEffect's
   IClientRenderable, called by the engine through a C++ vtable, so it stays
   native) asks the .so to draw that effect.
3. **Its own tier1.** That includes ConVars: the particle system declares its
   own, so the .so must run its own ConnectTier1Libraries against the
   engine's ICvar. Hidden visibility, plus -Bsymbolic or equivalent, keeps its
   tier1 copies (ConVar, KeyValues, strtools, CUtlBuffer...) from interposing
   on the identically named ports we compile into client.so (convar_port.cpp,
   tier1_port.cpp, convar_client.cpp). This is the main technical risk. Pin it
   with a link-time check: nm over both DLLs shows no shared tier1 symbol, and
   each resolves to its own copy.
4. **Process-wide libraries.** tier0 and vstdlib are loaded by the engine
   anyway, so the process gains nothing. The NEEDED entries just move from
   client.so to libss_particles.so.
5. **Allocation.** The client has NO_MALLOC_OVERRIDE (input lane, 2026-10-12)
   and runs under alloc_check (-Wl,--wrap). Decide whether the .so allocates
   through g_pMemAlloc (tier0) or libc. Then prove nothing crosses the
   boundary freed on the wrong heap, using alloc_check over a live run (0
   mismatches, 0 foreign frees).
6. **Hot reload.** The .so is native and isn't reloaded with managed code. The
   managed side's handles to systems must survive cs_cl_reload, like other
   native-owned state. ParticleReloadPolicy.cs already covers the
   per-level definition reload.
7. **The thread pool.** particlemgr's ParallelProcess under
   r_threaded_particles uses vstdlib's g_pThreadPool. Decide whether it moves
   with the .so or goes single-threaded.

## 4. What stays where

| Piece | Today | After |
|---|---|---|
| particles.a, dmxloader.a | client.so | libss_particles.so |
| tier1 members they need | client.so (tier1.a on its link) | libss_particles.so, hidden |
| tier2 stand-ins | client.so | libss_particles.so (or real ports) |
| CNewParticleEffect renderable shell | client.so (native) | client.so, calls the .so to draw |
| IParticleSystemQuery implementation | client.so / managed | unchanged, handed to the .so |
| which system, where, how long; definition reload | managed | managed, through the .so's interface |
| particlemgr.cpp (fork) | client.so | evicted with C7/C5 leftovers; check before starting |

## 5. Spike (do this first)

1. Build libss_particles.so from the two archives plus the tier1 members (from
   the census `<-` rows), with hidden visibility and one exported factory.
2. Start one particle system through it on ss_sandbox.
3. Compare its draw calls against today's path using the render traces
   (src/test/client/Fixtures/render-stock; the recorder in
   shell_render_trace_client.cpp).
4. Measure the extra hop (crossings and time per frame, with the render live
   step's numbers).
5. Report: the interface size, the callback list, the symbol-isolation check,
   and the cost. Then decide whether a lane does it properly.

## 6. Done when

- client.so's census rows `imports:tier1`, `tier0`, `tier2`, `vstdlib`, and
  every `<-particles` / `<-dmxloader` row, read 0.
- `needed-forbidden` reads 0 for client.so.
- Particle effects draw identically: render-trace compare plus the live
  particle steps (7a-effect-gate, fx-impact-gate, render).
- alloc_check: 0 mismatches and 0 foreign frees across the boundary.
- hot-reload and map-reload pass with particle systems alive.

## 7. Spike results (lane particles-so, 2026-10-12, branch client-r-particles-so)

Measured on debug builds of this branch (base client-novalve 53676f29a);
`SS_PARTICLES_SO=OFF` builds the old link for the A/B.

- **Shape.** `src/client/native/particles_lib/` builds `libss_particles.so`:
  particles.a, dmxloader.a, libtier1.a, libmathlib.a, its own tier2 stand-ins
  (CBeamSegDraw/wireframe stubs, CMaterialReference, the index builders), all
  local; the dynamic table exports `CreateInterface` and nothing else. client.so
  dlopens it RTLD_LOCAL and defines the 41 particles.a functions plus
  `g_pParticleSystemMgr` its objects name as forwards
  (`particles_lib_client.cpp`), so particlemgr.cpp and particles_new.cpp compile
  unchanged; CNewParticleEffect still derives from CParticleCollection.
- **Interface.** ISSParticleLib, 45 slots: Connect, Disconnect, Mgr, Counters,
  19 CParticleCollection (incl. new/delete/ctor/dtor), 2 definition, 20 manager.
  Hand-written for the spike; not yet a generated, managed-callable row.
- **Callbacks inward.** IParticleSystemQuery (11 virtuals, handed over by
  CParticleSystemMgr::Init, unchanged); from the factory: ICvar, IFileSystem,
  IMaterialSystem, IMaterialSystemHardwareConfig; process-wide: tier0
  (g_pMemAlloc, spew/assert, vprof, CThread*), vstdlib (RandomFloat,
  KeyValuesSystem). One ConVar is the library's (`r_sse_s`), registered by its
  own tier1 against the engine's ICvar (live: `r_sse_s = 1`).
- **Symbol isolation.** client.so's dynamic exports: CreateInterface, cvar.
  libss_particles.so's: CreateInterface. The library's 111 dynamic imports
  intersect client.so's exports in nothing; every tier1/mathlib copy inside is
  local (4,056 local symbols).
- **Draws (A/B, ss_sandbox, one env_fire_small, 300 frames).** Particle binds
  and draws per frame 3.03 from libss_particles.so (on) vs 3.07 from client.so
  (off), same materials (fire_env_fire 2.0, particle_glow_05_additive 1.0, the
  sporadic fire_burning_character child); client.so's other draws (26.6) and
  the engine's (86/68/64) unchanged. The render call recorder cannot see these
  (it hooks no Bind/dynamic mesh/Draw and keeps only client.so's calls); the
  probe is tools/probes/client/pso_probes_debug.cpp.
- **Cost.** 14.0 ISSParticleLib calls per frame with that one system;
  5.96 vs 3.75 ns per call forwarded vs direct (-O0) -- about 31 ns per frame
  per live system. Managed crossings per client frame (live-gate render,
  dm_lockdown, 2 passes): 45.354/45.233 on vs 45.258/45.260 off.
- **Release cost (full lane, client-r-particles-so on client-novalve 8ebfa6559
  vs 8ebfa6559 itself; both release, probes compiled in for the run only,
  dm_lockdown, `cs_cl_pso_bench 50000000` three times each).** Forwarded
  4.21-7.75 ns per call (median 5.35), direct 3.77-4.61 (median 3.89):
  0.4-1.5 ns more per call, so 6-21 ns per frame per live system at the
  14 calls a system makes.
- **render-plan after a sandbox visit fails on the base too.** `tools/live-gate
  --steps join,render,render-plan,render-plan-sandbox --passes 3` read
  render-plan PASS FAIL FAIL on this branch AND on client-novalve 8ebfa6559
  (SetFrameBufferCopyTexture: stock `-`, plan `_rt_fullframefb`, all three
  frozen poses). With no sandbox visit (`join,render-plan`, 3 passes) it passes
  3 of 3. Not this lane's; reported to the integrator (C7).
- **Allocation.** alloc_check in both modules (the library carries its own,
  tag `/particles`): 0 mismatches, 0 foreign frees in client.so and the
  library, across a join, a particle system, cs_cl_reload and a changelevel.
  CParticleCollection's new/delete both run in the library (g_pMemAlloc).
  Not seen by either checker: g_pMemAlloc blocks; any crossing there was
  already one before the split.
- **Thread pool.** particlemgr.cpp (client.so) owns r_threaded_particles and
  vstdlib's g_pThreadPool; the library starts no thread and is called from
  the pool's workers exactly as particles.a was. Nothing moves.
- **Reload.** A system alive across cs_cl_reload kept simulating; a
  changelevel then started a new one; no fault.
- **Census.** imports:tier0/tier1 `<-particles`/`<-dmxloader` 8/8/20/70 -> 0.
  Still open for client.so: imports:tier0 8, tier1 52, vstdlib 2 and
  needed-forbidden 2 are client.so's OWN imports (C9), not the archives'.

## 8. Full lane status (landed on client-novalve 628901edd, 2026-10-12)

Done: the switch and client.so's old particle link are deleted;
particles_tier2_stubs.cpp lives in the library and its counts reach
`cs_cl_effects_report` through ISSParticleLib::Counters;
`particles_lib_isolation_test` pins the symbol isolation; ISSParticleLib is a
generated, bound interface (ParticleLibVtbl, `SSParticleLib001`, served by the
client's internal factory) and managed reads the throttle through it
(IShimParticlesClient::ParticlesThrottleScaling deleted).

Still open against section 6: client.so's own tier0/tier1/vstdlib imports and
`needed-forbidden` 2 are C9's, not this plan's.

**Managed control-point API deferred until a caller needs it** (integrator,
2026-10-12). Start, stop and the state query are served by working shim slots
(IShimEffectsRenderClient::ParticleSystemStart/Stop/FollowPoints,
IShimParticlesClient::ParticleSystemsRead); renaming them buys nothing. Design
when a caller needs it:
- (A) library slots on an opaque collection handle (set control point
  position/orientation, stop emission, is-finished, particle count), with start
  returning the handle through a NEW appended slot, not a changed one;
- (B) the managed follow feed (managed computing each system's control points
  every frame instead of client.so's follow mask), only if a caller must
  override following, measured against the render budget.

Working notes:
- The A/B instrument is `tools/probes/client/pso_probes_debug.cpp`
  (`-DSS_PSO_PROBES=ON`, debug): draw calls by calling module, and a per-call
  timing of two manager getters. The render call recorder cannot see particle
  draws (no Bind/dynamic-mesh/Draw hooks, client.so callers only).
- A rebase over this lane conflicts in the measured files (shim-reasons,
  shim-ratchet, census ratchets, globals ledger): take the base's side, then
  re-measure at the tree (`make globals-client-ledger`, the shim walk's
  numbers, `tools/client-novalve-census --report`), never pick a side.
- Any test that proves it read client.so's link line by finding particles.a
  must name another archive (libmathlib.a): particles.a is the library's.
