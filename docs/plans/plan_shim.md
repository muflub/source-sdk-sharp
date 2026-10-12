# plan_shim.md — Closing the native shim

Measured at `85a7449d1` (the fork's working branch, the entity-port merge). Untracked until reviewed.

Three asks, in the order they will be executed:

1. **Pre-pass:** clean the three C# project scripts — `server_csharp.vpc`, `client_csharp.vpc`,
   `sourcesharp.vpc` — of precompiled-header logic, protobuf, and every platform that is not
   Windows, Linux or macOS on x64, and regroup their `$Folder` trees so a row's folder says what
   the file is for.
2. **The stock rows:** every `$SRCDIR\game\server`, `$SRCDIR\game\shared` and `$SRCDIR\public`
   row still compiled into `server_csharp.vpc` (25 translation units, 24,100 lines) either moves
   to managed code, is deleted as dead, or — for the handful the engine genuinely needs
   natively — is relocated under `src/sourcesharp/` so the vpc carries **zero** stock rows.
3. **The shim pass:** every file under `src/sourcesharp/` (360 files, ~187,000 lines including
   the client shim and the host) is ruled KEEP (the engine needs it natively, with the reason
   letter), MOVE (to a named managed home), SPLIT, or DELETE; what stays is simplified.

The per-file rulings live in `plan_shim_rulings.tsv` (439 rows, twelve read-only lanes,
one lane per file group, every row with its consumer counts, entry points, ConVars, gtests and
reasoning). This document is the plan over that table; it does not repeat the table.

---

## 0. Where the tree stands, and three build facts that change the plan

### 0.1 Sizes (this session, `wc -l`)

| tree | files | lines |
|---|---|---|
| stock rows still in `server_csharp.vpc` (game/server 12, game/shared 11, public 3) | 25 | 24,100 |
| `src/sourcesharp/game/server` | 133 | 56,975 |
| `src/sourcesharp/game/shared` | 89 | 33,171 |
| `src/sourcesharp/game/client` | 107 | 57,737 |
| `src/sourcesharp/public` + `common` | 5 | 3,753 |
| `src/sourcesharp/host` (`sourcesharp.h` is 10,502 lines, 7,899 of them comment) | 5 | 11,760 |
| managed: Server 64,779 · Shared 16,280 · Abi 78,133 · Tests 151,713 lines | — | — |
| test baselines: 4,068 gtests (`TEST(` lines), 5,356 `[Fact]`s | — | — |

### 0.2 The rulings, summed (lane TSVs; split halves counted on their own side)

| verdict | lines (≈) |
|---|---|
| KEEP | 113,000 |
| MOVE | 14,700 |
| DELETE | 19,300 |

Lane C1's TSV carried the whole-file count on each split half of
`shell_standalone_client.cpp` (7,400 on `#move`, `#delete` and `#stay` alike) and on three
smaller files; the totals above use the line ranges from `C1-notes` instead
(≈1,300 move, ≈1,738 delete for that file). The raw column sum is KEEP 116,664 / MOVE 22,160 /
DELETE 25,440. Either way: **about a third of the native tree leaves**, and most of what leaves is
dead rather than moving.

### 0.3 Three build facts every phase depends on

**The object directory lies.** `src/_vpc_/ninja/debug/objs/server_sourcesharp/` holds 190
objects and the debug ninja graph links 144 of them; the client directory holds 327 and links
272. The 46 + 55 leftovers (`baseentity.o`, `entitylist.o`, `gamemovement.o`, `util.o`,
`gib.o`, `c_baseentity.o`, `cdll_client_int.o`, …, dated 09-03/09-05) are translation units the
entity port evicted. Every lane that counted consumers over the directory over-counted, in
places by 2–13× (N2: mapentities 13→6, util_shared 7→3; S4: three "absent" files whose only
referrers were ghosts). Every ruling in the TSV was re-filtered against the link line; the
plan's **first executable step is a cold rebuild and a census tool that reads the ninja graph,
never the directory** (Phase 1a). Memory rules `ld-stops-naming-objects` and
`nm-without-symbol-type-inflates` already say this; this is the third instrument that needed it.

**`server_base.vpc` and `client_base.vpc` are not built.** `grep -c server_base
sdk_csharp_release.ninja` → 0. They are kept for the upstream-drift watermark
(`vpc_scripts/*.watermark`, `vpc_watermark_gate_test.cpp`) and are read by 22 gtests. Three
`src/sourcesharp/game/server` files live only in `server_base.vpc`
(`entity_bases.cpp`, `physics_shell.cpp`, `shell_abi_sdk_fixture.cpp`) and are therefore not
compiled into anything.

**Protobuf is dead and the precompiled header is a forced include.**
0 `.proto` rows in either C# vpc; 0 objects on either side import a `google::protobuf` symbol;
`libprotobuf.a` reaches the link line only through `game/protobuf_include.vpc`. The ninja
generator turns `$Create/UsePrecompiledHeader "Use …"` into `-include <pch>/cbase.h` on every
compile line not exempted per file (`physics.o` has it, `bone_setup.o` does not).

---

## 1. The one test, restated

Native code is allowed only where the engine leaves no choice (`plan.md` §1a). The lanes ruled
with the same eight letters, and the rulings table cites one per KEEP row:

| | reason | example in the tree |
|---|---|---|
| a | a vtable the engine or VPhysics calls through | `CStandaloneEntity`, `CCollisionEvent`, `IPartitionEnumerator` |
| b | an interface resolved by name at load | `ServerGameDLL012`, `ServerGameClients004`, `PlayerInfoManager002` |
| c | static-init registration | `EXPOSE_INTERFACE`, `DECLARE_HUDELEMENT`, `IGameSystem` |
| d | a layout the engine reads at fixed offsets | the engine block, `SourceSharpEntitySlot_t`, `CPlayerState` |
| e | a synchronous mid-step answer | `ShouldCollide`, `ShouldSolvePenetration`, `IMotionEvent::Simulate` |
| f | a bit-level wire codec | `usercmd.cpp`, SendTable proxies, the save block framing |
| g | leaf math over mdl/bsp data consumed by renderer or physics | `bone_setup.cpp`, `collisionutils.cpp`, the sprite/rope mesh builders |
| h | the CoreCLR host and bootstrap dispatch | `host/sourcesharp.cpp` |

"It was convenient", "it runs every frame" and "it would be slow in C#" are not on the list
(memory: `move-first-measure-after`, `perf-pass-comes-after-the-port`). (g) is the weakest
letter and the plan says so wherever it is the only reason.

**Two corrections to `plan_entity.md` §6 that the lanes measured:**

- **`IGameMovement` is not resolved by name.** `strings engine.so | grep -c GameMovement` → 0
  (lane H1). The row's reason was wrong; the shell stays as the object `IMoveHelper`/prediction
  hand around, which is (a), not (b).
- ~~**`physics_fx.cpp` is policy.**~~ **WITHDRAWN — the premise was false.** This document said
  "§6 ruled it in; its own test says out", and lane 2-physics checked: **both** of its gtests
  assert it STAYS native, across eight assertions. The file is relocated under (g), not moved.
  I wrote that premise from lane N1's summary without opening the tests; it is the one place in
  this plan where a ruling rested on a claim nobody had verified.

---

## 2. Phase 0 — the VPC pre-pass

All of Phase 0 is mechanical, changes no code path, and is gated by **a byte-identical build**:
the set of compile and link edges in the regenerated ninja graph is the same set, in a different
order, and every object still compiles. It goes first because every later phase edits these
files, and a row that moves in a clean tree is a one-line diff.

### 0a. Protobuf out

- Remove `$Include "$SRCDIR\vpc_scripts\protobuf_builder.vpc"`, `$Include
  "$SRCDIR\game\protobuf_include.vpc"` and the `GENERATED_PROTO_DIR` macro from
  `server_csharp.vpc` and `client_csharp.vpc`.
- Gate: link succeeds without `libprotobuf.a`; `nm -u` over every live object still shows 0
  `google::protobuf` imports (the check that can fail: add the include back and the ninja graph
  grows a `.a`).
- Test: one gtest, `vpc_no_protobuf_test.cpp`: neither C# vpc names `protobuf`, and the
  generated ninja's link line for `server.so` and `client.so` does not name `libprotobuf`.
- `stock_serverclass_census_test.cpp:462` mentions protobuf in a comment only; untouched.

### 0b. Precompiled header out

- Remove `$Create/UsePrecompiledHeader`, `$Create/UsePCHThroughFile`, `$PrecompiledHeaderFile`,
  every per-file `"Not Using Precompiled Headers"` block, and the two "Precompiled Header"
  folders with their `stdafx.cpp` rows (server `stdafx.cpp` is `#include "cbase.h"` and nothing
  else — delete the file; client `stdafx.cpp` row at `client_csharp.vpc:249`, same treatment
  after reading it).
- Consequence: every TU sees only what it includes. Eleven `src/sourcesharp` TUs and three
  `src/public` TUs carry no `#include "cbase.h"`; the three public ones are already exempt and
  compile clean; the eleven (`randoverride`, `steamid`, `physics_shell`, `shell_gamedll`,
  `bone_library`, `overlay_library`, `shell_acquire_reasons`, `vtable_hook`, `dt_send`,
  `dt_utlvector_common`, `studio`) currently receive the forced include on the server side.
- Gate: cold build of debug and release in the container (`native-only-build` recipe). Each
  failing TU is fixed by adding the includes it uses — **never** by adding `cbase.h` to a file
  whose header says "no Source headers on purpose" (`vtable_hook.cpp`).
- Tests that pin the old shape and are rewritten, not patched:
  `client_pch_prelude_test.cpp` (10 TESTs, the whole file is about the forced prelude; it
  becomes three: "no vpc names `PrecompiledHeader`", "no `stdafx` row", "every TU in either vpc
  that names a Source type includes a Source header of its own"),
  `bone_leaf_rehome_ruling_test.cpp:296-310` and `leaf_cycle_test.cpp:712-725` (assert the
  "Not Using" block; flip to "no PCH directive exists").
- Memory: `pch-force-include-makes-unreachable-edges` was misnamed — the PCH was never the cause
  of that defect; nothing here depends on it.

### 0c. Platforms: Windows, Linux, macOS, x64 only

Counted across the three scripts: `[$X360]` 10, `[$WINDOWS||$X360]` 1, `[$WINDOWS&&!$X360]` 2,
`[$WIN32]` 1, `[!$WIN64]` 2, `[$BUILD_REPLAY&&$WINDOWS]` 1, `[$LINUXALL&&!$SOURCESDK]` 1.

- Delete every `[$X360]` line (the `_360` output-directory blocks, `DEVKITBINDIR`); rewrite
  `[$WINDOWS||$X360]` and `[$WINDOWS&&!$X360]` to `[$WINDOWS]`.
- Delete `[$WIN32]` lines. The `steam_api` import row is `[!$WIN64]` today, which is TRUE on
  POSIX — it is what puts `libsteam_api.so` on the Linux and macOS link lines — so it is rewritten
  to `[!$WINDOWS]`, not deleted; keep the `[$WIN64]` `steam_api64` row. (The first draft of this
  step said delete; the Phase 0 lane's platforms test caught it.)
- `[$BUILD_REPLAY&&$WINDOWS]` and `USE_WEBM_FOR_REPLAY`: replay is a stock client subsystem;
  whether `replay/cdll_replay.cpp` is still a row in `client_csharp.vpc` is checked at this step
  (the release ninja compiles `cdll_replay.o`); if it is, it is a Phase 4 row, and the two
  replay conditionals go with it, not here.
- Keep `[$WINDOWS]`, `[$WIN64]`, `[$LINUXALL]`, `[$POSIX]`, `[$OSXALL]`.
- `vpc_scripts/*.vpc` (the SDK's base scripts, 3 X360 lines in `source_dll_base.vpc`) are
  upstream infrastructure shared by projects this tree does not build; **out of scope**, stated
  so a later reader does not think it was missed.
- Gate: same byte-identical-graph check on Linux; a Windows generation is not available here and
  is listed as unverified.
- `effect_tus_evicted_test.cpp:579` treats `_X360` as a dead token in *sources*; unaffected.

### 0d. Folders that say what a row is for

`$Folder` names carry no build semantics (the ninja has none of them), and no gtest matches a
folder name — 108 tests read vpc text, all of them match `$File` rows; two count braces
(`effect_tus_evicted_test.cpp:449`, `tempent_evicted_test.cpp`) and twelve read the comment
lines next to rows (memory: `vpc-prose-is-load-bearing`). Rule for the regroup: **a row moves
with its comment block, verbatim; the multiset of `$File` rows before and after is identical
(`grep '\$File' | sort | diff`)**.

Today `server_csharp.vpc`'s "SourceSharp/Shared" folder holds 55 server-only files, "Server"
holds 34, and "temporary entities" and "Tools Framework" are empty. Proposed tree, same for
both sides where the side has the files:

| folder | server rows | client rows |
|---|---|---|
| `Engine binding` — interfaces resolved by name and the runtime adapter | shell_gamedll, shell_gameclients, shell_gameents, shell_gametags, shell_playerinfo, shell_server, shell_sharedappsystems, shell_engineglobals, shell_engineinterfaces, managed_runtime_server, shell_frameshim, shell_framesteps, shell_levelshim, shell_levelsteps, shell_serversystems, shell_steamcontext, shell_mdlcache, shell_stringtables, shell_files, shell_filesystem, shell_templates, plugin_check, shell_commandclient, shell_clientsteps, shell_transmitsteps, shell_restoreentities, shell_playerslots, shell_playerlocaldata | managed_runtime_client, shell_client, shell_clientdll, shell_clientdll_*, shell_clientsystems, shell_prediction, shell_input, shell_stringtables_client |
| `Entity shell` — the vtable, the slot table, the world | shell_standalone, shell_standalone_base, shell_standalone_class, shell_standalone_hierarchy, shell_standalone_player, shell_dispatch, shell_scripted, standalone_relays, toucher, precache_model, baseentity_statics, sendproxy, ServerNetworkProperty, shell_entityslots, shell_entityapi, shell_nettable, shell_world_nettable, world_adapter, world_subject_dll, shell_globals | shell_standalone_client, shell_cliententitylist, shell_entitystatics, shell_recvtable, shell_world_nettable_client, world_adapter_client, shell_playerresource, shell_player_orphans |
| `Physics shell` | shell_constraints, shell_motioncontrollers, shell_physicsobjects, shell_worldquery, shell_breakpieces, shell_areaportal, shell_propdata, shell_particlequery, and — until Phase 2 empties them — physics, physics_fx, physics_shared, ragdoll_shared, collisionproperty | ragdoll_library, shell_worldquery_client |
| `Effects and temp entities` | shell_tempents, shell_te_fwd_*, shell_decals, shell_vieweffects, shell_debugoverlays, shell_recipientfilter, shell_usermessage_send, EffectsServer | shell_effects_sim, shell_te_*, shell_tempents_client, shell_*_sim, shell_*_render, shell_sprite_leaf, shell_impact_effects, shell_effect_ledger, shell_beam_endpoint_client, shell_explosion_overlay, shell_worldrender, shell_renderable_facts, shell_materialproxies, shell_view_facts, shell_recipientfilter_client |
| `Sound` | shell_sound, shell_soundent, shell_soundenvelope, shell_soundscape, shell_sentences | shell_soundscape_client |
| `HUD` | — | shell_hud, shell_hud_framework, shell_weapon_scripts |
| `Absent subsystems` — link-time stubs, empty at the end (`plan_entity.md` §3.13) | shell_*_absent, shell_*_orphans, server_util_library | shell_client_orphans, client_util_library, shell_bone_leaf_library |
| `Shared (both DLLs)` — the 30 rows also in `client_csharp.vpc` | sourcesharp_gamesystem, shell_movement, shell_diagnostics, shell_gamerules, shell_savegame, shell_vtable_interfaces, shell_shared, shell_internalfactory, shell_usermessages, shell_usermessage_registry, shell_gameevents, shell_benchmark, shell_acquire_reasons, standalone_player_sync, world_net_sync, sound_emitter, shell_propdata_system, shell_physsaverestore_absent, shell_animlists, particle_manifest, generic_payload, entity_shared_statics, trace_filter_library, util_shared_library, takedamageinfo_library, bone_library, overlay_library, randoverride, steamid (deleted in 1e), vtable_hook (server only) | same rows |
| `Leaf math` — reason (g) | bone_setup, collisionutils, studio, dt_send, dt_utlvector_common, animation, stringregistry | studio, dt_utlvector_common |
| `Stock (leaving in Phase 2)` | AI_Criteria, game, globalstate, mapentities, saverestore_gamedll, textstatsmgr, timedeventmgr, GameStats_BasicStatsFunctions, gamestringpool, movevars_shared, mp_shareddefs, predictableid, takedamageinfo, usercmd, util_shared | the 200 stock `game/client` rows, grouped by the same rule when Phase 4 touches them |
| `Link Libraries` | unchanged | unchanged |

Naming the stock folder "leaving" is deliberate: a row that is still there at the end of Phase 2
is a visible defect, not a category.

**On-disk subfolders were considered and are not in this pass.** Moving the files into
directories named like the folders costs 663 `#include` lines that name a shim header by bare
name, 915 path strings in gtests and 90 in managed tests — a mechanical churn that would
land in the same commits as real code moves and make every later diff unreadable. It is
offered as a separate, optional Phase 6 step once the tree has stopped shrinking.

### 0e. The orphan scripts and the three uncompiled files

- `server_base.vpc` / `client_base.vpc` stay: they are the watermark's subject, not this
  project's scripts. The three `src/sourcesharp/game/server` files that exist only there are
  deleted in Phase 1 (rows in the rulings table: `entity_bases.cpp/.h`, `physics_shell.cpp`,
  `shell_abi_sdk_fixture.cpp`; `physics_shell.cpp`'s eight static asserts move into
  `physics_shell.h`, where the gtest leg compiles them).
- `sourcesharp.vpc` (the host): no PCH, no platform conditionals, one `-$File memoverride.cpp`.
  Nothing to do; the check in 0a–0c runs over it so the gate proves that.

### 0f. Link libraries nobody imports from

**CORRECTED 2026-09-20, and the original reading would have broken the build.** The measurement
below was taken as "`nm -u` over our live objects, joined against each library's defined symbols",
which sees only DIRECT demand. It concluded that `choreoobjects.a` and `dmxloader.a` were both
unimported on both sides and that both rows could go. **Re-tested by actually dropping the rows and
linking:**

| library | direct demand from our objects | drops? |
|---|---|---|
| `choreoobjects.a` | **0 objects, both sides** | **YES — verified, links clean on both sides in debug and release** |
| `dmxloader.a` | **0 objects, both sides** | **NO — `ld` fails with ~10 undefined `CDmxElement::`/`CDmxAttribute::` references** |

`dmxloader` is demanded **transitively**: the members `particles.a` pulls in
reference `CDmxElement::GetAttribute`, `LockForChanges`,
`AddAttribute`, `UnpackIntoStructure` and friends. **A zero direct-import reading does not mean a
library is droppable, because another archive's pulled members can demand it.** `dmxloader` leaves
when `particles` does and not before.

`choreoobjects` is genuinely gone — that row is removed from both scripts.

The direct-demand figures for the rest, re-measured on the Phase 4 tip against the link line
(108 server / 253 client objects), with a positive control asserted (`AngleMatrix(QAngle const&,
matrix3x4_t&)` must be undefined in our objects and defined by `mathlib.a`; the first run of this
instrument returned "no symbol demanded" for **every** library because the undefined-symbol parse
kept `nm`'s leading whitespace, and an all-zero answer is an instrument report, not a result):

| library | server | client |
|---|---|---|
| `mathlib.a` | 55 | 105 |
| `tier2.a` | 19 | 41 |
| `particles.a` | 11 | 41 |
| `tier3.a` | 8 (14 objects) | 27 (39 objects) |
| `tier1.a` | — | 199 |
| `vgui_controls.a` | — | 751 |
| `bitmap.a` | — | 4 (`camomaterialproxy`, `view`, `viewpostprocess`) |
| `vtf.a` | — | 2 (`view`) |
| `matsys_controls.a` | — | 1 (`shell_clientdll`, `vgui_int`) |

**The three nearly-droppable client rows are each gated on named files leaving**, which is what
makes them actionable rather than aspirational: `matsys_controls` on two objects, `vtf` on one,
`bitmap` on three. And `tier3`'s 39 client demanders include `ClientScoreBoardDialog.o` and
`SpectatorGUI.o` — the two panels the user has already ruled do NOT stay native — so that row
shrinks when Phase 4's playerresource work lands.

`steam_api` is **not** a `$Lib`; it is an `$ImpLibexternal` (a shared-library import) and it is
genuinely demanded: **5 undefined Steam symbols on the server, 8 on the client**
(`SteamInternal_CreateInterface`, `SteamAPI_RegisterCallback`, `steamgameserverapicontext`…). It is
not removable while those calls exist. The rest of the `$Lib` list is real today and is
revisited at Phase 6 (§8): `mathlib.a` (60 symbols / 32 server objects, 110 / 70 client — the
leaf math and every matrix op), `particles.a` (server: 11 symbols from `particle_manifest`,
`shell_engineinterfaces`, `shell_levelsteps` only), `tier2.a` (server: `g_pFullFileSystem` in
two files and Connect/Disconnect), `tier3.a` (server: `mdlcache` in 11 objects, four
`studiohdr_t` helpers, `CJiggleBones`), `libsteam_api.so` (server: **one** consumer,
`shell_steamcontext.o`, 4 symbols; the `steamgameserverapicontext` it fills is read by four
objects and seven ABI slots managed calls), `libmanaged_host.so` (the host, reason h).

### Phase 0 exit

- Regenerated ninja: same edge set, same object set, `make check` green at 4,068 / 5,356 with
  zero warnings, cold release build in the container, `tools/live-gate` two passes.
- One commit per sub-step (0a, 0b, 0c, 0d, 0e), each with its test.

---

## 3. Phase 1 — delete what is dead

Everything in the DELETE column goes before anything moves, because a move whose caller is dead
is wasted work, and because the delete column is the larger one. Order is by what unblocks
what.

### 1a. Instrument first: a census over the link line

A committed tool, `tools/shim-census`, that for every object **on the debug ninja link line**
prints defined symbols with type and undefined symbols, joins them, and answers: for file X,
which live objects import anything X defines; for symbol S, who imports it. Output is a TSV
keyed by object; the tool refuses to run if the object directory is newer than the ninja file
or the graph names an object that is missing (memory: `a-check-log-has-no-provenance`). It
stamps the tip. Every lane below re-measures with it before deleting; the numbers in the rulings
table are the lanes' and are stale the moment Phase 0 lands.

Prerequisite: cold rebuild (`ninja -t clean` in the container, then debug and release), which
also removes the 101 ghost objects.

### 1b. Absent stubs and orphans (lane S4, 1,638 lines → 0)

Delete outright: `shell_tools_absent`, `shell_toolframework_absent` (+1 line at
`shell_gameclients.cpp:538`), `shell_vscript_absent`, `shell_subsystems_absent`,
`shell_ai_orphans`, and the dead halves of `shell_ai_absent` and `shell_entity_orphans`.
What survives is one `shell_absent.cpp` (~100 lines) that Phase 2 empties: three stubs die
with `mapentities.cpp`, six AI-stub calls die with lane S2's dead-arm deletion. Test files
`tools_absent_test.cpp` and `toolframework_absent_test.cpp` retire with the files.
Open question carried into the step: `CEnvMicrophone::OnSoundPlayed` returns false while
`sound_emitter.cpp` still calls it and a managed `EnvMicrophone.cs` exists — a live map with an
`env_microphone` settles whether the managed entity ever hears anything, before the stub is
touched.

### 1c. Bring-up instruments with no driver

The lanes found ≈8,600 lines of probes, ledgers, recorders and dev commands whose only readers
are the gtests that pin their strings:

| cluster | lines | lane | what must happen first |
|---|---|---|---|
| ~~`shell_steporder.h`~~, `shell_gamedll_refusals.h` tally, `cs_sv_dirty_stats`, the fs-trace hook (`shell_filesystem.cpp` + `vtable_hook`'s clone/swap half), `shell_benchmark.cpp` | **DONE, ≈3,204 lines deleted** (lane 1c-sv, `3e5cb7460`) | S2, H1 | three of this row's entries turned out to HAVE drivers and were kept — see below |
| `shell_diagnostics.cpp` assert stack, gcov, benchmark, `shell_boundary_probe.h`, `crossings_fetch.h`, `shell_notshipped.h` | 2,728 → ≈450 | H1 | `cs_*_crossings` (8 tools, live-gate, DevApi) and `cs_cl_notshipped` (tools 7a/7c) get managed ConCommands over the same counters before the native ones go |
| `shell_standalone_client.cpp:2798-2890, :5757-7401`, `shell_cliententitylist.cpp` census blocks, `shell_clientdll_probe.h`, `client_seam_census.h`, `shell_entitystatics.h` ruling table, `shell_recvtable.cpp` dumps, `shell_view_facts.cpp` reports | ≈2,900 | C1 | capture one `shell_clientdll_probe` report into `docs/engine-boundary.md` first — it is the evidence for which of the 79 `IBaseClientDLL` slots the engine calls |
| player shadow instrumentation, skeleton probe/replay, payload console, invisible-reach ledger (`shell_standalone_*.cpp`, `shell_scripted.cpp`) | ≈1,200 | S1 | `tools/live-gate:4158` runs `cs_sv_boneprobe seq 3`; a managed bone recorder lands first, or the gate step is shown optional |
| `physics_shell.h` ledgers and IVP audit, `SS-PHYSWORLD` scaffolding in `physics.cpp:700-1385` | ≈530 + ≈1,040 | S3, N1 | none; both are off by default |
| `shell_tempents.cpp` `cs_sv_te_dump`, scratch writer, `SuppressFilter`, empty prediction system | ≈520 | S3 | none |

20 of the 21 client-group ConCommands and 34 of the server entity shell's are instruments; the
findings they produce belong in `docs/` or in `SourceSharp.DevApi`, which exists.

**Three things this row claimed were driverless and are not** (lane 1c-sv measured each):

- **`assert_stack.h` stays.** It holds `SourceSharp_AssertShouldBeFatal`, the decision behind
  `-assertlog`, which **31 tools** pass on every launch and which only this file reads. Deleting it
  would kill every live gate on the first assert. Only its verdict half went (607 → 471 lines).
- **`cs_sv_transmitset` stays.** `tools/p4-exit-probe-skybox` runs it as the A/B for the 3D-skybox
  bug and `client_data_walk_test.cpp:40,293` pins the coupling. `shell_steporder.h` therefore
  shrank 354 → 160 rather than being deleted.
- **The refusal *sites* stay**; only the tally went. There are **6** refusal sites, not the
  sixteen this file's own prose claimed — lane S2 was right and the header was stale in three
  places. Likewise **41** `RecordStep` sites, not 44.

### 1d. Dead stock translation units (lane N2, nine files, 1,998 lines)

`AI_Criteria`, `game` (its ConVar accessor is the live one — `shell_gamedll.cpp:1459-1480`'s
identical copy is the dead one; delete that copy and keep the 17 ConVar declarations moving in
Phase 2), `saverestore_gamedll`, `textstatsmgr`, `timedeventmgr` (and
`CServerNetworkProperty::m_TimerEvent` with it), `GameStats_BasicStatsFunctions`,
`mp_shareddefs`, `predictableid`, `takedamageinfo`. Eight gtests pin these rows on a now-false
premise (`saverestore_evicted_test.cpp:199`, `map_residue_factories_test.cpp:388`,
`util_shared_library_test.cpp:167,175`, `takedamageinfo_library_test.cpp:232`,
`server_wave2_lifts_test.cpp:465`, `client_pch_prelude_test.cpp:415-417`,
`shared_random_move_test.cpp:218`) and retire with the rows.

### 1e. Dead shim files and dead ABI slots

- Nine files dead in the live graph (lane S1): `shell_abi_sdk_fixture.cpp`, `entity_bases.cpp/.h`,
  `ServerNetworkProperty.cpp` (after `shell_gameents.cpp:369/435` stops casting to it — a
  one-map-load `dynamic_cast` assert proves every edict's networkable is the shell),
  `notify_gate.h`, `push_blocker_rule.h`, `shell_parent_frame.h`, `shellless_find.h`; plus
  `handle_cross.h` (327 lines + 6 gtests, dead since `929085b6a`), `gib_head_throw.h`,
  `ragdoll_retire_rule.h`, `collision_rules_recheck.h`, `world_aligned_bounds.h`,
  `npc_filter_rule.h`, `shell_physsaverestore_absent.cpp` (lane H2, 779 lines).
- ABI slots with no managed caller: 29 stub bodies in `shell_entityapi.cpp` (23 unreached, 6 with
  callers that receive nothing — those 6 call sites are fixed in managed first), 26 in lane S3's
  group (15 in `shell_worldquery`, five `*Count`/`*IsValid` counters, three overlay, and
  `ModelBreakPiece(Count)`, which is verified at runtime by breaking a prop before it goes), 26
  `ManagedApi` slots with no native call site (lane C3), 5 dead HUD weapon slots, 14 weapon
  stubs in `sourcesharp_gamesystem.cpp:610-728`, 5 retired mover/explosion slots in lane S1's
  group. Each retirement deletes the `Abi.cs` field and the `Engine.cs` wrapper too.
  **One `SOURCESHARP_ABI_VERSION` bump per integration, by the integrator** (memory:
  `abi-version-bumps-collide`); lanes append to a shared retirement list, never bump.
- Stale copies: `steamid.cpp` (740 lines, its only consumer feeds a function that returns NULL),
  `stringregistry.cpp` (after `shell_soundscape.h:49` becomes a `CUtlDict<int>`),
  `animation.cpp`'s ≈900 dead lines (all 1,093 once 3f lands).

### 1f. The temp-entity forwarders (lanes S3 and C2)

Server: 40 `TE_*` forwards across eight `shell_te_fwd_*.cpp` (2,388 lines); 22 have no live
caller; the 18 live ones become one `shell_te_forwards.cpp` (~250 lines) with the
`SourceSharpTe_Forward` extern declared once in `shell_tempents.h`. Client: 52 `TE_*` entry
points across nine files, 0 managed callers, 3 reachable (`te->Decal/Bubbles/Smoke`); `c_te.cpp`
plus 2,469 lines become one ~100-line file. `tempent_forwards.h`'s one-number-per-entry table
shrinks to match, and `tempent_evicted_test.cpp` is rewritten to the new count.

### The vpc scripts carry no prose (standing rule)

**The three `.vpc` scripts carry `$File` rows and directives only — no multi-line comment blocks,
and no comments at all where a row's reason can live elsewhere.** The reasoning for a relocation,
a deletion or a platform condition goes in this plan, in `docs/`, or in the relocated file's own
header, which is where a reader looking at the code will find it.

Measured before the rule was applied: 32 comment lines in the server script and 22 in the client,
with runs up to 18 lines — commit messages embedded in a build file. Removing them changed no
`$File` row, left braces balanced at 22/22 and 26/26, and all five vpc-reading gtests stay green
(`vpc_folders`, `vpc_no_protobuf`, `vpc_platforms`, `tempent_evicted`, `effect_tus_evicted`).

Two reasons this is not merely tidiness. A `$File` row and its comment travelled together through
every Phase 1 and 2 merge, which is a second thing to get right in a conflict for no benefit. And
`vpc_folders_test`'s loose count is a **substring search over the whole script**, so a comment that
names a row keyword and a shim path on one line adds itself to the count and reddens a test about
the file list — a hazard the removed prose had to warn about at length, and which now cannot occur.

### Phase 2 — four more rulings overturned by measurement

Lane 2-physics re-measured before acting, as every lane is required to, and contradicted four:

- **`physics_fx.cpp`**: MOVE → **KEEP/relocate**. See the withdrawn correction above.
- **`collisionproperty.cpp`**: DELETE → **RELOCATE**. It has three live external symbols, a live
  `IPartitionQueryCallback` read-lock, and a test that already enforces a STAYS ruling.
- **The `SS-PHYSWORLD` scaffolding**: "no tool or managed caller, delete ~1,040 lines" → **8 tools
  drive it across 27 sites**, `tools/defect-shadow-probe` among them. A `ConCommand` is reached by
  NAME, and the link-line census cannot see that. Same blindness that produced the wrong
  reachability set in Phase 1 — this is now the third distinct way a symbol census has read a live
  thing as dead.
- **The physics event ring**: "declares 13 kinds, records 6" → declares **12**, recorded **5**.
  Now records 11: FRICTION, FLUID start/end, trigger enter/leave and CONSTRAINT_BROKEN, the last
  of which needed one line and no new ABI slot.

**All four experiments answered.** Penetration granularity is *not* identical — a managed pair set
is one tick late, but only on the transition tick, which follows three seconds of existing
penetration and is therefore invisible; the live confirmation is still owed because the managed set
does not exist yet. The client has **no** friction ring, so those bodies stay native. There is **no
double shake**: the native one had zero importers and is deleted. `RegisterConVar` does preserve
`FCVAR_REPLICATED` through to the tier1 constructor; whether the engine transmits a *dynamically*
registered replicated ConVar still needs a client join.

### Phase 1 wave A — what actually happened

Executed 2026-09-19/20 on this plan's integration branch (created at the Phase 0 tip), six lanes in worktrees.
Measured outcome after five merges:

| | Phase 0 tip | after wave A |
|---|---|---|
| server objects linked | 143 | 125 |
| client objects linked | 271 | 264 |
| managed facts | 5,890 | 5,903 |
| orphan candidates | 66 | 55 |

**Four rulings in this document were wrong, and the lanes' measurements overrode them:**

- `assert_stack.h` is **not** driverless: it holds the decision behind `-assertlog`, which 31 tools
  pass on every launch. Only its verdict half went.
- `cs_sv_transmitset` is **not** driverless: `tools/p4-exit-probe-skybox` runs it as an A/B.
- The refusal sites number **6**, not the sixteen this tree's own prose claimed; `RecordStep` sites
  number **41**, not 44.
- The seventeen ConVars in `game.cpp` do **not** all lack readers: seven have live by-name
  consumers (managed, the engine, and `cfg/settings.scr`). All seventeen stay until Phase 5.

**Two behaviour changes were made deliberately and are not regressions of anything live:**
`EntityShot_TraceAttackToTriggers` had zero importers and went, so a bullet no longer tells a
trigger it crossed it (restoring it is a feature needing an entities-along-ray slot); and
`drawline`/`drawcross` became retired managed commands because the ABI has no debug-line
primitive, restored by the `AddLineOverlay` append in the ABI tail.

**What was kept that this document expected to delete**, each because a live gate or tool drives
it and the replacement is a later step: the bone probe and its case file, the client-DLL slot
probe (its report must reach `docs/engine-boundary.md` from a live run first), the boundary probe,
the crossings commands, and `cs_cl_notshipped`.

### What lane 1f found that the rulings got wrong, and why it matters

The forwarders lane re-measured C2's reachability set before acting on it, as the brief required,
and **the count was right while the set was wrong**. C2 reported three reachable client forwards
and named `te->Decal` as one. The three are `te->Bubbles`, `te->Smoke` and `te->DispatchEffect`:
C2's cited Decal caller is `baseentity_shared.cpp`, a **server** object that is not on the client
link line at all. Acting on the named set would have kept one dead body and deleted one live one,
and the count would have matched either way.

Two more of the same shape: S3 lists `baseentity_shared.o` as a `TE_Decal` importer when only
`world_adapter.o` is, and a file header cited `te.cpp` callers for `TE_Fizz` when `te.cpp` is not
on the server graph at all.

**The rule this justifies:** a consumer count is not a consumer set, and only the set can be acted
on. Every deletion lane re-measures the set, never the number.

Two deviations from this document's estimates, stated rather than absorbed: the collapsed
forwarders are 939 and 328 lines against the "~250" and "~100" estimated here, because every body
and its per-body comment moved verbatim and only a paragraph repeated inside twelve bodies was
dropped; and `cs_sv_te_dispatch` stays, against S3's DELETE ruling, because a ConCommand is its own
driver and so is not a "bring-up instrument with no driver" — a name table test pins it.

### Phase 1 exit

Every deleted symbol had 0 live importers in the 1a census on the commit before its deletion
(the census output is committed next to the retirement list). `make check` green with the
retired tests removed and the rewritten ones passing; live gate two passes; crossing budget
re-recorded (`tools/live-gate --record`) because deleting the dead round trips in lane S2's group
changes the counts.

---

## 4. Phase 2 — the stock rows leave `server_csharp.vpc`

The user's first ask. End state: **no `$SRCDIR\game\server`, `$SRCDIR\game\shared` or
`$SRCDIR\public` row in `server_csharp.vpc`**, pinned by a gtest. Every one of the 25 is
accounted for below; a row that stays native is relocated under `src/sourcesharp/` with its
reason letter in the header comment, so the vpc rule holds and the reason is visible.

### 2a. `physics.cpp` (4,655 lines) — split three ways (lane N1)

- **Delete ≈1,782**: the `SS-PHYSWORLD` scaffolding (:700-1385), the `#if 0` blocks, everything
  `PhysicsShell` already does (step clamp, environment create/destroy, rule evaluation, event
  recording) and the collision shake/dust/warp that `PhysicsCollisionRules.cs` duplicates —
  which also settles the possible **double shake** at `physics.cpp:4289` (S3's open question:
  one heavy collision on a live join, count the shakes, before and after).
- **Move ≈1,011** to `PhysicsCollisionRules.cs` / `PhysicsCollisionResponse.cs`: penetration
  policy (`ShouldSolvePenetration` keeps answering synchronously from a pair set managed writes
  after the ring drain — same tick granularity as stock, verified by a two-prop overlap on a
  live map), friction-sound policy, damage events, the seven ConVars and ten ConCommands. What
  unlocks it: the ring already declares thirteen event kinds and records six; recording
  `FRICTION`, `FLUID` and `TRIGGER` needs no new slot except the one impact-sound play slot
  `PhysicsCollisionResponse.cs:30` already specifies.
- **Keep ≈1,696 → ≈700** under (e): `CCollisionEvent`'s 5+10+2 VPhysics slots, `Init`'s three
  resolved-by-name interfaces, `PhysFrame`'s `Simulate` and block walk. It merges into
  `physics_shell.cpp` as the "engine half" that header already reserves, and the vpc row is
  `sourcesharp\game\server\physics_shell.cpp`.

### 2b. The rest of the physics cluster

- `physics_fx.cpp` 279 → MOVE (see §1's correction).
- `physics_shared.cpp` 1,584: delete ≈804 (the whole `PhysModelCreate*` family has zero
  consumers on both sides — `shell_standalone_base.cpp:2430-2483` calls `physenv` directly; the
  data-object system serves one always-false watcher check); move ≈76 (friction effect/sound
  material policy); a 30-line `physics_globals.cpp` of the six interface pointers satisfies 9 of
  13 consumers per side and the remaining ≈500 lines (solid parsing, world, hash, precache) are
  one leaf file with zero entry points, relocated. Both vpcs.
- `ragdoll_shared.cpp` 1,259: move ≈541 (the LV manager and LRU — `RagdollPolicy.cs` /
  `Corpses.cs` are the twins; the native LRU has no writer on either side; its three
  `FCVAR_REPLICATED` ConVars need `RegisterConVar` to accept the replicated flag — check
  `Abi.cs:306` first); the builder stays under (g) and relocates. Both vpcs.
- `collisionproperty.cpp` 359 → DELETE (a lock around a list nothing fills; both vpcs; read the
  four `collisionproperty` gtests before the delete — they may pin the vanished
  `CCollisionProperty`, not the list).

### 2c. Map load, globals, vars

- `mapentities.cpp` 1,355 → SPLIT: ≈900 delete (`CreateEntityByName` always returns NULL since
  the entity port, so the hierarchy sort spawns an empty list; `MapEntity_ParseAllEntities` has
  comment mentions only), ≈350 move into `MapSpawner.cs` (which already claims map load end to
  end — **the open question**: `KeyValueShellOnly` at `shell_standalone_base.cpp:2215` is still
  called from `mapentities.o`, `shell_levelsteps.o` and `player.o`; a one-map-load counter on
  `CStandaloneEntity::KeyValue` says whether a second parser is live), ≈110 stay
  (`FreeContainingEntity` collapses into `shell_gameents.cpp:165`).
- `globalstate.cpp` 310 → MOVE; `GlobalStateTable.cs` (313 lines, 14 slots) is the twin; the
  residue is the two ConCommands, re-declared managed.
- `movevars_shared.cpp` 130 and `game.cpp`'s 17 ConVars → MOVE to `GameVars.cs` /
  `ConVars.cs`. **Blocked on one fact**: `FCVAR_REPLICATED` needs the same name registered by
  both DLLs and `ConVars.cs:6-8` records that a Shared-assembly declaration double-registers.
  The experiment is a managed replicated ConVar on a live join with the client reading it; if
  the client half fails, the 29 `sv_*` declarations stay in a 60-line native table with the
  rest of the file gone (`sv_skyname` is engine-resolved at 5 sites and is protocol either way).
- `util_shared.cpp` 1,242 → ≈1,100 delete (in-vpc consumers: 3), ≈40 move, 0 stay; the shared
  random generator already lives in `util_shared_library.cpp:406-439` (KEEP, f).
- `gamestringpool.cpp` 179 → 60 (four dead exports and the native ConCommand go), relocated
  under (d, weak: the string table's addresses are what `string_t` fields in the block are).
- `EffectsServer.cpp` 217 → ≈80: one `{filter-kind, TE_fn}` table for eight methods, relocated
  under (e, weak); or folded into `shell_te_forwards.cpp` at 1f if that lands first.

### 2d. Leaf math (lane N3) — relocated, and the activity cluster deleted

- `bone_setup.cpp` 6,048, `collisionutils.cpp` 3,261: KEEP (g), relocated to
  `src/sourcesharp/public/` beside `studio.cpp` (the four stock files still compiled from
  `src/public` and `src/game/shared` while their siblings were relocated is the inconsistency
  N3 flags). Their two natively declared ConVars (`anim_3wayblend`, `aim_constraint`, both
  `FCVAR_REPLICATED`) and `studio.cpp`'s `mod_load_showstall` move to managed declarations read
  through `ManagedConVarRef`. `studio.cpp:1407-1683` `RunFlexRules` (277 lines) has no in-vpc
  caller and goes.
- `animation.cpp` 1,093 → DELETE as a unit with `shell_animlists.cpp` (389),
  `anim_name_registry.h` (283), `studio.cpp:1684-1926` (243) and **`ActivityCatalog.cs`
  (2,048 managed lines)** — the activity-ordinal feed exists only to serve native readers that
  compare ordinals. Precondition: `precache_model.cpp:333,336,600-603` compares event names
  instead of registry ordinals and sources `AE_TYPE_CLIENT` from `EventCatalog`. Two gtests pin
  the current shape and retire.
- **One per-model descriptor crossing replaces eight per-call entry points**
  (`ModelDescriptor(modelIndex)` → sequences with label, activity name, flags, duration,
  events; attachments; bone count; cached managed per level). Retires `LookupSequence` ×2,
  `LookupSequenceByActivity` ×2, `SequenceDuration`, `SequenceEventCount/Event` ×2,
  `AttachmentName/Count`, `BoneCount`; deletes `sequence_by_activity.h`, most of
  `sequence_events.h`, `bone_library.cpp:299-392`. This also closes the **client/server
  `LookupSequence` disagreement** N3 found: the client's falls through to a *random* weighted
  pick on an activity name (`animation.cpp:545-556`), the server's refuses that fallback
  (`shell_scripted.cpp:1084-1098`) — a prediction bug candidate today, reproduced first with
  `devapi` on one v_ model before the crossing lands, so the fix has a red test.
- `stringregistry.cpp` and `steamid.cpp`: Phase 1e.
- `usercmd.cpp` 320: KEEP (f), relocated; the two `HL2_*` arms (:186-203, :306-319) are dead
  under both vpcs and go.

### Phase 2 exit

- `server_csharp.vpc` has zero rows under `$SRCDIR\game\` or `$SRCDIR\public\` — gtest
  `vpc_no_stock_rows_test.cpp`, mutation-verified by adding one back.
- `client_csharp.vpc`'s 30 shared rows changed in step: the datatable check
  (`datatable-verification-recipe`) on a live join, because `physics_shared`, `ragdoll_shared`
  and `collisionproperty` are compiled into both sides.
- Live gate two passes, save/load leg green (it is in `RUN_ORDER`), crossing budget re-recorded.

---

### Two rulings Phase 3 has already overturned, and one test that could not fail

Lane 3b re-measured before acting, as required, and found:

- **`shell_gameclients.cpp:811-819` is the LIVE standalone arm, not the dead one.** §5 3a listed
  it among the dead SDK-hierarchy arms to delete. Deleting as ruled would have removed the live
  path and kept the dead one, which is `:822-825`. This is the second time in this project that a
  ruling named the wrong side of a pair while counting correctly.
- **`sv_sourcesharp_fs_trace` does not exist** — it went with the file-open recorder at `dc79ba0e7`,
  an ancestor of the branch this plan was written against. Also: `ShellGameDll_RecordStep` was
  already gone from the frame steps, and `shell_playerlocaldata` has five datadesc blocks in the
  named range, not four.

- **A pre-existing test that passed on two absences.**
  `ServerGameDllTests.TheRulesEndOfFrameComesAfterTheEventQueueAndTheClientData` compared
  `IndexOf(a) < IndexOf(b)` with no found-check, and `IndexOf` answers −1 for an absent step. From
  the moment `ServiceEventQueue` stopped being called it was comparing −1 with −1 and reporting
  order. Every index it reads must now be found first. That is the fourth distinct
  cannot-fail shape this project has hit, after the numeric floors, the stale anchors and the
  vacuous guard the Phase 2 exit condition made true.

**A substitution worth recording as better than what was asked.** §5 3a wanted a live counter over
two map loads to prove `UTIL_SDKPlayerFromEdict` always returns NULL. Lane 3b instead showed the
function has exactly one definition on the link line and that **every one of its three return
statements is `return NULL`** — exhaustive over all paths, where a counter only covers the paths a
particular run took. It flagged the substitution rather than quietly claiming the experiment.

### An empty slot is not always a retirable slot

Lane 3b was told to retire four empty frame/level slots and retired **two**, declining the others
for reasons worth keeping:

- **`PrecachePointTemplates` — body emptied, slot deliberately kept.** A retired slot is a slot
  that is *not called*, and this call's **position** is the only way two orderings can be observed:
  low violence before the precache, and templates after the entities they read. That first ordering
  **was inverted in this port from the day the method was written**, under a `[Fact]` that pinned
  the inversion — `tools/gate5` found it, and no census, live gate or managed test could have.
  Retiring the slot deletes the evidence. One no-op crossing **per level** is the right price; the
  crossing budget is about per-frame and per-entity traffic, not per-level.
- **`EntitiesCleanupDeleteList` — left alone**, empty for the same reason but a flat ABI slot, so
  its marker belongs in the serialised tail rather than in a lane's hands.

**The rule: before retiring an empty slot, ask what its call POSITION proves.** A no-op whose
ordering is load-bearing is an assertion, not dead weight. This is the sixth way this tree hides a
live dependency from a census, after by-name reach, macro registration, inline construction, vtable
dispatch and test controls.

**Marker placement follows the slot's kind**, and the counts must be checked separately:
vtable slots of an internal shim interface mark in that interface's header (`ishim_frame.h` 4,
`ishim_level.h` 2); flat ABI slots mark in `host/sourcesharp.h` and `Abi.cs` (both 0). The ledger
distinguishes them, and the ABI worklist grep reading 0 is correct rather than an oversight.

**A test whose name oversold it**, found in the same pass:
`ANonEmptyDeleteQueueOnLevelStartIsReported` fed the slot a count of 3 and then asserted only that
the slot was **called once** — never that the warning fired or what it said. The report in its name
was never tested, and the 3 was a number native cannot answer.

### A refusal read as a measurement — the eighth way to get a wrong answer

Lane 3f was told `handle_cross.h` was dead, 327 lines with one consumer. **It has 102 server and
169 client live translation units.** The "one consumer" came from asking `tools/shim-census` — an
OBJECT-import instrument — about a header-only file. The tool **refuses** that question, and the
refusal was recorded as though it were a zero.

That is a distinct failure from every other one on this list: the instrument behaved correctly and
said so, and the answer was taken anyway. It is also the reason lane 3f's first act was to build
`tools/shim-header-census` for the four questions `shim-census` declines — includers, references,
vtables, orphan symbols — reading `.ninja_deps` directly, with a nine-check self-test where every
check has a negative control. **Nothing was deleted; the header is pinned with tests instead.**

The same lane found its own tool answering `0/0` for `sequence_events.h`, a file Phase 2d had
already deleted, and fixed it to refuse a missing path rather than answer about one.

**Four more rulings this lane overturned by measurement**, each in the safe direction:
`SetCritType` is live (called from `AddMultiDamage`), and the two DATADESCs stay because a save
description is reached through `DataMapAccess()` — "0 importers" says nothing about either;
`util_shared_library.cpp`'s "~130 dead lines" is **~7**, because the helpers called dead are
reached from siblings in the same file; `sound_emitter.cpp`'s precache log stays because
`-makereslists` is a live engine mode this shim already reads; and `shell_physsaverestore_absent.cpp`,
carrying a DELETE verdict, **has live consumers on both sides** (flagged to its owner, not touched).

Of the eighteen small rule headers, **four were dead, not eight** — one more died as a consequence
and one did not exist.

**The tally of ways a symbol census can be wrong here is now eight**: reachable by name, registered
by macro, constructed inline from a header, dispatched through a vtable, used as a test's control,
observed by call position, reached from a sibling in the same file, and — the newest — a
refusal recorded as a zero.

### TWENTY live callers receiving nothing — the largest defect this project has found

Lane 3c re-measured `shell_entityapi.cpp`'s stub slots and **S2's split is inverted**. It is not
23 unreached and 6 reached. It is **3 unreached, 5 decorated, and 20 with LIVE managed callers that
receive nothing** — armour, `func_friction`, `trigger_camera`, `player_speedmod`, `game_score` and
more.

**Why S2 missed it, and why I would have too:** S2 grepped for the slot name. Managed code does not
name slots — it names the **facade method** in `EngineBinding.cs`. So a grep for the slot finds no
caller and the slot reads as unreached, while a real game feature calls it every frame and gets
nothing back.

This is exactly the defect lane 2e-abi found in `ScriptedEntity.PlaySequence` — a live caller on a
slot that answers nothing, invisible to both suites and the live gate — **at twenty times the
scale**. It is a **defect ledger, not a retirement list**, and 3c pinned it as one rather than
deleting anything.

**Four more corrections from the same lane:**
- **`cs_standalone_spawn` is not deletable** — 8 tools drive it, `tools/live-gate` among them. S1
  ruled it dead. By-name reach again.
- **`collision_group_names.h` stays**, overturning S1's MOVE: `Collision_Group_t` is **positional**,
  so mirroring its numbers into a managed enum lets an engine insertion renumber them silently.
- **noclip needed a second crossing** the plan did not name — a between-frames command cannot be
  served by the movement-frame writeback.
- **The prose headers are 1,709 comment lines over 473 code**, not "≈2,300 over ≈150", and
  **27 gtests read those headers, several by text**. The prose move is not mechanical and needs a
  per-test audit; it is deferred with that reason recorded.

The lane also settled the shell's vtable slot count into `docs/engine-boundary.md`, which `S1`
could not, and left six items undone with the blocker named for each rather than guessing.

### Phase 4, item: the soundscape move is DESIGNED, MEASURED and DELIBERATELY NOT LANDED

Lane 4c wrote the four managed files the move needs — `SoundscapeLibrary.cs`, `SoundscapeResolver.cs`,
`ResolvedSoundscape.cs`, `SoundLevels.cs`, with the traversal, the flattening and the sound-level
conversion argued — and **did not commit them**, because landing a parser with no consumer is
exactly the unreached code that lane had spent five commits deleting. They are parked in the
session scratchpad at `4c-soundscape-resolver/` with `4c-render-handover.md` beside them.

**Why it is all-or-nothing:** the client's script trees serve both the resolver and the index, so a
partial landing leaves **two** index implementations rather than one. The failure mode is a
**silently quiet map** — no error, no crash, just missing ambience — which only a live run detects,
and lanes are forbidden live runs. It needs ~5 appended ABI slots, which the integrator should see
before they exist.

**Cost, measured on the shipped scripts** (labs/streetwar/strike, 46 sections, 57 `playsoundscape`
nestings): one resolve flattens to roughly 6–12 loops plus 2–5 randoms, so **~10–20 crossings per
soundscape change** at level/trigger cadence, against today's one call plus a native KeyValues walk.
**The one-shot scheduler stays native** in that design — it redraws time, volume, pitch and sound
level per shot on its own clock, so the intervals cross unresolved and the draw never leaves C++.

**A live defect found while measuring it:** the two native soundscape indexes **disagree on a
duplicate name today** — the client takes the **last** match, the server takes whatever
`CUtlDict::Find` returns, which is unspecified. Any map with a duplicated soundscape name can have
the two sides pick different entries. That is worth fixing whether or not the move ever lands.

## 4a. Outstanding issues, carried into Phase 3 with owners

Everything Phases 0–2 left open, with who takes it. **No item here is "known and accepted"** —
each is either scheduled below or has a stated reason it cannot be scheduled yet. Three of them
are real work deferred out of Phase 2, not loose ends.

### Deferred Phase 2 work (lane 3a, first, because the rest of 3 builds on it)

Lane 2-physics relocated all five physics rows and met the exit condition, but left three measured,
ready changes undone — each blocked on editing a file another lane held, not on evidence:

1. ~~**`physics.cpp`'s ~1,011-line policy move**~~ — **the figure does not survive measurement and
   the item needs re-scoping before anyone executes it.** Same raw-versus-code confusion found in
   `world_adapter`: `physics.cpp` is 4,292 raw but **2,202 code lines (49% prose)**. Of the 1,921
   code lines inside bodies, **701 are `CCollisionEvent::` vtable slots vphysics calls** and
   **443 are diagnostics reached by name** (8 tools, 30 sites) — neither can move. And the managed
   destinations **already hold 585 lines** landed by an earlier lane, which this figure was never
   reduced by.

   Lane 3a deliberately **did not execute it and did not guess at a replacement number**, which is
   the right call: the remaining scope has to be named function by function first. The event ring
   records 11 of its 12 kinds (`PRE_COLLISION` is a documented decision), so the mechanism the move
   needed does exist — what is missing is an honest list of what actually moves.
2. ~~**The `PhysModelCreate*` family**~~ — **ALREADY DONE; the claim was a grep over documentation.**
   Lane 5a re-measured: all six remaining hits across five files are **prose** — a tombstone in
   `physics_shared.h:158`, two explanatory sentences, commented-out stock code, and two test
   comments. Comment-stripped, the header holds **zero** of the nine names. Nothing asserted the
   absence, so 5a added `ThePhysModelCreateFamilyIsStillGoneFromTheHeader`. *(Same shape as the
   `hud_fastswitch` and `shell_gamedll_convars.cpp` readings I got wrong today: a name in text is
   not the thing itself.)*
3. ~~**The ragdoll low-violence manager and LRU**~~ — **DONE by lane 3a, with one deliberate
   refusal.** The LRU, the manager, three ConVars, both accessors and both system-table rows are
   gone, with tombstones and absence facts. **`ishim_level.h`'s `SetLowViolence` slot and its empty
   override STAY**: its *position* is the fact. `tools/gate5` found this port running
   `SetLowViolence` / `PrecachePointTemplates` **inverted against stock**, and two live facts are
   written between those two steps — retiring the slot deletes one endpoint of the only
   observation. `ishim_level.h` already makes the identical ruling for `PrecachePointTemplates`.

These touch `shell_serversystems.cpp`, `shell_clientsystems.cpp` and a machine-parsed RULING table,
so they are one lane's work and go first.

### Instruments that must be captured before their subject can go

4. ~~**`shell_clientdll_probe.h`**~~ — **CAPTURED. `docs/engine-boundary.md` §6a, commit
   `24badb01a`: 29 of 79 called, 0 refused, with the calling engine addresses, taken at the end of a
   full two-pass live gate.** The fifty uncalled rows are recorded as a floor and not as a kill
   list, because the gate's save-and-load step passes twice while all nine save/restore methods read
   zero. **The probe may now be deleted** — that deletion is the only part still owed, and it goes
   to the Phase 4 tail lane with a test.
5. **`cs_sv_boneprobe` and `BoneReplay`** (~1,168 lines) — `tools/live-gate` drives them at three
   sites and checks their console output at two more. A managed recorder must land **in the same
   commit** as the gate's checks being re-pointed. Owner: Phase 4.

### Small residue with a named owner

6. `hud_fastswitch` — 0 importers on both link lines, blocked only by living in the client lane's
   `shell_hud.cpp`. Phase 4.
7. `shell_gameclients.cpp`'s now-unused `extern g_pToolFrameworkServer` and its toolframework
   include. Phase 3, engine-binding group.
8. `shell_shot_path.h` and `anyentity.h:359` hold declarations with no definition — deliberate and
   documented in `removal_set_tail_test`. Phase 3, entity-shell group.
9. `client_class_names.h`, orphaned when `cl_dumpentityfactories` went. Phase 3.
10. `g_nSourceSharpStateChangedCalls` is write-only since its reader (`cs_sv_dirty_stats`) went —
    two lines in `shell_standalone_base.cpp`. Phase 3, entity-shell group.
11. `shell_absent.cpp`'s survivors lost their last importers when `mapentities.cpp` was cut
    (`CNodeEnt::Spawn`, `NWCEdit`, both `shell_templates.cpp` exports). Phase 3: the file should
    reach zero, which was always its exit condition.
12. `dumpgamestringtable` is still a native ConCommand; a managed one needs one appended engine-API
    slot touching four files. Phase 5's ledger row, appended by whichever Phase 3 lane owns those
    files first.

### Proofs that are owed, and are not blocking

13. ~~**The live-gate `push` failure is not control-verified.**~~ **There is currently no failure
    to control.** At the Phase 4 gate `push` PASSED both passes — 34.0 units then 13.7, against a
    threshold of 4.0. The second-load weakening is still visible (2.5x) but 3.4x clear. **This is
    one trial and does not retire the item**: 13.7 sits inside pass B's recorded 0.7–24.2 spread, so
    it is a draw from a distribution that still contains failures. The control run becomes runnable
    the next time the step actually fails; until then the honest statement is "not carrying a
    known-failing step", not "fixed".
14. **The prediction-bug fix has one live measurement, not a before-and-after — and the item as
    written is NOT EXECUTABLE.** Checked 2026-09-20:

    - The probe commands **do exist**, as managed ConCommands in
      `SourceSharp.Server/ServerModelDescriptorCommand.cs`. *(My first grep searched only `.cpp`
      and `.h` under `src/sourcesharp/` and found nothing; concluding "stale" from that would have
      been the day's fourth name-in-text error, in reverse. A wider search found them.)*
    - **The two-build recipe is not committed anywhere.** The only hit for `modeldesc` outside the
      command's own source is `tools/.live-gate.log`, which is a log, not a recipe.
    - The fix is `1b25d41e3` ("one ModelDescriptor crossing replaces seven per-call entry points"),
      **148 commits back**, touching **33 files / 2,603 insertions**. A "before" build by checking
      that commit out is not realistic — it predates the entire Phase 3 and Phase 4 tree.

    **So the before-arm has to be constructed, not checked out**: revert the fix's behaviour on
    today's tree behind a switch, run both arms on one join, and diff `cs_sv_modeldesc` against
    `cs_cl_modeldesc`. Until someone writes that, this item is a statement of what is missing
    rather than a task anyone can pick up. **It remains non-blocking**: the fix has a live
    measurement, what it lacks is a control.
15. ~~**Penetration granularity is owed a live confirmation**~~ — **SETTLED: the machinery is
    unreachable, and the ruling is DELETE, not port.** The probe measured **19,302 interpenetrating
    pairs entering `ShouldSolvePenetration` and ZERO getting past the owner guard**, over two maps
    and two runs; `FVPHYSICS_PENETRATING` was set on none of 519 objects. (Attribution: this
    reading is lane 3a's own, commit `a2b77ec74`; a concurrent agent in the same worktree restated
    it rather than measuring independently, and an earlier version of this plan credited it to the
    wrong one.)

    **All three controls had to be rebuilt, because the first live run passed two of them by
    accident and failed the third.** That is the part worth keeping:
    - Control 1 was asking a **sibling slot** (`ShouldCollide`) rather than the function under test.
    - Control 2 was summed across both maps, and the summed figure printed YES while
      **`ss_sandbox`'s own delta was zero** — one map standing in for the other, which is the exact
      failure two maps exist to prevent. Fixed by spawning a crate half inside a brush at the
      player's feet (pitch 30 → 0 entries in 14s; pitch 89 → 550 in 2.5s).
    - Control 3 was **passive**, reading `0x0000` over 484 objects, which cannot tell "field
      unreadable" from "nothing sets a flag". It now *drives* a flag and reads it back.

    **An instrument defect was found in the process**: `ss_physworld_object ""` matches **nothing**,
    because `V_stristr` compares each letter of the haystack against the needle's first character
    and an empty needle's first character is the terminator. The probe silently fell through to a
    `"prop"` fallback and reported **415 of 427** objects while its scope line claimed the whole
    environment — inside a control whose entire job was to establish scope. Pinned by
    `physworld_match_rule.h` and a seven-fact tier1-linked test.
    `FVPHYSICS_PENETRATING` was set on none of 519 objects.

    `PhysicsOwner_t::pBase` is NULL for every physics object this build makes — a managed entity
    returns early from the handle arm before it is assigned, the world stores NULL deliberately,
    and after §4a item 2 nothing in the tree assigns raw-pointer game data at all. So the penetrate
    event list, `FVPHYSICS_PENETRATING`, the three-second disable, the NPC solver and the error
    boxes are all dead. **There was never a granularity to regress from: porting this would have
    ADDED behaviour rather than moved it**, which is the opposite of what the phase is for.

    The probe was built as a control on a static reading precisely because that kind of reading has
    been wrong here before — and this time it was right, which is only knowable because it was
    checked.

### Settled since they were raised, recorded so nobody re-opens them

- **Can managed declare an `FCVAR_REPLICATED` ConVar, and does the engine transmit it?** Yes, both.
  Lane 2-maploadvars ran a live two-process join: a control moved, two treatments (managed server
  half, native client half) moved, two negatives did not.
- **Double collision shake** — never occurred; the native shake had 0 importers.
- **`KeyValueShellOnly` as a second map parser** — it is not; 0 over two loads.
- **Does managed need an activity ordinal?** No; the catalogue's only non-test reader was the
  native feed, and 2,048 managed lines went with it.

### The one thing that is genuinely unresolved

16. ~~**`sv_gravity` reads 600 on one build and 800 on another with identical declarations.**~~
    **SUPERSEDED — the symptom did not reproduce and this item's model of it was wrong.**
    Lane `3-gravity` took **21 readings across 5 builds, 2 commits and both build modes, and got
    600 every time** — including 3/3 debug and 3/3 release on the exact commit this item recorded
    800 on, plus a map reload, a `changelevel`, and two concurrent instances. At the originally
    reported rate, 21 consecutive 600s is about 5e-7.

    The framing was also wrong in a way worth keeping, because it misled this document twice.
    There is **one** `sv_gravity` declaration in the whole tree and its default is **800**; the
    engine declares none. **600 is a runtime write** from `Movement.cs:46` through
    `SourceSharp_RegisterMovementParams`. So there was never a question of "which default wins" —
    800 is simply what the ConVar holds whenever that one write does not survive, and nothing in
    the original report established whether it had. "Registration order at startup decides which
    default wins" and "link or static-init order" were both wrong. What produced the three original
    readings is **unsettled**, and the lane recorded the experiment that would settle it rather
    than inventing a cause.

16a. **What actually blocks the 29 `sv_*` moves, which is a different and real defect.**
    The engine refuses to link a replicated pair unless the parent carries `FCVAR_GAMEDLL` or
    `FCVAR_CLIENTDLL`, and `tier1/convar.cpp:76` ORs that flag in only for ConVars that **already
    exist** — so a managed declaration has neither. The managed server-side declaration becomes
    the parent, the client's native twin is refused the link, its `m_pParent` is never assigned,
    and it sits at its declared default forever.

    **This happens five times on every run today**: `mp_allowspectators`, `mp_fadetoblack`,
    `mp_forcecamera`, `mp_friendlyfire`, `sv_hudhint_sound` — exactly what `GameVars.cs` declares.
    It is harmless only because nothing on the client link line reads those five.
    `movevars_shared.cpp` is **not** in that position: its client half reads `sv_gravity` in
    `GetCurrentGravity`. That is the real blocker, and it explains why `GameVars.cs` looked like a
    working precedent — it is a precedent for declaring, not for linking.

    Second half of the mechanism: `shell_gamedll.cpp:1467-1472` slams a replicated ConVar's
    **parent** back to the registering declaration's default, and runs for every ConVar built after
    `ConVar_Register`, because `s_pAccessor` is never cleared.

    **No other ConVar is affected.** 1,369 live, 38 with value ≠ default, every one with a named
    cause. `mp_forcerespawn`'s disagreement comes from `cfg/settings.scr:107` and stock has the
    same one — inherited, not a regression.

    The lane landed the instrument the fix needs rather than the fix: `shell_movement_audit.*`
    classifies a movement ConVar three ways (as-registered / back at the **declared** default /
    operator override) in an engine-free TU so a gtest compiles it, sitting above
    `SourceSharp_OnConVarChanged`'s early-out. Proven live, not argued. It adds no `FCVAR_GAMEDLL`
    and changes no registration order.

---

## 5. Phase 3 — the server shim, simplified

Everything ruled KEEP+SIMPLIFY, MOVE or SPLIT in lanes S1–S4, H1, H2 that Phase 1 did not
already remove. Grouped by what the step produces; every item has its rows in the table.

### 3a. Engine binding (lane S2)

- `shell_entityapi.cpp` after 1e: collapse five spellings of `SetSolidity` into
  `EntitySetSolidity` + `EntitySetBounds`, four handle conversions into one.
- `shell_frameshim` + `shell_framesteps` → one file (≈250 from 753); `shell_levelshim` +
  `shell_levelsteps` → one file (≈350 from 1,015); the three refusal slots managed still calls
  each frame and four empty level slots retire at `SourceSharpFrame002` / `SourceSharpLevel002`.
- `shell_files` + `shell_steamcontext` + `shell_mdlcache` (+ `shell_templates`' one forward) →
  one file ≈150; `shell_engineglobals` + `shell_engineinterfaces` → one ≈250.
- Dead SDK-hierarchy arms: `UTIL_PlayerByIndex` / `UTIL_SDKPlayerFromEdict` always return NULL,
  so every arm they guard (gameclients :246-267, :340-406, :811-819, :837-854; playerlocaldata
  :255-282, :352-365; clientsteps :122-136; transmitsteps) and the ear-position / skybox-area
  native→managed→native round trips are scaffolding. Delete; re-run the live gate because the
  crossing counts move.
- ~~`LevelInit`'s four map-load slots → one~~ — **MEASURED AND DECLINED.** This item said "managed
  sets `eLoadType`", and managed **cannot set it at all**: `Globals.LoadType` is getter-only and
  its own doc reads "Written by the shell, never by managed code". Worse, the write is
  order-critical native work — all four arms write `eLoadType` **before** forwarding, and the
  managed arm then runs `ParseAllEntities`, whose stock entities read `gpGlobals->eLoadType`
  **during spawn**: `logicauto.cpp:89-101` branches four ways on it, `triggers.cpp:1518` gates on
  `MapLoad_NewGame`, and nine more stock files read it. One slot is only correct if managed writes
  the value before spawning, through a path that does not exist.
  The cost side is worse than the saving: collapsing four managed-API slots to one renumbers
  `CrossingOp` from 20 through 155+ (explicit values, with contiguity asserted), bumps the ABI
  version through order-sensitive parity, and edits four separate name lists — to save three slots
  and one branch. The four arms are identical apart from the slot pointer, and
  `ServerGameDll.cs:504-508` records that they stayed four **deliberately**, so the refusal sits at
  the method rather than inside a switch. This item reversed a stated design choice without
  knowing it was one. If revisited, the `eLoadType` write moves with it, not after it.
- `world_adapter.cpp` — **re-measured in full; the split is sound and every number here was wrong.**
  The permanent half is **251 code lines**, not ≈150 (the ≈600 raw figure was right — comment
  dominates the file). The moving half is **130 code lines**, not ≈300 (that was a raw span too).
  Every line range in the earlier draft had drifted 40–100 lines later; locate by symbol.

  **Five appended slots are genuinely required, not "≈6", and twelve things this plan would have
  counted are free**: the activity and event list registrations are **empty bodies**, the body
  queue is a stub, AI networks/schedules/relationships are empty or `return false`, and
  lightstyles, ConVars, the gib model, water sounds, sound-ent init and client game precache all
  have existing slots. What is actually needed: `InitPreEntitySystems` (**not optional — it is what
  creates `physenv` in shell mode**), `SentencesInit`, `PrecacheStandardParticleSystems`,
  `RunPrecacheRegisters`, and `SetStandardSpriteIndices`. Two more (`ResetGameRules`,
  `GameRulesPrecache`) only if the rules lifetime moves — if it stays native, the destructor's
  `delete g_pGameRules` stays with it.

  **The sprite indices are the awkward one.** Only **4 of 8** have any in-build consumer, and two
  of those four are dead `extern`s; the live readers are `g_sModelIndexSmoke` and
  `g_sModelIndexBubbles` in `server_util_library.cpp`. A managed `W_Precache` must hand those back,
  which is what `SetStandardSpriteIndices` is for. Note `shell_te_forwards.cpp` is **not** a reader
  — its two hits are a comment saying the externs deliberately did not come across.

  **Do NOT delete `ReportNetState`, and check before deleting `cs_sv_trace_subject`.** The
  diagnostics look unused — a literal-name census over `tools/`, `docs/`, `.cfg`, `.vpc`, managed
  and the tests finds the two console commands referenced **nowhere but their own definitions and
  four comments**. But `ReportNetState` is used by two test files as the **positive and negative
  control for their declaration and virtual detectors**: deleting it fails
  `RemovalSetTailFixture.VirtualDetectorSeesTheRealHeadersBothWays` and a
  `BaseEntitySharedLift` control. And `entity_list_sweep_test.cpp:1400` asserts on the *body* of
  `cs_sv_trace_subject`, so removing it fails outright. Deleting the command also strands
  `m_nImpacts`/`ImpactCount()` with zero readers. **A thing with no callers can still be load-bearing
  as a test's control** — that is a fifth distinct way this tree hides a live dependency from a
  census, after by-name reach, macro registration, inline construction, and vtable dispatch.
- `shell_globals.cpp` `variant_t` (≈340) → `VariantValue.cs` (exists);
  `shell_playerslots.cpp` name/userid/command-arg policy → `Players.cs`;
  `shell_commandclient.cpp` `IsCommandIssuedByServerAdmin` → `Commands.cs`;
  `shell_levelsteps.cpp`'s MOTD file policy → `ServerGameDll.cs`.
- `CheckTransmit` stays (a) — the engine hands it a bitvec to fill mid-frame; whether a managed
  per-frame bitset could replace the walk is a perf-pass question, recorded, not done.

### 3b. Entity shell (lane S1)

- Player policy → `Players.cs` / `DamageCheats.cs`: `ClientCommand`, its 20 `CON_COMMAND`s and
  `Route` (≈460 lines) become `[ConCommand]` attributes; one new crossing (view-angle snap with
  `fixangle`). `ss_kill` / `ss_hurtme` / `ss_setpos` / `ss_noclip` / `ss_ground` keep their
  spellings (`tools/` references `ss_ground`).
- Rule headers: `standalone_movetype.h`, `standalone_basevelocity.h`,
  `standalone_model_bounds.h`, `scripted_kinds.h`, `collision_group_names.h`,
  `entity_factory_table.h`, `break_piece_offset.h`, `player_name_match.h`,
  `scripted_var_defaults.h`, `jet_lifetime.h`, `view_effect_reach.h`,
  `particle_follow_mask.h` — each is a decision with a managed home named in the table and an
  existing crossing to ride (an `adoptBounds` argument on `SetModel`, a mask argument on the
  particle slot, the four kind values on `ScriptedEntity.cs`, and so on). Three fold into their
  one caller; `standalone_names.h` → `EntityDirectory.cs`.
- `KeyValueShellOnly` parser: dies once 2c's counter reads zero.
- Prose: `shell_standalone.h` 1,506 / `shell_resolve.h` 277 / `standalone_relays.h` 258 /
  `toucher.h` 163 / `entity_factory_table.h` 218 carry ≈2,300 lines for ≈150 declarations. The
  arguments move to `docs/entity-shell.md`; the headers keep a pointer and the declarations.
- `sendproxy.cpp`'s 19 unconsumed proxies, `baseentity_statics`' 26 unconsumed symbols, the
  `CBaseEntity` projection arms in `shell_standalone_base.cpp:298-468`: delete.
- The slot count: `plan_entity.md:146` says 39; S1 counted 59 `virtual` declarations in
  `shell_standalone.h`. A vtable dump of `CStandaloneEntity` from the live object settles the
  number and `docs/engine-boundary.md` records it.

### 3c. Physics shell and effects (lane S3)

- `physics_shell.h`: drop the `Defaults()` rule literal (:995-1027, duplicates
  `CollisionRules.cs`) and the ledgers (1c) → ≈1,800; `physics_shell.cpp`'s asserts fold in.
- `shell_worldquery.cpp`: after 1e's 15 dead slots, move the 14 effect/policy entries (≈670)
  onto `TempEntPlayback`; the partition enumerators stay (a). `RadiusDamage` /
  `ExplosionDamage` / `EntityTakeDamage` are already replaced by `BlastSweep.cs` — delete.
  `EntitiesInSphere` is a linear scan over the standalone list; whether managed can own it
  depends on managed holding bounds per entity — counted at runtime before deciding.
- `server_util_library.cpp`: delete the check-client PVS system (:187-419, its only reader is
  under `#ifndef SS_CBASEENTITY_ALIAS`, which the vpc defines), `VarArgs`, `IsValidEntity`,
  `DBG_Assert`; move the four TE emitters, `ImpactTrace`, `PrecacheOther`; ≈240 stay.
- `shell_recipientfilter.cpp`: delete the prediction rules (34), move the two culls (118); the
  filter vtables stay (a). `shell_usermessage_send.cpp`: 17 `MessageWrite*` → Begin/WriteBits/End
  when the last native senders leave.
- `shell_vieweffects.cpp` 370 → MOVE (`EnvFade.cs`, `PhysicsCollisionResponse.cs` exist);
  `shell_shadow_ctrl_probe.h` → DELETE (a closed-defect probe of `vphysics.so`'s private
  layout); `areaportal_list.h` merges into `shell_areaportal.cpp`; `shell_propdata.cpp` (12 code
  lines) and `shell_particlequery.h` fold.
- `SS_PHYSEVENT_CONSTRAINT_BROKEN` has no producer (`ConstraintBroken` :928-950 is empty):
  break a `phys_constraint` on a live map and watch `cs_sv_physics`; then either record it or
  delete the kind.

### 3d. Sound (lane S4)

`shell_sound.cpp` + `shell_sentences`' live 12 lines + `shell_soundscape`'s registry (≈90 code)
→ one `shell_sound.cpp` ≈300 (the two debug printers and `SourceSharp_PlayerSetSoundscape`,
which has no managed caller, go — `abi-requests/8e-sound.md` lists the four sites).
`shell_soundent.cpp` 217 → `SoundEnt.cs`, which already is the pool. `shell_soundenvelope.cpp`:
≈330 dies with `physics.cpp`'s friction loops (2a); what remains is two slots
(`SoundPatchCreate`, `SoundPatchSet`) if managed physics emits friction loops at all, or one
(`CheckLoopingSoundsForPlayer`) if it does not — decided by 2a's outcome. The soundscape
registry's last native decision leaves in 4c, when the client parses the file in managed code.

### 3e. Shared systems (lane H1)

- `sourcesharp_gamesystem.cpp` 3,561: 476 prototype lines → one **generated**
  `sourcesharp_entrypoints.h`; the 472-slot fill is hand-mirrored across `sourcesharp.h`,
  `Abi.cs` and the assignment today — generate it the way `VtableLayouts.g.cs` is generated
  (`SourceSharp.Generators`). ≈2,400 after 1e's weapon stubs.
- `shell_movement.cpp` 1,883 → ≈1,300: 18 forwarders whose only caller was `gamemovement.cpp`
  (in neither vpc; its `.o` is a ghost) go; `sourcesharp_movement_managed` — whose 0-arm is
  fatal — goes **after** `tools/movemath-gate`, `8f-probe-movetypes` and `8f-movetype-gate` are
  re-pointed (they flip it). Native decisions that remain: hull constants (→
  `Shared/Movement.cs` params), the ground write-back rule, the point-contents cache; the first
  moves, the two others are (e)-adjacent and stay with a comment.
- `shell_gamerules.cpp`: `GetViewVectors` / `GetAmmoDamage` / `GetChatPrefix` /
  `GetTaggedConVarList` → `GameRules.cs`; `g_Language`, `old_radiusdamage` → `ConVars.cs` (one
  `ManagedConVarRef` at `util_shared.cpp:534` until 2c removes it); the native `skill` duplicates
  the engine's own — delete once stock `gamerules.cpp` leaves the client vpc
  (Phase 4), verified by the live `skill 3` measurement `GameRules.cs:38-63` describes;
  `igamerules.h` 48 → 23 slots.
- `shell_savegame.h` + `save_field_codec.h` → one header (≈650 code lines); 620 comment lines
  → `docs/managed-saverestore.md`. The cluster is KEEP (f): save/load is wired end to end
  (live-gate `save-load` in `RUN_ORDER`, `SavedEntityCodec` / `SaveTiers` / `LogicAutosave`
  exist) and the engine parses the block framing.
- `shell_shared.cpp`: delete :169-182, :197-272; `default_fov` leaves with `view.cpp`'s retype
  (Phase 4). `vtable_hook.*` 490 → ≈30 (`MemberOf` only) once 1c removes the fs recorder.
- User messages: the list is C# (`Shared/UserMessages.cs`); the 64-slot hook-thunk table, the
  dropped-message buffer and the registry answering `GetUserMessageInfo` stay (c); the
  natively written `LayoutCheck` payload moves to managed.
- 40 `ManagedConVarRef` sites read managed tuning natively (`r_Snow*`, `violence_*`,
  `sv_alternateticks`, …) — native decisions the census instrument does not see. Listed in the
  ConVar ledger (Phase 5) as "decision at a native read site", each with its row.

### 3f. Shared libraries (lane H2)

- `trace_filter_library.cpp`: nine filter classes with 0 live importers (:275-575, ≈330 lines)
  — **after** a `vtable for CTraceFilter*` grep over live objects rules out header-inline
  construction (H2's caveat: the per-function census sees only out-of-line symbols; same for
  `CheckEntityVelocity`).
- `overlay_library.cpp/.h` 693 → MOVE over the `IVDebugOverlay` vtable the Abi already binds
  (managed reads OBB from the block per overlaid entity; measure the overlay call count with
  `ent_bbox` on 400 entities *after*, per the perf rule).
- `takedamageinfo_library.cpp`: ≈200 dead (`GetAmmoName`, `DebugGetDamageTypeString`, skill
  adjust, `SetCritType`, two DATADESCs); ≈419 move (`CalculateExplosive/Bullet/Melee/GuessDamageForce`,
  `ImpulseScale`; `phys_pushscale` becomes a managed declaration); the value type stays (d).
- `util_shared_library.cpp`: ≈130 dead; ≈473 move (`UTIL_Tracer` / `BloodImpact` /
  `DecalTrace` choose an effect — policy); the shared random generator stays (f).
- `shell_propdata_system.cpp`: `PropData.cs` already owns `BreakableModels` via an existing
  crossing; the KeyValues tree and the `base` chain follow (≈304); `CBreakParser`
  (`IVPhysicsKeyHandler`) stays (a). Cadence of `shell_scripted.cpp:640`'s per-spawn read is
  counted on a live join first.
- `particle_manifest.cpp`: the manifest read (≈165) → managed over `IEngineFiles.ReadGameFile`;
  `sound_emitter.cpp`: KEEP (b) — it does not parse `game_sounds`, `soundemitterbase->` is the
  `soundemittersystem` app system loaded by name; ≈250 lines of dev commands, precache log and
  eight dead free functions go; `sv_soundemitter_trace` registers from managed.
- `generic_payload.cpp`'s three ConCommands (≈170) → one "describe payload/schema" query and a
  managed DevApi command. `said_once_per_id.h` + `physics_bridge_refusal.h` → one 50-line latch.
- Layout headers stay (d): `engine_block_generated.h` (generated from `EngineBlock.cs`),
  `entity_slots.h`, `slot_table.h`, `physics_gamedata.h` (single-writer serial word, C++ pointers
  resolved in engine callbacks, `void*` read mid-step). `engine_block.h` folds into the generated
  banner; `tempent_schema.h`'s 17 restated constants (:59-113) are emitted from C# like the block
  is; 1,610 comment lines across five headers move to generator-emitted docs.

### 3g. Passthrough slots → bound vtables

The tree already has the route: `tools/vtable-layout` generates
`SourceSharp.Abi/VtableLayouts.g.cs` (one struct per engine interface, field position = slot),
`SourceSharp_AcquireInterface` hands managed the pointer plus a compiler-derived slot count that
`NativeInterfaceFactory` checks once per acquire, and `tools/vtable-policy.json` carries the
NOT EXPOSED (71) and `Refused_` (20: varargs, by-value `Vector` returns) rows. It is used at 25
call sites; the slot table has 504 assignments. None of the design documents mention it — Phase 6
adds the section to `plan.md` §3.

- `tools/shim-census passthrough` answers this: a slot is **passthrough** when its body takes
  blittable arguments, makes one call through an engine global and touches no shell, handle or
  slot-table state. **Re-measured after Phase 1 (lane 1e-abi, `0a9580a1c`): 5 strictly
  passthrough and 2 near, out of 911 definitions across 478 slot assignments; one of the 5 is the
  entity/physics carve-out below, so 4 are actionable.** (The earlier reading of 4-of-925-across-476
  was stamped at `216533fa8`, the Phase 0 tip, not at the current branch tip.) — not the 13 a crude awk produced, because two of
  those resolve a token or an entity before reaching the engine. So 3g is a small, surgical step,
  not a migration: the bound-vtable route is the right shape for the *new* engine reaches that
  Phase 3f and Phase 4 introduce (file reads, overlays, sound, model facts), and it retires only a
  handful of existing slots.
- Every move in 3d/3f/4 that reaches the engine for a file read, an overlay, a sound or a model
  fact binds the interface — this, not the 7 existing slots, is where the route pays (`IBaseFileSystem`, `IVDebugOverlay`, `IEngineSound`, `IVModelInfo`
  are in the layout) instead of adding a slot.
- Each passthrough slot's managed callers migrate to the bound impl class; the slot, its
  `Abi.cs` field and `Engine.cs` wrapper retire on the integrator's bump.
- **Not** for entity ops (`Entity*`, `Standalone*`, `Player*` prefixes: handle→shell resolution
  is the slot table's single-writer design) and **not** for VPhysics (`plan_entity.md` §3.8 keeps
  physics objects behind ids-with-serials on purpose; binding `IPhysicsObject` would need an
  id→pointer slot per call and give that up). The slot table ends as entity and physics ops
  plus the four refused proxies.

### Phase 3 exit

Every KEEP row in lanes S1–S4/H1/H2 has a reason letter in its file's header comment (a gtest
walks the shim and asserts the marker). Crossing budget re-recorded. Datatable check on a live
join for every both-sides file touched.

---

## 6. Phase 4 — the client shim (lanes C1–C3)

Same shape as Phase 3; the client's numbers are larger because it holds the renderers.

- **`shell_standalone_client.cpp` 7,400** is 41% comment and 9% slot bodies (16 out-of-line
  slots, 84 inline in the `.h`). After 1c's 1,738 instrument lines: `ComputeFxBlend`'s fade
  curve (:3992-4066) and the anim-layer / pose-parameter / sequence-cycle state (:2126-2324,
  :4733-5756, ≈1,200 lines) become **engine-block fields managed writes**
  (`ClientRenderPolicy.cs`, `ClientAnimationLayers.cs`, `ClientPoseParameters.cs`); `EnsureBones`
  keeps reading them per draw with no crossing — a layout change, not a call. Precondition: the
  `ClientBoneProbe` instrument (which also pokes those members) is gone (1c). The shell still
  implements `IGameEntity` (6 slots, 7 live stock callers via `standalone_relays_client.h`),
  which `plan_entity.md` §3.1 says should have no caller — those seven sites are retyped in this
  step and the six slots go.
- **`shell_playerresource.cpp/.h` 685 → DELETE.** Its only live native readers are
  `ClientScoreBoardDialog.cpp` (3 sites) and `SpectatorGUI.cpp` (5 sites), which read
  `GameResources()` per frame while open. **Decision (user, this plan): those two panels do not
  stay native.** They are ported to the managed HUD (`Hud.cs` / `SourceSharp.Client`), reading
  the player table `ClientPlayerResource.cs` + `Shared/Scoreboard.cs` already model, and the
  two stock rows leave `client_csharp.vpc` in this step. Order: managed scoreboard and spectator
  panels land and are seen on a live join; then the two stock TUs and `shell_playerresource`
  delete together; `IGameResources` has no other consumer on the link line (verify with
  `tools/shim-census` before the delete). This is the one place Phase 4 touches the 200 stock
  client rows.
- `shell_prediction.cpp` :83-211 (130 lines of empty `CPrediction` bodies) → implement
  `IPrediction`'s 13 slots directly; one `IMoveHelper` once stock `movehelper_client.cpp` leaves.
  `shell_clientdll_datachanged.*` (162) → inline in `world_adapter_client.cpp` (one producer).
  `shell_player_orphans.cpp` 207 → ≈40. `shell_clientdll_refusals.h` + the server twin → one
  shared header. Six files with no live feeder (`shell_clientdll_bonesetup`, `_classlist`,
  `shell_weapon_scripts`, `client_seam_census`) → 0.
- **Effects (lane C2)**: naming is inverted — `shell_*_render.cpp` are the ABI seams and
  `shell_*_sim.cpp` hold simulation and draw. The `*_sim` spawn loops are `IParticleEffect::Update`
  callbacks sampling inside ranges managed already pushes (every managed twin is 45–225 lines of
  `OnDataChanged → Effects.XUpdate`), so they stay (a, g). The two decisions that move: the wind
  gust state machine in `shell_effects_sim.cpp` (≈450 → `ClientWind.cs`; native then needs one
  `WindSet` per frame) and the impact effect-family / decal / sound choosing in
  `shell_impact_effects.cpp:1465-1708, :1984-2057` (≈316 → `ImpactEffects.cs`; the burst
  contents are already managed). Every other candidate (impact callback, snow volumes, tempent
  frame collision) is **blocked on one missing client-side trace slot** — added once, in this
  phase, at an integrator bump.
- **Renderers (lane C3)**: no whole-file MOVE — every renderer has a real mesh/material core.
  Split halves: `shell_soundscape_client.cpp`'s KeyValues walk (:940-1467, ≈500) becomes a
  managed parse (`Abi/KeyValuesText.cs` exists) handing native a resolved layer list per
  soundscape change, which also retires the server's duplicate index-only parse (3d) — both
  sides must build the same index from one `Shared` parser, and the random one-shot scheduler
  stays native or it becomes a per-frame crossing; `shell_surface_render.cpp`'s name-resolved
  blob decode (:162-284, ≈180) → one per-entity `SurfaceUpdate` slot; `shell_renderable_facts.cpp`'s
  four `return false` stubs and empty shadow bone-setup queue (≈150) and
  `shell_materialproxies.cpp`'s constant bodies and refusal counters (117) → delete;
  `shell_trail_render.cpp`'s kind-mismatch policy → `ClientTrails.cs`; `sprite_render_rules.h`
  → block fields. `shell_sprite_render.cpp` hand-rolls its pool three times while
  `shell_effect_registry.h` exists — one pool, three TUs.
- The `*_facts.h` / `*_subject.h` gtests are source-text scrapers (`rfind` of code strings);
  every simplification here breaks them without a behaviour change, so each is rewritten to
  assert the behaviour (memory: `the-check-that-cannot-fail`).
- ConVars: `showparticlecounts`, `r_lightmap_bicubic_set`, `vcollide_wireframe`,
  `cl_interp_npcs`, `cl_ShowBoneSetupEnts`, `r_drawsprites`, `cl_spraydisable`, the four
  `r_rain*` (declared natively at `shell_effects_sim.cpp:471-474` while the header claims they
  moved), `pointworldtext_rainbowspeed_*`, `mat_reduceparticles` (registered **twice**) →
  managed; `r_drawmodeldecals` → delete (the engine declares it). Protocol names that stay:
  `cl_team`, `cl_class`, `cl_interpolate`, `localplayer_visionflags`, `closecaption`, `english`.
- The 200 stock `game/client` rows are outside the user's ask and this plan; Phase 0d groups
  them so the next plan starts from a sorted list. `default_fov` and `skill` (3e) wait on two of
  them.

### Phase 4 — what the four lanes actually found

All four lanes merged into the integration branch. **The rulings above were taken at `85a7449d1` by
read-only lanes; Phase 4 overturned them at roughly the same rate Phase 3 did.** Everything in
this subsection was re-measured by the lane that executed it, on its own base, with a committed
instrument.

**Lane 4a — the client shell.** Five of the seven items above did not survive re-measurement:

| item | plan said | measured |
|---|---|---|
| facade audit | expect the server's inverted split | **0 stubs, 0 callers receiving nothing** |
| `shell_standalone_client.cpp` | 7,400 lines, 41% comment | **6,848 raw / 2,955 code / 57% prose**; the anim-state move does not survive |
| `IGameEntity` | 7 stock callers, retype and 6 slots go | **6 callers**; retyped, relays deleted, **the slots do not go** (9 shared-relay sites block them) |
| `shell_clientdll_probe.h` | capture, then delete | owed a live run; the probe is untouched |
| `cs_sv_boneprobe` / `BoneReplay` | managed recorder + gate re-point | **blocked on an ABI slot that does not exist** |
| `shell_playerresource` | delete | 3 importers confirmed; two managed inputs missing |
| ConVars | a row to close | already closed 5 of 5 |

The `≈1,200-line anim-state move` was a raw *span*: 1,223 raw against 625 code, and of 15
candidate members 10 need the studio header while 3 are read per draw. It is not a layout change.

**The server's twenty-slot defect does not occur on the client.** 62 slots served, 0 stubs, 0 live
managed callers receiving nothing, and zero rows for the `PlaySequence` shape (a caller routed at a
nulled slot). The zero means something because `tools/shim-facade-audit --self-test` forces a slot
the client *does* bind through the same report and requires its real caller back.

**Two slots are unreached and are PINNED, not retired**: `StandaloneSetShadowCast` and
`StandaloneShadowCast`. `ClientRenderPolicy.cs` computes a shadow choice that has no way out, so
retiring the write path would record an unfinished port as done and make the managed decision
permanently unreachable. Same rule as lane 3c's: an empty slot whose caller wants an answer is a
defect ledger, not a retirement list.

**Four ways that audit read wrong before it read right**, all in the direction of "delete this": a
two-line `engineApi.X =` assignment read as bound nowhere (the tool reported its own parse bug as a
finding); facades resolved as bare method names (28 rows, every one false); receivers typed per
file when the receiver is a base-class member and the callers are subclasses elsewhere (8 live
slots called dead); and a facade passed as a method group or received through `var` (2 more).

**Lane 4b — effects.** The plan's "**blocked on one missing client-side trace slot**" is wrong and
**no trace slot is needed**. The three items resolved three different ways: one was already wired,
one wants a trace *structure* that survives to three native helpers, and one no longer uses a trace
at all because an earlier lane replaced it with a box intersection. `ClientTraceLine` already
exists. Two files the rulings marked delete are KEEP: `shell_effect_ledger` (four gate tools read
`cs_cl_effects_report`) and `PlayImpactSound`.

**The wind move is blocked on randomness, not on design.** `ClientWind.cs` needs
`CUniformRandomStream::GenerateRandomNumber`, whose implementation is **not in this repository** —
it lives in prebuilt `libvstdlib.so`. A managed port would have to reproduce the algorithm from
outside the tree, and a wrong reproduction yields a wind that *looks* right in every static check.
The experiment that settles it is in the lane's report; the move is parked, not cancelled.

**No generator for `shell_te_backend_args.h`**, ruled against deliberately: the generator would
read the same declarations its test checks, which is a check that cannot fail. That is the ninth
instance of that shape in this project.

**Lane 4c — renderers, HUD, soundscape.** Five HUD slots the rulings called dead had **five live
callers**. `hud_fastswitch` moved to managed. The sprite shell's three hand-rolled owner lookups
and the material proxies' dead counters collapsed to the one implementation that already had a
test.

**A cross-lane reading went stale, and re-measuring caught it.** Lane 4a's audit reported four STUB
slots with no managed caller in lane 4c's `shell_hud.cpp` — but 4a's base predates 4c's merge, and
4c had already fixed them. Re-run on the merged tree, those four rows are gone. Four slots remain
unreached in other lanes' files (`GetLocalPlayerHealth`, `GetLocalPlayerIndex`,
`ClientColorCorrection`, `GetLocalPlayerOrigin`) and are recorded, not acted on.

**Owed at the Phase 4 gate, both on the integrator:**

1. The `IBaseClientDLL` probe report into `docs/engine-boundary.md` from a live run, BEFORE the
   probe is deleted: `ss_cl_clientdll_probe_reset`, play, `ss_cl_clientdll_probe_report`. Output is
   native, so it lands in `game/mod_sharp/console.log` and never in the devapi log.
2. ~~A `ClientImpactChoice` crossing budget.~~ **RUN, and it found something bigger than a missing
   ceiling.** Lane 4b was right to flag rather than invent, and leaving the op without a ceiling
   stays right. But the op also had **no measurement**: `cs_te_fire all` spawns temp entities and
   never traces into a material, so the three sites that ask for a choice are never reached by it.
   The op read 0 at 6 fires and 0 at 12 — which reads like a dead port, and is not one.

   **The port is live.** Real weapon fire at a surface gives **34 choice crossings in 2.5s and 47
   more over the next 5.0s, and 0 across a 6s idle window** with the trigger released, reproduced
   three times. The tool now has a weapon arm that REQUIRES a non-zero, because an unwired slot and
   a quiet map produce the same zero. Mutation-verified red by pointing that arm back at the
   temp-entity verb. Commit `5034fb3e6`.

   The lesson generalises: **reporting an op through a verb that cannot move it is a measurement
   that cannot fail**, and it had been sitting inside a tool whose header already explained why the
   op had no ceiling.

The bone-probe item needs a `StandaloneBoneMatrix`-shaped slot appended before it can be attempted
at all: `posed` and `written` are read off the bone matrix cache and **no ABI slot exposes a bone
matrix**. "Or not at all" applies until that slot exists.

### The Phase 4 gate — RUN, and PASSED

`tools/live-gate --keep` on the integration branch at `15ffa286c`, all four Phase 4 lanes merged.
`ninja: no work to do` immediately afterwards, so the binaries the gate drove are exactly the
committed merge. (The devapi build stamp reads the pre-merge commit `d381b4d9+dirty`, because the
build ran while the merge was still conflicted and git's HEAD is the first parent until the merge
commits. That is a provenance quirk of the stamp, not a content mismatch, and the no-work rebuild
is what settles it.)

```
LIVE GATE PASSED -- 2 pass(es) of 21 step(s) (§5's 16 and 5 beyond it) in ONE process, 0 skipped
```

Suites at the same tip: native **298 binaries / 4,071 tests / 0 failures** (a full run, not a
truncated one); managed **6,255 passed / 0 failed / 0 skipped / 0 warnings**. Every one of the 42
crossing-budget rows is under its ceiling.

**§4a item 13 — the `push` control — is answered differently than expected: `push` PASSED, twice.**

| | pass A | pass B | threshold |
|---|---|---|---|
| crate displacement | 34.0 units | 13.7 units | 4.0 |

So there was no failure to control for. **The second-load weakening is still visible** — pass B is
2.5x weaker than pass A, which is the documented shape — but it stayed a factor of 3.4 clear of the
threshold on this run. Both passes reported the JAM mode (the player meets the crate and stops),
which the step records as one of this build's two modes.

**A SECOND GATE RUN, at the ABI merge `412cfaaf4`, changes the reading — and weakens the
second-load hypothesis rather than supporting it.**

| run | pass A | pass B | B vs A | mode |
|---|---|---|---|---|
| Phase 4 gate, `15ffa286c` | 34.0 | 13.7 | **2.5x weaker** | JAM both passes |
| ABI gate, `412cfaaf4` | 221.9 | 228.2 | **1.03x STRONGER** | no jam either pass |

Both runs PASSED (threshold 4.0). But the step has two documented modes — a JAM, where the player
meets the crate and stops, and a clean push — and **mode selection dominates the number by an order
of magnitude**. The first run jammed in both passes and the second jammed in neither.

So the "pass B is an order of magnitude weaker" shape did not reproduce at all on the second run,
and the 2.5x from the first run sits inside the spread that mode selection alone produces. **The
honest reading is that pass-A-versus-pass-B is confounded by which mode each pass took, and no
run so far has controlled for mode.** An experiment that wants to answer "is the second load
weaker" has to hold the mode constant or record it per pass and compare like with like —
`tools/defect-shadow-probe` runs only the push, N times in one process, which is the instrument
for it.

**THIRD RUN, `8d16d20ce`, and it settles the question against the hypothesis.**

| run | commit | pass A | pass B | B vs A | mode |
|---|---|---|---|---|---|
| 1 | `15ffa286c` | 34.0 | 13.7 | 2.5x weaker | JAM both |
| 2 | `412cfaaf4` | 221.9 | 228.2 | 1.03x stronger | no jam either |
| 3 | `8d16d20ce` | 33.9 | 47.9 | **1.4x STRONGER** | JAM both |

All three passed. **Runs 1 and 3 hold the mode constant — both jammed in both passes — and pass B
came out weaker in one and stronger in the other.** That is the comparison the "second map load is
weaker" hypothesis needed to survive, and it does not.

**FOURTH RUN, `94bd5749b`, and it is the decisive one: the two passes took DIFFERENT MODES inside
ONE process.** Pass A jammed (crate 43.9); pass B did not (crate 184.3), a 4.2x swing between two
passes of the same run on the same build with nothing varied at all.

| run | commit | pass A | pass B | modes |
|---|---|---|---|---|
| 1 | `15ffa286c` | 34.0 | 13.7 | JAM, JAM |
| 2 | `412cfaaf4` | 221.9 | 228.2 | clean, clean |
| 3 | `8d16d20ce` | 33.9 | 47.9 | JAM, JAM |
| 4 | `94bd5749b` | 43.9 | 184.3 | **JAM, clean** |

**That settles it.** Mode varies between passes *within a single process*, so the pass-A-versus-
pass-B difference can never have been attributable to the second map load. The original "order of
magnitude weaker on the second load" was one draw from a bimodal distribution, read as a trend, and
carried in this plan as a known defect for weeks.

**So §4a item 13 is answered: there is no reproducible second-load weakening.** What there is, is a
step whose outcome is dominated by which of two modes it takes, plus ordinary run-to-run spread
inside each mode. The original observation was a single draw from that distribution read as a
trend. `tools/defect-shadow-probe` remains the instrument if anyone wants the distribution properly
characterised, but nothing is owed at a gate any more.

**What no run establishes.**  The prior investigation put the threshold inside pass
B's recorded 0.7–24.2 spread, and 13.7 sits inside that same spread — so this run is a draw from a
distribution that still contains failures, not evidence the defect is gone. Quoting it as "fixed"
would be the `a-few-green-runs-are-not-absence` error. What it does license: the Phase 4 gate is
not carrying a known-failing step, and the control run the plan asked for cannot be run until there
is a failure to control.

**DECISION (user, 2026-09-20): HLTV and Replay support is DROPPED.** That supersedes the
investigation below, and it closes the blind spot the right way round — by removing the subject
rather than by testing it. Lane `5c-drophltv` executes it.

What the census found, and the trap in it: `replay|hltv|sourcetv` matches **138 code-ish lines
across 15 files**, and **70 of those 138 are a substring collision** — `BoneReplay` and the
`SS6CREPLAY` log tag belong to the bone probe, a different instrument entirely. The genuine surface
is **68 lines**, and part of it is already dead: `REPLAY_ENABLED` is defined only in
`src/vpc_scripts/source_replay_private.vpc`, which **neither project script includes**, so
`shell_engineboundary.cpp`'s `g_pReplay` / `g_pReplayServerContext` / `sv_maxreplay` block has never
been compiled in this build.

**Three things cannot be deleted and the lane is told so explicitly**: `GetReplayDelay`
(`IServerGameClients`), `ReplayInit` and `ReplayPostInit` (`IBaseClientDLL`) are engine interface
**vtable slots**. The engine calls them by POSITION; removing one shifts every later slot, and the
failure would not look like a missing replay feature — it would look like an unrelated method
misbehaving. They stay as unconditional refusals with the reason written at each.

**And the enforcement point, which the investigation below is what uncovered:** HLTV currently fails
to start only because this mod happens to lack a director module. That is luck, not a decision. The
lane is asked to make it explicit (`-nohltv`, `tv_enable 0`) so that "support is dropped" is
something the build states rather than something that happens to be true — and to prove the flag
takes effect rather than assuming a correctly spelled flag works.

---

**The investigation that preceded the decision, kept because it is what found the enforcement gap:**

**`hltv/replay`: I tried to close it, and it cannot be closed this way. The row stays NOT-COVERED.**

The gate never starts an HLTV or Replay server, and those servers are the callers that write
through `GetPlayerState`'s result with no NULL check. Phase 3c removed the
`if ( !player->GetUnknown() )` that used to open that method — correctly, because that guard *was*
the crash: the engine makes the master client as a fake client that is connected but not yet put
in the server, so `ClientPutInServer` has not run and `GetUnknown()` is NULL at exactly that
moment.

A two-arm live experiment on the Phase 4 tip, same box, same session, one variable:

| arm | `GetPlayerState` calls | answered from a player's block | orphan arm |
|---|---|---|---|
| control, no `tv_enable` | 706 | 706 | **0** |
| `+tv_enable 1 +tv_maxclients 2` | 956 | 956 | **0** |

**What it establishes.** The SourceTV master client IS created (`maxplayers set to 5 (extra slot
was added for SourceTV)`), it reaches `ClientPutInServer`, managed sees it (`howdy, SourceTV
(ent 1)`), and the server does not fault. Every `GetPlayerState` in both arms answered from a real
player block; the orphan arm never ticked in either.

**What it does NOT establish, and this is the point.** `tv_status` reports **`SourceTV not
active`**, because the console says `Mod doesn't support SourceTV. No director module found.` The
HLTV server never starts, so **its `GetPlayerState` write is never reached** and the specific
crash site is still untested. The orphan arm reading 0 in the HLTV arm is therefore not evidence
that the orphan path is unreachable — it is evidence that this experiment never got near it.

**So the blind spot is narrowed, not closed**, and the reason the obvious experiment fails is now
recorded so nobody repeats it: enabling `tv_enable` in this mod does not start SourceTV. Closing it
for real needs a director module, which this mod does not have. The control arm is still worth
having — it shows the orphan arm is genuinely quiet in normal play, so if it ever ticks, that
means something.

---

**The original wording, which stands:**  an HLTV or Replay
server is never started, and those are the only two sites that write through `GetPlayerState`'s
result with no NULL check. Phase 3c depends on that pointer being real and nothing in the checklist
would notice if it were not.

**The datatable check owed since lane 3e is satisfied by this run, and by the right evidence.**
3e's risk was that every file it touched is both-sides and the player hulls now arrive from C# on
BOTH sides, so the handshake changed sender. A datatable mismatch reports nothing when it is wrong
— the client is admitted, begins instantiating, and stops. The gate closes that three ways, each
twice:

- **two complete joins**, the process's own and a second client's, the latter reaching
  `ClientLevelInit` 1.1s after the server saw it join and building **52 networked entities** from
  the server;
- **a value written on the server and read back off the wire** — the walked origin is 355.5 units
  from where it started and **0.0 from the server's copy of it**, which is the round trip, not a
  printed number compared by eye;
- **the second client's own input moving its player 193.3 units on the server**, so the handshake
  carries commands in the other direction too.

**The `IBaseClientDLL` probe capture (§4a item 4) is landed** in `docs/engine-boundary.md` §6a:
**29 of 79 methods called, 0 refused**, with the calling engine addresses. The fifty uncalled rows
are recorded as a floor, not as a kill list, and the document says why: the gate's `save and load`
step passes twice while **all nine save/restore methods read zero**. The probe may now be deleted.

### Phase 4 exit

Client crossing budget re-recorded; datatable check; the `IBaseClientDLL` probe report is in
`docs/engine-boundary.md` before the probe is deleted.

---

## 7. Phase 5 — the ConVar ledger

**EXECUTED by lane `5d-convarledger`. `docs/convar-ledger.tsv` is the deliverable: 144 rows,
one per name.** Verdicts: **55 native-required, 31 deferred-lane, 25 reader-owed, 14 managed,
13 deferred-abi, 6 protocol-keep.** Fourteen names moved to managed
(`SourceSharp.{Server,Client}/MoveVars.cs`), both halves in one commit.

**Five claims in the section below were wrong, and the protocol-keep correction is the one that
matters:**

- **Protocol keeps are SIX, not eight.** `english` and `localplayer_visionflags` are **not** keeps:
  neither string is in the shipped `engine.so` at all. `FCVAR_USERINFO` and `FCVAR_REPLICATED` are
  transmitted **by flag, not by name**, so "the engine transmits it" was never a reason to keep
  the *declaration* native. `english` moved. The six real keeps are `cl_interpolate`,
  `closecaption`, `developer`, `r_visualizetraces`, `skill`, `sv_skyname`.
- **They were verified by NEIGHBOURHOOD, not by bare presence** — `skill` follows "Game skill level
  (1-3).", `sv_skyname` sits beside `ProcessServerInfo`, `closecaption` inside the engine's
  client-convar query block. A bare `strings` hit can be coincidence; an adjacent known string is
  evidence. **Only 6 of 113 names appear in `engine.so` at all.**
- `sourcesharp_verbose` **IS** a keep, and this was verified rather than assumed:
  `shell_diagnostics.cpp:89` reads it before managed exists.
- **`shell_gamedll_convars.cpp` still exists** (it holds `g_pDeveloper`); its seventeen moved in
  Phase 2c, not here. *(I told the lane the file was gone — my check printed a fallback on grep's
  exit code, not on a missing file. The lane corrected me.)*
- "87 rows declare a ConVar or ConCommand" → **113 in the lane's 45-file scope alone**, 144 across
  the tree. And **the three live native readers this section said to wait for never existed.**

**The ordering experiment was run and designed to fail.** Predicted wrong-order shapes: unknown
convar, resolves-but-wrong-value, or a replicated-link warning in `console.log`. Controls:
`sv_gravity` must resolve (it did, 600) and a planted `ss_5d_no_such_convar` must not (it did not).
All fourteen resolved at exactly their declared defaults, spelling included, with zero replicated
warnings and zero refusals. **It does not settle names with an EARLIER native reader** — which is
precisely why the 25 reader-owed rows stay blocked.

`convar_ledger_test.cpp` pins the ledger's `native_now` column and is mutation-verified both ways:
a ConVar added outside the ledger goes red, and deleting a keep's declaration goes red naming the
stale row. **Its own first run reported 10 phantom declarations** — gtest needles that quote a
declaration read as declarations. Same shape as the vpc assertion that read a paragraph as a
directive.

**Provenance, which the lane stated unprompted:** `tools/shim-census` was never invoked, so the
`who-imports` defect fixed in `124375929` does not touch any verdict here. Everything rests on name
greps over the 322 sources enumerated from the ninja graph's own `build … cxx` lines, `strings -a
engine.so`, and live read-back.

**THE RULES LAYER: I WAS WRONG, AND THE SURVEY SAYS 31 KEEP / 1 DELETE.**

After the user's fall-damage-and-teamplay deletion I reported that the argument probably reached
further, on the strength of three neighbours — `PlayerCanHearChat`, `GetChatPrefix`,
`GetChatLocation` — that I said had no callers outside tests and declarations.

**None of the three is in the managed rules layer at all.** They exist only in stock
`game/shared/gamerules.h` and `game/server/client.cpp`, neither of which is a `$File` row in either
vpc, so neither is compiled. They left with the eighteen callerless `IGameRules` slots at 3e, and
nothing managed was ever named after them. `grep -c` over the two managed headers returns 0 and 0.
**I sampled stock source and reported it as managed.** Sixth instance in two days of reading a name
in text as the thing itself, and the first where it set a lane's whole premise.

The real survey, re-measured: `Services.cs` declares **32** members across **five** interfaces, not
22 across one, and `GameRules` implements **7**, not 6. **Four of the five interfaces are not a
rules layer at all** — each is injected into live consumers (`PlayerResource`, `SsPickup`,
`Players`/`GameUi`, `EnvSoundscape`), so the interface is dispatched regardless of any one member's
caller count.

**Split: 31 keep, 1 delete, 0 unclear — and the one was not on my list.** It was
`GameRules.cs`'s `internal static SkillIsStillNative`: not on an interface, in no container, no
ConVar, so not an extension point by any shape, and its own documentation was **contradicted** by a
test in the same file — it blamed the client script for compiling a stock file that another fact
asserts it stopped compiling, and cited a document absent from this tree.

**A finding for the next ABI pass, measured so it need not be re-taken.** Of the four slotted rules,
**only `OnPlayerSpawned` has a native call site**. `CanTakeDamage`, `CanRespawn` and
`GetRespawnDelay` have a slot, a prototype and an assigned thunk with **no caller on the far side**
— armed, not asked. That is not the retired pair's state (their slot is gone; these are one native
line from being asked), so nothing was touched, but whether those three should be retired is a real
question.

**The three test-only `IGameRules` members are kept on documented intent**, not on caller count:
`ServerModule.cs:989` registers the rules `AddInstance<IGameRules>` **by contract**, and the line
above says why — what a map entity sees is the interface, which is what keeps `Think`, `ResetRound`
and `Reset` out of reach inside a tick. A narrower contract would compile fine and silently widen
what a map entity can call, which is why that fact exists.

**DECISION (integrator, 2026-09-21): the nine HUD selection commands STAY NATIVE, and the policy
that blocks them stays.**

Their only route to managed is `IVEngineClient::ServerCmd` / `ClientCmd`, and `EngineClient.cs`
declares no method for those. That refusal is deliberate and, in this tree's own words, *"expressed
only by absence — there is no table saying `ClientCmd_Unrestricted` must not be called; there is
simply no method for it. That is only a real rule if something notices when a method appears"*,
which `VtableBindingTests`' surface tests do.

Opening that route to move nine display commands would trade a typed boundary for a string channel.
Every crossing in this project is a typed slot whose shape a test pins; a command-running slot is an
unstructured escape hatch that any future managed code could use to bypass the ABI entirely, and
nothing downstream would notice. Nine commands that only select what is drawn are not worth that.

**They are not deferred, they are ruled.** The ledger rows stay `native-required` with this as the
reason. Revisit only if something else forces a command channel to exist — and if that happens, the
argument has to be made on its own merits, not inherited from here.

**Deferred and why:** 13 rows need an ABI slot (`Engine.RegisterConCommand` takes no FCVAR word;
`dumpgamestringtable` is among them) — **no slot was appended**, correctly, since the ABI tail was
renumbering. Nine HUD selection commands are ruled but not moved: their only route is
`IVEngineClient::ServerCmd`, and `EngineClient.cs` refuses command-running slots **as stated
policy**. That is a surface decision, not a ledger one, and is escalated rather than taken.

---

### The original section, kept because five of its claims are corrected above



87 rows in the rulings table declare a ConVar or ConCommand natively. `plan_entity.md` §6's
rule: no ConVar's default or semantics is declared natively unless the engine itself transmits
or resolves the name. The ledger (`docs/convar-ledger.tsv`, written in this phase) has one row
per name: file, flags, verdict (protocol-keep / managed / delete / dead-already), the phase that
moved it. Protocol keeps found so far: `mapcyclefile` (engine-resolved), `sv_skyname`, `cl_team`,
`cl_class`, `cl_interpolate`, `localplayer_visionflags`, `closecaption`, `english`. `sourcesharp_verbose` is a candidate keep
(the host logger may read it before managed exists — verified at this step, not assumed). Everything else moves with its phase; a gtest pins "no
`ConVar`/`CON_COMMAND` constructor in `src/sourcesharp/` outside the ledger's keep list",
mutation-verified.

### The seventeen in `shell_gamedll_convars.cpp` — all move, in this phase

Phase 1d deleted `game/server/game.cpp` and kept its ConVar declarations in
`src/sourcesharp/game/server/shell_gamedll_convars.cpp` because `game.h` still externs all
seventeen. Measured on that branch, every one of them can be a managed declaration:

- **The flags are covered.** `RegisterConVar` takes any `FCVAR_*` plus optional clamped bounds,
  so `FCVAR_NOTIFY` (thirteen of them) and `FCVAR_ARCHIVE` (`suitvolume`) need nothing new.
- **The change callback is covered.** `mapcyclefile` carries `MapCycleFileChangedCallback`, and
  the ABI already has `ConVarChanged` native→managed with `SourceSharp_OnConVarChanged` behind it
  and `OnChanged` on the managed `ConVar`. The callback body calls
  `GameRules()->ResetMapCycleTimeStamp()`, which is managed's job anyway.
- **Only three have a live native reader, and all three leave on their own schedule:**
  `displaysoundlist` ← `shell_soundent.cpp` (3d moves it to `SoundEnt.cs`, which already reads the
  value by name), `footsteps` ← `shell_subsystems_absent.cpp` (Phase 1b deletes it),
  `suitvolume` ← `shell_sentences.cpp` (3d folds it into `shell_sound.cpp`). The other fourteen
  have **zero** live native readers — they exist as `game.h` externs and nothing else.
- **Name reachability survives the move**, which is the point: `RegisterConVar` constructs a real
  native `ConVar` in the engine's registry, so the engine's own lookup of `mapcyclefile`,
  managed `FindConVarValue`, and the five names `game/mod_sharp/cfg/settings.scr` sets all still
  resolve.

So this is a Phase 5 move, not a Phase 2 one: it waits on 3d taking the three readers, and then
the file deletes entirely along with `game.h`'s extern block. **One thing to verify first, because
it is ordering and not content:** managed `ConVarRegistry.RegisterAll` must run before the engine's
first `ConVarRef("mapcyclefile")`. Experiment: declare it managed, start a dedicated server, and
check `sv_remoteaccess` resolves it — if the order is wrong, that one ConVar stays native and the
other sixteen still move.

`sv_max_usercmd_move_magnitude` (the clamp is policy), `sv_force_transmit_ents` (its only read
is `(void)`'d) and `sv_sourcesharp_fs_trace` are the three the engine-binding lane called out;
`ai_LOS_mode` and `sv_vehicle_autoaim_scale` are dead already.

---

## 8. Phase 6 — close

**CLOSED, except one item that must NOT be closed the way §8 asks.**

`tools/live-gate-budget.tsv` was last written 2026-09-14, before all eleven merged shim lanes, and
§8 wants it "re-recorded once at the end". **I ran `tools/live-gate --record` and the result was
wrong in three ways. Reverted; the file still carries its 2026-09-14 rows.**

1. **It destroyed 46 lines of load-bearing prose** and added none — a pure truncation of the
   accumulated findings, including the 2026-09-06 measurement that rate rows swing **6.3x between
   two regimes** on an unchanged build. Same hazard as `tools/entity-census --write`, in a
   different tool.
2. **It wrote the three total-gated ceilings from ONE sample**, where the file's own policy says a
   total ceiling rises only on `samples >= 4` and that `--record` must otherwise keep what is on
   disk. `damage` 1160 → 237, `die` 1362 → 442, `shutdown` 752 → 130 — each dropping 11–13 samples
   of history. "Down is free" walks a short-stage ceiling straight back under its own population,
   which is the bug `SHORT_STAGE_FLOOR` exists to prevent.
3. **It raised a RATE ceiling 6.7x** — `hot-reload` 65,748 → 443,064 — where the policy states a
   rate ceiling never rises. That run landed in the busy regime, which is precisely what the prose
   it had just deleted warns about.

**`tools/live-gate --self-test` caught the second and third by itself**: 266 checks 0 failed before,
**264 checks 4 failed** after, naming all three total rows and the hot-reload widening. The
instrument that writes the file disagrees with the instrument that checks it, and the checker is
right.

**So the owed work is not "re-record it" — it is to fix `--record` first**: preserve the prose,
honour the `samples >= 4` rule, never raise a rate row, and respect `SHORT_STAGE_FLOOR`. Then merge
several runs with `--record-totals` rather than recording one. Until then the 2026-09-14 rows are
stale but SOUND, and stale-but-sound beats freshly-wrong.



- `plan.md` §3 gains the bound-vtable route (3g); `plan_entity.md` §6 amended with §1's two corrections and the measured slot count; the
  "What remains C++" table re-derived from the rulings table's KEEP rows with reason letters.
- `docs/engine-boundary.md` gains the `IBaseClientDLL` probe report and the vtable dump.
- `tools/shim-census` and the retirement list are committed; `tools/live-gate-budget.tsv`
  re-recorded once at the end, not per lane.
- Ratchets, as gtests: no stock row in `server_csharp.vpc`; no native ConVar outside the ledger;
  every `src/sourcesharp` `.cpp` header carries a reason letter; shim line count ≤ the count
  recorded at close (a ratchet, like `docs/entity-ratchet.tsv`).
- Link libraries, re-measured with `tools/shim-census` against the closed tree: `particles.a`
  leaves the server once the manifest read is managed (3f) and `CParticleSystemMgr::Init` is
  shown unnecessary for name precache on a live join; `tier2.a` leaves the server once
  `shell_filesystem` (1c) and `precache_model`'s `g_pFullFileSystem` read the engine's
  `filesystem` instead; `tier3.a` on the server costs `mdlcache` acquired by name plus four
  `studiohdr_t` helpers and a jiggle-bone stub (~60 lines) — a judgment call, recorded either
  way; `libsteam_api.so` stays on the server only while the seven `*SteamGameServer*` slots have
  a managed consumer (34 managed files name Steam today); `mathlib.a` stays with the leaf math;
  `libmanaged_host.so` stays (h). The client keeps `particles`, `tier2`, `tier3`, `steam_api`
  (renderers, VGUI, avatars) until its stock rows are planned.
- Optional, separate decision: on-disk subfolders (0d's costed churn).
- Memory: the stale-object finding (`objs directory is not the link line`), and the
  `IGameMovement` correction.

---

## 9. Verification, per phase

| gate | command | pass |
|---|---|---|
| native cold build, both configs | `native-only-build` recipe with `ninja -t clean` first | exit 0, and `build_check` — which is native-only (memory) |
| managed suite | `MSBUILDDISABLENODEREUSE=1 dotnet test -m:1` (memory: node reuse hangs) | zero failures, **zero warnings** |
| gtests | `make check` | green; retired tests removed in the same commit as the code they pinned |
| live gate | `tools/live-gate` (two passes, one process — one map load hides a bug class) | green; `--record` only at phase close |
| both-sides files | datatable verification recipe on a live join | agree |
| crossings | `tools/live-gate-budget.tsv` | within budget; re-recorded at phase close |
| census provenance | `tools/shim-census` stamps the tip and refuses stale objects | every deletion's 0-importer proof committed |
| mutation | each new gtest/Fact is shown red on the un-fixed tree (`copy+utime`, not `copy2`; clear caches) | recorded in the commit message |

Numbers quoted in any report are re-measured with a second command (memory:
`count-before-and-after-a-mechanical-edit`); a lane's brief is re-measured at the lane's start
against its own tip (memory: `a-brief-scoped-from-a-handover-goes-stale`).

---

## 10. Open questions, and the experiment that settles each

Carried from the lanes' "could not settle" sections; each is scheduled inside the step that
depends on it, never after.

| question | experiment | blocks |
|---|---|---|
| Is `KeyValueShellOnly` still a live parser on map load? | count `CStandaloneEntity::KeyValue` arrivals over one map load with a temporary `Warning` | 2c, 3b |
| Does managed `RegisterConVar` take `FCVAR_REPLICATED`, and does a Shared declaration double-register? | one managed replicated ConVar on a live join, read from the client | 2b, 2c |
| Does a managed pair set give `ShouldSolvePenetration` the same tick as stock? | two-prop overlap on a live map, count penetration ticks before/after | 2a |
| Does the client have a physics event ring for friction? | `nm -u client_sourcesharp/physics.o \| grep Friction`, grep the client shell for a sink | 2b |
| Do the six managed callers of stub slots (weapons/team/score) observe anything? | make each stub `Warning()`, play dm_lockdown with weapons | 1e |
| Is `ModelBreakPiece` reached at runtime? | break a `prop_physics` under devapi, count calls | 1e |
| Client vs server `LookupSequence` on an ACT_ name | devapi on one v_ model, both sides | 2d |
| Does managed ever need an activity ordinal? | null the feed slot, run the managed suite | 2d |
| Does `env_microphone` hear anything today? | a map with one, and a sound | 1b |
| Do bullets need to hit triggers (`TraceAttackToTriggers` has no live caller)? | shoot a `trigger_hurt` with a managed weapon on the live gate | 1b |
| Which of the nine trace filters are constructed inline from a header? | `nm -C \| grep 'vtable for CTraceFilter'` over live objects | 3f |
| Is anything reading `EntityAbs_AreQueriesValid` during net updates? | delete the four calls, two-pass live gate, diff `console.log` | 4 |
| Double collision shake at `physics.cpp:4289`? | one heavy collision, count shakes | 2a |
| `SS_PHYSEVENT_CONSTRAINT_BROKEN` producer? | break a constraint, watch `cs_sv_physics` | 3c |
| Does `CheckTransmit` need the native walk? | perf pass, after the port | never blocks |

---

## 11. Execution

- **Worktrees, one per lane**, reset to the integration tip as step 0 (memory:
  `worktree-agents-start-on-master`); scratch files prefixed with the lane id; the integrator
  owns `sourcesharp.h`'s version and the vpc rows (memory: `abi-version-bumps-collide`,
  `always-use-subagents`). The integration branch for this plan is its own, created
  at the Phase 0 tip; the fork's working branch is not touched.
- **ABI slot retirement is mark-now, remove-once.** A lane that kills an entry point deletes the
  body, points the assignment at a retired stub, and **marks the slot** with
  `// SS_RETIRED <SlotName> lane=<lane> <reason>` above the field in both `host/sourcesharp.h`
  and `SourceSharp.Abi/Abi.cs`, plus a row in `docs/abi-retirements.md`. Marking parallelises;
  **one final pass removes every marked slot in a single commit and renumbers once**, and the
  integrator bumps the version there. No lane removes a slot: a removal from the middle renumbers
  every slot after it, and `AbiParityTests` pins names in order, so an early removal silently
  corrupts every concurrent lane's work.
- **Lane split follows the rulings table's lane column** — twelve file groups that were
  measured together are edited together. The ABI tail (slot retirements, the descriptor
  crossing, the client trace slot, the four→one level slot) is serialised through the integrator.
- Phase 0 is one lane, sequential (five commits). Phase 1 fans out after 1a. Phase 2 needs 1b–1f
  merged. Phases 3 and 4 fan out fully. Phase 5 is one lane over the merged tree. Phase 6 is
  the integrator.
- Every commit: build, suite, and — for anything touching a both-sides file, an ABI slot or the
  vpc — the live gate; commit messages carry the numbers and the command that produced them.

---

## Appendix A — how the measurements were taken

```
# sizes
wc -l src/game/server/server_csharp.vpc; grep -c 'game.server\\' …
find src/sourcesharp/game/server -maxdepth 1 -name '*.cpp' -o -name '*.h' | xargs cat | wc -l

# protobuf
grep -c '\.proto' src/game/{server/server,client/client}_csharp.vpc          # 0 0
for o in src/_vpc_/ninja/debug/objs/server_sourcesharp/*.o; do nm -C -u $o | grep -q 'google::protobuf' && echo $o; done | wc -l   # 0 (client: 0)

# PCH
grep -A3 'objs/server_sourcesharp/physics.o:' src/_vpc_/ninja/sdk_csharp_release.ninja | grep -c -- '-include'   # 1 ; bone_setup.o: 0

# platforms
grep -ohE '\[[^]]*\]' src/game/server/server_csharp.vpc src/game/client/client_csharp.vpc src/sourcesharp/sourcesharp.vpc | sort | uniq -c

# base scripts not built
grep -c 'server_base' src/_vpc_/ninja/sdk_csharp_release.ninja      # 0

# stale objects (lane finding, reproduced)
ls src/_vpc_/ninja/debug/objs/server_sourcesharp | wc -l            # 190 on disk
grep -c 'objs/server_sourcesharp/[^ ]*\.o:' src/_vpc_/ninja/sdk_csharp_debug.ninja   # 144 on the graph

# consumers (the census every lane ran, per file)
nm -C --defined-only X.o | awk '$2 ~ /[TWDBRV]/'                    # definitions with type
nm -C -u <every live object>                                        # importers; count OBJECT FILES
grep -c 'SourceSharp_[A-Za-z0-9_]*(' X.cpp                          # entry points; managed reaches them by engineApi slot NAME, never the C name

# tests that read vpc text
grep -l '_csharp.vpc\|_base.vpc' src/sourcesharp/test/*.cpp | wc -l   # 108
```

Lane outputs (rulings, notes, per-object consumer tables) are in this session's scratchpad and
summarised in `plan_shim_rulings.tsv`; the notes' per-file reasoning is reproduced there in the
`notes` column and cites line numbers at `85a7449d1`.
