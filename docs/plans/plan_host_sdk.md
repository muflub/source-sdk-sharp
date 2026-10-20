# plan_host_sdk.md: the Descent mod, hosted through Host.Sdk

Written 2026-09-30 against this repo's `c2315b3` (main after server-novalve's
fast-forward; engineapi-vtables' last 6 commits, up to 43d89be, are not in it
yet) and source-sdk-sharp-host's `057b7a3` (packaging fixed at `a0e0810`), which commits the spec this
implements: `docs/sdk-integration.md` (below, "the spec"). Facts marked
*(read)* come from reading code; *(measure)* ones are open until the phase that
owns them runs its probe. Untracked until reviewed.

## The goal, as ruled

The user's rulings (2026-09-30):

- **Scope: lifecycle and players.** Implement spec §1–6 and §8–9 fully: the
  packages, the rules module, start/pump/stop, Booting/MapReady, joins with
  character leases and checkpoints, and every service command (PrepareHop,
  Retry, Kick, Drain, Shutdown, Say, Exec). Spec §7 (items, kills, travel)
  gets **console-command hooks only**, enough to drive the spec's done-criteria
  1–4 by hand or by a script. No new gameplay: no drops, inventory UI, corpses
  or stairs.
- **A `descent` mod in `game/mods.yaml`**, plus everything around it that a
  pod needs to run it: content dir, gameinfo, the dedicated engine entry, the
  assemblies, and the build and deploy of the SDK packages.
- **Four Descent projects**: Rules, Shared, Server and Client. The user added
  Shared and Client on 2026-09-30.
- **Descent.\* classes reach the core only by constructor injection** (the
  user, 2026-09-30). That covers SourceSharp.Abi, .Shared, .Server and
  .Client:
  - no static `Log`, no `Engine` or ABI tables, no `new` of a core service;
  - no BCL process statics where the core can own the seam (the environment,
    the clock).
  - **If the core lacks something Descent needs, it is added to the core**:
    generically, with its own facts, in a commit before the Descent code that
    uses it. It is never worked around inside Descent. §2.8 lists the
    additions known now.
  - A fact enforces this (§2.8).
- **The SDK pumps on its own thread** (the user, 2026-09-30). Message queues
  carry actions between that thread and the game thread (§2.9). This deviates
  from the spec's §3, "pump on the main thread", on purpose. The spec's
  hang-detection is kept by gating the pump on the game thread's liveness.
- Plan first, reviewed before any code.

Done means the spec's §10 criteria 1–4 on the host's `TIER=real` deployment,
plus the local equivalents (§5 of this plan), which run without a cluster.

---

## 1. Where things stand *(read)*

### 1.1 This repo

| question | answer | where |
|---|---|---|
| mods | one, `test`: `exe: hl2_linux64`, `assemblies.server: [SourceSharp.Server]` | `game/mods.yaml` |
| mods.yaml schema | `title, game_dir, content, steam_appid, engine{appid,exe,restart_exit_code,fd_limit}, assemblies{server,client}, managed_dir, args, env`; an unknown key is an error | `src/mods/ModsFile.cs` |
| layout | `tools/setup` lays out `bin/<config>/mods/<id>/` from `content`, and links `bin/<config>/mods.yaml` to the repo file | `tools/setup` |
| assembly load | `Entry.LoadModule` loads each listed name from `<managed_dir>/<name>.dll` **from a byte[]** into a collectible `GameLoadContext`. It requires exactly one `ISideModule` across them. Dependencies resolve by simple name from the managed dir | `src/host/managed/Entry.cs:643-697`, `GameLoadContext.cs:68` |
| DI | `ServerModule.Initialize` builds the container by hand. It is singleton-only and sealed after init. `IGameLifecycle` / `IFrameLifecycle` are found only if registered there. `[ConCommand]`, `[ConVar]` and entity classes are discovered in every assembly of the ALC | `ServerModule.cs:255`, `LifecycleDriver.cs:35`, `FrameDriver.cs:50`, `LoadedAssemblies.cs` |
| frame | `ServerModule.Frame` (level loaded) and `EngineThink` (every frame, "level or no level"; the dev API's pump) on the game thread. **No SynchronizationContext** | `ServerModule.cs:1269,1340`, `DevApiServer.cs:666` |
| connect / active | `OnClientConnect(name, address)` (can reject), `OnClientPutInServer(entIndex, name)`, `OnClientActive(entIndex, loadGame)` | `ServerModule.cs:1854,2237,2244` |
| disconnect | **no managed callback.** `ClientDisconnect` announces the standalone player's removal (managed sees the entity's `OnRemoved`), and `ClientDisconnected` in the rules is empty | `shell_gameclients.cpp:338`, `shell_gamerules.cpp:736` |
| SteamID | **not exposed.** `IVEngineServer::GetClientSteamID` (slot 110) and `GetPlayerNetworkIDString` (16) are in `VtableLayouts.g.cs` but not surfaced by `IFwdEngineServer` | `VtableLayouts.g.cs:873,1131`, `EngineServer.cs:37` |
| port | no API. `IEngineConsole.FindConVarValue("hostport")` reads the cvar, which is the *requested* port | `EngineAreas.Core.cs:33` |
| args | `ILocalCommandLine.Args`, from `/proc/self/cmdline` | `LocalCommandLine.cs:76` |
| kick / say / client cmd / server cmd | none / `SendUserMessage("SayText"/"TextMsg")` / `SendClientCommand(handle, cmd)` (standalone players) / `ExecuteServerCommand` (runs synchronously). **No console output capture** | `EngineAreas.Core.cs:293`, `shell_entityapi.cpp:1615,1636` |
| dedicated | **none.** `source#` always starts `hl2_linux64`; `make headless` is a client run | `Makefile`, `src/launcher` |
| packages | no NuGet.config or central package management. `TreatWarningsAsErrors` everywhere | `Directory.Build.props` |

### 1.2 The host side

- `make pack` builds `SourceSharp.Host.Contracts` and `SourceSharp.Host.Sdk`
  (both net10.0, version 1.0.0) into `bin/nupkg`.
  - **Gap, now fixed at a0e0810:** Host.Sdk had a plain `ProjectReference`
    to Host.Proto, so its package depended on a `SourceSharp.Host.Proto` package
    that `make pack` never built (restore failed with NU1101). Since a0e0810 the
    Sdk package carries `SourceSharp.Host.Proto.dll` itself, and `make pack`
    proves the fix by building a throwaway consumer. The version is still 1.0.0,
    so P1 first deletes `~/.nuget/packages/sourcesharp.host.{sdk,contracts}`,
    which may hold the broken copy.
  - Transitive dependencies: Grpc.Net.Client 2.84.0 and Google.Protobuf 3.36.2.
- **The SDK cannot find a byte-loaded module.** `LoadedAssemblyModuleSource.Find`
  only accepts an assembly with a non-empty `Location`
  (`src/Host.Sdk/Modules.cs`). It then reads the assembly's dependencies from
  the directory beside that path. Every assembly here is loaded from a byte[],
  so its `Location` is `""`. Booting would therefore announce no module.
  - The fix is on our side: a `HostSdkOptions.ModuleSource` that describes
    `<managed_dir>/Descent.Rules.dll` by its path (§3.2).
  - No change is needed in the host.
- `StartAsync()` reads `Environment.GetCommandLineArgs()`, which under the
  hosted CLR is not the engine's argv. The explicit overload
  `StartAsync(args, env, options)` takes them; we pass `ILocalCommandLine.Args`.
- **Tasks complete inside `Pump()`**, on the thread that pumps. Continuations
  run inline when there is no SynchronizationContext. The SDK's 15 s stall
  detector times the gap between `Pump()` calls; that is how the service learns
  a game thread hung. Both facts shape §2.9.
- Pods run `srcds_linux64` with `-game /game/mods/descent`. They are launched
  by the host's NativeAOT launcher (D-H11), never `source#`. The entrypoint sets
  `SOURCESHARP_MOD` / `SOURCESHARP_MODS_YAML` and reads `engine.fd_limit` and
  `env` from our `mods.yaml`.
- H0a booted the test mod that way after three fixes, all now required of the
  mod image (`docs/mod-image.md` in the host repo):
  - `|all_source_engine_paths|` instead of `|appid_N|`;
  - `server_srv.so` beside `server.so`;
  - glibc no newer than the engine image's.
- plan_host §14.3 asks this repo for three things:
  - a `make dedicated` target;
  - a `mods.yaml` engine entry for `srcds_linux64`;
  - an `SOURCESHARP_ENGINE_DIR` / `-engine <dir>` override in `source#`.
- **Hibernation.** H0a's server hibernated with no players. If GameFrame and
  Think stop while hibernating, `Pump()` stops too. After 15 s
  (`MainThreadStallLimit`) the SDK withholds heartbeats and the service reaps
  the instance. *(measure, P0)*

