# plan_devapi_interfaces: the dev API links only its side's Interface assembly

## 0. The user's rulings (2026-10-02)

These reverse plan_side_isolation ruling 10 ("DevApi may use Abi").

- "dev api should not be linked to any other assemblies except a Server.Interface assembly that
  shares hook points between the server and dev api. This is the same model as the host sdk."
- DevApi.Client follows the same model: it links only a Client.Interface.
- DevApi.Contracts "should be abstracted away over the Client and server Interfaces". A
  DevApi.Shared assembly may exist if the two halves of the dev API need to share code.
- This session does the work as a lane on engineapi-vtables, after this plan is reviewed.

**Exit criteria**
- DevApi.Server references exactly: System.*, SourceSharp.Server.Interface and, optionally,
  SourceSharp.DevApi.Shared. Read from its metadata (AssemblyReferences and every TypeRef).
- DevApi.Client references exactly: System.*, SourceSharp.Client.Interface and, optionally,
  SourceSharp.DevApi.Shared.
- Server.Interface and Client.Interface reference System.* only, and so does DevApi.Shared.
- SourceSharp.DevApi.Contracts is deleted. No game assembly references DevApi.Shared.
- The dev API serves a live game on both sides exactly as today: tools/debug-api status, log,
  entities, commands, console, net and prediction views, the debug channel and the web UI's
  selftest.
- The Abi census loses its DevApi tiers. Every file they held "stuck" is re-ruled, and
  NotPlumbingCeiling falls to the measurement.

## 1. Where it stands (06f43ec)

| assembly | lines | references | what it names outside itself |
|---|---|---|---|
| DevApi.Server (src/debug_api) | 9,900 in 23 files | Abi, DevApi.Contracts | 55 Abi types and a few Server/Shared ones: the IEngine* areas through `DevEngine` (an IServiceLocator), ConVar/ConCommand/NativeConsole, EntityHandle/Vector/Angles/Output/Parameter, the NetSchema/NetTable/NetTraffic family, PredictionErrors, BuildInfo, Crossings, Log, LocalCommandLine. DevApiServer.cs alone is 3,567 lines. |
| DevApi.Client (src/debug_api_client) | 527 in 1 file | Abi, DevApi.Contracts | BuildInfo, Crossings, LocalCommandLine, Log, PredictionErrorRing/PredictionErrors, Side |
| DevApi.Contracts | 1,059 in 4 files | Abi | IDevApi and IDevApiClient (the side halves), DevApiJson, and the debug-channel protocol (DebugChannelProtocol/Codec/Command/Query/Message/Invitation) |
| who references Contracts | | | Shared, Server, Client, DevApi.Server, DevApi.Client |

**Loading.** The server's DevApiLoader does `Assembly.Load("SourceSharp.DevApi.Server")`, finds the
IDevApi implementation, and constructs it with the command line and the side's container. The
client's DevApiClientLoader does the same for DevApi.Client.

**Why it matters beyond tidiness.** The Abi census (docs/abi-residency.tsv) holds 100 Abi files
"stuck" because the DevApi assemblies name them and can see only Abi. They can't move to their
side's assembly until the dev API stops naming them.

## 2. The model (as the host SDK: game code implements contracts the tool calls)

```
SourceSharp.Server.Interface   (System.* only)   hook points + plain DTOs the server serves
SourceSharp.Client.Interface   (System.* only)   the same for the client
SourceSharp.DevApi.Shared      (System.* only)   what both dev API halves share: the debug-channel
                                                 protocol and codec, JSON shapes, utilities
SourceSharp.DevApi.Server  -> Server.Interface, DevApi.Shared
SourceSharp.DevApi.Client  -> Client.Interface, DevApi.Shared
SourceSharp.Server         -> Server.Interface   (implements its hooks; loads DevApi.Server by name)
SourceSharp.Client         -> Client.Interface   (the same for DevApi.Client)
```

- **Hook points are interfaces over plain data.** Strings, numbers, records and spans declared in
  the Interface assembly. Never an Abi type: the Interface can't see one. For example
  `IDevConsole.Execute(string)`, `IDevEntities.List(): IReadOnlyList<DevEntity>`,
  `IDevNet.Schema(): DevNetSchema`.
  - The server implements them in SourceSharp.Server (or Server.Interop where pointer work is
    involved) by translating its own types into the DTOs.
  - Exactly as the host SDK's game side implements Host.Contracts.
- **The game no longer knows the debug-channel protocol.** Today Shared, Server and Client name
  DebugChannelProtocol. Under the model, the game moves opaque bytes through a transport hook
  (`IDevTransport.Send(ReadOnlySpan<byte>)` and a receive callback) and the protocol lives in
  DevApi.Shared, owned by the two dev API halves. P0 measures whether any game-side use is more
  than transport; if one is, it is stated as a hook, not as a protocol type.
- **Loading is unchanged in shape.** The loader still loads by name into the side's context. It
  constructs the dev API with the command line and ONE object implementing the side's hook
  interfaces (or a small hub the Interface declares), instead of the side's container.
- **No statics.** Hook implementations are instances owned by the side's composition (the user's
  rule). The two-instance fact applies: two server compositions serve two dev APIs that don't
  share state.

## 3. Phases (one commit per step; make check throughout)

