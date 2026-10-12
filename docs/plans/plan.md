# SourceSharp — C# Game Logic on Source SDK 2013

A thin C++ shim hosting a .NET runtime, with game logic authored in C#.

This document records the design decisions and the reasons behind them. It is
not a work log: what was built when, how it was verified, test counts and open
tasks live in the git history and in `docs/`. Each section states a decision and
the finding that forced it, so a later reader can tell a deliberate choice from
an accident.

---

## 1. Constraints that shape everything

**64-bit only.** .NET has no `linux-x86` runtime at all. This fork builds
`linux64`, which is the only reason the project is feasible; stock 32-bit SDK
2013 would have been a dead end on Linux.

**One CLR per process.** A listen server loads `client.so` and `server.so` into
the same process, and CoreCLR initialises once per process. The two sides
therefore cannot each host their own runtime.

**C# cannot supply a C++ vtable.** `g_pClientMode`, `input`, `prediction`, the
game rules and the player are C++ objects the engine calls through vtables every
frame with C++ types. "Implemented in C#" therefore means: C++ holds thin shells
that implement the required interfaces and route every call into C#, and the
behaviour lives in C#.

**The engine is a closed, stripped binary.** It reads `edict_t` fields at fixed
offsets and calls virtuals on our objects. Every networked entity must be a real
C++ object with a real vtable at a real address; a managed object can never be
handed to the engine directly.

**No borrowed mod code.** The first cut layered SourceSharp on top of HL2MP, and
C# was a passenger owning no gameplay. The intended end state is the opposite:
no HL2MP or SDK-template C++ in the mod, with everything `client_base` and
`server_base` require answered by SourceSharp's own shells. The SDK template was
rejected as an intermediate step because it is still borrowed gameplay and has
bit-rotted (undefined activities, stale enums, case-sensitive include failures).

**The contract is the undefined symbol list.** Building with only
`client_base`/`server_base` plus the shim and collecting every undefined symbol
gives the exact set the mod must supply: 28 symbols across server, client and
shared. This replaced guessing about which interfaces matter. The shells are thin
because every base class they publish (`CInput`, `CPrediction`,
`ClientModeShared`, `CGameMovement`, `CMultiplayRules`, `CAmmoDef`) is already
compiled into the base libraries; the shells instantiate and publish, they do not
reimplement.

**Injection point is `IGameSystem`.** It already provides Init, Shutdown,
LevelInit, LevelShutdown and per-frame hooks on both sides, so the shim registers
a game system rather than patching `CServerGameDLL` or `CHLClient`.

---

## 1a. Principles

Two rules apply to every part of the design below. Where an existing piece
breaks one of them, the rule is the target and the deviation is a debt, not a
precedent.

### Native code exists only to bind the engine

C++ is written only where the engine leaves no choice: a vtable the engine calls
through, an interface it resolves by name, a registration that must happen at
static-init time, or a memory layout it reads at fixed offsets. Everything else
lives in managed code: policy, decoding, formatting, bookkeeping, and every
decision about what the game does.

Why:

- **C++ is the expensive side.** A change there needs the container build, is
  invisible to the managed test suite, and is not hot-reloadable. Every line
  moved into C# becomes testable without an engine and iterable without a
  rebuild.
- **The boundary stays small and auditable.** The 28-symbol contract
  (section 1) is the whole surface the engine demands; a shell that grows past
  what the engine asks for has started to accumulate game logic in the wrong
  place. The client-side blob decode was moved out of the shell for exactly this
  reason (section 4): C# wrote the layout, so only C# knows what the bytes mean.
- **Behaviour in C++ is behaviour that is not the mod's.** The goal is a mod
  whose gameplay is authored in C#; a shell that decides anything is borrowed
  mod code by another name (section 1).

The test for a candidate line of C++ is "does the engine need this to exist
natively?" If the answer is "it was convenient", it belongs in C#.

### No statics or globals; objects are owned by the game DLL instance

The engine creates one `CServerGameDLL` or `CHLClient` per DLL load and drives
it through `DLLInit` and `DLLShutdown`. That instance's lifetime is the natural
scope for everything SourceSharp owns on a side. Registries, rules, entity
tables, subscriptions and caches are instance members reached through the side
module that the game DLL instance creates and destroys, not static fields or
file-scope globals. Interfaces are used at the seams so that a component
receives what it depends on rather than reaching for a global.

Why:

- **Two sides share one process, and statics do not know that.** The
  process-wide `Engine` static was overwritten by the second side's load and
  routed every runtime call to the wrong DLL (section 3). A static has one value
  per load context; the design has two live sides, so any static in shared code
  is a latent cross-talk bug.
- **Reload re-runs initialisation over surviving statics.** A static event
  accumulated a second subscription on reload and would have doubled every
  score (section 6). Idempotency guards are patches over that; an object that
  dies with the DLL instance and is rebuilt with it needs none.
- **Statics pin load contexts.** A collectible context unloads only when nothing
  references it, and a static rooted in the wrong assembly is the classic silent
  leak (section 7). Instance-owned state dies when its owner is dropped.
- **Statics are untestable in the way that matters here.** Every test would need
  to reset global state between facts and could not run two sides in one
  process, which is precisely the configuration the listen server produces.
  Constructing a fresh side module per test is the same operation the engine
  performs per DLL load.
- **Lifetime becomes visible.** When every object hangs off the instance the
  engine created, "what exists after `DLLShutdown`?" has one answer: nothing.
  With globals, that question needs an audit.