---

## 2. Design

### 2.1 Projects (plan_host D-H8: Descent lives here as `Descent.*`)

| project | path | references | in mods.yaml |
|---|---|---|---|
| `Descent.Rules` | `src/descent/rules/` | `SourceSharp.Host.Contracts` (package) **only** | no, loaded by Descent.Server touching a type |
| `Descent.Shared` | `src/descent/shared/` | `SourceSharp.Abi`, `SourceSharp.Shared` | no; a dependency of Server and Client, resolved from the managed dir |
| `Descent.Server` | `src/descent/server/` | `Descent.Shared`, `SourceSharp.Host.Sdk` (package), `Descent.Rules` | `assemblies.server: [SourceSharp.Server, Descent.Server]` |
| `Descent.Client` | `src/descent/client/` | `Descent.Shared`, `SourceSharp.Abi` | `assemblies.client: [SourceSharp.Client, Descent.Client]` |
| `Descent.Tests` | `src/descent/tests/` | all four, xUnit, the engine fakes | n/a |

- **Descent.Rules starts as a port of `Host.FakeRules`.** It implements every
  `IGameRules` member: JSON sheet schema 1, the five tiers, and a deterministic
  `KillRoll`. It carries `[assembly: HostRulesModule(typeof(DescentRules))]`.
  It is original work against the host's contract, so it gets no Valve header.
  - A test checks that it references nothing but `System.*` and
    Host.Contracts. The service loads it alone.
- **None of the Descent assemblies has an `ISideModule`.** Entry requires
  exactly one per side, and those belong to `ServerModule` and `ClientModule`.
- **Descent.Shared holds what both ends agree on:**
  - the user messages Descent adds (`descent_retry`, `descent_notice`), as
    names, payload records and codecs;
  - the names and help text of the console commands;
  - the mod's `SharedInfo`-style constants.
  It references neither the SDK nor Host.Contracts, so the client never loads
  either. A test checks that closure.
- **Descent.Client starts small:**
  - the `descent_retry` handler, which runs `retry` locally (§2.3);
  - `descent_notice`, which shows the service's Say/Drain text on the HUD;
  - a HUD line with the instance kind and depth, sent by the server on join.
  Everything else client-side is later gameplay.

### 2.2 How the Descent assemblies plug in: mods.yaml is the registration

**Listing an assembly in mods.yaml loads it.** Its `[ConCommand]`, `[ConVar]`
and entity classes are then found by the existing scan (`LoadedAssemblies.Types`).
Three things are still not found:

- **Services.** The containers are built by hand (`ServerModule.Initialize`,
  `ClientModule`), so a service in a mod assembly is never constructed, and
  `IGameLifecycle` / `IFrameLifecycle` only run what is in the container.
- **User-message handlers on the client.** These are scanned only from
  SourceSharp.Shared's assembly (`ClientModule.cs:296`,
  `UserMessageRegistry.AddHandlersTo(_services, typeof(SharedInfo).Assembly)`).
- **The same on the server**, for any message it registers.

**There is no `Register` call or extension interface for the mod to implement.**
Each side treats the assemblies mods.yaml lists for it, other than the one that
holds its `ISideModule`, exactly as it treats its own code:

- **Services:** classes marked `[SideService(Side.Server)]` /
  `[SideService(Side.Client)]` (a new attribute in SourceSharp.Abi, the
  `[ConCommand]` idiom) are registered as singletons. They are constructed by
  constructor injection from the container, after the module's own
  registrations and before sealing.
  - A marked class that also implements `IGameLifecycle` / `IFrameLifecycle`
    runs through the existing drivers. Order values must be unique, as today.
- **User messages:** handlers and registrations are scanned in those assemblies
  and in their direct references. That is how Descent.Shared's are found
  without Descent.Shared being listed.
  - A message name defined twice is refused, with both assemblies named.
- **Mods add their own dependencies** (the user, 2026-09-30). The attribute
  covers the simple case. A mod registers anything else through its own code:
  - **`[SideService(Side.Server, As = new[] { typeof(IFoo) })]`** registers the
    class under the interfaces it serves, as well as under itself. The
    attribute may be repeated, once per side, for Descent.Shared services both
    ends use.
  - **`ISideServiceRegistrar`**, for what an attribute cannot express:
    - binding a type from an assembly that cannot carry the attribute;
      `IGameRules` → `DescentRules` is the case, since Descent.Rules may
      reference only Host.Contracts;
    - an instance built by a factory: the SDK's options, a `HostSdk` holder;
    - third-party types such as gRPC options.
    ```csharp
    public interface ISideServiceRegistrar           // any number per assembly, found like [SideService]
    {
        Side Side { get; }
        void Register(ISideServiceRegistry registry);
    }
    public interface ISideServiceRegistry            // a narrowed view of ServiceContainer
    {
        void Add<TService, TImpl>() where TImpl : class, TService;
        void AddInstance<TService>(TService instance);
        void AddFactory<TService>(Func<IServiceLocator, TService> factory);   // lazy, built once, on first resolve
    }
    ```
  - **How registrars are built.** Each one is constructed by the container, so
    its constructor can take core services already registered. `Register` runs
    after the core's own registrations and the attribute scan, and before
    sealing.
  - **What a mod registration is refused for**, each refusal naming both
    sources:
    - overriding a type the core registered;
    - registering a type another mod assembly already registered.
  - **Construction is lazy and by constructor injection.** A mod service can
    depend on core services, on other mod services and on registrar-made
    instances. A cycle is reported with its path.
  - **Core gains** `ServiceContainer.AddFactory`, a generic addition with its
    own facts. Today the container has `AddInstance`, `Add<TService,TImpl>` and
    lazy `Add<T>`, but no factories.
- **Why an attribute and not "every `IGameLifecycle` type":** the scan must not
  pick up helpers or test doubles that happen to implement an interface, and
  the attribute names the side.

The SDK holder (`HostService`) is one of these services. Nothing outside
`src/descent/` names a Descent type.

**Tests:**

- a marked service in a test assembly listed for a side is resolved, and its
  frame hook is called, on each side;
- an unmarked class implementing `IFrameLifecycle` is not picked up;
- a client-side handler in a referenced (unlisted) assembly is dispatched;
- `As` binds an interface; a registrar's binding, instance and factory each
  resolve, and the factory runs once, lazily;
- a registrar constructor receives a core service;
- a mod service is injected into another mod service, and a core service into
  a mod service;
- overriding a core registration is refused, and so is a duplicate between two
  mod assemblies;
- a registration cycle is reported with its path;
- a duplicate message name is refused;
- the test mod builds exactly the containers and message tables it builds
  today, per side, by diffing the extracted registration lists. Its mods.yaml
  lists only the module assemblies, so nothing new is scanned.

### 2.3 The engine surface we add (all generic, all managed where possible)

| need | how | native? |
|---|---|---|
| a client's SteamID64 | `IFwdEngineServer.GetClientSteamID(edict)`: slot 110 is already in `VtableLayouts.g.cs`, so surface it on the bound interface and read the `CSteamID` as a `ulong`. Zero = not authenticated | no |
| a client's address | keep the `address` from `OnClientConnect`, keyed by the entity index that `OnClientPutInServer` reports for the same name/slot. *(measure, P0: the slot that connect reports.)* The spec says "the address string the engine has, unchanged" | no |
| disconnect | a generic `IGameLifecycle.OnClientDisconnect(int entIndex)`, raised from the managed handler of the standalone player's removal that `ClientDisconnect` already announces. If P0 shows that announcement can come too late, or for the wrong entity, add a `ClientDisconnected` managed-vtable slot instead. That is a one-line native forward, appended to the ABI tail | maybe one forward |
| the bound port | read from the kernel: sockets in `/proc/self/fd` → inodes → `/proc/self/net/udp{,6}` local port, choosing the UDP socket bound on the game interface that isn't HLTV's. This is what CLAUDE.md already prescribes for "the engine silently binds a higher port". `hostport` is cross-checked and logged, never trusted | no |
| kick | `ExecuteServerCommand($"kickid {userId} {reason}")`, using the userid from `IEnginePlayers.GetUserId` | no |
| retry | the `descent_retry` user message (Descent.Shared), whose Descent.Client handler runs `retry` locally. This avoids depending on whether the client accepts a server-sent `retry`. P0 still measures that, so the fallback for a client without Descent.Client is known | no |
| say | `SendUserMessage("SayText", …, all)`, and `descent_notice` for the HUD | no |
| exec with output | run the command through `ExecuteServerCommand` and reply with whatever the managed console sink captured during the call, plus `"(native output not captured)"`. Managed-only is the accepted behaviour (decision 6.2); engine spew capture is a recorded divergence, not planned | no |
| exit | the Shutdown command: checkpoint and release everyone, `await DisposeAsync`, then `IServerConsole.Quit(0)`. Exit code 0 comes from the engine's normal quit. Never `Environment.Exit` | no |

