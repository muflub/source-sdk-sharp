# plan_side_isolation.md: client and server isolated in one host

Written 2026-10-01 against main 47a68b1, then revised for the reviewer's
rulings with main at 14b8728. Facts marked *(read)* come from a survey of the
tree; *(measure)* ones stay open until the phase that owns them runs its
probe. Untracked until reviewed.

## The goal, as ruled

The user, 2026-10-01: **isolation between client and server in a shared host**
(a listen server runs both in one process). Concretely:

1. **No shared managed assembly at all.**
   - `SourceSharp.Shared`, `TF2.Shared` and `TF2.Descent.Shared` stop being
     assemblies; their files compile into each side's project (stock Source's
     `game/shared` layout).
   - **SourceSharp.Abi loads once per side too** (review answer 1). The host
     and the sides share no managed type.
2. **User-message ids are strings, scoped by module.** Message keys travel
   in a server-written networked string table, and the engine's one-byte
   message id is the key's index in that table. A message's id has to be
   unique only within its own module, because the table prefixes it with the
   module name.
3. **Every protocol list is in key order** (review answer 2). There is no
   pinned stock prefix; where an order matters to the engine, the table
   carries it.
4. **Tests split by side**: `SourceSharp.Server.Tests`, `SourceSharp.Client.Tests`,
   and `SourceSharp.Shared.Tests` (which compiles the shared files itself).
5. **The dev API is independent**: it references only its own
   `SourceSharp.DevApi.Contracts`; no Abi, Server, Client or Shared.
6. **The core fold waits for the engineapi lane's L3** (review answer 3).

## 1. Where things stand *(read, 47a68b1 unless noted)*

| fact | where |
|---|---|
| Shared is 117 files / 16,805 lines; TF2.Shared 1 file; TF2.Descent.Shared 1 file (2 messages) | src/shared/managed, src/tf2/shared, src/tf2_descent/shared |
| Shared already loads once per side (collectible ALC); **only Abi is pinned to the default context**, so Abi is the runtime state the sides share today | GameLoadContext.cs:52-66 |
| the host's boundary is Abi: `Entry` (1,733 lines, 42 `[UnmanagedCallersOnly]` thunks) casts the side's root to `ISideModule`/`IServerModule`/`IClientModule`/`IConVarHost` and fills `ManagedApi` with its own thunks (14b8728) | Entry.cs:19-46, :179-360 |
| Abi's thread-static `ActiveSide` (146 uses) exists because one Abi serves both sides; boundary code sets it via `SideEntry.Enter`, and the engineapi lane's Phase E deletes it (14b8728) | Engine.cs:49,74; SideEntry.cs |
| the crossing counters are one process-wide table *because* Abi is shared | Entry.cs:340-344 |
| 11 projects reference Shared; three see both sides and would hit CS0433: SourceSharp.Tests (189/457 files name Shared), OracleTests (12/21), TF2.Descent.Tests (3/8) | csproj files |
| DevApi references Server (for `IDevApi`) and uses Shared's DebugChannel* and LocalCommandLine; MapGen uses `EntityNames` (432 uses) through Server | SourceSharp.DevApi.csproj:7, SandboxPass.cs:336 |
| **L3 landed** (bf11962 on engineapi-vtables): ManagedVtable is now src/abi/ManagedVtable.cs, with a per-side `ManagedVtables` registry held by Bootstrap's Entry.cs; the generator targets `global::SourceSharp.Abi.ManagedVtable`. ManagedRoot.cs stays in Shared, and L4–L6 (reload patching, hash check, call depth) edit ManagedVtable.cs, the generator, Entry.cs's reload path and ManagedRoot.cs again | -a4, 2026-10-01 |
| SourceSharp.Client lacks the generators analyzer reference Shared's partials need | SourceSharp.Client.csproj |
| **five lists rely on order**, all derived from the assembly scan (name order, then metadata token, with the "shared" assembly first): user messages, string tables, temp ents, standalone net states, entity layouts (each calls `WarnIfNotShared`) | LoadedAssemblies.cs:42-52, UserMessage.cs:288, StringTableRegistry.cs:153, TempEntRegistry.cs:120, StandaloneNetStates.cs:416, EntityLayoutRegistry.cs:85 |

User messages, end to end:

- 40 core declarations plus 2 in TF2.Descent.Shared.
- **The native registry:**
  - It's idempotent by name and capped at 64.
  - The index is the order of insertion (shell_usermessages.cpp:182-259,
    shell_usermessage_registry.cpp:161-178).
  - The client hooks each index through a table of 64 per-index trampolines
    (:142-171).
- **Server sends go by name**: `LookupUserMessage`, then `UserMessageBegin`
  (shell_usermessage_send.cpp:109-129). Three native senders use literal
  names: CloseCaption, Shake and Fade.
- **The client** dispatches engine → `CUserMessages::DispatchUserMessage(idx)`
  → managed `Dispatch(index)`.
- **The SDK client hooks 8 stock messages natively by name** (VGUIMenu,
  Rumble, Shake, Fade, VoiceMask, RequestState, SavedConvar,
  AchievementEvent). Their names must exist before `gHUD.Init`, before any
  string table.
- **The engine reads `GetUserMessageInfo(index)`** (name and size) to
  describe messages and size-check them. It also uses it for demos, which the
  user ruled out of scope (2026-10-01): demo playback isn't a goal.
- **The native user-message code is 1,160 lines**:
  - shell_usermessages.cpp, 475;
  - shell_usermessage_registry.cpp, 336;
  - shell_usermessage_send.cpp, 178;
  - usermessage_write.h, 95;
  - ss_usermessages.h, 76.

  Its tests are usermessage_{haptics_order,info,write,ordering_guard}_test.cpp
  and one oracle test.

String tables:

- They're declared by `[StringTable]` and created only in
  `CreateNetworkStringTables`, the per-map window before clients connect, and
  clients receive them at sign-on.
- The client resolves a table by name on every call, with no cache and no
  change callback; the native callback hook covers stock tables only
  (shell_clientdll.cpp:1923-1967).
- Limits: `MAX_TABLES` is 32 (7 stock plus SourceSharpManaged), and
  `GetString` returns at most 260 bytes.

## 2. Design

### 2.1 Key order for every protocol list

Once the shared files compile into each side, "the shared assembly first, then
by name" means nothing. Every protocol list is therefore ordered **by its
declared key alone**, with ordinal comparison:

| list | key |
|---|---|
| user messages | the full table key (§2.2) |
| string tables | the table name |
| temp ents | the temp-ent name |
| standalone net states | the declared net-state key |
| entity layouts | the layout's class key |

- **No stock prefix is pinned** (review answer 2).
  - The six haptics no longer sit at ids 0-5. Their position was only ever a
    message id, which the table now carries (§2.2).
  - P0(5) lists every consumer that reads a list by **position** (native code,
    engine, demos). Any such list either gets its positions carried the same
    way, or comes back to the reviewer as a named exception. No list keeps a
    pinned prefix silently.
- **A protocol entry declared on one side only is refused.** If only one side
  declares an entry, the two sides' orders differ. Today that is a warning
  (`WarnIfNotShared`); it becomes an error, unless a list is server-only by
  nature.
- **Facts:**
  - Shared.Tests builds each list from the shared files alone, then from
    Server's and Client's built assemblies loaded by path, and requires all
    three to be identical.
  - The order tests that pin today's positions (HapticUserMessageOrderTests,
    UserMessageDeclarationTests, ContractDiffTests:577, MessageOrderTests,
    the native usermessage_* tests) are rewritten to pin key order, never
    deleted.

### 2.2 The message table: string ids, scoped by module, with index = engine id

- **Ids are strings.** A message declares `[UserMessage("notice")]`; any
  printable ASCII is allowed, with no `:` and no whitespace. An id only has to
  be unique **within its module**: a duplicate in one module refuses to start
  and names both classes, and a Shared.Tests fact checks every module.
- **The table key is `<module>:<id>`.**
  - The module is the assembly name **with the side suffix removed**:
    SourceSharp.Server and SourceSharp.Client are both `SourceSharp`, and
    TF2.Descent.Server/Client are both `TF2.Descent`.
  - This is how the ruling's "prefixed with the assembly name" survives the
    fold: the same shared file compiles into two assemblies, so the raw
    assembly names would never match across sides.
  - Side assemblies must be named `<Module>.Server` and `<Module>.Client`. A
    load-time check enforces it, and mods.yaml already lists them that way.
  - So the TF2 Descent notice is `TF2.Descent:notice`.
- **Stock messages keep their bare names** (`Shake`, `Fade`, `VGUIMenu`, …)
  and are marked `[UserMessage("Shake", Stock = true)]`. Their name is part of
  the native contract, because Valve code still hooks and sends by that name. Stock names are one global namespace;
  the registry refuses a stock name declared twice, or one not on the pinned
  list of stock names.
- **Server.**
  - At DLL init it registers every message with the engine in key order.
    Each message's engine id is its position in that order.
  - In `CreateNetworkStringTables` it creates the networked string table
    `SsUserMessages` and writes the key for engine id *i* at index *i*.
    Its max entries is a power of two ≥ 256 *(measure: the engine's
    constraint)*.
  - The table is static for the game: all messages register at DLL init, and
    a registration after the table is created is an error, never a race.
    Later table updates ride the unreliable snapshot, while messages are
    reliable.
  - Keys are at most 255 bytes, checked at registration (under `GetString`'s
    260).
- **Client.**
  - After sign-on it reads `SsUserMessages` and builds, in C#, a map from
    engine id (the table index) through the key to its handler (§2.2a).
  - A key the client has no declaration for is logged once and its messages
    dropped.
- **Payloads are unchanged**: there is no carrier message and no extra byte,
  so the 255-byte payload limit stays.
- **Demos are out of scope** (user, 2026-10-01). `GetUserMessageInfo`
  answers from the managed registry (§2.2a) only for the engine's own
  description and size checks.

### 2.2a User messages move into managed code (user, 2026-10-01)

**All user-message logic moves to C#**: the registry, id assignment, the
table, send, dispatch and hook bookkeeping. Native code keeps only the
engine and Valve-code entry points, each forwarding to managed in one call,
and the managed code calls back into native where it must.

| native today | becomes |
|---|---|
| `CUserMessages` registry (by name, 64 cap, insertion index) | a managed registry. No cap beyond the engine's one-byte id (255) |
| `IServerGameDLL::GetUserMessageInfo` (shell_gamedll.cpp) | a vtable slot forwarding to the managed registry |
| server send: `LookupUserMessage`, `UserMessageBegin`, the bf_write helpers (usermessage_write.h) | managed: C# looks the id up and writes the payload, then calls the engine's `UserMessageBegin`/`MessageEnd` through the bound engine-server interface |
| the 3 Valve-code senders by literal name (CloseCaption, Shake, Fade) | unchanged Valve call sites; the native `usermessages` lookup they reach forwards to managed |
| `IBaseClientDLL::DispatchUserMessage` → `CUserMessages::DispatchUserMessage` → 64 per-index trampolines | one forward: the native slot passes (id, bf_read) to managed. C# maps id → key → handler. The trampoline table goes |
| `HOOK_MESSAGE` from Valve client code (the 8 stock hooks, registered before `gHUD.Init`) | native `usermessages->HookMessage(name, fn)` forwards (name, fn) to managed. Managed stores the function pointer by key and **calls back into native** to run it with the payload |

