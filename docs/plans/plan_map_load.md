# Plan: faster map loads (material reload, preload, JIT)

Status: APPROVED 2026-10-02 (P0 → P1, stop with numbers, then P3b). P2: match map HDR (user, 2026-10-02).
RE-RANK PROPOSED 2026-10-02 after the remote-client profile (see "Remote clients"); awaiting the user's review.

## Evidence

All numbers come from a release build at 96d57d1 (worktree `../ss-loadprof`), loading in a listen server: cold `dm_lockdown`, `map ss_sandbox`, `changelevel dm_lockdown`, `changelevel dm_lockdown` (same map), `map dm_lockdown` (same map). Everything was in the OS page cache. The render GPU is an RTX 2070 SUPER on a **PCIe 3.0 x1** link (about 1 GB/s), shared with every other game on the box. So GPU waits here are inflated; CPU numbers are not.

| Instrument | What it showed |
|---|---|
| Engine `-profile` (`timestamped.log`), 3 runs per arm | Same-map reload 3.0 s, cross-map 4.3–5.1 s (medians) |
| LD_PRELOAD shim interposing tier0 `COM_TimestampedLog`, backtrace per VMT parse | `SV_SpawnServer` (`engine.so+0x4a0430`) calls `materials->ReloadMaterials(NULL)` unconditionally after `AddSearchPath(bsp)` + `BeginMapAccess`. The call is vtable slot 69, at `materialsystem.so+0x7a450`. |
| Same shim, slot 69 patched to skip that one call (2 runs on, 2 off) | Spawn phase 1.0–1.1 s → 0.43–0.55 s on every load after the first. Same-map VMT parses ~1,690 → 57–80. Material errors unchanged (4). Frames render normally. |
| `perf` (`-F 499`, all threads), run 22 | About half of the main thread's CPU per load (0.31 s same-map, 0.56 s cross, 0.96 s `ss_sandbox`) is `studiorender.so` 0xf3a0..0x105d4, the static-mesh vertex build (`R_StudioCreateStaticMeshes`). The full reload's `RestoreShaderObjects` → `datacache` → `studiorender` path rebuilds every resident model's meshes. |
| `perf`, same run | `libclrjit.so` takes 0.22 s (cold) and 0.27 s (`ss_sandbox`) of main-thread CPU, and the `.NET Tiered Compilation` thread another 0.12–0.35 s. Nothing in the build uses ReadyToRun. |
| `perf`, same run | File reads (kernel `_copy_to_iter` + `filesystem_stdio`) take 0.05–0.10 s per load when warm. |
| Shim main-thread wall-clock sampler (2 ms) | The main thread is on CPU only 35–52% of each load; the rest is GPU waits: the DXVK present in `SwapBuffers`, and `UpdateFrameSyncQuery` in `BeginFrame`. Native↔managed work is 4–10% of the first-snapshot gap. |
| BSP headers | `dm_lockdown` is LDR-only (v19, no HDR lumps); `ss_sandbox` has HDR. Each hop between them runs `EnableHDR` → config update → a second full reload plus a render-target rebuild: `Map_LoadModel` 0.87–1.3 s vs 0.05 s. |

The full report was a private page and is not in the repo.

## Remote clients (measured 2026-10-02, after approval: this re-ranks the phases)

These are TF2Descent's real hops: a client joins the local k3s stack through the gateway at <gateway-ip>:27015 (hub `tf2_descent_town`), then makes 4 hops per session via the admin API (`tf2_descent_travel 1 down|up`).
- The client is built at the pod image's commit, 178bfc1.
- Instruments: the shim timestamps, a 2 ms main-thread sampler, `perf`, and polling of the client's sockets.

**The engine calls `ReloadMaterials(NULL)` only from `SV_SpawnServer`.** It is the only call through the `materials` global in `engine.so`. A remote client never runs it, so **P1 helps only listen servers**: local play and live gates.

Client cost per hop:

