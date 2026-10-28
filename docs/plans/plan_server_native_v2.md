# server.so: what is left, and how it leaves (v2)

Uncommitted draft (repo rule: plans wait for user review). Base: main
42b718990 (2026-10-18). Supersedes the inventory, per-file verdicts and
phases of `plan_server_native_min.md` (2026-10-11, base baaebbe6a), whose
ledger (`server_native_audit.tsv`), recipes (§8, A-G) and six user rulings
stay in force and are referenced here, not repeated. Every number below was
measured this session at 42b718990 with the command named beside it; the
fork was clean after every run.

**Asked (user, 2026-10-18):** what code is left in server.so, and a plan to
remove, port or reduce it; server.so should only ever include public Valve
headers, and, asked mid-audit, whether it compiles without the
`patches/source-sdk-2013/` series.

## 0. Where the 2026-10-11 plan stands

Not started, except the lane it declared out of scope. Of its P0-P4 nothing
landed: no `tools/server-native-scope` check, no `docs/server-native-ratchet.tsv`,
and every whole-file deletion it named in §3 is still on the link
(`collisionproperty.cpp`, `shell_restoreentities.cpp`, `shell_playerslots.cpp`,
`shell_playerlocaldata.cpp`, `sendproxy.cpp`, `dt_utlvector_common.cpp`,
`shell_gamedll_convars.cpp`, `baseentity_statics.cpp`, `shell_absent.cpp`,
`shell_playerinfo.cpp`, `plugin_check.cpp`, `shell_te_forwards.cpp`,
`shell_stringtables.cpp`; checked by path this session). What did land since
baaebbe6a and changed the server link: physics and bones to managed
(`plan_physics_bones_native_zero`, DONE 2026-10-12: `physics_shared.cpp`,
`bone_setup.cpp`, `bone_library.cpp`, `studio_sequence_timing.cpp` off the
link), the mathlib port moved to `src/shared/native` for both DLLs (C9), the
engine forwarders (`libss_fwd_server.so`), `entity_bases.cpp` deleted, and
the two random-port TUs moved client-only.

## 1. Inventory at 42b718990

Instruments: `python3 tools/cmakegraph.py objects server --config debug`
(the link edge, 123 objects), `size -B` over those objects and the deployed
`.so`, `ninja -C farm/debug/build -t deps` over the 123 objects (headers),
`nm -g` over the objects (who imports whom), `tools/physics-scope/fnmap`
over every TU joined against the 2026-10-11 ledger, `make debug` and
`make release` run in this checkout at the tip, and a `-fsyntax-only` trial
of every TU's own compile command against the pristine upstream clone
(`../source-sdk-2013-valve` at `valve-sdk.lock` 22288b91).