The one sanctioned exception is the bootstrap's dispatch indirection (section
3): native needs a function pointer that never changes across a managed reload,
and a single mutable reference in the non-collectible bootstrap assembly is the
only way to provide one. It holds a reference to the current side module and
nothing else. Native shells follow the same rule with the tools C++ offers: the
game system owns its state, and a file-scope object exists only where the engine
requires static-init registration (section 9).

---

## 2. Hosting the runtime

**Framework-dependent hosting with `DOTNET_ROOT` pointed at a bundled runtime.**
This form is documented to work with `hostfxr_initialize_for_runtime_config`;
self-contained component hosting is the one genuinely uncertain piece of the
design and was deferred. Bundling means players need no .NET install. The
runtime is .NET 10, the current LTS.

**The host is a static library, not a shared one.** The original plan called for
a shared `libsourcesharp.so` on the grounds that a static lib gives each DLL its
own init flag and the two would race to start the runtime. That reasoning was
wrong: hostfxr documents `Success_HostAlreadyInitialized` as a *successful*
result when the config is compatible, so hostfxr itself is the single-CLR
guarantee, not our refcount. A static lib also avoids shipping an extra `.so`
plus RPATH handling that the VPC build has no machinery for. Confirmed in the
live process: the server DLL gets `Success` and the client DLL gets
`Success_HostAlreadyInitialized`. Consequence: the refcount is a per-DLL guard,
not a process-wide one.

**Two collectible `AssemblyLoadContext`s**, one per side, each loading the
shared assembly plus its own. This maps onto how Source already works: shared
code compiled into both sides under `CLIENT_DLL`/`GAME_DLL`, with the sides
exchanging only network messages. It also makes independent hot reload possible.

**The contract assembly is returned explicitly from the load context, not
resolved by fallback.** Under component hosting the default context does not
reliably probe for it even when it is in the deps file. Returning the already
loaded instance also makes type identity unconditional rather than dependent on
probing order.

**`EnableDynamicLoading`** is what makes a class library emit the
`.runtimeconfig.json` that hostfxr initialisation needs.

**The host has no Source dependency**, so it is testable standalone. It cannot
call `Msg()`, so it takes a logger which the game shim installs *before*
acquiring the runtime, so a failure during startup is visible rather than silent.
Errors log at every verbosity. hostfxr status codes are decoded by name.

**Workstation GC, and zero allocation on per-frame paths.** GC pauses are frame
hitches.

---

## 3. The interop contract

**Two function-pointer tables exchanged once at init.** No COM interop, no
automatic marshalling. One hostfxr lookup resolves the bootstrap entry; from then
on everything is raw function pointers. Blittable types only, UTF-8 `byte*` for
strings.

**Every native-callable body is wrapped in try/catch.** A managed exception
unwinding into Source's C++ frames corrupts the process. `UnhandledException` and
`UnobservedTaskException` handlers cover what the per-call catch cannot: threads
SourceSharp did not start, and swallowed Task faults. PDBs load alongside each
assembly so managed traces carry line numbers into the same console log as native
output.

**Every pointer handed to native belongs to the non-collectible bootstrap
assembly** and dispatches through a single mutable reference to the current side
module, the one exception to the no-statics rule (section 1a). This is the hot-reload rule (native must never hold a pointer into a collectible
context) applied from day one, so a context can be swapped without native seeing
a changed pointer.

**Two engine tables, not one.** The `Engine` static lives in the ABI assembly,
loaded once and shared by both sides, but each DLL exports its own engine API.
With a single static the second side's load overwrote the first; init-time calls
worked because the loads are sequential, but every runtime call resolved against
whichever side loaded last. Both tables are kept and the bootstrap marks the
active side before dispatching. `Engine.ActiveSide` is also how shared code tells
which side it is running on.

### Decision callbacks

**Tri-state, never boolean.** A rule returns "yes", "no" or "use the stock
rule". A plain bool would force C# to have an opinion about every case and
silently override the engine when it had none.

**Enums with pinned numeric values** (`Decision`, `PlayerRelationship`,
`CommandResult`) rather than `bool?` or bare ints whose meaning lived in a
comment. Native is unchanged and still reads an int32. Because the bootstrap
casts straight to int, tests assert the numeric values, not the names: renaming
a member is harmless, renumbering one silently changes what the shell does. Two
invariants hold: every "defer" member is negative, every decided member is
non-negative.

### The second route: a bound vtable, not a slot

**There are two ways for managed code to reach the engine, and the slot table
is not always the right one.** A slot is a function pointer in the exchanged
table, written by a native body in the shim. When that body does nothing but
pass blittable arguments straight through to an engine global, the slot buys
nothing and costs a C++ function, an `Abi.cs` field, an `Engine.cs` wrapper and
a place in the ABI version.

The alternative is to bind the engine interface's vtable directly.
`SourceSharp_AcquireInterface` hands managed the interface pointer **plus a
compiler-derived slot count**, `NativeInterfaceFactory` checks that count once
per acquire, and `tools/vtable-layout` generates
`SourceSharp.Abi/VtableLayouts.g.cs` — one struct per engine interface, field
position equal to slot index — from the SDK headers, so a layout cannot drift
from the C++ it describes without regenerating. Calls go through `Vtable`.

**The slot count is the whole safety argument.** A vtable is an array of
function pointers with no names in it; calling slot 31 of an interface that has
30 is not a type error, it is a jump into whatever follows. Managed code never
sees the count as a number to trust — the factory refuses the acquire when the
native count and the generated layout disagree, which turns an ABI drift into a
startup failure with a name in it instead of a crash three frames later.