| Cost | Evidence | Fix |
|---|---|---|
| **HDR flip** → full material reload inside `Map_LoadModel` | `tf2_descent_town` has HDR lumps; levels are LDR-only. `Map_LoadModel` takes 4.4–6.1 s, or 0.01 s with `mat_hdr_level 1` (hrun-3 vs hrun-4). Wall time per hop: up 11.1/12.1 s → 5.0 s; down 14.1 s → 9.1 s. | P2: match HDR between the town and the generated levels |
| **Level BSP deleted, then re-downloaded** | `[TF2Descent] removed 1 old level file(s) from download/maps` on return to town. On the next down hop the client idles 4.4–4.7 s in CheckUpdatingSteamResources: the sampler shows 95% of main-thread samples in the frame limiter's SDL poll. The prefetcher fetches depth N+1, never the level being re-entered. | New P7: keep level files the player may re-enter (cache keyed by level hash, LRU), and prefetch the down target from town |
| **Particle definitions reparsed** | `ReloadParticleEffects()` (`src/shared/native/particle_manifest.cpp`, `bReloadAll = true`) re-reads every manifest `.pcf` with precache on, every level. `LevelInitPreEntity` takes 1.0–1.5 s of CPU, almost all DMX parsing in client.so (CUtlSymbolTable, CDmxAttribute, msort). `ParseParticleEffectsMap` also runs twice per level. | New P8: reload all only when the previous level loaded map particle overrides, or pure files changed; drop the duplicate map parse |
| ServerInfo wait | Signon 2 → ProcessServerInfo takes about 1.6 s on every hop. This is server/pod side. | Investigate on the pod (host lane) |
| First-snapshot GPU wait | 0.5–1.1 s on the PCIe x1 2070 | none (box) |

**Revised order:**
1. P0 (harness, now including the hosted-client driver)
2. **P2**
3. **P7**
4. **P8**
5. P3b, re-measured after P2: with HDR matched, `Map_LoadModel`'s mesh rebuild is gone
6. P1 (listen servers and gates)
7. P5
8. P6

## Phases

Each phase is its own commit set and lands with tests. Every timing gate is measured, not assumed: a load-time gate is the median of 3 runs of the 5-load sequence, against a same-session baseline. Count the other games on the box first.

### P0. Commit the measurement harness

Move the scratch tools into the repo as `tools/loadprof/`:
- the shim source;
- the driver, today `loadprof.sh`;
- `phases.py`, the sampler analyses and `perfwin`.

Add `make loadprof [CONFIG=release]`, which writes per-load phase costs.

The shim is original work: no Valve header. Committed harnesses find their root from their own location.

Retention test: the analyser must fail on a log that is missing a phase anchor, and on an empty sample set.

### P1. Targeted reload instead of `ReloadMaterials(NULL)` at spawn

**Native (client.so), carrying out the decision only:**
- At client DLL init, patch slot 69 of the `IMaterialSystem` vtable (`VMaterialSystem082`).
- Patch only if the slot's target is the expected function. Compare against the original pointer read at init, and refuse with a log line if the layout differs (an engine update).
- The wrapper forwards every call except `ReloadMaterials(NULL)` inside the spawn window. The window is open between `Set up players(done)` and the world `GetModelForName`; detect it with a flag the server's spawn path sets through an existing hook, not by a return address.

**Managed (client), making the decision:**
- Inside the window, ask managed for the reload set: the material names in the new map's pak plus those in the previous map's pak.
  - Read the BSP pakfile lump as a zip in C#.
  - Same map, same pak hash → empty set.
- Native then calls the original `ReloadMaterials(name)` per entry.

**ABI:** this needs a new bound-interface slot for the managed decision. Add a row in `docs/managed-interfaces.tsv`, regenerate both sides, and add a `chain=` row if needed. No free `SourceSharp_*` function.

**Tests:**
- managed: reload-set computation for same map, different map, a pak that overrides a loaded material, and a missing or corrupt pak (a corrupt pak is a full reload, never a silent skip);
- gtest: the wrapper forwards outside the window and with a substring, and refuses to patch on a slot mismatch.

**Live gate:** keep the second map load. Add a material-override check: a map whose pak overrides a VMT that was loaded before must show the override after the hop. Build that map in the sandbox generator.

**Expected gain:** about 0.55 s per load (prototype), plus the studio mesh rebuild CPU and its GPU re-uploads.

