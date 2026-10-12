# Entities in managed code

A plan for moving every entity, on both sides, into C#, and reducing what C++
holds about an entity to the vtables the engine calls through. It follows the
two principles in `plan.md` §1a: native code exists only to bind the engine, and
nothing is a static or a global.

**Definition of done.** Three properties of the built binaries, each checked by
a command (Appendix A), plus a game that plays:

1. **No base entity in native code.** Neither `server.so` nor `client.so`
   defines a member of `CBaseEntity`, `C_BaseEntity`, or any class derived from
   them. The names survive only as opaque forward declarations inside engine
   headers that this project does not compile (`IServerUnknown::GetBaseEntity`,
   `IClientUnknown::GetBaseEntity`, `IServerGameEnts::EdictToBaseEntity`), and
   every such method answers `NULL`. No shim class inherits from a stock entity
   class.
2. **No entity list in native code.** No `CGlobalEntityList`,
   `CClientEntityList`, `CBaseEntityList`, `IEntityListener`, and no native
   structure that can iterate entities, find them by name or class, or be
   listened to. The one native table that remains is the engine-index slot
   table (§3.3), which holds a shell pointer per index because the engine's
   own APIs are index-based, and which cannot iterate.
3. **No entity casts.** Over every translation unit compiled into either DLL,
   the count of every way of turning something into an entity pointer reads 0:
   `dynamic_cast`, C-style and `static_cast` to an entity type, `assert_cast`,
   `ToBasePlayer` and kin, `CBaseEntity::Instance`, `EntityFromEntityHandle`
   and its three siblings, `GetContainingEntity`, `GetBaseEntity()`, a
   `GetGameData()` cast, and any `CHandle<T>` with an entity `T` (§3.16). The
   one downcast on an entity path is inside the slot-table resolver, which
   checks the serial before it returns.

"The game plays" is the live gate in §5: a player joins, moves, shoots, picks
up, triggers, rides, dies, respawns, and reloads the map; two players see each
other; a hot reload preserves the world.

**What this plan does not do.** It does not port the physics solver, the studio
bone math, the material system, VGUI, or the renderer's world pass. Those are
mechanism, they contain no decision about what the game does, and §6 lists them
with the reason each stays.

---

## 1. Where the tree stands

Measured on the fork's working branch at `9f8ba7b8` (2026-09-05), from the ninja
object tables and the built binaries, not from vpc prose. Commands are in
Appendix A.

| | server | client |
|---|---|---|
| objects linked | 160 | 286 |
| … stock `game/server` / `game/client` | 35 | 155 |
| … stock `game/shared` | 20 | 41 |
| … stock `public` | 4 | 19 |
| … SourceSharp shim | 101 | 71 |
| stock TUs that **define** an entity class | 12 (25,904 lines) | 15 (21,305 lines) |
| stock TUs that **use** the base class but define none | 28 (35,036 lines) | 83 |
| stock TUs with entity-free text (all still include `cbase.h`) | 15 (3,354 lines) | 98 |
| `CBaseEntity::` / `C_BaseEntity::` members in the binary (`nm --defined-only`) | 1,207 | 602 |
| `CGlobalEntityList::` / `CClientEntityList::` members | 33 | 91 |
| `dynamic_cast` sites in compiled stock TUs | 59 in 14 files | 68 in 27 files |
| `dynamic_cast` sites in the shim | 11 | 16 |
| entity-list call sites in the shim | 18 `gEntList.` + 13 `g_pEntityList->` | 25 |

The two game DLLs are already built from flat, positive file lists
(`src/game/server/server_csharp.vpc`, `src/game/client/client_csharp.vpc`);
neither includes `server_base.vpc` or `client_base.vpc`, and neither has a
`-$File` line. That matters: every removal in this plan is a line deleted from
one of two files, and the linker reports exactly what the deletion costs.

**What already exists and is kept.** The shape of the end state is present on
both sides; what is missing is the deletion of everything beside it.

| | where |
|---|---|
| A server entity that is not a `CBaseEntity`: `CStandaloneEntity : IServerEntity, IServerNetworkable, ICollideable, IGameEntity` | `src/sourcesharp/game/server/shell_standalone.h:166` |
| A client entity that is not a `C_BaseEntity`: `C_StandaloneEntity : IClientEntity, ICollideable, IGameEntity` | `src/sourcesharp/game/client/shell_standalone_client.h:92` |
| The blob allocated past the object, sized per class, with no un-sized `new` | `shell_standalone_base.cpp:546-573`; client `shell_standalone_client.cpp:2182` |
| Runtime send/recv tables built from one shared declaration | `shell_nettable.cpp`, `shell_recvtable.cpp`, `SourceSharp.Abi/StandaloneNetStates.cs` |
| Blob-backed managed fields with compile-time layout | `SourceSharp.Generators/NetStateGenerator.cs`, `SourceSharp.Abi/NetState.cs` |
| Map keyvalues, inputs, outputs, spawnflags declared by attribute | `SourceSharp.Abi/ScriptedEntity.cs`, `ScriptedVarsGenerator.cs` |
| 284 map classnames on 186 managed server classes; 46 managed client classes | `SourceSharp.Server/Entities/**`, `SourceSharp.Client/Standalone/**` |
| Handle-keyed registries owned by the side module on both sides | `ScriptedEntity.cs:1518`, `Client/Standalone/ClientStandaloneRegistry.cs:16` |
| A fake engine that makes entities testable without Source (64 test files use it) | `SourceSharp.Abi/RecordingEngine.cs` |
| Shared movement arithmetic compiled once and called by both sides | `SourceSharp.Abi/MoveMath.cs` |
| Prediction as a `memcpy` ring over the declared blob | `src/sourcesharp/game/shared/prediction_ring.h` |
| The game DLL, game-ents, game-clients and game-tags interfaces shimmed | `shell_gamedll.cpp:128`, `shell_gameents.cpp:54`, `shell_gameclients.cpp`, `shell_gametags.cpp:48` |
| The per-classname diff between what stock declares and what managed answers | `SourceSharp.Tests/ContractBaseline.cs` |

**What is wrong with it, stated once.** The parallel entity exists next to the
stock one rather than instead of it, and every seam between the two is a place
for a silent wrong answer: two hierarchies (`EntityHierarchy` in
`ScriptedEntity.cs:67`), a three-armed resolver (`shell_resolve.h:66-91`, 119
call sites), a three-armed toucher (`toucher.h`), eleven relay functions
(`standalone_relays.h`), and a probe that exists only to prove every ABI slot
answers both arms (`AbiBothHierarchies.cs`). The shells themselves still hold
policy (`Remove`, `UpdateMovement`, `BlockerAt`, `UpdateFlight`,
`FireNamedOutput`, `KeyValue`, the usercmd loop) that decides what the game
does, and the client shell decides interpolation, shadows, render groups and
bone caching on its own. None of that is engine binding.

**Two things this paragraph used to say, corrected by measurement rather than
quietly dropped.** Both were written before steps 2a–2h, 3a–3g, 6a–6f and the
two entity-list lanes landed, and the tree has since moved out from under them.

*It used to read "about 7,600 lines of policy", and the queued STATE note said
"roughly 20,000 lines of shim C++" fail §6's test.* Measured instead with
`tools/shim-policy-audit`, which strips comments and string literals before it
counts anything — prose counted as code has bitten this port five times — the
shim is **313 files and 140,360 raw lines, of which 63,004 are code**, and the
policy left in it is **about 2,340 comment-stripped code lines in six clusters,
plus 78 native ConVar declarations**. The clusters are
`shell_impact_effects.cpp` (858), `shell_standalone_player.cpp`'s fall damage,
usercmd loop and VCollision state machine (380), `takedamageinfo_library.cpp`
(359), `shell_sprite_leaf.cpp` (303), `shell_gamerules.cpp`'s eight rule bodies
(267) and `thirdperson_camera.h` (62), with roughly 110 lines of scattered
rules. The file-by-file argument is `.claude/entity-port/status/shim-audit.md`.
The remaining distance is far shorter than 7,600 lines. It is not zero, and
none of it is in the files §6 keeps.

*It used to name "the whole of `shell_standalone_hierarchy.cpp`" in the list
above, and that example no longer supports the claim.* Step 2h moved the
parenting registry, the composition arithmetic, the cycle refusal, the deferred
`parentname` and the once-a-frame sweep to `SourceSharp.Abi/Hierarchy.cs`, where
`HierarchyTests.cs` states 52 facts about them with no game running. What is
left in the file is `EnsureBones`, attachment lookup, `TestHitboxes`, pose
parameters and transform propagation. §6's bone-leaf-library row and §3.7's
"a shim function the server uses for `TestHitboxes`, attachments and hit-group
lookup" sanction the first four by name; §6 now carries a row for the fifth.
On the audit's own instrument the file scores `Pk = 11.1` — three deciding
conditions in 990 stripped code lines — against a policy floor of 33.7, and the
audit judged it STAY. A file this section cites as policy while §6 keeps it
entire is a contradiction the next reader would otherwise have to rediscover,
so it is written down here instead.

---

## 2. What the engine demands of an entity

This is the contract the design is built against. Everything an entity is in
C++ beyond this list is SDK convention, and convention is what the plan
deletes. Counts are pure virtuals declared in `src/public`, measured with the
command in Appendix A.

**Per entity, server (39 slots):** `IHandleEntity` 2, `IServerUnknown` 3,
`IServerEntity` 3, `IServerNetworkable` 9, `ICollideable` 20. The engine reaches
them through `edict_t::m_pUnk` and `m_pNetworkable` (`public/edict.h:227-230`),
the spatial partition, and `trace_t`. Stock `baseentity.h` declares 189
virtuals; 150 of them are the SDK talking to itself.

**Per entity, client (77 slots):** `IHandleEntity` 2, `IClientUnknown` 6,
`IClientNetworkable` 14, `IClientRenderable` 45, `IClientThinkable` 5,
`IClientEntity` 5. The renderer calls `IClientRenderable` during every draw and
shadow pass; the network decoder calls `IClientNetworkable`. Stock
`c_baseentity.h` declares 218.

**Per side, once:** server `IServerGameDLL` 49, `IServerGameEnts` 6,
`IServerGameClients` 19, `IServerGameTags` 1, `IPlayerInfoManager` and
`IPlayerInfo`; client `IBaseClientDLL` 80, `IClientEntityList` 9,
`IPrediction` 13, `IGameMovement` (resolved by name, `plan.md` §9).

**Callbacks the engine or VPhysics makes into us:** `ITraceFilter` 2,
`IEntityEnumerator` 1, `IPartitionEnumerator` 1, `IPhysicsCollisionEvent` 8,
`IPhysicsObjectEvent` 2, `IPhysicsConstraintEvent` 1, `IMotionEvent` 1,
`IPhysicsCollisionSolver` 5, `IPhysicsTraceFilter` 2, `IPVSNotify` 1, and the
four `CBaseEdict::GetChangeAccessor` slots (`public/edict.h:165`).

**Structures the engine walks:** `ServerClass` and `ClientClass` lists linked at
static-init or DLL-init time (`server_class.h:24`, `client_class.h:60`),
`SendTable`/`RecvTable` with proxy function pointers (`dt_send.h:41`,
`dt_recv.h:47`), `edict_t` fields at fixed offsets, `CPlayerState` returned from
`IServerGameClients::GetPlayerState`, and `CUserCmd` bytes in
`ProcessUsercmds`.

**What the engine does not demand.** A base class. An entity list (it owns the
edicts; the client list interface is index-based and nothing in it iterates).
Datamaps (only save/restore and the SDK's own prediction copy use them). A
think list, a touch list, an I/O event queue, interpolation, a movement class,
game rules, a player class. Each of those is a decision about what the game
does, and each moves.

Two facts fix details below. `NUM_ENT_ENTRY_BITS` is `MAX_EDICT_BITS + 2`, so a
handle addresses 8,192 entries with a 16-bit serial (`public/const.h:77-81`):
indices 0–2047 are edicts, 2048–8191 are the engine-invisible range. And
`IPrediction`'s four `C_BasePlayer *` parameters (`public/iprediction.h`) are an
engine ABI: they stay in the signature and are never dereferenced.

---

## 3. The design

### 3.1 One shell per side, final, with no base

Each side has exactly one native entity class: `CEntityShell` on the server
(`IServerEntity`, `IServerNetworkable`, `ICollideable`) and `C_EntityShell` on
the client (`IClientEntity`, `ICollideable`). Both are `final`. Neither derives
from anything that is not an engine interface, and neither has subclasses: not
a player shell, not a scripted shell, not an animating shell. What an entity is
becomes its data, and the data is managed.

Why one and not a family: the SDK needs a class per capability only because
each capability is a C++ base (`CBaseAnimating`, `CBasePlayer`). Once the
capabilities are managed, the only reason to subclass a shell is to override a
virtual with a different native body, and the design forbids native bodies that
decide anything. The current `CStandalonePlayer` exists because the usercmd
loop and the movement state live in C++; §3.10 moves them. The current
`CStandaloneScriptedEntity` exists to route virtuals to managed; §3.4 makes
the base do that.

Why no `IGameEntity`: it was the interface stock C++ used to ask a parallel
entity questions. Under this plan no stock C++ asks an entity anything, so the
interface has no caller and is deleted with its last one. The shell answers the
engine, and the engine asks only through the interfaces in §2.

### 3.2 The engine block: shared memory at offset zero

Most of the 39 and 77 slots are getters the engine calls synchronously and
often: `GetCollisionOrigin`, `OBBMins`, `GetSolid`, `GetRenderOrigin`,
`GetModel`, `IsTransparent`, `ShadowCastType`, `GetSkin`. They cannot cross into
managed code: the partition asks per trace, the renderer per draw. They also
cannot be native policy.

So they are answered from memory that managed code writes. The blob every
entity already carries (`plan.md` §4) gets a fixed prefix at offset 0, the
**engine block**: a struct declared once in C# (`EngineBlock`, blittable,
`StructLayout.Sequential`) and once in C++ (`EngineBlock_t`), holding every
value a shell getter returns:

- identity: handle, classname index, model index, collision model index;
- placement: origin, angles, the cached world transform, mins, maxs, solid
  type, solid flags, collision group, parent handle, owner handle, move type,
  `FL_*` flags, effects, water level;
- server-only tail: the PVS info the engine writes, `CPlayerState` for player
  slots (the engine reads it through `GetPlayerState`);
- client-only tail: render origin and angles (the interpolated pose), render
  bounds, render colour and fx blend, skin, body, shadow choice, render group,
  and the animation inputs the bone code reads (§3.7).

The shell reads the block; managed code writes it and calls `StateChanged` with
the field's offset so delta compression is preserved (`plan.md` §4). On the
client the engine's decoder writes the networked part of the block directly,
because the recv props address it. The two declarations are held equal by
three checks: an xUnit fact per field against `Marshal.OffsetOf`, a gtest per
field against `offsetof`, and a fingerprint of the block's layout that both
sides publish at DLL init and the join compares alongside the existing layout
fingerprint. A field that exists in one declaration and not the other fails the
build of the side that has it, because the fingerprint constants are generated
from the C# declaration and compiled into both.

Why a struct and not a list of offsets passed at runtime: the shell's getters
must be a load at a constant offset. An offset table would put an indirection
in the partition's inner loop for no gain, and a struct can be asserted.

### 3.3 Identity is a handle; the slot table is the only native table

An entity's identity everywhere is Source's own `CBaseHandle`: index plus
serial in one `uint32`. Managed code holds handles and resolves on use
(`plan.md` §4). Native code holds handles too, and the only native pointer
that outlives a call is the shell pointer in the **slot table**.

The slot table is one array per side, owned by the side's game-system object
and sized `NUM_ENT_ENTRIES`, holding `{ shell pointer, serial }` per index. It
has four operations: bind, unbind, resolve-by-handle, resolve-by-index. It
cannot iterate, has no listeners, and knows no names. On the client it *is*
the `IClientEntityList` implementation the engine resolves by name: the nine
methods in `public/icliententitylist.h` are index and handle lookups plus
counts, and the counts are maintained by bind and unbind. On the server it
replaces `CGlobalEntityList` for the one thing the shim needs it for, turning an
`IHandleEntity *` from a trace or a partition walk into a shell without a cast:
`GetRefEHandle()` is a virtual on the engine interface, the handle indexes the
table, the serial is compared, and the concrete pointer stored at bind time is
returned.

Why this is not "an entity list": it stores no entity, only the binding between
an engine index and the object the engine already holds through that index. It
is the same information as `edict_t::m_pUnk` on the server, kept in a form that
gives the concrete type back without RTTI. Everything a list *does* — iterate,
find by name, find by class, notify on add and remove — lives in the managed
`EntityDirectory` (§3.5), one per side module.

Entities the engine cannot see have no shell at all. A `logic_relay`, a filter,
a `math_counter`, a `point_template`, a `phys_constraint` are managed objects
that exist only in the directory. They take handles from the 2048–8191 range,
with serials the directory owns, so an `!activator` or a targetname lookup
works uniformly. The stock design spent a `CServerOnlyEntity` (a full
`CBaseEntity`, 3,233 header lines of it) on each; here they cost nothing native.
A shell is created only for an entity that is networked, in the partition, or
attached to a physics object.

