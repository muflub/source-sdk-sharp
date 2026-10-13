# plan_server_novalve.md: server.so without Valve game source or tier libs

Measured at this repo's `53dd88a` against the fork at `0c627ba61`, debug config
(`farm/debug/build`), on 2026-09-28. Every number below comes from a command in
§8; re-run them at the tip before starting a phase. Untracked until reviewed.

## The goal, as ruled

`server_sourcesharp` (server.so):

- **compiles no fork source**, and no fork header outside one generated
  allowlist: the engine interface headers, the mathlib headers,
  and their include closure (§2.1);
- **does not link** tier0, tier1, vstdlib, tier2, tier3, particles or
  dmxloader;
- **keeps linking** `libmathlib.a` only; *superseded 2026-10-13 (§6): server.so links no
  Valve library, the mathlib members it calls being `mathlib_port.cpp`'s;*
- **moves Steam to managed**: C# calls `libsteam_api.so`'s flat API itself,
  and server.so neither links it nor includes a `steam/` header (§3.5);
- **drops VScript** entirely;
- **moves to managed** the particle manifest, every ConVar and ConCommand
  declaration, and the user-message and usercmd encodings.

Whatever it still needs from the dropped libraries is either re-implemented in
this repo or moved into managed code. A native re-implementation is a port:
Valve header, a `docs/valve-header-new.tsv` row, and zero `valve-text-audit`
findings, so it is rewritten rather than copied.

client.so is not a target of this plan, but it may change wherever that
simplifies the server (user ruling, 2026-09-28): a codec moved to managed moves
for both ends, and a shared TU may be split or edited on the client side too.
The client must build and pass its tests at every commit. The launcher and the
host are out of scope.

---

## 1. Where server.so stands

### 1.1 What it compiles

| | count | instrument |
|---|---|---|
| translation units | 108 (68 server, 40 shared) | `ninja -t commands out/server.so`, `.o` inputs |
| fork **sources** among them | 1: `public/tier0/memoverride.cpp` | same |
| headers in the dep closure | 943 | `ninja -t deps` over the 108 objects, `readlink -f` |
| from the fork | **375** (127,962 lines) | same, prefix `/…/source-sdk-2013/` |

The most-included fork headers, counted by the `#include` lines in our files:
`tier0/memdbgon.h` 105, `game/server/cbase.h` 97, `player.h` 21,
`mathlib/vector.h` 20, `eiface.h` 15, `entitylist.h` 14. `cbase.h` is the
wall.

### 1.2 What the dropped libraries must still provide

These are the symbols imported directly by our 108 objects and by the 6
`libmathlib.a` members the link pulls (per `--why-extract`), attributed to the
first library in link order that defines them:

| dropped library | our objects | kept mathlib | **union to supply** |
|---|---|---|---|
| libtier0.so | 34 | 8 (assert/spew hooks, `GetCPUInformation`, `ThreadSleep`) | **37** |
| libvstdlib.so | 4 | 0 | **4** |
| libtier1.a | 123 | 0 | **123** |
| tier2.a | 3 | 0 | **3** |
| tier3.a | 8 | 0 | **8** |
| particles.a (+ dmxloader.a) | 11 | — | **0**: moves to managed |
| libsteam_api.so | 4 | — | **0**: moves to managed (§3.5) |
| total | | | **175** |

Of the tier1 imports: `bf_write` 17 + `bf_read` 2, `KeyValues` 13,
`CUtlBuffer` 12, `ConVar` 9 + `ConVarRef` 4 + `ConCommand` 4 +
`ConVar_Register`/`Unregister` 2, `CDataManagerBase` 8, `CUtlSymbolTable` 5,
`CStringPoolBase` 4, `CUtlString` 3, about 30 `V_*` string functions, `CRC32_*`,
`MD5_PseudoRandom`, `InterfaceReg`, `Sys_GetFactoryThis`, and the tier
connects.

The link also uses two fork files that are not libraries:
`devtools/version_script.linux.txt`, and the `--wrap=fopen|open|stat…` flags,
whose `__wrap_*` targets live in libtier0.so.

### 1.3 The header split

The allowlist probe (§8) compiles one TU that includes the root headers with
the server's flags. The roots are: `eiface.h`, `edict.h`, the entity
interfaces (`iserverentity.h`, `iservernetworkable.h`, `iserverunknown.h`,
`ihandleentity.h`, `engine/ICollideable.h`), `engine/*`, `icvar.h`,
`filesystem.h`, `igameevents.h`, `networkstringtabledefs.h`,
`ispatialpartition.h`, `ivoiceserver.h`, `irecipientfilter.h`,
`dt_send.h`/`dt_common.h`/`server_class.h`, `vphysics_interface.h` and
`vphysics/*`, `datacache/*`, `SoundEmitterSystem/*`, `scenefilecache/*`,
`toolframework/*`, `game/server/iplayerinfo.h` and `mathlib/*`.

| | fork headers | lines |
|---|---|---|
| closure of the roots (stays) | **112**, none under `game/` or `steam/` | — |
| to remove: `game/server` 77, `game/shared` 72 | **149** | 32,086 |
| to remove: `public/` and `common/` (38 of them `steam/`) | **114** | 49,571 |

Two results from building the probe:

- `vscript/ivscript.h` must not be a root. It pulls in 30 `game/` headers,
  `cbase.h` among them, through
  `vscript/variant.h → game/shared/ehandle.h → cbase.h`. Dropping VScript is
  what makes the allowlist clean.