### P2. HDR agreement across a mod's maps

The full reload from an HDR flip is the engine's (`EnableHDR`); we can only avoid triggering it.
- Option A: build `ss_sandbox` (and TF2Descent's generated maps) LDR-only, or HDR to match the maps it hops with.
- Option B: ship `mat_hdr_level 1` in the mod cfg. HDR maps then get LDR+bloom and never flip.

This is a decision for the user. Measure the hop before and after.

**Expected gain:** 0.8–1.25 s per HDR↔LDR hop.

### P3. Keep assets resident across a hop (GPU and CPU). Models: REPLACED by P3b (user, 2026-10-02); materials: still open

- **Keep set:** a mod declares assets to keep, e.g. shared models and materials for a town ↔ level loop.
  - Client: hold `IMaterial` references through a keep list that `UncacheAllMaterials` does not release. They then survive `UncacheUnusedMaterials`, and so do their textures on the GPU.
  - Server: precache the shared models with preload.
- **Gate:** the cross-map hop's world and studio cost (today 1.3 s + 0.55 s when returning to a map) against the same-map numbers. Also gate RSS growth.
- **Order:** P1 first. With the full reload in place, a bigger resident set makes each load slower: the reload cost grows with what is resident (0.23 s → 0.94 s observed).

### P3b. CHOSEN (user, 2026-10-02): cache studiorender's hardware meshes across unload

**MEASURED 2026-10-03: no code needed.** The mesh cache only paid while HDR flipped. On the k3s hub ↔ level hops (`perf` on the client main thread, `studiorender.so` 0xf3a0..0x105d4), with the client at 2f04e2f8 and `mat_hdr_level 1` standing in for matched HDR, mesh building per hop is 0.00–0.04 s. With the flip it was 0.47–0.65 s. Once host e8c677a's HDR levels are live, a cache has under 0.05 s a hop to save, which doesn't justify four hooked slots and a layout parity test. Closed unless a later profile shows otherwise.

**Why `ReloadMaterials(NULL)` rebuilds everything.** It forces `bVertexFormatChanged = true` → `ReleaseShaderObjects`/`RestoreShaderObjects` → mdlcache's release/restore callbacks → `IStudioRender::UnloadModel`/`LoadModel` for every resident model. A substring reload only does that when a material's vertex format really changes.

`perf` (runs 23 and 24, main thread, `studiorender.so` 0xf3a0..0x105d4):

| Load | Stock | P1 prototype |
|---|---|---|
| Same map | 0.29–0.30 s | **0.00 s** |
| → `ss_sandbox` | 1.06 s | 0.65 s |
| Back to `dm_lockdown` | 0.69 s | 0.47 s |
| Cold | 0.38 s | 0.44 s |

What remains after P1 is cross-map: models the previous level's purge unloaded, the HDR-flip restore (removed by P2), and genuinely new models.

**Hook.** Patch `VStudioRender025` (`IStudioRender`, a public SDK header) slots `LoadModel`/`UnloadModel` from client.so, with the same slot-target check as P1.
- `UnloadModel` moves the `studiohwdata_t` contents (LOD array, `IMesh*`s) into an LRU instead of freeing them.
- `LoadModel` with a matching key restores them. The key is model name, studiohdr checksum, VTX checksum, and the material-required vertex format.
- A key or format mismatch calls the original.

**Tests and gates:**
- a parity test on `studiohwdata_t`/`studioloddata_t` layout;
- an LRU cap with RSS/VRAM gates;
- a gtest that a format mismatch rebuilds.

**Limits:**
- cold first loads aren't helped; a cross-run cache would mean porting the mesh build;
- static-prop colour meshes are separate and still recomputed.

**Expected gain:** up to about 0.5 s per cross-map hop after P1, without a declared keep set.

### P4. Threaded preload of the next level's material files

**On CPU:** reads and VMT parsing are small when warm (0.05–0.10 s file reads per load). The win is the cold case: a fresh download, or first play after a reboot.
- Off-thread, read the next level's VMT/VTF/MDL/VVD/VTX into memory, using the level list from the server or prefetch.
- Serve them through a filesystem hook (`IBaseFileSystem` `ReadFile`/`Open` on the bound vtable).
- This is safe off the main thread because it is plain file I/O. `FindMaterial`/`FindMDL` stay main-thread only.
- **Gate:** cold-cache numbers. Needs `vmtouch -e`-style eviction of the game's files, or a fresh boot. Without them this phase is unproven.

**On GPU:** uploads cannot be issued off the main thread through the engine (QMS is off during loads; D3D9 via DXVK is single-device). The levers are P3 (don't evict) and P1 (don't re-upload).

### P5. ReadyToRun for the managed assemblies

**MEASURED 2026-10-03: ReadyToRun cannot take effect as things stand.** A test library (200 methods, `PublishReadyToRun`, net10.0) compiled 0 methods when loaded into a non-collectible AssemblyLoadContext, and 207 in a collectible one: the same 207 as the plain JIT build. ReadyToRun code is ignored in collectible contexts, and `GameLoadContext` is collectible (hot reload). The only way to use it is to load release builds non-collectibly, which gives up their hot reload. That is a decision for the user; no code until then. The cost it would save is the 0.22–0.27 s of main-thread JIT on the first loads only.

**DONE 2026-10-03 without ReadyToRun (user: keep hot reload in release).** `JitWarmup` (src/shared/managed) runs a below-normal-priority background thread at the end of each module's Initialize, calling `RuntimeHelpers.PrepareMethod` on every non-generic method in the side's own load context. It is cancelled and joined first in Shutdown, so it never holds an unload up. Results:
- In the test library, the main thread compiled 207 methods without the warm-up and 6 with it; the context still unloaded.
- Multicore-JIT replay did nothing in a collectible context (201 of 207).
- Live (release, `make loadprof --perf`, main thread): `libclrjit` was 0.066 s on the first load and 0.044 s on the second (0.22 s and 0.27 s before), and 0 after.
- The warm-up itself took 15,510 server methods in 262–289 ms and 3,612 client methods in 88 ms, all off the main thread.
- The live gate's hot-reload step passed both passes, with the warm-up rerunning after each reload.

Crossgen our assemblies, to cut the 0.22–0.27 s of JIT on the main thread in early loads and the tier-up thread's CPU.

Verify first that R2R code is used inside our collectible AssemblyLoadContexts (hot reload). If it isn't, measure the alternatives:
- non-collectible loading for release builds;
- `TieredCompilation` quick-jit settings.

**Gate:** `libclrjit.so` main-thread samples in the cold load, under `perf`.

### P6. Keep the window responsive during loads

**DONE 2026-10-03, narrowed.** The launcher's `ILauncherMgr` header and its `togl` dependencies are not in the farm, and `sdk-links` may only shrink, so the client calls `SDL_PumpEvents` from the libSDL2 the launcher already loaded (`RTLD_NOLOAD`). It runs on the main thread at both level-init edges. That reaches only the steps client.so owns. The long engine-only stalls are removed at their cause instead: the HDR flip (P2: Map_LoadModel 4.4–6.1 s → 0.01 s), the level re-download (P7: 4.5 s → 0.15 s) and the particle re-read (P8: 1.3 s → 0.35 s).

Pump `ILauncherMgr::PumpWindowsMessageLoop()` from main-thread points we own:
- a loading panel's `OnThink`, through `SetLoadingBackgroundDialog`;
- the client level hooks;
- the server's precache loop on a listen server.

This changes no timing; it removes "not responding". Test: SDL events queued during a load are delivered afterwards.

## Not doing

- QueuedLoader or `mod_load_*_async`: disabled on PC or compiled out.
- `mod_touchalldata 0`: moves cost into first-frame hitches.
- `progress_enable 0`: its apparent gain was GPU contention on the shared x1 link.

## Checkpoints for the user

- P3 vs P3b: P3b chosen 2026-10-02. A material/texture keep set (P3's client half) is still open; decide after P1 numbers.
- P2: option A chosen (match map HDR), 2026-10-02.
- P1's live-gate override map is a sandbox generator change.
- Stop after P1 with numbers before P3.