**Measured on this branch** (`tools/vtable-layout`'s output and a walk of
`SourceSharp.Abi/`, excluding the tests):

| | |
|---|--:|
| generated layout structs | 33 |
| …bound by a shipped implementation today | 7 |
| `Vtable.*` call sites, in 12 files | 46 |
| slots the policy marks NOT EXPOSED | 71 |
| slots refused outright (varargs, by-value `Vector` returns) | 23 |
| ABI slots still in `Abi.cs` | 207 |

`tools/vtable-policy.json` carries the refusals with a sentence each. Two of
the seven bound layouts — `ShimFrameVtbl` and `ShimLevelVtbl` — are not engine
interfaces at all: they are the shim's own `IShim*` objects, bound the same
way, which is how a new managed→native surface is added without a new free
function at the ABI boundary.

**Which route a new reach takes.** Bind the vtable when the call is a
passthrough to an engine global with blittable arguments — a file read, an
overlay, a sound, a model fact. Keep a slot when the body has to do something
native first. Two areas are **not** candidates on purpose:

- **entity operations** (`Entity*`, `Standalone*`, `Player*`), because
  handle→shell resolution is the slot table's single-writer design and a bound
  call would bypass it;
- **VPhysics**, because `plan_entity.md` §3.8 keeps physics objects behind
  ids-with-serials deliberately, and binding `IPhysicsObject` would need an
  id→pointer lookup per call and give that up.

The slot table therefore ends as entity and physics operations plus the refused
proxies, and does not shrink to nothing.

**The fail direction is chosen per decision.** Admission fails open: a managed
fault must never make a server unjoinable. Damage returns the *incoming* value on
fault so a bug cannot amplify it, and `CanTakeDamage` fails open because refusing
would make players unkillable. Rules may only refuse damage or override a delay;
they can never make damage land that the stock rules would have blocked.

**Sentinels are chosen so they cannot collide with data.** `GetPlayerTeam`
returns -1 for "not a player" because 0 is `TEAM_UNASSIGNED`, a real team;
conflating them would make friendly-fire logic block a non-player attacker.
`GetLocalPlayerHealth` returns -1 for "no local player" because 0 means dead,
and the menu and the gap between levels both have no player. `GivePlayerAmmo`
returns the amount actually added rather than a bool, because the engine clamps
to the carry limit and a caller deciding whether a pickup was consumed needs the
difference.

**A shared clock.** `Engine.CurrentTime` exposes `curtime` rather than
accumulating frame deltas, because the shell computes respawn deadlines from
`curtime` and the two sides of a rule must agree on when a delay has elapsed.

**`HudColor` is its own type, not a tuple.** The ABI takes eight loose ints, and
a wrong argument order produces a plausible-looking wrong colour, not an error.

**Copying is fine off the hot path.** `CreateMove` copies the user command in
and out of C# once per command. That is the opposite of entity state, where
copying would be fatal (section 4).

---

## 4. Entity state: share memory, do not marshal

### The finding that decides the design

`CBaseEdict::StateChanged(offset)` records the byte offset of each changed field
so the engine re-encodes only those props, up to 19 per entity per tick before
degrading to a full re-encode. The offset-less overload is documented for props
that "used some kind of pointer dereference" and sets `FL_FULL_EDICT_CHANGED`.

So a send proxy that calls back into C# to fetch data marks the whole edict
dirty on every change, the engine re-encodes every prop, and the managed proxy is
invoked for each of them. The callback count goes *up*. The proxy-callback design
is self-defeating, not because interop is slow but because it defeats the
engine's delta compression.

**Therefore entity data lives in a native blob at real offsets.** The send path
stays fully native and offset-based with zero managed calls during networking.
How C# reads and writes the blob is off the hot path: game logic touches a field
a handful of times per tick, while the engine walks every prop.

### No `unsafe` in game code

The single unsafe operation is confined to the engine wrapper; callers see a
bounds-checked `Span<byte>`. Because `Span<T>` is a ref struct, the compiler
also prevents the pointer escaping the frame that fetched it. A Roslyn source
generator can later remove the boilerplate without changing the memory layout or
the runtime contract; C++ accessor shims remain a fallback.

### Tables and classes are built at runtime

`SendPropInt` and friends are plain functions returning a `SendProp` by value,
`SendTable` has an ordinary constructor, and `ServerClass`/`ClientClass`
constructors link themselves into the global lists that the engine only walks
later. Registering a class is therefore constructing one before the engine
snapshots the list. The runtime comes up inside `IGameSystem::InitAllSystems`,
which is inside `DLLInit`, comfortably before either list is read. The runtime
entity carries no `DECLARE_SERVERCLASS`/`DECLARE_CLIENTCLASS`; those macros
generate exactly the static plumbing being replaced.

**The engine pairs a `ClientClass` to a `ServerClass` by name.** A
`#define CSourceSharpPlayer C_SourceSharpPlayer` under `CLIENT_DLL` broke the
pairing because `IMPLEMENT_CLIENTCLASS_DT` stringizes its argument, and the two
sides registered under different names. The runtime pair registers under one
shared constant so that mismatch is unrepresentable. Diffing the two class lists
is the first thing to run against any networking problem.

**The two sides resolve different blob base offsets, and that is correct.** The
server and client shells have different base-class sizes. The wire carries prop
values in table order, not offsets; what must match is the sequence, type and
width of the props, which all come from the shared metadata. `RecvProp` carries
no bit count: the client learns each width from the server during the handshake,
which is why a width disagreement shows up as a bitstream desync rather than a
clean error. That is what the layout fingerprint below exists to catch.