- `tier1/convar.h`, `tier1/bitbuf.h`, `tier1/KeyValues.h`, `tier1/utlvector.h`,
  `tier0/dbg.h`, `tier0/threadtools.h`, `cmodel.h` and `gametrace.h` are
  **inside** the closure: the engine interfaces declare their signatures with
  them. So the engine ABI types are never re-declared. They are compiled
  against as they are, and only their out-of-line members need a definition
  (§3.2).

The public headers to remove include `studio.h`, `bone_setup.h`,
`bone_accessor.h`, `jigglebones.h`, `collisionutils.h`, `PlayerState.h`,
`networkvar.h`, `saverestoretypes.h`, `isaverestore.h`, `vcollide_parse.h`,
`model_types.h`, `shake.h`, `worldsize.h`, `coordsize.h`,
`particles/particles.h`, the `dmxloader/*` headers, `vscript/*`,
`tier0/vprof.h`, `tier2/tier2.h`, `tier3/tier3.h` and about 15 tier1 container
headers (`utlbuffer.h`, `utlhash.h`, `stringpool.h`, `datamanager.h`,
`utllinkedlist.h`, …).

---

## 2. The end state and its gates

A gate can fail. Each removal gate has a retention half that deleting code
cannot satisfy.

### 2.1 The allowlist

`docs/server-fork-allowlist.txt` is **generated**:
`tools/server-novalve-census --allowlist` builds the probe TU from the committed
roots list (`docs/server-fork-roots.txt`) and records the resolved fork
closure. The census fails if any line of it is under `game/`, `vscript/`,
`particles/` or `dmxloader/`. A new root is a reviewed one-line change.

| # | removal gate | instrument |
|---|---|---|
| R1 | fork files in `server_sourcesharp`'s dep closure that are **not** allowlisted: 263 → 0 | `tools/server-novalve-census`: `ninja -t deps`, `readlink -f`, set difference; a missing deps log is a failure |
| R2 | fork sources in the target: 1 → 0 | same, over the `.o` inputs |
| R3 | on the link line: `tier0 tier1 vstdlib tier2 tier3 particles dmxloader steam_api`, `devtools/`, `--wrap=` | `ninja -t commands out/server.so` |
| R4 | `DT_NEEDED` on `libtier0.so` / `libvstdlib.so` | `readelf -d out/server.so` |
| R5 | include directories other than `public`, `public/tier0`, `public/tier1`, `public/mathlib` and ours | `compile_commands.json`, every server entry |
| R6 | native ConVar/ConCommand declarations (`ConVar x(`, `CON_COMMAND*`, `ConCommand x(`): 39 + 51 → 0 | the census, anchored on whole declarations |

R5 makes a stray `game/` include a compile error early, but it cannot see a
relative include (`../../game/shared/ehandle.h` is how `variant.h` gets in).
R1 is the authority.

| # | retention gate (must not change) | instrument |
|---|---|---|
| K1 | the exported interface set | `CreateInterface` names registered, dumped by a gtest; `nm -D` diffed against the P0 snapshot |
| K2 | layouts of the types we re-declare that the engine reads (`studiohdr_t`/`mstudio*`, `CPlayerState`, the save/restore records, `vcollide_t`) | `tools/abi-parity`: clang `-fdump-record-layouts`, oracle headers against ours, diffed; mutation-proved |
| K3 | the send tables | full SendTable dump (names, types, flags, bits, offsets) at load, diffed against P0 |
| K4 | ports match stock | oracle gtests: CRC and `MD5_PseudoRandom` bit-exact; the managed usercmd decoder against `ReadUsercmd` over random commands; the managed manifest reader against `CParticleSystemMgr`'s name/precache list |
| K5 | live behaviour | `make live-gate` (dm_lockdown, both loads), a full join, and three P0 snapshots diffed: the `ParticleEffectNames` string table, the console variable list (`cvarlist`: names, flags, defaults, help), and a user-message round trip |
| K6 | test sizes | `tools/leg-floor`, native and managed, not below P0 except tests deleted with their subject and named in the commit |

---

## 3. Design

### 3.1 What stays a fork header, and what gets written

The allowlist already declares the engine ABI: `edict_t`, `CGlobalVars`,
`SendProp`/`SendTable`/`ServerClass`, `bf_write`/`bf_read`,
`ConVar`/`ConCommand`, `KeyValues`, `CUtlVector`, `Ray_t`/`trace_t`, the math
types, and every engine interface. **Nothing there is re-declared.**

What gets written is new, in `src/engineabi/native/include/engineabi/` (new
module, organised by what it declares, never by Valve path):

| file | replaces | notes |
|---|---|---|
| `studio.h`, `bones.h` | `studio.h`, `bone_setup.h`, `bone_accessor.h`, `jigglebones.h` | the `studiohdr_t`/`mstudio*` records bone setup reads, layout-identical (K2); only the members our TUs use |
| `playerstate.h`, `netvar.h` | `PlayerState.h`, `networkvar.h` | `CPlayerState` is engine-read (K2); network-var macros only as far as the shell uses them |
| `saverestore.h` | `saverestoretypes.h`, `isaverestore.h`, `game/shared/saverestore.h` | the records `IServerGameDLL`'s save entry points pass |
| `collision.h`, `world.h` | `collisionutils.h`, `coordsize.h`, `worldsize.h`, `vcollide_parse.h`, `shake.h`, `model_types.h` | constants and declarations for code already in this repo |
| `server.h` | `cbase.h` and the 149 `game/` headers | only what our TUs used, found TU by TU (P4), never by reading `cbase.h` |