All new managed→native calls go through bound interfaces (`IShim…` / vtable
rows), never a free `SourceSharp_*`. Each ABI addition is appended at the tail
of both files, and the integrator bumps the version.

### 2.4 Descent.Server's shape

It copies `FakeGameServer.cs`, the spec's reference, onto the engine's hooks:

- **`HostService`** (`[SideService(Side.Server)]`, an `IGameLifecycle` and an
  `IFrameLifecycle`):
  - **Server start (`OnPostInit`):**
    - `HostSdk.StartAsync(commandLine.Args, processEnvironment.GetVariable, options)`, both injected (§2.8),
      where `options.ModuleSource` is a `PathModuleSource` (§1.2) for
      `<managed_dir>/Descent.Rules.dll`;
    - `RuntimeHelpers.RunClassConstructor` on a Descent.Rules type first, so
      the module is loaded;
    - `HostConfigException`: log it loudly through `ILog`, then `IServerConsole.Quit(1)`.
      **Unhosted is explicit and for local testing only** (decision 6.1).
      `-unhosted` on the command line skips the SDK, with one log line saying
      so. Without it, missing host settings are a `HostConfigException`, so a
      pod with a broken Secret fails loudly instead of running unhosted.
      `-unhosted` together with `-hostlocal` or any `DESCENT_*` variable is
      refused as ambiguous.
  - **The SDK thread** owns the `HostSdk` and pumps it, and every SDK call is
    made there (§2.9). The game thread only stamps its liveness each frame,
    from the engine-think path, which runs with or without a level.
  - **`OnLevelInit(map)`:** `Booting(map)`. Retry on Unreachable or Rejected
    (the fake does both) on `IFrameScheduler`, with 1 s backoff. Any other refusal
    is logged with the error, reason and detail, then the server quits non-zero.
  - **Map loaded and socket bound:** `MapReady(boundPort, map)`, retrying only
    on Unreachable. Triggered by HostService's `IGameLifecycle.OnLevelReady(map)` hook (§2.8), with the port from `IBoundPort`.
- **`PlayerService`** (steamId → `PlayerState { peer, entIndex, lease, reserve }`):
  - **Joins:**
    - only after `Boot` has arrived; until then, `OnClientConnect` rejects with
      "server starting";
    - while draining, it rejects with "server closing";
    - on `OnClientActive`: PlayerJoined, then List; on the hub, Create; then
      Lease. PlayerService implements `ILeaseHook.OnLeaseLost` (§2.11),
      which freezes the player and kicks them with the reason;
    - on a level: OpenReserve and SetReachedDepth.
  - **Leave** (disconnect, kick, Retry): Checkpoint, Release, PlayerLeft.
    Checkpoint and Release are skipped when the lease is already released.
  - **Checkpoint timer:** every 60 s per player. The fake doesn't have one;
    the spec asks for it.
  - **The sheet** is the lease's current `Character.Sheet` until gameplay
    writes one. Its schema version is owned by Descent.Rules, and the
    `uint`/`int` mismatch between `OpaquePayload` and `CharacterSheet` is
    converted in one place.
  - **`Session.Players`** is set from the map's count on every change.
- **`CommandService`** wires `sdk.Commands.*` on the SDK thread, as the table
  in spec §6 lays out.
  - Engine work goes to the game thread as a message. Its result comes back as
    a message.
  - PrepareHop: the game thread snapshots the sheet, then the SDK thread runs
    Checkpoint, Release and `Ready()`, in that order, each awaited (§2.9).
- **Console hooks** (`[ConCommand]`, `FCVAR_CHEAT`-free but server-only):
  - `descent_status`: boot info, the leases held, the SDK's connection state.
  - `descent_travel <descent|stairs|portal|return|corpse> [depth]`: the spec
    §7 travel calls for the issuing player.
  - `descent_kill [robot]`: one kill roll, then `NextForTier`, then `Reveal`,
    printing the item. No world entity is spawned.
  - `descent_checkpoint`: an immediate checkpoint.
  - These are the drivers that spec §10 notes the real tier needs.
- **Peers.Resolve** is called on join and logged. Bans are out of scope.

### 2.5 The mod: mods.yaml, content, dedicated

**The schema becomes defaults plus per-mod overrides** (the user, 2026-09-30),
and the client engine gets its own parent beside `dedicated`:

```yaml
default_mod: test            # was `default`; renamed so it can't be confused with `defaults`
defaults:                    # every mod setting; a mod overrides any of it
  title: "{id}"
  game_dir: mods/{id}        # {id} is the only substitution, allowed in title and path values
  content: game/mod_{id}
  steam_appid: 243750
  managed_dir: bin/managed
  assemblies:
    server: [SourceSharp.Server]
    client: [SourceSharp.Client]
  engine:
    client:                  # `source# -mod X`: the game client and listen server
      appid: 243750
      exe: hl2_linux64
      restart_exit_code: 42
      fd_limit: 2048
      args: []
      env: {}
    dedicated:               # `source# -mod X -dedicated`, `make dedicated`, and the pod
      appid: 244310
      exe: srcds_linux64
      restart_exit_code: 42
      fd_limit: 4096
      args: [-console, -nohltv, +tv_enable, "0"]
      env: {}
mods:
  test:
    title: SourceSharp test mod
  descent:
    title: Descent
    assemblies:
      server: [SourceSharp.Server, Descent.Server]
      client: [SourceSharp.Client, Descent.Client]
```

- **Merge rules,** one resolver and stated once:
  - maps merge key by key, recursively;
  - scalars override;
  - **lists replace** and never concatenate, so a mod restates the whole list
    (as `descent` does for its assemblies). Concatenation would make "remove
    a default" impossible.
  - `{id}` is substituted after the merge.
  - Validation runs on the merged result: unknown keys anywhere (in
    `defaults` or a mod), a required field missing after the merge, a path
    escaping its root, and a `default_mod` naming no mod. Each error names the
    mod and the key path, e.g. `descent.engine.dedicated.exe`.
- **The test mod's resolved settings must not change.** A fact resolves `test`
  under the new file and diffs every field against the values today's file
  gives, extracted at the base commit and checked in as the expected record.
  The `steam_appid`, `content` and `game_dir` it gets from defaults are its
  current values.
- **Code that changes:**
  - `ModsFile.cs`: the `defaults` node, `engine.client` / `engine.dedicated`,
    and `args`/`env` moving under each engine.
  - `ModsConfig.cs`: the merge, the substitution and validation on the merged
    result. `Mod` gains `Client` and `Dedicated` `EngineSettings`, each
    carrying its args and env.
  - `source#`: `-dedicated` picks `Dedicated`; otherwise it picks `Client`.
  - Bootstrap's `ModAssemblies`: unchanged in behaviour, reading resolved
    assemblies.
- **`tools/setup` (Python) also reads mods.yaml,** for `game_dir` and
  `content`. It gets a Python port of the same merge. A parity fact runs both
  resolvers over a set of fixture files and diffs the results, with a mutation
  arm: a planted difference in one resolver must fail it. That prevents two
  merges from drifting.
- **`source# -resolve <mod> [-dedicated]`** prints that mod's fully resolved
  settings for one engine as flat YAML. Tools and the host can read effective
  values without reimplementing the merge.
- **Host impact.** `deploy/game-entrypoint.sh` awk-greps `fd_limit` out of the
  mod's block. Under defaults it would find none, or the client's, and the
  fd limit would silently fall back.
  - `make mod-image` therefore writes the resolved dedicated settings to
    `/game/mods/<id>/mod.dedicated.yaml`, using `-resolve`. That file has a
    flat `fd_limit:` that the existing awk would also find.
  - We ask the host to read that file and to apply its `args` and `env`. Its
    comment says the entrypoint does, but it applies only `fd_limit` today.
    This is §2.7(11), a **host** request.
- **Why two engines.** Players run the mod as a *client* (`hl2_linux64`) to
  join a hosted server, and the server is `srcds_linux64`. Each gets its own
  parent with its own exe, appid, exit code, fd limit, args and env.
  Server-only cvars also live in `cfg/server.cfg`.
- **`default_mod` stays `test`.** The live gate, `make run` and every existing
  launch path are unchanged.
- **`game/mod_descent/`** holds the mod's game directory:
  - `gameinfo.txt` (game "Descent"), with search paths per §2.7(1);
  - `cfg/server.cfg` as a seed, with the hibernation setting and the download
    settings (§2.7(4));
  - `maps/descent_town.bsp`, which is §2.7(3).