**`bits=0` on float props is correct.** `dt_send` sets `SPROP_NOSCALE` for any
32-bit float and deliberately zeroes the bit count; the width is implied.

**The blob is 16-byte aligned.** A raw byte array has alignment 1, and the first
placement put every prop on an odd address. x86-64 tolerates that, so it worked,
but it was slow and not portable.

### Variable-length blob

`CBaseEntity::operator new` is a plain size-based `PvAllocEntPrivateData`, and
`C_BaseEntity` has the same overridable operator. The shell therefore allocates
`BlobOffset() + blobBytes` and indexes past the end of the class. A fixed member
was used first to prove the generator and access path, then replaced because it
capped what C# could declare and wasted the difference on every entity.

- **Trailing allocation rather than a flexible array member.** A class with
  virtual bases and a flexible array member is a GCC extension with awkward
  corners; "allocate more and index past the end" is plain C++ with the same
  layout. `BlobOffset()` rounds `sizeof` up to 16 so props stay aligned however
  the class happens to end.
- **The size is derived from the described fields, not passed from C#.**
  Computing it from the same data the props come from means it cannot drift.
- **The size refuses to change once fixed.** Growing it after entities exist
  would leave those objects shorter than the props address: silent corruption
  rather than an error.
- **The blob offset is exported at runtime, never hardcoded.** It differs per
  shell type and must be added to every C#-computed field offset.

**A small family of shells, not one.** The datatable system chains to the base
class's table, and rendering, animation and physics come from the real C++ base.
A C# entity that wants animation must genuinely be a `CBaseAnimating`. Wanting a
new base later is one recompile.

**Virtual pre-routing is the real determinant of whether C++ stops being
touched.** Adding a field is free, but the first virtual C# needs that was not
routed is a recompile. Routes carry a "does C# override this?" bit so unoverridden
virtuals skip the managed transition entirely.

### Handles, never pointers

**Hold a handle, resolve on use.** The blob is engine-owned memory freed on
entity destruction, so any long-lived managed wrapper around the pointer is a
use-after-free waiting to happen, from safe C#. Source's own `CBaseHandle` is
used (a `uint32` packing index and serial) rather than a parallel handle table:
the engine already maintains the serials and the lookup. A stale handle fails
the serial compare and yields `default`, never a read of freed memory. Slot
reuse is the case the whole design exists to prevent, and it is the single most
important handle test.

- **Resolve per callback, not per field.** The same safety costs one lookup
  instead of N: resolve at the managed entry point into a frame-scoped ref
  struct. Safe because `UTIL_Remove` only marks for deletion; memory survives to
  end of frame. `UTIL_RemoveImmediate` is not exposed to C# at all, which
  eliminates the hazard class cheaply.
- **Rebuild handles with `UnsafeFromIndex`, not `Init()`.** `ENT_ENTRY_MASK`
  is derived from the serial bit count, not the entry width, so decomposing by
  hand produced handles that never resolved. "Unsafe" only means the packed value
  is not validated; the serial check in `Instance()` still happens.
- **Handles survive hot reload trivially** because they are plain integers. A
  managed memory wrapper would have been a context-owned object pinning the
  context alive.
- **Entity iteration is handle-based**, following Source's start-after idiom,
  because a walk can span frames and callers may spawn or remove entities
  mid-iteration; a removed entity must fail to resolve rather than dangle.

**The client-side entity registry is keyed by engine index**, which is only safe
because the shell destructor notifies the registry. Without that, index reuse
would hand an old managed instance to a new entity. An unknown index is not an
error, because the engine can destroy an entity the client never saw enter the
PVS. `Clear` copies before iterating because `OnDestroyed` is managed code that
may touch the registry.

### Layout determinism

Client and server generate layouts independently from the same metadata with
nothing enforcing agreement: no shared header, no compile-time link.

- **Order comes from `MetadataToken`**, which the compiler assigns in source
  declaration order and fixes in the compiled image. Reflection order is not
  guaranteed and would be a latent desync that survives testing until a runtime
  or compiler change. Reordering the source is the intended and only way to
  change the wire format; an explicit `Order` property was removed for this
  reason.
- **An unsupported field type throws rather than being skipped.** A silently
  missing field would desync the two sides in a way that is very hard to trace
  back.
- **Alignment is natural alignment, not field size.** A `Vector` is three
  4-byte floats and aligns to 4, not 12. The first generator rounded to a
  multiple of the size and the unit test *encoded* the bug by asserting
  `offset % size == 0`. Only dumping the real built table against known widths
  showed it. Lesson: structural invariants ("offsets increase") pass on wrong
  values.
- **A layout fingerprint is exchanged at join** over the user-message channel,
  hashing each field's name, type, offset and size plus the total and the owning
  type. A mismatch warns rather than kills: the state is untrustworthy either
  way, but naming both fingerprints is far more useful than a terminated
  process. The tests are written around what must *break* it: reordering two
  fields, retyping `int` to `float` (same size, so a size check misses it), and
  renaming.
- **C# owns the decode.** Native hands over the blob pointer and length; C#
  knows what the bytes mean because it wrote the layout. Reading the whole state
  as one sequential struct is only valid while the generated layout agrees with
  .NET's, so tests assert every field against `Marshal.OffsetOf` rather than
  trusting two alignment rule sets to coincide.

---

## 5. Shells are mechanism; C# is policy