**Naming rule.** A type the engine sees keeps its Valve name in the global
namespace, with its layout proved by K2, so moved and unmoved TUs link together
during the transition. A type used only inside server.so must **not** reuse a
Valve name: it becomes `std::` or `ss::`, because two bodies behind one COMDAT
name would be silent corruption. The version script's `local: *` stays.

### 3.2 The 175 symbols

Native code may carry out a decision but never make one, so logic that only
parses or decides goes to C#.

| group | ruling |
|---|---|
| `ConnectTier1/2/3Libraries`, `cvar`, `g_pCVar`, `g_pFullFileSystem`, `mdlcache` | **reroute**: one connect step does the factory lookups into globals we define |
| `RandomFloat`/`RandomInt`/`RandomSeed` | **reroute** to `IUniformRandomStream` from `VEngineRandom`, which we already acquire. Divergence: vstdlib's stream is process-wide and shared with the engine (and the client on a listen server). A P0 probe decides whether any caller depends on that sharing. |
| `InterfaceReg`, `Sys_GetFactoryThis`, `CreateInterface` | **port** (K1) |
| `bf_write`/`bf_read` (19) | **replace**: see §3.3. Nothing remains to port except `bf_read::SetOverflowFlag`, which `bitbuf.h`'s inline `Seek` calls. |
| `ConVar`/`ConVarRef`/`ConCommand`/`ConVar_Register` (19) | **move declarations to managed, keep one native carrier**: see §3.3 |
| `CRC32_*`, `MD5_PseudoRandom` | **port**, bit-exact (K4); `MD5_PseudoRandom` seeds usercmd randomness. If usercmd handling moves to C# whole, this moves with it. |
| `KeyValues` (13, in 22 files) | split per call site. Anything that parses text (propdata, soundscapes, sentences, manifests) **moves to managed** (`KeyValuesText.cs`). A `KeyValues*` that crosses the engine boundary needs its out-of-line members ported and `IKeyValuesSystem` from the factory: P0 measures which sites cross, and whether the factory answers `KeyValuesSystem002`. |
| `CUtlBuffer`, `CUtlString`, `CUtlSymbolTable`, `CStringPoolBase`, `CDataManagerBase`, the `V_*` functions | **replace** with `std::`/`ss::`/libc at each call site: nothing outside server.so sees them. `CDataManagerBase` has no direct user in our files, so P0 finds the header that instantiates it. |
| particles (11) | **move to managed**. The server needs only the manifest's system names and always-precache flags, to fill `ParticleEffectNames`. C# reads `particles/particles_manifest.txt` and each `.pcf`'s DMX header, string dictionary and `DmeParticleSystemDefinition` elements; native keeps only the string-table precache calls. K4 plus K5's table diff. |
| `studiohdr_t::GetVirtualModel`/`GetAnimBlock`/`GetAutoplayList`, `virtualgroup_t::GetStudioHdr`, `CJiggleBones::BuildJiggleTransformations` | **port** beside `bone_setup.cpp` (K4) |
| tier0 logging and asserts (`Msg`, `Warning`, `DevMsg`, `Error`, `_SpewInfo`/`_SpewMessage`, the assert hooks; mathlib calls these too) | **port** against `dbg.h`: hand each line to the spew function current in the engine's libtier0.so, found at runtime (`loaded_symbol.h`): it writes the console and `console.log`. `ICvar::ConsolePrintf` reaches the console only (a gate at 2bac4cf lost every native line from `console.log`); it is the fallback when no libtier0 is loaded. `DevMsg` reads `developer`. Proof: a line from server.so lands in `mods/test/console.log`. |
| threading (`CThreadFastMutex`, `CThreadSpinRWLock`, `CThreadLocalBase`, `ThreadGetCurrentId`, `ThreadSleep`) | **port** against `threadtools.h`, over pthreads |
| VProf | **delete** the uses (no-op macros) |
| `Plat_FloatTime`, `Plat_IsInDebugSession`, `GetCPUInformation` | **port**. First check (P0) whether any caller compares our `Plat_FloatTime` with an engine value: tier0 measures from a process-wide base. |
| `CommandLine_Tier0` | **reroute** to the command line the managed host holds, behind an `ICommandLine` implementation |
| `g_pMemAlloc` + `memoverride.cpp` | **drop `memoverride.cpp`; define our own `g_pMemAlloc`; check with a link hook**: see §3.6 |
| `--wrap=fopen…` | **delete** for this target; P0 converts any raw `fopen`/`stat`/`open` in server TUs to `IFileSystem` first |
| `devtools/version_script.linux.txt` | **replace** with `cmake/version_script_server.txt`, which exports `CreateInterface` only (K1) |
| VScript | **delete** `scriptmanager`'s acquisition and global (`shell_engineboundary.cpp:176,413`; nothing calls it) and every `vscript/` include. First search the engine binaries for a consumer of the server's `VScriptManager` (CLAUDE.md). |

### 3.3 The two engine containers: `bf_write`/`bf_read` and `ConVar`