Rules:
- **Native makes no decision** (CLAUDE.md porting rule). Each forwarder does
  one call and returns.
- **The new managed→native surface is a bound interface**: an `IShimUserMessages`
  area on the bound-vtable pattern, with a `chain=` row in tools/vtable-layout,
  carrying engine `UserMessageBegin`/`MessageEnd` and "invoke native hook
  (fn, bf_read)". No free `SourceSharp_*` functions.
- **Native→managed** goes through the side's existing managed root.
- **Tests:**
  - The native usermessage_* gtests become xUnit facts over the managed
    registry, writer and dispatch. One behaviour per fact; the behaviours the
    gtests pinned are carried over and listed before and after.
  - A gtest remains only for each forwarder, showing it reaches managed, with
    its fail-first arm.
  - The haptics-order gtests and their oracle test go: with demos out of
    scope and ids carried by the table, there's no order for them to pin.
- **The evicted native files** are recorded in docs/server_evictions.md and
  client_evictions.md. Their `sdk-links` lines go, and the
  `GC_SECTIONS=0` build is run after the eviction.

### 2.3 Abi per side: the host shares no managed type (review answer 1)

**Two contexts per side.**
- **The side root**: a non-collectible context per side, created at the
  side's first load and kept for the process. It holds that side's copy of
  Abi, and with it the side's `ManagedVtables` registry.
  - The registry has to outlive reloads: native code holds the `IManaged*`
    block pointers for the whole process, and the engineapi lane's L4
    (plan_managed_vtable_reload, approved) patches those same blocks in place
    on each reload.
- **The game context**: the existing collectible context (the side's game
  assemblies, mods and DevApi). It unloads and reloads as today. Its `Load`
  hands back **its own side root's** Abi, where today it hands back the
  default context's.
- So the two sides share no managed type, and within one side Abi keeps one
  identity across reloads.

**The boundary becomes raw pointers.**
- `SourceSharp.Bootstrap` stops referencing Abi.
- `LoadSide(side, …)` keeps its native signature. It creates or reuses the
  side root, creates the game context, and loads the side's assembly.
- It finds the side's `[UnmanagedCallersOnly]` entry by a fixed name (e.g.
  `SourceSharp.Server.SideEntryPoint.Load`) and calls it through a function
  pointer, passing the native arguments through unchanged.
- No managed object crosses the boundary in either direction.

**`ManagedApi` is filled by the side.**
- The 42 thunks in `Entry.cs` that dispatch through `s_Server`/`s_Client` move
  into each side, as its own static entry points over its own module.
- `EngineApi` and `ManagedApi` stay blittable structs that each Abi copy
  declares. Their layout is pinned by the existing parity tests (AbiParityTests
  and the tools/vtable-layout rows), now run against both side assemblies.

**What per-side Abi changes:**
- **`ActiveSide` and `SideEntry.Enter` lose their reason.**
  - Each Abi copy knows its side for the life of its ALC.
  - The thread-static switch, and the class of bug it guards against (a server
    call run as the client because a client callback ran last), go away.
  - This retires the engineapi lane's Phase E work early, so the two must be
    coordinated (§4, R6).
- **Abi still needs a process restart to change**, as today: each side root
  keeps its Abi for the process. `WarnIfTheAbiMoved` and `StampTheAbiOnce`
  stay, applied per side root.
- **The `ManagedVtables` registry moves from Bootstrap's Entry.cs into each
  side root's Abi.** Its lifetime, the process, is unchanged.
- **The crossing counters become per side** (`cs_sv_crossings` and
  `cs_cl_crossings` each report their own side). Today's single table was a
  by-product of the shared Abi (Entry.cs:340). If the combined view is wanted,
  the native side sums the two.
- **Statics become per side.** P0(3) lists every Abi static, and for each one
  either confirms that one copy per side is correct, or names it as state the
  two sides really did share (expect none).

**Unload:**
- `UnloadSide` calls the side's own unload entry, then unloads the game
  context. The side root stays.
- The existing unload facts (the game context is collected, with no foreign
  references left) still hold. A new fact shows that the side root's Abi, and
  its registry, keep their identity across a reload.

### 2.4 Folding the shared projects

- **Mods first** (small): TF2.Shared goes into TF2.Server and TF2.Client, and
  TF2.Descent.Shared into TF2.Descent.Server and TF2.Descent.Client.
  - The shared folders stay as source folders, compiled by each side's csproj
    (`<Compile Include="..\shared\**\*.cs" />`).
  - `[SharedAssembly]` goes: §2.1 has no shared assembly to name.
- **Core** (large): SourceSharp.Shared goes into SourceSharp.Server and
  SourceSharp.Client. **This waits for the engineapi lane's L3** (review
  answer 3), which moves `ManagedVtable.cs` into Abi, so the file moves once.
  - `ManagedRoot` and `ManagedBindings` stay in the shared folder.
  - Client gains the generators analyzer.
- **Path tables follow the move**: tools/shim-facade-audit,
  tools/module-map.tsv, tools/move-classify, tools/valve-text-exempt.tsv,
  SourceSharp.slnx, and the ContractDiffTests needles.

### 2.5 Tests by side

| project | references | holds |
|---|---|---|
| SourceSharp.Shared.Tests | Abi, and **compiles the shared files itself**; loads Server/Client by path for the cross-side facts | shared code's facts; §2.1/§2.2 cross-side list identity; §2.3 boundary layout parity on both sides |
| SourceSharp.Server.Tests | Server (its own Abi) | server facts |
| SourceSharp.Client.Tests | Client (its own Abi) | client facts |
| TF2.Descent.Tests | split the same way (its 3 both-side files go to path-loading facts) | — |

- **The split is mechanical but large** (457 files). It's scripted, with
  per-file fact and assert counts compared before and after (CLAUDE.md's rule
  for scripted edits). Facts that read both sides become path-loading facts
  in Shared.Tests.