The remaining base classes were measured before deciding their fate:
`CBasePlayer` alone is 9,700 lines and 217 virtuals, and with `CMultiplayRules`,
`CPrediction`, `CInput` and `ClientModeShared` the total is around 19,000 lines,
most of it engine plumbing (prediction, viewport, input sampling, entity
bookkeeping) rather than game logic. Replacing them would buy standalone-ness
without moving much decision-making into C#. So the base classes stay as
mechanism and the policy comes out: the shell asks C# first, applies the answer,
and falls back to the stock rule when C# declines. A gap in policy degrades to
Source's behaviour rather than to none.

**Input.** `IInput` has 52 virtuals, nearly all plumbing. The decision that
matters is what ends up in the `CUserCmd` each tick, so the shell subclasses
`CInput`, lets the base build the command, and hands it to C# to modify.

**Game rules must be a shared, networked class** registered via
`REGISTER_GAMERULES_CLASS` with a proxy carrying send/recv tables. A bare `new`
links fine and misbehaves at runtime.

**The round loop.** `Think` runs every server frame, so it early-returns when
the limit is off or the round is over and allocates nothing on the common path.
The base runs first and C# looks afterwards, so intermission is entered from a
consistent point rather than mid-update. Ending the round goes through
`EndMultiplayerGame` so the base does its own bookkeeping. The round-over flag
is latched *before* the call because it can re-enter `Think` on the same frame,
and ending twice skips the map change. The round is reset at `LevelInit` because
the rules object outlives a map change; otherwise the second map's round could
never end, a bug that would be very hard to attribute.

**A rule that only pretended to be one.** The first `CanTakeDamage` had three
branches that all returned "use stock rule": dead code shaped like logic. Writing
it properly required knowing the players' teams, which is what forced
`GetPlayerTeam` into the ABI.

**The loadout belongs to the game rules, not the player class.** Which loadout
you get is the mode's decision. Ammo came before weapons because `GiveAmmo`
silently does nothing for an unregistered type; a weapon handed out first looks
like it works and then does not fire. `GetAmmoDef` must not lazy-init behind a
"done" flag: the engine can reach it from weapon-script parsing before the
runtime is up, and the flag would latch an empty table forever. The table starts
empty and valid, and C# fills it at side load.

**Items are a managed description plus one generic shell entity** wearing a
model, so adding an item kind is a C# change with no native work. They are placed
on the map's spawn points because the map's own item entities are classes this
mod does not define, and it means a map needs no special authoring. Placement
runs on the first tick, not at `LevelInit`, because the map's entities do not
exist yet when `LevelInit` runs. A collected pickup is dropped from the live map
before it is removed so a second touch in the same frame finds nothing, and a
full-health player does not consume it, as with every Source health item.

**Combat without a weapon entity first.** A real `CBaseCombatWeapon` drags in
viewmodels, ammo pools, animation events and prediction, none of which is needed
for players to kill each other. The shell supplies two primitives, trace along
the aim and apply damage; range, damage, rate of fire and edge detection are C#.

- Damage goes through `TakeDamage` rather than writing health, because the
  gamerules hooks, the death sequence and `player_death` must run; the
  scoreboard depends on that event.
- The trace uses `MASK_SHOT` and skips the shooter, as the stock weapon code
  does. Anything else lets players shoot themselves or through cover.
- Buttons are read *after* the base `PlayerRunCommand`, so the position and
  view angles are the ones that command produced.
- The cooldown is per player, keyed by handle; a global one would let any
  player's shot gate everyone else, which only shows up with more than one
  person on the server. A zero interval is clamped so a config mistake cannot
  fire every tick. Button bits are pinned by test against `in_buttons.h`.

**Weapons: one class, many names.** Source loads `scripts/weapon_<classname>.txt`
from the entity's own classname, so a single `CBaseCombatWeapon` subclass
registered under several names picks up a different script per instance. The
mod already ships those scripts, which makes them usable without porting a class
each. `PrimaryAttack` deliberately does not chain to the base, which runs its own
hitscan, ammo decrement and effects and would double up on the C# attack path.
`CTakeDamageInfo` gets force and position: a player only reads the amount, but a
physics prop feeds both into VPhysics and breakable geometry picks a break point
from the position. The direction is normalised defensively because attacker and
victim can share a point, and a normalised zero vector yields NaNs that propagate
into the solver.

**Firing effects.** The stock tracer refuses shots under 256 units and draws a
random segment rather than the whole shot; deliberate in HL2, wrong for a mod
where a close shot showing nothing reads as a broken gun. Precaching happens at
`LevelInitPreEntity`: a late precache returns a valid index that the client
cannot resolve because the model string table has already been sent, with no
error anywhere. Temp entities can be dropped silently, so the beam is a real
networked `CBeam` as `func_tank` uses. The beam starts offset forward, right and
down from the shoot position because in first person that position is the
camera, and a beam viewed along its own axis degenerates into a flat quad across
the screen; the offset is dropped under 64 units or the beam would start beyond
what it hit.

---

## 6. Declared in C#, discovered by attribute

ConCommands, ConVars, user messages, game events, entity layouts and ammo types
all follow one pattern: declare with an attribute, discover at side load,
register with the engine. Shared rules:

- **Native owns the engine object for the process lifetime; C# owns only the
  handler.** `UnregisterConCommands` is all-or-nothing per DLL, so objects are
  never created and destroyed per reload; the handler is rebound behind a stable
  native object.
- **Registration is idempotent by name.** Native lists survive a managed reload,
  so re-running registration would append duplicates and shift every index.
- **Bad declarations are reported, never silently ignored.**