Physics game data is the handle too. `IPhysicsObject::SetGameData` takes a
`void *`; it is given the packed handle plus one (so a null still means "no
entity"), never a pointer. Every VPhysics callback then resolves through the
slot table. That single decision deletes the pointer-compare-then-registry
fallback in `physicsowner.h:498-526`.

### 3.4 Routing: an op table with override bits, and per-frame batching

The virtuals the engine calls that need a decision (`Spawn`, `Touch`,
`StartTouch`, `EndTouch`, `TakeDamage`, `PostDataUpdate`, `NotifyShouldTransmit`,
`DrawModel` for custom draws, `DoAnimationEvents`, `ShouldCollide`) reach
managed code through one native function pointer, `Dispatch(handle, op, args)`,
in the non-collectible bootstrap (`plan.md` §3). `op` is a pinned enum shared
by both sides and asserted numerically.

Every managed entity class registers, once at side load, a bitmask of the ops
it overrides, computed by reflection (`DeclaringType != typeof(Entity)`) and
stored in the class descriptor the shell holds. The shell tests the bit before
crossing. This is the "does C# override this?" bit `plan.md` §4 describes and
the tree does not have: today every `OnManaged*` crosses unconditionally
(`shell_standalone_scripted.cpp:31-260`). A test mutates a class to drop an
override and asserts the crossing count falls to zero.

Per-frame work is never one crossing per entity. Think, movement simulation,
interpolation, client think and animation-event delivery are each one crossing
per frame into managed code, which walks its own scheduler or directory. The
server frame is `GameFrame → managed RunFrame(curtime)`; the client frame is
`FrameStageNotify(stage) → managed OnStage(stage)`. The tree's own measurement
of what per-entity crossings cost, 38,578 `EntityIsValid` calls in one server
frame before `LivenessMirror` (`SourceSharp.Abi/LivenessMirror.cs:10`), is the
reason this is a rule and not a preference. The live gate counts crossings per
frame and fails over a budget (§5).

Resolution happens once per crossing. The bootstrap thunk resolves the handle
against the directory into a frame-scoped `ref struct EntityRef` that exposes
the entity object and its blob `Span<byte>`; the compiler forbids it escaping
the frame. The per-callback dictionary lookup the registries do today becomes
an array index, because the directory is an array indexed by handle entry.

### 3.5 The managed entity model

One base class, `Entity`, in the shared assembly, with the engine block as its
first fields and the networked and predicted fields declared by attribute. The
generator that exists (`NetStateGenerator.cs`) already emits blob-backed
properties with compile-time offsets; it is extended to emit the engine block
first and to lay a class's own fields after its base's, so a class hierarchy
in C# produces one blob layout that the class's tier table addresses (§3.6).
`[Networked]` marks a
field for the send table; `[Predicted]` marks it for the client's ring;
`[Interpolated]` marks it for the client's history. A field with none of these
is plain managed state and is lost on hot reload, which is the existing rule
(`plan.md` §7).

Layering is managed inheritance: `Entity → AnimatedEntity → Player`,
`Entity → BrushEntity → Trigger`, `Entity → PhysicsProp`. Each layer adds
fields and overrides ops; the shell beneath is the same object. This replaces
`ScriptedEntityKind` (`ScriptedEntity.cs:280`), which encoded "what kind of
shell" into an enum because the shell varied; nothing varies now.

The `EntityDirectory`, one per side module, owns:

- the array of live entities by handle entry, and serial allocation for the
  engine-invisible range;
- iteration, find-by-name, find-by-classname, and the classname index;
- the think scheduler: a min-heap of `(nextThink, handle, context)`, run once
  per frame, supporting named contexts as stock does;
- the I/O queue: outputs are fields holding connection lists parsed from the
  map (the `CEventAction` list that today still lives in the shell's `KeyValue`,
  per the managed survey), delays are queued here, and `!activator`,
  `!caller`, `!self`, `!player` resolve here;
- the touch link table: which pairs are currently touching, so `StartTouch`
  and `EndTouch` edges are computed in managed code from the per-frame overlap
  report;
- removal: `Remove(handle)` marks; the directory frees at end of frame, as
  `UTIL_Remove` does, so a handle stays valid for the rest of the frame.

Map spawning is managed end to end: `LevelInit` hands the entity lump to
managed, which parses it (`Shared/MapEntityBlock.cs` exists), creates entities,
applies keyvalues, resolves parents and connections, and calls `Spawn` and
`Activate` in stock order. `MapEntity_ParseAllEntities`, `mapentities.cpp`,
`TemplateEntities.cpp` and the factory dictionary are not needed natively; the
one factory the engine calls is the player's, and that is a slot-table bind.

Native-side state that is neither in the engine block nor networked does not
exist. If the shell needs a value, it is in the block; if managed needs a
value, it is a managed field or a blob field.

### 3.6 Networking

The send and recv tables stay the **tier tables** that exist today
(`generic_nettable.h`: seven blob widths, four names each, one shared field
list, two offset tables). A managed class declares its blob layout with the
generator; the class descriptor records the tier whose width holds it, and the
shell is allocated and networked at that tier. The send path stays native and
offset-based with no managed call during encoding (`plan.md` §4). Send and
recv proxies are the standard ones plus the few the engine block needs
(handles, angles), all in the shim.

Why tiers are kept: they already work, the engine pairs them by name on both
sides today, and nothing in this plan needs a table per class. The engine
block (§3.2) is the same prefix in every tier, so a shell getter reads the same
offset whatever tier it was allocated at, and the block's fingerprint covers it
once. The tier is looked up per class exactly as now (`shell_standalone.h:97-118`
explains why it is the single source for both the allocation and the table).

A table per map classname remains an option for later: it would give the
class-list diff, `dumpentityfactories` and `ContractBaseline` the map's own
names and drop the tier indirection, at the cost of an instance baseline per
class (`MAX_SERVER_CLASSES` is 512, `public/const.h:72`, against 284
classnames). Nothing here forecloses it, because the shell never asks which
kind of table it has; the change would be confined to the table builders and
the class descriptor.

The `ClientClass` create function is one per tier, as now; it allocates the
shell at the tier's width, binds the slot, and creates the managed entity
immediately rather than on first data update, because the class descriptor
now travels with the create call (the classname is published with the
descriptor at level start, not learned from the first update). The player is
not special: the client decides "is this the local player" from
`engine->GetLocalPlayer()` as it does now (`shell_standalone_player.cpp:158`),
and the managed `Player` class is just the class registered for the player
classname.

Layout change still needs a map restart and is refused, never applied
(`plan.md` §7). The fingerprint exchanged at join now covers the engine block.

### 3.7 Animation: policy managed, bone math a leaf library

Animation state — sequence, cycle, playback rate, pose parameters, bone
controllers, overlay layers, body, skin, the "sequence changed" and "cycle
wrapped" facts — is managed state in the blob, networked as stock networks
it. Advancing the cycle, choosing sequences by activity, blending layers,
computing aim and movement pose parameters, and deciding which events fired
are managed, once per frame, for every animating entity.

Turning that state into bone matrices is not a decision. `public/bone_setup.cpp`
and `studio.cpp` take a `CStudioHdr`, a sequence, a cycle, pose parameters and
bone controllers and produce matrices; IK takes a trace callback. They are kept
as a leaf library with no entity type in their interface, called from the
shell's `SetupBones` (client, for the renderer) and from a shim function the
server uses for `TestHitboxes`, attachments and hit-group lookup. The bone cache
key already exists as an engine-free header (`bone_cache_key.h`); it becomes
the cache rule of the leaf library. Bone merge (a viewmodel following a weapon)
takes the source by handle through the slot table.

Animation events are delivered as a batch: the shell's `DoAnimationEvents` is
a no-op, and managed code asks the leaf library for the events between the
previous and current cycle when it advances the cycle, so it already knows
which fired. Flex and morph (`CBaseFlex`, `SetupWeights`) are a recorded
absence: this mod has no facial animation, `SetupWeights` does nothing, and
`ContractBaseline` carries the entry.

Ragdolls follow the same split. `ragdoll_shared.cpp` is already entity-free
(`ragdollparams_t::pGameData` is a `void *`). Creating a ragdoll from a bone
snapshot is a flat shim function returning physics object ids (§3.8); which
entity becomes a ragdoll, when, and for how long is managed. The client ragdoll
is a managed entity whose render pose comes from the physics objects' positions
written into the block by the physics step.

### 3.8 Physics: the environment and its callbacks are mechanism

The server creates and steps one `IPhysicsEnvironment` per level and
implements every callback interface VPhysics calls (§2). That is a shim object,
`PhysicsShell`, owned by the game-system instance, with no policy:

- `IPhysicsCollisionSolver::ShouldCollide` is called per candidate pair per
  step and must answer synchronously. It reads both entities' engine blocks
  (collision group, solid flags, owner, `FL_*`) and applies the rule table
  managed code registered at level start (`RegisterCollisionRules` exists).
  No crossing.
- `IPhysicsCollisionEvent` (pre/post collision, friction, start/end touch,
  fluid), `IPhysicsObjectEvent` (wake/sleep), `IPhysicsConstraintEvent`
  (broken) are queued into a native ring during the step and drained by one
  crossing after `PostSimulationFrame`. This is stock's own pattern:
  `physics.cpp` defers damage and impact sounds to after the simulation because
  the callbacks fire mid-solve.
- `IMotionEvent::Simulate` is the exception: it must return a linear and
  angular force during the step. Motion controllers therefore run natively
  from parameters managed code sets (constant force, motor, keep-upright,
  medium), as `shell_motioncontrollers.cpp` does today; a managed-defined
  controller is not supported and `ContractBaseline` says so.
- `VPhysicsUpdate` per object per step writes origin and angles into the
  engine block and marks the edict changed. No crossing.

Managed code refers to physics objects, constraints, controllers, springs and
shadow controllers through ids with serials, allocated by `PhysicsShell`, so a
stale id fails the way a stale handle does. The flat functions managed calls
(`ApplyForce`, `SetVelocity`, `EnableMotion`, `CreateConstraint`, …) already
exist as ABI slots and are retargeted from handles-plus-pointer-lookups to ids.

Entity movement that is not VPhysics — `MOVETYPE_PUSH`, `FLY`, `TOSS`, `STEP`,
`NOCLIP`, `FOLLOW`, and pushing the things a mover blocks — is `physics_main.cpp`
and `physics_main_shared.cpp`, 4,654 lines of decisions over traces. It is
managed, run from the per-frame crossing, using the trace ABI that exists. The
shell's `UpdateMovement`, `BlockerAt`, `UpdateFlight`, `UpdateTouchLinks` and
`TouchTriggers` (about 720 lines, `shell_standalone_base.cpp:1075-2639`) are
what it replaces. The trigger overlap report is one crossing per frame: native
walks the partition for every solid-trigger shell and hands managed the list
of overlapping pairs; managed diffs it against the touch link table.

### 3.9 Client: interpolation, prediction, think, render state

**Interpolation** is a history per `[Interpolated]` field, kept by the managed
client entity in preallocated rings, sampled at `PreRender` for every entity in
one crossing, with the result written into the block's render pose. The shell's
`GetRenderOrigin` is a load. `CInterpolatedVar` and `interpolatedvar.cpp` are
not compiled. The cost is arithmetic over a few vectors per visible entity per
frame, which is nothing next to the draw it precedes.

**Prediction** keeps the ring (`prediction_ring.h`, a `memcpy` per slot over
the blob) but the ring is owned by the managed client player and driven by the
managed movement code (§3.10), so the same C# runs on both sides for the same
command. `IPrediction` remains a shim vtable of thirteen methods whose bodies
call managed code; the four `C_BasePlayer *` parameters are ignored. The
compare-and-report exists (`PredictionErrors.cs`).

**Client think** is the managed scheduler. `IClientThinkable::GetClientThinkable`
returns `NULL` as it does today; `client_thinklist.cpp` is not compiled.

**Render policy** — should draw, render group, translucency, fade, shadow type,
render-to-texture shadow, bounds — is written into the block by managed code
when its inputs change (data update, dormancy, effects flags), not per frame.
The leaf system and the shadow manager are client-DLL mechanism the engine's
BSP callbacks feed (`engine/IClientLeafSystem.h`); they stay, de-entitied: they
address renderables through `IClientRenderable` and handles resolved by the
slot table, never through `C_BaseEntity` or `ClientEntityList()`.

**Drawing** a model is the shell filling `ModelRenderInfo_t` from the block and
calling `IVModelRender::DrawModelEx`, or `DrawBrushModelEx` for a brush
(which is the one reason the shell must be an `IClientEntity`,
`shell_standalone_client.h:20-23`). Sprites, beams, ropes, particle systems
and glow effects are drawn by leaf renderers (`beamdraw.cpp`, `c_sprite.cpp`'s
draw path, the particle manager) fed parameter structs by managed code; the
`C_*` entity classes that own them today are the part that goes. The shim's
`shell_*_sim.cpp` and `shell_*_render.cpp` files are already entity-free
(client survey, §1); their simulation policy moves to managed where it is
policy and stays where it is a mesh builder.

### 3.10 The player, movement, input, game rules

**The player** is a managed class on the same shell. The engine's player
touch points are all edict- or index-based and map onto it directly:
`ClientPutInServer(edict)` binds a shell to the reserved edict and creates the
managed player; `ProcessUsercmds` decodes commands natively (`usercmd.cpp` is
entity-free) into a blittable array and crosses once per call; `GetPlayerState`
returns the block's `CPlayerState`; `ClientSetupVisibility` asks managed for the
view origin once and adds it to the PVS natively; `ClientEarPosition` likewise;
`IPlayerInfo` is a shim reading the block. The usercmd queue and the
run-one-command loop in `shell_standalone_player.cpp` move with the player.

**Movement** is managed on both sides: `CGameMovement` (5,398 lines, compiled
twice today, the source of the jump-misprediction class the tree already
records at `MoveMath.cs:22-23`) becomes one managed `Movement` class calling
the trace ABI, with `MoveMath.cs` as its arithmetic. `IGameMovement` remains a
shim vtable the engine resolves by name; `ProcessMovement` copies `CMoveData`
in and out once per command, which is the `CreateMove` rule from `plan.md` §3
applied. The client's prediction replays through the same class.

**Input** is unchanged: `CSourceSharpInput : CInput` already touches no entity
(`shell_input.cpp`). `CInput`'s remaining entity references (`in_main.cpp`,
`in_camera.cpp`, `cam_thirdperson.cpp`) are the local player's eye and the
third-person camera, answered from the block and from `thirdperson_camera.h`'s
policy moved to managed.

**Game rules** have no native object. The gamerules proxy entity is already
deleted (`shell_gamerules.h:30-60`); `CMultiplayRules` and `gamerules.cpp` exist
only because native callers (damage, collision, spawn selection, player
relationships) asked them, and every such caller moves. The decision callbacks
in `plan.md` §3 (`CanTakeDamage`, `GetPlayerRelationship`, admission) become
ordinary managed calls with no native side.

**Ammo, weapons, items** are already managed descriptions over generic
entities (`plan.md` §5); `CAmmoDef` and `weapon_parse.cpp` are entity-free and
stay.

### 3.11 The world

Edict 0 is an entity with model index 1, and the engine names it by index
(`plan.md` §9 lists nothing for it because the SDK did it for free). Here it
is bound to index 0 first at `LevelInit`,
with a managed `World` class that owns what the SDK's `CWorld` and
`C_World` decided: world bounds, the detail-sprite material, the skybox
settings, `startdark`, the timelimit-free defaults. The server adapter
(`world_adapter.cpp`, `CWorld : IServerEntity` in `game/server/world.h:339`)
and the client's `C_World : C_BaseEntity` (`c_world.h:45`, carrying the
SourceSharp world blob through `c_world.cpp:85-247`) both come off the SDK
hierarchy -- see the paragraph below for what they land on, which is NOT
`CStandaloneEntity`. `detailobjectsystem.cpp` reads its material from the
block.

**THE WORLD IS NOT PUT ON `CStandaloneEntity`, and this paragraph used to say
it was.** An earlier draft read "the same shell class as everything else",
which contradicted a standing OWNER INSTRUCTION already enforced by a green
gtest (`shim_underive_verdict_test.cpp` verdict 2, quoting
`plan_cworld_trace.md`): *"CWorld should not inherit CStandaloneEntity either —
it should be its own standalone class. A trace should be able to return a
pointer to CWorld, or CStandaloneEntity, or CBaseEntity."* Lane 4g found the
contradiction, declined to break the tie, and was right to raise it — but it is
not a design fork, because the two sides are not equal: **an owner instruction
outranks this document's prose, and this document is what was wrong.** Amended
rather than left for the next lane to re-litigate.

The sequencing argument is 4g's and it is independently sound: `CStandaloneEntity`
as it stands is a PRE-4f class that 4f itself reshapes, so converting the world
onto today's base would be doing the work against a base that is about to move.
The wire is neutral to the question — the world's table already comes from the
managed declaration — so if the two ever converge it can happen without the
client noticing. **Nothing in Phase 4 is obliged to converge them, and nothing
should try.**