| # | phase | what | gate |
|---|---|---|---|
| P0 | inventory | Every member of Abi/Server/Shared/Client that DevApi.Server, DevApi.Client and DevApi.Contracts call or name, grouped into candidate hook interfaces with their DTOs. Every game-side use of the contracts, split into transport and anything more. Recorded in §5 of this plan. | the table, reviewed with the integrator |
| P1 | the empty assemblies and their facts | Server.Interface, Client.Interface and DevApi.Shared, with no members, wired into the build and deploy. Closure facts (metadata) for every exit-criteria reference rule, each with a fail-first arm that plants a reference and shows it reported. The facts are marked pending for the dev API assemblies until P4. | make check |
| P2 | server hooks | Group by group (console and convars; entities and spawning; net schema, payload and traffic; prediction; build and crossings; logs; map/BSP data): declare the hook and its DTOs in Server.Interface, implement it in Server, switch DevApi.Server over, and delete its Abi use for that group. The BSP and netcode reporters get DTO inputs or their own copies of the pure data types they parse. | make check; live: tools/debug-api exercises that group on a live server |
| P3 | client hooks | The same for DevApi.Client over Client.Interface. | make check; live: the client views through the server's proxy (/client/{slot}/...) |
| P4 | contracts dissolved | The protocol and codec move to DevApi.Shared. The game keeps only transport hooks. IDevApi/IDevApiClient move to the Interfaces as the hook surface. DevApi.Contracts is deleted, along with every game assembly's reference to it. The P1 facts drop their pending marks. | make check; the live gate; the debug channel live (handshake, query, prediction push); the UI selftest |
| P5 | census | AbiResidency loses its DevApi tiers. Re-rule the formerly stuck files and lower NotPlumbingCeiling. Then the Phase 1 moves they unblock go as their own waves. | make check; the live gate once at the end |

## 4. Risks

- **DevApiServer.cs is 3,567 lines.** P2 must keep each group's commit reviewable, with no drive-by
  refactors.
- **The JSON the UI reads must not change shape.** DTO translation must preserve field names and
  values. The UI's tests (the ui leg) and the selftest are the guard; add golden JSON facts for
  each endpoint touched, before touching it.
- **The test project references everything.** It may keep testing the dev API through the
  Interfaces with fakes, which is the point of the model.

## 5. P0 inventory (measured at 96d57d1)