- **Tests that use `ActiveSide`** (TestServices, CoreTests and others) lose the
  switch: each test project is one side.
- **Legs**: `make test` gets one leg per project, each floored by
  tools/leg-floor. The managed total is reconciled against today's single leg
  (6,752 at 14b8728) to the fact.
- **OracleTests** (12 of 21 files name Shared) splits the same way, or
  references only the side it checks.

### 2.6 The dev API, independent

- **`SourceSharp.DevApi.Contracts`** (new, no dependencies) holds `IDevApi`,
  the side's half the dev API calls (status, log, entities, commands,
  console), and the debug-channel protocol types now in Shared
  (`DebugChannelProtocol`, `DebugChannelCodec` and friends).
- **`SourceSharp.DevApi`** references only its contracts. Its direct Abi use
  (`DevEngine` over `EngineServices.Defaults`, `LocalCommandLine`) moves
  behind the contract, implemented by each side. *(measure: DevApi's Abi call
  sites, which size that phase.)*
- **Server and Client** reference the contracts, implement the side half, and
  still load `SourceSharp.DevApi` by name, only when asked.
  - With per-side Abi, DevApi itself loads into the side's ALC that asked for
    it, so it shares nothing across sides either.
- **A closure fact**: DevApi references nothing but its contracts and the
  framework. Its fail-first arm plants a reference and shows it reported.

## 3. Phases

Each phase is committed step by step, and `make check` stays green
throughout. The live gate (with its second map load) runs for every runtime
change, and every check, gate and live run is gated on `tools/box-ready`.

| # | phase | commits | gate |
|---|---|---|---|
| P0 | probes | (1) every native caller of the user-message code (Valve call sites of `usermessages->`, `HOOK_MESSAGE`, `UserMessageBegin`), with each one's forwarder; (2) the engine's max-entries constraint for a table; (3) every Abi static with its owner, plus every Shared type named by Bootstrap, native code or tools; (4) DevApi's Abi call sites; (5) every consumer reading a protocol list by position, and today's orders recorded as the baseline | a findings table in §7 |
| P1 | key order | §2.1 for all five lists; side-only protocol entries refused; cross-side identity facts; the order tests re-pinned | `make check`; live gate (the order is runtime) |
| P2 | user messages in managed code | §2.2a: the managed registry and `GetUserMessageInfo`; managed send through `IShimUserMessages`; managed dispatch and native-hook callbacks; native files evicted; gtests carried over as facts | `make check`; `make debug GC_SECTIONS=0`; live gate; a joined client sees Shake/Fade and a HUD message |
| P2b | the message table | string ids, module-scoped keys, the stock list; `SsUserMessages` written by the server; the client's id → key map; the late-registration refusal | `make check`; live gate; a joined client receives a `TF2.Descent:notice` and a Shake/Fade (via the joined-client lease) |
| P3 | mod shared folds | TF2.Shared and TF2.Descent.Shared into their side projects; `[SharedAssembly]` retired | `make check`; tf2 and tf2_descent boot as dedicated servers |
| P4 | dev API independence | DevApi.Contracts; DevApi off Abi, Server and Shared; the closure fact | `make check`; the dev API serves a live game on both sides |
| P5 | Abi per side | Bootstrap off Abi; side entry points and `ManagedApi` filled by the side; layout parity on both sides; per-side unload facts; `ActiveSide` and `SideEntry` deleted (Phase E left them to this plan) | `make check`; live gate ×2; a listen server (both sides in one process) runs the gate's map change and reload; unload collects both game contexts while both side roots keep their Abi and registry |
| P6 | test split | Shared.Tests, Server.Tests, Client.Tests (plus Oracle and TF2.Descent), counts reconciled | `make check` with the new legs; totals reconciled |
| P7 | the core Shared fold | **after the engineapi lane's L3 (it messages when committed)**: Shared into Server and Client, Client's analyzer, path tables | `make check`; live gate ×2; a joined client |

P1–P4 touch none of the engineapi lane's files. **P5 waits for that lane's L6**, because it rewrites Entry.cs's reload path and moves the `ManagedVtables` registry from Bootstrap into each side root. P5 deletes `ActiveSide` and `SideEntry`: the engineapi lane's Phase E is complete and leaves them to this plan. P7 runs after L6 too; ManagedRoot.cs keeps its path, and only the assembly it builds into changes.

P6 follows P5, because one side per test project is only true once Abi is
per side.

## 4. Risks

| # | risk | retired by |
|---|---|---|
| R1 | a protocol list orders differently on the two sides after a fold: silent wrong decoding | P1's cross-side identity facts, before any fold |
| R2 | an engine or native consumer reads a list by position, and key order moves it | P0(5) lists them; each is carried or ruled before P1 lands |
| R3 | natively hooked stock messages lose their hooks once dispatch is managed | P0(1) lists every hook; P2's joined client exercises Shake/Fade |
| R4 | a message registered after the table is sent arrives before its entry | the late-registration refusal (P2) |
| R5 | an Abi static held state the sides really did share | P0(3) lists every one before P5 |
| R6 | P5 collides with the engineapi lane's in-flight Abi work (Phase E, `ActiveSide`) | P5 is planned with that lane; it either lands the deletion or rebases onto theirs |
| R7 | the test split drops facts silently | per-file fact and assert counts compared before and after; floors per leg |
| R8 | the core fold collides with the engineapi lane's Shared edits | P7 is sequenced after their L3 |

## 5. Rulings (2026-10-01)