This does NOT block step 5e. The server already publishes entity 0's state as a
managed-declared `ServerClass` over a generic payload, which is what a client
shell at index 0 consumes; everything 5e changes is on the client side of that
wire.

### 3.12 Save and restore, and the last datamap

This mod is multiplayer and cannot save. The engine's save entry points on both
DLL interfaces are refused as they are today (`shell_gamedll.cpp:947`), the
block handlers stay as vtable-only stubs, and no entity has a datamap. The
managed save record (`SavedEntity.cs`, `SavedEntityCodec.cs`) exists for hot
reload and stays for that. With prediction copying the blob (§3.9) and inputs
dispatched by reflection-built tables (§3.5), `datamap.h`'s `typedescription_t`
has no user; `CPlayerState` and `CPlayerLocalData` lose their prediction
datamaps when the movement code that compound-assigns them is managed
(`shell_player_orphans.cpp:43-53` explains why they survived).

### 3.13 Build composition: the linker is the instrument

The two flat vpc lists are kept, and the work is subtractive. The instrument
for each removal is the undefined-symbol list (`plan.md` §1): delete the
translation unit, link, and every undefined symbol is a question with exactly
three answers — port the caller, answer it from the shim as engine binding, or
show the caller is itself leaving. An absence stub is not an answer; where one
is written it is written into one file per side (`shell_absent_server.cpp`,
`shell_absent_client.cpp`) with the symbol of the caller it waits for, and that
file must be empty at the end.

Order comes from the dependency graph, measured rather than guessed. For each
shim object, `nm -u` gives its undefined symbols; for each stock object,
`nm --defined-only` with symbol types gives what it defines; the intersection
is the set of **cross-edges** from the shim into stock entity code. That count
goes to zero first (the shim stops depending on anything it is about to
delete), then the stock TUs are removed in reverse topological order, which by
then is a link exercise. The same script counts edges between stock TUs so a
batch can be chosen that closes cleanly.

Why in place and not a parallel target: every commit must leave the game
playable, the vpc gives per-TU control, and the linker tells the truth about
each step. A second output directory would double the build, and its
undefined-symbol list would be the same list.

The 47 gtests that read the vpc text (build survey, §6) are load-bearing and
move with each batch: a test that pins "file X is compiled" is rewritten to pin
"file X is not", and its reason.

### 3.14 Hot reload

Nothing here changes the rule that anything surviving reload lives in the
blob (`plan.md` §7); the engine block is in the blob. What the design adds to
the reload path is rebuilt, not restored: the think heap, the touch link table,
the interpolation histories and the I/O queue are re-derived from blob state
or lost, and the tests say which. The delayed-output queue is the one that
matters to a player and is written into the reload record. The prediction ring
is invalidated by generation as it is today (`prediction_ring.h:32-38`).

### 3.15 Diagnostics

`devapi ents`, `classes`, `instances`, the class-list diff and
`dumpentityfactories` keep working because they read the managed directory and
the runtime class lists. Two counters are added because the design depends on
them: crossings per frame per op, and physics ring depth per step. Both are
read by the live gate.

### 3.16 Every cast to a base entity, and where each one goes

`dynamic_cast` is the visible cast; it is not the common one. The census below
was taken over every compiled TU (Appendix A, "cast census"), counting the
seven ways C++ in this tree turns something into an entity pointer. The
definition of done at the top of this document is widened to all of them: the gate counts every
pattern, and the only permitted downcast on an entity path is the one line
inside the slot-table resolver.

| pattern | server stock | server shim | client stock | client shim |
|---|---|---|---|---|
| `dynamic_cast<…>` | 50 in 14 files | 13 in 7 | 65 in 26 | 16 in 7 |
| C-style `(CBaseEntity *)`, `(CBasePlayer *)`, … | 28 in 12 | 12 in 6 | 21 in 11 | 0 |
| `static_cast<CBase…*>` / `<CStandalone…*>` | 11 in 7 | 18 in 10 | 19 in 9 | 0 |
| `reinterpret_cast`, `assert_cast` | 5 in 3 | 2 (headers) | 23 in 9 | 0 |
| `ToBasePlayer`, `ToBaseCombatCharacter`, `ToBaseAnimating` | 41 in 10 | 6 in 3 | 10 in 7 | 3 in 1 |
| `CBaseEntity::Instance(edict or index)` | 15 in 7 | 4 in 3 | 3 in 1 | 0 |
| `EntityFromEntityHandle`, `GameEntityFromEntityHandle`, `TraceEntity`, `TraceGameEntity` | 72 in 14 | 18 in 7 | 55 in 14 | 8 in 1 |
| `->GetBaseEntity()` | 2 in 2 | 3 in 2 | 3 in 3 | 2 in 2 |
| `GetContainingEntity(edict)` | 23 in 6 | 9 in 3 | 0 | 0 |
| `(CBaseEntity *) …->GetGameData()` | 1 in 1 | 3 in 1 | 12 in 2 | 0 |
| `EHANDLE` / `CHandle<T>` (a C-cast in `Get()`) | 44 in 15 | 34 in 17 | 45 in 18 | 16 in 9 |

The stock columns go with their files (Phases 4 and 7). The shim columns are
the sites this plan must rewrite, and they fall into six families, each with
one replacement:

1. **Typed handles.** `CHandle<T>::Get()` is `(T*)CBaseHandle::Get()`
   (`game/shared/ehandle.h:186`): a C cast from `IHandleEntity *` to whatever
   `T` is, correct only when `T`'s `IHandleEntity` is at offset zero. The shim
   holds `EHANDLE`s in 26 compiled TUs. Replacement: the shim holds
   `CBaseHandle` only and resolves through the slot table; `CHandle<T>` is
   never instantiated for an entity type in a shim TU, and the template leaves
   with `baseentity.h`. `CBaseHandle` itself stays: it is the engine's handle.
2. **Edict to entity.** `GetContainingEntity(edict)` (`shell_gameclients.cpp:394,490,548`,
   `shell_gameents.cpp:102-103,178,210`, `shell_transmitsteps.cpp:92`),
   `CBaseEntity::Instance(edict)` (`shell_gameclients.cpp:411,930`,
   `shell_entity_orphans.cpp:735`, `shell_standalone_player.cpp:267`),
   `edict->GetUnknown()` through `handle_cross.h`'s
   `SourceSharp_SDKEntityFromUnknown`, and `->GetBaseEntity()`
   (`shell_gameents.cpp:503`, `shell_standalone_base.cpp:234`). Replacement:
   `ShellFromEdict(edict)` is a slot-table lookup by edict index; the two
   `IServerGameEnts` methods that return or take a `CBaseEntity *` answer
   `NULL` and `NULL`, and Phase 0 instruments them to learn whether the engine
   ever calls them with a real expectation.
3. **`IHandleEntity` to entity.** The four `util_shared.h:115-211` helpers and
   the toucher's three arms (`toucher.h:135-138`): 18 shim sites, the largest
   group being `shell_gamerules.cpp:101-123` reading a trace result.
   Replacement: `ShellFromHandleEntity(IHandleEntity *)` calls the virtual
   `GetRefEHandle()` and indexes the slot table; a trace result crosses to
   managed as a handle. The pair of functions per question, one per
   hierarchy, collapses to one.
4. **Physics game data.** `shell_constraints.cpp:872,920-921` and
   `physicsowner.h:523` cast a `void *` to `CBaseEntity *`. Replacement: game
   data is the handle (§3.3); the cast becomes a slot-table lookup.
5. **Downcasts inside the hierarchy.** `ToBasePlayer` and `(CBasePlayer *)`
   (`shell_gameclients.cpp:394,411,548,930`, `shell_gameents.cpp:298`,
   `shell_transmitsteps.cpp:97`, `shell_tempents.cpp:623`,
   `shell_gamerules.cpp:1058-1067`) ask "is this a player"; the
   `static_cast<CStandaloneEntity *>` and `<CStandalonePlayer *>` sites
   (`standalone_relays.cpp:34`, `shell_entityapi.cpp:381,1933`,
   `shell_standalone_base.cpp:209`, `shell_standalone_scripted.cpp:292,328`,
   `shell_standalone_player.cpp:1301,1335-1336`, `shell_worldquery.cpp:235,284,574`,
   `shell_standalone.cpp:1139`) cross from `IGameEntity *`, `IServerEntity *`
   or `IClientNetworkable *` to the concrete shell; the client's three
   `dynamic_cast`s do the same from the entity list. Replacement: "is a
   player" is a flag in the engine block; the shell is `final`, so there is no
   subtype to cast to; and the concrete pointer comes from the slot table,
   which stored it at bind time. The Phase 13 canaries that
   `reinterpret_cast` the `IGameEntity` subobject to `CBaseEntity *`
   (`shell_standalone.h:249`, `shell_standalone_client.h:210`, read at
   `shell_standalone_base.cpp:2813` and `shell_standalone_client.cpp:5260`)
   are deleted with `IGameEntity`.
6. **Casts through `void *` for datamaps and AI nodes.**
   `baseentity_statics.cpp:126` (`(CBaseEntity *)pStruct` in a datamap
   accessor) and `shell_debugoverlays.cpp:139` (an AI node hint). Both leave
   with their subjects in Phase 4.

The gate for all six is one script (Appendix A) whose pattern list is checked
in beside the census baseline, so a new spelling of the same cast is a
one-line addition to the list, not a hole.

---

## 4. Phases and steps

A phase is an integration milestone with a live-gate exit. A **step** is the
unit a lane (one subagent, one worktree reset to the branch tip) implements
and verifies alone. Every step lists what it **owns** (files no concurrent
step touches), what it **needs** (steps that must have merged), the **ABI**
slots it requests, and its **gate** (something the lane runs itself: xUnit
facts, gtests, a link, the census). The live gate runs once per phase, by the
integrator.

Rules that every step follows:

- Shared files are never owned by a lane: `Abi.cs`, `host/sourcesharp.h`,
  `Bootstrap/Entry.cs`, the `Entity` base and the generator once they exist,
  the two `*_csharp.vpc` files, and the op enum. A step writes the ABI slots it
  needs into its brief; the integrator appends them and bumps the version
  once per merge (`abi-version-bumps-collide`).
- A step touching a shared `game/shared` file owns it whole on both sides
  (`shared-file-virtual-differs-per-side`).
- Every step lands with tests that were red first, the full managed suite
  green with zero warnings, the native suite green, and the census diff
  showing only the movement it claims.
- A step that needs a stub writes it into the side's absence file with the
  caller it waits for; the file must be empty at the phase exit.
- Lanes prefix scratch files with their step id
  (`agents-collide-in-the-shared-scratchpad`).

### Phase 0 — Instruments

**0a — Census tool.** Owns `tools/entity-census`, `src/sourcesharp/test/entity_census_test.cpp`.
Needs nothing. Produces the §1 table from the ninja object tables and `nm`
with symbol types, the shim→stock cross-edge count with the edge list per
stock TU, and the cast census over the compiled TU list with the §3.16 pattern
file. Output is committed as `docs/entity-census.txt`. Gate: the gtest fails
when a number moves without the baseline moving; the pattern file is read with
`read -r` and a test proves a backslash survives.

**0b — Gates tool.** Owns `tools/entity-gates`. Needs 0a. Runs the three
definition-of-done checks against two DLL paths; exit non-zero on the first
failure; prints counts. Gate: run on today's binaries reports 1,207/602,
33/91, and the §3.16 totals.

**0c — Crossing counters.** Owns `SourceSharp.Abi/Crossings.cs`,
`Bootstrap/Entry.cs` counter hooks (integrator-applied), `DevApi` status
fields, `shared/shell_diagnostics.cpp` for the native side. ABI: one slot,
`CrossingsReport`. Gate: facts that the counter increments once per thunk
entry and reads zero after `Reset`; `devapi status` shows per-op counts on a
live server.

**0d — Live-gate checklist.** Owns `tools/live-gate`, its scenario files, and
the `DevApi` endpoints each step asserts through. Needs 0c. Adds the sixteen
steps in §5 with a second client; records the crossing budget per stage from
today's build. Gate: the full checklist passes on today's build twice in one
process (`one-map-load-hides-a-whole-bug-class`).

**0e — Engine-boundary probes.** Owns `shell_gameents.cpp` logging arms,
`shell_gameclients.cpp` logging arms, a `docs/engine-boundary.md`. Instruments
`EdictToBaseEntity`, `BaseEntityToEdict`, `GetPlayerState`, `IPlayerInfo`,
and every `IServerGameEnts`/`IClientEntityList` method with a call counter, so
Phase 1 and Phase 5 know what the engine actually calls and with what
expectation. Gate: the doc names each method with its call count over one
live-gate run and a verdict: called, never called, called and result used.

**0f — Fingerprint slot.** Owns the join handshake's user message and its
reader/writer facts. Adds the second hash carrying an empty block. Gate: the
existing fingerprint tests plus one fact that the second hash is sent and
compared.

Phase exit: 0a–0f merged; `docs/entity-census.txt` committed; the live gate
passes on today's build with a recorded budget.

### Phase 1 — Engine block, slot table, dispatch (server)

**1a — Engine block declaration.** Owns `SourceSharp.Abi/EngineBlock.cs`,
`sourcesharp/game/shared/engine_block.h`, `test/engine_block_layout_test.cpp`,
`Tests/EngineBlockTests.cs`, the generator's block emission
(`NetStateGenerator.cs`, owned for this step only). Needs 0f. Declares the
struct (§3.2) with the server tail and the client tail as separate structs
after a shared head; generates the fingerprint constant into both sides.
Gate: one fact per field against `Marshal.OffsetOf`, one gtest per field
against `offsetof`, the reorder/retype/add mutations each change the hash, and
the block is the first thing in every generated layout.

**1b — Shell getters read the block.** Owns `shell_standalone.h`,
`shell_standalone_base.cpp` lines 842–1075 (`SetVisible`, `SetSolidity`,
`UpdatePartition`, `SetAbsTransform`, `SetModelName`), `standalone_layout_test.cpp`.
Needs 1a. Every `ICollideable`, `IServerEntity` and `IServerNetworkable`
getter returns a block field; every setter is deleted in favour of a managed
write plus `StateChanged(offset)` through the existing `EntityMarkStateChanged`
slot, with the partition update triggered by a `PositionChanged` op. Gate: the
layout test pins that the shell has no field the block also has; a fact that
writing origin marks exactly the origin offset and never
`FL_FULL_EDICT_CHANGED`.

**1c — Slot table.** Owns `sourcesharp/game/shared/slot_table.h/.cpp`,
`shell_entityslots.cpp`, `entity_slots.h`, `entity_slots_test.cpp`,
`entitylist_slot0_test.cpp`. Needs nothing. Bind, unbind, resolve-by-handle
with serial check, resolve-by-index; owned by the game-system object; serial
mirrored into the edict as `standalone_netserial.h` does. Gate: the
serial-reuse test (bind, unbind, rebind, old handle resolves to null) and a
gtest that the table has no iteration API (a compile-must-fail probe).

**1d — Shim entity-list sweep.** Owns `shell_entityapi.cpp`, `toucher.h`,
`shell_worldquery.cpp`, `shell_mdlcache.cpp`, `shell_levelsteps.cpp`,
`shell_restoreentities.cpp`, `baseentity_statics.cpp`, `shell_standalone.cpp`,
`shell_soundenvelope.cpp`, `world_adapter.cpp`, `gameentity_handle.*`,
`shell_resolve.h`, `handle_cross.h`. Needs 1c. Every `gEntList.` and
`g_pEntityList->` site (server survey, §3) becomes a slot-table resolve or a
managed directory call; `SourceSharpEntity_t` becomes the single shell pointer;
`gameentity_handle.cpp`'s reverse scan is deleted; the cast families 2 and 3 of
§3.16 in these files are rewritten to `ShellFromEdict` and
`ShellFromHandleEntity`. ABI: `EntityFindByName` and `EntityFindByClassname`
reversed so native asks managed, not the other way. Gate: the census shows
zero `gEntList`/`g_pEntityList` in these files and the cast census falls by the
sites named; `abi_both_hierarchies` still passes for the standalone arm.

**1e — Typed handles out of the shim.** Owns the 17 server shim TUs that use
`EHANDLE`/`CHandle<T>` (`shell_standalone_base.cpp`,
`shell_standalone_hierarchy.cpp`, `shell_restoreentities.cpp`,
`shell_constraints.cpp`, `shell_soundenvelope.cpp`, `shell_entityapi.cpp`,
`shell_ai_absent.cpp`, `sourcesharp_gamesystem.cpp`, `shell_savegame.cpp`,
`shell_gamerules.cpp` and the rest listed by the census). Needs 1c, 1d.
Every typed handle becomes `CBaseHandle` plus a slot-table resolve at the use
site. Gate: the census reads zero `CHandle<`/`EHANDLE` in compiled server shim
TUs; a compile-must-fail probe that instantiating `CHandle<CStandaloneEntity>`
in a shim TU is refused.