- **Engine selection.** `source#` gains `-engine <dir>` and
  `SOURCESHARP_ENGINE_DIR` (plan_host §14.3).
  - Without either, it keeps finding the engine through Steam: for the
    dedicated engine it looks up 244310's install dir. Plan_host's ops notes say
    that app ships 32-bit only *(measure, P0)*; if so, the local path is the
    243750 client's `srcds_linux64` and `-engine` names it.
- **`server_srv.so`.** `tools/setup` / the build links `bin/<config>/bin/linux64/server_srv.so`
  to `server.so`, so a dedicated engine finds it in both trees and the image.
- **`make dedicated [MOD=descent] [MAP=…]`** runs `./source\# -mod descent -engine … -port … +map …`
  with the server flags `HEADLESS_ARGS` already carries, minus the windowing
  flags. It sources `tools/gpu-offload` only if the engine needs it, which a
  dedicated one shouldn't.
- **`make mod-image`** packs `bin/release/` into `/game` per the host's
  `docs/mod-image.md`, with label `org.sourcesharp.mod=descent`.
  - Built from `make ci-build` output, so glibc stays within the engine image's.
  - Symlinks are dereferenced.
  - `make mod-image-push REGISTRY=…` is separate, and is the P9 checkpoint.
  - The host's `make mod-image-dev` then goes, and its gameinfo rewrite is no
    longer needed (the host session is told).
- **Packages:**
  - A repo `NuGet.config` adds `../source-sdk-sharp-host/bin/nupkg`, overridable
    with `HOST_ROOT`.
  - `make host-pack` runs `make -C $(HOST_ROOT) pack`. Plan_host §14.9 allows a
    `HOST_ROOT` ProjectReference too, but the package is the contract, so we use
    the package.
  - `Descent.Server` sets `CopyLocalLockFileAssemblies` so that Host.Sdk,
    Host.Contracts, Grpc and Protobuf land in `bin/<config>/bin/managed`.
  - A missing `bin/nupkg` fails restore with a message naming `make host-pack`.
- **Unchanged:** the test mod's `server: [SourceSharp.Server]` stays as it is,
  so Descent.Server is never loaded there.

### 2.6 What stays out

- Gameplay for spec §7: spawning items, pickup, death, corpses, vendors, parties
  and trades beyond console hooks.
- Client gameplay beyond Descent.Client's three handlers (§2.1).
- Packet captures.

### 2.7 What else it takes to work

Each item below is required for the §5 done-criteria. Items marked **host**
need a change or an answer in source-sdk-sharp-host. Those go to the host
session as requests with this plan's section number; we never edit that repo.

1. **TF2 content.**
   - Descent runs on TF2 content (D-H6). A pod mounts it read-only at
     `/game/content` (`GamePodBuilder.cs:94`), filled from app 232250.
   - `gameinfo.txt` mounts `tf/` VPKs and dirs relative to itself:
     `|gameinfo_path|../../content/tf/…`, which from `/game/mods/descent` is
     `/game/content`. The base SDK content stays on `|all_source_engine_paths|`.
   - Locally, `tools/setup` links `bin/<config>/content` to the local TF2
     install, so the same gameinfo works in both trees. The install is found
     through Steam's `libraryfolders.vdf`, or `TF2_DIR`.
   - A missing install is a setup failure for the descent mod only.
   - *(measure, P0: that a relative `|gameinfo_path|..` path mounts, and which
     TF2 VPKs a generated level needs.)*
2. **Pool levels are found by their plain name.** **host**
   - Levels arrive at `mods/descent/maps/pool/descent-<d>-<h>.bsp` and the pod
     passes `+map descent-<d>-<h>`. The engine looks in `maps/<name>.bsp`, so it
     would not find them.
   - Proposed fix: gameinfo adds `game |gameinfo_path|pool`, and the host
     mounts the overlay at `mods/descent/pool/maps`. The client then fetches
     `maps/descent-<d>-<h>.bsp.bz2`, which is exactly what fastdl serves.
   - Probe first in P0. If `+map pool/<name>` works for the server, that fork
     is rejected anyway: the client would ask fastdl for
     `maps/pool/<name>.bsp.bz2`, and fastdl 404s that.
3. **The town map, `descent_town`.** The hub runs it (`ServiceOptions.TownMap`,
   `ServiceOptions.cs:45`), and it ships with the mod. Nobody has built one
   yet; §2.10 is the design.
4. **Clients download levels.** **host**
   - The server sets `sv_allowdownload 1` and `sv_downloadurl`. The URL comes
     from the pod: we propose a `DESCENT_FASTDL` env var or a `+sv_downloadurl`
     in `Instances.Args`; the host decides which.
   - Without it, a hosted level server logs that clients cannot download.
5. **The player's client.**
   - `source# -mod descent +connect <address>` runs the client engine with
     Descent.Client, the town map and TF2 content mounted. `make
     join ADDR=…` wraps it.
   - Whether a Steam-authenticated join through the gateway works
     (`-insecure` or not) is the host's H0b/H0c territory. P9 records the
     regime it ran under.
6. **Hops in local mode: same process; hosted: never** (the host's user,
   2026-09-30).
   - **Local:** one game goes up and down by itself. `PrepareHop` gains
     `string map = 4`, and `HopRequest` gains `Map` and `IsSameProcess`. The
     service sets these only when it runs local.
   - **The game's side:** checkpoint, release, `Ready()`, then `changelevel`
     to `Map` through `IServerConsole`.
   - **Hosted:** one map per pod for its whole life, and every hop goes to
     another pod. Hosted pods never see `IsSameProcess`, and the host has a
     fact for that.
   - **Until the contract lands,** with its version and sha from the host, the
     local gate runs two processes: the level's `local-level-<id>.json` and a
     second game with its own TMPDIR, devapi dir and `-port`.
7. **Hibernation.** An empty hub must keep pumping (R1). The setting lives in
   `cfg/server.cfg` per P0.
8. **Hot reload.** The SDK lives in the collectible load context.
   - `ServerModule.Shutdown` disposes `[SideService]` singletons that are
     `IAsyncDisposable` / `IDisposable`, which is generic. HostService's
     dispose releases leases and disposes the SDK.
   - After a reload the server boots again. In hosted mode the service
     re-adopts the instance.
   - The live gate's second map load runs with the descent mod once (R5).
9. **Running unhosted, for local testing** (decision 6.1).
   - `-unhosted` runs the descent mod as a plain server.
   - `make run MOD=descent`, `make dedicated MOD=descent` without `HOSTED=1`,
     and any gate that loads descent pass it.
   - Nothing in the mod image or `engine.dedicated.args` carries it.
   - Facts: no settings and no flag refuses to start; the flag alone runs
     unhosted; the flag plus any host setting is refused.
10. **Local hosted runs.**
    - `make local-hosted` (or `make dedicated HOSTED=1`) does three things:
      runs `make -C $(HOST_ROOT) local` if `bin/local/local-host.json` is
      absent, waits for it, then starts the dedicated server with `-hostlocal`
      pointing at it.
    - `-hostlocal inproc` is used only by facts. The game's runtime would need
      ASP.NET Core and SQLite (D-H13), which the shipped runtime does not carry.
11. **The pod reads resolved mod settings.** **host** The entrypoint reads
    `fd_limit`, `args` and `env` from `/game/mods/<id>/mod.dedicated.yaml`,
    as §2.5 explains.

### 2.8 Core additions: what Descent needs that the core lacks

Every row is a generic core service, named for what it does rather than for
Descent. It is registered in the core container, so any mod can inject it.