**ConVars.** The value is cached managed-side and refreshed by the change
callback, because ConVars are read per tick. `ConVar<T>` parses once in the
callback rather than on every read. The registry scans only the module's own
assembly: a ConVar declared in the shared assembly would load into both contexts
and register from both DLLs, which Source rejects as a parent cvar conflict. The
same trap is why the native verbosity ConVar is named per side. A ConVar the
engine refuses stays bound to its declared default rather than disappearing;
otherwise the policy reading it silently falls back to its own null-coalesce
value, a different number from the declared default and invisible from outside.
This is also what makes rules testable with no engine.

**User messages.** A message travels as an index into the registered list, so
order is protocol and comes from `MetadataToken` for the same reason as entity
layouts. Both sides register even though only the client receives: the server
*sends*, and `UserMessageBegin` with an unregistered name is a fatal `Error()`,
which killed the game on the first spawn (`CBasePlayer::Spawn` sends `Fade`).
Only the client hooks. The payload is copied out of the engine's `bf_read`
before crossing the ABI so managed code holds a pointer valid only for that call,
and every read is bounds-checked and returns a default past the end, because the
bytes come off the network and a malformed message must not fault the runtime.
`pfnUserMsgHook` carries no index, so a class template with an int parameter
generates one distinct function per message; a recursive filler instantiates
them because the SDK builds as C++11.

**Game events** deliberately differ from user messages: order carries no meaning
because the engine dispatches by name; a malformed class is skipped rather than
fatal because it costs only its own behaviour; and several classes may claim one
event, with one throwing not stopping the others. Each class decodes the raw
event once into a record and raises an event subscribers can attach to. The
first version made these static classes with static events, which is what
produced the double-subscription problem below; under the no-statics rule
(section 1a) the event classes and their subscriptions are owned by the side
module and rebuilt with it. The reader is a ref
struct wrapping the native `IGameEvent`, valid only for the callback, so the
compiler refuses to let it be stored or captured, which is exactly the mistake
that becomes a use-after-free. Subscribers work only with data they can keep.

A class declared in shared code fires on both sides: the server sees the
authoritative event and the client its networked copy. Subscribers with side
effects gate on `Engine.ActiveSide`, or every kill would score twice.
`Subscribe()` guards against running twice because a reload re-runs module init
and a static event would otherwise accumulate a second subscription.

**Why `CEventLog` was replaced.** Registering a listener is what forces the
engine to create an event's descriptor. An inert game-log stub listened for
nothing, so `player_connect` fired with a null descriptor and the engine
segfaulted reading `userid`. Managed subscriptions during side load (inside
`DLLInit`) meet the same requirement; `GameLogSystem()` still returns a non-null
object because the engine requires one.

---

## 7. Hot reload

**Blobs survive reload for free.** Entity state lives in engine-owned native
memory, so when a context unloads the C# objects die but every blob survives and
the new assembly re-wraps the same pointers. Corollary rule: anything that must
survive reload belongs in the blob; plain managed fields are lost, and that is
asserted rather than accidental.

**The class list and table layout are a client/server contract fixed at level
load.** `m_ClassID` is engine-managed and instance baselines live in a networked
string table populated at level start. Hence three tiers: method-body updates
via `MetadataUpdater` (the default inner loop, no unload risk); context reload
for new or changed non-networked types, which connected clients never notice; and
layout changes, which need a map restart and must be detected and refused, never
silently applied.

**What makes context reload actually work.** Collectible contexts unload only
when nothing references them, and this is where .NET hot reload usually fails
silently: the context leaks and stale code keeps running.

- Native never caches managed function pointers; every call goes through one
  indirection rebound atomically (section 3).
- Native owns ConCommands and ConVars permanently (section 6).
- The unload is asserted with a `WeakReference` after a forced collect, and the
  check needs the unloading code in a separate non-inlined frame: with the
  context still a live local, the first version reported a false leak. Anyone
  writing this check naively gets a false positive.
- The client's prediction history is flushed on reload, since it is keyed to a
  datamap that may have changed.

---

## 8. HUD and VGUI

**Immediate mode on one canvas.** The stock health, crosshair and damage
elements are three `CHudElement` subclasses of roughly 950 lines whose structure
exists only because of retained-mode drawing. The shell is a single panel with no
state and no layout; once a frame it hands C# the screen size and C# issues draw
calls. The draw entry points are gated on a flag set around the paint callback,
because drawing is only legal inside VGUI's paint pass and a stray draw from a
managed timer would corrupt VGUI state without announcing itself.

**Rectangles and text, not textures.** The mod has no content of its own to load
a material from. The pickup-history element crashed on null `CHudTexture`
pointers for exactly this reason, so the crosshair is four rectangles and the
weapon list is text.

**The damage indicator is a vignette, not directional.** The stock one points at
the attacker using data this mod's `Damage` message does not carry; pointing
somewhere arbitrary would look like a feature and be a lie.

**A `HudLayout.res` entry is required**, sized with `f0` so the canvas covers
any resolution. The layout pass runs after `ApplySchemeSettings`, so bounds set
in code are not reliable.

**The root panel is sized in `PerformLayout`, not at creation.** The bare
`vgui::Panel` created for the root was never sized, so every client panel was
clipped to VGUI's default 64x24 box in the corner. Two diagnostics reported the
canvas as full-screen and both were true and useless: a panel's own size says
nothing about what its parent clips it to. The screen size is also not final at
creation and changes on a resolution change. The parent-chain dump is kept at
verbosity 2 because it is what would otherwise be rediscovered.