**Buffers.** The engine owns the buffer at both crossings. `UserMessageBegin`
and `EntityMessageBegin` return the engine's `bf_write`, whose bits it forwards
to clients unread. `IServerGameClients::ProcessUsercmds` receives the engine's
`bf_read` over the bits the client wrote. So the container's layout is the
engine's, and `bitbuf.h` stays on the allowlist. What goes **inside** is
SourceSharp's own at both ends:

- User-message payloads are built by C#'s `UserMessageWriter`. The 17 native
  `MessageWrite*` writers have no caller outside tests. Delete them, and write
  the managed payload into the engine's `bf_write` with one bit copy through
  its public members (`m_pData`, `m_nDataBits`, `m_iCurBit`) and the inline
  `SetOverflowFlag`. The "dropped message" sink becomes an `ss::` type.
- **User messages (as landed in P2.2):** the three payloads the engine-side
  code still sends natively (Shake, Fade, CloseCaption) are built as bytes by
  `CUserMessagePayload` (`usermessage_write.h`). Every message, those and C#'s,
  reaches the engine's `bf_write` through one `SourceSharp_AppendBits`, which
  uses only the buffer's public fields and inline bits. The 17
  `MessageWrite*` writers are gone. `usermessage_write_test` holds the bytes
  to stock `bf_write` writers bit for bit, at all 32 starting offsets.
- **Usercmds (user ruling, 2026-09-28): decoded in C#, with one callback
  per packet.** `ProcessUsercmds` hands C# the engine buffer's bytes, the bit
  its commands start at and the counts, in ONE call (`PlayerDecodeCommands`).
  C# does the rest: the `ReadUsercmd` decode (`UserCmdCodec`), the
  `MD5_PseudoRandom` seed, the move-magnitude clamp inside the delta chain,
  and the plan and queue (`PlayerCommandQueue.DecodeAndQueue`). It answers
  once with the queued commands in run order, replays resolved, and where
  the commands end. Native `Seek`s past them and turns each into the
  `CUserCmd` its run loop reads. That replaces the native decode loop and
  the second crossing that carried its result to the planner. The decode is
  held bit for bit to stock `ReadUsercmd` on a recorded corpus
  (`Fixtures/usercmd-decode-stock.txt`: 250 live packets and 300 seeded
  random bitstreams, all decoded by stock). The client's `WriteUsercmd` is
  `CLIENT_DLL`-only; its format is unchanged.

**ConVars.** The engine keeps a `ConCommandBase*` for every console variable and
command. It calls through the vtable (`IsCommand`, `IsFlagSet`, `GetName`, the
`IConVar::SetValue` overloads, `InternalSetValue`, `Dispatch`) and reads fields
through `convar.h`'s inline accessors compiled into the engine
(`GetInt`/`GetFloat`/`GetString` read `m_pParent->m_nValue` and the rest
directly). So the object handed to `ICvar` must have `ConVar`'s layout and
vtable exactly. That costs nothing to write: `convar.h` is on the allowlist, so
the layout and vtable come from compiling against it.

Moving all declarations to managed shrinks the native side to one generic
**carrier**. Managed already builds native `ConVar`s through
`SourceSharp_EngineRegisterConVar` and `…RegisterConCommand`. The carrier's
virtual hooks (the change callback and `InternalSetValue`, and `Dispatch` for
commands) forward to managed, which owns defaults, clamping, flags policy and
the command bodies. Then:

- the 39 native `ConVar`s and 51 native `CON_COMMAND`/`ConCommand`s move to C#
  (R6);
- `ConVarRef` uses become managed lookups;
- `ConVar_Register`/`IConCommandBaseAccessor` go away: the managed registry
  registers each object itself and sets `FCVAR_GAMEDLL` (it already fills that
  hole, per `sourcesharp_gamesystem.cpp`'s registrar);
- what remains to port is the carrier's out-of-line members from
  `convar.cpp`: the constructors, `Create`, `InternalSetValue`/`SetValue`, the
  value-string bookkeeping behind the inline accessors, `Dispatch`, and the
  `ConCommandBase` virtuals. It must keep `m_fValue`, `m_nValue` and
  `m_pszString` consistent the way the engine's inline readers expect.
  `docs/convar-ledger.tsv`, the concommand-flag tests and K5's `cvarlist` diff
  are the retention half.

*Amended in P2.3.* What landed:

- **ConVars.** Every server declaration is a `[ConVar]` in C#, read natively
  through `CManagedConVarRef` (a by-name handle that answers the declared
  default until registration where 0 would change behaviour). `skill`,
  `developer` and `r_visualizetraces` are the ENGINE's (engine.so declares and
  registers all three first), so the server's copies were deleted; stock
  `util_shared.h`'s inline readers of `r_visualizetraces` are pointed at the
  engine's by a server-only compile definition that turns its `extern ConVar`
  into `SourceSharp_EngineVisualizeTraces()`.
- **Commands.** All 47 server commands are declared in C#
  (`SourceSharp.Server.NativeCommands`: name, help, flags) and registered
  through the existing slot. Their bodies read native state and stay native as
  `SS_NATIVE_COMMAND( name )` (native_command_body.h): a list keyed by name that
  the registration's carrier consults once, then runs on the engine's own
  `CCommand` with no crossing. No new ABI. A declaration without a body reaches
  C#, which says so; `NativeCommandTableTests` holds the two lists equal.