| core service | where | what | replaces in Descent |
|---|---|---|---|
| `ILog` | Abi (interface) + both modules (registration) | `Msg` / `Warning` / `Error` to the side's console, through the same sink as `Log` | the static `Log` |
| `IFrameScheduler` | Abi + both modules | `Task Delay(TimeSpan, CancellationToken)`, which resumes on the game thread from the frame driver. Await-only: no callbacks to register. Periodic work, such as the checkpoint timer, is an `IFrameLifecycle` hook that checks its own due time | `Task.Delay`, timers. Used for Booting retries |
| `IModInfo` | Abi, filled by Bootstrap from mods.yaml | mod id, game dir, managed dir, the side's assembly list | hard-coded paths. `PathModuleSource` reads the managed dir from here |
| `IGameThreadDispatcher` | Abi + both modules | `Post(Action)` from any thread; drained by the frame driver on the game thread every frame (level or none), within a budget; backlog metric. DevApiServer's own marshalling moves onto it | ad-hoc queues; the SDK thread reaching the engine |
| `IGameThreadLiveness` | Abi + both | the frame stamp (monotonic ms of the last frame), readable from any thread | the SDK's game-thread stall detection (§2.9) |
| `IProcessEnvironment` | Abi + Shared | `GetVariable(name)`; `ILocalCommandLine` already covers args | `Environment.GetEnvironmentVariable` |
| `IEngineClients` (server) | Abi + Server | `SteamId64(entIndex)`, `Address(entIndex)`, `UserId(entIndex)`, `Kick(entIndex, reason)`, `ClientCommand(entIndex, cmd)` | §2.3's rows, in one place |
| `IClientLifecycle` (server hook interface) + `ClientLifecycleDriver` | Abi + Server | `OnClientConnecting(name, address) → string? reject`, `OnClientActive(entIndex)`, `OnClientDisconnected(entIndex)`, `ClientLifecycleOrder`. The driver works as `LifecycleDriver` does: `ResolveAll`, unique order, each call isolated and reported by name. The first non-null reject wins | hooking `ServerModule` |
| `IBoundPort` (server) | Server | the UDP port the server socket actually bound (§2.3) | reading `/proc` in Descent |
| `IServerConsole` (server) | Abi + Server | `Execute(cmd) → captured output`, `Quit(exitCode)` | `ExecuteServerCommand`, `quit` strings |
| `IChat` (server) | Abi + Server | `PrintAll(text)`, `Print(entIndex, text)` | building `SayText` payloads |
| `ILevelInfo` (both) | Abi + both | state only: the current map name and whether the level is up | `ServerModule._currentMap` (private) |
| `IGameLifecycle.OnLevelReady(string map)` | Abi + Server | a new hook method with an empty default body, like `OnLevelInit`. It is called once per level, on the first frame after `ServerActivate`, once the socket is bound | guessing the first frame |
| `IHooks<T>` | Abi + both | the drivers' dispatch made generic, for hook interfaces a mod defines (§2.11). It is injectable and resolves `ResolveAll<T>()` lazily on first dispatch (after sealing), so a hook can depend on its dispatcher without a cycle. Unique `Order` per hook interface; each call isolated and reported by name | C# events between Descent classes |
| `[SideService]`, `ISideServiceRegistrar`, `ServiceContainer.AddFactory`, discovery + disposal | Abi + both | §2.2 | n/a |

**Enforcement.** A fact in `Descent.Tests` reflects over every type in the
Descent.\* assemblies and fails if any of them:

- references `SourceSharp.Abi.Log`, `Engine` or the ABI table types;
- calls `Environment.GetEnvironmentVariable` / `Environment.Exit`;
- has a static mutable field.

Its fail-first arm is a planted violation in a test assembly. Core services may
only be constructor parameters.

The rows land in P3, each as its own commit with its facts and with a
`RecordingEngine`-style fake that asserts rather than answering `default`.
§2.3's table is how each server row is implemented.

### 2.9 Threading: the SDK thread and the two queues

Descent.Server holds no SDK object on the game thread, and its SDK thread
calls no engine service.

```
 game thread (engine)                               SDK thread (HostLink)
 ───────────────────                               ─────────────────────
 frame: liveness.Stamp()            ── to-SDK ──▶   loop: drain to-SDK queue
        dispatcher.Drain(budget)                          gate → sdk.Pump()
 HostLink.PlayerJoined(..) ─ msg ─┐                        events → to-game queue
   awaits; resumes on game thread │                  ◀── to-game ── results, commands
 handlers: Kick/Say/Retry/Exec ◀──┴───────────────────────────────┘
```

- **The SDK thread** (`HostLink`, a Descent.Server service, started by
  HostService):
  - It is a dedicated, named, background `Thread`. It runs `StartAsync` and
    then loops: drain the to-SDK queue, pump, sleep until the next tick
    (16 ms) or until a message wakes it.
  - Every `HostSdk` call happens here. It is the SDK's "main thread" in the
    spec's sense, so it never blocks on an SDK task. Everything is `await`ed,
    and continuations run inline in its own `Pump()`.
- **To-SDK queue** (game → SDK): a `Channel<HostRequest>`, unbounded and
  single-reader. Requests are immutable records: `Booting(map)`,
  `MapReady(port, map)`, `Join(peer, steamId)`, `Checkpoint(steamId,
  sheetBytes, schema)`, `Leave(steamId, peer)`, `Players(n)`, `Travel(…)`,
  `ExecReply(id, text)`, and so on.
  - No engine object, entity or span ever crosses. The game thread copies
    what it sends.
- **To-game queue** (SDK → game): the core `IGameThreadDispatcher` (§2.8).
  - Any thread can `Post` to it.
  - The frame driver drains it on the game thread every frame, level or no
    level, within a per-frame budget. The rest carries over, and the backlog is
    counted.
  - It carries SDK events (Kick, Say, Retry, Drain, Shutdown, Exec,
    PrepareHop, LeaseLost) and the results of game-thread requests. Each
    event is handed to its hook interface's implementations through
    `IHooks<T>` (§2.11).
- **Awaiting from the game thread.** `HostLink`'s game-facing methods return a
  `Task<HostResult<T>>`. Its `TaskCompletionSource` is completed *by a message
  drained on the game thread*. With no SynchronizationContext, the
  continuation runs inline there, so game code that `await`s resumes on the
  game thread, in a later frame, and may touch the engine.
  - A fact proves this by checking the managed thread id.
- **Hang detection kept.** The game thread stamps `IGameThreadLiveness` each
  frame. The SDK thread pumps only while the stamp is younger than
  `MainThreadStallLimit` (15 s). A hung game thread therefore stops the pumps,
  and the SDK withholds heartbeats exactly as the spec intends.
  - Facts: a frozen stamp stops `Pump()` within the limit, and pumping resumes
    when the stamp moves.
  - A map load before the first frame is fine, as in the spec. After that the
    engine-think path stamps during loads. *(measure, P0(1))*
- **Ordering.** Requests for one player are handled in order on the SDK
  thread. A hop's Checkpoint → Release → `Ready()` runs entirely there, after
  the sheet snapshot arrives from the game thread, so no interleaving can put
  `Ready()` before Release.
- **Shutdown and reload:**
  - Shutdown: the game thread snapshots every player's sheet and posts them.
    The SDK thread checkpoints, releases, disposes the SDK and posts `Quit(0)`.
  - Hot reload or DLL shutdown: HostService signals the thread, joins it with
    a 5 s timeout, and logs if the join times out. Leases are released before
    the context unloads (R5).
- **Thread-affinity checks:**
  - The asserting engine fakes record the calling thread and fail on any
    non-game thread.
  - An `SdkThreadGuard` in HostLink asserts that every SDK call is on the SDK
    thread.
  - Both have fail-first arms.

### 2.10 `descent_town`: a simple mining town with a mineshaft down

The user's ruling, 2026-09-30: a simple mining town for now, with a mineshaft
going down. It replaces the hand-authored square and church stairs of the old
TF2 plan (`plan_tf2_rpg.md` §11 Phase 9, in sharp-fresh), and that layout can
replace this one later. Generated by `src/mapgen`, compiled by map-tools'
`ssmap`, as the sandbox is.

**How it's built** *(read)*:

- `src/mapgen` writes a `.vmf` from C# (`Vmf.cs`, `Site.cs`; `VmfMap.Box`,
  `Site.Ent/Brush/Solid/Wire`). The Makefile's `map:` target compiles it with
  `ssmap all --vbsp --vvis --vrad` (`Makefile:378-455`).
- `Program.cs` hard-codes `Sandbox.Build()`, so the town adds:
  - a `town <dest.vmf>` subcommand;
  - `src/mapgen/DescentTown.cs` (`DescentTown.Build() → VmfMap`), as an
    injectable builder rather than a static class;
  - `descent_town` in `GENERATED_MAPS`, compiled into
    `bin/<config>/mods/descent/maps/`. `mapcheck`/`mapdrift` stay
    sandbox-only.
- The `.vmf` and `.bsp` are per-tree build output: never committed, never
  copied between trees (CLAUDE.md, on `ss_sandbox.bsp`). `make mod-image`
  builds the town if it's missing.

**Layout.** Brushes and props only, with no displacements; the generator has
no displacement support and the town doesn't need it.

| part | what | size (units, approx.) |
|---|---|---|
| valley | a sealed box: rock-wall cliffs on four sides (`nodraw` outside), a dirt ground plane, skybox ceiling, `light_environment` for an afternoon sun | 3072 × 2048, cliffs 768 high |
| main street | one dirt street down the long axis, boardwalks on both sides | 2400 × 384 |
| buildings | 6 closed wooden shells (general store, assay office, saloon, bunkhouse ×2, smithy), each a box with a pitched roof made of two boxes, a porch and a door frame. Not enterable in v1; signs are `info_target` markers for later shops | 384–512 square |
| square | the street's widening at the town end. The spawn area: 16 `info_player_deathmatch` (whichever spawn class the game uses, checked against `Sandbox.Spawns`), facing the mine | 768 × 768 |
| mine head | at the far end, against the cliff: a timber headframe (four posts, crossbeams, a sheave wheel prop if the content has one), a fenced shaft mouth 192 × 192, a winch house shell | 512 × 512 |
| the shaft | open, 384 deep, timber-lined, with a **cage** (a lift platform at the top) | 192 × 192 × 384 |
| props | ore carts on a short rail spur, crates, barrels, lanterns (`light` + prop), a water tower; picked from the content palette below | — |