**1f — Op enum, dispatch, override bits.** Owns
`SourceSharp.Abi/EntityOp.cs`, `sourcesharp/game/shared/entity_op.h`,
`shell_standalone_scripted.*` (deleted at the end), the `ScriptedEntity`
dispatch entry points in `ScriptedEntity.cs:1827-2494`,
`Tests/EntityOpTests.cs`, `test/entity_op_test.cpp`. Needs 1b. Pinned numeric
values asserted on both sides; the thirteen `OnScripted*` slots become ops
through one `Dispatch` slot; the class descriptor gains the override bitmask,
computed by reflection at registration; the shell tests the bit; the
frame-scoped `EntityRef` replaces the per-callback dictionary lookup. ABI:
`Dispatch`, `RegisterClassOps`. Gate: the override-bit mutation test (drop an
override, crossings for that op read zero; remove the bit check, the test is
red); numeric pins; `CStandaloneScriptedEntity` no longer exists.

**1g — `RunFrame` and the think heap.** Owns `SourceSharp.Abi/EntityDirectory.cs`
(new), `ThinkScheduler.cs`, the server `GameFrame` route in
`shell_gamedll.cpp`, `ServerGameDll.cs`, `Tests/ThinkSchedulerTests.cs`.
Needs 1f. One crossing per server frame; the heap with named contexts; the
existing `SetScriptedNextThink` slot is deleted. Gate: N thinking entities,
one crossing per frame; equal-time ordering; a named context cancelled
independently; the crossing counter from 0c shows think at one per frame on
the live server.

**1h — Physics game data is the handle.** Owns `shell_physicsobjects.cpp`,
`shell_constraints.cpp` (cast family 4), `shell_motioncontrollers.cpp`,
`game/shared/physicsowner.h`, the `InitPhysics` arm of `shell_standalone_base.cpp`.
Needs 1c. `SetGameData(handle + 1)`; `PhysicsOwnerOf` becomes a slot-table
resolve; the file-scope `g_PhysicsOwners` registry is deleted. Gate: a gtest
that a stale handle in game data resolves to null; the census shows zero
`GetGameData` casts in the shim.

Phase exit: `shell_scripted.h`, `shell_standalone_scripted.*` and
`gameentity_handle.*` are gone; cast families 1 and 4 read zero in the server
shim; the block fingerprint is exchanged and a reordered block is refused; the
live gate passes within budget.

**Three clauses were removed from this paragraph, because they asked Phase 1
for work this plan's own step list gives to later phases.** They were not
relaxed to make a gate pass — each was checked, found to be owned elsewhere by
name, and moved to its owner:

- *`shell_resolve.h`'s three-armed struct is gone.* It cannot go here. `pWorld`
  cannot be dropped: `CWorld` is `: public IServerEntity`, not a shell, so
  binding it into the slot table makes the one allowlisted downcast undefined
  for entry 0, and dropping the arm makes the world resolve as *gone* — the
  exact bug `plan_cworld_trace.md` T3 fixed. It needs the managed directory's
  slot allocator (**§3.3, Phase 2**) and **4g**. `pBase` cannot go while the SDK
  hierarchy still spawns: 302 `.pBase` lines, **Phase 4**.
- *Zero cross-edges into `entitylist.cpp` and `entitylist_base.cpp`.* **4f owns
  both files by name.** All three symbols the shim still needs from
  `entitylist_base.o` are `AddNetworkableEntity`, `AddNonNetworkableEntity` and
  `RemoveEntity` — the engine handle table, which §3.3 requires every shell to
  be in. The slot table deliberately has no allocator, and `slot_table.h`'s
  must-not-compile legs forbid adding one.
- *The cast census reads only family 5 and 6 sites.* Families 1 and 4 do reach
  zero here and are kept as the exit condition. Families 2 and 3 are 34 sites in
  files owned by 3c, 3f, 3g, 4e, 4f and 4g.

The lesson is worth keeping: an exit condition that names a subject another step
owns cannot be met without doing that step's work, and a phase that tries will
either stall or force a change that breaks the later design.

### Phase 2 — Server entity mechanism into managed code

**2a — I/O connections and the event queue.** Owns `EntityDirectory.cs`'s
I/O half, `SourceSharp.Abi/EntityIo.cs` (new), the `KeyValue` arm of
`shell_standalone_base.cpp:1667-1773`, `FireNamedOutput` (`:1816-1929`),
`standalone_relays.cpp`'s `_DeliverInput`, `Tests/EntityIoTests.cs`. Needs 1g.
Connection strings parsed in managed keyvalue handling; delays queued in the
directory; `!activator`/`!caller`/`!self`/`!player` resolved there;
`FireScriptedOutput` and `EntityFireInput` slots deleted. Gate: facts for
ordering, delay, `times`, re-entrancy depth, and a delayed output surviving
hot reload (written into the reload record).

**2b — Touch links.** Owns `EntityDirectory.cs`'s touch half,
`shell_standalone_base.cpp:1075-1389` (`Touch`, `StartTouch`, `EndTouch`,
`UpdateTouchLinks`, `TouchTriggers`, `TellToucher`, `AsToucher`), `toucher.*`,
`Tests/TouchLinkTests.cs`. Needs 1f. Native reports overlapping pairs once per
frame from a partition walk over solid-trigger shells; managed diffs against
the link table and raises start, stay and end ops. ABI: `OverlapReport`
(one call, a span of pairs). Gate: enter/stay/leave from two reports;
`bTouchEveryTick` becomes a managed per-class choice; the live gate's trigger
step.

**2c — Deferred removal.** Owns `EntityDirectory.cs`'s removal half,
`shell_standalone_base.cpp:2639-2906` (`Remove`), `shell_levelsteps.cpp`'s
delete-list arms. Needs 1g. `Remove(handle)` marks; the directory frees at
end of frame with one `DestroyShell` crossing per removed entity batch; the
shell destructor holds only unbind, partition removal, edict release. Gate:
a handle stays valid until end of frame; a double remove is idempotent; the
clean-shutdown step of the live gate destroys every entity.

**2d — Map spawning.** Owns `SourceSharp.Server/MapSpawner.cs` (new),
`Shared/MapEntityBlock.cs`, `Templates/TemplateStore.cs`,
`shell_levelsteps.cpp`'s spawn and activate arms, `shell_templates.cpp`,
`shell_internalfactory.cpp`. Needs 2a. Parse, create, keyvalue, resolve
parents and connections, `Spawn`, `Activate`, in stock order; `mapentities.cpp`,
`subs.cpp` leave the vpc (integrator). Gate: `SandboxPass` reports every
declared classname present; `map_entity_block_test` extended with the spawn
order fixture.

**2e — Shell-less logical entities.** Owns `ScriptedEntity.cs`'s kind
handling, `scripted_kinds.h`, `shell_scripted.cpp`, the engine-invisible
serial allocator in `EntityDirectory.cs`. Needs 2a, 2c. Every class of kind
`Logical` stops creating a shell and takes a handle from 2048–8191. Gate:
`devapi ents "*"` on `dm_lockdown` reports the same classnames with fewer
edicts; an `!activator` from a shell-less entity resolves; a handle from the
invisible range never reaches native (counter).

**2f — Movement integrators, part one.** Owns
`SourceSharp.Server/EntityMotion.cs` (new), `Abi/MoveMath.cs` additions,
`shell_standalone_base.cpp:2117-2238` (`UpdateFlight`), `:2285-2353`
(`StopMoving`), `Tests/EntityMotionTests.cs`. Needs 1g. `FLY`, `TOSS`,
`NOCLIP`, `FOLLOW` integration over the trace ABI, run from `RunFrame`. Gate:
a projectile's trajectory matches the recorded stock fixture; the live gate's
shoot step.

**2g — Movement integrators, part two: push.** Owns `EntityMotion.cs`'s
push half, `shell_standalone_base.cpp:2353-2639` (`BlockerAt`,
`UpdateMovement`), `push_blocker_rule.h`, `standalone_riders.h`,
`shell_mover_velocity.h`. Needs 2f, 2b. `MOVETYPE_PUSH` with riders and
blocking, doors, lifts, trains. Gate: the door and lift steps of the live
gate; the rider fixture.

**2h — Parenting.** Owns `shell_standalone_hierarchy.cpp` (all but
`EnsureBones`/`GetAttachment`/`SetPoseSequence`), `SourceSharp.Abi/Hierarchy.cs`
(new), `shell_parent_frame.h`, `Tests/HierarchyTests.cs`. Needs 1b. Transform
composition in managed matrix math writing the block; deferred parent-name
resolution in the directory; cycle refusal. Gate: a child follows its parent
through a rotation; a cycle is refused by name; removal of a parent detaches
children in one frame.

Phase exit: `shell_standalone_base.cpp` and `shell_standalone_hierarchy.cpp`
contain no body from the §1 list (gtest names them); `physics_main.cpp`,
`physics_main_shared.cpp`, `mapentities.cpp`, `hierarchy.cpp`, `subs.cpp`
are out of the server vpc; the live gate passes.

### Phase 3 — Physics shell, player, movement, rules (server)

**3a — PhysicsShell.** Owns `sourcesharp/game/server/physics_shell.h/.cpp`
(new), the environment-management arms of `game/server/physics.cpp` it
replaces, `test/physics_shell_test.cpp`. Needs 1h. Environment creation and
step, the callback vtables, the event ring, `PostSimulationFrame` drain as one
crossing, the native `ShouldCollide` rule evaluation over the block. ABI:
`PhysicsEvents` (one call, a span), `RegisterCollisionRules` retargeted. Gate:
a gtest over the ring (overflow refuses, order kept); a fact that a rule
change is visible to the next step without a crossing; ring depth reported.

**3b — Physics ids.** Owns `physics_shell.cpp`'s id table,
`shell_physicsobjects.cpp`, `shell_constraints.cpp`, `shell_motioncontrollers.cpp`,
`Abi/EngineAreas.Entities.cs`'s physics area, `Entities/Physics/**`. Needs
3a. Objects, constraints, controllers, springs, shadows addressed by id with
serial; every flat slot retargeted. Gate: stale-id test; the physics-prop step
of the live gate; `PhysicsCannister` facts unchanged.

**3c — Player state and `IPlayerInfo`.** Owns `shell_standalone_player.h`
(shrinking), `shell_gameclients.cpp`'s `GetPlayerState`, `ClientSetupVisibility`,
`ClientEarPosition`, a new `shell_playerinfo.cpp`, `Entities/Players/SsPlayer.cs`.
Needs 1b, 0e. `CPlayerState` lives in the block's server tail; `IPlayerInfo`
and `IPlayerInfoManager` read the block; view origin for PVS asked of managed
once per client per frame. Gate: 0e's probe shows `GetPlayerState` reading
the block; `status` and a plugin-style `IPlayerInfo` query answer.

**3d — Usercmd loop.** Owns `shell_standalone_player.cpp:549-660`
(`QueueUsercmds`, `RunQueuedCommands`, `RunOneCommand`), `SsPlayer.cs`'s
command handling, `Tests/PlayerCommandTests.cs`. Needs 3c, 3e. Commands
decoded natively into a blittable array, one crossing per `ProcessUsercmds`.
ABI: `PlayerCommands`. Gate: N commands, one crossing; the recorded command
fixture produces the recorded positions.

**3e — `Movement`.** Owns `SourceSharp.Abi/Movement.cs` (new, shared),
`MoveMath.cs`, `game/shared/gamemovement.cpp` (leaves), `shell_movement.cpp`,
`shell_shared.cpp:52`, `Tests/MovementTests.cs`. Needs 1b. The full
`ProcessMovement` in managed code over the trace ABI; `IGameMovement` is a
shim vtable copying `CMoveData` across. Gate: the movement parity fixture
(server vs recorded stock) within `PredictionErrors.cs` tolerances;
`movemath-gate` green.

**3f — Player lifecycle.** Owns `shell_standalone_player.cpp` (remaining:
`Spawn`, `ViewPunch`, `SetObserver`, ground entity), `shell_standalone_player.h`
(deleted at the end), `CStandaloneMoveHelper`, `shell_gameclients.cpp`'s
`ClientPutInServer`/`ClientActive`/`ClientDisconnect`, `SsPlayer.cs`. Needs
3d. `ClientPutInServer` binds the reserved edict and creates the managed
player; `CStandalonePlayer` is deleted, the factory's `bPlayer` flag goes.
Gate: join, spawn, die, respawn steps; no class named `CStandalonePlayer`.

**3g — Rules callers.** Owns `shell_gamerules.cpp/.h`, `game/shared/gamerules.cpp`
(leaves), `Entities/Rules/**`, the decision callbacks in `Entry.cs`
(integrator). Needs 3e, 3a. Every native caller of `CMultiplayRules` ported;
the shim keeps the ammo definition and the registration the engine needs;
cast family 5 sites in `shell_gamerules.cpp:1058-1067` go with the callers.
Gate: `CanTakeDamage`, relationships and admission facts unchanged; the
census shows zero `ToBase*` in the shim.

Phase exit: `physics.cpp`, `physics_shared.cpp`, `physics_impact_damage.cpp`,
`physics_npc_solver.cpp`, `physics_prop_ragdoll.cpp`, `physics_fx.cpp`,
`player_pickup.cpp`, `gamemovement.cpp`, `movehelper_server.cpp`,
`gamerules.cpp` are out of the server vpc; the live gate passes with two
players.

### Phase 4 — Server stock deletion to zero

Each step owns one stock cluster and every shim file that referenced it; the
integrator removes the vpc lines and moves the vpc-reading gtests. Order from
the census's reverse topology; the expected batches:

**4a — Effects cluster.** `gib.cpp`, `EntityDissolve.cpp`, `RagdollBoogie.cpp`,
`sound.cpp`, `damagemodifier.cpp`, `gametrace_dll.cpp`,
`effect_dispatch_data.cpp`, `shell_breakpieces.cpp`, `shell_decals.cpp`.
Effects become managed temp-entity emissions. Gate: the ragdoll and
break-piece steps; cross-edges into the cluster zero.

**4b — Sound and recipients.** `SoundEmitterSystem.cpp`, `recipientfilter.cpp`,
`shell_sound.cpp`, `shell_soundent.cpp`, `shell_soundscape.*`,
`shell_tempents.cpp:623` (family 5). Entity parameters become handles; the
emitter moves under the shim as mechanism. Gate: a sound emitted from a
shell-less entity's position; the census.

**4c — Damage and particles.** `takedamageinfo.cpp`, `particle_parse.cpp`,
`particlesystemquery.cpp`, `ndebugoverlay.cpp`, `debugoverlay_shared.cpp`,
`shell_debugoverlays.cpp:139` (family 6). Gate: damage facts; overlays draw
for a handle.

**4d — Bone library extraction (server).** `baseanimating.cpp`,
`animation.cpp`, `ragdoll_shared.cpp`, the `EnsureBones`/`GetAttachment`/
`SetPoseSequence` arms of `shell_standalone_hierarchy.cpp`, a new
`sourcesharp/game/shared/bone_library.h/.cpp`, `shell_mdlcache.cpp:55`
(family 5, `dynamic_cast`). The library takes a `CStudioHdr` and block
inputs and returns matrices; hitbox traces and attachments use it. Gate:
`TestHitboxes` hits a known hitbox on the fixture model; attachment positions
match the recorded values.

**4e — Utilities.** `util.cpp`, `util_shared.cpp` (the four `EntityFromEntityHandle`
helpers, family 3), `collisionproperty.cpp`, `baseentity_shared.cpp`,
`shell_worldquery.cpp` residue. Every `UTIL_*` the shim still calls is either
managed or a shim mechanism function taking handles. Gate: cross-edges zero;
the trace ABI facts.

**4f — The base itself.** `client.cpp`, `cbase.cpp`, `entitylist.cpp`,
`entitylist_base.cpp`, `baseentity.cpp`, `entity_bases.h`, `baseentity_statics.cpp`,
`shell_entity_orphans.cpp`, `shell_ai_orphans.cpp`, `shell_ai_absent.cpp`.
Needs 4a–4e. Gate: the server passes gate 1 and gate 2.

**4g — Shim residue.** `shell_areaportal.cpp` (a managed areaportal entity
calling `engine->SetAreaPortalState`), `shell_abi_sdk_fixture.cpp`,
`AbiBothHierarchies.cs`, `standalone_relays.*`, `world_adapter.cpp` (§3.11),
`IGameEntity` (`public/igameentity.h`), `public/ss_cbaseentity_alias.h`, the
`IServerGameClients` declaration moved out of stock `gameinterface.h:39`,
the step-5 canaries (`ss_step5_sim`), every `SOURCESHARP` guard left in a
compiled file. Gate: the server passes gate 3; `grep -c 'SOURCESHARP' ` over
compiled stock files reads zero.

**4h — The entity-free fifteen.** The (c) TUs stop including `cbase.h`;
`cbase.h` is not compiled into the server. Gate: the vpc names no stock
`game/server` TU that references `CBaseEntity`; the absence file is empty.

Phase exit: all three gates on `server.so`; the live gate in full.