**`HIDEHUD_PLAYERDEAD`, not `HIDEHUD_MISCSTATUS`.** The latter means trains,
pickup history and death notices, and would hide a health readout for unrelated
reasons.

**`weapon_selection.cpp` is excluded from the build rather than satisfied.**
`client_base` registers `invnext`, `invprev` and the slot commands at static
init, unconditionally, and each dereferences a weapon-selection singleton that
lives in HL2 code this mod does not compile. Supplying a subclass failed twice: a
non-`vgui::Panel` element is skipped by `CHud::Init` (the "Non-vgui hud element"
line was in the log the whole time), and a Panel subclass was constructed and
then destroyed, leaving the singleton dangling. VPC supports removing a file from
an inherited project, which deletes the crashing commands; SourceSharp registers
its own, which switch immediately since there is no selection UI. Returning
`NULL` from `GetHudWeaponSelection()` is safe because its only base caller
null-checks it.

**Weapon names go into a reused buffer** rather than a fresh string per weapon
per frame, because this runs 60+ times a second. A weapon with no clip reports
-1 and the readout shows reserve only; "0" and "-1" would both be lies about a
crowbar.

---

## 9. Engine facts the shells must respect

- **Static-init safety.** `RegisterUserMessages` runs from a static constructor
  during `dlopen`, before ConVars are registered; reading one there segfaults
  inside the dynamic loader. Verbosity is held in a constant-initialised int
  updated by a change callback, with `fputs` until tier0 is ready. Early traces
  therefore go to stdout, never `console.log`.
- **Defining a global is not exporting an interface.** The engine asks for
  `IPrediction` and `IGameMovement` by name; defining the global satisfies the
  linker and the engine still dies. Both need `EXPOSE_SINGLE_INTERFACE_GLOBALVAR`.
  Interface parity with HL2MP was established by diffing the actual compiled
  source lists of the two builds, not by grepping source folders.
- **`ClientModeShared::Init` dereferences its viewport unchecked**, so the shell
  must create one.
- **`IInput` is ambiguous once VGUI is in scope** (`using namespace vgui`
  injects `vgui::IInput`), so `input` is defined in a translation unit that never
  includes VGUI.
- **`CreateWeaponInfo` and `g_pPredictionSystems` are already in `client_base`
  but not `server_base`**, so the shared shell guards them with `#ifndef CLIENT_DLL`.
- **Do not re-register spawn points.** `subs.cpp` already does. `InstallFactory`
  only asserts on a duplicate, compiled out in release, and inserts anyway,
  leaving two factories for one key: a latent heap bug where an entity created
  by one is destroyed through the other. Only `player` needs registering.
- **`LevelInit_ParseAllEntities` is a no-op in every shipping mod**;
  `MapEntity_ParseAllEntities` has already run.
- **A player class needs a local-player-exclusive table with at least one prop**,
  as every shipping mod has.
- **A mod directory needs real content**: `steam.inf`, `resource/`, `scripts/`,
  `shaders/` and `maps/` with at least one map. GameUI's create-server page
  crashes on an empty map list, which bites when mounted HL2:DM content is absent.
- **Command-line ConVars are applied after `DLLInit`**, so `+cvar 1` on the
  command line cannot switch on anything needed at init; a dump-on-demand command
  is needed instead.
- **Per-frame hooks and touch handlers log nothing at default verbosity.** A
  touch fires every tick while the player stands in the trigger, and a pickup on
  a spawn point means the player starts the round inside one.
- **Debug info is 87% of the binaries.** Release builds split it with a debuglink
  so gdb still follows; the build never stripped and the stock script did not
  actually strip either.

---

## 10. Testing

Most failure modes in this architecture are silent: lost delta compression, a
context that never unloads, a stale handle that resolves to the wrong entity.
None crash immediately and all are expensive later. Four levels: pure managed,
a fake-engine harness that implements only the engine calls the shim uses (the
highest-leverage investment, built before the game code), in-engine, and
two-process. The three tests that matter most are serial-number reuse,
`StateChanged` with the correct offset and no `FL_FULL_EDICT_CHANGED`, and
context unload plus every routed call reaching the *new* assembly.

Principles the work settled on:

- **Test what must break, not what must stay.** The fingerprint tests reorder,
  retype and rename; a stability-only test proves nothing.
- **Assert numeric values for anything that crosses the ABI as an int.**
- **Structural invariants pass on wrong values.** "Offsets are increasing" held
  for a broken layout; check against known widths or a dumped real table.
- **Assert what the harness can observe.** It links the host but not the game
  DLL, so it cannot see native idempotency; it asserts that every load emits the
  same ordered list instead.
- **Check the test data before the code.** A reader test failed because its
  payload was a byte short.
- **Self-test ConVars for primitives whose natural trigger is interactive**,
  such as ending a round or firing one shot on spawn. A call that reaches deep
  into the engine should not ship unexercised because it needs a mouse click.
- **Graceful shutdown to exercise destroy paths.** Killing on a timeout skips
  entity teardown, so destroy notifications look broken when they are not.
- **Look up counts by name, not magic numbers**, so a test does not break every
  time a command is added.

---

## 11. Debugging: why the tooling exists

- **Instrument instead of guessing.** Post-mortem analysis of the stripped engine
  was near-useless. Stub logging, entity-creation logging, an exported-interface
  dump, a class-list diff and a `CreateInterface` miss hook turned "what is the
  engine asking for that we do not answer?" into a printout. The miss hook is
  installed from a file-scope static constructor, because installing it from
  game-system init misses the early probes, and the absence of misses had once
  been used as evidence.