**Going down.** This is the same contract as the generated levels, so one
implementation serves both:

- **The trigger.** Inside the cage, a `func_button` ("Descend") fires
  `Transition` on a `logic_level_transition` named `mine_descent`, with
  `direction=down` and `map=descent-1`.
  - Its keys and inputs are map-tools' `ModEntityContract` v1:
    `targetname`, `direction`, `map`, `StartDisabled`; inputs `Transition`,
    `Enable`, `Disable`; output `OnTransition`. Defined in
    `RoomContracts/LevelTransition.cs`.
  - Worldspawn carries `ssmap_entities` / the contract version, as linked
    levels do.
  - A `trigger_once` across the shaft floor also fires it, for players who
    jump.
  - Then a `func_door`-style cage descent of a few seconds plays while the
    hop runs. It is cosmetic: the hop decides.
- **`logic_level_transition` is new game code.** Nothing implements it today
  *(read: no class in this repo, sharp-fresh or the host)*. It goes in
  Descent.Server as a server-only (no edict) managed entity, implemented to
  the contract. Its activator's player is the one who travels, and its
  `Transition` sends a travel request through HostLink (§2.9):
  - in the hub, with `direction=down`: `Travel.RequestDescent(lease, partyId,
    depth: 1)`. A solo player first gets a party through `Parties.Create`.
    *(measure, P7: whether the service requires a party.)*
  - on a level, with `down`: `StairsDown(lease, partyId, depth + 1)`; with
    `up` and `map` equal to the town: `ReturnThroughPortal`. Otherwise up is
    the level above.
  - `Started`: nothing more here; a PrepareHop follows (spec §6).
  - `Waiting`: a `descent_notice` to the player, "the cage is being readied",
    and the entity re-enables the button after `await IFrameScheduler.Delay(Eta)`.
  - `Refused`: a notice with the reason.
  - **Unhosted:** the entity logs "no host" and does nothing.
  - **Entity DI:** if entity classes cannot take constructor-injected
    services today, that is added to the core first (§2.8). Read in P3.
  - The game must also accept the contract's worldspawn keys without
    warnings.
- **Arrival from below.** Players coming back up appear at the mine head: a
  second spawn group of 8, tagged `arrival`, around the shaft mouth. Spawn
  selection is a Descent.Server service: a join that follows a hop from a
  level picks `arrival`, and otherwise the square.
  - *(read in P6: whether `PlayerJoin` or the character says where the player
    came from; if not, `ReachedDepth > 0` plus "last instance was a level" is
    asked of the host.)*

**Content palette.** Descent mounts TF2 plus the SDK base's HL2 content
(§2.7(1)).

- The town's materials and models come from one table in `DescentTown.cs`:
  ground, cliff, timber, plank, roof, rail, sky, and prop models. TF2's
  mining-themed sets are the first choice. The `props_mining` models,
  wood/rock materials and a western-style sky are candidates, each
  *unverified until P0(9)*. HL2 equivalents are the fallback.
- **A fact proves every entry exists** in the mounted VPKs or dirs, resolved
  through the descent gameinfo's search paths. It uses map-tools'
  `MapFormats` VPK reader, through the `ssmap` process if no library
  reference is wanted. A missing entry fails the fact, never a pink
  checkerboard in game.

**Facts** (`src/test/managed`, beside the sandbox tests):

- the VMF has exactly one `logic_level_transition`, with `direction=down` and
  `map=descent-1`, wired from the button and from the shaft trigger;
- spawns: 16 in the square, 8 tagged `arrival`;
- the town is sealed: `ssmap vbsp` reports no leak, and the build fails on
  one;
- every palette entry resolves (above);
- the generator is deterministic: two runs produce identical bytes;
- the transition entity, against a recording HostLink:
  - hub + down requests depth 1;
  - level + down requests `depth + 1`;
  - `Waiting` re-enables the button at `Eta`;
  - `Refused` notifies with the reason;
  - unhosted does nothing;
  - fail-first arms for each.
- **Live:** `make dedicated MOD=descent MAP=descent_town` loads it twice in
  one process (the second map load). In the P7 gate, pressing the cage
  button in a hosted local run hops the player to a level instance.

### 2.11 Events into Descent classes: hook interfaces, wired by DI

The user's ruling, 2026-09-30: events reach Descent classes the way they reach
core classes. A class implements a hook interface, registers as a service, and
the container connects it at run time. That is the `IGameLifecycle` /
`IFrameLifecycle` pattern: `ResolveAll<T>()` finds every registered service
whose concrete type implements `T`, so no per-interface registration is
needed, and a driver dispatches in a unique order, isolating each call
(`LifecycleDriver.cs:33-45`, `ServiceContainer.ResolveAll`).

- **From the core:** `IGameLifecycle` (with `OnLevelReady`), `IFrameLifecycle`,
  `IClientLifecycle` (§2.8). Their drivers already call every registered
  implementation, including Descent's `[SideService]` classes.
- **From the SDK, via HostLink:** Descent.Server defines its own hook
  interfaces. HostLink dispatches them on the game thread through
  `IHooks<T>`:

  | hook interface | methods | implemented by |
  |---|---|---|
  | `IHostBootHook` | `OnBooted(BootInfo)`, `OnLive()` | PlayerService (to open joins), the transition entity's depth source |
  | `IHostCommandHook` | `OnKick`, `OnSay`, `OnRetry`, `OnDrain`, `OnShutdown`, `OnExec`, `OnPrepareHop` (each with its command record) | CommandService |
  | `ILeaseHook` | `OnLeaseLost(steamId, reason, detail)` | PlayerService |
  | `IHostConnectionHook` | `OnConnectionChanged(bool connected)` | HostService (status), the HUD line |

- **HostLink is the only translator.** It is the one Descent class that
  subscribes to C# events, and those are the SDK's own: `sdk.Commands.*`,
  `lease.LeaseLost`, `Session.ConnectionChanged`. It runs them on the SDK
  thread and posts each as a message whose handler, on the game thread, calls
  `IHooks<T>`.
- **Nothing else declares or subscribes to events.** No Descent class
  declares a C# `event` or takes a callback delegate from another Descent or
  core class. `IFrameScheduler` is await-only for this reason.
- **The enforcement fact** extends §2.8's. It reflects over Descent.\*
  assemblies and fails on:
  - an `event` member;
  - a call to any `add_*` event accessor, found by an IL scan, outside
    HostLink.
  Fail-first arms: a planted event, and a planted subscription.
- **On the client,** Descent.Client's user-message handlers already follow
  this: they are discovered handler types, not subscriptions (§2.2).
- **Tests:**
  - every hook interface has a fact that a `[SideService]` implementing it
    is called, and that an unregistered implementation is not;
  - two implementations with the same order are refused, naming both;
  - one implementation throwing does not stop the others, and is reported.

---

## 3. Phases

Each phase is committed step by step, with `make check` green at the end.
Before every `make check`, `make test` or live gate, wait for an idle box. The
peers asked for this on 2026-09-30, after overlapping runs froze the machine
and their gates lost the devapi. Idle means:

- no `make` or `ctest` in another tree
  (`for p in $(pgrep -x make); do readlink /proc/$p/cwd; done`);
- no running game (`pgrep -x hl2_linux64`, and `srcds_linux64` once P8 exists; `hl2_linux` never matches).

