# Server native code: down to the minimum the managed code needs

Uncommitted draft (repo rule: plans wait for user review). Base: main
baaebbe6a (2026-10-11). Physics and bones are OUT OF SCOPE: another plan owns
them (plan_physics_bones_native_zero.md); their rows here are `OOS` and were
not ruled.

Goal (user, 2026-10-11): audit server.so's native code and reduce it to the
absolute minimum required to make the managed code work. "Minimum" is
CLAUDE.md's rule: native code may carry out a decision; it must never make
one. What stays is the engine binding -- a vtable the engine calls, a layout
it reads, a registration it needs at load, the CoreCLR host, and a
marshalling-only outbound call where managed cannot reach the engine
directly. Everything else moves to C# or is deleted.

## 0. How to execute this plan

Read this section first; it is written for an agent that has not seen the
audit. CLAUDE.md's rules apply throughout and are not repeated here except
where this plan narrows them.

**The work queue is the ledger.** `docs/plans/server_native_audit.tsv` has
one row per function (columns: cluster, file, start, end, code, symbol,
verdict, destination, reason). Each phase below names the verdicts it
consumes; pull its rows with awk and work them file by file, biggest file
first:

```
# all DELETE rows of one file, with the evidence column
awk -F'\t' '$2=="src/server/native/shell_entityapi.cpp" && $7=="DELETE"' docs/plans/server_native_audit.tsv | cut -f3,6,9
# every file's MOVE total, descending
awk -F'\t' '$7=="MOVE"{m[$2]+=$5} END{for(f in m) print m[f]"\t"f}' docs/plans/server_native_audit.tsv | sort -rn
```

Line numbers in the ledger are true at baaebbe6a and drift as files shrink;
address a row by file + symbol, never by line.