### Phase 5 — Client singletons and the slot table

**5a — `IClientEntityList` over the slot table.** Owns `slot_table.*`'s
client arm, a new `shell_cliententitylist.cpp`, `cliententitylist.cpp`
(leaves), `shell_standalone_client.cpp:247,287` and the three
`IClientNetworkable* → C_StandaloneEntity*` casts (`:2520,2726,3455`),
`shell_beam_endpoint_client.cpp`, `shell_worldquery_client.cpp:166`,
`shell_te_backends_client.cpp` and `shell_te_pool_client.cpp` list sites.
Needs 1c. Gate: the nine methods answer from the table; the census shows zero
`cl_entitylist`/`ClientEntityList()` and zero entity `dynamic_cast` in the
client shim.

**5b — `IBaseClientDLL`.** Owns a new `shell_clientdll.cpp`,
`cdll_client_int.cpp` (leaves), `shell_client.cpp`, `ClientModule.cs`'s stage
entry. ABI: `ClientStage`. Gate: 0e's probe shows every `IBaseClientDLL`
method the engine calls answered; `FrameStageNotify` is one crossing per
stage; the client joins.

**5c — Client engine block and create functions.** Owns
`shell_standalone_client.h/.cpp` allocation and getter arms,
`shell_standalone_client.cpp:2344-2435` (tier create functions),
`shell_recvtable.cpp`, `ClientStandaloneRegistry.cs:117-158`. Needs 1a, 5a.
The render tail lands with its agreement checks; the create function binds
the slot and creates the managed entity from the published descriptor; the
client op dispatch and override bits mirror 1f. Gate: layout facts and
gtests for the tail; an entity exists in managed code before its first data
update; the fingerprint covers the tail.

**5d — Typed handles out of the client shim.** Owns the nine client shim
TUs with `EHANDLE`/`CHandle<T>` (`shell_standalone_client.cpp`,
`shell_rope_render.cpp`, …). Needs 5a. Gate: as 1e, client side.

**5e — The world.** Owns `c_world.cpp` (leaves), `shell_world_nettable_client.cpp`,
`world_subject.h`, `world_net_sync.*`, `SourceSharp.Shared` world state,
`detailobjectsystem.cpp`'s material read. Needs 5c, 4g. The shell bound to
index 0 and the managed `World` on both sides. Gate: `world_shim_test`,
`world_subject_test`, `world_release_test` rewritten to the single world;
the map loads with sky and detail sprites.

Phase exit: `cliententitylist.cpp`, `cdll_client_int.cpp`, `c_world.cpp`,
`c_team.cpp` out of the client vpc; two clients see each other; the
class-list diff is empty.

### Phase 6 — Client mechanism into managed code

**6a — Interpolation.** Owns `SourceSharp.Client/Interpolation.cs` (new),
`shell_standalone_client.cpp:589` (`Interpolate`), `:2675-2697` (the interp
system), `interpolatedvar.cpp` (leaves), `Tests/InterpolationTests.cs`. Needs
5c. Rings per `[Interpolated]` field; one `PreRender` crossing; results in the
render tail. Gate: a stepped input produces the stock-equivalent curve on the
fixture; crossings per frame unchanged by entity count.

**6b — Client think and render policy.** Owns `shell_standalone_client.cpp:599-738`
(`OnPositionChanged`, `AddToLeafSystem`, `RenderGroup`, `UpdateVisibility`,
`RemoveFromLeafSystem`, `ShouldDraw`), `:826-956` (shadow policy),
`shadow_cast_policy.h`, `shell_leaf_link.h`, `client_thinklist.cpp` (leaves),
`ClientStandaloneEntity.cs`. Needs 5c. Policy written into the block on change;
the shell's leaf and shadow calls become block-driven. Gate: render group,
fade and shadow choices asserted as block writes; `client_thinklist.cpp` gone.

**6c — Bone library extraction (client).** Owns `c_baseanimating.cpp`'s
`BuildTransformations`, procedural and jiggle bones, IK, bone merge,
attachments; `shell_standalone_client.cpp:1016-1700`; `bone_merge_cache.cpp`;
`bone_library.*` (shared with 4d, serialised after it); `procedural_bone_rule.h`,
`anim_layer_rule.h`, `bone_cache_key.h`, `bone_merge_key.h`. Needs 4d. Gate:
the skinned-model and viewmodel-merge steps of the live gate; bone matrices on
the fixture equal the recorded set.

**6d — Animation policy.** Owns `ClientAnimationLayers.cs`,
`ClientPoseParameters.cs`, `ClientProceduralBones.cs`, `ClientAttachments.cs`,
`ClientBoneMerge.cs`, `StandaloneMirror.cs`, `sequence_by_activity.h`,
`sequence_events.h`, `anim_name_registry.h`, the server's animation state in
`Entity`. Needs 6c. Sequence, cycle, layers, pose parameters and events owned
by managed code on both sides; `DoAnimationEvents` a no-op. Gate: an event at
a known cycle fires once; layers blend to the recorded weights.

**6e — Ragdolls.** Owns `ragdoll.cpp` (leaves), client `physics.cpp`
(leaves), `physics_main_client.cpp`, `movehelper_client.cpp`, the ragdoll
arms of `shell_standalone_client.cpp`, `Entities/Players/SsRagdoll.cs`, the
client ragdoll class. Needs 3b, 6c. Creation is a flat function over a bone
snapshot returning ids; the pose is written into the render tail by the
physics step. Gate: the ragdoll step; a ragdoll survives a hot reload.

**6f — Prediction ownership.** Owns `shell_prediction.cpp`,
`prediction_ring.h`, `shell_standalone_player.cpp` (client; deleted at the
end), `shell_standalone_player.h` (client), `standalone_player_sync.*`,
`standalone_player_state.h`, `shell_player_orphans.cpp`, the client `Player`.
Needs 3e, 5c. The ring is owned by the managed player and replays through
`Movement`; `IPrediction` bodies call managed code; `CPlayerState` and
`CPlayerLocalData` lose their datamaps. Gate: prediction errors on the
fixture within tolerance; `C_StandalonePlayer` gone; `cs_cl_player_predict`
on by default.

Phase exit: `c_baseanimating.cpp`, `c_baseentity.cpp`,
`c_baseentity_statics.cpp`, `interpolatedvar.cpp`, `client_thinklist.cpp`,
`ragdoll.cpp`, client `physics.cpp`, `physics_main_client.cpp`,
`movehelper_client.cpp`, `bone_merge_cache.cpp` out of the client vpc;
`shell_standalone_client.cpp` holds only engine binding (gtest names the
deleted bodies); the live gate passes.

### Phase 7 — Client stock deletion to zero

One step per group; the integrator edits the vpc and the vpc-reading tests.

**7a — Effect entities over leaf renderers.** `c_rope.cpp`, `c_sprite.cpp`,
`c_entitydissolve.cpp`, `c_func_dust.cpp`, `c_env_fog_controller.cpp`,
`c_impact_effects.cpp`; the shim's `shell_rope_render.cpp`,
`shell_sprite_render.cpp`, `shell_dust_render.cpp`. Gate: each effect draws
on the sandbox map's station; `SandboxPass` green.

**7b — Temp entities.** `c_te_*.cpp`, `c_basetempentity.cpp`,
`c_te_legacytempents.cpp`, `fx_*.cpp` that take an entity; the shim's TE
pool and backends. Gate: the TE test suites (`TempEnt*Tests.cs`) green; the
TE demos on the sandbox map.

**7c — View and client mode.** `viewrender.cpp`, `view.cpp`, `view_scene.cpp`,
`view_effects.cpp`, `viewpostprocess.cpp`, `clientmode_shared.cpp`,
`cam_thirdperson.cpp`, `thirdperson_camera.h`, `view_effect_reach.h`,
`in_main.cpp`, `in_camera.cpp`. De-entitied in place: the local player's eye,
fov and view model come from the block and the managed player. Gate: first
and third person views; screen shake and fade; the viewmodel draws.

**7d — Leaf system and shadows.** `clientleafsystem.cpp`, `clientshadowmgr.cpp`,
`detailobjectsystem.cpp`, `c_pixel_visibility.cpp`, `glow_outline_effect.cpp`.
Renderables addressed only through `IClientRenderable` and slot-table
resolves. Gate: shadows render for a moving prop; the census shows zero
`ClientEntityList()` in these files.

**7e — Particles and sound.** `particlemgr.cpp`, `particle_property.cpp`,
`particles_*.cpp`, `c_soundscape.cpp`, `soundenvelope.cpp`,
`SoundEmitterSystem.cpp`, the material proxies that read an entity. Gate:
a particle system attached to a handle follows it; soundscapes change on
entry.

**7f — Shared movement and collision residue.** `gamemovement.cpp` (client
copy), `physics_main_shared.cpp`, `util_shared.cpp`, `baseentity_shared.cpp`,
`collisionproperty.cpp`, `takedamageinfo.cpp`, `cdll_util.cpp`. Needs 3e,
4e (the shared files are owned whole across both sides). Gate: cross-edges
zero on the client.

**7g — Not shipped.** `achievementmgr.cpp` and siblings, `replay/`,
`hltvcamera.cpp`, `sixense/`, `entity_client_tools.cpp` (an `IClientTools`
refusal stub), `MapOverview.cpp`, `point_bonusmaps_accessor.cpp`,
`basepresence*.cpp`. Gate: the engine's interface probes show each
interface answered or refused by name; the census.

**7h — The entity-free 98.** `cbase.h` out of every remaining client TU.
Gate: the vpc names no stock TU referencing `C_BaseEntity`; the absence
file is empty.

Phase exit: all three gates on `client.so`; the live gate in full with two
clients.

### Phase 8 — Close

**8a — Scaffolding.** `EntityHierarchy`, `AbiBothHierarchies`, the
`OnHierarchy` flag, the dead `OnNetEntity*` slots, `EntityData.cs`'s older
layout model. The tier tables stay (§3.6). Gate: the census; the suites.

**8b — `ContractBaseline`.** Re-derived: `Universal` is what the managed
`Entity` does not answer, argued per entry. Gate: `ContractDiffTests`.

**8c — Test leg.** The census, the gates and the crossing budget become a
`make test` leg through `tools/run-test-legs`. Gate: the leg fails on a
planted cast.

**8d — `plan.md`.** A section recording the decisions in §3 that changed an
earlier one: the family of shells, the native touch edges, the native
interpolation; and one line noting that a table per class is open.

**8e — Close the open defects.** Every defect the port found and did not fix.
They are listed here because the alternative is that they live only in lane
status files, and a defect nobody wrote into the plan is a defect that ships.
**This is not one step; it is six, and they are sized so that no two of them
touch the same file.** Each carries the measurement that found it.

- **8e-render — three pieces of state that never reach the client.**
  `CStandaloneEntity::SetVisible` writes `Block().Head.ModelIndex = 0` because
  there is no `EF_NODRAW` on this hierarchy, so `GetModel()` answers NULL
  forever and an invisible managed brush arrives with no shape at all
  (`func_dust`, `func_smokevolume`, `func_precipitation` all do it). Step 8b
  then found the other half: **`EngineBlockHead.Effects` already exists**, is
  written exactly once to `0` (`shell_standalone_base.cpp:824`) and read
  nowhere — four dead bytes per entity, and precisely the storage
  `m_fEffects`/`EF_NODRAW` wants. World fog and the 3D skybox also do not
  render, and that gap is `PlayerNetState` rather than any one file. Gate: an
  invisible brush is invisible AND still has a model; fog and skybox draw; the
  `key|effects` contract entry and 7a's dust ledger row both come out.

- **8e-sound — the two sound paths that are declared refusals.** A sound
  emitted from a SHELL-LESS entity is dropped: `SourceSharp_EntityEmitSound`
  resolves through `g_pEntityList` and a shell-less entity is in no list — no
  edict, no index, no C++ object. Measured on `dm_lockdown`, where 81 of 473
  live entities are shell-less: three shelled `func_door` gave three
  `EmitSound:` traces, one shell-less `info_target` gave zero. And soundscapes
  cannot change on entry: `SourceSharp_PlayerSetSoundscape` is an empty body
  and `Soundscape_Update` has no caller. Closing designs are in
  `requests/4b.md` §1 and `requests/7e.md` §1. Gate: 7e's soundscape half of
  `tools/7e-particle-sound-gate` goes green; a shell-less entity's sound is
  heard at its own position.

- **8e-entfire — `ent_fire` cannot reach an entity with no edict**, while
  `devapi ents` finds the same entity in the same second. That is every point
  entity `ent_create` makes. It is very probably why step 4e's screen shake and
  fade went ungated until 7c built its own instrument, so closing it is worth
  more than one defect: it gives every later lane a way to drive managed
  entities. Gate: `ent_fire` reaches an edict-less entity, proved by an input
  that changes something a census can see.

- **8e-classes — client classes with nothing behind them.** `InLocalTeam()`
  has returned `true` for every entity in the game for the life of this build,
  because it compares two NULLs; it has no compiled caller, but it is public
  and virtual, so the first new caller gets a permissive default in silence.
  And the Phase 5 exit's "the class-list diff is empty" clause was measured for
  the first time by the `c-team` lane and is NOT empty: `CBoneFollower`,
  `CGameRulesProxy`, `CRagdollPropAttached` and `CRopeKeyframe` have no server
  counterpart. Gate: the class-list diff is empty, and `InLocalTeam` answers
  from state rather than from a NULL comparison.

- **8e-physics — the two open physics faults, and they need the SCC 11 physics
  cluster to land first.** A newly spawned prop occasionally falls straight
  down through the floor, and then every prop after it in that game does too;
  the fallen crate's collision fields read byte-for-byte identical to a healthy
  `func_physbox`'s and to the world's own, so the refusal is BELOW the rule
  table. The unit of trial is a PROCESS, not a round — which is why 40+ rounds
  of `3f-gatebisect` saw nothing — and `tools/3f-gatebisect` now gives a
  2.5-minute trial. Separately, `IVP Failed at ../ivp_utility/ivu_vhash.cxx 157`
  fires during the level shutdown a `load` performs, traced to the physics
  environment deleting a dead physics object whose IVP hash removal does not
  find its element. Gate: 20 fresh-game trials with zero
  sunk props; a `load` cycle that does not abort.

- **8e-ratchets — the checks that still cannot fail.** `ContractBaseline`'s
  `Refusals` list has no ratchet at all, where `Deliberate` and `OpenDefects`
  both have one: a refusal whose subject is deleted would go on excusing
  nothing forever. Nine witnesses and nine mutations, on the `Defect` record's
  `File`/`Code` shape. Gate: deleting any refusal's subject reddens a fact.

**8f — `gamemovement.cpp` leaves, and the managed side starts.** An OWNER
RULING, and it settles a question two lanes correctly declined to answer alone:
7f measured that stock's `ProcessMovement` is the live fallback and handed the
decision on; `phase4-exit` measured it four ways and handed it on again. **The
ruling is: start the managed side, and remove the file.**

Three things hold `gamemovement.cpp` in the build, and they are independent
until the last one:

- **8f-movetypes — the three declared refusals.** Step 3e's managed loop
  answers `false` for `MOVETYPE_OBSERVER`, `MOVETYPE_FLY` and
  `MOVETYPE_FLYGRAVITY`, and the caller runs the stock body. Those three become
  managed. `sourcesharp_movement_managed` reads 1 for every move type
  afterwards, not "1 except three". No vpc change.

- **8f-igamemovement — the interface the engine asks for BY NAME.**
  `EXPOSE_SINGLE_INTERFACE_GLOBALVAR` publishes `IGameMovement`; there are 8
  pure virtuals and 95 `CGameMovement::` definitions behind it. The shim must
  publish that interface itself. **And the fallback for "a build whose managed
  side has not started" is DELETED rather than reimplemented**: a managed side
  that has not started is now a loud failure, not a silent reversion to stock
  movement. A silent fallback is how this port shipped eight silent losses; the
  ninth will not be the movement loop. No vpc change.

- **8f-remove — the file leaves both vpcs**, in one commit, once the two above
  have landed. It is in the SERVER and the CLIENT vpc; a one-sided removal is
  silently wrong. Gate: `gamemovement.cpp` in neither vpc, `IGameMovement`
  still answered by name, the live gate's movement steps pass with two clients,
  and prediction still agrees between the sides.

Phase exit: definition of done on both DLLs; the two-process, hot-reload and
map-reload gates pass; the crossing budget is enforced.

---

## 5. Verification

### The three gates

Run by `tools/entity-gates` against the built DLLs; commands in Appendix A.

| gate | check | today |
|---|---|---|
| base entity | `nm --defined-only` members of `CBaseEntity::`, `C_BaseEntity::`, and of any class whose vtable references theirs, plus a source scan of every compiled TU for `: public CBaseEntity` and kin | **1,185 / 584** (1,207 / 602 unfiltered) |
| entity list | `nm --defined-only` members of `CGlobalEntityList::`, `CClientEntityList::`, `CBaseEntityList::`; source scan for `gEntList`, `ClientEntityList()`, `cl_entitylist`, `IEntityListener` | **62 / 120** (33 / 91 without `CBaseEntityList::`) |
| casts | the cast-census script over the compiled TU list: every pattern in §3.16; the resolver's `static_cast` is allowlisted by file and line and covered by the serial-reuse test | **412 / 301** summed over the twelve gated families |