- **Judge interface misses by timing and a control case, not by name.** The
  optional tool probes appear in one block and execution continues for hundreds
  of lines; a genuinely missing interface stops immediately with a dialog.
  Grepping a stock binary for the interface string proves nothing, since the
  string appears whether a module requests or provides it.
- **Read the fault registers, not just the backtrace.** With no symbols, `rdi`
  and `rsi` named the exact event and field in one step after a dozen rounds of
  unsymbolised stacks.
- **Symbolise cores in the container** (`src/symboliseCore`). Cores were
  unreadable because gdb ran against host libraries rather than the Steam
  runtime's, not because the binaries were stripped: `nm` was not installed on
  the host and a not-found command was taken as evidence. Every crash after the
  tool existed was named in one step; every crash before it was chased by
  hypothesis.
- **`stdout` and `console.log` show different apparent crash sites**, and the
  console one misleads: `-condebug` closes the log early, so a crash near the
  boundary writes nowhere.
- **Stale processes poison runs.** `timeout` does not always kill the game, and
  a leftover instance holding the log looks like a regression.
- **Rule out the gate before blaming the game.** The live gate can pick a stale
  pid and report a healthy run as a crash; `coredumpctl` is the discriminator,
  since a real fault leaves a core.
- **One fault was closed by decision, not by finding.** An intermittent startup
  SIGSEGV at an unresolved address (roughly 1 in 20 runs, August 2026) was never
  reproduced and is recorded as cause unknown. It is not explained by the later
  worktree startup failures: the dates and the signatures differ. The repro
  recipe is kept: run under gdb with the solib search path at the Steam runtime,
  expect about 20 runs, and check `stdout` rather than `console.log`.

## 12. Decisions the entity port changed

`plan_entity.md` §3 settled four questions this document had already answered,
and in three of them it answered differently. They are recorded here rather
than left as a contradiction between two files, because the earlier text reads
as current and a reader has no way to know it was superseded.

### The base classes did not stay: there is a family of shells

§5 above says the remaining SDK base classes "stay as mechanism and the policy
comes out", on the measurement that `CBasePlayer` alone is 9,700 lines and 217
virtuals and that replacing them "would buy standalone-ness without moving much
decision-making into C#".

**That is no longer what the port does.** There is a family of standalone
shells that derive from no SDK entity — `CStandaloneEntity`,
`CStandalonePlayer` and `CStandaloneActivatorScope` on the server, with the
client's own — and the SDK hierarchy is being removed rather than subclassed.

The measurement in §5 was not wrong; the goal changed. "Standalone-ness" turned
out to be the point rather than a side effect, because the three gates in
`plan_entity.md` §5 — no base entity, no entity list, no entity casts — cannot
be met by a shell that inherits from the thing being counted. A subclass of
`CBaseEntity` is a `CBaseEntity` to `nm` and to the source scan alike.

**And it cost more than the line count predicted, in a way §5 could not have
seen.** Taking one class off its base has now produced **five** silent losses:
things the base did FOR the entity rather than TO it, invisible to the
compiler, to `make check`, and to every live gate —

- `Spawn()` itself, whose only caller was the base, taking the shared activity
  list, the event list and eight `g_sModelIndex*` sprite indexes with it;
- `ent_create`'s drop-to-floor, whose helper returns false under `SOURCESHARP`
  and was then deleted as dead — correct about the call graph, and the
  behaviour went with it;
- shadow re-projection, because `InvalidatePhysicsRecursive` is `CBaseEntity`'s;
- every managed entity's sound emitted `GENDER_NONE`, because
  `C_BaseEntity::Instance` answers NULL for one;
- `SetVisible(false)` writing `ModelIndex = 0`, so an invisible managed brush
  reaches the client with no shape at all.

So the standing rule for anything that comes off a base: **enumerate what the
base was doing for it — registration, ordering, defaults forced by index, and
any method whose only caller was the base — and pin each one.** A green build
is not evidence here, and neither is a green gate.

### Touch edges are computed in managed code; the overlap report stays native

Native reports overlapping pairs once per frame and managed derives the enter,
stay and leave edges from consecutive reports. The split is deliberate: the
overlap set is an engine fact and cheap to hand over once per frame, while
*which* of the three edges a pair is on is game policy and belongs in C#.

### Interpolation is managed, but `interpolatedvar.cpp` did not leave the build

`plan_entity.md` §3.9 states that "`CInterpolatedVar` and `interpolatedvar.cpp`
are not compiled". **The first half happened and the second did not, and §3.9
is wrong as written.**

The algorithm is managed and the two `CInterpolatedVar` instantiations per
client entity are gone. But the file cannot leave the client build:
`c_baseentity.cpp` builds `C_BaseEntity`'s VarMapping out of `CInterpolatedVar`,
so the TU comes out only when that class does. A gtest pins the reason so the
next reader does not retry it as an oversight.

This is the general shape the port kept meeting and it is worth stating plainly:
**a file's compilation is a property of the link graph, not of whether anyone
still calls into it.** The Phase 4 exit is the same finding at scale — steps
4a–4f and the Phase 2 and Phase 3 exits are one connected component, proven by
relinking `server.so` without them and reading 163 undefined symbols from 39
objects, so they come out together or not at all.

### Still open: a table per class

§4 says "nothing in this plan needs a table per class", and that is still true
of what is built. It is recorded as open rather than closed: the entity port
did not need one either, but it did not establish that one will never be wanted.