**Before touching a file, re-measure its row.** A ledger row is a claim made
at baaebbe6a. A DELETE row is executed only after Recipe A below reproduces
its evidence at the current tip; a MOVE row only after reading the function
at the current tip. If the evidence no longer holds, change the row (the
ratchet from P0 lets a row's verdict change in the same commit as the code)
and say so in the commit message.

**One commit per unit.** A unit is: one evicted TU, one deleted body (or a
group of bodies that share one proof), one folded slot, one managed-built
interface, one moved logic block with its facts. The commit message names
the ledger rows it closes (`ledger: shell_playerslots.cpp DELETE x7`) and
carries the proof command and its output for a deletion. Never `git add -A`;
stage the paths named in the unit.

**Where to work.** P0 and P1 happen on one branch cut from main
(`server-min-p1`), rebased and fast-forwarded when green. P2 likewise
(`server-min-p2`). P3 is one worktree per lane (`server-min-p3a` ... `p3g`),
cut from main at the P2 merge, each owning only the files its block names;
the ABI tail (`docs/engineapi-areas.tsv`, `docs/managed-interfaces.tsv`,
`tools/vtable-layout`'s chain rows, the generated files) is serialised:
one lane edits it at a time, the others rebase. A fresh worktree needs
`make setup ROOT=../source-sdk-2013`.

**Per commit.** `make debug` (zero warnings), the touched tests
(`make managed-test FILTER='FullyQualifiedName~X'`, `farm/debug/src/buildcpptests NAME`,
`make cpp-test`), `make debug GC_SECTIONS=0` after any TU eviction, and
`git -C ../source-sdk-2013 status --porcelain` empty. **Per phase.**
`make check`, then the daily gate's next report (`tools/daily-gate --status`)
before the next phase builds on the new link order. The live gate by hand
only when the user asks.

**Stop points** (ask the user, do not guess): a measured gate that blows its
budget (P2.4, P3d, P3f); a change that needs a fork edit (`../source-sdk-2013`
is never written); a ledger row whose verdict you would overturn from
KEEP-* to DELETE on new evidence (say what you found).

The recipes in the appendix (§8) are the mechanical steps each unit follows.

## 1. Inventory at baaebbe6a

Instruments (all run this session, read-only; the fork was clean after):
`tools/physics-scope/fnmap` over every TU in `src/server/native/CMakeLists.txt`
(130 TUs; 119 in scope after the 11 physics/bones TUs), `nm -C` over the 130
objects under `farm/debug/build/.../server_sourcesharp.dir` and over
`bin/debug/bin/linux64/server.so`, `ninja -t deps` for the headers, a caller
census of the 337 server rows of `docs/engineapi-areas.tsv` against every
non-test `.cs` (every slot has a managed caller; no free deletion at the slot
level), and `src/abi/VtableLayouts.g.cs` for the engine interfaces managed
already drives directly.

| what | number |
|---|--:|
| in-scope TUs | 119 (55,104 raw / 23,351 code / 25,082 comment lines) |
| functions ruled (ledger rows, every fnmap row once) | 1,425 (20,704 code lines) |
| headers reached by server objects | 176: 80 server-only (13,745 lines), shared 22,095, host+engineabi 2,673 |
| debug-only TUs (`server_sourcesharp_debug_sources`) | 24, 1,219 code lines; release compiles none |
| debug server.so `.text` | 1,324,782 bytes (release binary is stale: 2026-10-03) |

The ledger is `docs/plans/server_native_audit.tsv` (1,654 rows: 1,425
functions + headers and inline sub-rows), joined on file+start against every
fnmap ledger with zero gaps and zero duplicates.

## 2. Verdicts, and the totals

| verdict | means | .cpp code lines | header lines (approx) |
|---|---|--:|--:|
| KEEP-THUNK | vtable the engine calls; body forwards to managed or returns a field | 2,436 | 890 |
| KEEP-LAYOUT | struct the engine reads at fixed offsets | 518 | 1,224 |
| KEEP-REG | load-time / static-init registration the engine needs | 817 | 354 |
| KEEP-HOST | CoreCLR host, dispatch, and the tier substrate server.so carries because it links no tier libs | 1,840 | 1,199 |
| KEEP-OUT | outbound engine call that only marshals, on an interface managed has no layout for yet | 2,788 | 820 |
| FOLD | outbound call on an interface managed already has a layout for: managed calls the vtable; body and slot go | 538 | 170 |
| MOVE | logic: decides, computes, parses, iterates, keeps state | 5,599 | 3,312 |
| DELETE | dead: no caller, no engine entry (nm + grep evidence per row) | 2,199 | 585 |
| DEBUG | debug-build-only diagnostics: delete, or move to the managed debug API | 2,320 | 598 |
| OOS | physics / bones | 1,649 | 83 |
| **total** | | **20,704** | **9,235** |

Reading: of the 19,055 in-scope function code lines, 5,611 are binding
(thunk+layout+reg+host), 2,788 are marshalling that may stay, and 10,656
(MOVE+DELETE+FOLD+DEBUG) are not binding. The end state the ledger implies is
about 7,000 code lines of native server code in release (5,611 binding plus
whatever of KEEP-OUT survives P2/P3, estimated under 1,500), down from
19,055: a 63% cut, before the substrate (tier ports, string pool, ConVarRef:
most of KEEP-HOST's 1,840) shrinks with the readers that need it. The
2,320 DEBUG lines are already out of release and go to the debug API or
away.

Header lines are approximate (a comment-stripped read); function lines are
exact. shell_savegame.h's 560 double-counts its two inline sub-rows (250).

**KEEP-THUNK is not the floor.** A thunk whose body only forwards to managed
can be the vtable itself: `tools/vtable-layout` already builds Valve-layout
vtables in managed from `[UnmanagedCallersOnly]` addresses (`ValveLayout =
true`: IPhysicsObjectEvent, IMotionEvent, the B2.1/B2.2 pattern). Applied to
the engine-called interfaces whose methods are pure forwards (IServerGameDLL:
30 of 55; IServerGameClients; IServerGameEnts; CShimLevel/IShimFrame's
forwards), the native class goes and what stays is the registration row plus
an object whose vtable pointer is installed once CoreCLR is up (P2.4). The
2,436 KEEP-THUNK lines were ruled before this was applied; P2.4 re-rules them
and the end-state estimate falls below 7,000 by whatever it takes.

## 3. Per-file verdicts

Whole files that can leave the server link (every row DELETE, or DELETE after
one MOVE; each gated by the P1 proof):

| file | code | why |
|---|--:|---|
| src/shared/native/collisionproperty.cpp | 131 | the dirty list has no writer (`AddEntity` deleted, `PushItem` re-queues its own drained nodes); the exclusive branch is unreachable; its one server consumer is a `kServerSystems[]` row. The "STAYS" it cites is the client's. |
| src/server/native/shell_restoreentities.cpp | 42 | `BeginRestoreEntities` has no definition in src/ (server.so: 0), so `EndRestoreEntities` returns on its first line forever; `Game_SetOneWayTransition` sets a flag nobody reads. Two call sites to drop. Closes an `x` row in shim-reasons.md. |
| src/server/native/shell_playerslots.cpp (+ player_name_match.h) | 111 | every function's sole referrer is shell_standalone_player_debug.o; managed has PlayerNames and PlayerPing.Sample (same formula). |
| src/server/native/shell_playerlocaldata.cpp (DT_Local + 5 datadescs) | 141 | `ss::DT_Local::g_SendTable` and the `m_DataMap`s have no importer in any server object; no ServerClass embeds it. The 13-line ctor stays with the struct. |
| src/server/native/sendproxy.cpp, src/shared/native/dt_utlvector_common.cpp | 83 | DT_Local was their only user (`SendPropArray3/Bool/EHandle`, `SendProxy_EHandleToInt`, `DT_ArrayElementNameForIdx` + its 2048-string table). |
| src/server/native/shell_gamedll_convars.cpp | 4 | `g_pDeveloper`: 0 `U` references over all 130 objects, 0 in server.so; gamedll_convars_test.cpp pins readers (physics.o, shell_soundscape.o) that no longer exist. |
| src/shared/native/shell_shared.cpp (server arm) | 74 | every server-compiled body is a CLIENT_DLL arm, a per-call `Warning()` refusal (RegisterAmmoType, which AmmoTypes.Register still reaches), or a write-only table. Only `g_pMoveData` stays, beside its one importer. |
| vstdlib_random_port.cpp, randoverride.cpp | 94 | DONE 2026-10-12 (ea's client batch 4, main 4335ea97a): moved to `src/client/native/` (user ruling: server-dead code goes client-only), dropped from the server's CMakeLists, ledger rows closed DELETE (moved). |
| src/server/native/baseentity_statics.cpp | 5 | `SendProxy_ClientSideAnimation` has 0 importers; `g_pForceAttachEdict` is written (shell_mapparse.cpp:125/155) and never read. |
| src/server/native/entity_bases.cpp | 104 raw | not in CMakeLists.txt, no .o, includes a cbase.h the farm lacks; only cpp_alias_retype_w1_test.cpp:579 reads its text. |
| src/server/native/shell_absent.cpp | 82 | 45 DELETE (CPostFrameNavigationHook toggles a bool nobody reads; RadiusDamage is a printed refusal with no managed caller) + 36 MOVE (tracer flags). |
| src/server/native/server_util_library.cpp | 191 | 129 DELETE: the CCheckClient subsystem writes two PVS bitsets nothing reads (`UTIL_FindClientInPVS` is gone; `GetNewCheckClient` `continue`s every candidate); the rest MOVE/FOLD and two UTIL_ClearTrace callers inline it. |
| src/server/native/shell_te_forwards.cpp | 369 | 354 MOVE: the 14 `TE_*` + CEffectsServer exist only for native callers and round-trip `SourceSharpTe_Forward -> IManagedTempEnts.OnNativeTempEntity` back into managed, which already builds every effect (EffectsOverTempEnts.cs). Leaves when its native callers call managed. |
| src/shared/native/shell_gameevents.cpp | 116 | 58 FOLD onto GameEventManagerVtbl; 40 KEEP-OUT only because IGameEvent has no layout (add one: the rest folds). |
| src/server/native/shell_stringtables.cpp (+ .h) | 98 | every slot FOLDs onto NetworkStringTableContainerVtbl / NetworkStringTableVtbl; the 7 stock table globals persist only for native readers. |
| src/server/native/shell_engineservices.cpp | 46 | 34 FOLD (BaseFileSystemVtbl) + 1 OOS. |
| src/shared/native/shell_acquire_reasons.cpp | 22 | 15 MOVE + a 7-line copy. |
| dead headers | 21+ | activator.h, entity_funcs.h (+entity_funcslot.h), scaled_collision_rule.h, notify_gate.h, push_blocker_rule.h, ent_fire_access.h (ported: DevCommandsLive.cs), physics_sweep.h (declares a function with no definition), scripted_var_defaults.h, said_once_per_id.h, igamerules.h (with the IGameRules retirement below), shell_movement_audit.h (orphan since its .cpp left 2026-10-01), gamesound.h (five declarations nothing defines). Zero includers in src/ including tests, except where a test reads the text. |

Files that are binding and stay, with their thunk/layout/registration lines
(what shrinks is their MOVE/DEBUG share):

| file | code | keep | not keep |
|---|--:|---|---|
| shell_gamedll.cpp (IServerGameDLL) | 487 | 453 thunk/host/reg; 30 of 55 methods are pure forwards or constants | 5 methods decide (LevelInit arm choice, PreClientUpdate gate, string-table order, GetSaveComment format, RegisterConCommandBase policy); 9 DEBUG |
| shell_standalone.h / shell_standalone_player.h (CStandaloneEntity, IMovementPlayer vtables) | 1,505 / 522 | KEEP-THUNK 484 (the engine's ICollideable / IServerEntity / IServerNetworkable) | 11 lines |
| shell_standalone_base.cpp | 927 | OUT 489, LAYOUT 89, THUNK 36 | MOVE 186 (KeyValueShellOnly + mirrors 49, SetModelName/SetVisible/UpdatePartition 74, …), DELETE 16, OOS 111 |
| shell_standalone_class.cpp, entity_factory_table.h, shell_factory.h, standalone_nettable.h | 246 + 147 | KEEP-REG (factory tables, send tables) | CStandaloneScriptedClass copies ppOutputNames per class and nothing reads them (outputs are managed since step 2a) |
| convar_port.cpp | 443 | genuinely engine-ABI: ICvar holds the carriers and calls SetValue/Dispatch/AutoCompleteSuggest; clamp/callback bodies are tier1's contract | ConVarRef (14 importing objects) is substrate that goes as native readers go |
| engine_block*.h, entity_slots.h, entity_op.h, saverestore.h, sourcesharp.h | ~1,224 | KEEP-LAYOUT; sourcesharp.h (2,440 lines) is pure ABI data: 43 structs, 20 enums, 0 inline functions | 4 enums (WeaponAnim, WeaponSound, FadeFlags, ShakeCommand) + 1 struct with no user |
| dt_send.cpp | 510 | LAYOUT 82 (SendProp/SendTable the engine walks), THUNK 75 (proxies the engine calls), REG 6 | MOVE 269: the SendProp* builders (proxy by size/UNSIGNED, bits defaults, COORD/NORMAL/NOSCALE rules, range multiplier, COLLAPSIBLE); DELETE 78 |
| shell_gameclients.cpp (IServerGameClients) | 303 | THUNK 166, REG 7 | MOVE 102 (ClientSetupVisibility's areaportal walk + bit packing 77; ClientActive 25), DELETE 28 (`#ifdef PORTAL`) |
| sourcesharp_gamesystem.cpp (+ generated shell_shim_areas.inc) | 763 | HOST 256, THUNK 219, OUT 133 | MOVE 73 (RegisterConVar/RegisterConCommand policy), DELETE 51 (14 no-op weapon slots) |
| shell_level.cpp (CShimLevel) | 179 | THUNK 137 | 13 of 24 methods are empty or retired; MOVE 30 |
| shell_playerinfo.cpp (IPlayerInfo/IPlayerInfoManager), plugin_check.cpp | 222 + 10 | nothing: no engine binary asks for `PlayerInfoManager002` (engine-boundary.md §6), only third-party plugins would, and plugins are dropped (user ruling 2026-10-11, decision 1) | whole files DELETE in P1 |
| shell_movement.cpp (IGameMovement) | 645 | THUNK 293, OUT 75 | MOVE 90 (FillConstants 36 = 46 words managed already declares; the point-contents A/B cache; the stock arms in ProcessMovement/RunManagedCommand), DELETE 76 (DiffPrint, GetPlayerMins/Maxs/ViewOffset: no caller in either DLL), DEBUG 62, FOLD 39 |
| exit_watch, fatal_report, loaded_symbol, ss_command_line, engine_heap, console_display_forward, managed_runtime_server, shell_internalfactory, shell_vtable_interfaces, tier0_spew_port, tier0_thread_port, tier1_port, tier_connect, gamestringpool | 1,500 | KEEP-HOST / REG | tier1_port: 31 DELETE (CRC32 table with 0 importers, HushAsserts U=0, _V_strcmp); shell_diagnostics.cpp: 274 of 484 are DEBUG |
| studio.cpp, model_descriptor.cpp, studio_mdlcache.cpp | 900 + 95 + 22 | KEEP-OUT (mdl readers, pose-param defaults) | 400 OOS (bones); only 24 of studio.cpp's 65 functions are referenced from another server object -- the `studiohdr_t::` twins of `CStudioHdr::` are DELETE candidates pending an intra-TU check |

The largest logic blocks, by destination (details per row in the ledger):

| block | lines | destination |
|---|--:|---|
| sound: CSoundEmitterSystem + EmitCloseCaption (sound_emitter.cpp) | 670 | new `SoundEmitter` over SoundEmitterSystemVtbl + EngineSoundVtbl + EngineServerVtbl.EmitAmbientSound; CloseCaptions.cs |
| sound: CSoundPatch + CShellSoundController (shell_soundenvelope.cpp) | 286 | SoundEnvelopeController.cs / SoundController.cs (handle table already managed) |
| sound: EntityEmitSound/StopSound/EmitSoundAtPosition (shell_sound.cpp) | 84 | SoundEmitter (SOUND_FROM_WORLD rules, PAS filter via EngineServerVtbl PVS calls) |
| tempents: tempent_schema.h planner + SourceSharp_TempEntRegister | 603 + 111 | src/shared/interop/TempEntRegistry.cs + new `TePropPlanner`: managed emits `SourceSharpTePropPlan_t` rows; native only constructs SendProp/SendTable/ServerClass (~75 lines) |
| tempents: TE_* forwarders (shell_te_forwards.cpp) | 354 | EffectsOverTempEnts.cs (already builds them); native callers call managed |
| datatables: SendProp* builders (dt_send.cpp) | 269 | new `SendPropPlan`: kind/offset/bits/flags/proxy-id/high-low; native stores into the SendProp it must own |
| save/restore: shell_savegame.h codec + shell_savegame.cpp drivers | ~790 | new `SaveFieldCodec`/`SavedEntityCodec`; three generic IManagedSaveRestore slots hand the engine's buffer/fields/base across; saverestore.h stays LAYOUT. **Decision 3** (contradicts managed-saverestore.md's "(f) stays native", which defends the byte layout, not an engine dependency) |
| entity I/O: ss::variant_t Set/Convert/ToString (shell_globals.cpp) | 230 | VariantValue.cs / EntityIo.cs; the one live path is DispatchInput's `Convert(FIELD_FLOAT)` + `ToString`; EHANDLE arms unreachable |
| player: command path (TickBase_Adjust, RunCommand prepare, FinishMove/pitch clamp) + SyncToBlob + IMoveHelper decisions (shell_standalone_player.cpp) | 280 | PlayerCommandQueue.cs + Movement.cs; PlayerNetState.cs / PlayerShadow.cs. Thunk left ~37 lines. Depends on movement state ownership (P3d) |
| player: PlayerSync_Write (standalone_player_sync.cpp) + ClientSetupVisibility | 101 + 77 | PlayerNetState.cs / new `ClientVisibility` on EngineServerVtbl (ResetPVS/AddOriginToPVS/SetAreaPortalStates/GetArea/GetAreaBits) once g_AreaPortals is managed |
| areaportals: areaportal_list.h state + rule, shell_areaportal.cpp | 93 + 26 | a managed AreaPortals record; crossing: SetAreaPortalState (FOLD) |
| hierarchy: EntitySetParent/GetParent/GetParentAttachment/Set|GetLocalTransform slots + relays | 138 | round trips (managed -> native -> IManagedHierarchy -> managed): Entities.* facades call HierarchyDriver directly; GetParent reads the block ParentHandle (EngineBlock.cs). Crossing: none |
| world/entity API: TraceLineInto's SOURCESHARP_TRACE_* mask table, TemplateSpawnKeyed, EntityAddBaseVelocity, CheckTransmit walk, PaintDecal, CWorld::Precache, EntityGetState, CWorld::KeyValue, EntitySetKeyValue round trip, WorldChapterTitle round trip | ~830 | PointTemplate.cs, Movement.cs, ServerEntities (measure CheckTransmit after), DecalEmitter.cs, LifecycleStages.cs, EntityKeyValues.cs/MapSpawner.cs |
| rule headers: scripted_kinds.h, collision_group_names.h, movement_ground*.h, ground_handle_wire.h | 200 | EngineCollisionGroup.cs already mirrors the positional engine enum (overturns entity-shell.md §3/§9's reason to keep the native 5-row translation), SsPlayer.GroundWireNone, Movement.cs; scripted_kinds needs the ABI change entity-shell.md §9 named |
| overlays: overlay_library.cpp + shell_debugoverlays.cpp | 240 | new `DebugOverlays` (DebugHooks): viewer, clip rule, counters, pool, picker gate; every draw FOLDs to DebugOverlayVtbl |
| damage/blood/tracer: takedamageinfo_library.cpp, blood/tracer arms in util_shared_library / server_util_library / shell_absent | 125 + 120 | DamageRules/BlastRules (g_MultiDamage's only importer is shell_gamerules' ctor/EndGameFrame -- mostly DELETE after the move); new BloodEffects/TracerEffects (violence convars, German recolour, x5 multiplayer, MECH sparks) |
| recipient filters: shell_recipientfilter.cpp + ss_recipient_filter.cpp + closecaption_recipient.h | 135 | new managed `RecipientFilter` (AddAllPlayers, PVS/PAS via `Message_DetermineMulticastRecipients`, attenuation cull, caption removal); needs a managed-built IRecipientFilter (4 slots). **Decision 2** |
| trace filter: trace_filter_library.cpp + CSourceSharpGameRules::ShouldCollide | 65 | managed trace filter rule (owner, shape clause, group matrix); needs a managed-built ITraceFilter the engine calls per candidate. **Decision 2** |
| lifecycle: RegisterConVar/RegisterConCommand policy, RegisterConCommandBase (stock game.cpp:60-98 replicated-default capture), kServerSystems[] order, LevelInit arm selection, Precache_Model late-precache rule | 135 | ConVars/GameDllConVars (needs a "flags of a ConCommandBase" and a "set ConVar by pointer" slot), LifecycleStages/GameLifecycle (per-system stage slots), HostingServices, ServerPrecache/ClientAssets |
| gamerules: CSourceSharpGameRules | 206 | **the engine never calls IGameRules**: igamerules.h is native-internal (10 native importers, no engine consumer); 17 of 24 virtuals are empty/constant. Retire reader by reader: shell_gameclients -> IManagedClients, shell_frame -> ManagedFrame, gametags empty; then the object, its empty SendTable and igamerules.h go |

## 4. Phases

Every phase: one reviewable commit per unit; build + the tests the change
touches per commit; `make check` and the daily gate's next report per phase
(the live gate by hand only when the user asks or to diagnose); ledgers and
ratchets in the same commit; zero warnings; the fork clean after every run.
Each DELETE lands with the nm/gc proof in its commit message; each MOVE lands
with the facts that pin it, written from the native code first and shown red
(test-first-physics-bones.md's rule, applied here too). Stock arms behind a
NULL `pBase` are PORTED, never deleted (port-dead-stock-arms.md).

### P0. The ledger becomes a ratchet (1 commit; Recipe G)

- Commit `docs/plans/server_native_audit.tsv` and a `tools/server-native-scope`
  check in `tools/checks.tsv`: it re-runs fnmap over the server source list,
  requires every function to have a ruling (join on file+symbol, not line:
  lines move), and holds a per-file ratchet on MOVE+DELETE+FOLD+DEBUG code
  lines that may only fall (`docs/server-native-ratchet.tsv`, regenerated
  with `--write`, reviewed in the diff). A KEEP-* function that grows a
  branch on game state is a new row to rule, not a silent pass: the check
  fails on an unruled function.
- Why first: every later phase is scored by this number, and the file-count
  instruments here have flattered progress before (CLAUDE.md "Measurement
  pitfalls").

### P1. Deletions (about 2,200 .cpp + 585 header lines; ~25 commits; Recipes A, B, C)

Cheapest first, each proven before it lands (`tools/shim-orphan-reach`,
`nm -u` over every object, `--print-gc-sections`, grep over src/ with tests,
then `make debug GC_SECTIONS=0` after each evicted TU):

1. Whole files from the table in §3 (13 TUs, 12 headers) plus the plugin
   ABI (shell_playerinfo.cpp, shell_playerinfo_debug.cpp, plugin_check.cpp,
   the ServerPluginHelpers layout; user ruling 2026-10-11), one commit each,
   CMakeLists comment or docs/server_evictions.md row per eviction, their
   `sdk-links` lines if any, and the tests that read their text re-pointed
   or retired with them (entity-shell.md §6: 27 gtests read these headers'
   text; audit per test first).
2. Dead bodies inside live files: the 24 inert slot bodies in
   shell_entityapi/shell_worldquery (empty or constant; the managed caller
   gets nothing: Ignite, Dissolve, ShootGib, FireEffect x3, texture frame,
   EnvZoom FOV, ...), the 14 no-op `SourceSharp_Weapon*` slots
   (`memset; return 0`), `FindBaseEntityByName` (INVALID unconditionally),
   `EntityParentProbe` (constant), `StandaloneUpdate` (empty, 0 callers),
   `PlayerSetSoundscape`, `EntitiesSdkPlayerSkyboxArea` /
   `ClientsSdkPlayerEarPosition` (return 0), `SourceSharpTe_SuppressFilter`
   (constant false behind 9 `if (Suppress) return` sites), IShimFrame's 4
   empty slots, CShimLevel's 13 empty/retired methods, `TemplateCapture`
   (`return 0`), `InitBodyQue`, `CSourceSharpEventLog`, the pending-spawn
   queue in shell_mapparse (push-then-claim-on-the-next-line for every live
   caller; only drain importer is a debug object), CStandaloneActivatorScope
   (no reader since FireNamedOutput left), the per-class output-name copies,
   IGameMovement's DiffPrint/GetPlayerMins/Maxs/ViewOffset, DT_Local,
   tier1_port's CRC32 table and HushAsserts, shell_movement's dead arms.
   The managed side loses the matching EngineSide wrappers and
   `engineapi-areas.tsv` rows in the same commit; a managed caller that was
   getting nothing names what it actually needs in its own comment (the
   ledger rows do: Arsenal.cs, Armour.cs, PlayerNetState fields).
3. ABI slot removal is batched: `SS_RETIRED` markers per commit, one
   serialised renumbering pass at the end of P1 (docs/abi-retirements.md),
   then `AbiParityTests`, brace/line/summary checks.
4. Client-only arms compiled out of shared files (`#ifdef CLIENT_DLL`: 284
   lines in cluster C, 161 in util_shared_library) are not server deletions;
   they are listed so the number is honest and go to the client lanes.

Exit: ratchet DELETE lines at zero for every in-scope file; `make debug
GC_SECTIONS=0` links; the daily gate green on the new static-init order.

### P2. Round trips and folds (about 540 FOLD + ~300 round-trip MOVE lines; ~20 commits; Recipes C, D, E)

No new logic in managed; managed calls what it already can.

1. Round trips, where native holds managed data only in transit: the five
   hierarchy slots (138), `WorldChapterTitle`, `EntitySetKeyValue` on a
   managed entity, `CWorld::KeyValue` (every arm ends in managed), skybox
   area (sky_camera -> FillSkyboxParams -> native -> managed), ear position,
   `cs_sv_crossings` (native command printing IManagedDiagnostics'
   report; live-gate issues it: make it a managed command first, then
   delete), `SourceSharp_EndRound` if CShellGameRules::EndMultiplayerGame
   forwards to managed (verify).
2. FOLD onto layouts managed already has (`VtableLayouts.g.cs`), one
   interface per commit, slot and body gone with it: GameEventManagerVtbl
   (shell_gameevents 58; add an IGameEvent layout and the other 40 follow),
   NetworkStringTableContainer/TableVtbl (shell_stringtables 45 +
   precache material/particle AddString), BaseFileSystemVtbl
   (shell_engineservices 34), DebugOverlayVtbl (overlay_library 29),
   EngineServerVtbl (ServerCommand/Execute, SetView, GetPlayerUserId,
   LightStyle, PrecacheDecal, PrecacheModel/IsModelPrecached, the PVS reads,
   SetAreaPortalState, Message_DetermineMulticastRecipients, Time()),
   EngineTraceVtbl.GetPointContents, EffectsVtbl (Sparks, Dust,
   MuzzleFlash), ModelInfoVtbl.GetModel, PhysicsSurfaceProps
   (GetSurfaceIndex/GetSurfaceData), UploadGameStatsVtbl, the GlobalVars
   fields IShimFrame reads/writes (managed holds the pointer). What cannot
   fold and why is a row: `SendClientCommand` (ClientCommand is varargs),
   every trace (needs an ITraceFilter object: P3f).
3. Debug commands whose subject is managed data move to `tools/debug-api`
   routes (cs_sv_dump_types, cs_sv_playerinfo, the payload oracle/poke/probe
   bodies in shell_standalone.cpp -> a `_debug.cpp` first, then the API);
   commands with 0 drivers in tools/ (cs_standalone_parent/mapspawn/
   viewmodel_report, dump_entity_sizes) are deleted; `cs_standalone_spawn`
   stays until tools/live-gate and live-gate-skips.tsv are re-pointed;
   readers of native-only state (ss_ground, cs_sv_handle_cross_check, the
   SendTable dumpers) stay native until their subject moves.

4. Pure-forward engine vtables become managed-built Valve-layout tables (user
   question 2026-10-11: "can we call straight into the vtable from C# for
   pure forwards?" -- yes, the existing `ValveLayout` pattern). Per interface,
   one commit: a `docs/managed-interfaces.tsv` block, `chain=` in
   tools/vtable-layout, AbiParityTests, then the native class deleted.
   Order: IServerGameEnts (6 methods, 3 hot voids), IServerGameClients (19),
   IServerGameDLL (55; `DLLInit` stays native because it starts the runtime,
   and the five deciding methods move their decision to managed in the same
   commit). Two constraints hold the shape: the engine asks CreateInterface
   for these BEFORE DLLInit, so the factory hands out a native object whose
   vtable pointer is swapped to the managed table at DLLInit (and swapped
   back on `cs_sv_reload`, plan_managed_vtable_reload's rules); and an
   engine-to-managed call is a reverse P/Invoke with a GC transition on every
   call (`[SuppressGCTransition]` applies only to managed-to-native leaf
   calls), so the hot per-entity vtables (CStandaloneEntity's ICollideable /
   IServerEntity / IServerNetworkable, called by the engine per entity per
   frame) are measured on dm_lockdown before they follow; a blown budget keeps
   those as a native class over the engine block, which is KEEP-LAYOUT work
   either way.

Exit: FOLD at zero; the EngineSide wrapper count falls by the folded slots;
KEEP-THUNK re-ruled per interface with the measured exceptions named;
`SideAuditTests` and `EngineApiAreasTests` green.

### P3. Logic to managed, by subsystem (about 5,000 lines; the long phase; Recipe F, E for P3f)

Order: self-contained and large first; the player/movement block last
because it depends on who owns movement state. Each block is its own lane
and worktree (delegate by file ownership; serialise the ABI tail).

- **P3a sound (1,040 lines).** `SoundEmitter` over the three sound Vtbls;
  SoundEnvelopeController.cs takes the patch state; CloseCaptions.cs the
  caption rules. One generic crossing stays until P3f: an EmitSound/StopSound
  slot taking a recipient slot list (the UserMessageSend shape), because
  IRecipientFilter is a vtable the engine calls back. Facts: the script
  resolve, pitch/volume overrides, delay->start time, gender-from-model,
  rndwave stop loop, preload and late-precache rules, each from the native
  code at base, red first. Live check: the gate's second map load (sound
  patches survive a reload).
- **P3b tempents and datatables (1,570 lines).** Managed emits the TE prop
  plan (`SourceSharpTePropPlan_t` rows) and the SendProp plan; native keeps
  the constructors it must own (SendProp has a vptr; the engine walks the
  table) in ~75 + ~80 lines. The `TE_*` forwarders go when their native
  callers (shell_worldquery effect slots, world_adapter decals, blood/tracer,
  physics_shared -- the last is OOS and waits for the physics plan) call
  managed. A datatable change is verified only by a full join (`make
  headless`, write on the server, read on the client).
- **P3c save/restore codec (790 lines).** Byte arithmetic over the engine's
  buffer and descriptors; three generic IManagedSaveRestore slots; the entry
  stays IServerGameDLL::Save* in shell_gamedll.cpp. Byte-exact oracle: a
  save written by the native codec at base must be identical from the
  managed one (hash the buffer). Decision 3 ruled: managed.
- **P3d entity API and world (1,500 lines).** variant_t to VariantValue.cs
  (DispatchInput crosses the raw field type); the trace mask table (managed
  passes `MASK_*` raw); TemplateSpawnKeyed to PointTemplate.cs;
  EntityAddBaseVelocity to Movement.cs; PaintDecal to DecalEmitter.cs;
  CWorld::Precache to LifecycleStages.cs; KeyValueShellOnly and its mirrors
  to EntityKeyValues.cs/MapSpawner.cs; SetModelName/SetVisible/
  UpdatePartition to ScriptedEntity.cs + CollisionRules.cs (needs the
  partition handle as a block word and a managed-written touch-mask word);
  the four rule headers; the areaportal record; overlays; blood/tracer;
  takedamageinfo. CheckTransmit's per-client walk moves under "move first,
  measure after": dm_lockdown numbers per client per frame before the native
  walk is deleted, and a blown budget is a batching task.
- **P3e lifecycle (135 lines).** ConVar/ConCommand registration policy and
  the replicated-default capture to ConVars/GameDllConVars (two generic
  slots: "flags of a ConCommandBase", "set ConVar by pointer");
  kServerSystems' order to LifecycleStages; LevelInit's arm selection
  (loadGame/background in SourceSharpMapLoad; the four OnMapLoad_* slots
  collapse to one); Precache_Model's late-precache rule; the IGameRules
  retirement reader by reader.
- **P3f the two engine callbacks (200 lines; Decision 2).** A managed-built
  ITraceFilter (ShouldHitEntity per candidate, mid-trace) and IRecipientFilter
  (4 slots), on the B2.1/B2.2 pattern (`docs/managed-interfaces.tsv` rows,
  `chain=` in tools/vtable-layout, AbiParityTests), with
  `[SuppressGCTransition]`. Measured on dm_lockdown against the native filter
  before the native one goes: TraceLine is the hottest slot in
  `tools/slot-census.tsv` (3,250 calls, 1.57 trace lines per frame at rest).
  With these two in place every trace FOLDs onto EngineTraceVtbl and the sound
  slot-list crossing from P3a goes.
- **P3g player command path and movement (570 lines).** TickBase_Adjust,
  the RunCommand prepare, FinishMove/pitch clamp, SyncToBlob, the IMoveHelper
  decisions, PlayerSync_Write's 30-field copy (inverts once managed owns the
  fields), ClientSetupVisibility, shell_movement's FillConstants (46 words
  managed already declares) and stock arms. Depends on IMovementPlayer's
  members (m_vecAbsVelocity, m_Local, ...) leaving C++, which shell_movement
  reads through the vtable: ruled with the physics plan's 2.5 (player
  shadow split) and sequenced after it. The IGameMovement export is parity
  only (no SDK 2013 binary carries "GameMovement001"; its only caller is
  ours): **Decision 4**.

Exit per block: MOVE at zero for its files; its facts green; the sandbox
pass (Sandbox.cs) after the batch; a live check on the paths single-load
gates miss (sound patches and areaportals across the second map load).

### P4. Substrate collapse and close

- KEEP-HOST's tier substrate (tier0_spew_port, tier1_port, ConVarRef and
  CManagedConVarRef, the string pool, thread id) exists because server.so
  links no tier libs and our own native code reads it. Re-measure importers
  after P3 (`nm -u` per object) and delete what has none; what the engine
  itself calls (spew hooks, ICvar carriers) stays.
- `shell_diagnostics.cpp`'s 274 DEBUG lines and `alloc_check.cpp` (510)
  stay as debug-build instruments unless the debug API covers them.
- Close: re-run §1's instruments at the tip, the before/after table with
  commit shas, release `.text` size (build release in this checkout first:
  the current binary is stale), a census of what native stays and why (thunk
  / layout / registration / host / marshalling / debug-only), zero
  MOVE/DELETE/FOLD rows, `make sdk-census` and the `sdk-links` lines
  deleted for every fully-ported file, docs/shim-reasons.md's `x` rows closed,
  and the memory note updated.

## 5. Decisions (all six ruled by the user, 2026-10-11)

1. **IPlayerInfo / IPlayerInfoManager (222 lines + the orphan CPlayerState).**
   RULED 2026-10-11 (user): third-party plugins are dropped. shell_playerinfo.cpp
   and its debug file, plugin_check.cpp (`IPluginHelpersCheck`, 33 lines) and
   the `ServerPluginHelpersVtbl` rows are P1 deletions; `GetPlayerState` stays
   non-NULL (engine-boundary.md §4.1) by other means.
2. **Managed-built ITraceFilter and IRecipientFilter.** RULED: managed. The
   engine calls both back synchronously (per trace candidate; per
   recipient); both are built as managed-built Valve-layout vtables, measured
   on dm_lockdown before the native ones go (P3f).
3. **Save/restore codec.** RULED: managed. docs/managed-saverestore.md's "(f)
   stays native" defended the byte layout, which the managed codec keeps
   byte-exact (oracle-hashed); the ruling is overturned in P3c's first commit,
   which rewrites that doc's row.
4. **IGameMovement's EXPOSE_SINGLE_INTERFACE_GLOBALVAR.** RULED: cut it. No
   engine binary resolves "GameMovement001"; the export goes in P1 and the
   object stays as our own thunk until movement state is managed (P3g).
5. **Debug commands with zero drivers in tools/.** RULED: cut it (P2.3);
   anything wanted back is a debug-API route, not a native command.
6. **Order of P3.** RULED: a, b, c in parallel lanes (disjoint files), then d
   and e, then f, then g after the physics plan's 2.5.

With these ruled, the plan has no open stop points other than the measured
gates (P2.4's hot vtables, P3d's CheckTransmit walk, P3f's callbacks) and the
coordination points with the physics lane.

## 6. Risks

- **Per-call cost of managed callbacks (P3f) and of the CheckTransmit walk
  (P3d).** Mitigated by measuring before deleting the native path; a blown
  budget is a boundary-design task (batching, `[SuppressGCTransition]`),
  never a retreat (CLAUDE.md).
- **Tests that read native text.** 27 gtests read the entity-shell headers'
  text, and others pin readers that no longer exist (gamedll_convars_test).
  Each deletion audits its tests first; a test that pins a dead reader is
  retired with the reader, not satisfied by keeping dead code.
- **Static-init order.** Every evicted TU re-orders the link; the daily gate
  covers the new order and its next report is read before building on it.
- **Hot reload.** Managed state that now outlives a `cs_sv_reload` (sound
  patches, areaportals, the TE registry) follows
  plan_managed_vtable_reload's rules; the gate's second map load is the
  check.
- **Coordination.** The physics plan owns the player shadow arm, physics.cpp,
  physics_shared and the four twinned deciders; P3b's physics_shared caller
  and P3g wait for it. Message the physics lane before touching
  shell_standalone_player.cpp or standalone_relays.cpp.

## 7. What was not established (named per ledger row)

- Whether managed can build a trace filter today (none found: every trace is
  KEEP-OUT with its decisions MOVE until P3f).
- CheckTransmit's and a managed CAreaPortal::UpdateVisibility's cost: ruled on
  shape, numbers in P3d.
- IServerGameDLL per-method engine call counts (no live probe in
  engine-boundary.md; ruled from eiface.h semantics): ReplayInit,
  GetServerGCLobby, GetWorkshopMap, Prepare*, CanProvideLevel.
- Which managed code drives each IShimFrame slot (only
  `Frame.GetFrameTime(` greps); traced in P2.2 before the fold.
- studio.cpp's intra-TU references (nm cannot see them): the 41 functions
  with no external referrer are KEEP-OUT with a DELETE note until checked.
- Whether SS_BOUNDARY_HIT compiles into release (shell_boundary_probe.h is
  DEBUG either way).
- RESOLVED 2026-10-12: HushAsserts survived the link with U=0 over our
  objects because `lib/libmathlib.a` imports it (archive members are
  invisible to that sweep). Its row is KEEP-HOST; tier1_port's other
  "dead" rows (the CRC32 table, `_V_strcmp`) are re-checked against the
  archive and the GC_SECTIONS=0 link before any cut (Recipe A step 1).
- Whether `g_PendingDeletes`' fallback in Remove()/Finish() is ever taken (no
  counter proves the comment).

## 8. Recipes (the mechanical steps behind each unit)

All paths are from the repo root. `$B` is `farm/debug/build`; the server's
objects are under `$B/src/server/native/CMakeFiles/server_sourcesharp.dir/`
(shared TUs under `.../__/__/shared/native/`). Build once before any
measurement (`make debug`) so the objects match the tip.

### Recipe A: prove a DELETE before deleting it

1. Symbol-level: no object imports it.
   ```
   OBJS=$(grep -oE '[^ ]*server_sourcesharp\.dir[^ :]*\.o' $B/build.ninja | sort -u)
   for o in $OBJS; do nm -C -u $B/$o | grep -F 'SymbolName' && echo "  <- $o"; done   # must print nothing
   nm -C --defined-only bin/debug/bin/linux64/server.so | grep -F 'SymbolName'          # gc-dropped already? then it is dead in the binary
   ```
   A whole file: `tools/shim-orphan-reach --side server <file>.o` (exit 1 if
   any symbol is DEAD; read the per-symbol table).
   **Objects are a floor: sweep the archives too.** server.so also links
   `$B/lib/libmathlib.a` (read the link rule: `grep -A8 '^build out/server.so:' $B/build.ninja`),
   and an archive member's import is invisible to nm over our objects.
   `nm -C -u $B/lib/libmathlib.a | grep -F 'SymbolName'` must print nothing
   too. HushAsserts was ruled dead at baaebbe6a on object nm alone and is
   imported by libmathlib.a's QuaternionMatrix asserts: the split failed to
   link and the row is KEEP-HOST (ea's lane, 45cb3808b). The link in step 5
   is the proof; nm only says where to look.
2. Source-level: no caller in either language, tests included.
   ```
   grep -rn 'SymbolName' src tools docs --include='*' | grep -v '^\s*//'
   ```
   A hit in a test that only pins the symbol's existence is retired with the
   symbol (say so in the commit). A hit in `docs/engineapi-areas.tsv` means
   the body is a slot: use Recipe C.
3. Virtual calls emit no symbol: for a virtual method, also grep the method
   name across `src/*/native` and read each hit.
4. **A shared TU is swept on both sides, and server-dead code leaves it.**
   If the file is under `src/shared/native/` (the tier ports are there since
   ea's client batches 3 and 4, main 466d9e12c and 4335ea97a: tier0_spew_port,
   tier0_thread_port, tier1_port, convar_port, tier_connect, studio_mdlcache;
   their ledger rows were re-pointed in those commits), repeat step 1 over the client's objects
   (`client_sourcesharp.dir`) and client.so. User ruling 2026-10-12: code the
   server no longer needs does NOT stay in shared code. A symbol dead on the
   server and imported by the client is split into a client-only file under
   `src/client/native/` (the shared TU keeps only what both sides import);
   a symbol neither side imports is deleted. A whole TU dead on the server
   moves to `src/client/native/` and leaves the server's CMake list in the
   same commit (Recipe B step 2), with its ledger rows flipped in that commit.
5. Delete, then `make debug` and `make debug GC_SECTIONS=0`; both must link.
   Paste the step-1 commands and their (empty) output into the commit.

### Recipe B: evict a whole TU

1. Recipe A over every symbol it defines (`nm -C --defined-only <obj> | awk '$2~/[TtWV]/'`).
2. Remove its line from `src/server/native/CMakeLists.txt` (both the source
   list and, if present, `server_novalve_sources`); leave a one-line comment
   at its old place pointing at `docs/server_evictions.md`, and add the row
   there (date, file, lines, reason, what replaced it). If the file is also
   built by client.so, it stays on disk; otherwise `git rm` it.
3. If the file has a line in `sdk-links/*.txt`, delete it (`make sdk-census`
   says which list).
4. Tests that read the file's TEXT (grep the basename under `src/test`):
   re-point or retire each, one by one, in the same commit.
5. `make debug`, `make debug GC_SECTIONS=0`, `make test`. The link order
   changed: the next daily-gate report covers it before the next eviction
   builds on this one.

### Recipe C: retire a managed-to-native slot (a `SourceSharp_*` body)

1. Find its row: `grep -n '^SlotName' docs/engineapi-areas.tsv` (column 8 is
   the body function, column 3 the IShim interface).
2. Find its managed callers: `grep -rn '\.SlotName(' src --include=*.cs`
   (the EngineSide wrapper in `src/shared/interop/EngineSide.cs`, then the
   wrapper's own callers). Delete the callers' use first (they get nothing
   from an inert slot: the ledger row names what they actually need; leave
   that as a `// TODO(ledger)` comment or a docs/todo.md item, per
   small-items-to-todo).
3. Mark, don't renumber: add `SS_RETIRED <SlotName> lane=<branch> <reason>`
   at the slot's field in `src/host/native/sourcesharp.h` and
   `src/abi/Crossings.cs` (docs/abi-retirements.md's contract; append your
   row there). Delete the native body and the EngineSide wrapper.
4. At the END of the phase, one serialised pass: delete every marked row
   from `docs/engineapi-areas.tsv`, run `tools/shim-areas --write` and
   `tools/vtable-layout --write`, then `make managed-test FILTER='FullyQualifiedName~AbiParity'`
   and `~EngineApiAreas`, and check brace balance / per-parent line counts /
   `<summary>` balance in the generated files (CLAUDE.md "After an ABI merge").

### Recipe D: FOLD a slot onto an engine vtable managed already has

1. Confirm the layout: `grep -n 'struct <Name>Vtbl' src/abi/VtableLayouts.g.cs`
   and the method row inside it (the comment names the C++ signature). No
   row: the interface is not folded in this plan; leave the row KEEP-OUT.
2. In the managed caller (usually the EngineSide wrapper, or the service that
   calls it), replace the slot call with the direct vtable call the way
   existing code does it (grep `EngineServerVtbl` outside `.g.cs` for the
   pattern: `<Holder>.Vt-><Method>(<Holder>.Self, ...)`). Marshal strings as
   the existing callers do (utf8 `fixed` pins).
3. Behaviour parity: a fact that calls the managed path and asserts the same
   engine call sequence the native body made (the `Crossings` log or a
   fake vtable in `src/test/managed`), red before step 2, green after.
4. Retire the slot with Recipe C.

### Recipe E: a managed-built Valve-layout vtable (P2.4, P3f)

The pattern is `src/server/interop/PhysicsObjectEventBinding.cs`
(`[ManagedVtable(typeof(PhysicsObjectEventVtbl))]`) and its chain row in
`tools/vtable-layout` (`chain=['IPhysicsObjectEvent']`, which generates
`[ImplementedByManaged("...", ValveLayout = true)]`).

1. Layout: the interface's vtable struct must exist in
   `src/abi/VtableLayouts.g.cs` with every slot in Valve's order (the
   `docs/abi-parity.tsv` row pins it against upstream; `tools/abi-parity
   --side server` must pass). Add the chain row in `tools/vtable-layout`,
   `tools/vtable-layout --write`.
2. Rows in `docs/managed-interfaces.tsv` (interface, method, return,
   parameters), then `tools/managed-areas --write` (regenerates
   `src/shared/native/imanaged_areas.h` and the managed side).
3. The binding class: one `[UnmanagedCallersOnly]` static per slot, the
   table filled with their addresses in `NativeMemory` (copy the physics
   binding's shape), registered as a service (no statics: SS0001).
4. Installation: who hands the engine the pointer. For VPhysics callbacks it
   is a `Set*Handler` call at level init. For the CreateInterface'd
   interfaces (P2.4) the engine already holds a native object from before
   DLLInit: keep that object, and at `IServerGameDLL::DLLInit` (after the
   runtime is up) overwrite its vtable pointer with the managed table;
   restore the native table in `DLLShutdown` and across `cs_sv_reload`
   (plan_managed_vtable_reload.md's rules: the table must survive an
   assembly reload or be re-pointed after it).
5. Facts: `AbiParityTests` (slot order), a fact per slot that the engine's
   call reaches the managed method (drive the vtable from the test through
   the function pointers), and the gate's second map load live.
6. Cost (P2.4's hot vtables, P3f): `tools/slot-census` before and after on
   dm_lockdown; quote both numbers with commit shas in the commit. A blown
   budget stops for the user.
7. Delete the native class (Recipe A/B) in the same commit as step 4 lands
   green, or the next.

### Recipe F: MOVE a logic block (test-first)

1. Read the native function at the tip and write its facts first, in the
   managed test project that owns the destination (`src/test/server` for
   server-only, `src/test/managed` for shared): one `[Fact]` per behaviour,
   inputs taken from the native code's branches (each `if`, each default,
   each table row is a fact). Run them against an empty managed
   implementation and confirm `Failed: N` with N = the count you wrote.
2. Port the function into the destination the ledger names (an existing
   type: add to it; `new: <Name>`: a new file, DI-registered, no statics).
   A stock arm behind a NULL `pBase` is ported too (port-dead-stock-arms).
3. Crossing: prefer none (the data is already managed); else an existing
   generic slot; else add a generic slot (Recipe C in reverse: a row in
   `docs/engineapi-areas.tsv`, `tools/shim-areas --write`, a body that only
   marshals). Never a slot that finds its state through a native global.
4. Re-point the callers: managed callers call the managed type; native
   callers of the old function are either on the MOVE list themselves (do
   them in dependency order: leaf first) or get a thunk that calls managed
   through the IManaged* interface (a row in `docs/managed-interfaces.tsv`).
5. Delete the native function (Recipe A). Facts green; the ledger row flips
   to DONE in the same commit (P0's check accepts DONE as a terminal verdict).
6. For anything per-frame or per-entity (CheckTransmit, the trace filter,
   the areaportal walk): `tools/slot-census` numbers before and after, in
   the commit; blown budget = stop.

### Recipe G: the P0 ratchet check (`tools/server-native-scope`)

A Python script in `tools/`, in the style of `tools/server-novalve-census`
(read its header for the self-test and `--write` conventions), one row in
`tools/checks.tsv` (tier routine, self-test first in the same row):

- Input: the server source list parsed from `src/server/native/CMakeLists.txt`
  (the `server_sourcesharp_sources` and debug lists), `tools/physics-scope/fnmap`
  per TU, `docs/plans/server_native_audit.tsv`.
- Join: (file, symbol) where symbol is the fnmap head line with whitespace
  collapsed; a function with no ledger row fails the check naming it; a
  ledger row whose function is gone is fine only if its verdict is DELETE,
  DONE or FOLD (else fail: "ruled KEEP but vanished").
- Ratchet: `docs/server-native-ratchet.tsv`, one row per file: code lines in
  MOVE+DELETE+FOLD+DEBUG rows. Measured > committed fails; `--write`
  rewrites the file for review in the diff.
- Self-test: plant a function in a temp copy of a TU, run, expect the
  "unruled" failure; plant a raised ratchet row, expect the "rose" failure.
- `--self-test` must fail when the ledger is missing (a missing input is a
  failure, never a pass).