`nm` counts use symbol types (`awk '$2 ~ /^[TWVDBRt]$/'`) because weak inline
emissions otherwise read as definitions, and they are a floor, not a proof:
`--gc-sections` drops what nothing reaches, so the source scan over the
compiled list is the sufficient check and `nm` is the fast one.

**The bold figure in each row is that strict reading, and it is what the gate
tests.** The bracketed one is §1's table, which was taken with the looser
commands — no symbol-type filter on the first row, and `CBaseEntityList::` left
out of the pattern on the second — so §1 and this paragraph disagreed with each
other until 0a reconciled them. Both readings reproduce exactly, and
`tools/entity-census` and `tools/entity-gates` each carry the loose one as an
explicit `reconcile` row so neither can be quietly reintroduced as the gate.

Do not hand-maintain these numbers. `make entity-census` diffs the whole table
against `docs/entity-census.txt` and fails on drift; `make entity-gates` reports
all three gates and exits non-zero until every gated row is 0. The figures here
are today's reading, recorded once, and the tools are the authority.

### Tests that must exist, and what breaks them

- **Engine block agreement**: one fact per field against `Marshal.OffsetOf`,
  one gtest per field against `offsetof`, and a fingerprint test that reorders
  two fields, retypes one at equal size, and adds one, each of which must
  change the hash.
- **Serial reuse**: bind, unbind, rebind the same index, resolve the old
  handle: `default`. Same on both sides, and once more through the physics
  game-data path.
- **Override bit**: a class with and without an override; the crossing counter
  differs by exactly the ops removed. Mutation-verified: remove the bit check
  and the test is red.
- **Per-frame batching**: with N entities thinking, one `RunFrame` crossing per
  frame regardless of N.
- **Directory**: think ordering with equal times, named contexts, delayed
  outputs firing in order, `!activator` resolution, touch edges from two
  overlap reports (enter, stay, leave), removal deferred to end of frame.
- **Movement parity**: the same command sequence through server `Movement`
  and client prediction gives equal positions; the fixture is a recorded
  stock run.
- **Send path**: a field write marks exactly its offset and never sets
  `FL_FULL_EDICT_CHANGED` (`plan.md` §10).
- **Census**: the committed baseline matches the tree; a stock TU added to
  either vpc, or a shim object gaining a cross-edge, fails.
- **Absence stubs**: the two stub files are empty at Phases 4 and 7.

Every fact is one behaviour; every bug fix lands with a test that was red
first; a check that cannot fail is not a check
(`the-check-that-cannot-fail`). Counts before and after any mechanical edit
are compared, not eyeballed.

### The live gate

`make live-gate` grows to this checklist, each step asserted through `devapi`
rather than read from a log:

join · move · jump · shoot and hit · take damage · pick up health and ammo ·
enter and leave a trigger · open a door · ride a lift · push a physics prop ·
die and ragdoll · respawn · second client joins and both see each other ·
hot reload with the world intact · map reload · clean shutdown with every
entity destroyed.

It also reads the crossing counters and fails over budget. The initial budget
is whatever Phase 0 measures on today's build; each phase may only lower it.

---

## 6. What remains C++ when this is done