| # | phase | commits (each with its tests) | gate |
|---|---|---|---|
| P0 | **probes** (scratch only, nothing committed but a findings section appended here) | (1) GameFrame/EngineThink under hibernation on a dedicated server, with and without `sv_hibernate_think 1`; (2) the slot/entIndex pairing between `OnClientConnect` and `OnClientPutInServer`; (3) `retry` from the server accepted by our client; (4) the bound-port reader against a taken `-port` (engine walks up), compared with `hostport`; (5) which `srcds_linux64` exists locally (244310 vs 243750); (6) when the removal announcement from `ClientDisconnect` reaches managed relative to the edict going away; (7) a relative `|gameinfo_path|../../content/tf` search path mounts, and which TF2 VPKs a level needs; (8) a map in a `pool/maps/` search path loads by plain name, and what a client requests from `sv_downloadurl` for it; (9) the town's content palette: list the TF2/HL2 VPKs' mining-themed materials, models and skies, and pick the table | a findings table with numbers. **Checkpoint:** stop if R1 or R3 rules out the design |
| P1 | packages + projects | `NuGet.config`, `make host-pack`, the empty `Descent.Rules`, `.Shared`, `.Server` and `.Client` projects in the slnx (each its own commit); the reference-closure facts (Rules → Contracts only; Shared and Client → no SDK and no Contracts); a restore test that fails with the named message when `bin/nupkg` is absent | `make check`; `dotnet build` lands Host.Sdk + Grpc in `bin/debug/bin/managed` |
| P2 | mods.yaml-driven discovery and mod registration | `ServiceContainer.AddFactory`; `[SideService]` (with `As`) on the server, then the client; `ISideServiceRegistrar` and its refusals; user-message discovery; disposal on shutdown (§2.2, §2.7(8)); each with its facts | `make check`; the test mod's containers and message tables unchanged |
| P3 | core additions | §2.8's services, one commit each: `ILog`, `IGameThreadDispatcher` (and DevApi moved onto it), `IGameThreadLiveness`, `IFrameScheduler`, `IModInfo`, `IProcessEnvironment`, `IEngineClients` (SteamID on `IFwdEngineServer`, address, kick, client command), `IClientLifecycle` + its driver (with the disconnect of §2.3), `IBoundPort`, `IServerConsole`, `IChat`, `ILevelInfo`, `OnLevelReady`, `IHooks<T>`; each with its facts and an asserting fake; then the Descent DI-enforcement fact | `make check`; the live gate (runtime change) |
| P4 | Descent.Rules | port of FakeRules; determinism facts (same inputs → same bytes, 1000 seeds); the reference-closure fact; `PathModuleSource` with a fact that a byte-loaded assembly is described by path and its hash matches the file | `make check` |
| P5 | Descent.Server lifecycle | HostLink: the SDK thread, both queues, liveness-gated pump, game-thread resumption, `SdkThreadGuard` (§2.9). HostService: start, Booting/MapReady with retry/refusal policy, unhosted mode. Facts drive it against **Host.Local in-process** (`-hostlocal inproc` path, test-only reference) | `make check`; `make dedicated MOD=descent` against `make -C $(HOST_ROOT) local` reaches `instance.live` in the local service's audit and stays up past 120 s |
| P6 | players | PlayerService: join, lease, checkpoint timer, leave, `LeaseLost`, drain refusal | `make check`; a real client joins the local hosted dedicated server, and `/ops/leases` shows the lease |
| P7 | commands + client | Descent.Shared's messages and Descent.Client's handlers (retry, notice, HUD line). CommandService: hop ordering fact (Ready never before Release, by a recording fake), Retry, Kick, Drain, Shutdown (exit 0, everyone checkpointed), Say, Exec | `make check`; `descent_travel descent` hops a client between two local instances and back with the sheet unchanged |
| P7b | town | `DescentTown` + the `town` subcommand + `GENERATED_MAPS`; the palette table and its existence fact; `logic_level_transition` in Descent.Server and its facts; arrival spawn selection (§2.10) | `make check`; the town compiles leak-free, loads twice, and the cage button hops a player in the local hosted run |
| P8 | mod + dedicated | the mods.yaml schema (§2.5): `defaults` + overrides, `engine.client`/`engine.dedicated`, `default_mod`, `{id}`; the merge in `SourceSharp.Mods` and its Python port in `tools/setup`, with the parity fact and the test mod's no-change fact; `-dedicated` and `-resolve` in `source#`; `descent` in `game/mods.yaml`; `game/mod_descent/` (gameinfo with TF2 + pool paths, cfg seeds); the TF2 content link in `tools/setup`; `descent_town` in the mod's maps (built in P7b); `server_srv.so` link; `-engine`/`SOURCESHARP_ENGINE_DIR`; `make dedicated`, `make local-hosted`, `make join`, `make mod-image` (§2.5, §2.7); `tools/launcher-name-test` extended to the new launch path; mods.yaml facts that `descent` validates, `default_mod` is still `test`, and lists replace rather than append; `make mod-image` writes `mod.dedicated.yaml` | `make check`; `make live-gate` (test mod unaffected) |
| P9 | the real tier | with the host's `make deploy TIER=real MOD_IMAGE=<our mod-image>`: spec §10 criteria 1–4. **Checkpoint: deploying to k3s and the restart test are outward-facing enough to ask first** | the four criteria, each recorded with the audit lines that show it |

P8 can move before P5 if the dedicated launch is wanted for P5's local gate;
otherwise P5–P7 run `source#` with `-mod descent` on the listen engine.

## 4. Risks and the probe that retires each

| # | risk | probe | if it bites |
|---|---|---|---|
| R1 | a hibernating dedicated server stops calling GameFrame/Think, so the liveness stamp freezes, the SDK thread stops pumping, and the instance is reaped at 15 s | P0(1) | `sv_hibernate_think 1` in the mod's args/cfg; failing that, `sv_hibernate_when_empty 0` |
| R2 | engine state touched off the game thread, or SDK calls off the SDK thread | the affinity checks of §2.9 (fakes and `SdkThreadGuard`), with fail-first arms | a violation fails a fact. Nothing ships with one |
| R8 | the to-game queue backs up (a slow frame, a burst of commands), so kicks and hops lag | the backlog metric, under P7's hop gate | raise the per-frame budget; a kick or hop must never wait more than 2 frames |
| R3 | a client without Descent.Client cannot be moved by `descent_retry` | P0(3) measures whether stock `retry` from the server works | keep the user message as the path; a server-sent `retry` only as the fallback if P0 shows it works |
| R4 | the bound-port reader picks the wrong socket (HLTV, Steam's query port) | P0(4) with `-nohltv` and without | key on the socket the engine was given via `-ip`; refuse to send MapReady on ambiguity, loudly |
| R5 | hot reload (collectible ALC) leaves the SDK's gRPC channel and timers in a dead context | a fact that unload disposes the SDK; the live gate's second map load with `descent` | §2.7(8): generic disposal of `[SideService]` singletons; re-Start after reload, re-using the instance id |
| R6 | Grpc/Protobuf versions clash with assemblies already in `bin/managed` | P1 build log | pin in Descent.Server, matching the host's versions |
| R7 | the spec changes (the user is still reviewing it) | the host session will tell us | re-read the spec before each phase; cite its sha in commits |

## 5. Done when

1. Locally, against `make -C $(HOST_ROOT) local`:
   - the hosted dedicated `descent` server reaches `instance.live` and stays up
     past 120 s while idle (hibernation included);
   - a real client joins, and the lease is visible;
   - `descent_travel` hops it to a level and back with the inventory unchanged;
   - a service restart is survived (`instance.adopted`).
2. Spec §10 criteria 1–4 on `TIER=real` (P9, after asking).
3. `make check` green, and `make live-gate` green with the test mod and its
   second map load. The test mod's behaviour is unchanged: `default: test`, no
   Descent assembly loaded.

## 6. Decisions (the user, 2026-09-30)

1. **Unhosted: allowed for local testing.** It is an explicit `-unhosted` flag
   that the local make targets pass. It never applies silently and never in a
   pod (§2.4, §2.7(9)).
2. **Exec output: managed-only capture is fine.** Engine spew capture is not
   needed, and the divergence is recorded in §2.3.
3. **Dedicated locally: yes.** The 243750 client's `srcds_linux64` is used
   through `-engine` (pending P0(5) for the path).
4. **Mod image:** lives here (§2.5).
5. **Host requests: yes.** §2.7 items 2, 4, 6 and 11 are sent to the host
   session. Their answers are recorded here as they arrive.

---

## 7. P0 findings (2026-09-30, host-sdk at c26accd and after)

| probe | result | instrument |
|---|---|---|
| (1) hibernation | An empty dedicated server logs `Server is hibernating`, but `ServerThink` (the per-frame path that stamps liveness and drains the game-thread queue) keeps running at the tick rate: 668 crossings in 10 s with `sv_hibernate_think 0` and 668 with `1`. **R1 retired.** The cvar stays in the seed `server.cfg`, though this measurement does not show it has any effect. Idle with no map, `ServerThink` ran 12,897 times too | dev API `/status` crossings, sampled 10 s apart, on dm_lockdown |
| (2) connect/active pairing | **Superseded.** The address comes by entity index from `GetPlayerNetInfo(entIndex)->GetAddress()` (P3), so connect and active are never paired by name | n/a |
| (3) server-sent `retry` | **Superseded** by the user's ruling: an explicit `SsReconnect` server→client message whose client handler reconnects. The client still offers no general console command | n/a |
| (4) bound port | Asked for 27215 while a UDP socket held it: the engine bound **27216**, `hostport` still read **27215**. The kernel table showed UDP 27005 (`clientport`, opened even on a dedicated server) and 27216. `ProcBoundPort` sets `clientport` aside and returns 27216. Reporting `hostport` would have sent the service the wrong port | `/proc/<pid>/fd` socket inodes against `/proc/<pid>/net/udp{,6}`; `hostport` via the dev API |
| (5) local `srcds_linux64` | 244310 ships no `srcds_linux64` but holds the 64-bit `*_srv.so`; 243750 ships `srcds_linux64` and `libsteam_api`, `filesystem_stdio`, `libtier0`, `libvstdlib`. `source# -mod descent -dedicated` runs 243750's exe in 244310's directory with both `bin/linux64` on the library path (`engine.dedicated.exe_appid`) | `ls` of both installs; the dedicated runs below |
| (5b) **the dedicated engine cuts argv[0] at a space** | With argv[0] = `…/Source SDK Base 2013 Multiplayer/srcds_linux64`, `path` showed `BASE_PATH` = `…/steamapps/common/`. `|all_source_engine_paths|` mounted nothing, and the map died on a missing HL2 model (SIGSEGV). Fixed in the launcher: a dedicated run with a space in its install goes through `bin/<config>/engines/dedicated/`, symlinks to the engine directory's entries plus the exe, as cwd and argv[0]. The map then loads | engine `path` command; the crash, then the clean load |
| (6) disconnect timing | The standalone player's removal reaches managed code in time: the client quit, and the service audited `lease.release` after `character.create`. The leave path (checkpoint, release, PlayerLeft) ran from `OnClientDisconnected` | the host's `/admin/audit`, P6's join |
| (7) relative TF2 mount | `|gameinfo_path|../../content/tf/*.vpk` mounts (`path` lists the three TF2 VPKs under `bin/debug/content/tf`) | engine `path` command |
| (8) pool level by plain name | A level at `mods/descent/pool/maps/descent-1-abc.bsp`, with gameinfo's `game |gameinfo_path|pool`, loads as `+map descent-1-abc`. The client asks fastdl for `maps/descent-1-abc.bsp.bz2`, the path Q18 serves (the engine's download name is maps/<mapname>.bsp). Host item 1 (mount the overlay at `{modDir}/pool/maps`) stands | a scratch copy of dm_lockdown, deleted afterwards |
| (9) town palette | See §2.10: every candidate exists in the TF2 VPKs (131,498 entries listed) | VPK v2 directory parse |