- R6 is 0. `ConVar_Register` and the carrier itself remain until P3 replaces
  tier1's `ConVar`.

### 3.4 The header move: two include sets on one target

*Amended in P1.* An object library would put objects from a second target on
server.so's link edge, and `tools/cmakegraph.py` and its C++/C# twins require
every object there to come from `server_sourcesharp`'s own `set()` list. So the
target keeps its one list and the sets are **per source**
(`src/server/native/CMakeLists.txt`):

- the target itself carries only this repo's directories;
- `server_novalve_includes`, the R5 set (`public`, `public/tier0`,
  `public/tier1`), for the TUs in `server_novalve_sources`, which also get
  `SS_ENGINEABI`;
- `server_legacy_includes`, the old target-wide list in its old order, for
  every other TU.

Moving a TU means adding it to `server_novalve_sources`, one commit per TU or
small batch. R5 is true once that list is the whole source list. The census
row `incdir-forbidden-tus` counts the TUs still on the legacy set (108 at P1),
and `novalve_include_set_test` plus the `novalve_include_set_canary`
must-not-compile check prove the R5 set compiles the allowlisted headers and
cannot reach `cbase.h` (mutation-proved: adding `game/server` to the set turns
the canary red).

- **Link order changes** when a TU moves, and a new object order is a new
  static-init order. Run the live gate after every move batch. Keep the moved
  TU's position where CMake allows it (one object library per position group
  if needed), and write down any order change.
- **Shared TUs** (40 of the 108) also build into client.so, which keeps the
  fork headers. Preferred order for each one: (1) its native logic moves to
  managed on both sides (usercmd, user messages, ConVars, particle names);
  (2) it is split into a server-only and a client-only file where the two
  have drifted apart; (3) only then, if it genuinely must build in both
  worlds, its removed-header includes go through `engineabi/switch/<name>.h`
  (the new header under `SS_ENGINEABI`, set only on the moved TUs; the fork
  one otherwise). The client build must stay green at every commit.

### 3.5 Steam: managed only

Today server.so's Steam use is one file, `shell_engineservices.cpp`, and it
does three things behind ABI entry points C# already calls:
`SourceSharp_SteamGameServerReacquire` (`CSteamGameServerAPIContext::Init`),
`…Clear`, and `…RequestGameplayStats`
(`ISteamGameServer::GetGameplayStats`). `shell_engineboundary.cpp` defines
the two context globals, and `shell_gameclients.cpp` includes
`steam_gameserver.h` without calling it. Its imports from `libsteam_api.so`
are 4: `SteamGameServer_GetHSteamUser` and three `SteamInternal_*` functions,
all from the context's inline code.

`libsteam_api.so` exports the flat C API (1,048 `SteamAPI_*` functions,
including `SteamAPI_SteamGameServer_v014` and
`SteamAPI_ISteamGameServer_GetGameplayStats`). So:

- C# P/Invokes the flat API directly. It gets the game-server interface with
  `SteamAPI_SteamGameServer_v014()`, re-fetched on activation, which is what
  `Reacquire` did, and calls `GetGameplayStats` itself. The launcher already
  binds `steam_api` from C#, which is precedent for the import resolver.
- **The same instance, proven:** the engine loads `libsteam_api.so` before the
  mod. A test asserts that the handle managed resolves is the one
  `dlopen(RTLD_NOLOAD)` returns. Two copies would mean two Steam contexts.
- The engine's `IServerGameDLL::GameServerSteamAPIActivated`/`…Shutdown`
  callbacks stay on the native vtable. They already forward to managed
  (`OnSteamGameServerActivated`/`…Shutdown`) and need no Steam headers.
- Delete the three entry points (the ABI slots retire under the append-only
  rule: marked retired, never reused), the context globals, and the `steam/`
  includes. Then drop `libsteam_api.so` from the link: R3 and R1 cover it.
- Retention: the live gate and a join on a Steam-authenticated dedicated
  server, with the managed log showing the game-server interface acquired and
  `BLoggedOn` true after activation.

### 3.6 Allocation: drop memoverride, keep a link-time checker

Today `memoverride.cpp` (fork, compiled into server.so) defines `malloc`,
`free`, `realloc`, `calloc` and the `new`/`delete` family, all routed to tier0's
`g_pMemAlloc`. `libtier0.so` defines no `malloc`/`free` of its own and imports
glibc's (`nm -D`), so tier0's allocator sits on top of glibc. Whether a pointer
from it can be passed to glibc's `free` is not visible from here: there is no
tier0 source.

**`g_pMemAlloc` stays defined.** 43 of our 108 objects reference it, because
allowlisted inline code calls it (`tier1/utlmemory.h`'s aligned growth,
`tier0/memalloc.h`). We define it as our own `IMemAlloc` over glibc, compiled
against the allowlisted `memalloc.h`. `memoverride.cpp` goes, and server.so
uses glibc directly.

**The checker is a link hook, debug config only** (`SS_ALLOC_CHECK`, on by
default in debug, absent in release):

- `-Wl,--wrap=` on `malloc`, `calloc`, `realloc`, `free`, `posix_memalign`,
  `aligned_alloc`, `strdup` and the whole `operator new`/`delete` family
  (`_Znwm`, `_Znam`, the nothrow, sized and aligned variants, and the matching
  deletes). Every call made from server.so's code resolves to a `__wrap_`
  function; allocations made by the engine never do.