**Instruments.** Every number below was taken at 96d57d1, from a debug managed build of that tree, unless it says otherwise.
- **Roslyn walk.** A scratch tool (Microsoft.CodeAnalysis.CSharp 5.0.0; uncommitted, in the lane's scratch dir) rebuilds each
  project's compilation from its own `csc` command line (MSBuild `CscCommandLineArgs`, with the source generators run). It
  resolves every name, call, construction, operator, conversion, foreach, base type, interface implementation and expression
  type through the semantic model. It records each symbol whose assembly is another SourceSharp assembly. It reads symbols,
  not text, so the BSP records' `Angles` and `Parameter` *properties* are not counted as types.
- **Metadata cross-check.** The same tool lists each built DLL's TypeRefs.
  - DevApi.Server: Roslyn finds 61 Abi types; metadata finds 59 top-level TypeRefs plus the nested `ReloadStore.ReloadTally`,
    which is 60. The 61st is `NetTrafficSampler`, used only for its `const`s, which are inlined and never reach metadata.
  - DevApi.Client agrees exactly (9 types).
  - DevApi.Contracts differs only by `PredictionErrorAnalysis`, again const-only.
  - On the game side, the TypeRefs are DebugChannelInvitation (Shared, Server, Client), IDevApi (Server) and IDevApiClient
    (Client). `DebugChannelProtocol` is const-only from all three.
  - The cross-check caught one bug in the tool, a dropped generic type argument (`Action<DebugChannelInvitation>` in
    ClientModule). The bug was fixed and every count below rerun.
- **Route closures.** For each `case` of `DevApiServer.Route`, and each `if` in its default arm, the tool follows calls
  inside the assembly transitively, including dispatch through interfaces. It records the external members the case
  reaches, the `IDevApi` delegates it reads, and the JSON key literals (`"key":`) in its string literals and interpolations.

**Corrections to §1.**
- The dev API names nothing in the Shared.Interop, Server.Interop or Client.Interop assemblies (they exist at 96d57d1; the dev API does not reference them).
- DevApi.Server names **no** Server, Shared or Client type: it references only Abi and Contracts.
- Its Abi count is 61 types, not 55.
- Constants never appear in metadata, so the P1 metadata facts cannot see a const-only coupling: `NetTrafficSampler`,
  `PredictionErrorAnalysis`, and `DebugChannelProtocol` from the game side. The csproj reference rule is what catches those.

### 5.1 Totals

| assembly | rows (symbol uses) | lines with an external use | Abi types / members | Contracts types / members |
|---|---|---|---|---|
| DevApi.Server (23 files) | 1,597 | 544 | 61 / 238 | 8 / 73 |
| DevApi.Client (1 file) | 147 | 39 | 9 / 12 | 7 / 22 |
| DevApi.Contracts (4 files) | 219 | 84 | 23 / 54 | (itself) |
| **union over the three** | | | **67 types, 266 member names** | |

Per file:

| file | lines | Abi types / members | Contracts types / members | lines with an external use |
|---|---|---|---|---|
| DevApiServer.cs | 3,567 | 44 / 120 | 5 / 54 | 272 |
| NetTableJson.cs | 513 | 15 / 62 | 0 | 102 |
| PredictionJson.cs | 181 | 4 / 32 | 0 | 40 |
| EntitySpawner.cs | 473 | 6 / 13 | 0 | 31 |
| NetTrafficJson.cs | 127 | 4 / 18 | 0 | 28 |
| DebugChannelListener.cs | 612 | 3 / 4 | 5 / 10 | 18 |
| DevEngine.cs | 46 | 9 / 1 | 0 | 16 |
| DebugChannelHandshake.cs | 432 | 1 / 0 | 3 / 13 | 15 |
| EntityTargets.cs | 82 | 2 / 2 | 0 | 9 |
| NetPayloadSampler.cs | 130 | 2 / 4 | 0 | 7 |
| ModLayout.cs | 81 | 1 / 1 | 0 | 5 |
| ConsoleTail.cs | 241 | 1 / 1 | 0 | 1 |
| *no external use:* BspFile, BspFlags, BspLumpDetail, BspMaterials, BspReport, BspWriter, MapSource, EntityEventLog, HttpSocketServer, LogRing, UiAssets | 3,415 | 0 | 0 | 0 |
| DebugChannelClient.cs (Client) | 527 | 9 / 12 | 7 / 22 | 39 |
| DebugChannel.cs (Contracts) | 704 | 6 / 31 | | 53 |
| DevApiJson.cs (Contracts) | 105 | 7 / 23 | | 21 |
| IDevApi.cs (Contracts) | 237 | 10 / 0 | | 10 |

**The BSP and map code is already independent.** The six Bsp*.cs files and MapSource.cs (2,580 lines) name no Abi type.
Their `Output`, `Parameter`, `Angles` and `Origin` are members of their own records (`string Parameter`, `float[] Angles`).
- Abi's `Output` is named only by DevApiServer, for the static `Output.AnyFired` event (group G4).
- Abi's `Vector` is named only by DevApiServer and EntitySpawner (G2).
- The /map routes reach Abi only through the `CurrentMapName` delegate (G7) and `IEngineHost.ManagedDirectory` (ModLayout,
  G2).

So no data type is copied for BSP, and nothing there changes.

**The census (docs/abi-residency.tsv at 96d57d1).** 100 files are `stuck`.
- 21 are named directly by a dev API assembly: AmmoType, ConCommand, ConVar, ConVarFlags,
  EngineAreas.{Core,Entities,Players,World}, GameServices, INetTiers, LocalCommandLine, NativeConsole, NetEnums, NetSchema,
  NetTable, NetTraffic, PredictionErrors, SavedEntityCodec, ScriptedEntity, Services and UserMessage.
- The other 79 are stuck through a "named by stuck X" chain.
- Vector, EntityHandle, Log, BuildInfo, Crossings, ReloadStore, GitProvenance and Side are `plumbing` and stay in Abi. The
  dev API still may not name them after P4.

### 5.2 Every Abi member the dev API names, by candidate group

The letters say who names the type: S is DevApi.Server, C is DevApi.Client, K is DevApi.Contracts.

| group | types (members) | who |
|---|---|---|
| G1 console & convars | ConCommandInfo (Name, Help, Flags, IsRegistered, DeclaringType); ConVarBase (Name, Help, Flags, IsRegistered, DefaultValue, MinValue, MaxValue, RawValue, TypeName); ConVarFlagNames (Describe); ConVarFlags; IEngineConsole (ExecuteServerCommand); NativeConsole (Snapshot, ValueOf: static); NativeConsoleEntry (Name, DeclaredBy, Help, DefaultValue, IsCommand, FlagNames, Value, Min, Max) | S, K |
| G2 engine entities & spawning | IEngineEntities (FindAllByClassname, FindAllByName, FindByName, FireInput, GetAngles, GetClassName, GetHealth, GetIndex, GetOrigin, IsValid, Teleport); IEngineDamage (Apply, TakeDamage); IEnginePlayers (GetHandle); IEngineWorldQuery (TracePlayerAim); IEngineHost (ManagedDirectory); IGameClock (Now); IServiceLocator + ServiceLocatorExtensions (Require); EntityHandle (Value, None); Vector (new, X, Y, Z, Zero, Normalized, op_Subtraction); DamageType (Bullet) | S |
| G3 scripted entity catalogue | ScriptedClassInfo (ClassName, TypeName, Kind, LiveCount, Inputs, Outputs, Vars); ScriptedFieldInfo (Name, TypeName); ScriptedInstanceInfo (ClassName, Index, Handle, IsValid, Origin, Health, SpawnFlags, Outputs, Vars); ScriptedOutputInfo (Name, FireCount, LastValue); ScriptedVarInfo (Name, TypeName, Value) | S, K |
| G4 entity events | Output (AnyFired: **a static event**); ScriptedEntity (Handle, ClassName) | S |
| G5 net schema & payload | NetSchemaType (TypeName, ClassName, TableName, TypeId, SchemaHash, Shell, Fields, PropCount, BlobBytes, LayoutBytes, PayloadWords, Published); NetSchemaField (Name, Offset, Bytes, Kind, BitShift, Bits, IsPacked, EnumType, CsType); NetSchema (Find, Decode, Hex, ChangedRuns, Touches, StandaloneSharedProps: static); NetSchemaShell (Standalone, ==); NetShellKind (Standalone, NetEntity); NetFieldKind; NetEntityBlob (Found, Index, Handle, ClassName, Blob, Agreement, Missing); NetDecodedField (Field, Present, Value, Number, Hex); NetEnums (IsFlags, MembersOf: reflection over a `System.Type`); NetEnumMember (Name, Value); INetTiers (For, Ceiling); EmptyTiers (Instance); NetTierInfo (Table, Props, Words, PayloadBytes, Live); INetTypeRefusals (All); NetTypeRefusal (ClassName, TypeId, LayoutBytes); PayloadAgreement (Name, State, Agrees, Words, FirstBadWord); PayloadAgreementState (NotSupplied, ==) | S, K |
| G6 net traffic | NetTrafficReport (Sampling, ElapsedSeconds, TrackedEntities, Classes); NetTrafficClass (ClassName, Seen, ChangedFrames, BytesChanged, BytesPerSecond, RecentBytesPerSecond, WordsTouched, Fields); NetTrafficField (Name, Words, BytesPerSecond); NetTrafficSampler (WindowSeconds, BitsPerChangedWord, WordBytes: consts) | S, K |
| G7 status & services | ReloadStore (Saved, Restored, RestoreFailures: static); ReloadStore.ReloadTally (Entities, Classes, Fields, Bytes, Parts, PartFields, PartBytes, ShellLess, IoConnections, IoEvents); ReloadCarrier (Count, IoCount, PartCount, ShellLessCount: static); ServiceInfo (Key, Kind, Implementation, Built, LookedUpBy) | S, K |
| G8 build & crossings | BuildInfo (Managed, Native, ManagedFreshness, NativeFreshness: static); BuildHalf (Stamp, Source, Path); BuildStamp (IsKnown, Hash, BuiltAtUnixMs, Parts, Describe); BuildFreshness; GitProvenance (IsKnown, Commit, BranchName, Dirty, Describe); Crossings (Total, Active, ActiveEntityOps, OpCount, ServerFrames, ClientFrames, Resets: static); Side (Server, Client) | S, C, K |
| G9 log | Log (Msg, Warning, **Sink**: the dev API installs itself as the process log sink and restores it in Dispose) | S, C |
| G10 command line | LocalCommandLine (Has, Value: static helpers over an args list) | S, C |
| G11 prediction | PredictionErrors (Ring: **a static ring**); PredictionErrorRing (new, Add, Since, Reset, Count, Capacity, DefaultCapacity, NextSequence, Observed, Mispredicted, ToleratedOnly); PredictionErrorRecord (new, Sequence, TimeUtc, ClassName, TypeId, Command, Fields, BlobBytes, StandingBytes, FirstDiffOffset, ToleratedFields, Mispredicted); PredictionFieldError (new, Name, Kind, Offset, Bytes, DifferingBytes, MaxAbsDelta, Tolerance, ToleranceRatio, Tolerable, Tolerated, Components); PredictionComponent (new, Server, Predicted, Delta); PredictionErrorAnalysis (MaxFieldsPerRecord: const) | S, C, K |
| G12 user-message codec | UserMessageWriter (WriteInt, WriteString); UserMessageReader (ReadInt, ReadString, Remaining, Overran). Used only by `DebugChannelInvitation.Write/TryRead` | K |

Lines with an external use, by group:
- DevApi.Server: G1 74, G2 135, G3 38, G4 5, G5 129, G6 30, G7 30, G8 9, G9 13, G10 5, G11 54, Contracts protocol 40,
  `IDevApi` 1.
- DevApi.Client: G8 2, G9 1, G10 2, G11 9, protocol 30.

### 5.3 Proposed hook interfaces

**DTO conventions.** All DTOs are records or record structs in the Interface assembly. They carry strings, numbers, bools,
`IReadOnlyList<>` and `ReadOnlyMemory<byte>`. An entity handle is a `uint` (`0xFFFFFFFF` = none, never 0). A vector is a
`DevVec3(float X, float Y, float Z)`.

**Ownership.** The server implements every hook in SourceSharp.Server, as instances owned by ServerModule's composition.
It hands them to the dev API at construction as one hub (`IDevServerHooks`, one property per group). The hub replaces
today's `IServiceLocator` argument and the 21 `IDevApi` delegate setters.

| hook (Server.Interface) | proposed members | DTOs | implements today | endpoints |
|---|---|---|---|---|
| **G10**: none | args already reach the ctor; `Has`/`Value` become a DevApi.Shared helper | | `LocalCommandLine` (Abi, pure) | all (startup flags) |
| **IDevLog** (G9) | `Msg(string)`, `Warning(string)`, `IDisposable Subscribe(Action<DevLogLine>)` | DevLogLine(long Seq, bool Warning, string Text) | `Log` static (Abi Log.cs) | /log; every route's own messages |
| **IDevBuild** (G8) | `DevBuildInfo Build(bool clientHalf)`, `DevCrossings Crossings()` | DevBuildInfo (managed and native halves: hash, builtAtUnixMs, parts, source, path, freshness; git commit, branch, dirty), DevCrossings (7 counters) | `BuildInfo`, `GitProvenance`, `Crossings` statics (Abi) | /status, /prediction (choosing local vs channel), the client `status` reply |
| **IDevStatus** (G7) | `DevStatus Status()`, `IReadOnlyList<DevService> Services()`, `string CurrentMap { get; }` | DevStatus (map, ticks, constraints, springs, motionControllers, think, io, touch, mapSpawn, factories, logical, overlay, reload tallies: nested records replacing the 7 named tuples), DevService(Key, Kind, Implementation, Built, LookedUpBy) | ServerModule lambdas over ScriptedEntityRegistry.Directory, StandaloneHierarchy.Overlaps, MapSpawner, EntityFactories, IEngineHud.OverlayReadout, ServerTicks, IEngineConstraints/Springs/MotionControllers, ServiceContainer.Describe; the ReloadStore/ReloadCarrier statics | /status, /services, /map*, /entity/output, Pump |
| **IDevEntityEvents** (G4) | `IDisposable Subscribe(Action<DevOutputFired>)` | DevOutputFired(uint Handle, int Index, string ClassName, string Output, string? Value) | `Output.AnyFired` static event (Abi ScriptedEntity.cs) | /events |
| **IDevEntityCatalog** (G3) | `Classes()`, `Instances()` | DevEntityClass(ClassName, TypeName, Kind, LiveCount, Inputs, Outputs, Vars: DevField(Name, TypeName)); DevEntityInstance(Index, Handle, ClassName, Valid, Origin, Health, SpawnFlags, Outputs: DevOutputStat(Name, FireCount, LastValue), Vars: DevVar(Name, TypeName, Value)) | ScriptedEntityRegistry.DescribeClasses/DescribeInstances (Abi ScriptedEntity.cs) | /entityclasses, /instances |
| **IDevConsole** (G1) | `Execute(string)`, `ConVars()`, `Commands()`, `NativeEntries()`, `string? ValueOf(string)` | DevConVar(Name, Help, Flags, FlagNames, Registered, Default, Min, Max, Value, Type); DevCommand(Name, Help, Flags, FlagNames, Registered, DeclaredBy); DevNativeEntry(Name, IsCommand, Help, DeclaredBy, FlagNames, Default, Min, Max, Value) | ConVarRegistry and ConCommandRegistry (Abi); the NativeConsole static over IEngineConsole (an Abi engine area) | /convars, /commands, /convar, /command, and the console-driven /entity/input, /entity/output, /entity/connect, /entities/spawn |
| **IDevNetTraffic** (G6) | `DevNetTraffic Report()`, `Reset()` | DevNetTraffic(Sampling, ElapsedSeconds, TrackedEntities, WindowSeconds, BitsPerChangedWord, WordBytes, Classes: DevTrafficClass(…, Fields: DevTrafficField)) | INetTrafficProfiler (Server NetTrafficProfiler.cs) | /nettraffic |
| **IDevWorld** (G2) | `IReadOnlyList<uint> FindByClassname(string)`, `FindByName(string, int max)`, `DevEntity? Describe(uint)`, `bool FireInput(uint, string input, string? param, uint activator)`, `Teleport(uint, DevVec3, DevVec3? angles, bool zeroVelocity)`, `Damage(uint target, uint attacker, float amount, int type, DevVec3? force, DevVec3 at, bool direct)`, `uint PlayerHandle(int slot)`, `DevAimHit? TraceAim(uint player, float range)`, `float Now { get; }`, `string ManagedDirectory { get; }` | DevEntity(Handle, Index, ClassName, Origin, Angles, Health, Valid), DevAimHit(uint Entity, DevVec3 End) | IEngineEntities/IEngineDamage/IEnginePlayers/IEngineWorldQuery/IEngineHost (Abi engine areas over generated bindings); IGameClock (Abi GameServices.cs) | /entities, /entity/input, /entity/damage, /entities/spawn, /player/aim, /entity/output, /entity/connect, /map/* (ModLayout), Pump (event-log liveness), Dispose |
| **IDevNetSchema** (G5) | `IReadOnlyList<DevNetType> Types()`, `DevNetTiers Tiers()`, `IReadOnlyList<DevNetRefusal> Refusals()`, `DevNetPayload Payload(int index)` | DevNetType(TypeName, ClassName, TableName, TypeId, SchemaHash, Shell, PropCount, BlobBytes, LayoutBytes, PayloadWords, Published, Fields: DevNetField(Name, Offset, Bytes, Kind, BitShift, Bits, Packed, CsType, EnumFlags, EnumMembers)); DevNetTiers; DevNetRefusal; DevNetPayload(Found, Index, Handle, ClassName, Blob, Agreement, Decoded: DevNetValue(Field, Present, Value, Number, Hex)) | ServerModule.NetSchemaTypes and NetPayloadReader (Server); NetTiers and NetTypeRefusals (Abi, from the container); `NetSchema.Decode` (about 250 lines of game logic, so it stays a **hook result**); `NetEnums` (reflection, so the enum members travel in the DTO) | /nettables, /nettable/{type}, /entity/{i}/payload |
| **IDevPrediction** (G11, server) | `bool LocalClient { get; }`, `IReadOnlyList<DevPredictionRecord> Since(long, int)`, ring stats, `Reset()` | DevPredictionRecord, DevFieldError, DevComponent (copies of the Abi shapes) | the `PredictionErrors.Ring` static (Abi): **the in-process client's ring, read by the server** (question Q1) | /prediction |

**Client.Interface.** DevApi.Client has 13 lines with an Abi use, all in one file.

| hook | members | implements today | used by |
|---|---|---|---|
| IDevClientPrediction | `Since(long seq, int max)`, `Reset()` | the `PredictionErrors.Ring` static (Abi) | the prediction push and the ResetPredictionRing command |
| IDevBuild (client copy) | `Build()`, `Crossings()` | the BuildInfo and Crossings statics | the `status` reply (`/client/{slot}/status`) |
| IDevLog (client copy) | `Msg`, `Warning` | Log | the dev API's own messages |
| command line | args passed to the ctor | LocalCommandLine | `-debugui`, `-debuguihost` |

**Estimated hook members.**
- Server: about 35, in 11 interfaces. They replace 21 delegates, 7 engine areas, and the Abi statics listed in Q4.
- Client: 6.

### 5.4 Pure data types the dev API parses

| type | today | becomes |
|---|---|---|
| BSP records (BspFile/BspReport: entities, connections with `Parameter`, `float[] Origin/Angles`) | the dev API's own | unchanged: no Abi type is involved |
| `Vector` | Abi struct (X/Y/Z, -, Normalized, Zero) | the `DevVec3` DTO; Damage's direction math becomes local |
| `EntityHandle` | Abi struct | `uint`, with `None` = `0xFFFFFFFF` |
| `Output` | Abi class, named only for the static `AnyFired` event | the IDevEntityEvents subscription |
| netcode schema shapes (NetSchemaType/Field, NetEntityBlob, NetDecodedField, NetTierInfo, NetTypeRefusal, PayloadAgreement, NetEnumMember) | Abi | DTOs (G5). `Decode` stays a **hook result**. `Hex`, `ChangedRuns` and `Touches` (byte utilities, about 50 lines) become **dev API-local copies** |
| NetTraffic* | Abi | DTOs (G6); the three sampler consts become report fields |
| prediction records | Abi, and encoded by the Contracts codec | a DTO in Client.Interface (and in Server.Interface, for Q1); the codec's own **DevApi.Shared copy** for the wire |
| `DebugChannelInvitation` (Protocol, Port, Nonce) | Contracts; written and read by the game | a DevApi.Shared type with a span codec; the game sees bytes only (§5.5) |

### 5.5 Game-side uses of DevApi.Contracts (Roslyn; metadata agrees)

| assembly / file | uses | transport only? | proposal |
|---|---|---|---|
| Shared UserMessages.cs:599-651 | `[UserMessage(DebugChannelProtocol.MessageName)]`; `Action<DebugChannelInvitation> Accepted`; `DebugChannelInvitation.TryRead(ref UserMessageReader, ...)` and a refusal log | **no**: the game parses the invitation | Shared owns the message name (a game constant; the dev API never needs it) and hands over the raw bytes: `Action<ReadOnlyMemory<byte>>? Accepted`. Parsing and the refusal log move to DevApi.Client. Gate one is kept: when `Accepted` is null, nothing is read |
| Server ServerModule.cs:2665-2687 (InviteToDebugChannel) | `IDevApi.InviteDebugChannel(slot)` returns an invitation; `stackalloc[MaxInvitationBytes]`; `invitation.Write(ref UserMessageWriter)`; logs `invitation.Port` | **no**: the game encodes the invitation, and knows its size and the port | `int IDevApi.WriteInvitation(int slot, Span<byte> into)` in Server.Interface (0 = none, -1 = did not fit). The dev API logs the port; the game sends opaque bytes |
| Server ServerModule.cs:945-1122, 1542, 1578, 1842, 2437 | `IDevApi`: Start, Pump (×2), LevelShutdown, Dispose, and the 21 delegate setters | lifecycle plus hooks | IDevApi's lifecycle (Start, Pump, LevelShutdown, WriteInvitation, Dispose) moves to Server.Interface. The setters become the hook hub (§5.3). `DescribeStatus` returns a **JSON fragment built by the game**, which becomes DevStatus fields |
| Server DevApiLoader.cs | `IDevApi` (type test; `Activator.CreateInstance(type, args, IServiceLocator)`) | loading | constructs with (args, `IDevServerHooks`) |
| Client ClientModule.cs:208-218, 810-858 | `IDevApiClient` (field, `Attach` as a method group, Dispose); `DebugChannelProtocol.ClientParam` in a log line | lifecycle plus a constant | `IDevApiClient.Attach(ReadOnlySpan<byte>)` and Dispose move to Client.Interface. The loader keeps its own `EnableParam = "-debugui"`, as the server's DevApiLoader does |
| Client DevApiClientLoader.cs | `IDevApiClient`; `DebugChannelProtocol.ClientParam` | loading | constructs with (args, `IDevClientHooks`) |

The invitation's wire format must not change: today it is whatever `UserMessageWriter.WriteInt/WriteString` writes. P4
adds a golden-bytes fact before the codec moves.

### 5.6 DevApi.Shared (System.* only)

It holds what both halves use today from Contracts:
- DebugChannelProtocol's constants, all except `MessageName`.
- `DebugChannelInvitation`, with a `Span<byte>` codec in place of UserMessageReader/Writer.
- The `DebugChannelCommand`, `DebugChannelMessage` and `DebugChannelQuery` enums.
- `DebugChannelCodec`, with its own prediction-record copy and `MaxFieldsPerRecord` (today a const borrowed from Abi).
- `DevApiJson.Quote`, and the Build/Crossings JSON writers over DevApi.Shared records. Each half maps its Interface DTO
  into them.
- The `LocalCommandLine.Has/Value` helper.

That is about 810 of Contracts' 1,059 lines: DebugChannel.cs (704) and DevApiJson.cs (105). IDevApi.cs (237) and
IDevApiClient.cs (13) go to the Interfaces instead.

### 5.7 JSON endpoints (DevApiServer.Route, DevApiServer.cs:795)

There are 30 `case` labels and 4 default-arm prefixes, so 34 routes.
- **Hooks** are today's `IDevApi` delegates.
- **Keys** are the `"key":` literals in the route's transitive closure. That is the union of nested keys and error keys:
  a checklist for the golden facts, not a schema.
- **Tests** counts grep mentions of the route string in src/test/managed. A mention is not a golden fact.

| route | handler | params | hooks today | Abi groups | keys | tests |
|---|---|---|---|---|---|---|
| (empty) | Help :1068 | | | | endpoints | |
| /status | Status :1117 | | DescribeStatus, Think/Io/Touch/MapSpawn/Factories/Logical/OverlayReport | G2 G7 G8 | address allocated available blocks build bytes capacity classes clipped connections createCrossings crossings cyclesRefused deliveredLastFrame deliveredTotal drawn edgesLastFrame edgesTotal entities error exhausted factories failed fields first heapDepth io ioConnections ioEvents links live logical managedBlocks managedKeys mapSpawn native nativeBlocks nativeReach noOverlay noViewer ok overlay pairs partBytes partFields parts pending pid postedAfterThePass ranLastFrame ranTotal refusalTimes refused regrows released reload restored saved sdkAsks sdkTook shellLess spawnCrossings think ticking time touch unanswered unhandled unhandledDistinct viewerSet waiting; plus DevApiJson.Build/Crossings, and the game's DescribeStatus fragment (map ticks constraints springs motionControllers) | 3 |
| /log | Log :1371 | since, max | | G9 (via Sink) | at lines next seq text warning | 1 |
| /console | Console :1406 | since, max | | | at following lines next path seq text warning | 1 |
| /command | Command :1415 | body or c | | G1 G9 | error logFrom ok | 1 |
| /convar | ConVar :1440 | name, value or body | ReadConVarValue | G1 | error name value | 3 |
| /entities | Entities :1471 | class | | G2 | angles class classname count entities error handle health index origin | 1 |
| /convars | ConVars :1562 | source | ListConVars | G1 | convars count declaredBy default error expected flags help max min name registered source type value | 6 |
| /commands | Commands :1679 | source | ListConCommands | G1 | commands count declaredBy error expected flags help name registered source | 3 |
| /entityclasses | EntityClasses :1855 | | ListEntityClasses | G3 | classes className count declaredBy error inputs kind liveCount name outputs type vars | 1 |
| /services | Services :1797 | by | ListServices | G7 | built by count error expected implementation key kind lookedUpBy services | 5 |
| /nettraffic | NetTrafficJson.Report :19 | reset | DescribeNetTraffic, ResetNetTraffic | G6 | available bitsPerChangedWord bytesChanged bytesPerSecond classes className elapsedSeconds entityFramesChanged entityFramesSeen fields fieldsNote name note recentBytesPerSecond sampling trackedEntities unit windowSeconds wordFill words wordsTouched | 4 |
| /nettables | NetTables :1988 | | DescribeNetTables, ReadNetTiers, ReadNetRefusals | G5 | bits bitShift blobBytes bytes ceiling className count csType enum error fieldCount fields fill flags kind layoutBytes live members name nearCeiling offset oversized packed payloadBytes payloadWords props published refused schemaHash schemaHashHex sharedProps shell table tier tiers tiersLive type typeId typeIdHex typeName types value words | 4 |
| /nettable/{type} | NetTable :2092 | | DescribeNetTables, ReadNetTiers | G5 | as /nettables, plus asked known | 0 |
| /entity/{i}/payload | Payload :2138 | | DescribeNetTables, ReadEntityPayload, ReadNetTiers | G5 | as /nettable, plus agrees ascii blob blobShorterThanLayout changed first firstBadWord handle hex number payload present resized rowBytes rows runs sinceSeconds state | 0 |
| /prediction | Prediction :2018 | since, max, reset | | G8 G11 | atUtc available blobBytes bytes capacity class command components delta differingBytes fields firstDiffOffset held kind kindName maxAbsDelta mispredicted name nextSequence note observed offset predicted records sequence server standingBytes tolerable tolerance toleranceRatio tolerated toleratedFields toleratedOnly typeId unit | 11 |
| /debugchannel | DebugChannel :2059 | | | protocol | accepted address everAttached listening note outstandingInvitations port public recordsReceived refused sessions | 6 |
| /clients | AttachedClients :1055 | | | | slots | 1 |
| /client/{slot}/{q} | ClientQuery :1040 | | | protocol | error expected question slot; the body is the client's `status` reply (side, build, crossings) | 0 |
| /instances | EntityInstances :1902 (game thread) | | ListEntityInstances | G2 G3 | className count error fireCount handle health index instances lastValue name origin outputs spawnFlags type valid value vars | 3 |
| /map | MapInfo :2690 | map | CurrentMapName | | available brushModelCount connectionCount current directory entityCount error loaded map path version | 13 |
| /map/entities | MapEntities :2727 | map | CurrentMapName | | angles brushModel classname connections count delay entities error id input key keyvalues map model origin output param target targetname times value | 2 |
| /map/geometry | MapGeometry :2798 | map | CurrentMapName | | error faceIndices faceLengths faceStarts map maxs mins models origin vertices | 1 |
| /map/file | MapFile :2869 | map | CurrentMapName | | about 100 BSP keys (lumps, faces, props, cubemaps, worldlights …) | 4 |
| /map/lump | MapLump :3179 | map, lump | CurrentMapName | | about 50 per-lump keys | 5 |
| /map/save | SaveMap :3321 | map, as, body | CurrentMapName | G2 (Now) | backup bytes entityCount error map ok overwrote path | 0 |
| /events | Events :2176 | since, max | | G4 (via AnyFired) | classname count detail events handle index kind name next samplePeriod sampling seq time | 1 |
| /entity/input | FireInput :2225 | target, input, param, delay, activator | | G1 G2 | accepted activator cheatGated command error logFrom matchedBy ok target targets via | 3 |
| /entity/output | FireOutput :2441 | map, entity, output | CurrentMapName | G1 G2 | entity error fired logFrom map note ok | 1 |
| /entity/damage | Damage :2339 | target, class, amount, type, at | | G2 | amount classname count error gone healthAfter healthBefore hit ok type | 0 |
| /entities/spawn | SpawnGroup :2589 | body | | G1 G2 | classname created entities error index logFrom name ok requested | 0 |
| /player/aim | PlayerAim :2564 (game thread) | player | | G2 | aim error eye hit hitClass player | 2 |
| /entity/connect | Connect :2511 | source, output, target, input, param, delay, times | | G1 G2 | command error field logFrom ok | 2 |
| /ui… | UiAssets.Serve :36 | | | | asset error live stamp | 6 |

Outside Route:
- The WebSocket upgrade on `DebugChannelProtocol.Path`, in DebugChannelHandshake and DebugChannelListener (protocol, G10,
  G11).
- The lifecycle closures: the ctor (G1 NativeConsole, G2), Pump (G2, G9, CurrentMapName) and Dispose (G2, G4, G9).

### 5.8 Questions for the integrator (each changes what P2 builds)

- **Q1: listen-server prediction.** /prediction reads `PredictionErrors.Ring`, the process-wide static that the
  *in-process client* writes, after checking `BuildInfo.Managed(Side.Client)`. A server dev API that sees only
  Server.Interface cannot see client state. Two options:
  - (a) An `IDevPrediction` hook that the server implements over the Abi static. This keeps today's behaviour, but the
    server's composition reads a client static.
  - (b) The listen client attaches over the loopback debug channel, like a remote client. This removes the cross-side
    read, but the listen client then needs `-debugui`, and the report runs a round trip behind (DevApiServer.cs:2025 argues
    against this).
  - Recommendation: (a) in P2, for parity; (b) as its own later step.
- **Q2: DTOs both Interfaces need** (build info, crossings, prediction records). Neither Interface may reference the other
  or DevApi.Shared. So each declares its own small records, and each dev API half maps them to DevApi.Shared's. Is that
  duplication acceptable, or should the exit criteria allow a shared System-only Interface assembly?
- **Q3: `Log.Sink`.** Today the dev API *replaces* the process log sink, a static that two dev API instances would fight
  over. `IDevLog.Subscribe` turns it into a subscription. The Abi static stays ("the owner's one permitted static"), so the
  server's IDevLog implementation must multiplex it.
- **Q4: static engine state behind the hooks.** The hooks wrap `Output.AnyFired`, `PredictionErrors.Ring`, `NativeConsole`,
  `ReloadStore`, `ReloadCarrier`, `Crossings` and `BuildInfo`. They make the *dev API* instance-only. Making those statics
  per instance is outside this plan.


**Rulings (2026-10-02).**
- **Q1, from the user:** the listen client's prediction data goes through the client's dev API. DevApi.Client serves
  it over Client.Interface, and the server proxies it under `/client/{slot}/prediction`, like the other client views.
  The server's `/prediction` shows server-side data only. This survives per-side Abi (P5), where the server cannot see
  the client's static.
- **Q2, from the user:** duplicate the small records each Interface needs: build info, crossings and the prediction
  record. A fact holds the two copies' shapes equal, field by field, so they cannot drift.
- **Q3, the integrator:** a subscription (`IDevLog.Subscribe`), never a replaced process sink. Two dev API instances
  must not fight over it (the user's no-statics rule). The server's implementation multiplexes until statics-to-zero
  removes the static.
- **Q4, the integrator:** the Abi statics behind the hooks stay for now. The hooks make the dev API per instance;
  statics-to-zero and plan_side_isolation P5 make what is behind them per side. Each hook's implementation is an
  instance owned by its side's composition.

### 5.9 Proposed P2 order (smallest and most independent first)

**lines** is the number of DevApi.Server lines with an external use (measured). **hook members** is an estimate.

| # | group | lines | hook members | notes |
|---|---|---|---|---|
| 1 | G10 command line | 5 | 0 | args already reach the ctor; the helper is copied |
| 2 | G9 log | 13 | 3 | Q3; ConsoleTail and DevApiServer |
| 3 | G8 build & crossings | 9 (+21 in DevApiJson) | 2 | the DevApiJson writers move into the dev API |
| 4 | G4 entity events | 5 | 1 | the DTO needs only handle, index and classname |
| 5 | G3 entity catalogue | 38 | 2 | replaces 2 delegates |
| 6 | G7 status & services | 30 | 3 | replaces 10 delegates and the game-built JSON fragment; carries CurrentMap for /map* |
| 7 | G6 net traffic | 30 | 2 | replaces 2 delegates |
| 8 | G1 console & convars | 74 | 5 | replaces 3 delegates, NativeConsole and IEngineConsole |
| 9 | G2 engine entities | 135 | ~11 | removes DevEngine and IServiceLocator; the most lines touched; also EntitySpawner and EntityTargets |
| 10 | G5 net schema & payload | 129 | 4 | replaces 4 delegates; Decode stays a hook result, the byte utilities are copied |
| 11 | G11 prediction (server) | 54 | 3 | after the Q1 ruling; shares DTO shapes with P3/P4 |

After P2:
- P3, the client: G8, G9, G10 and G11 over Client.Interface (13 lines).
- P4, the protocol: 40 lines in DevApi.Server, 30 in DevApi.Client, and the 5 game-side files in §5.5.