## 8. Where the implementation departed from §2 (as built on host-sdk)

| plan said | as built | why |
|---|---|---|
| `descent_retry` (Descent.Shared) runs `retry` on the client's console (§2.3) | A core message, `SsReconnect` (SourceSharp.Shared, appended last), whose client handler calls `IClientReconnect` → `IFwdEngineClient.Reconnect()`: the ClientCmd slot with the fixed text `retry` | The client offers no way to run a console command (VtableBindingTests' policy fact). The user's ruling: "make an explicit server => client message that enables them to retry" |
| `ISideServiceRegistrar.Side` property (§2.2) | Registrars carry `[SideService(side)]` like services; the interface has only `Register` | Reading an instance property meant building another side's registrar with services that side lacks |
| a mod's user messages are found in assemblies holding an extension (§2.2) | The core already scans every loaded assembly. The registry now puts the shared anchor assembly's declarations first, and `[SharedAssembly]` marks a mod's shared assembly | `Descent.Shared` sorts before `SourceSharp.Shared`, which would otherwise shift every core wire index |
| `IEngineClients`, `IClientEvents` (§2.8) | `IClientInfo` / `IConnectedClients`; `IClientLifecycle` with its driver | An `IEngine*` interface is enrolled as an engine area by naming rule; the user asked for hook interfaces, not events |
| an up transition: the level above, or ReturnThroughPortal to the town (§2.10) | `TownPortal` | The SDK has no stairs up |
| local hops through `local-level-<id>.json` (§2.7(6)) | `tools/local-hosted` reads `map` and `levelFiles` from that file and links them into `pool/maps` | Agreed with the host session; the fields land on its side |
| a dedicated run is `source# -dedicated` (§2.5) | It also goes through `bin/<config>/engines/dedicated/` when the install path has a space | P0(5b): the dedicated engine cuts argv[0] at a space |

## 9. Live gates run (local, against the host's real service)

| gate | result | instrument |
|---|---|---|
| P5: the hub boots and goes live | `[Descent] booted: hub depth 0, module Send`, `[Descent] live on port 27015`; the service audited `instance.boot_reported`, `instance.map_ready`, `instance.live`; up over 300 s with no later failure event. The first attempt reported its real port, 27115, and the service refused it as `port_mismatch` (expected 27015). The game failed loudly, as designed | dev API managed log; `/admin/audit` on the local service |
| P6: a client joins | A Steam client (`make join` over the LAN address) was admitted, `joined as hero-<steamid> (soldier)`; audit `character.create`, then `lease.release` when it left | the same |
| found on the way | (a) PostInit was never dispatched (fixed, 73ba9bd); (b) a client's own listen server must not fail as "unhosted" (fixed, 18645b4); (c) clients are insecure, so the dedicated role is `-insecure`; (d) a dedicated server ignores connectionless packets from 127.0.0.1; (e) `IServerConsole.Quit(1)` exits 0: the engine's quit ignores `Environment.ExitCode`. Harmless for the host, which treats any exit as a crash, but a divergence from the spec's "exit non-zero" | logs above |
| open | P7's hop needs the host's `map` / `levelFiles` / `port` in the local level file (agreed, waiting on the host's user); P9 is the user's checkpoint | — |

## 10. Wrapped up (the user, 2026-10-01): P7's hop and P9 deferred

Merged to main with P0-P6, P7b and P8 done, and P7's code (commands, travel,
reconnect) in place and unit-tested. Left for later:

- **P7's live hop.** It needs the host's local connection files to carry
  `map`, `levelFiles` and a per-instance `port` (agreed with the host
  session, waiting on its user). Then `make local-hosted` starts a level
  process for each level file, and `descent_town`'s cage button can be
  driven end to end.
- **P9, the real tier.** `make mod-image CONFIG=release`, a push to the
  cluster's registry, `make deploy TIER=real` in the host repo, and spec
  §10's four criteria. The push and the deploy are the user's call.
- **The live gate's `push` step** failed 2 of 12 trials here. It is the known
  "jam" flake: the same shape on engineapi-vtables (3/62) and server-novalve
  (2/42, including one of three on 2026-10-01), so not this branch. A base
  control was started and stopped at wrap-up.
- **`IServerConsole.Quit(1)` exits 0** (§9): a divergence from the spec.


## 11. Resumed (the user, 2026-10-02): P7's hop and P9

The user: "Do it, use the clang build, fix local hops, use local cluster,
insecure is fine for now". Rulings:

- **The target is the local k3s cluster** (the host's `TIER=real` selects
  `deploy/k8s/local`; registry `localhost:30500`). A public tier is its own
  plan.
- **The image packs the clang build** (`bin/release`), not `make ci-build`
  output. This box's glibc is 2.43, equal to the engine image's (Ubuntu 26.04),
  which is the ceiling `docs/mod-image.md` sets.
- **Insecure is fine for now**: the dedicated role keeps `-insecure`, and the
  join is recorded as insecure.
- **P7's hop is done now**, so criterion 3 (Hops) can pass on the cluster.
- The push, the deploy and the restart test are approved.


### 11.1 Results (2026-10-02)

All four §10 criteria pass on the local k3s cluster (host 27225ef; mod image
`localhost:30500/tf2-descent-mod:31347c3`, the release clang build). The
host session ran the cluster side, by the user's decision.

| Criterion | Evidence |
|---|---|
| 1. Boots | hub boot_reported → module.accepted → map_ready → live; pod up past 120 s, 0 restarts |
| 2. Joins | client through the gateway LB <gateway-ip>:27015 (insecure); lease and a SteamID-bound session on the hub |
| 3. Hops | `tf2_descent_travel 1 down` (service Exec): level pod booted, Retry, client reconnected through the gateway and loaded tf2_descent-1-…; `up` back to the town, lease back on the hub |
| 4. Survives a restart | clean run 13:44 UTC: adopted + reconnected, no lease.expired or release; the SDK's own 30 s fail-closed tripped (a 32 s gap) and the game held and re-leased the player, never kicked |

Local same-process hop (`tools/local-hosted --port 27215 --pack <library>`):
down and up both pass; the server side of a hop takes about 2 s.

Fixed on the way: the same-process hop (37e82c1); a lease lost to an
unreachable service holds and re-leases the player (9208c54);
`tf2_descent_travel` for pods without the dev API (31347c3); `fire-as` on the
dev API (b58debb); a timestamped console log per launch (0adcb59). On the
host side: fetch-level's output dir (b779415), no lease expiry during
adoption (38dbed5) or shutdown (f9ecf00), superseded sessions keep the lease
(27225ef).

Open: a client crash on the fourth level load in one process (town, level,
town, drop, town); a client level load of 7–10 s from local disk; the host's
NextLevel prefetch (SDK 2.1.0, D-H14) needs a client-side fetch.