| | why the engine needs it natively |
|---|---|
| `CEntityShell`, `C_EntityShell` | the 39 and 77 vtable slots — **both names and both numbers are out of date; see the measurement below the table** |
| the slot table, and `IClientEntityList` over it | the engine addresses entities by index and handle |
| the engine block struct | fixed-offset memory the shell getters and the network decoder read |
| `ServerClass`/`ClientClass`/`SendTable`/`RecvTable` builders and the standard proxies | walked at DLL init, called during encode |
| the game DLL interfaces: `IServerGameDLL`, `IServerGameEnts`, `IServerGameClients`, `IServerGameTags`, `IPlayerInfoManager`, `IBaseClientDLL`, `IPrediction`, `IGameMovement` | resolved by name |
| `PhysicsShell`: environment, the callback vtables, the event ring, the `ShouldCollide` rule evaluation, motion-controller bodies | VPhysics calls mid-step and needs synchronous answers |
| the bone leaf library (`bone_setup.cpp`, `studio.cpp`, the extracted transform builders, and the sequence/activity/event lookup in `animation.cpp`) and the ragdoll builder (`CRagdoll` and `CreateRagdoll` in `game/client/ragdoll.cpp`, `game/shared/ragdoll_shared.cpp`) | pure math over mdl data with a matrix output the renderer consumes. `animation.cpp` reads `CStudioHdr` and answers "which sequence, which event, what bbox"; the ACTIVITY it is asked about is chosen by managed code, and the file names one entity type in a comment and none in code |
| engine-block transform maintenance: parent/child propagation and world↔local conversion (`shell_standalone_hierarchy.cpp:608-944`) | a position write is the hottest path on the hierarchy and cannot be made conditional on managed state native cannot see. The parenting *relation* is managed — step 2h moved the registry, the composition arithmetic, the cycle refusal, the deferred `parentname` and the frame sweep to `SourceSharp.Abi/Hierarchy.cs` — and what is left in this band is fifteen call sites' worth of relay that composes the transform. See §1's correction |
| prop data and surface-property tables (`shell_propdata_system.cpp`, 508 lines) | an engine data file: `scripts/propdata.txt`, loaded through `filesystem` at level init and keyed by the surface properties the physics engine already owns. Zero deciding conditions by the audit's instrument |
| the usercmd codec (`usercmd.cpp`) | bit-level wire format |
| the prediction-shared random generator (`SharedRandomFloat`/`Int`/`Vector`/`Angle`, `util_shared_library.cpp:406-439`) | it must be bit-identical on both sides of prediction, and it is a seeded hash of a name and a tick rather than a decision. A bare `RandomFloat` on a predicted path is a prediction bug; this is the generator that is not one |
| leaf renderers: brush, studio, sprite, beam, rope, particle, world text, cracked glass | mesh building and material calls. **The row covers the emitters and pools these renderers are driven from, not only the draw calls**: the ten `fx_*` emitters (`fx_blood.cpp`, `fx_discreetline.cpp`, `fx_explosion.cpp`, `fx_fleck.cpp`, `fx_line.cpp`, `fx_shelleject.cpp`, `fx_sparks.cpp`, `fx_staticline.cpp`, `fx_tracer.cpp`, `fx_water.cpp`), the beam pool and per-frame update behind `beamdraw.cpp` (`view_beams.cpp`), the temporary-entity pool (`c_te_legacytempents.cpp`, whose `C_LocalTempEntity` is already `: public CShellRenderable, public C_SpriteRenderer` and is not an entity) and the screen overlay (`warp_overlay.cpp`). **`fx_impact.cpp` is NOT on this row** — see the ruling below |
| the leaf system, shadow manager, view render, client mode, input, HUD canvas | client-DLL mechanism the engine's callbacks and VGUI drive. **This row covers the systems, not their inputs:** render group, should-draw, translucency, fade and shadow type are written into the block by managed code when their inputs change (§3.9), not chosen here per draw. Also on it: the flashlight's projected texture (`flashlighteffect.cpp` — it builds a `FlashlightState_t` and hands it to `g_pClientShadowMgr`, and its eighteen ConVars — sixteen `r_*`, two `mat_*_shadowmap` depth biases — are that texture's fov, offsets, attenuation and depth bias, not game rules), the third-person camera offset (`cam_thirdperson.cpp`, whose `cl_thirdperson` is `FCVAR_USERINFO` and so is a transmitted protocol name), and the client half of the engine's voice system (`voice_status.cpp` — a net-message callback and `engine->GetVoiceTweakAPI`) |
| the close-caption and HUD-element registry (`shell_hud_framework.cpp`, `shell_hud.cpp`, 966 lines) | `DECLARE_HUDELEMENT` is how the client DLL's own element list constructs this mod's C# HUD host, and `CHud` is what the engine and a dozen client files call for lifecycle, icons and render-group locking. The `closecaption` and `english` ConVars declared at `shell_hud_framework.cpp:426-427` are `FCVAR_USERINFO` — the engine transmits them and the server reads them back through `engine->GetClientConVarValue` (`shell_recipientfilter.cpp:667`) — so they are protocol names, not tuning values |
| the sound emitter and soundscript lookup (`sound_emitter.cpp`, 1,535 lines) and the looping-sound envelope over it (`soundenvelope.cpp`) | the engine's sound interface and its data files. 80 calls through an engine global, the most of any file in the shim (the next is 62), 21 of them `enginesound->`; it parses the `game_sounds` manifest at level load and turns a soundscript name into a wave, and it has no `SourceSharp_*` entry point because which sound plays, when, and from what already arrive as managed arguments |
| the soundscape system (`shell_soundscape_client.cpp`, 1,696 lines) | parses soundscape KeyValues and drives the engine's layered ambient channels. Which soundscape is active arrives from managed through `SourceSharp_ClientSetSoundscape` (line 515); zero deciding conditions by the audit's instrument |
| material proxies (`shell_materialproxies.cpp`, 585 lines) **and the twenty stock proxies the same macro registers** — `mathproxy.cpp` (18 of them), `matrixproxy.cpp` (2), `entityoriginmaterialproxy.cpp` (3), `AnimateSpecificTextureProxy.cpp`, `IsNPCProxy.cpp`, `ProxyHealth.cpp`, `WaterLODMaterialProxy.cpp`, `WorldDimsProxy.cpp`, `alphamaterialproxy.cpp`, `animatedentitytextureproxy.cpp`, `animatedoffsettextureproxy.cpp`, `animatedtextureproxy.cpp`, `baseanimatedtextureproxy.cpp`, `camomaterialproxy.cpp`, `lampbeamproxy.cpp`, `lamphaloproxy.cpp`, `particle_proxies.cpp`, `texturescrollmaterialproxy.cpp`, `timematerialproxy.cpp`, `toggletextureproxy.cpp` | `EXPOSE_INTERFACE( …, IMaterialProxy, "Shield" IMATERIAL_PROXY_INTERFACE_VERSION )` — the material system resolves a proxy by the name written in the `.vmt` and calls it during material binding. Every one of the twenty carries that macro; `baseanimatedtextureproxy.cpp` is the base three of them derive from and carries none. A proxy that reads the bound entity reads it through the block or a handle, never a typed pointer — `IsNPCProxy` and `ProxyHealth` already do |
| the client tool framework (`toolframework_client.cpp`) | a client-DLL game system the engine's own frame and level callbacks drive, holding the engine's `VCLIENTENGINETOOLS001` — ruled in by lane `loose-ends`; see below |
| the client/server-neutral effects façade (`EffectsClient.cpp`) | the name by which shared code reaches the leaf renderers above it; no interface the engine asks for, no entity, no decision — ruled in by lane `loose-ends`; see below |
| the physics splash/impact effects (`game/server/physics_fx.cpp`, 279 lines) | leaf effect dispatch over a water trace. It defines no entity class, and its ONE entity token is inside a COMMENT (`:189`) — so it is entity-free in code. Its two live sites call `PhysicsOwner_t::GetWaterType()`, the port's own neutral wrapper, NOT an entity; the `CBaseEntity::GetWaterType()` its object owes is emitted by `physicsowner.h:320`'s inline in the UNARMED arm only, and the armed arm returns 0 and owes nothing. Ruled in by lane `f-physics` and verified here rather than taken: ruling it in takes server `physics.o` from 4 owed symbols to 3 |
| trace filters, partition enumerators, recipient filters | callback vtables. The client's enumerators and its engine-call helpers live in `cdll_util.cpp`: `CFlaggedEntitiesEnum` and `UTIL_EntitiesInBox` / `InSphere` / `AlongRay` are `IPartitionEnumerator` implementations, and the rest of the file is angle, trace, screen-space and message helpers. It answers with `IClientUnknown *` after the flip; it decides nothing (4 deciding conditions in 935 code lines) |
| the client-effect registry and its front door (`c_te.cpp`, `c_te_effect_dispatch.cpp`, `clientsideeffects_test.cpp`) | `DECLARE_CLIENT_EFFECT( "bloodspray", … )` is how a name the SERVER puts on the wire reaches a client renderer, and `DispatchEffect` / `TE_DispatchEffect` are the lookup that does it. The name is protocol; the routine it reaches is a leaf renderer on the row above; the choosing in between is a table lookup, not a decision. `c_te_effect_dispatch.cpp`'s own `C_TEEffectDispatch` temp-entity class is already guarded out and leaves with the hierarchy — **the file does not** |
| the effect wire struct and its network proxies (`effect_dispatch_data.cpp`) | `CEffectData` is the `SendTable`/`RecvTable` payload of `DispatchEffect`, on the builders-and-standard-proxies row above. Its one entity accessor (`GetEntity()`) becomes `GetRenderable()` / `entindex()`, both of which it already has |
| the particle library's callback vtable (`particlesystemquery.cpp`) | `IParticleSystemQuery` is how the particle library asks the game for lighting, a trace and a bone position mid-simulation. It is `VParticleSystemQuery001`, which is in **no** engine binary — the library links into the client DLL — so this is a callback vtable rather than a resolved-by-name interface, and it is on the trace-filter row's reason, not the game-DLL-interfaces row's |
| the precache-callback registry (`precache_register.cpp`, 40 lines) | a linked list of registered callbacks run at level load. Zero entity tokens, zero deciding conditions, and its **only** consumer on the client link line is the port's own `world_adapter_client.o` |
| `IMoveHelper` (`movehelper_client.cpp`) | the callback vtable `IGameMovement` calls to touch, trace and play sounds during a move. `IGameMovement` is on the game-DLL-interfaces row and this is the object it is handed; `SetHost` takes a handle after the flip |
| `IClientVirtualReality` (`client_virtualreality.cpp`) | resolved by name: `ClientVirtualReality001` is in the `engine.so` this mod runs on (`strings … | grep -cx` → **1**, against **0** for `VParticleSystemQuery001` and `IViewRenderBeams` in the same command). Same row, same reason, as `IBaseClientDLL`. **Its thirty-two `vr_*` ConVars are the exception this row does not cover** — see the ruling below |
| ConVar and ConCommand **registration** for values managed code declares | the engine owns the console registry and the command dispatch, and `SourceSharp.Abi/Abi.cs:306`'s `RegisterConVar` is how a managed declaration reaches it; 52 ConVars already arrive that way. **No ConVar's default or semantics is declared natively.** A `ConVar` constructed in C++ is a tuning value managed code cannot read, and registration being native is not a licence to declare one there — the only ones that legitimately stay are engine-protocol names the engine itself transmits or resolves, such as `closecaption` and `english` above |
| the CoreCLR host and the bootstrap dispatch | `plan.md` §2–3 |

Nothing on this list decides what the game does. Nothing on it names an
entity type.

### The shell's slot count, measured at the close

The first row above says "the 39 and 77 vtable slots" of `CEntityShell` and
`C_EntityShell`. Both classes were renamed during the port and both numbers
have moved. Measured out of the **debug** binaries this branch deploys — not
counted from the headers, and not taken from an earlier report:

```
$ tools/vtable-dump game/mod_sharp/bin/linux64/server.so CStandaloneEntity
  vtable for CStandaloneEntity  bytes=856  words=107
  sub-vtable 0: 60   sub-vtable 1: 11   sub-vtable 2: 20   sub-vtable 3: 8
  TOTAL 99 virtual slots across 4 sub-vtables

$ tools/vtable-dump game/mod_sharp/bin/linux64/client.so C_StandaloneEntity
  sub-vtable 0: 102  sub-vtable 1: 45   sub-vtable 2: 14
  sub-vtable 3: 5    sub-vtable 4: 20   sub-vtable 5: 8
  TOTAL 194 virtual slots across 6 sub-vtables
```

So: **`CStandaloneEntity`, 99 slots; `C_StandaloneEntity`, 194.** The sub-vtable
count is the number of engine interfaces each shell answers — four on the
server, six on the client — and it is the number that makes the row's argument,
because every one of those is an interface the engine calls through and
therefore the row's own reason (a).

The measurement is only valid against binaries the debug graph describes.
`make release` deploys over `make debug`, and `tools/entity-ratchet` refuses to
measure at all when the deployed build-id does not match the graph — which is
how the mismatch was caught here rather than producing a plausible wrong
number. Re-run `make debug` before quoting anything from `game/mod_sharp/bin`.

### SUPERSEDED, at the close of `plan_shim.md`: the table above is a list of clusters, and `docs/shim-reasons.md` is the list of files

The table above names clusters in prose and carries **no reason letters**.
`plan_shim.md` §1 gave the eight letters after it was written, and §8 required
the table re-derived against the tree rather than transcribed. That is
`docs/shim-reasons.md`, and it is derived from the `SS_REASON` marker each of
the 168 shim translation units now carries in its own header, so it cannot
drift from the tree the way this one has:

| | reason |
|---|---|
| a | a vtable the engine or VPhysics calls through |
| b | an interface resolved by name at load |
| c | static-init registration |
| d | a layout the engine reads at fixed offsets |
| e | a synchronous mid-step answer |
| f | a bit-level wire codec |
| g | leaf math over mdl/bsp data consumed by renderer or physics |
| h | the CoreCLR host and bootstrap dispatch |
| **x** | **no letter fits** — native only until a named native caller goes |

**The per-letter counts are deliberately NOT copied here.** They live in
`docs/shim-reasons.md`, where `shim_reason_letters_test.cpp` checks each row
against its own walk of the tree, so the table cannot say 54 while the tree
says 53. A second copy in this document would be a number nothing re-derives —
which is exactly how the prose table above went eight files and two corrections
out of date.

**Two corrections this re-derivation makes to the table above**, both measured
rather than argued:

- **`IGameMovement` is not resolved by name.** The game-DLL-interfaces row
  lists it alongside `IServerGameDLL` and `IBaseClientDLL`. Re-measured at the
  close against the engine this mod runs on, **with its own positive control**
  in the same command, because a `strings` that finds nothing looks identical
  to a `strings` pointed at the wrong file:

  ```
  E=".../Source SDK Base 2013 Multiplayer/bin/linux64/engine.so"
  strings "$E" | grep -c GameMovement    ->  0
  strings "$E" | grep -c ServerGameDLL   ->  2     # the control
  strings "$E" | grep -c VClient         -> 11     # the control
  ```

  Measured by lane H1. The shell stays, as the object `IMoveHelper` and
  prediction hand around — that is (a), not (b).
- **Eight files are on no row at all**, because no letter fits them. The prose
  table had no way to say that, so they read as though they had been ruled in.
  They have not: each is native only while a named native caller survives, and
  `shim_reason_none` in `docs/shim-ratchet.tsv` caps the count at 8 so it
  cannot quietly grow.

`sourcesharp/test/shim_reason_letters_test.cpp` walks the shim and requires the
marker; the counts above are its counts.

### The two files this list did not name, and why they are on it now

Waves 2 and 3 turned up two compiled client TUs that no row covered.
`c7-fx` and `c7-leaf` each flagged theirs and declined to rule; lane
`loose-ends` ruled on both. **Both stay native and are de-entitied in place,
and both already are.**

**`toolframework_client.cpp` — STAYS.** The question raised was whether the SFM
recording bridge is engine mechanism or is deleted with the entity classes it
records. It is mechanism, on three measurements:

* it holds an interface the ENGINE provides —
  `VCLIENTENGINETOOLS001` is in this mod's own
  `bin/linux64/engine.so` (`strings … | grep -cx` → **1**, against **0** for
  `VCLIENTTOOLS001`, which the client DLL exposes for a tool host rather than
  for the engine);
* it is a `CClientSystemPerFrame`, so the client DLL's own frame and level
  callbacks drive its `Init`, `LevelInit*`, `LevelShutdown*`, `PreRender` and
  `PostRender`;
* its API is consumed by files this section already names —
  `ToolFramework_SetupEngineView` and `ToolFramework_AdjustEngineViewport` by
  `view.cpp` (view render), `ToolFramework_PostToolMessage` by
  `clientshadowmgr.cpp` (shadow manager), `particles_new.cpp` and `fx.cpp`
  (leaf renderers), and by two `src/sourcesharp/game/client` shims.
  **Deleting the file would delete `ToolFramework_SetupEngineView`, which
  `view.cpp` calls unconditionally** — so it cannot leave with the entity
  classes even if the recording half were dead.

Its recording half IS dead here, and stays guarded rather than extracted:
**one** `#ifndef SS_CBASEENTITY_ALIAS` region, which deliberately reaches the
closing brace, because extracting the `IsToolRecording()` gate alone would
leave the two `GetToolHandle()` asks at the foot compiled and unguarded.

**IT IS ENTITY-FREE NOW, AND THIS PARAGRAPH USED TO SAY IT WAS NOT.** When
`loose-ends` wrote the ruling there were **two** guard regions and **exactly
one** surviving unguarded entity token: `CRecordEffectOwner`'s constructor took
a `C_BaseEntity *pEntity` in its SIGNATURE — declared in
`toolframework_client.h`, defined in the `.cpp` — outside every guard, with the
armed arm of its body never reading the parameter. `defect-convar-notify`
retyped that constructor to `IClientEntity *` and asks `clienttools` for the
handle by entindex, which removed the ask the first region guarded. So the
count is now **one region and zero unguarded entity tokens**, and
`section6_rulings_test.cpp` pins both — `ToolframeworkHasTwoGuardRegionsAndNeitherIsNested`
asserts `1u`, `NoUnguardedEntityTokenSurvivesInToolframeworkClient` asserts `0`.
The prose above said two and one for four merges after the code stopped
agreeing with it; `c8-rulings` found the disagreement by running the same
measurement over 65 other files and checking this one as its FIXTURE, which is
the only reason it was found at all. A ruling's prose is not a ruling's
measurement, and only the second is checked by a test.

**`EffectsClient.cpp` — STAYS.** `IEffects` reads like a game interface and is
not one:

* **nothing outside the game DLLs asks for it.** `IEffects001` is **absent**
  from `bin/linux64/engine.so` (`strings … | grep -cx` → **0**, against **1**
  for `VClientEntityList003` and `VEngineClient014` in the same command), and
  the `EXPOSE_SINGLE_INTERFACE_GLOBALVAR` in the file is therefore vestigial:
  every consumer reaches it through the `g_pEffects` global this file assigns
  two lines below;
* **it decides nothing.** Every one of its eleven methods is a
  `SuppressTE` test and a one-line forward into an `FX_*` routine —
  `FX_Smoke`, `FX_ElectricSpark`, `FX_Dust`, `FX_MuzzleEffect`,
  `FX_MetalSpark`, `FX_EnergySplash` — and those routines are the *sprite*,
  *beam* and *particle* leaf renderers this section's own row already keeps
  native. It is the client/server-neutral NAME for that row, not a layer above
  it;
* **it is already entity-free.** Its only entity tokens are three
  `INVALID_EHANDLE` arguments — a sentinel it passes, never an entity it reads.

**A stated divergence, because the lane brief asks for one rather than an
accidental asymmetry:** the SERVER's `EffectsServer.cpp` sends temp entities
where this client one calls `FX_*` directly. That asymmetry is stock's design
and not this port's — the server broadcasts an effect and the client renders it
— so the two sides of `IEffects` are deliberately NOT the same shape, and the
ruling above is about the client half only. The server half is unowned and
unruled; whoever takes it should read this paragraph first.

### The 65 client-closure rows §6 did not cover — ruled. **48 STAY, 17 LEAVE**

`tools/client-flip-closure` prints the 116 TUs that stop linking when the client
entity hierarchy and its list leave `client_csharp.vpc`. **51 of them carry a §6
tag** — 7 the section names outright, 44 matched to a category row by that
tool's own reading — and those are de-entitied in place. **The other 65 carried
no §6 ruling at all.** Lane `c8-rulings` made one for each, against §6's own
closing test and against the tree rather than against the filename:

    tools/client-flip-closure
      CLOSURE                          116 TU(s), 111036 lines
          §6 STAYS NATIVE -- de-entity  51 TU(s), 60015 lines
          everything else               65 TU(s), 51021 lines

    the 65, ruled:   48 STAY (de-entitied in place)   17 LEAVE

**A file that STAYS is as much a ruling as one that goes**, and it is the more
expensive one to get wrong: a STAY that should have LEFT keeps policy in C++
forever, and a LEAVE that should have STAYED deletes a capability the game
needs. Each ruling below therefore says which §6 row carries it, and every
LEAVE names what does the thing afterwards.

The rows the 48 land on are written into the table above rather than only here,
so the section covers them by a rule and not by an appendix.

**And "de-entitied in place" is a bill, so here is its size.** Counting stock
entity type names over the 65 with comments and string literals stripped AND
the port's own guards honoured — `#ifndef SS_CBASEENTITY_ALIAS`, `#ifndef
SOURCESHARP`, and the stock `#else` arm of `#ifdef SOURCESHARP` — with nesting
tracked by depth rather than by "the next `#endif`":

    the 48 that STAY      159 entity tokens in the text,  109 still live
                          34 of the 48 are ALREADY entity-free
    the 17 that LEAVE     948 entity tokens in the text,  703 still live

That 6.4x ratio is the ruling's own check: a STAY list that carried as much
live entity text as the LEAVE list would be a list of files somebody had not
looked at. The residue is concentrated — `ragdoll.cpp` and
`cdll_util.cpp` (26, the enumerator arrays and `GetLocalPlayer()`) carry most of
the 109 — and everything else is single figures.

**THE SCRIPT BEHIND THE THREE AGGREGATES ABOVE IS NOT IN THE TREE.** `c8-rulings`
was forbidden `src/` and `tools/`, so only its prose landed and 159 / 109 / 34
cannot be re-run as written. The counter was rebuilt in
`section6_rulings_test.cpp` and **calibrated against two of this ruling's own
published numbers, which it reproduces exactly**: `cdll_util.cpp` reads 26, and
34 of the 48 are already entity-free. On the other two it disagrees — it reads
**130 in the text and 85 live** — and roughly two thirds of that gap is the
`ragdoll.cpp` error corrected below. The residual is **not** accounted for, and
is left standing here as a disagreement rather than restated as a new number:
the aggregates are the ruling's, the per-file counts the test pins are the
measurement's, and where they differ the test is the one that can be re-run.

**`ragdoll.cpp` does not carry 37, and the split is not 6/31.** The RULING is
unchanged and is the part that matters: the file STAYS on the leaf row, `class
C_ServerRagdoll` inside it LEAVES, and the staying half carries **real residue
rather than none** — the first draft of this paragraph said all of it was in the
class that goes, which would have handed the next lane a file it thought was
finished. Measured two independent ways, the file carries **16** tokens. Split
at `class C_ServerRagdoll` (`:377`): **10** after it, **6 before it, in the
builder that STAYS** —
`CRagdoll::BuildRagdollBounds( C_BaseEntity * )`, `CRagdoll::RagdollBone(
C_BaseEntity *, … )`, two `C_BaseEntity *ent` parameters on `CreateRagdoll`, and
a `static_cast< C_BaseEntity * >( …->GetGameData() )` inside
`CRagdoll::VPhysicsUpdate` which is also a live gate-3 cast site. So the split
ruling below leaves real work behind rather than none, and "all of them are in
the class that goes" would have handed the next lane a file it thought was
finished. The same measurement over `toolframework_client.cpp` and
`EffectsClient.cpp`, the two files §6 already ruled on, reads **0 and 0** live,
which is what calibrates it: it was run against those two FIRST and it is what
found the stale paragraph above. `toolframework_client.cpp` is the sharper of
the two — **1 token in the text, 0 live** — so a counter that ignored the guards
would read 1 there and still pass every other count while counting dead text.
`section6_rulings_test.cpp` pins that pair as the fixture ahead of every number
it takes, and all four of its facts are mutation-verified red.

**WAS 48, AND `animation.cpp` IS THE ONE THAT WENT** — plan_shim.md Phase 2d,
lane `2-leafmath`. §6 ruled it STAYS as leaf math: "reads `CStudioHdr` and
answers which sequence, which event, what bbox; the ACTIVITY it is asked about
is chosen by managed code". The first half of that was true and the second half
is what ended it.

About 82% of the file had no in-vpc caller at all by the time the entity port
landed — `ExtractBbox`, `SelectWeightedSequence`, `GetEyePosition`, the
bodygroup and hitbox-set family, `GetAnimationEvent`, all pulled only by objects
the build had evicted. Its last live tenant was the activity/event INDEX family
(`SetActivityForSequence`, `SetEventIndexForSequence`, `VerifySequenceIndex`),
which exists to look a name up in a per-level registry and WRITE THE ORDINAL
BACK INTO MDL MEMORY so that a native reader can compare it against an enum.
That is not leaf math over mdl data; it is a numbering scheme kept in step with
a second numbering scheme, and its two readers were `precache_model.cpp` (which
compares event NAMES now) and `studio.cpp`'s activity-to-sequence mapping
(reached only by the client's `::LookupSequence` fall-through, which was a
prediction defect: the server's lookup refused that fallback and answered -1 for
the same name).

So the file went, with `shell_animlists.cpp`, `anim_name_registry.h`,
243 lines of `studio.cpp` and the 2,048-line `ActivityCatalog.cs` that fed the
registry. The QUESTIONS it answered are a `ModelDescriptor` crossing and one
managed rule, which is also the only shape that makes the two sides agree by
construction.

#### The 47 that STAY, by the row that carries them


    material proxies (20)
      AnimateSpecificTextureProxy.cpp  IsNPCProxy.cpp  ProxyHealth.cpp
      WaterLODMaterialProxy.cpp  WorldDimsProxy.cpp  alphamaterialproxy.cpp
      animatedentitytextureproxy.cpp  animatedoffsettextureproxy.cpp
      animatedtextureproxy.cpp  baseanimatedtextureproxy.cpp
      camomaterialproxy.cpp  entityoriginmaterialproxy.cpp  lampbeamproxy.cpp
      lamphaloproxy.cpp  mathproxy.cpp  matrixproxy.cpp  particle_proxies.cpp
      texturescrollmaterialproxy.cpp  timematerialproxy.cpp
      toggletextureproxy.cpp

    leaf renderers, their emitters and their pools (14)
      fx_blood.cpp  fx_discreetline.cpp  fx_explosion.cpp  fx_fleck.cpp
      fx_line.cpp  fx_shelleject.cpp  fx_sparks.cpp  fx_staticline.cpp
      fx_tracer.cpp  fx_water.cpp  view_beams.cpp  c_te_legacytempents.cpp
      warp_overlay.cpp  ragdoll.cpp (the CRagdoll half -- see the split below)

    the client-effect registry and its wire struct (4)
      c_te.cpp  c_te_effect_dispatch.cpp  clientsideeffects_test.cpp
      effect_dispatch_data.cpp

    client-DLL mechanism the engine's callbacks drive (6)
      flashlighteffect.cpp  cam_thirdperson.cpp  voice_status.cpp
      client_virtualreality.cpp  movehelper_client.cpp  cdll_util.cpp

    callback vtables, leaf math and registration (3)
      particlesystemquery.cpp  soundenvelope.cpp  precache_register.cpp

`ragdoll.cpp` appears once, on the leaf row, because the FILE keeps its vpc row;
the class inside it that leaves is named in the split ruling below rather than
given a row of its own. 20 + 14 + 4 + 6 + 3 = 47.

#### The 17 that LEAVE, and what does the thing afterwards

| TU | why it is not §6's | what does it afterwards |
|---|---|---|
| `c_baseentity.cpp` · `c_baseentity_statics.cpp` · `c_baseanimating.cpp` | they DEFINE the hierarchy | `C_EntityShell` and the managed entity (§3). **Carve-out, the same shape as `physics_main_shared.cpp`'s below:** the bone-access lock and the bone thread pool are `C_BaseAnimating` STATICS defined in `c_baseanimating.cpp` — `PushAllowBoneAccess` (`:3101`), `PopBoneAccess` (`:3115`), `InitBoneSetupThreadPool` (`:2761`), `ShutdownBoneSetupThreadPool` (`:2765`), `ThreadedBoneSetup` (`:2769`), `UnlockStudioHdr` (`:957`) — and the port's own files already call four of them (`shell_clientdll.cpp:1083,2106,2146`, `shell_view_facts.cpp:509`). They are §6's bone-leaf-library row wearing the class's name and must be re-homed, not deleted with it |
| `cliententitylist.cpp` | it IS the list | the slot table and `IClientEntityList` over it — §6's own second row. **It leaves LAST**: all 116 closure members depend on it and it on none of them, and the port's own eleven `SourceSharp_LegacyList*` / ask-counter definitions live inside it until lane `c8-listseam` moves them out |
| `c_entitydissolve.cpp` | `C_EntityDissolve : public C_BaseEntity, public IMotionEvent` — an entity class, declared in `c_entitydissolve.h` and so invisible to gate 1's ": public \<entity\>" scan | a managed dissolve; its `BuildTeslaEffect` / `DoSparks` / `ComputeRenderInfo` half is leaf rendering and must land on a shim, not be dropped |
| `physics_main_client.cpp` | fifteen out-of-line `C_BaseEntity::Physics*` members and nothing else. Its own header says "THIS WHOLE FILE IS REMOVAL-SET TAIL", and it is **already entirely inside `SS_CBASEENTITY_ALIAS`** — 20 entity tokens in the text, **0** live | managed `Movement` |
| `baseentity_sound_members.cpp` | out-of-line `CBaseEntity::EmitSound` / `StopSound` members; 9 entity tokens, **0** live | the managed entity's sound calls through `sound_emitter.cpp`, which STAYS |
| `baseentity_shared.cpp` · `physics_main_shared.cpp` | `CBaseEntity::` and `CBasePlayer::` members shared by both sides — 98 and 104 entity tokens. These are lane `c8-shared6`'s and are inside BOTH atomic cycles | the managed entity and managed `Movement`. **Carve-out:** `physics_main_shared.cpp` also defines `ClientSystem_DataObjectAccess()`, a live `kClientSystems[]` row, which has to be re-homed rather than deleted with the file |
| `gamerules.cpp` | the definition of "decides what the game does": `CanHaveAmmo`, `GetPlayerSpawnSpot`, `IsSpawnPointValid`, `CanHavePlayerItem`, `RadiusDamage`, `ClientCommand`. 23 deciding conditions and four map classnames in 708 code lines | `shell_gamerules.o`, which is already on the client link line. 16 objects on that link reference `g_pGameRules` and 7 of them are the port's own |
| `particle_property.cpp` | `CParticleProperty` is `pEntity->ParticleProp()` — a component OF the entity, not a renderer. 740 lines of which **76 are still live**; the rest is already guarded out | the managed entity owns its particle attachments; `CNewParticleEffect` and `particlemgr.cpp` stay |
| `particle_parse.cpp` | every `DispatchParticleEffect` overload takes a `CBaseEntity *` and ends at `pEntity->ParticleProp()->Create(...)`. It is the entity component's front door and cannot outlive it | `SourceSharp_ParticleSystemStart` / `Stop` / `FollowPoints`, already in the shim |
| `env_wind_shared.cpp` | `CEnvWindShared` is `DECLARE_EMBEDDED_NETWORKVAR` — the `env_wind` entity's networked state (min/max wind, gust delays, gust direction change) plus the simulation that runs on it | `shell_effects_sim.cpp` and `sourcesharp/game/shared/wind_think_schedule.h`, which already exist and already have a gtest. **Constraint to carry:** `c_rope.cpp`, `particles_simple.cpp`, `c_func_dust.cpp` and `c_te_legacytempents.cpp` all STAY and all read the wind speed, so the value must stay readable natively after the flip |
| `fx_impact.cpp` | `GetImpactDecal( pEntity, iMaterial, iDamageType )` chooses a decal from a surface material and a damage type, `PlayImpactSound` chooses a sound, and `FX_AffectRagdolls` applies an impulse to every ragdoll along the shot ray by damage type. The last of those changes game state; the other two are game semantics | `sourcesharp/game/client/shell_impact_effects.cpp`, which already carries `FX_DebrisFlecks`, `FX_GlassImpact` and `GetColorForSurface` |
| `weapons_resource.cpp` · `weapon_parse.cpp` · `weapon_parse_default.cpp` | the weapon script database: `FileWeaponInfo_t`'s clip sizes, slots, buckets and sounds are what the game does, and `scripts/weapon_*.txt` is **not** an engine data file — nothing outside the game DLLs reads it. That is the whole distance between this and `shell_propdata_system.cpp`, which §6 keeps for the opposite reason | the managed weapon declaration. **And this trio is a SECOND atomic commit nobody has scheduled**: `tools/p4-exit-batches --side client` batch 45 is exactly these three, 779 lines, and `nm` over all 287 client objects finds **no consumer of `gWR`, `WeaponsResource::` or `PrecacheFileWeaponInfoDatabase` outside the trio itself** — its HUD callers (`weapon_selection.cpp`, `history_resource.cpp`, `hud_ammo.cpp`, `c_baseplayer.cpp`) are not compiled into this project. **Said plainly, as COMMON.md requires: the capability being removed is already unreachable in this build.** It is held in the 116-closure by one symbol, `g_pEntityList`, so it does not have to wait for the flip |

#### The four hardest, and why

**1. `ragdoll.cpp` — STAYS, and its entity class LEAVES. It is one file doing
two jobs and the closure seeds it as one.** `tools/client-flip-closure`'s
`DEFAULT_SEED` lists it among the seven hierarchy definers, and `COMMON.md`
lists it among the five client TUs that still define an entity class. Both are
right about `C_ServerRagdoll : public C_BaseAnimating` at `ragdoll.cpp:377`, and
that decl is — measured, over every compiled TU in the closure with comments
stripped and the port's guards honoured — **the ENTIRE client column of gate 1's
": public \<entity\>" row, which reads 1.** But the same file's other half is
`CRagdoll`, `CreateRagdoll`, `RagdollBone`, `BuildRagdollBounds`,
`VPhysicsUpdate` and `CheckSettleStationaryRagdoll` — **the ragdoll builder §6
names on the bone-leaf-library row**. So the ruling is a split: the file keeps
its vpc row and `C_ServerRagdoll` (with `C_ServerRagdollAttached`) is deleted
from it in place. This is `p4-flip`'s `physics.cpp` argument, one file over: the
graph put the file in the removal set, the plan did not.

**2. `c_te_effect_dispatch.cpp` — STAYS, although it is a seed member.** It is
seeded for `C_TEEffectDispatch : public C_BaseTempEntity`. That class is
**already fully inside `SS_CBASEENTITY_ALIAS`**: the file has 2 entity tokens in
its text and **0** live. What IS live is `DispatchEffect`, `TE_DispatchEffect`,
`DispatchEffectToCallback` and `RecordEffect` — the lookup that turns a name the
server put on the wire into a `DECLARE_CLIENT_EFFECT` callback. Deleting the
file to delete a class that is already gone would delete the client's whole
effect dispatch. **The seed is a list of files that DEFINE the hierarchy; it is
not a removal list, and two of its seven members are not one.**

**3. `view_beams.cpp` — STAYS, and §6's "beam" leaf renderer is not what it
is.** This is the ruling lane `c8-tempents-beams` is blocked on, and the brief
is right that the beam leaf renderer and the beam entity list are different
objects: `beamdraw.cpp` builds the mesh, `view_beams.cpp` is `CViewRenderBeams`
— `BeamAlloc`, `BeamFree`, `InitBeams`, `ClearBeams`, `ShutdownBeams`,
`UpdateTempEntBeams` and `DrawBeam` behind the global `beams`. It stays
regardless, on the measurement rather than on the name: **0 deciding conditions
in 1,523 code lines**, one ConVar (`r_DrawBeams`, `FCVAR_CHEAT`, a wireframe toggle),
`IViewRenderBeams` in **no** engine binary so nothing resolves it by name, and
its `Beam_t` pool is the same kind of object as the tempent pool — a renderer's
own free list, not entity storage. Its three live entity tokens are two legacy
overloads (`KillDeadBeams`, `ComputeBeamEntPosition`) that already have handle
twins beside them, and whose only stock caller is `C_BaseEntity::Term`.

**4. `client_virtualreality.cpp` — STAYS on the interface, and its ConVar block
is a stated exception rather than a clean pass.** The engine resolves
`ClientVirtualReality001` by name — `strings … | grep -cx` → **1** over
`~/.steam/steam/steamapps/common/Source SDK Base 2013 Multiplayer/bin/linux64/engine.so`,
which is the engine this mod runs on (`gameinfo.txt` `SteamAppId 243750`) and
the only `engine.so` on this machine, since the repo ships none. Positive
control in the same command: `VClientEntityList003` **1** and
`VEngineClient014` **1**; negative alongside, `VParticleSystemQuery001` **0**
and `IViewRenderBeams` **0**. So the file is on
the same row as `IBaseClientDLL` and cannot leave. But it declares **32
`vr_*` ConVars and one `ConCommand`**, the largest native ConVar block anywhere
in the 116-TU closure (`tools/shim-policy-audit` reads 33 because its
`CONVAR_RE` counts `ConCommand` too — re-counted with a second command, because
33 and 32-plus-one are different statements and only one of them is true), and
some of them are not display settings: `vr_moveaim_mode` selects between
"move+shoot from face", "move with torso" and "shoot with face+mouse cursor",
and `vr_moveaim_reticle_yaw_limit` is the angle past which the mouse drags the
torso. **That is aiming policy declared in C++, which §6's ConVar row does not
sanction**, and it is recorded here as a named exception rather than absorbed by
the row: the arm is unreachable in this mod (`vr_activate_default 0`), which is
why it is a follow-on and not a blocker. Whoever ports it moves the mode
selection to a managed declaration and leaves the interface where it is.

#### What the instruments got wrong, measured

`tools/shim-policy-audit` measures §6's two clauses directly and its bands hold
across these 65 — but three of its readings are false and one of gate 1's is a
floor in a way that is not printed:

* **`MATERIAL_[A-Z0-9_]+` is in its game-token list**, so `MATERIAL_VAR_TYPE_FLOAT`
  — the material system's own data-type enum — counts as "what the game does".
  `mathproxy.cpp` therefore reads **41.4 deciding conditions per kloc**, above
  every plan-MOVES file's band, for 33 branches that dispatch on a material
  variable's type. `texturescrollmaterialproxy.cpp` (10.6) and
  `toggletextureproxy.cpp` (11.8) are the same false positive, smaller.
* **`CONVAR_RE` counts every ConVar**, not only ones whose text carries a
  game-semantic token, and `decide_per_kloc` adds that count in whole. So
  `flashlighteffect.cpp` reads **47.5**, the highest of the 65, for eighteen
  projected-texture parameters that decide nothing — sixteen `r_*` and two
  `mat_*_shadowmap` depth biases; and
  `client_virtualreality.cpp` reads 34.3 on a ConVar count alone, with
  `decide_conds` **0** — and that count silently includes one `ConCommand`,
  because `CONVAR_RE` matches both. The signal is right for the second file and
  wrong for the first, and no reading of the number alone separates them.
* **`lines` is one greater than `wc -l` per file** (`split('\n')` on a
  trailing newline). `code_lines`, which every rate is computed from, is
  unaffected.
* **Gate 1's client ": public \<entity\>" column is a floor for a reason the
  tool does not print**: it scans compiled TUs and shim headers, so
  `C_EntityDissolve : public C_BaseEntity`, declared in the stock header
  `c_entitydissolve.h:16`, is not counted. The column reads 1 and there are at
  least 2.

**The bands hold at the MEDIAN and fail at the TAIL, which is where a lane
reads them.** Over the 65, split by the ruling above:

    decide_per_kloc      n     min   median    max
      the 48 that STAY   48    0.0      0.0   47.5
      the 17 that LEAVE  17    0.0      5.6   41.0

The medians are 0.0 against 5.6 and the separation is real. But **the three
highest-scoring files in the whole 65 all STAY** — `flashlighteffect.cpp` 47.5,
`mathproxy.cpp` 41.4, `client_virtualreality.cpp` 34.3 — and all three are the
false positives above. A lane that sorted by this column and cut at a threshold
would delete the flashlight, the material math proxies and the VR interface,
and keep `particle_parse.cpp` (17.3), which leaves.

**And the branch-density warning in the wave brief is confirmed from the other
side.** The four files with the highest deciding-condition counts in the 65 —
`c_baseentity.cpp` (47), `baseentity_shared.cpp` (42), `physics_main_shared.cpp`
(39), `mathproxy.cpp` (33) — split three-to-one the right way, and the one that
breaks is the leaf that branches per material variable. Density ranked it above
`gamerules.cpp`, which is the file this whole section exists to move.

---

## 7. Risks, and what is decided up front

- **`ShouldCollide` cannot cross.** VPhysics calls it per pair per step. The
  rule table it evaluates natively must be expressive enough for every
  managed rule the mod has; if a rule needs entity state outside the block,
  the state moves into the block. Decided: no exception path that crosses.
- **`IMotionEvent` cannot cross.** Custom motion controllers are not
  supported; the four stock controller kinds are parameterised. Decided, and
  recorded in `ContractBaseline`.
- **Managed per-frame cost.** Interpolation, movement integration and the
  overlap diff run in C# every frame. Move first, measure after
  (`move-first-measure-after`); the crossing and frame-time counters are the
  measurement, and the fix for a hot loop is a better loop, not a return to
  native policy.
- **Engine block drift.** Two declarations of one layout. Three checks, and
  the fingerprint is generated from the C# declaration so a C++ edit alone
  cannot pass.
- **Tier width.** A class whose blob outgrows the widest tier fails at
  registration, as now; the refusal names the class and the width. A table
  per class would lift this, and is the recorded later option (§3.6).
- **The 47 vpc-reading gtests.** They move with every batch; the census test
  replaces the ones that pinned presence.
- **`IServerGameClients` is declared in stock `gameinterface.h`.** It moves
  into the shim in Phase 4 batch 5; the split test that pins the `EXPOSE`
  macro text moves with it.
- **Prediction parity.** One `Movement` on both sides removes the class of
  bug; the parity fixture proves it and the tolerances already recorded
  (`PredictionErrors.cs`) are the acceptance.
- **Deletion instead of porting.** The temptation on every batch is a stub.
  The stub files must be empty at the phase exit, and a behaviour that is
  refused is a `ContractBaseline` entry with a reason, never a silent
  no-op.
- **Casting to make a batch close.** `dynamic_cast` count is a gate from
  Phase 0, so a batch that adds one fails before it merges.

---

## Appendix A — How the measurements are taken

Object tables, not linker output (`ld-stops-naming-objects`):

```bash
N=src/_vpc_/ninja/sdk_csharp_debug.ninja
grep -oP '^build _vpc_/ninja/debug/objs/\K[^/]+' $N | sort | uniq -c
grep -oP "^build _vpc_/ninja/debug/objs/server_sourcesharp/[^ ]+\.o: cxx \K\S+" $N | sed 's|/[^/]*$||' | sort | uniq -c
grep -oP "^build _vpc_/ninja/debug/objs/client_sourcesharp/[^ ]+\.o: cxx \K\S+" $N | sed 's|/[^/]*$||' | sort | uniq -c
```

The three gates, on the built DLLs:

```bash
B=game/mod_sharp/bin/linux64
for so in $B/server.so $B/client.so; do
  echo "$so"
  nm -C --defined-only $so | awk '$2 ~ /^[TWVDBRt]$/' | grep -c 'CBaseEntity::\|C_BaseEntity::'
  nm -C --defined-only $so | awk '$2 ~ /^[TWVDBRt]$/' | grep -c 'CGlobalEntityList::\|CClientEntityList::\|CBaseEntityList::'
done
# cast census: every compiled TU, from the ninja table, every pattern in §3.16
# (tools/entity-census carries this list; read patterns with `read -r` or the
# backslashes are lost and every count is wrong, which happened once)
cat > /tmp/castpat.txt <<'PAT'
cstyle	\(\s*(C_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon|Flex|AnimatingOverlay)|CWorld|C_World)\s*\*\s*\)
static_cast	static_cast\s*<\s*(C_?Base(Entity|Player|Animating|CombatCharacter|CombatWeapon)|CWorld|C_World|CStandalone[A-Za-z]*|C_Standalone[A-Za-z]*)\s*\*
reinterpret_cast	reinterpret_cast\s*<\s*(C_?Base[A-Za-z]*|CStandalone[A-Za-z]*|C_Standalone[A-Za-z]*|IGameEntity)\s*\*
assert_cast	assert_cast\s*<
dynamic_cast	dynamic_cast\s*<
ToBaseX	\bTo(BasePlayer|BaseCombatCharacter|BaseAnimating|BaseCombatWeapon|BaseFlex)\s*\(
Instance	CBaseEntity::Instance\s*\(
EntityFromHandle	\b(EntityFromEntityHandle|GameEntityFromEntityHandle|TraceEntity|TraceGameEntity)\s*\(
GetBaseEntity	->GetBaseEntity\s*\(
GetContainingEntity	\bGetContainingEntity\s*\(
GetGameData	GetGameData\s*\(\s*\)
EHANDLE	\b(EHANDLE|CHandle\s*<)
PAT
grep -oP "^build _vpc_/ninja/debug/objs/server_sourcesharp/[^ ]+\.o: cxx \K\S+" $N > /tmp/tus
while IFS=$'\t' read -r name pat; do
  printf "%-20s %s\n" "$name" "$(xargs -a /tmp/tus grep -cE -- "$pat" | awk -F: '{n+=$2; if($2>0)f++} END{print n" in "f" files"}')"
done < /tmp/castpat.txt
```

Cross-edges from the shim into stock entity code (the ordering instrument):

```bash
O=src/_vpc_/ninja/debug/objs/server_sourcesharp
nm -u  $O/sourcesharp/game/*/*.o            | awk '{print $2}' | sort -u > /tmp/shim_undef
nm --defined-only $O/game/server/baseentity.o $O/game/server/entitylist.o \
   $O/game/server/physics.o $O/game/server/util.o ... | awk '$2 ~ /^[TWVDBRt]$/ {print $3}' | sort -u > /tmp/stock_def
comm -12 /tmp/shim_undef /tmp/stock_def | wc -l
```

Pure virtuals per engine interface:

```bash
awk '/^(abstract_class|class) IClientRenderable/{p=1} p&&/= 0;/{n++} p&&/^};/{print n; exit}' src/public/iclientrenderable.h
```

Managed facts and the test legs:

```bash
grep -rho '\[Fact\]' --include=*.cs src/sourcesharp/managed/SourceSharp.Tests | wc -l   # 3196
MSBUILDDISABLENODEREUSE=1 dotnet test src/sourcesharp/managed/SourceSharp.Tests -m:1 --nologo
src/buildcpptests <test_name>
make live-gate
```