- libstdc++ stays shared. Its out-of-line template code (`std::string`,
  `std::vector` growth) allocates and frees inside libstdc++.so, where no
  reference is wrapped, so those blocks are invisible to the checker rather
  than foreign. (P0 tried `-static-libstdc++` first; it brought libstdc++'s own
  mutable state into server.so, which the globals ledger counts.)
- The wraps keep a side table (pointer → kind, size, return address). They add
  no header to the block, so an engine `free` of a server pointer still works
  and the checker never changes behaviour. Three findings, each logged with a
  backtrace:
  1. **kind mismatch** (`new`/`free`, `malloc`/`delete`, `new[]`/`delete`,
     sized-delete size wrong): abort;
  2. **foreign free**: server.so frees a pointer it did not allocate. That is
     engine or tier0 memory, so ownership crossed into us;
  3. **live at unload**: pointers still in the table after `DLLShutdown`.
     These are leaks, or memory handed to the engine and freed there. Each is
     attributed to its allocation site.
- Gtests: each finding fires on a planted case (a death test for the
  mismatch), and a clean alloc/free pair raises nothing.
- The gate: `make live-gate` and a full join on debug with zero mismatches,
  zero foreign frees, and every live-at-unload site either fixed or named in
  a reviewed allow-file with its reason.

**Rule (user ruling, 2026-09-28): memory is never freed across the
engine/mod boundary**, in either direction. The module that allocates a block
frees it. There is no fallback to tier0's allocator.

- Finding 2 (**foreign free**) is a failure, like a mismatch: abort in the
  checker and fail the gate.
- Finding 3 (**live at unload**) is split by site. A block handed to the
  engine and freed there is a failure. A plain leak goes in the reviewed
  allow-file until it is fixed.
- Each crossing found is fixed at its site. Examples: a `CUtlVector` the
  engine fills through its own `utlmemory.h` inline growth and we then destroy
  is replaced by a copy-out into server-owned storage, or by an engine call
  that fills a caller-sized buffer. A string or `KeyValues` we give the engine
  to keep becomes one the engine allocates, or one we keep and free after the
  engine's documented release point.
- P0's baseline run lists the crossing sites that exist today. Each gets a
  P-step, and P4 cannot drop `memoverride.cpp` until that list is empty.

---

## 4. Risks and the measurement that retires each

| risk | measurement, before the change it guards |
|---|---|
| a re-declared layout drifts | K2, with the mutation proof |
| moved and unmoved TUs disagree on a same-named type | §3.1 naming rule, plus a census check: every Valve-named type with a vtable or COMDAT in both worlds must be on the K2 list |
| the allowlist grows by accident | it is generated from `server-fork-roots.txt`; a forbidden directory in it fails the census |
| the ConVar carrier's values disagree with the engine's inline reads | K5 `cvarlist` diff, plus a live check: set a replicated convar on the server, read it on a joined client |
| the managed usercmd decoder drifts from the client's encoder | K4 over random commands, plus the live join (movement works) |
| the shared vstdlib random stream matters | P0 probe (§3.2) |
| a free across the engine/mod boundary (forbidden), mismatched new/delete and malloc/free | the §3.6 link-hook checker, landed in P0 against today's memoverride build (the baseline list of crossing sites), then gating P4 at zero |
| log lines disappear from `console.log` | a live check before `-ltier0` is dropped |
| a `cbase.h` includer needs `CBaseEntity` behaviour, not only its type | the P4 per-TU compile says so; the answer is a port, an ABI call or a move to managed, never a copy of `baseentity.h` |

---

## 5. Phases

Work happens in one worktree cut from `main` at the reviewed tip, with its own
`make setup`. Commit after each step; a replacement and the deletion of what it
replaced are separate commits. Each phase ends with `make check`, the live
gate, `git -C ../source-sdk-2013 status --porcelain` empty, and the ratchet
updated.

**P0: instruments, baseline, first deletions**
1. `tools/server-novalve-census` (finds its root from its own location): R1–R6
   counts, per-library import lists, and `--allowlist`. It writes
   `docs/server-novalve-ratchet.tsv`, which only shrinks, and
   `docs/server-fork-allowlist.txt`. Self-tests: a `game/` include in a server
   TU goes red; a `game/` path in the generated allowlist goes red.
2. `tools/abi-parity` with the K2 type list and its mutation proof.
3. Snapshots: K1; K3; the `ParticleEffectNames` table; `cvarlist`; a
   user-message round trip; K6 counts.
4. The §3.6 allocation checker, with its gtests. Its first run is on
   today's build, where memoverride sits under the wraps, and the findings
   become the baseline.
5. Probes, with results written into §7 before P1: random-stream sharing, `Plat_FloatTime` comparisons, raw file I/O,
   `KeyValues` boundary crossings and `KeyValuesSystem002`,
   `CDataManagerBase`'s instantiating header, and the two dangling references
   (§8).
6. Delete VScript, the 105 `memdbgon.h` includes, our VProf uses, and the 17
   uncalled `MessageWrite*` writers with their tests.

**P1: skeleton**
Split the include set per source (§3.4) and move the first TU. Prove R5 on
it: a canary TU with a `game/` include fails, and the negative control compiles
in the old list. Create `engineabi` with `switch/`.