| what | number |
|---|--:|
| TUs on the link edge | 123: 79 `src/server/native` + 44 `src/shared/native`; 24 are debug-only (`server_sourcesharp_debug_sources`) |
| raw `.cpp` lines | 58,241 (36,040 server + 22,201 shared-on-the-server-list) |
| code lines (comments and blanks stripped) | 25,345; 1,369 debug-only; **23,976 compiled into release** |
| our headers the objects reach | 171 files, 36,668 lines: 97 shared (22,966), 71 server (10,523), `src/host/native/sourcesharp.h` (2,824, the ABI data), `engineabi/saverestore.h` (240), fwd (115) |
| fork headers the objects reach | **153 files, every one under `src/public/`, 63,105 lines**; 11 of them carry hunks from patch 0001 (below); 3 more are OUR files served at `public/` paths (`igameentity.h` 301, `entity_funcslot.h` 109, `chain_state_notify.h` 86) |
| what server.so links | `libmanaged_host.so`, `libss_fwd_server.so`, libc/m/dl/pthread/uuid. No Valve archive (`cmakegraph.py libs server`) |
| exports | one: `CreateInterface` (`nm -D`). It serves 10 interface names (`shell_factory.cpp` `kExposed[]`): ServerGameDLL, ServerGameEnts, ServerGameClients ×3 versions, ServerGameTags, PlayerInfoManager, PluginHelpersCheck, ServerDllSharedAppSystems, GameMovement |
| managed→native surface | 290 rows for the server in `docs/engineapi-areas.tsv` (231 `server` + 59 `both`) across 38 `IShim*` areas; 505 `SourceSharp_*` bodies in the server TUs behind them (grep); none exported |
| native→managed surface | 20 server-side interfaces in `docs/managed-interfaces.tsv` (IManagedRuntimeServer 29 methods, IManagedClients 15, IManagedSoundController 13, IManagedHierarchy 11, IManagedEntities 8, ...). 7 are managed-built Valve-layout vtables today (IMotionEvent, IPhysicsCollisionEvent, IPhysicsCollisionSolver, IPhysicsConstraintEvent, IPhysicsObjectEvent, IVPhysicsKeyHandler, IWakeProbe) |
| debug `server.so` | `.text` 939,083 B (objects' `.text` sum before GC 1,126,796; debug-only objects 86,287); file 6,594,672 |
| release `server.so` | **`.text` 403,711 B, file 601,568** (`make release` this session; the previous binary was 2026-10-03's) |

The 11 patched public headers and how many of the 123 objects reach each
(`ninja -t deps`): `tier1/utlblockmemory.h` 92, `datamap.h` 76,
`iserverunknown.h`/`iservernetworkable.h`/`iserverentity.h` 73,
`gametrace.h` 68, `dt_send.h` 49, `eiface.h` 48, `networkvar.h` 27,
`isaverestore.h` 1 (`world_adapter.cpp`), `haptics/haptic_utils.h` 1
(`sound_emitter.cpp`). Patches 0002-0005 touch nothing server.so reads.

### 1.1 The biggest objects (debug `.text`, code lines, 2026-10-11 verdict lines where the ledger row joined)

| `.text` B | code | object | ledger lines by verdict (joined rows only) |
|--:|--:|---|---|
| 83,604 | 898 | sourcesharp_gamesystem.cpp | HOST 256, THUNK 143, OUT 133, MOVE 73, DELETE 51 |
| 48,173 | 888 | shell_standalone_base.cpp | OUT 438, MOVE 182, LAYOUT 81, THUNK 36, DELETE 16 |
| 48,030 | 265 | shell_standalone_class.cpp | OUT 45, REG 29 (the factory and send tables) |
| 46,592 | 726 | shell_standalone_player.cpp | MOVE 175, OUT 147, THUNK 51, LAYOUT 46, 96 unruled (ShadowFacts etc., new since) |
| 38,423 | 227 | mathlib_port.cpp | unruled (the C9 port; substrate) |
| 36,290 | 978 | studio.cpp | OUT 395, 211 unruled; **imported by model_descriptor.cpp only (8 symbols)** |
| 35,604 | 897 | shell_worldquery.cpp | MOVE 418, OUT 235, FOLD 104, DELETE 83 |
| 33,391 | 849 | sound_emitter.cpp | ledger rows hand-spelled (MOVE-dominant per the old §3) |
| 30,612 | 379 | shell_tempents.cpp | MOVE 232, LAYOUT 40; **235 KB of `.bss`** (the TE pool) |
| 28,558 | 880 | shell_entityapi.cpp | MOVE 374, OUT 218, DELETE 130, FOLD 47, LAYOUT 47 |
| 26,555 | 788 | shell_movement.cpp | hand-spelled rows (THUNK/OUT-dominant, MOVE 90, DELETE 76 per old §3) |
| 24,863 | 344 | shell_soundenvelope.cpp | hand-spelled (MOVE-dominant) |
| 23,618 | 175 | shell_playerlocaldata.cpp | MOVE 19; **DT_Local: no server object imports its table** |
| 17,670 | 2,194 | collisionutils.cpp | never ruled; **one symbol imported, `IsBoxIntersectingSphere`, by shell_worldquery.cpp** |
| 16,833 | 320 | shell_savegame.cpp | MOVE 193, FOLD 36 |
| 16,803 | 398 | world_adapter.cpp | MOVE 135, LAYOUT 93 |
| 15,711 | 61 | shell_areaportal.cpp | MOVE 26, FOLD 16, OUT 10 |
| 14,765 | 134 | shell_shared.cpp | hand-spelled (DELETE-dominant per old §3) |
| 14,661 | 452 | shell_standalone.cpp | DEBUG 182, REG 150 |
| 13,830 | 128 | shell_gameevents.cpp | FOLD 58, OUT 40 |
| 13,769 | 462 | convar_port.cpp | hand-spelled (THUNK/REG: engine ABI) |
| 13,662 | 565 | shell_diagnostics.cpp | DEBUG 274, HOST 209 |
| 13,189 | 509 | shell_gamedll.cpp | 55 IServerGameDLL slots, 30 pure forwards |
| 12,313 | 340 | shell_gameclients.cpp | THUNK 139, MOVE 102, DELETE 28 |
| 12,282 | 188 | physics.cpp | was OOS; now in scope: CPhysicsHook registration + held globals |
| 12,107 | 105 | model_descriptor.cpp | OUT 66; imported by sourcesharp_gamesystem.cpp only (1 symbol) |
| 11,289 | 533 | dt_send.cpp | MOVE 269, LAYOUT 82, DELETE 78, THUNK 75 |

Full table: regenerate with the three instruments above (the join script is
Recipe G's; its output this session is not committed).

### 1.2 The ledger, re-joined at the tip

`server_native_audit.tsv` has 1,654 rows. Joined on file + fnmap head line:
1,069 functions match (DEBUG 113 rows/1,700 lines, DELETE 165/1,436, FOLD
49/445, KEEP-HOST 66/988, KEEP-LAYOUT 39/450, KEEP-OUT 262/2,441, KEEP-REG
33/342, KEEP-THUNK 108/831, MOVE 196/3,763, OOS 38/542); 374 functions at
the tip have no joined row (5,013 lines) and 292 ledger rows join nothing.
Three causes, all mechanical: (a) 65 OOS rows landed with the physics plan;
(b) the rows of sound_emitter, shell_movement, convar_port, tier0/tier1
ports, shell_shared, shell_sound, shell_soundenvelope, gamestringpool and
alloc_check were hand-spelled (`CSoundEmitterSystem::EmitSound`, `ConVar
ctor(3)`) rather than as fnmap heads, so they neither join nor are missing;
(c) three files were never ruled: `collisionutils.cpp` (2,075 code lines),
`physics.cpp`/`physics_debug.cpp`/`shell_physicsobjects.cpp` (386, were
OOS), `mathlib_port.cpp` (202, arrived with C9). T0 below fixes (b) and
(c) so the ratchet joins exactly.

### 1.3 Does server.so compile without the patch series? (asked 2026-10-18)

Not yet, but it is one naming change away. The trial compiled each of the
123 TUs with its own command from `compile_commands.json`, the three farm
`public/` include dirs swapped for the pristine clone's (clang++-22,
`-fsyntax-only`):

| include set | TUs failing | the errors |
|---|--:|---|
| stock `src/public` only | 97 | `igameentity.h` not found (68 TUs) and the bit-field error below |
| stock + our three overlay headers on the path | 92 | every one on `utlblockmemory.h:140-141`: `non-const reference cannot bind to bit-field` (clang only, diagnosed at the template's definition; **g++ 15.2 accepts the stock header**, so the CI build already compiles it unpatched) |
| stock + overlay + the clang fix for that one header | **48** | **every error is the `IGameEntity`/`CBaseEntity` naming**: 93 `GetBaseEntity` return not covariant (stock declares `CBaseEntity*`, our shells return `IGameEntity*`), 48 `GetGameEntity` `override` overrides nothing, 25 no `GetGameEntity` in `IServerEntity`, 22 handle helpers (`GameEntityFromEntityHandle`, `SourceSharp_IsTheWorld`, `SourceSharp_HandleFromAnyEntity`), 8 `IGameEntity` unknown, 8 `IHandleEntity*` vs `trace_t::m_pEnt` |

The 75 passing TUs include dt_send, shell_tempents, shell_savegame,
sound_emitter (its haptics include compiles stock too), convar_port and the
whole host substrate. The server uses none of patch 0001's other
constructs: `DEFINE_FUNCTION`/`EntityFuncSlot` (0 server sites),
`BEGIN_SEND_TABLE` with a base (0; only `_NOBASE`), `CNetworkHandle` (0),
`DECLARE_NETWORKVAR_CHAIN` (0 outside the overlay).

## 2. What is left, by kind

The 23,976 release code lines fall into the same bins as before; the
binding bins are what the engine forces (CLAUDE.md: a vtable it calls, a
layout it reads, a registration it needs, a pointer it holds), everything
else leaves.

| bin | what, at the tip | goes by |
|---|---|---|
| engine-called vtables (KEEP-THUNK) | IServerGameDLL (55 slots), IServerGameClients (19), IServerGameEnts (6), IServerGameTags, SharedAppSystems, CShimLevel/IShimFrame, CStandaloneEntity's ICollideable/IServerEntity/IServerNetworkable, CShellGameMovement, ConVar/ConCommand carriers, the sound-emitter and string-table forwards | pure forwards become managed-built Valve-layout vtables (T3.4); hot per-entity ones measured first |
| layouts the engine reads (KEEP-LAYOUT) | SendProp/SendTable/ServerClass, edict/PVSInfo, the engine block, CPlayerState, saverestore records, `sourcesharp.h`'s 43 structs | stays; shrinks only as readers go |
| registration (KEEP-REG) | `CreateInterface` + `kExposed[]`, factory tables, kServerSystems, ConVar_Register, CPhysicsHook, DLLInit | stays (minus PlayerInfoManager, PluginHelpersCheck, GameMovement: rulings 1 and 4) |
| CoreCLR host + substrate (KEEP-HOST) | managed_runtime_server, shell_internalfactory, exit_watch, fatal_report, tier0/tier1 ports, mathlib_port (38 KB), convar_port's ConVarRef, gamestringpool | stays; the ports shrink with their native readers (T5) |
| marshalling-only outbound (KEEP-OUT) | traces, PVS, precache, string tables, mdl reads, the entity API's getters | FOLD onto the 159 engine Vtbl layouts managed already has (`src/abi/VtableLayouts.g.cs`) where a layout exists; the rest after T3.4/P3f |
| logic (MOVE) | sound (sound_emitter, soundenvelope, shell_sound: ~67 KB `.text`), tempents + datatables (42 KB), standalone entity base/player (MOVE 357 lines), worldquery/entityapi (MOVE 792), save/restore codec, areaportals, hierarchy, variant_t, player command path, movement constants, ClientSetupVisibility, model descriptors | T4, per subsystem, test-first |
| dead (DELETE) | the §3 whole files of the old plan, plus found this session: `collisionutils.cpp` kept for one function; `shell_playerslots.cpp` referred to only by a debug object; `studio.cpp`'s 22 unimported `CStudioHdr`/`studiohdr_t` members; 8 unreached headers (760 lines: `entity_bases.h`, `push_blocker_rule.h`, `notify_gate.h`, and the five under `src/server/native/sdk/game/server/`, read only by 20 gtests' text checks) | T2 |
| debug-only (DEBUG) | 24 TUs, 1,369 code lines, 86 KB debug `.text`; release compiles none | T3.3: debug-API routes or deletion (ruling 5) |

## 3. Tracks

Order: T0, then T1 (it respells a type in 48 TUs and every later lane
would conflict with it), then T2-T4 in lanes as ruling 6 ordered the old
P3, then T5. Per commit: `make debug` (zero warnings), the touched tests,
`make debug GC_SECTIONS=0` after any eviction, fork clean. Per track:
`make check`, `make test-full` before a landing that changes the link line
(test-full-before-link-landing), the daily gate's next report before the
next track builds on the new static-init order. Ledger rows close in the
same commit as the code (Recipe G).

### T0. The ratchet, re-keyed (1-2 commits; Recipe G)

1. Re-key the hand-spelled ledger rows (§1.2 (b)) to fnmap head lines so
   the join is exact; rule the never-ruled files (§1.2 (c)): `collisionutils.cpp`
   DELETE-after-MOVE (one live function), `physics.cpp` KEEP-REG with its
   held globals, `shell_physicsobjects.cpp` KEEP-OUT (vphysics asks it
   mid-step), `physics_debug.cpp` DEBUG, `mathlib_port.cpp` KEEP-HOST,
   `shell_standalone_player.cpp`'s 7 new functions by reading them.
2. Land `tools/server-native-scope` in `tools/checks.tsv` as the old P0
   described (every function has a ruling; per-file MOVE+DELETE+FOLD+DEBUG
   lines may only fall; `docs/server-native-ratchet.tsv`). Add two columns
   the old ratchet lacked: release `.text` per object (`size -B`, so a move
   that leaves a copy behind shows) and the stock-header canary count (T1).

### T1. Stock public headers only (the user's 2026-10-18 ask; ~8 commits)

End state: the server's R5 include set is the pristine upstream's
`src/public` (no patch, no overlay served at a Valve path), and a check
proves it every run.

1. **The canary first** (Recipe H): `tools/server-stock-headers`, in the
   full tier, compiles every server TU `-fsyntax-only` against the pristine
   clone (`VALVE_SDK`, `valve-sdk.lock`, already provisioned by `make setup`
   for the oracles) and holds the failing-TU count as a ratchet starting at
   48. Refusal half: re-introduce one `GetGameEntity` override in a scratch
   copy and see red. Lands with T0 so progress is scored from the start.
2. **Decision D1: the server's entity face is spelled `CBaseEntity`.** Stock
   headers forward-declare `class CBaseEntity` and return it from
   `IServerUnknown::GetBaseEntity`, `IServerNetworkable::GetBaseEntity`,
   `IServerGameEnts::EdictToBaseEntity`, `trace_t::m_pEnt` (the server arm),
   `EHANDLE` and `inputfunc_t`; the patch exists only to respell those as
   `IGameEntity`. If our class *is* `CBaseEntity`, every one of the 48
   failures goes away without a header edit. Mechanics: `igameentity.h`
   leaves the overlay for `src/shared/native/game_entity.h`, defines
   `class CBaseEntity` under `GAME_DLL` with today's body, and keeps
   `using IGameEntity = CBaseEntity;` so the 75 files that say `IGameEntity`
   need no edit at all (the client keeps its own spelling: the alias is
   per side). The build enforces the name once: a must-not-compile test
   that a second `class CBaseEntity` definition on the server fails.
3. `GetGameEntity()` collapses onto `GetBaseEntity()` (15 files;
   `handle_cross.h`, `ss_entityhandle.h`, `shell_resolve.h`): the two
   partition the world identically now that no SDK `CBaseEntity` exists on
   the server, so the added `IServerEntity` virtual (a slot appended to a
   Valve interface) is not needed. Facts: `handle_cross` tests pass with
   the one accessor; the live gate's second map load.
4. The other two overlay public headers (`entity_funcslot.h`,
   `chain_state_notify.h`) have no server user: they move under
   `src/client/native/sdk/public/` (client-only overlay). `datamap.h`'s
   `DataMapAccess(IGameEntity*)`/`PredMapBaseAccess` hunks are then unused
   by the server too (`entity_bases.h`, their one echo, is dead: T2).
5. **Decision D2: the clang bit-field error in `tier1/utlblockmemory.h`.**
   Stock compiles under gcc (CI) and not under clang 22 (the host). Options:
   (a) a compile-only overlay of that one header in `cmake/compat/clang/`
   (the Windows precedent: `cmake/compat/windows/`), carried as
   OURS-OVERLAY with the reason in its header; (b) build server.so with gcc
   on the host too (`SS_CXX=gcc` for one target: loses `-Wunused-*`
   parity and lld's ICF); (c) ask upstream. Recommended: (a), named in
   `docs/valve-header-new.tsv`, and the canary counts it as the one
   excused header.
6. **Decision D3: switch the include root.** When the canary is at 0, the
   server's `server_novalve_includes` point at the pristine tree
   (`tools/setup` links `farm/<config>/stock-public` from `VALVE_SDK`;
   `sdk-links/server.txt`+`shared.txt` keep listing what is read, now
   against the lock in `valve-sdk.lock`). From then on a server TU that
   needs a patched construct fails the normal build, and the canary becomes
   a self-test of the setup (the link resolves to the pristine tree, not
   the fork). Patch 0001's server-only hunks (`iserverentity.h`'s added
   virtual, `iserverunknown.h`, `iservernetworkable.h`) can then leave the
   series in the client plan's time, not this one's: the client still
   reads them.

Exit: `tools/server-stock-headers` at 0; `make sdk-census` green with the
server lists against the pristine lock; `patches/` unchanged (the client's);
release `.text` unchanged within noise (a rename moves no code; confirm by
`size -B` before/after).

### T2. Deletions (old P1, re-proven at the tip; ~30 commits; Recipes A, B, C)

Each with its proof in the commit message (`nm -g` over the 123 objects,
`--print-gc-sections`, grep over `src/` with tests, `GC_SECTIONS=0` link).
Evidence found this session, to re-run per unit:

- `collisionutils.cpp` (2,194 code lines, 17.7 KB): one importer,
  `shell_worldquery.cpp:235` → `IsBoxIntersectingSphere` (37 lines). Move
  that function beside its caller, or its caller to managed (the client
  already has a managed port in `PrecipitationSim.cs`; the server would take
  a shared one), evict the TU, delete its `sdk-links` line if any.
- `studio.cpp` (36 KB): 30 symbols defined, 8 imported, all by
  `model_descriptor.cpp`; the 22 others (`GetActivityListVersion`,
  `CountAutoplaySequences`, the `studiohdr_t::` twins) are DELETE after an
  intra-TU check (`nm` cannot see those).
- `shell_playerslots.cpp`: 6 symbols, every importer is
  `shell_standalone_player_debug.cpp` (debug-only). Managed has PlayerNames
  and PlayerPing.Sample. Goes with the debug command (T3.3) or before it.
- `shell_playerlocaldata.cpp` (DT_Local, 23.6 KB, 6.8 KB `.data`) + its only
  consumers `sendproxy.cpp`, `dt_utlvector_common.cpp`: no server object
  imports the table (importers: `shell_frame`, `shell_standalone_player`,
  for the struct's ctor only).
- `collisionproperty.cpp` (one symbol imported, by `shell_serversystems`'s
  `kServerSystems[]` row), `shell_restoreentities.cpp`,
  `baseentity_statics.cpp`, `shell_gamedll_convars.cpp`, `shell_absent.cpp`'s
  DELETE rows, `shell_playerinfo.cpp` + `plugin_check.cpp` + their
  `kExposed[]` rows (ruling 1), the `GameMovement001` export (ruling 4),
  `shell_te_forwards.cpp` once its native callers call managed (T4).
- The 8 unreached headers (760 lines). 20 gtests read their text: each
  test is retired with the header, not satisfied by keeping it.
- The dead bodies inside live files that the old P1.2 lists (24 inert
  slots in entityapi/worldquery, 14 no-op weapon slots, IShimFrame's 4
  empty slots, CShimLevel's 13, ...), each with its `engineapi-areas.tsv`
  row and EngineSide wrapper in the same commit; one serialised ABI
  renumbering at the end (`docs/abi-retirements.md`).

Exit: DELETE at zero in the ratchet; 10 → 7 names in `kExposed[]`;
`GC_SECTIONS=0` links; release `.text` down by the evicted objects' sizes
(quote before/after).

### T3. Folds, round trips, debug routes, managed-built engine vtables (old P2; ~25 commits; Recipes C, D, E)

Unchanged from the old plan's P2.1-P2.4 and ruled (rulings 2, 4, 5). Sizes
at the tip: FOLD 49 joined functions/445 lines plus the hand-spelled sound
and string-table rows; 159 engine `*Vtbl` layouts exist in
`VtableLayouts.g.cs` (GameEventManager, NetworkStringTable(Container),
BaseFileSystem, DebugOverlay, EngineServer, EngineTrace, Effects,
ModelInfo, PhysicsSurfaceProps, UploadGameStats, SoundEmitterSystem,
EngineSound, Cvar are all there). T3.4's managed-built Valve-layout vtables
have seven precedents in the tree already (§1); order IServerGameEnts (6),
IServerGameClients (19), IServerGameDLL (55, `DLLInit` stays native), each
with its `docs/managed-interfaces.tsv` block and AbiParityTests, the hot
per-entity vtables measured on dm_lockdown before they follow.

### T4. Logic to managed, by subsystem (old P3a-P3g, ~5,000 lines; the long track)

Ruling 6 orders it: a (sound), b (tempents + datatables), c (save/restore)
in parallel lanes, then d (entity API/world) and e (lifecycle), then f (the
two engine callbacks), then g (player command path and movement). Tip
sizes to score each lane by, from §1.1: sound 67 KB `.text` across three
objects; tempents 30.6 KB + 235 KB `.bss` + dt_send 11 KB; save/restore
16.8 KB; worldquery + entityapi 64 KB (MOVE 792 lines); world_adapter 16.8
KB; areaportal 15.7 KB; standalone base + player MOVE 357 lines; movement
26.5 KB; gameclients 12.3 KB. Two additions since the old plan:

- **Model descriptors** (`model_descriptor.cpp` 12 KB + `studio.cpp` 36 KB
  + `studio_mdlcache.cpp`): kept because `ModelDescriptors` lives in
  Shared.Interop and cannot reach the managed StudioHdr (docs/todo.md,
  2026-10-12). **Decision D4:** move `ModelDescriptors` to where the
  managed StudioHdr is reachable and retire the `IShimModels.ModelDescriptor`
  slot (one serialised ABI change); then the whole chain leaves the server
  link (the client keeps `studio.cpp` for its own readers). 48 KB of
  release `.text` for one slot.
- **physics.cpp / shell_physicsobjects.cpp** (now in scope): CPhysicsHook
  is KEEP-REG, the held globals (`physenv`, `g_PhysWorldObject`,
  `physcollision`...) stay as engine-held pointers, and the four native
  decisions the physics census left are the movement lane's (P3g).

### T5. Substrate collapse and close (old P4)

Re-measure importers of `mathlib_port.cpp` (13 symbols imported today by
19 objects), `convar_port.cpp`'s ConVarRef (15 importing objects),
`gamestringpool.cpp`, the tier0/tier1 ports; delete what has no importer
left after T4. Close with §1's instruments re-run at the tip, the
before/after table with shas, and the census of what native stays and why.

**DONE 2026-10-19** (lane T5, base 91296dc75, 23 commits). Importers were
measured with nm U over both link edges (no static archive is on either
link any more) and confirmed by the linker's own discard list; every
deletion ran `tools/nogc-relink`, and the TU eviction a full
`make debug GC_SECTIONS=0`. The measured close is §4; what stays native and
the follow-ups no design decision covers are docs/todo.md's T5 section.

### T6. Native globals and statics to one root (user ask 2026-10-18; plan_globals.md's design, re-measured)

The design already exists: plan_globals.md §2, "one root, two tiers". One
static, `s_gameShellDll`; a DLL-tier object (`m_pDll`, `new` in DLLInit,
`delete` in DLLShutdown) holding EntitySlots, NetTables, TempEnts, Precache,
Players, Sound, Diagnostics; a level-tier object (`m_pLevel`, `new` in
LevelInit, `delete` in LevelShutdown) holding physics, per-map caches and
latches, so a reload leak is impossible by construction; and every
managed→native call arriving on a bound vtable whose `this` is the owner, so
no free `SourceSharp_*` body needs a global to find its state. That plan's
numbers are stale (its own banner says so); the instrument is current.

Measured this session (`make globals-census`, debug, tip a594c5e14 with
uncommitted docs; `tools/globals-census` reads the link edge, `readelf`
over the 123 objects, state = OBJECT/TLS symbols in `.bss`/`.data`/`.tbss`/
`.tdata`, never a source grep):

| what | number |
|---|--:|
| mutable statics, link level (`docs/globals-ledger.tsv` rows) | 359; object level 390; 1,716,959 bytes |
| objects with a static initialiser | 108 of 123 |
| ledger dispositions | UNRULED 173, MEMBER 59 (1,053,460 B: `g_LineBuffer` 1 MB), CONST 48, DEAD 45, MANAGED 26, FLOOR 8 |
| UNRULED by census category | other 105 (102,758 B), engine-iface-ptr 35 (`engine`, `gpGlobals`, `filesystem`, `physenv`...: 28 in shell_engineboundary.cpp, 8 in physics.cpp), abi-block 23 (536,023 B: `g_TeRegistry` 232 KB, `g_EntitySlots` 164 KB, `s_SlotTable` 82 KB, `s_Types` 43 KB), datatable 9, convar 6, pool 1, factory 1, gamesystem 1 |
| gate | PASS (every symbol has a row; no row without a symbol) |

Steps (each closes ledger rows; `make globals-census` gate green per commit;
the live gate's second map load is the reset proof for every level-tier
move):

1. **Rule the 173 UNRULED rows** by the plan's dispositions (DEAD, CONST,
   MANAGED, MEMBER with owner tier, FLOOR) in `docs/globals-ledger.tsv` via
   `make globals-ledger` (reconciles, never regenerates). The engine-iface
   pointers are MEMBER on the root's engine binding (plan_globals §2:
   `m_pEngine`, `m_pGlobals`...); the four abi blocks are MEMBER `m_pDll`
   (EntitySlots, TempEnts, NetTypes); the player-info and boundary-probe
   rows are DEAD with T2 (ruling 1); the `s_Said*` refusal latches are
   DEAD with the MOVE of their bodies.
2. **DEAD rows go with T2** (45 ruled + the newly ruled): 12,740 bytes
   today; DT_Local's 28 datatable rows are the `shell_playerlocaldata`
   eviction.
3. **The scaffolding** (plan_globals G2): `CServerDllState` and
   `CServerLevelState` behind `m_pDll`/`m_pLevel`, constructed in
   DLLInit/LevelInit; `IShimRoot::Acquire` as the bootstrap pair replacing
   `SourceSharp_InternalFactory`. One integrator commit series before any
   fan-out; the ABI tail serialised as everywhere in this plan.
4. **MEMBER rows move**, owner by owner (59 ruled + the abi blocks + the
   engine pointers): a subsystem object implements its `IShim<Area>` and
   reads `m_...`; its former globals delete in the same commit. Order: the
   four abi blocks first (536 KB, and every entity op resolves through
   `EntitySlots`), then the engine binding, then Diagnostics/Console/
   Overlaps/World/NetTables/GameRules/Physics/Players by tier. This rides
   T3's folds and T4's moves: a MOVE to managed closes its rows as MANAGED
   instead, so T6's step 4 is done last in each lane, over what stays.
5. **Static initialisers** (plan_globals G5): the 108 objects with
   `.init_array` entries fall to the root, the `InterfaceReg` nodes and
   whatever D6 keeps; `tools/globals-census summary`'s
   `objects_with_a_static_initialiser` is the ratchet.
6. **C callbacks** per D6: `ShellSide()` beside the root for the spew hook,
   the signal handler and ConVar change callbacks; a gtest pins the calling
   files (plan_globals §2.3).

**T6 part 1, re-measured at main f087134f5 (2026-10-19)** (`make globals-census`,
debug server, 98 objects on the link edge; `tools/globals-census` reads them
with readelf):

| what | number |
|---|--:|
| link_level (`docs/globals-ledger.tsv` rows) | 225; object level 247; 1,411,344 bytes |
| objects with a static initialiser | 85 of 98 (the ratchet row read 104: lowered to 85) |
| dispositions before | UNRULED 123, MEMBER 51, CONST 30, MANAGED 9, FLOOR 8, DEAD 4 |
| UNRULED by census category | other 66, engine-iface-ptr 34, abi-block 9, datatable 9, convar 4, string pool 1; the big blocks `g_EntitySlots` 163,856 B, `s_SlotTable` 81,936 B, `s_Types` 43,016 B (`g_TeRegistry` went in T4b) |
| dispositions after step 2 | UNRULED 0, MEMBER 133, CONST 35, DEAD 29, MANAGED 18, FLOOR 10 |

Two rulings differ from the step-1 text above: `BoundaryProbes()::s_Probes` is
MEMBER Diagnostics, not DEAD, because `tools/live-gate`'s join step reads
`ss_boundary_probe_report` in debug; and the engine pointers read only inside
the acquisition (`datacache`, `enginesound`, `g_pVoiceServer`, `scenefilecache`,
`soundemitterbase`, `sv_cheats`) are DEAD as globals, their refuse-if-missing
checks kept as locals. `g_pSharedChangeInfo` needs a fork edit to go further
(plan_globals G2.4), so it waits for the user.

**RULED 2026-10-20 (user): the two `bitvec.h` tables need no fork edit.**
`bitvec.h` stays (the engine passes `CBitVec` through `eiface.h`'s transmit
info and `Message_DetermineMulticastRecipients`). Its tables (`firstBitLUT`,
1,024 B; `g_StartMask`, 128 B; ruled CONST, never written) reach server.so
only through `ss::CRecipientFilter::AddPlayersFromBitMask`, which the native
PVS/PAS filters still use (shell_te_forwards.cpp, server_util_library.cpp).
Of the three ways out offered (leave them; a `__builtin_ctz` loop of our own;
let them go with their last caller), the user chose the third: they leave the
link when the remaining native PVS/PAS filter users move to managed, which
`RecipientFilter.cs` already ports. No code changes for them now.
Done 2026-10-27: the PVS/PAS filters had no caller left, and the tables went
with `AddPlayersFromBitMask` (`globals_link_level` 118 -> 116).

**T6 part 1 done (2026-10-19, lane t6-globals, steps 1-3 and step 4's first
item; not yet on main).** Measured at the lane's tip the same way (debug
server, 98 objects): link_level 225 -> **186**; object level 1,411,344 ->
**1,116,807 B**; MEMBER 123, CONST 35, MANAGED 18, FLOOR 10, DEAD 0, UNRULED 0;
85 objects with a static initialiser (unchanged). What landed:

- every DEAD row went (3a-3h), including two the rulings exposed:
  `g_TePrediction` (its last caller was the multi-damage apply) and the server's
  `cvar` (client-only now). `IShimFrame::SetServerCurTime` and
  `IShimLevel::ResetServerTime` are `SS_RETIRED` empty slots awaiting one
  `--reorder` pass (docs/abi-retirements.md).
- the scaffolding (4a, 4b): `server_state.h`'s `CStateTier<T>` behind the root's
  `m_pDll` (DLLInit..DLLShutdown) and `m_pLevel` (LevelInit..LevelShutdown);
  `CShimRootServer`, the bootstrap object, a by-value member of the root whose
  `Acquire` is the server's row set (SourceSharp_InternalFactory is the
  client's alone).
- the abi blocks (5a-5c): EntitySlots (`g_EntitySlots`, `s_SlotTable`),
  NetTypes (`s_Types`) and TempEnts (`g_TeBuilt` and four siblings) are
  `CServerDllState` members.

**What is left of T6, measured at the lane's tip** (MEMBER rows by owner,
`docs/globals-ledger.tsv`):

| step | owner (tier) | rows | bytes |
|---|---|--:|--:|
| 4 | EngineBinding (ROOT): `engine`, `gpGlobals` (duplicates of m_pEngine/m_pGlobals), `g_pCVar`, `mdlcache`, `s_factories`, the 3 by-value interface objects + shared app systems, `g_pSharedChangeInfo` (needs the fork's edict.h `#ifndef SOURCESHARP`) | 21 | 209 |
| 4 | Diagnostics (DLL 27, ROOT 3, LEVEL 1; movement counters 2) | 33 | ~13,750 |
| 4 | Console (DLL): `g_LineBuffer` (1 MB) and convar_port's registration state | 11 | 1,049,035 |
| 4 | Overlaps (LEVEL) | 5 | 77 |
| 4 | World (DLL 3, LEVEL 2) | 5 | 542 |
| 4 | NetTables (DLL 7: the tier tables, world table, payload names; LEVEL 1) and StringTables (LEVEL 7) | 15 | 2,834 |
| 4 | GameRules (LEVEL), GameEvents (DLL 2), StringPool (LEVEL), Precache (DLL) | 5 | 129 |
| 4 | Physics (DLL 5, LEVEL 2: `physenv`, `g_PhysWorldObject`) | 7 | 184 |
| 4 | Players (DLL 4 + players/player-shadow subsystems 4) | 8 | 16,868 |
| 4 | the rest: EntityFactories 2, EntityShells 3, ManagedBridge (DLL 4, ROOT 2: g_ShimFrame/g_ShimLevel), EntitySlots' world serial 1, TempEnts' s_EffectServer 1 | 13 | 925 |
| 5 | static initialisers: 85 of 98 objects (tools/globals-census summary) | | |
| 6 | C callbacks with no user-data argument: the spew hook (shell_diagnostics.cpp SpewOutputFunc), the assert notify (SetAssertFailedNotifyFunc), the global ConVar change callback (sourcesharp_gamesystem.cpp); the signal/SDL watch is FLOOR already (g_ExitWatch) | 3 sites | |

The free `SourceSharp_*` entry points of the moved owners still reach the DLL
tier through the root's accessor (`ShellGameDll_Dll()`); routing them through
`this` on their IShim area (plan_globals §2 route 2) is the remaining half of
each owner's move.

Exit: `link_level` = the FLOOR rows D6 allows (target: 1 object plus the
callback accessor and whatever of (b)/(c) the ruling keeps), 0 UNRULED, 0
MEMBER, 0 DEAD; `objects_with_a_static_initialiser` at the floor; the daily
gate green on the new static-init order; plan_globals.md's banner replaced
by a pointer here.

### T6 part 3: injection, not lookup (user 2026-10-27)

**The goal, restated by the user (2026-10-27):** "remove free standing functions
and use classes with deps injected via the constructor owned by the game dll
instance". Moving a global onto a tier is half of it. The state must also be
reached through `this`: a subsystem is a class whose constructor takes the
engine interfaces and tier objects it uses and stores them, owned by the game
DLL instance (directly, or by its DLL or level tier), and every call that
reaches it (managed through its `IShim<Area>`, or the engine through an
interface the root serves) arrives on that object.

**Where part 2 left it (2026-10-27, lane tip bf13e21f2).** Part 2's 18
commits took `globals_link_level` from 168 to 118 by making the state tier
members, but the code reads them through free accessors that find the root
through its one static. That is a service locator, and the globals ratchet
cannot see it. Measured by `grep -rhoE` over `src/server/native` and
`src/shared/native` (45 files):

| accessor | call sites |
|---|--:|
| `ServerEngine()` | 52 |
| `ShellGameDll_Dll()` | 47 |
| `ServerModelInfo()` | 17 |
| `ServerMdlCache()`, `ServerFileSystem()` | 12 each |
| `ShellGameDll_Managed()`, `ShellGameDll_Level()`, `ServerPartition()` | 9 each |
| `ServerOverlay()` 7, `ServerTrace()` 6, `ServerStaticProps()` 5, `ServerStringTables()` 4, `ServerRandom()` 4, `ShellGameDll_ShimRoot()` 3, `ServerGameStats()` 3, `ServerEvents()` 3 | 45 |
| **total** | **211** |

`tools/native-symbols` (debug, same tip): server.so defines 654 free
functions from `src/server/native` and `src/shared/native` (no `::` in the
name), 273 of them `SourceSharp_*`.

**The shape.**

1. **A shim area is implemented by its subsystem's class.** Today
   `tools/shim-areas` generates an empty `CShim<Area>` per area whose methods
   call the free `SourceSharp_*` body the row names, and `CShimAreas` (a
   member of `CShimRootServer`) holds them all. Instead, a row's `body`
   column may name a class (`CServerUserMessages`); the generator then emits
   no forwarder for the area, and `ShimAreas_Acquire` hands out the tier's
   instance of that class. The class implements `IShim<Area>`, takes what it
   uses by constructor, and its free bodies and their `sourcesharp_entrypoints.h`
   declarations go in the same commit. Areas convert one at a time;
   `CShimAreas` shrinks as they do and goes with the last.
2. **Lifetime.** Managed acquires the areas once, at attach in DLLInit, and
   keeps the pointers across maps. So the object behind an area is
   DLL-tier (or a root member). An area whose state is per map holds the
   level tier by pointer, which the root sets in LevelInit and clears in
   LevelShutdown: an injection the owner performs, visible at one site, not a
   lookup.
3. **Interfaces the engine calls** (`IServerGameClients`, `IServerGameEnts`,
   the tags and shared-app-systems objects: `s_ServerGameClients` and the
   other MEMBER rows) become members of the root, built with their
   dependencies, and served by the root's `CreateInterface` answer.
4. **Helpers stay free only when they are pure**: a function of its
   arguments that reads no state (string and math helpers, `ss::Format`).
   A helper that needs the engine becomes a method of the class that has it.
5. **The accessors retire as their callers go.** `ServerEngine()` and the
   other `Server*()` functions in `server_engine.h`, then
   `ShellGameDll_Dll()`/`ShellGameDll_Level()`/`ShellGameDll_Managed()`,
   delete once nothing calls them.

**Two cases with no object to call into, and their answers:**

- **`CBaseEdict::StateChanged`** (vendor `edict.h`, inline) has no engine
  caller: the engine compiles its own copy. In server.so its only callers
  are `CStandaloneEntity::NetworkStateChanged` (the argument-less overload,
  which reads no state) and `CStandaloneEntity::MarkBlockChanged` (the offset
  overload, which reads `SharedEdictChangeInfo()` and calls
  `CBaseEdict::GetChangeAccessor`, both defined in our
  `shell_engineboundary.cpp` through `ServerEngine()`). So the offset
  overload's bookkeeping moves to a method of a class that holds the engine
  and the shared change list by constructor, and the vendor inline and
  `GetChangeAccessor`'s definition go: both are non-virtual, outside any
  layout the engine reads. `SharedEdictChangeInfo()` then deletes.
- **C callbacks with no user-data argument** (D6(a)): the spew hook
  (`SpewOutputFunc_t (SpewType_t, const char *)`), the assert notify
  (`AssertFailedNotifyFunc_t (const char *, int, const char *)`) and the
  global ConVar change callback (`FnChangeCallback_t (IConVar *, const char *,
  float)`). The user asked (2026-10-27) for a generated code block that binds
  each to its class instance, in place of D6(a)'s `ShellSide()` accessor.
  It is feasible: a `CBoundCallback<Sig, &Class::Method>` owned by the
  instance writes a small thunk into a page it allocates read-write, then
  flips the page to read-execute (never both; `mmap`/`mprotect` on Linux,
  `VirtualAlloc`/`VirtualProtect`/`FlushInstructionCache` on Windows). The
  thunk inserts the instance as the first argument and tail-jumps to the
  member's static trampoline. All three signatures have at most three
  arguments, so the shift fits in registers on both ABIs: System V moves
  `rdx->rcx, rsi->rdx, rdi->rsi` and loads `rdi`; floats stay in `xmm0`.
  Win64 moves `r8->r9, rdx->r8, rcx->rdx` and loads `rcx`; a float is
  positional there, so the change callback's float moves `xmm2->xmm3`.
  The template accepts only signatures it has a shift for
  (`static_assert`). Ordering: the owner uninstalls the hook before the
  thunk frees, as the current code restores the previous spew function.
  The CLR already JITs in this process, so executable pages are allowed.
  Proof: gtests call each thunk through its plain function pointer and check
  the instance and every argument (the float among them) arrive; the Windows
  arm builds under `tools/windows-cross` and runs in CI.
  ConVar callbacks on our own ConVars need no thunk: `FnChangeCallback_t`
  receives the `IConVar *`, and our ConVar subclass can carry its owner.

**The ratchet.** A new row in `docs/globals-ratchet.tsv` (or a sibling
check): call sites of the root accessors and the `Server*()` accessors,
counted as code relocations against them over the server's link-edge objects
(`tools/globals-census summary`'s `locator_calls`; `globals_locator_calls` in
`docs/globals-ratchet.tsv`, held by `globals_ledger_test`). 181 at its first
measure (fe875afa0 plus the tool, debug; `objdump -dr` agreed; the 211 above
is a source grep that also counts declarations and comments). It only falls.
Widened the same day to the 16 managed-area accessors (`ManagedCommands()` and the
rest, native finding the managed object it calls): 251 at e0334fd36. A free-function row counts server.so's free
functions that read state, with the pure helpers ruled by name.

**Order.** The smallest areas first, to set the pattern and the generator
change: user messages (`shell_usermessage_send.cpp`) and recipient filters,
then the client steps, sentences, precache, temp entities, then the large
ones (standalone entities, players, game clients, physics). The engine
interfaces the root serves follow. The thunks come when Diagnostics and
Console convert.

**Exit:** no free function in server.so reads state except the bound-callback
trampolines; the accessor ratchet at 0; `link_level` at the D6 floor.

## 4. End state (measured at the T5 close, 2026-10-19)

Measured by T5 on its branch at its last code commit (the lane's tip; base
91296dc75, which is in the archived lineage `archive/pre-public-main-9` since
main was rebased onto GitHub as c56e578d8 with the same tree). The 42b718990
column is §1's, measured when this plan was written; the 91296dc75 column was
measured by T5 in this checkout (release built at 91296dc75, then the tip
rebuilt over it). Commands: `size -A` and `readelf -SW` on
`farm/<config>/build/out/server.so`, `stat` on the deployed
`bin/release/bin/linux64/server.so`, `tools/cmakegraph.py objects|sources
server`, `tools/server-native-text`, `tools/server-native-scope`,
`tools/shim-scope`, the `shim_reason_letters_test` walk (ceiling set to 1),
`make globals-census`, `tools/client-novalve-census`.

| instrument | 42b718990 | 91296dc75 (T5 base) | T5 tip |
|---|--:|--:|--:|
| debug `server.so` `.text` (B) | 939,083 | 359,607 | 359,607 |
| release `server.so` `.text` (B) | 403,711 | 150,612 | **150,612** |
| release `server.so` deployed (stripped, B) | 601,568 | 391,984 | 391,984 |
| objects on the link edge, debug / release | 123 / 99 | 99 / 81 | 98 / 80 |
| release object `.text` before `--gc-sections` (server-native-text) | n/m | 270,949 | **249,799** |
| release `.cpp` lines on the list, raw / code | 58,241 (debug list) / 23,976 | 36,320 / 14,510 | 35,307 / 13,859 |
| not-native lines (server-native-scope: MOVE+DELETE+FOLD+DEBUG, joined) | 8,083 (first ratchet, 5a1941ff3) | 4,935 | **4,818** |
| shim lines (the walk, .cpp and .h) | 145,428 | 129,214 | 128,502 |
| shim-reasons table (.cpp walk), files / lines | 222 / 100,278 | 216 / 87,100 | 217 / 86,493 |
| server statics, link level (globals ledger rows) | 359 | 237 | **225** |
| client native logic lines (R7, shim-scope) | 8,257 | 7,515 | 7,515 |
| exports / Valve archives linked | `CreateInterface` / none | same | same |

**What T5 itself changed, and what it did not.** The linked `.text` of both
builds is the size it was at 91296dc75 (359,607 debug, 150,612 release;
byte-identical after T5 1, by sha256 of the section; later moves only
re-placed functions within it):
everything T5 deleted had already been discarded by `--gc-sections`, which is
what "no importer" means. T5 removed the source and the compiled objects'
copies (release object `.text` -21,150 B; mathlib_port 16,575 -> 2,056),
one TU from the server link (tier0_thread_port.cpp), 117 not-native lines,
712 shim lines, 12 static-state rows, and the tests whose only subject was
the deleted code (user ruling 2026-10-19).

**The substrate, re-measured** (nm U over the link edges at the tip):
`mathlib_port.cpp` keeps 9 symbols (7 functions, `vec3_origin`, `vec3_angle`)
imported by 13 server objects (was 13 symbols by 19); its other 35 members
are `src/client/native/mathlib_port_client.cpp`'s and 11 went. ConVarRef is
imported by 9 server objects (was 15); all 76 of `convar_port.cpp`'s
definitions are in the linked server.so (the ConVar/ConCommand vtables the
engine calls reach the ones no object names). `gamestringpool.cpp`: all 4
live. `tier0_spew_port.cpp` keeps the 13 server.so imports plus
`Tier0_DevSpewV`, the seam the client file's two printers use;
`tier1_port.cpp` keeps `bf_read::SetOverflowFlag` alone;
`tier0_thread_port.cpp` is client-only.

**What native stays, and why** (release, the tip's 80 TUs, 11,674 code lines
in 938 functions, joined through the ledger by `tools/server-native-scope`'s
own Joiner):

| category | code lines | functions | what |
|---|--:|--:|---|
| engine-called vtables (KEEP-THUNK) | 1,766 | 190 | IServerGameDLL, IServerGameClients, IServerGameEnts, IServerGameTags, the shared app systems, CStandaloneEntity's ICollideable / IServerEntity / IServerNetworkable, CShellGameMovement, the ConVar and ConCommand carriers, the string-table forwards |
| layouts the engine reads (KEEP-LAYOUT) | 565 | 47 | SendProp / SendTable / ServerClass (dt_send.cpp, shell_tempents_store.cpp storing the managed plan), edict / PVSInfo, CPlayerState, `sourcesharp.h`'s structs |
| registration (KEEP-REG) | 885 | 63 | `CreateInterface` and `kExposed[]`, the factory tables, ConVar_Register, CPhysicsHook, DLLInit |
| host, substrate and engine-held pointers (KEEP-HOST) | 1,922 | 124 | the CoreCLR host (managed_runtime_server, shell_internalfactory, exit_watch, fatal_report), the ports above, tier_connect, engine_heap, loaded_symbol, and the engine interface pointers (38 `engine-iface-ptr` statics, most in shell_engineboundary.cpp) |
| marshalling-only outbound (KEEP-OUT) | 2,087 | 224 | the entity API's getters, precache, string tables, the recipient filters' list logic |
| logic still native (MOVE / FOLD / OOS) | 2,742 / 164 / 232 | 160 / 19 / 20 | MOVE's largest: shell_entityapi (288), shell_globals (260), shell_standalone_base (182), shell_te_forwards (175), overlay_library (148), dt_send (148), shell_worldquery (141), shell_standalone_hierarchy (138), shell_movement (126), takedamageinfo_library (125); OOS is physics.cpp's hook and held globals |
| debug instruments (DEBUG) | 1,288 in release TUs; +624 in the 18 debug-only TUs | 88; +33 | diagnostic arms and console commands (ruling 5); release compiles no debug-only TU |
| static-state FLOOR rows (D6) | | 8 | `s_gameShellDll`, `g_pServerClassHead`, `s_pNonModifiedPointerProxyHead`, `g_ExitWatch` (the kernel's and SDL's callbacks), console_display_forward's `g_Forwarder` and `g_pInstalledCvar`, alloc_check's `g_State` and `t_depth` (debug) |

The globals ledger at the tip: 225 link-level rows (UNRULED 123, MEMBER 51,
CONST 30, MANAGED 9, FLOOR 8, DEAD 4), 1,411,344 B at object level (the TE
pool and the entity slot blocks are most of it), 85 objects with a static
initialiser (measured; the ratchet row reads 104). T6 is the track that takes
these to the D6 floor.

Against §4's 2026-10-18 estimate ("release `.text` from 403,711 B to an
estimated 150-200 KB"): 150,612 B, at the low end, with 2,742 MOVE lines still
native. What is left of the binding (THUNK + LAYOUT + REG + HOST, 5,138 lines)
is above the estimate's ~3,600 because the joined ledger now counts the
hand-spelled convar and tier rows it then estimated separately.

## 5. Decisions for the user

The six rulings of 2026-10-11 (plugins dropped; managed ITraceFilter and
IRecipientFilter; managed save/restore codec; GameMovement001 export cut;
driverless debug commands cut; P3 order a,b,c | d,e | f | g) stand.

**RULED 2026-10-18 (user), at the landing:** after the rebase onto
plan_forwarders_everywhere (#10, #11), the shim areas are append-only
forwarder tables, and this branch's slot deletions may regenerate them with
`tools/engine-forwarders --write --reorder` once (both sides rebuild
together), rather than keep retired placeholders. The user also asked that
all of this branch's work be squashed and landed on main once main is
updated.

**RULED 2026-10-19 (user):** "Remove tests that test unused code." When T5
deletes code with no caller in either DLL (shown by `--print-gc-sections` in
both release and debug, and nm over both link lines), every test whose
subject is only that code goes with it, in the same commit, which names
them. Test hooks that drive live code (ExitWatch_*ForTest) stay; a test file
that also covers live code loses only the dead subject's facts; code that is
server-dead but client-live moves to a client-only file and keeps its tests.

**RULED 2026-10-19 (user):** D6 "keep it as is": the three shapes (C
callbacks with no user-data argument, the ConVar/ConCommand objects the
engine holds, the registration list heads static initialisers build) stay
as the FLOOR rows they are; T6 hangs everything else off the one game-DLL
object. And "expose file handles to managed code": the managed file-system
wrapper may open, size, read and close through IBaseFileSystem's
forwarders, which lifts EngineBaseFileSystem's "no handles" rule, so the
game-file read slots fold (T3).

**THE --reorder, NAMED (2026-10-18, follow-up to the landing at bd894daf1).** The
landed commits' messages do not say it, so it is said here. Two of the four
landed commits shrank forwarder tables with `tools/engine-forwarders --write
--reorder` (the user's ruling above); 393cb1bdb and 54621d40c touch no table.
Found by comparing each commit's `*Forwarders.g.cs` field lists with its
parent's.

- **d9e567c0b (T2):** ShimDamage (RadiusDamage, ExplosionDamage,
  EntitySetTakeDamageMode, EntityIgnite, EntityDissolve); ShimEffectsServer
  (BloodDecal, ShootGib, FireEffectCreate, FireEffectScale, FireEffectEnable);
  ShimEntitiesServer (FindBaseEntityByName, EntityDetectInSkybox,
  EntitiesSdkPlayerSkyboxArea); ShimEntityLifecycle (TemplateCapture,
  EntitiesPurgeMapRefs, EntitiesCleanupDeleteList, RegisterWeaponEntity);
  ShimMovementServer (PlayerForceButtons, PlayerGetButtons,
  PlayerSetButtonsDisabled, PlayerSetLaggedMovement, PlayerSetAllowAutoMovement,
  PlayerSetMovementConstraint, PlayerSetFrozen); ShimPhysics
  (EntitySetForceServerRagdoll); ShimPlayers (AddPlayerScore, PlayerSetFov,
  PlayerSetSoundscape, ClientsSdkPlayerEarPosition); ShimTransform
  (EntityMoveToPosition, EntityMoveToAngles, EntityStopMoving). Whole tables
  gone: ShimInventory, ShimWeapons, ShimWeaponsServer. EntitySetSkin and
  RegisterAmmoType were appended to ShimStandaloneClient (no reorder there).
- **bd894daf1 (T3 part):** ShimConsoleServer (ExecuteServerCommand);
  ShimEventsServer (EventCreate, EventFire, EventFree); ShimPlayers
  (GetPlayerUserId, PlayerSetViewEntity, PlayerBoxInPvs); ShimWorldServer
  (EngineLightStyle). Whole tables gone: ShimStringTables,
  ShimStringTablesServer. The engine rows (GameEventManager, the string-table
  container and table, EngineServer) only appended.

**RULED 2026-10-18 (user):** D1 "can alias the two" (one class under both
names: `class CBaseEntity` with `using IGameEntity = CBaseEntity`, as T1.2
says); D2 yes (option a, the one-header compat overlay); D3 yes (the
pristine tree becomes the server's include root at canary 0); D4 yes
(retire the ModelDescriptor slot; the studio chain leaves the server link);
D5 yes (T1 before the lanes). The user also asked for native globals and
statics to go "except for the one instance of game dll": that is track T6,
and its floor is D6.

6. **D6 (T6): the static-state floor.** The one static object is
   `s_gameShellDll` (`shell_gamedll.cpp`, 2,032 bytes, the address
   `EXPOSE_SINGLE_INTERFACE_GLOBALVAR` takes at static init). Three shapes
   cannot hang off it without a global and need a ruling each: (a) C
   callbacks with no user-data argument that the engine or the kernel calls
   (the spew hook, the signal handler in `exit_watch.cpp`, `FnChangeCallback_t`
   on ConVars): plan_globals.md §2.3's answer is one accessor, `ShellSide()`,
   defined beside the root, with a gtest pinning its callers [recommended];
   (b) `ConVar`/`ConCommand` objects the engine's ICvar holds pointers to (7
   rows today): heap members of the DLL tier registered in `DLLInit`, or
   left static; (c) the registration list heads static initialisers build
   before `DLLInit` (`g_pServerClassHead`, `s_pNonModifiedPointerProxyHead`,
   the 40 `SS_NATIVE_COMMAND` list nodes): rebuilt in `DLLInit` from
   constant tables [recommended], or left as the FLOOR rows they are.

New:

1. **D1 (T1.2): the server's entity face is named `CBaseEntity`** (with
   `IGameEntity` as a per-side alias), so stock headers bind to it. The
   alternative is to keep the patch forever: there is no third way, because
   the stock headers name the type.
2. **D2 (T1.5): the clang-only `utlblockmemory.h` error**: (a) a one-header
   compile-only overlay in `cmake/compat/clang/` [recommended], (b) gcc for
   server.so on the host, (c) leave server.so on the patched header and
   accept "stock except one clang fix".
3. **D3 (T1.6): after the canary reaches 0, point the server's include set
   at the pristine tree** (`VALVE_SDK`), so the normal build is the proof;
   or keep the farm's patched headers and rely on the canary alone.
4. **D4 (T4): retire `IShimModels.ModelDescriptor`** by moving
   `ModelDescriptors` next to the managed StudioHdr, so `studio.cpp`,
   `model_descriptor.cpp` and `studio_mdlcache.cpp` leave the server link
   (48 KB release `.text`); or keep the chain as KEEP-OUT.
5. **D5: order.** T1 before T2-T4 [recommended: it respells one type across
   48 TUs and would conflict with every lane], or T1 as its own lane beside
   them with the conflicts resolved at each rebase.

**RULED 2026-10-27 (user):** T6's goal is free-standing functions removed in
favour of classes with their dependencies injected by constructor, owned by
the game DLL instance; moving a global onto a tier and reading it through an
accessor is not the end state (§3 T6 part 3). The user also asked whether the
spew hook and ConVar callbacks can be bound to their instance by a generated
code block; part 3 says how, in place of D6(a)'s `ShellSide()` accessor.
The user approved part 3 as written (2026-10-27), thunks included.

**RULED 2026-10-27 (user), on the rows left after the diagnostics hooks:** "Move
logging to log class, remove g_pCVar and have a CVar registry class, sure keep
get verbosity, do it. Can have a per class string pool instance instead of
global." So: the logging state (verbosity cache, the ready flag, the
not-shipped ledger and the trace/stub/entity-log bodies) moves into a log
class, and `SourceSharp_GetVerbosity()` stays the free read the trace macros
use; `g_pCVar` and convar_port's registration state become a CVar registry
class; pooled strings live in a pool instance owned by the class that pools
them, not one global pool.

**T5 CLOSED, 2026-10-19.** Substrate collapse done as §3 T5 says, under the
user's rulings of 2026-10-12 (server-dead code the client uses moves to
src/client/native) and 2026-10-19 ("remove tests that test unused code":
each deletion took the tests whose only subject it was, named in its
commit). Not done, each needing a decision or a lane of its own (docs/todo.md):
the header-inline relays of anyentity.h and physicsowner.h, dt_send.cpp's
dead SendTable and non-modified-proxy constructors (the latter builds a D6
FLOOR list head), managed-table slots left with no native caller
(IManagedHierarchy.HierarchyResolveParentNames, IManagedSoundEnt's insert;
retiring a row is a --reorder), shell_movement.cpp's server-dead arms, and
three client-only imports kept in shared TUs as seams over file-local
helpers (Tier0_DevSpewV, SourceSharp_LogStub) or text-pinned
(UTIL_Tracer).

## 6. Risks

- The `CBaseEntity` name is load-bearing in tests: 99 gtests under
  `src/test/native` mention it (`cpp_alias_retype_*`,
  `entity_bases_alias_*`, `sdk_player_cast_test`, the 20 that read the dead
  headers' text), many asserting its *absence* on the server. T1.2 audits
  each before the rename (a test that pins the old spelling is rewritten to
  pin the new invariant: one definition, ours).
- `SS_CBASEENTITY_ALIAS` is a server compile definition from the P5 era:
  431 test lines and ~30 ledgered comments name it, but only two
  preprocessor sites still read it, one client header and one test fixture
  (`grep -E '^\s*#\s*(if|ifdef|ifndef|elif).*SS_CBASEENTITY_ALIAS'`). It is
  not half of this change; it goes with T1.2 once the two sites are
  re-pointed.
- Two include roots (farm for the client, pristine for the server) is a
  setup-tool change; `tools/setup`'s refusal of a path supplied by both
  repos must extend to the third tree.
- Everything the old plan's §6 listed (callback cost, text-reading tests,
  static-init order, hot reload, coordination with the physics lane: the
  last is closed, the physics plan is DONE).

## 7. Recipes

A-G as in `plan_server_native_min.md` §8. New:

- **Recipe H, the stock-header canary.** For each object on the link edge
  (`cmakegraph.py objects server`), take its `compile_commands.json`
  command, replace the three farm `public` include dirs with
  `$VALVE_SDK/src/public{,/tier0,/tier1}` (plus the one excused header's
  dir under D2), drop `-o`/`-MD`/`-c`, add `-fsyntax-only`, run; count the
  failing TUs and the distinct error texts; compare with
  `docs/server-native-ratchet.tsv`'s column (may only fall). A missing clone
  or a clone whose HEAD is not `valve-sdk.lock` is a failure. Self-test:
  with one `GetGameEntity` override put back in a scratch TU the count
  rises by one.
- **Recipe I, the rename.** (1) the audit of tests that name `CBaseEntity`;
  (2) `game_entity.h` with `class CBaseEntity` + the alias, overlay
  `igameentity.h` deleted in the same commit, `tools/setup` re-run; (3)
  `GetGameEntity` → `GetBaseEntity` file by file with `make debug` between;
  (4) the canary count after each step in the commit message.