1. Abi is per side, not a shared contract (§2.3). Each side's Abi lives in a non-collectible side root that outlives the side's reloads, because the engineapi lane's approved reload plan patches native blocks in place.
2. Every list is in key order; no stock prefix is pinned (§2.1).
3. The core fold waits for the engineapi lane's L3 (§2.4, P7).
4. Message ids are strings, unique within their assembly; the table prefixes
   them with the assembly name (§2.2). The plan reads "assembly name" as the
   module name: the assembly name without its `.Server`/`.Client` suffix,
   because one shared file compiles into both side assemblies. **Accepted:
   short names** (`TF2.Descent:notice`).
5. Demos are out of scope.
6. All user-message logic moves into managed code, calling back into native
   where it must (§2.2a).
7. Start now, with the library cull partly merged at 3662e01 (user, 2026-10-01); server-novalve's remaining work stays off this plan's files.
8. DevApi.Server and DevApi.Client (§8).
9. **Original start gate, superseded by 7:** no phase starts until the engineapi-vtables lane (through
   L6) and the library cull have landed and merged into main. P0 re-measures
   at that main.

## 7. P0 findings (2026-10-01, at 41b31d0 = main 3662e01 + this plan)

Read-only probes; the detail is in the session's scratch notes. Corrections to §1:

| # | finding | consequence |
|---|---|---|
| F1 | native user-message code is 1,162 lines; **6** stock messages are hooked natively in compiled code (VGUIMenu, Rumble, Shake, Fade, VoiceMask, RequestState), not 8: SavedConvar is hooked only in uncompiled gamerules.cpp, AchievementEvent nowhere; `CUserMessageRegister::RegisterAll` walks an empty list | §2.2a forwards `HookMessage` for 6 names; the stock list stays as declared |
| F2 | native senders: 4 Begin/End paths: the managed path (`SourceSharp_SendUserMessage`), Shake and Fade (shell_vieweffects.cpp, reached from managed ScreenShake/ScreenFade rows), CloseCaption (sound_emitter.cpp) | Shake/Fade/CloseCaption payloads move to managed writers; their native callers become forwarders. The 5 behavioural write gtests (bit-identical payloads) become facts over the managed writer |
| F3 | managed reaches the registry through 3 rows (RegisterUserMessage, SendUserMessage on `IShimEvents`/`IShimEventsServer`; OnUserMessage on ManagedApi, `CrossingOp` 76); no P/Invoke by name | `IShimUserMessages` replaces the two shim rows; the client's OnUserMessage stays the one native→managed forward |
| F4 | no protocol list needs an engine or stock position; every positional consumer is satisfied by both sides agreeing (temp-ent wire ids come from `ServerClass`'s alphabetical list; net states key by hash; layouts are 0 in production) | §2.1 needs no exception. Overflow tie-breaks at each cap are the only order effect |
| F5 | hot reload: native registration is idempotent by name and keeps old indices, so today a mid-list addition on reload misroutes | under §2.2 ids are assigned by key and kept in the side root's registry across reloads; a key new after table creation is refused until restart (a fact covers both) |
| F6 | `MAX_TABLES` 32 also counts the engine's own tables (~9-10, unverified count), plus 7 stock + GameRules + SourceSharpManaged; power-of-two max entries unverified (engine source absent) | `SsUserMessages` costs one table; P2b measures the live table count and uses a power-of-two size |
| F7 | **Bootstrap writes Abi statics on the sides' behalf**: `Engine.SetActiveSide` (33 sites), `Engine.Initialize`/`AttachShims`/`ManagedDirectory`, movement rules, `StandaloneMirror`, `StandaloneNetStates` field reads, `BuildInfo`, 42 `Crossings.Count`, 20 `Log` calls; 43 Abi types used (14 value types, 4 interfaces, 25 classes) | P5 is a move of Entry.cs's dispatch logic (~1,700 lines) into each side, not a boundary tweak. Bootstrap keeps only ALC management, the stamp hashing and the pointer hand-off |
| F8 | **four genuinely cross-side reads**: `Crossings` (one table by design), `BuildInfo` (client HUD and DevApi read the server's), `PredictionErrors.Ring` (client writes, DevApi on the server reads), `Engine.LogSink` (DevApi sees client log lines) | needs a route that isn't a shared Abi: see §8 |
| F9 | 99 `StaticStateWaivers` rows name SourceSharp.Abi (EntryThunks 27, EngineApiLane 13, …, CrossSide 7) | the CrossSide 7 are F8's; each waiver is re-ruled in P5 |
| F10 | DevApi: references Abi + Server; ~55 Abi types, `IDevApi` from Server, 7 Shared types (DebugChannel*, LocalCommandLine); loaded by name into the server's ALC only, when `-debugapi` is given | §2.6's contracts carry the debug-channel types and the side reads |

## 8. Open after P0: the dev API's cross-side view

F8's reads exist so one dev API sees both sides. Under isolation:

**Ruled (user, 2026-10-01): "There should be a DevApi.Server and DevApi.Client".**
Each side loads its own dev API assembly (`SourceSharp.DevApi.Server`,
`SourceSharp.DevApi.Client`) into its own game context, each built from the
dev API's shared source plus `SourceSharp.DevApi.Contracts` and referencing no
side assembly. Each serves its own socket. The cross-side views (one
crossings table, client prediction against server state, both sides' logs)
are merged by the caller, `tools/debug-api`, which talks to both sockets. The
client HUD shows its own side's build stamp, and the server's arrives with
the sign-on (P4 decides the carrier). The options considered:

- **(a) One dev API per process, beside the sides**. `SourceSharp.DevApi` and `SourceSharp.DevApi.Contracts` load once in a context of their own. Each side registers its implementation of the contracts (status, log, crossings, build stamp, prediction errors) at load, and unregisters at unload. The contracts hold no game types, so nothing of either side is shared; the only shared assembly is the dependency-free contract.
- **(b) One dev API per side**: fully isolated, but two sockets, and the cross-side views (client prediction against server state, one crossings table) are gone or have to be merged by the caller.

The client HUD's server build stamp travels the same way as the dev API in (a), or over the network in (b).
10. **DevApi.Server/Client may reference SourceSharp.Abi** (each side's own
    copy) plus SourceSharp.DevApi.Contracts, and nothing else of the game.
    Dev-API-only types (IDevApi, the debug-channel protocol, the log sink)
    leave Abi, Server and Shared for the contracts (user, 2026-10-01).
11. **One socket per process: the server's.** The client's dev API connects to
    the server's dev-API listener, and the server proxies every client request
    over its own unix socket (user, 2026-10-01). tools/debug-api, the browser
    proxy and the live gate keep talking to one socket; a request for client
    state is forwarded to the connected client.
12. **Client routes are per client: `/client/{index}/...` on the server**
    (user, 2026-10-01), where the index is the player's slot. The server binds
    each debug-channel session to a slot through the nonce it sent that player:
    the invitation goes to one slot, and redeeming its nonce ties the session to
    it, so the slot is never the client's own claim. `/clients` lists the slots
    with a session; a slot with none answers 404.

## 9. P7 measured (2026-10-04, at c7479536 on engineapi-vtables, debug)

Measure-and-propose only; no file has moved. Instruments:

- **metadata**: a System.Reflection.Metadata dump of each built DLL in
  `bin/obj/<project>/Debug/`: its TypeRefs resolving to `SourceSharp.Shared`,
  the MemberRefs on them, and its own types deriving from or implementing one.
- **trial fold**: a throwaway edit of the csprojs only, compiled and then
  restored from a `cp` snapshot (the tree is clean again, and SourceSharp.Server
  rebuilt to its 884 top-level types). It compiled `src/shared/managed/**` into
  Server and Client, moved the 11 files of option (a) below into Shared.Interop,
  and dropped every reference to SourceSharp.Shared.

### 9.1 What each Interop assembly uses from Shared *(metadata)*

| assembly | types | member refs | implements |
|---|---|---|---|
| SourceSharp.Server.Interop | 8 | 18 | 1 (`UserMessageOutbox : IUserMessageOutbox`) |
| SourceSharp.Client.Interop | 7 | 16 | 1 (`UserMessageInbox : IUserMessageInbox`) |

That's 13 distinct types; `IConVarHost` and `UserCmdCodec` are used by both
Interop assemblies. All of them are in namespace `SourceSharp.Abi`.

| type (file, lines) | used by (member) | proposal |
|---|---|---|
| `DecodedUserCmd` (23), `UserCmdCodec` (171) | Server.Interop PlayerCommandQueue, PlayerCommandsBinding (`Decode`, `Span<DecodedUserCmd>`); Client.Interop DiagnosticsClientBinding (`Md5PseudoRandom`) | **(a)**. The usercmd wire codec depends only on Abi's `UserCmd`, and no game file uses it. |
| `IConVarHost` (10) | CommandsBinding, CommandsClientBinding (ctor) | **(a)**: a contract. |
| `IUserMessageOutbox` (in UserMessageSender.cs) | Server.Interop `UserMessageOutbox` implements it | **(a)**: split the interface out; `UserMessageSender` stays shared. |
| `IUserMessageInbox` (in UserMessageReceiver.cs) | Client.Interop `UserMessageInbox` implements it | **(a)**: split the interface out. |
| `UserMessageRegistry`, `UserMessageDeclaration` (UserMessage.cs, 784) | Server.Interop UserMessagesBinding (`At`, `Name`, `Size`) | **(c)**: a catalog interface in Shared.Interop (index → name, size), implemented by the registry. |
| `UserMessageReceiver` (153) | Client.Interop UserMessagesClientBinding (`Dispatch`) | **(c)**: a dispatch interface in Shared.Interop, implemented by the receiver. |
| `StringTableRegistry`, `StringTableDeclaration` (252) | Server.Interop StringTableCreation (`Declarations`, `Tables`, `Log`, `Name`, `MaxEntries`, `Contract`) | **(b)**: StringTableCreation (52 lines, no unsafe; its one caller is ServerGameDll) moves up into Server. That retires Shared's InternalsVisibleTo grant to Server.Interop. |
| `Angles`, `Angles.Basis` (103) | Client.Interop ThirdPersonCamera (`Vectors`, `Forward/Right/Up`), which PredictionClientBinding and the generated areas call | **(a)**: pure math with no Shared dependencies. (b) is not available: the camera's callers are themselves interop. |
| `TempEntReader` (286) | Client.Interop TempEntDeclarationReader (9 read members) | **(a)**: the reader over the engine's temp-ent payload, with no Shared dependencies. The alternative is (b): the declaration reader (293 lines, called only from Client) moves up, and its one unsafe method, `InlineVectors`, becomes a span helper left in Interop. |

- **Closure, from the trial fold.** All-(a) (the 11 files: the two codec
  files, IConVarHost, Angles, TempEntReader, StringTableRegistry and the 5
  user-message files, 1,958 lines) compiled in Shared.Interop against Abi and
  the generators alone. Server.Interop, Client.Interop and Server then built
  with 0 errors and 0 warnings. So the set above is closed, and the mixed
  (a)/(b)/(c) plan moves strictly less.
- **The cost of (a).** Shared.Interop allows unsafe code, so whatever moves
  there leaves the safe game assemblies. That's why the two registries take
  (c), not (a): about 1,100 lines of registry logic stay safe, and only two
  interfaces move.

### 9.2 Everything else that names Shared *(metadata over all 24 built assemblies)*

| assembly | types / member refs | after the fold |
|---|---|---|
| SourceSharp.Server, SourceSharp.Client | 166 / 868, 177 / 852 | the shared files compile in; the trial built both with 0 errors |
| TF2.Descent.Server, TF2.Descent.Client | 13 / 24, 12 / 22 | resolve through SourceSharp.Server and SourceSharp.Client (the trial built both); the `using SourceSharp.Shared;` namespace still exists |
| TF2.Server, TF2.Client | 0 / 0 | the ProjectReference is unused: drop it |
| SourceSharp.MapGen | 1 type (`EntityNames`), through Server | unchanged |
| Bootstrap, Abi, Shared.Interop, Server/Client.Interface, Debug.Api.{Server,Client,Shared}, Mods, TestSupport, TF2.Descent.Rules | 0 | unchanged, so Debug.Api's hooks are untouched |
| SourceSharp.Tests, OracleTests, TF2.Descent.Tests | 260, 53, 12 types | see §9.4 |

**Grants and analyzers:**

- **Generators.** SourceSharp.Client gains the analyzer reference. The trial
  compiled Client with it: 0 errors, no duplicate generated members.
- **InternalsVisibleTo naming SourceSharp.Shared.**
  - Abi's `AssemblyInfo.cs:38` grant becomes dead. Client compiled without an
    Abi grant, so the shared code uses no Abi internals. Delete it.
  - Shared.Interop's grant to Shared becomes dead. Shared.Interop must also
    **grant SourceSharp.Client**: without it the trial failed 12 times on
    `NetState.Blob` (NetArray.cs, NetStateTransfer.cs).
  - Shared.csproj's own grants move to the side csprojs: Tests and
    TF2.Descent.Tests on both, OracleTests on Server. Server.Interop's grant
    goes with (b).
- **mods.yaml** lists only side assemblies (`SourceSharp.Server`,
  `SourceSharp.Client`, plus the mods'). Shared was loaded as a dependency, so
  **no change**.
- **The build stamp.** Each side's `build: managed … (N assemblies)` line
  (SideLoad.cs:306) should fall by exactly 1. That's the gate's
  assembly-count check.
- **Deploys.** A stale `SourceSharp.Shared.dll` stays in
  `bin/<config>/bin/managed` until `make setup` or a clean: nothing loads it,
  but the step should remove it.
- **Path tables.** The files keep `src/shared/managed`, so
  tools/module-map.tsv, move-classify and valve-text-exempt.tsv are
  path-keyed and unchanged; the farm still links the old
  `src/sourcesharp/managed/SourceSharp.Shared/` view.
  - SourceSharp.slnx drops one row.
  - DebugApiClosureTests.cs:73 drops its `SourceSharp.Shared` row.
  - tools/shim-facade-audit names the assembly (`roots=('SourceSharp.Client',
    'SourceSharp.Shared')`), but **it is already dead at this base**: it reads
    `ROOT/src/sourcesharp/...` from the repo root, raises FileNotFoundError, and
    nothing runs it. That's out of P7's scope, recorded here.

### 9.3 The runtime anchor: "the shared assembly" becomes the side's own

- **Core reads the shared assembly through `typeof(SharedInfo).Assembly`**: 9
  sites in ServerModule, 6 in ClientModule. They anchor the user-message,
  string-table, temp-ent, game-event, layout and net-state registries, and
  `ProtocolManifest.Of`.
- **After the fold that expression is SourceSharp.Server (or .Client).** No
  compile error marks the change.
- **Scanning and the wire are unchanged:**
  - Discovery spans every loaded assembly, and P1 made every list key-ordered.
  - `ModuleOf` maps `.Server`, `.Client` and `.Shared` alike to `SourceSharp`,
    so user-message keys don't move.
- **One check is lost: `WarnIfNotShared`.** It can no longer see a core
  protocol declaration that only one side has, because that declaration now
  sits in "the shared assembly".
- **Proposed replacement: a build-time fact over the two built side
  assemblies.**
  - The rule: every `[UserMessage]`, `[StringTable]`, temp-ent,
    `[StandaloneNetState]` and layout declaration in SourceSharp.Server has a
    same-full-name twin in SourceSharp.Client, and vice versa.
  - **The shared set is the full-name intersection of Server and Client.**
    Today that intersection is **0** (and Shared ∩ Server = Shared ∩ Client =
    0), so after the fold it is exactly the folded files.
  - A fail-first arm adds one side-only declaration and shows it reported.
  - The join-time ProtocolManifest comparison stays as the runtime net, as it
    already is for the mods (P3).
- **SideAuditTests.SharedAssemblyLeansOnlyOnSlotsBothSidesFill** audits the
  Shared DLL today. It becomes the same audit (BothFill) over the
  intersection's types in each side DLL. Its fail-first arm is a shared file
  calling a server-only slot.

### 9.4 The test projects: the hard part *(trial fold, then a scripted alias pass)*

- **The precedent.** TF2.Descent.Tests already aliases its Client reference
  (`Aliases="client"`, P3).
- **Applied to the three test projects.** Shared's types then resolve to
  Server's copy. Every Client-only type needs `client::`, and every value that
  crosses into a Client API needs the Client copy.

| pass | SourceSharp.Tests | OracleTests | TF2.Descent.Tests |
|---|---|---|---|
| fold, alias `client` | 239 errors / 69 files (CS0246, CS0234: Client names) | 2 / 1 | 7 / 3 (CS0433: SourceSharp.Client arrives transitively, unaliased) |
| `using client::…` and type aliases on those files | 236 errors / 57 files: the compiler's next layer | 1 | 2 |
| of which cross-side identity (CS1503, CS0029, CS0115, CS0535) | **56 in 18 files** (51 CS1503, 5 CS0029; plus 1 CS0115 fixed in the pass above): `SavedEntity`, `HudMessageArgs`, `DamageMessage`/`IUserMessageEvent<>`, `PredictionComponent`, `AnimationEventExpectation`, `ColorCorrectionVolumeNetState` | 0 | 1 (`HudMessageArgs`) |

- **The pass count is unknown.** Errors surface in layers, so the true number
  of passes isn't known until the last one compiles.
- **A token classification** of the 535 SourceSharp.Tests files (simple type
  names against the side-only sets; approximate):
  - 248 name only server types;
  - 59 name only client types;
  - **69 name both**;
  - 159 name neither.
  - OracleTests: 4 / 0 / 2 / 17. TF2.Descent.Tests: 2 / 1 / 1 / 9.
- **A silent drift with no compile error.** 58 `typeof(SharedInfo).Assembly`
  sites in 32 test files become "SourceSharp.Server". Registries the tests seed
  from "the shared assembly" would then also pick up server-only declarations.
- **Facts that name the Shared assembly need rewriting:**
  - LoadedAssemblyTests: 3 facts;
  - BootstrapLoadTests:58;
  - ReloadTests:227;
  - UnloadTests:54 and :138;
  - GameAssembliesAreSafeTests: the csproj row and the `WeaponScriptDatabase`
    probe;
  - SideAuditTests:945.

  UserMessageKeyTests' `SourceSharp.Shared` rows are `ModuleOf` inputs, and
  stay.

### 9.5 Proposal

1. **P7a, now, with no fold: the Interop decoupling (§9.1).**
   - Move the (a) types to Shared.Interop: the usercmd codec, IConVarHost, the
     inbox and outbox interfaces, Angles and TempEntReader.
   - Add the (c) catalog and dispatch interfaces.
   - Move StringTableCreation up into Server.
   - Drop the Interop assemblies' Shared references.
   - Behaviour-neutral, so `make check` is the gate. Afterwards the metadata
     dump shows 0 Shared refs from both Interop assemblies; that's the fact,
     with a fail-first arm.
2. **P7b: the fold itself.**
   - Compile in the shared files, add the Client analyzer and the
     InternalsVisibleTo moves (§9.2).
   - Add the side-agreement fact that replaces WarnIfNotShared (§9.3).
   - Drop the Shared references from the TF2 mods.
   - **A clean answer needs P6's project split in the same step.**
     - The interim alternative is aliasing one test project. At least 130
       file-level edits across two passes, plus the 18 identity files and an
       unknown further layer, all for references P6 then deletes.
     - It also leaves the 58 anchor sites meaning something else without a
       compile error.
   - So P7b lands with P6's split, and does not need P5 for it:
     - SourceSharp.Server.Tests references Server only. It takes the 248
       server-only files and the shared-only part of "neither".
     - SourceSharp.Client.Tests references Client only. It takes the 59
       client-only files.
     - **The 69 "both" files** go to a cross-side project that aliases Client
       (the TF2.Descent.Tests precedent) or loads by path (§2.5).
     - Every anchor site is re-pointed per project.
     - Per-file fact and assert counts are compared before and after (R7).
   - P6's own remainder, `ActiveSide` going from the tests, still waits for
     P5.
3. **If the integrator wants P7b without P6:** the interim is the
   SourceSharp.Tests alias, with TF2.Descent.Tests also aliasing its transitive
   `SourceSharp.Client`. Its size is the table in §9.4, and every
   `typeof(SharedInfo).Assembly` site must be reviewed by hand.

### 9.6 Done (2026-10-04, si-p7)

The integrator's rulings: P7a with the mixed plan; P7b with P6's split, in
this lane; delete tools/shim-facade-audit.

| commit | what |
|---|---|
| 0d3d8c81 | P7a: the Interop assemblies reference no SourceSharp.Shared. (a) codec, IConVarHost, inbox/outbox, Angles, TempEntReader moved down; (c) IUserMessageCatalog and IUserMessageDispatch; (b) StringTableCreation moved up. Metadata fact with a planted control |
| da5f6bfd, c80cb7d8, a14b58b8 | P6 split, made while Shared was still an assembly: SourceSharp.Server.Tests (386 files, server only), SourceSharp.Client.Tests (65, client only), SourceSharp.Shared.Tests (58, the files that name both sides), fact-less helpers in src/test/common. Three make test legs, each floored |
| 47d50dcb | P7b fold: Shared is a source folder; the anchors are each side's Game assembly; LoadedAssemblies keeps a side's scan to its side; WarnIfNotShared is replaced by ProtocolSetTests; the shared-source audit compiles the folder on its own |
| 26771eab | tools/shim-facade-audit deleted |

**Reconciled by test name** (trx, against 0d3d8c81's single leg):

- 7772 became 7774.
- 8 facts were deleted: the OtherModulesSide exemption's 7 rows and the
  DebugApiClosure row for Shared.
- 10 facts were added: 7 OtherSideSuffix rows and 3 ProtocolSet facts.
- No outcome changed.

**The gate's results:**

- `make check`: all 10 legs passed.
- **The live gate, four runs at the head** (26771eab):
  - Runs 1 and 2 failed `push` in pass 2 only: the crate moved 1.9 and 3.5
    units, below the step's 4.0.
  - Runs 3 and 4 passed, all 29 steps on both passes.
  - At c7479536 and at 0d3d8c81, one run each passed.
  - **Whether the fold raises the push failure rate is not settled.** Other
    lanes' logs show 9 of 158 push samples below 4.0. This branch shows 2 of
    8. If that 5.7% rate held here, two or more failures in eight would happen
    with P ≈ 0.07. The managed members the two builds generate are identical,
    apart from the planned moves.
- **Headless reload**: `cs_cl_reload` and `cs_sv_reload` twice each. Each
  printed "reload complete" (472 server and 41 client entities) and
  "re-attached in place", with 0 EXCEPTION. The HUD painted after each reload.
- **Build stamp**, headless, same mode: the base gives Server 8 and Client 6;
  the head gives Server 7 and Client 5. That is −1 per side, as §9.2 predicted.