**P2: move to managed**
1. The particle manifest reader (C#), then drop `particles.a` and
   `dmxloader.a` from the link group.
2. User messages: native payloads as bytes, one bit append, the 17
   `MessageWrite*` writers deleted; `WriteUsercmd` becomes client-only. The
   usercmd decode moves to C# with one callback per packet (§3.3). The
   `bf_write` imports reach zero and `bf_read::ReadFloat` leaves with
   `ReadUsercmd`; `SetOverflowFlag` is left for P3.
3. ConVars and ConCommands: move the 39 + 51 native declarations to C# in
   batches by owning file, replace `ConVarRef`s, then retire
   `ConVar_Register`. R6 reaches zero.
4. `KeyValues` text parsing at our call sites.
5. Steam (§3.5), then drop `libsteam_api.so` from the link.

**P3: tier3, tier2, tier1** (ports and replacements compile on the R5 set)
tier3 → tier2 → tier1 in this order: connects, strings, CRC and MD5, interface
registry, the ConVar carrier's members, the utl replacements, what is left of
`KeyValues`. An archive leaves the `LINK_GROUP` in the commit where its import
list reaches zero; a partial port fails the link. After each: K4, the live
gate, the ratchet.

**P4: tier0 and vstdlib**
Logging and asserts (mathlib's calls included), threading, time, the command
line, random, `cvar`, our `g_pMemAlloc` over glibc (§3.6), our own version
script, and dropping the tier0 `--wrap=fopen…` flags (the allocation checker's
wraps are separate and stay). Then remove `-ltier0 -lvstdlib` and
`memoverride.cpp`: R2, R3 and R4 reach zero. Then the live gate and a full join
on debug with the checker clean, and the `console.log` check.

**P5: the header move**
1. The `engineabi` headers other than `server.h` (studio, bones, playerstate,
   saverestore, collision, world), with K2 over each.
2. Move TUs onto the R5 set in waves, ordered by the closure of our own
   headers: leaf TUs first, `cbase.h` includers last. For each `cbase.h`
   includer, swap in `engineabi/server.h`, compile, and add only what the error
   list asks for. A type that brings behaviour gets a §3.2 ruling.
3. Shared TUs: move, split, or as a last resort go through `switch/`
   (§3.4); the client stays green.
4. When the old list is empty, delete it: R1 and R5 reach zero.
5. Delete our `g_pMemAlloc` (§3.6). It exists only for inline code in fork
   headers (CUtl growth, `memalloc.h`, studio and save/restore hashes). At
   2bac4cf, 21 server objects plus `memoverride` referenced it. Gate: the
   `nm -u` sweep over the graph's server objects finds zero referencing
   objects, and server.so neither defines nor imports the symbol.

**P6: cleanup and final measurement**
Run `make sdk-census --write`: `sdk-links/server.txt` should empty out, and
shared lines only the client still uses move to `client.txt`. Add move-ledger
rows for the new module, a `docs/server_evictions.md` entry for
`memoverride.cpp`, and `make debug GC_SECTIONS=0` plus a release build. Then
re-measure every §1 number with §8's commands and write the before/after table,
quoting each number with its commit.

---

## 6. Rulings and remaining decisions

Ruled by the user on 2026-10-13, superseding the first ruling below:
- Drop `libmathlib.a` from server.so too: "ports the needed math logic into server to skip
  linking it". The twelve functions and two constants server.so's objects call are
  `src/server/native/mathlib_port.{h,cpp}`, held to stock bit for bit by
  `mathlib_port_stock_test`; DLLInit no longer calls `MathLib_Init`. client.so keeps the archive.

Ruled by the user on 2026-09-28:
- Keep `libmathlib.a`.
- Move Steam to managed: server.so drops `libsteam_api.so` and the `steam/`
  headers (§3.5).
- Keep the engine interface headers from the fork (the allowlist).
- Drop VScript.
- Drop `particles.a` (and with it `dmxloader.a`); port the particle manifest to
  managed.
- ConVar, ConVarRef, ConCommand and ConVar_Register become managed (§3.3).
- Replace `bf_write`/`bf_read` use where the encoding is SourceSharp's own
  (§3.3).
- Drop `memoverride.cpp`; track allocations with a debug-only link hook that
  enforces matching `new`/`delete` and `malloc`/`free` (§3.6).
- No frees across the engine/mod boundary, in either direction (§3.6).

- Client changes are allowed where they simplify the server (scope, §3.3,
  §3.4).
- "Tier libs" includes the engine-shipped `libtier0.so` and `libvstdlib.so`:
  no `DT_NEEDED` on either (R4). This is most of P4.

No open decisions remain.

## 7. Probe results

Measured in P0 on branch `server-novalve` (debug), 2026-09-28.

- **Allocation checker baseline** (`tools/novalve-snapshot`, dm_lockdown to a
  clean `quit`, memoverride still in place under the wraps): 3,311
  allocations, 5,385 releases, **0 mismatches, 4,318 foreign frees**, 2,167
  blocks live at unload. The foreign frees come from five sites, each a real
  crossing:
  1. `KeyValues::RemoveEverything` (4,314), under
     `CSoundscapeRegistry::AddSoundscapeFile`. `KeyValues::LoadFromFile` first
     asks vstdlib's `KeyValuesSystem()->LoadFileKeyValuesFromCache` to fill
     our object, so vstdlib allocates the value strings and our tier1 code
     frees them. P2.4 (text parsing to managed) removes it.
  2. `ConVar::ChangeStringValue` via `ConVarRef::SetValue` ←
     `SetEngineConVar` ← `SourceSharp_RegisterMovementParams`. `ConVar::SetValue`
     runs `InternalSetFloatValue` on `m_pParent`, and when two DLLs register
     the same name (a replicated `sv_` var on a listen server) vstdlib links
     our ConVar's parent to the other module's object. So server.so rewrites
     and frees a string client.so allocated. The §3.3 carrier must forward to
     a foreign parent through its virtual `SetValue`, and never run our
     setter on it.
  3. `ConVar::~ConVar` at global destruction: the inverse of 2 (the string it
     frees was reallocated by the other module's code).
  4. `CWorld::~CWorld` ← `CWorldNetworkable::Release` ←
     `CShimLevel::ClearEntityList`.
  5. `UserMessageHelper::~UserMessageHelper` at global destruction.
- **Particles:** `PrecacheStandardParticleSystems` precaches **0** systems on
  dm_lockdown (`[novalve-snapshot:particle-precache-count] 0`). The managed
  reader's first oracle is that count on this map; P2.1 adds a map or
  manifest entry with `!` (the always-precache marker) to show the reader
  finds a non-zero set, so that a reader which finds nothing cannot pass.
- **Random:** callers are `SharedRandom*` (`util_shared_library.cpp`, which
  seeds and then draws), `physics_fx.cpp`, `takedamageinfo_library.cpp`
  (through `random->`), `shell_propdata_system.cpp`, and `randoverride.cpp`'s
  `rand()`. None of them depends on sharing vstdlib's process-wide instance,
  only on the generator's algorithm (`SharedRandom*` reseeds before every
  draw). Ruling: port `CUniformRandomStream` natively as one server-owned
  instance (managed already has `UniformRandomStream.cs`), with an oracle
  test against vstdlib's sequence. This replaces the reroute to
  `VEngineRandom`.
- **Plat_FloatTime:** every server use is relative to an earlier value of our
  own (`studio.cpp` stall timing and hitch prints,
  `entity_shared_statics.cpp`'s 5-second spew throttle). Nothing compares it
  with an engine timestamp, so `clock_gettime(CLOCK_MONOTONIC)` is safe.
- **Raw file I/O:** two `fopen`s, both on absolute paths
  (`physics.cpp`'s `/tmp/ss-physworld-<pid>.log` diagnostic, and
  `shell_standalone_hierarchy.cpp`'s bone-replay file given by the operator).
  Dropping tier0's case-folding `--wrap=fopen…` changes neither.
- **VScript:** `engine.so` (SDK Base 2013 MP, linux64) contains no
  `VScriptManager` string, and nothing in server.so read `scriptmanager`.
  Deleted in P0.
- **Exports:** the debug-only `-static-libstdc++` exported 10 libstdc++
  runtime symbols until `cmake/version_script_lld.txt` hid them. server.so
  exports exactly what the main tree's does: `CreateInterface`, `cvar`, and
  the three linker symbols.
- **Still to probe, in the phase that needs it:** `KeyValues` crossings other
  than the cache fill (P2.4), `KeyValuesSystem002` from the factory (P3),
  `CDataManagerBase`'s instantiating header (P3), and the two dangling
  references (P5, when `cbase.h` leaves).

- **An intermittent live-gate stall, found in P3 and open.** A full gate can
  die after a step that loads (hot-reload in pass 2, save-load in pass 1): the
  step's checks pass, then the server runs no further frames and the dev API
  stops answering. It is not a P3 change: a control build without tier2's
  removal stalled too. Five full gates on 2026-09-28: 3 stalled. The same steps
  alone pass (save-load 3 of 3, hot-reload 3 of 3, tools/3f-gatebisect).

## 8. Instruments (as run for §1)

```sh
B=farm/debug/build
# objects and link line
ninja -C $B -t commands out/server.so | tail -1 | tr ' ' '\n' | grep '\.o$'
# header closure, classified by owner
ninja -C $B -t deps <server objects> | grep -E '^\s+/' | sed 's/^ *//' | sort -u \
  | while read f; do readlink -f "$f"; done | sort -u      # prefix-match the fork
# direct imports (LC_ALL=C for comm)
nm -C --defined-only $OBJS | awk '$2 ~ /[TDBRWVtdbrwvu]/' | sed 's/^[0-9a-f]* . //' | sort -u > def
nm -C -u $OBJS | sed 's/^ *U //' | sort -u > und
comm -23 und def                                           # 309 at 53dd88a
# attribute in link order: tier0.so vstdlib.so tier1.a mathlib.a dmxloader.a particles.a tier2.a tier3.a steam_api.so
# members the link pulls, and why
<link command> -Wl,--why-extract=why.txt                   # 6 mathlib members among 40
# mathlib's imports: ar x those 6 members, same def/und/comm, minus our
# objects' definitions -> tier0 8, nothing else
# allowlist closure: a probe TU #including the roots, compiled with
# shell_gamedll.cpp's flags from compile_commands.json plus -MM
#   -> 112 fork files, 0 under game/ or steam/
#      (with steam/ as a root: 150; with vscript/ too: 215, 30 under game/)
```

Two leftovers: `CAI_BaseNPC::TaskComplete` and `DispatchSpawn` are referenced
by our objects, defined nowhere, and the link still succeeds. The reference is
presumably in a discarded COMDAT copy. P0 confirms that; both must still be
gone at the end.
