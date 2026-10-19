# ABI slots retired, awaiting the final renumbering pass

Every row here names a slot whose native body or only native caller is gone but
whose FIELD is still in `src/sourcesharp/host/sourcesharp.h` and
`src/sourcesharp/managed/SourceSharp.Abi/Abi.cs`, marked in both with

    SS_RETIRED <SlotName> lane=<lane> <one-line reason>

Removing a slot from the middle renumbers every slot after it, and
`AbiParityTests` pins names IN ORDER -- so a lane that removes its own slot
corrupts every other lane's work silently. Removals are batched into one
serialised pass at the end of the phase, which renumbers once and reruns the
parity test once. The marker is the contract: a slot that is deleted early, or
left unmarked, breaks that pass.

**Append only.** Never rewrite another lane's row.

**WHICH ROWS ARE STILL PENDING IS NOT DECIDED BY THIS FILE.** A slot is pending
if and only if its `SS_RETIRED` marker is still in
`src/sourcesharp/host/sourcesharp.h`; the rows below accumulate and are never
deleted, so completed passes leave their rows in place with a "the pass ran"
section underneath. Read the worklist out of the source, not out of this table:

    grep -n 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h \
                         src/sourcesharp/managed/SourceSharp.Abi/Abi.cs

and require the two counts to agree before starting. At the Phase 2 merge that
read **9 and 9** against **15** rows here: three were retired by lane `1e-abi`
and are recorded as done below, three more are that pass's own history rows, and
the nine live markers were Phase 2d's.

**Those nine are done.** Lane `2e-abi` removed them at ABI **247 -> 248**; the
section at the foot of this file records the pass. `grep -c SS_RETIRED` over
either file now answers **0** while the rows stay where they are, and that zero
-- not any count taken over this table -- is what says the worklist is empty.

| slot | body / only caller was in | lane | commit | reason |
|---|---|---|---|---|
| `BenchmarkNoop` | `sourcesharp/game/shared/shell_benchmark.cpp` | 1c-sv | see `git log --grep 'cs_bench_boundary'` | The boundary-cost measurement `cs_bench_boundary` was its only native caller, and had 0 consumers (0 tools, 0 managed files, 0 tests, 0 docs). The managed thunk in `Entry.cs` and the `CrossingsAuditTests` facts over it are left in place. |
| `BenchmarkTypical` | `sourcesharp/game/shared/shell_benchmark.cpp` | 1c-sv | see `git log --grep 'cs_bench_boundary'` | Same caller, same measurement: the "wearing what every real callback wears" arm of the same A/B. Same 0-consumer proof. |
| `OnViewModelReport` | `sourcesharp/game/client/shell_standalone_client.cpp`, inside `CON_COMMAND( cs_cl_viewmodel_report )` | 1c-cl | `d792b3ae2` | A `SourceSharpManagedApi` slot -- NATIVE asking MANAGED -- so what it lost is its native CALLER rather than its body. It exists so the managed arsenal can print the weapon it holds beside the model the native view pass actually loaded; a stale viewmodel is exactly their disagreement. `cs_cl_viewmodel_report` was its only caller anywhere (0 tools, 0 other native files, 0 tests), and it is one of the client bring-up instruments Phase 1c deletes. The managed implementation (`ClientModule.OnViewModelReport`) and the `Entry.cs` thunk are left in place and are now unreached. |

## The pass ran — ABI 244 -> 245, lane `1e-abi`

Every row above is DONE. The three fields are out of
`src/sourcesharp/host/sourcesharp.h` and
`src/sourcesharp/managed/SourceSharp.Abi/Abi.cs`, the struct tails after them
renumbered once, and `SOURCESHARP_ABI_VERSION` / `AbiVersion.Current` went from
**244 to 245** in that one commit rather than once per slot.

The rows are kept rather than deleted, because the ledger's value after the pass
is the same as before it: it is where a reader finds out why a slot that used to
exist does not, without reading three merge commits. `grep -c SS_RETIRED` over
either file now answers **0**, and that — not the absence of rows — is what says
the worklist is empty.

WHAT WENT WITH EACH SLOT. All three are `SourceSharpManagedApi` slots (native
asking managed), so there was no `sourcesharp_gamesystem.cpp` assignment and no
`Engine.cs` wrapper to remove. What there was instead:

| slot | field | assignment | ABI thunk | CrossingOp |
|---|---|---|---|---|
| `BenchmarkNoop` | removed from both files | `Entry.cs` server + client arms | removed | was exempt from counting; exemption removed |
| `BenchmarkTypical` | removed from both files | `Entry.cs` server + client arms | removed | member removed, enum renumbered |
| `OnViewModelReport` | removed from both files | `Entry.cs` client arm | removed | member removed, enum renumbered |

`CrossingOp` had to be renumbered with the ABI: `CrossingsTests.OpValuesAreContiguousFromZero`
pins the values at `0..OpCount-1` with no gaps, because `Crossings` indexes its
name table by the enum value directly. 188 members became 186.

TWO FACTS WERE RETIRED WITH THE CODE THEY PINNED, in the same commit:
`CrossingsAuditTests.BenchmarkNoopIsNotCounted` and `.BenchmarkTypicalIsCounted`.
Both read `Entry.cs` for a thunk by name through `Thunks().Single(...)`, so with
the thunks gone they could only ever throw — and a fact that can only fail is no
more a claim about the build than one that can only pass. The ASYMMETRY they
pinned (the floor is uncounted, the typical one is counted) is not lost: it is
written into `CrossingOp`'s own remarks in `SourceSharp.Abi/Crossings.cs`, for
whatever prices a crossing next.

WHAT WAS DELIBERATELY LEFT, and it is a boundary rather than an oversight. The
managed GAME-MODULE surface behind `OnViewModelReport` stays: `ISideModule.OnViewModelReport`
(a defaulted interface member), `ClientModule.OnViewModelReport`, and
`ClientArsenal.DescribeViewModel` under it. Removing an ABI slot removes the ABI
mirror — the field, the assignment, the thunk — and that chain is one level past
it: it is managed code callable from managed code, and a managed
`cl_viewmodel_report` DevCommand re-reaches all of it in one line, which is the
direction this port is going in anyway. It is unreached today and is recorded
here so that is a known state and not a discovery.

## Phase 2d, lane `2-leafmath` — seven slots, one crossing

Seven `SourceSharpEngineApi` slots whose native
bodies are deleted on BOTH sides (14 bodies across
`sourcesharp/game/server/shell_scripted.cpp` and
`sourcesharp/game/client/shell_standalone_client.cpp`). Their fields stay, each
marked `SS_RETIRED` in both headers, and each assignment in
`sourcesharp_gamesystem.cpp` is `NULL` with the marker beside it.
`Engine.cs`'s wrappers stay too, per the protocol, and are listed in
`EngineAreaTests.RetiredAwaitingTheAbiPass` so that the suite's
"every Engine member is forwarded" fact says what is happening rather than
going quiet — the pass that removes the slots deletes that list with them.

WHAT REPLACES ALL SEVEN is one appended slot, `ModelDescriptor`, which crosses
a whole model once and is cached per level by
`SourceSharp.Abi/ModelDescriptors.cs`. Each of the seven answered ONE SCALAR
PER CALL about a file that cannot change while the level is loaded; two of
them answered once per EVENT and once per ATTACHMENT.

| slot | bodies were in | lane | commit | reason |
|---|---|---|---|---|
| `EntityLookupSequence` | `shell_scripted.cpp`, `shell_standalone_client.cpp` | 2-leafmath | see `git log --grep 'ModelDescriptor'` | One `ModelDescriptor` lookup answers it. AND THE TWO BODIES DISAGREED: the client's called `::LookupSequence`, which on a label miss falls through to an activity lookup resolved by `SelectWeightedSequence` — a weighted RANDOM pick — while the server's called `BoneLibrary_LookupSequence`, which refuses that fallback and answers -1. A sequence index is PREDICTED state, so the same `ACT_` name resolving to a random index on one side and nothing on the other is a misprediction per lookup. The managed body answers -1 on both sides. |
| `EntityLookupSequenceByActivity` | both | 2-leafmath | same | The first-match rule `sequence_by_activity.h` held is one managed implementation over the cached descriptor now, which is what makes it the same on both sides by construction rather than by review. |
| `EntitySequenceDuration` | both | 2-leafmath | same | A fixed number per sequence — `Studio_Duration` over the model's own default pose parameters — so it rides in the descriptor. |
| `EntitySequenceEventCount` | both | 2-leafmath | same | The event table rides in the descriptor. This cost one call per model check. |
| `EntitySequenceEvent` | both | 2-leafmath | same | The same table. This cost one call PER EVENT, so reading one weapon's fire animation was `1 + 1 + (E+1)` crossings. |
| `StandaloneAttachmentCount` | both | 2-leafmath | same | The attachment names ride in the descriptor. |
| `StandaloneAttachmentName` | both | 2-leafmath | same | The same names. This cost one call PER ATTACHMENT. |

ONE SLOT ON THE LIST WAS NOT RETIRED, AND THE MEASUREMENT DISAGREES WITH THE
RULING. `plan_shim.md` §2d names `StandaloneBoneCount` among the eight. It
answers TWO numbers, and only one of them is a fact of the model: the total
bone count is (and moved to the descriptor), but `pOutCached` is how many bones
the BONE CACHE is holding for that entity at that instant — zero until
something has asked it to pose. That cannot be cached per model without
becoming a different number, and it is the half that tells "never drawn" from
"drawn flat". The slot stays, bound on both sides, and `EngineBinding.BoneCount`
now takes its total from the descriptor and its cached count from the slot.

ONE SLOT WAS APPENDED, which needs no pass: `ModelDescriptor`, at the end of
`SourceSharpEngineApi` on both sides. `SOURCESHARP_ABI_VERSION` and
`AbiVersion.Current` are NOT bumped — the integrator bumps once.

## Phase 2d, lane `2-leafmath` — the two anim-list feeds

Two `SourceSharpManagedApi` slots — NATIVE asking
MANAGED — so what each lost is its native CALLER and its managed thunk rather
than a body in a shim file. Their fields stay, marked `SS_RETIRED` in both
headers; `Entry.cs` no longer binds either on either side.

| slot | only native caller was in | lane | commit | reason |
|---|---|---|---|---|
| `AnimListActivityNames` | `sourcesharp/game/shared/shell_animlists.cpp` | 2-leafmath | see the `activity-ordinal` commit | It filled a NATIVE registry of 1,947 activity names, once per level per side, so that `game/shared/animation.cpp` could write an ORDINAL from it into mdl memory for a native reader to compare against an enum. Nothing compares activity ordinals now: the sequence lookups are one managed rule over a `ModelDescriptor`, which carries activity NAMES. The registry, `animation.cpp`, `anim_name_registry.h`, `studio.cpp`'s activity-to-sequence mapping and the 2,048-line `ActivityCatalog.cs` went with it. MEASURED on a live map load: 49,385 bytes per side per level, against 8,505 bytes for a whole level's descriptors. |
| `AnimListEventNames` | `sourcesharp/game/shared/shell_animlists.cpp` | 2-leafmath | same | The event half of the same registry. `precache_model.cpp` compares event NAMES now (`anim_event_names.h`), which needs no registry and no write into the model. `EventCatalog.cs` STAYS — managed code reads it itself, and the five native rows are checked against it in both directions. |

`CrossingOp` lost its two members and renumbered, which is NOT the ABI pass's
job and is the distinction worth writing down: `CrossingOp` is managed-only,
its values index a name table derived from the enum itself, and nothing native
reads them. `CrossingsAuditTests` states the enum/thunk equality in BOTH
directions precisely because an op left behind by a deleted thunk compiles
fine and reports a permanent zero. The ABI SLOTS are what a lane must not
renumber, and those are marked and left in place.

RUNNING TOTAL FOR THIS LANE: nine slots marked, one appended
(`ModelDescriptor`), no version bump.

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h               -> 9
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs   -> 9

## The pass ran — ABI 247 -> 248, lane `2e-abi`

Every row of both Phase 2d sections above is DONE. Nine fields are out of
`src/sourcesharp/host/sourcesharp.h` and
`src/sourcesharp/managed/SourceSharp.Abi/Abi.cs`, the struct tails after each
renumbered once, and `SOURCESHARP_ABI_VERSION` / `AbiVersion.Current` went from
**247 to 248** in that one commit rather than once per slot.

`grep -c SS_RETIRED` over either file now answers **0**. The rows above are kept
rather than deleted, for the reason the `1e-abi` section gives: the ledger's
value after a pass is the same as before it.

THE WORKLIST WAS READ OUT OF THE SOURCE, not out of this table, and the two
marker counts were required to agree first:

    grep -c SS_RETIRED src/sourcesharp/host/sourcesharp.h              -> 9
    grep -c SS_RETIRED src/sourcesharp/managed/SourceSharp.Abi/Abi.cs  -> 9

WHAT WENT WITH EACH SLOT. The seven `SourceSharpEngineApi` slots each had a
field on both sides, an assignment on BOTH arms of
`sourcesharp_gamesystem.cpp` (`= NULL`, left by lane 2-leafmath) and an
`Engine.cs` wrapper. The two `SourceSharpManagedApi` slots had a field and
nothing else: lane 2-leafmath had already deleted both `Entry.cs` thunks and
both `CrossingOp` members, so no `CrossingOp` renumbering was needed this pass.

| slot | struct | field | assignment | `Engine.cs` wrapper |
|---|---|---|---|---|
| `EntityLookupSequence` | engine | removed from both | both arms | removed |
| `EntityLookupSequenceByActivity` | engine | removed from both | both arms | removed |
| `EntitySequenceDuration` | engine | removed from both | both arms | removed |
| `EntitySequenceEventCount` | engine | removed from both | both arms | removed |
| `EntitySequenceEvent` | engine | removed from both | both arms | removed |
| `StandaloneAttachmentCount` | engine | removed from both | both arms | removed |
| `StandaloneAttachmentName` | engine | removed from both | both arms | removed |
| `AnimListActivityNames` | managed | removed from both | none already | none |
| `AnimListEventNames` | managed | removed from both | none already | none |

`StandaloneBoneCount` WAS CHECKED AND NOT REMOVED, which is recorded here
because the plan predicted eight. It carries no `SS_RETIRED` marker, and the
reason is in its signature: `int32_t (*StandaloneBoneCount)( uint32_t hEntity,
int32_t *pOutCached )`. The total is a fact of the model and moved into the
descriptor; `pOutCached` is how many bones the bone cache holds for THAT ENTITY
at that instant, which is not a property of the model file at all.

THE AREA FACADE STAYED. `IEngineEntityRender.LookupSequence`,
`LookupSequenceByActivity`, `SequenceDuration`, `AttachmentCount`,
`AttachmentName`, `SequenceEventCount` and `SequenceEvent` are all still there
and are the supported way to ask: `EngineBinding` already answered all seven out
of `ModelDescriptors.For(entity)` rather than out of the slot. Only their
`<inheritdoc cref="Engine.X"/>` had to change, because a cref to a deleted
wrapper is CS1574 and the managed build is required to be warning-free.

ONE LIVE CALLER WAS FOUND AND REPAIRED, which the pass was not expecting.
`ScriptedEntity.PlaySequence` — what `PropDynamic` calls to play the animation a
map names — still went through `Engine.EntityLookupSequence`. That slot has been
`NULL` since lane 2-leafmath, so the wrapper's null guard answered -1 and
`PlaySequence` returned false for every prop, silently, at every call. It now
resolves through `ModelDescriptors.For(Handle).LookupSequence(name)`, which is
the same rule both sides run.

TWO FACTS WERE RETIRED WITH THE CODE THEY PINNED, in the same commit:

- `EngineAreaTests.RetiredAwaitingTheAbiPass`, the exemption list the Phase 2d
  section above says "the pass that removes the slots deletes with them". With
  the wrappers gone there is nothing to exempt, and
  `EveryEngineMemberIsOnAnArea` now covers every remaining member with no
  waiver.
- `AnimClusterLedger.TheSlotsTheyFilledAreMarkedAndNulledRatherThanRemoved`,
  which required five slots to be assigned `= NULL` and to carry an
  `SS_RETIRED` marker. That was the protocol for a slot WAITING for this pass;
  with the fields, the markers and the assignments all gone, every assertion it
  makes can only fail.

`SideAuditTests.DualWired` also lost the seven engine slots: `DiffPartition`
reports a declared slot the fill block does not wire as `not wired at all`, so
leaving them would have reddened the audit.

## Phase 3, lane `3c-entityshell` — the three weapon-state slots nothing called

| slot | body / only caller was in | lane | commit | reason |
|---|---|---|---|---|
| `GetPlayerWeaponAmmo` | `sourcesharp/game/server/shell_entityapi.cpp:664-672` | 3c-entityshell | see the log entry "the three weapon-state slots" | Emptied out with the CBasePlayer inventory in 13c and answered zero afterwards. **0 managed callers through either name it has**: not `Engine.GetPlayerWeaponAmmo` and not `IEnginePlayers.GetWeaponAmmo`, the facade method managed code would actually name. The only caller of the facade was `ArsenalPlayers`' own pass-through, which nothing called in turn. Measured by resolving the facade name out of `EngineBinding.cs` (the `... => Engine.<Slot>` lines) and grepping for BOTH names outside `SourceSharp.Abi` — a grep for the slot name alone sees only the wrapper layer, which is how lane S2 recorded 23 of these as unreached when only 3 are. |
| `SetPlayerWeaponClip` | `sourcesharp/game/server/shell_entityapi.cpp:674-676` | 3c-entityshell | see the log entry "the three weapon-state slots" | Same measurement, same 0 through `IEnginePlayers.SetWeaponClip`. |
| `TakePlayerAmmo` | `sourcesharp/game/server/shell_entityapi.cpp:678-681` | 3c-entityshell | see the log entry "the three weapon-state slots" | Same measurement, same 0 through `IEnginePlayers.TakeAmmo`. Note that `IEngineWeapons.TakeAmmo` is a DIFFERENT method of the same name over `Engine.WeaponTakeAmmo`, is live, and is untouched — a grep for `TakeAmmo` alone conflates the two. |

**What went with the bodies, and what deliberately did not.** The three
`IEnginePlayers` facade methods were removed from the interface, from
`EngineBinding.EnginePlayers`, from `RecordingEngine` and from `ArsenalPlayers`,
because a facade method forwarding to a NULL slot is the lane 2e-abi shape
exactly: it compiles, it runs, and it answers nothing. The `Engine.cs` wrappers
and the `Abi.cs` fields stay, per the protocol.

**These three are not the disposition of the other 25 stubs in that file.**
Twenty of those have a live managed rule calling them and receiving nothing, and
a caller that wants an answer is a requirement rather than a finished job. They
are listed with the rule each breaks in `docs/entity-shell.md` section 2 and
pinned by `sourcesharp/test/entity_shell_inert_slots_test.cpp`, which fails if
that set changes in either direction.
## Phase 3 §5 3a, lane `3b-enginebind` — four IShimFrame VTABLE slots

**These four are NOT `SourceSharpEngineApi` or
`SourceSharpManagedApi` fields, and the difference decides which pass removes
them.** They are virtual methods of `IShimFrame`
(`src/sourcesharp/game/server/ishim_frame.h`), reached by managed code through
the forwarder table `SSFwd_ShimFrame` (`ShimFrameFwd` in
`SourceSharp.Abi/SharedForwarders.g.cs`, generated), whose slots follow the
header's order.

So the worklist grep this file's preamble prescribes —

    grep -c 'SS'_'RETIRED' src/sourcesharp/host/sourcesharp.h
    grep -c 'SS'_'RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs

— **does not and must not see these four.** Neither file is touched by this
lane; both still answer 0. The markers for these four are in

    src/sourcesharp/game/server/ishim_frame.h              -> 4

and the pass that acts on them is the deliberate `SourceSharpFrame001` ->
`SourceSharpFrame002` version-string bump, not the ABI renumbering pass. The
managed side is generated from that same header by `tools/engine-forwarders`
(the forwarder table and `ShimFrameFwd`), so there is no second file to mark:
the table follows the header by construction and the tool diffs it on every
run.

WHY THEY CANNOT SIMPLY BE DELETED, which is the same argument as the ABI's and
one notch sharper. Acquisition holds the managed slot count N against the count
native takes from the compiler, M, and requires `N <= M`. That check is
sufficient for APPENDS and for nothing else. Removing a method from the middle
of the class renumbers every method after it, the span check still passes, and
the wrong function is called — returning whatever the callee left in the return
register, with the symptom arriving somewhere else, later.

| slot | body was in | lane | reason |
|---|---|---|---|
| `PhysicsRunThinkFunctions` | `sourcesharp/game/server/shell_framesteps.cpp` (now `shell_frame.cpp`) | 3b-enginebind | Stock's step 8. `Physics_RunThinkFunctions` is `physics_main.cpp`'s and that file left the vpc; the `SimThink` list it walks is defined only there and in `entitylist.cpp`, both out; and under the entity alias there is no native entity object that could be on the list. The thinks are managed — `ThinkScheduler` drains through `EntityDirectory.RunFrame` from `StandaloneHierarchy`'s hook inside `ServerSystemsPostThink`, the very next step. The one non-think effect, the deferred-delete cleanup, already ran a step earlier from managed code. Body was a refusal stub that warned and did nothing. |
| `ServiceEventQueue` | same | 3b-enginebind | Stock's step 9. The native queue is defined in `cbase.cpp`, which left the vpc, and so did both producers — the `ent_fire` console command in `baseentity.cpp` and the output-firing path in `entityoutput.cpp`. No armed server TU adds an event, so there is nothing to service. The entity I/O queue armed is the managed `EntityIoQueue`, and `EntityIoRoutes.Service` runs at stock's own position in `GameFrame`. Body was a refusal stub. |
| `FireNetworkPropertyEvents` | same | 3b-enginebind | Stock's step 12. **THE PLAN NAMED THREE FRAME SLOTS AND THIS IS A FOURTH**, found by re-measuring: its body was already literally empty. `g_NetworkPropertyEventMgr` went with `game/server/timedeventmgr.cpp` at Phase 1d — `CServerNetworkProperty::SetUpdateInterval` was the only thing that could register an entity with that manager and it had no caller in any compiled TU — and 1c-sv deleted `ShellGameDll_RecordStep` with the step-order ring. Each side of that merge had removed the other's only statement. |
| `DeliverToSdkEntities` | same | 3b-enginebind | Not a frame step: the leaf the managed I/O queue asked when its directory answered nothing. It existed to reach four stock classes the managed `EntityDirectory` does not hold — the dissolver, the two ragdoll kinds and the physics solver — and every one of their defining translation units is absent from the armed build, so the hierarchy it walked has no members. The two passes it called left with `cbase.cpp` and have no other armed caller. The managed directory is the whole population, and `EntityIoRoutes` already warns once per distinct target and input that nothing took it — the better message, because it names the target. |

**THE MANAGED CALLERS WENT IN THE SAME COMMIT**, which is the Phase 3 addendum's
rule and the defect that produced it: lane 2e-abi found `ScriptedEntity.PlaySequence`
— every dynamic prop's sequence call — still routed through a slot nulled in
Phase 2, returning false for every prop on every map, silently, with the live
gate passing over it twice. The four callers removed here:

| slot | caller |
|---|---|
| `PhysicsRunThinkFunctions` | `SourceSharp.Server/ServerGameDll.cs`, `GameFrame` |
| `ServiceEventQueue` | `SourceSharp.Server/ServerGameDll.cs`, `GameFrame` |
| `FireNetworkPropertyEvents` | `SourceSharp.Server/ServerGameDll.cs`, `GameFrame` |
| `DeliverToSdkEntities` | `SourceSharp.Server/ServerModule.cs`, the `EntityIoRoutes.BindSdkDelivery` binding |

`EntityIoRoutes.Deliver` already answers 0 when nothing is bound
(`SdkDelivery is null ? 0 : ...`, `SourceSharp.Abi/EntityIo.cs`), which is the
same value the retired body returned — so unbinding is observably identical and
costs one crossing fewer per unhandled input.

SIX MANAGED FACTS WERE REPOINTED, NOT DELETED, and one of them could not fail.
Five pinned the frame's step order using a retired slot as a LANDMARK; the
claims survive, so they now name `Frame.ServerSystemsPostThink` — the step the
thinks and the I/O pass actually sit against. The sixth,
`ServerGameDllTests.TheRulesEndOfFrameComesAfterTheEventQueueAndTheClientData`,
was written as `steps.IndexOf(a) < steps.IndexOf(b)` with no found-check:
`IndexOf` answers -1 for an absent step and -1 is less than everything, so the
moment `ServiceEventQueue` stopped being called the assertion passed VACUOUSLY.
It went green on a tree where it measured nothing. Every index it reads is now
required to be found first.

ONE ASSERTION IS GONE RATHER THAN MOVED, and it is named here because it was a
real claim: `TheEntityThinksRunBetweenTheTwoSystemPasses` required
`ServerSystemsPreThink < PhysicsRunThinkFunctions < ServerSystemsPostThink` —
the entity thinks bracketed by the two passes, which is the whole reason stock
names them PreEntityThink and PostEntityThink. With step 8 retired the thinks'
position is no longer observable from the frame's step ORDER at all: they run
INSIDE `ServerSystemsPostThink`, and a recording engine sees one call. The test
is now `NothingRunsBetweenTheTwoSystemPasses`, which pins what is still
observable and is the property a "cleverer" frame would break — a step appearing
between the passes is a call AROUND them, which is the shape that hoists the
managed frame ahead of `CSoundEnt`, `CRagdollLRURetirement` and
`CEntityListSystem`.

RUNNING TOTAL FOR THIS LANE: four IShimFrame vtable slots marked in
`ishim_frame.h`, zero ABI fields marked, zero slots removed, no version bump of
either kind.

## Phase 3 §5 3a, lane `3b-enginebind` — two IShimLevel VTABLE slots

Same mechanism as the IShimFrame section above, and
the same warning applies: these are virtual methods of `IShimLevel`
(`src/sourcesharp/game/server/ishim_level.h`), NOT `SourceSharpEngineApi` or
`SourceSharpManagedApi` fields. The ABI worklist grep over `host/sourcesharp.h`
and `Abi.cs` does not and must not see them; both still answer 0. The markers
are in

    src/sourcesharp/game/server/ishim_level.h              -> 2

and the pass that acts on them is the `SourceSharpLevel001` ->
`SourceSharpLevel002` version-string bump.

| slot | body was in | lane | reason |
|---|---|---|---|
| `ResetDeleteList` | `sourcesharp/game/server/shell_levelsteps.cpp` (now `shell_level.cpp`) | 3b-enginebind | "How many SDK entities were still queued for deletion when the level started". Armed there is no SDK delete list to hold one, so the native body was a literal `return 0;` and could not be anything else. Asking cost a crossing per level to be told a constant. The parallel hierarchy's own count is `EntityDirectory.PendingRemovalCount`, which is managed and free to read. |
| `ActivateAllEntities` | same | 3b-enginebind | Stock's `ServerActivate` walk, gameinterface.cpp:1233-1250. Every line of it reaches the hierarchy `server_csharp.vpc` evicts, so the body has been empty since that eviction. The parallel hierarchy's half is `_spawner.ActivateAll()` in `ServerGameDll.ServerActivate`, in managed memory with no crossing, immediately where the native call was — so armed that is the whole of ServerActivate's activation pass, and the ORDER is unchanged because the two hierarchies were already activated in the order their entities were made. |

**THE MANAGED CALLERS WENT IN THE SAME COMMIT**, both in
`SourceSharp.Server/ServerGameDll.cs`'s `ServerActivate`.

**THE PLAN NAMED FOUR AND TWO OF THEM DID NOT RETIRE.** Recorded here because
"the plan said four, the tree has two" is otherwise a gap a later reader has to
re-derive:

- **`PrecachePointTemplates` — body emptied, slot NOT retired.** Its native body
  did nothing (`TemplateEntities.cpp` left the vpc; a managed template entity is
  precached by the managed spawner) and the loop is gone. But **a retired slot is
  not called**, and this call's POSITION is what two orderings are observed
  through: low violence before the precache (`gameinterface.cpp:1049` then
  `:1053`), and the templates after the entities they read. The first was
  inverted in this port from the day the method was written, under a doc comment
  saying "the sequence is stock's" and a `[Fact]` that pinned the inversion;
  `tools/gate5` found it on its first real run, and no census, live gate or
  managed test could see it. Retiring the slot deletes the only marker either
  ordering has. One no-op crossing per LEVEL is the right price.
- **`EntitiesCleanupDeleteList` — untouched.** Its body is empty for the same
  reason as the retired pair, but it is a flat ABI slot rather than an
  `IShimLevel` method, so its marker belongs in `host/sourcesharp.h` and
  `Abi.cs` — the serialised tail this lane does not write into.

ONE MANAGED FACT RETIRED WITH THE CODE IT PINNED, and its name promised more
than its body delivered: `ServerGameDllTests.ANonEmptyDeleteQueueOnLevelStartIsReported`
set `Level.ResetDeleteList` to answer **3** and then asserted only that the slot
was CALLED ONCE — never that the warning fired, never what it said. The report in
its name was not tested, and the number it fed in was one native cannot answer.

ONE MANAGED FACT REPOINTED: `ServerActivateRunsStocksStepsInStocksOrder`'s
expected sequence loses its first two entries, with both departures named in the
test so the shortened list is not read as a port that forgot two steps.

WHAT IS NOT RESTORED, AND IS FLAGGED RATHER THAN DONE: the non-empty-delete-queue
warning. The question is still worth asking of the hierarchy that HAS a queue,
but `ServerGameDll` holds `IEngineEntities` and not the directory, so reaching
`EntityDirectory.PendingRemovalCount` means a constructor change — a larger edit
than this lane is making, and one that belongs to whoever owns that constructor.

RUNNING TOTAL FOR THIS LANE: four IShimFrame slots and two IShimLevel slots
marked, zero ABI fields marked, zero slots removed, no version bump of either
kind.

    grep -c 'SS_RETIRED' src/sourcesharp/game/server/ishim_frame.h          -> 4
    grep -c 'SS_RETIRED' src/sourcesharp/game/server/ishim_level.h          -> 2
    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h                 -> 0
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs     -> 0
## Phase 3, lane `3e-sharedsys` — the piecewise movement arithmetic and the three policy hooks

Thirteen `SourceSharpManagedApi` slots — NATIVE
asking MANAGED — so what each lost is its native CALLER, not a body in a shim
file. Their fields stay, marked in both headers; `Entry.cs` still binds them,
and the pass that removes the fields removes the binds with them.

THE MEASUREMENT, taken on `eaf6ae7d3` with the tree current
(`ninja -t cleandead`, then `ninja -n` reporting `no work to do`):

    tools/shim-census --mtime-policy off who-imports \
        src/sourcesharp/game/shared/shell_movement.cpp

    server  2 object files import it   shell_standalone_player.o (g_pGameMovement),
                                       sourcesharp_gamesystem.o (MoveTrace, MoveEffect,
                                       MoveSurface, MovePointContents,
                                       AuditMovementConVar, RegisterMovementParams)
    client  2 object files import it   shell_prediction.o (MoveBeginClientCommand,
                                       MoveEndClientCommand), sourcesharp_gamesystem.o (same six)

Not one of the thirteen forwarders below is among the imported symbols on either
side. Their one call site was `game/shared/gamemovement.cpp`, which is in
neither vpc (`grep -n gamemovement src/game/{server,client}/*_csharp.vpc` — no
match) and has been since `plan_entity.md` step 8f-remove.

| slot | only native caller was in | lane | commit | reason |
|---|---|---|---|---|
| `OnPlayerJump` | `sourcesharp/game/shared/shell_movement.cpp` (`SourceSharp_JumpScale`) | 3e-sharedsys | see the `piecewise movement forwarders` commit | The mod's jump rule, asked natively once per command while jump was held. The managed loop asks the same rule for itself through `GameMovement.JumpRule`, which `Entry.cs` wires per side out of `IServerModule.GetJumpScale` / `IClientModule.GetJumpScale` — the same `Movement.JumpScale` body. Two routes to one rule, and only the managed one has a caller. |
| `OnPlayerFriction` | same (`SourceSharp_FrictionScale`) | 3e-sharedsys | same | The friction rule. `GameMovement.FrictionRule` is the managed route. |
| `OnPlayerMaxSpeed` | same (`SourceSharp_MaxSpeedScale`) | 3e-sharedsys | same | The max-speed rule. `GameMovement.MaxSpeedRule` is the managed route. |
| `MoveClipVelocity` | same (`SourceSharp_MoveClipVelocity`) | 3e-sharedsys | same | `docs/plan-audit.md` E1's PIECEWISE half: C# arithmetic slotted into stock's C++ `PlayerMove` chain one method at a time, each forwarder answering `false` so the stock body behind it could run. Every call site was in `gamemovement.cpp`. The arithmetic itself did not move — `SourceSharp.Abi`'s `MoveMath` still does it, reached from the managed loop, which is the whole-loop crossing `MoveProcessMovement` and is NOT retired. |
| `MoveDecayPunchAngle` | same | 3e-sharedsys | same | as above |
| `MoveCheckVelocity` | same | 3e-sharedsys | same | as above |
| `MoveGravity` | same | 3e-sharedsys | same | as above |
| `MoveAccelerate` | same | 3e-sharedsys | same | as above |
| `MoveAirAccelerate` | same | 3e-sharedsys | same | as above |
| `MoveFriction` | same | 3e-sharedsys | same | as above |
| `MoveSplineFraction` | same | 3e-sharedsys | same | as above |
| `MoveCalcRoll` | same | 3e-sharedsys | same | as above |
| `MoveReduceTimers` | same | 3e-sharedsys | same | as above |

WHAT THE REMOVING PASS ALSO HAS TO TOUCH, so it is not discovered late:

- `SourceSharp.Bootstrap/Entry.cs` binds all thirteen on both sides
  (`pOut->OnPlayerJump = &ServerOnPlayerJump;` and its neighbours) and defines
  the thunks behind them.
- `managed_runtime_server_test.cpp` carries a NAMED LIST of the
  `SourceSharpManagedApi` tail; ten of the thirteen are in it
  (`"MoveClipVelocity"` … `"MoveReduceTimers"`) with a long comment block that
  already says they have no native caller. The list and the comment go with the
  fields.
- `SourceSharpJumpRequest` becomes unreferenced when `OnPlayerJump` goes; it is
  a struct in `sourcesharp.h`, not a slot, so it is not marked here.
- `MoveMath`'s C# bodies STAY. They are what the managed loop runs.

RUNNING TOTAL FOR THIS LANE: thirteen slots marked, none appended, no version
bump.

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h               -> 13
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs   -> 13

## Phase 3, lane `3e-sharedsys` — two rules slots whose native caller left with IGameRules' dead members

Two more `SourceSharpManagedApi` slots. Each was
asked by exactly one native method, and both methods were among the eighteen
`IGameRules` members no compiled translation unit calls.

A VIRTUAL CALL EMITS NO SYMBOL, so `tools/shim-census` cannot answer this one
and the measurement is a source scan over the SOURCES of every object on the
ninja link line, taken twice:

- `.lane3e/igamerules_callers.py` — `<rules pointer>->Slot(` for each of the
  known receiver spellings. 20 members with no site.
- `.lane3e/igamerules_second.py` — any `Slot(` at all, over-counting on purpose
  so it cannot MISS. It found five sites the first had not; three were real
  (`CreateCustomNetworkStringTables` and `Damage_GetNoPhysicsForce`, both called
  from inside `shell_gamerules.cpp` itself, which the first scan skipped) and
  two were unrelated names (`engine->ClientCommand`, `pApi->IsTeamplay`).

So the list is 18 and not 20, and the difference is the whole reason the second
scan exists.

AND THE COMPILER AGREED: both DLLs link with the eighteen members removed from
the interface, the class and the file.

| slot | only native caller was in | lane | commit | reason |
|---|---|---|---|---|
| `GetFallDamage` | `sourcesharp/game/shared/shell_gamerules.cpp` (`CSourceSharpGameRules::FlPlayerFallDamage`) | 3e-sharedsys | see the `igamerules` commit | `FlPlayerFallDamage` is an `IGameRules` member with no caller: stock's one call site is in `game/server/player.cpp`, which is in neither vpc. Fall damage is `MoveMath.FallDamage` inside the managed movement loop now, handed the fall velocity that armed the call — see `shell_gamemovement.h`'s `SourceSharpMovement_PendingFallDamage` for why recomputing it from the helper's own member was wrong. |
| `IsTeamplay` | same (`CSourceSharpGameRules::IsTeamplay`) | 3e-sharedsys | same | `IsTeamplay` is an `IGameRules` member with no caller. Managed code that wants the answer has `TeamplayConVar` in `GameRules.cs` and reads it directly; the slot was the native half of a question nothing native asks. |

NOT A SLOT, BUT REMOVED IN THE SAME COMMIT AND RECORDED HERE SO THE PASS DOES
NOT LOOK FOR IT: `SourceSharpGameRulesPolicy::bAllowDamage`. It is a field of a
parameter struct rather than a slot table, so it carries no marker and needed no
pass — both sides are compiled from one declaration in one tree. Its one reader
was `CSourceSharpGameRules::AllowDamage`, which went with the other seventeen.
`GameRulesPolicyTests.EveryPolicyFieldIsReadByTheMethodItBelongsTo` is what
forced the issue: its second loop asserts that no field exists that no method
reads, and it went red the moment the method went. The struct is one field now
and was seventeen.

RUNNING TOTAL FOR THIS LANE: fifteen slots marked, none appended, no version
bump.

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h               -> 15
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs   -> 15

## Phase 3, lane `3e-sharedsys` — the join handshake's payload

One `SourceSharpEngineApi` slot — MANAGED asking
NATIVE — so what it lost is its native BODY. The field stays, marked in both
headers, and the assignment is `= NULL` rather than removed.

| slot | body was in | lane | commit | reason |
|---|---|---|---|---|
| `SendLayoutCheck` | `sourcesharp/game/shared/shell_usermessages.cpp` | 3e-sharedsys | see the `join handshake` commit | It wrote the join handshake's three fingerprints as three `WRITE_LONG`s into a `"SourceSharpLayout"` message, and the comment over it said exactly what was wrong with that: "the one place client and server hand-agree on a byte layout". `ServerModule.SendLayoutCheck` fills a `UserMessageWriter` with three `WriteInt`s and hands the bytes to `SendUserMessage`, which is the general path every other managed message already takes. Byte-identical: `WriteInt` is `BitConverter.TryWriteBytes` over four bytes, `WRITE_LONG` is a 32-bit little-endian write into the same bit stream, and the general path's `WRITE_BITS( pData, nBytes * 8 )` copies them unchanged. The RECEIVE side never changed. |

`Engine.SendLayoutCheck` THROWS rather than returning quietly, which is a
departure from "leave the wrapper" worth writing down. A wrapper over a nulled
slot whose guard returns silently is how lane `2e-abi` found every dynamic
prop's sequence call answering false for months, unseen by both suites and by
the live gate. There is no caller left — the only one moved to the managed
writer in the same commit — so the throw is unreachable, which is exactly the
condition under which making it loud costs nothing.

RUNNING TOTAL FOR THIS LANE: sixteen slots marked, none appended, no version
bump.

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h               -> 16
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs   -> 16

---

## Lane `4c-render`, plan_shim.md §6 (the client's leaf
## renderers, HUD and soundscape)

| slot | body was in | reason |
|------|-------------|--------|
| `GetLocalWeaponCount`   | `sourcesharp/game/client/shell_hud.cpp` | read the retired C_BasePlayer inventory; answered 0 |
| `GetLocalWeaponName`    | `sourcesharp/game/client/shell_hud.cpp` | as above; answered "" |
| `GetLocalActiveWeapon`  | `sourcesharp/game/client/shell_hud.cpp` | as above; answered -1 |
| `GetLocalWeaponClip`    | `sourcesharp/game/client/shell_hud.cpp` | as above; answered -1 |
| `GetLocalWeaponReserve` | `sourcesharp/game/client/shell_hud.cpp` | as above; answered -1 |

Commit: the third of lane `4c-render`'s series on its branch.

**THE RULINGS TABLE CALLED THESE FIVE DEAD AND THEY WERE NOT.**
`plan_shim_rulings.tsv` row C3 / `shell_hud.cpp` reads *"5 DEAD:
GetLocalWeaponCount/Name/ActiveWeapon/Clip/Reserve ... only reachers are
Engine.cs:3345-3353 + EngineBinding.cs:671 wrappers with no caller"*. That is
the shape the phase-4 addendum predicted: **managed does not name the slot, it
names the facade.** `SourceSharp.Client/Hud.cs:213-246` read
`Screen.LocalWeaponCount`, `.LocalActiveWeapon`, `.LocalWeaponName(i, buf)`,
`.LocalWeaponClip` and `.LocalWeaponReserve` every frame the HUD painted — five
LIVE managed callers receiving nothing, which is a defect ledger and not a
retirement list.

So they were fixed at the managed end FIRST and retired second, in one commit:
`Hud.cs`'s native-fed weapon fallback is deleted (it returned immediately on
`count <= 0` and drew nothing, for every player, since the standalone player
became the local player), the Arsenal feed above it is the only feed, and
`HudAmmoFeedTests.AnEmptyFeedFallsBackToTheNativeAsks` is replaced by
`AnEmptyFeedDrawsNothingAndAsksNothingNative`, which asserts all five members
are asked zero times.

The five assignments in `sourcesharp_gamesystem.cpp` are `NULL`, following the
`GetPlayerWeaponAmmo` / `SetPlayerWeaponClip` / `TakePlayerAmmo` pattern lane
`3c-entityshell` set in the same file. `Engine.cs`'s wrappers null-check their
field and answer 0/-1, which is the same value the deleted bodies returned, so
nothing changes shape for any future caller.

RUNNING TOTAL FOR THIS LANE: five ABI fields marked, zero slots removed, no
version bump.

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h                 -> 8
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs     -> 8

(8 and 8, not 5 and 5: three were already marked by lane `3c-entityshell`.)

---

# THE THIRD PASS RAN: lane `4e-abi`, ABI **248 -> 249**

Twenty-four slots, every one of them carrying a marker in BOTH
`src/sourcesharp/host/sourcesharp.h` and
`src/sourcesharp/managed/SourceSharp.Abi/Abi.cs`. The two counts were asserted
equal before the pass started, and the two NAME LISTS were diffed as well --
counting alone would have passed on a one-sided marker swap, and the first
extraction of those names read the marker token rather than the slot name out of
every line, so the diff agreed trivially. That is the shape of a check that
cannot fail; it was re-taken with

    grep -h 'SS_RETIRED' <file> | sed -E 's@.*SS_RETIRED ([A-Za-z0-9_]+).*@\1@' | sort

on both files, which answers 24 identical names.

## The worklist, and who marked it

**Nine `SourceSharpEngineApi` slots** (managed calls native):

| slot | lane | what it did |
|---|---|---|
| `GetPlayerWeaponAmmo` | 3c-entityshell | read a native combat weapon's clip/reserve |
| `SetPlayerWeaponClip` | 3c-entityshell | wrote the clip the HUD shows |
| `TakePlayerAmmo` | 3c-entityshell | spent rounds from the reserve |
| `GetLocalWeaponCount` | 4c-render | the selection HUD's inventory size |
| `GetLocalWeaponName` | 4c-render | one carried weapon's name |
| `GetLocalActiveWeapon` | 4c-render | index of the weapon in hand |
| `GetLocalWeaponClip` | 4c-render | rounds in the active clip |
| `GetLocalWeaponReserve` | 4c-render | rounds in reserve |
| `SendLayoutCheck` | 3e-sharedsys | the join handshake's two fingerprints |

**Fifteen `SourceSharpManagedApi` slots** (native asks managed), all marked by
lane 3e-sharedsys:

`GetFallDamage`, `IsTeamplay`, `OnPlayerJump`, `OnPlayerFriction`,
`OnPlayerMaxSpeed`, `MoveClipVelocity`, `MoveDecayPunchAngle`,
`MoveCheckVelocity`, `MoveGravity`, `MoveAccelerate`, `MoveAirAccelerate`,
`MoveFriction`, `MoveSplineFraction`, `MoveCalcRoll`, `MoveReduceTimers`.

`MoveProcessMovement` is NOT among them and must not be: it is the one slot of
the eleven with a caller left, and it is how the whole managed movement loop is
entered.

## The 0-caller proof, taken through the FACADE

`tools/shim-facade-audit --self-test` passed first (6 checks), then every one of
the twenty-four was resolved to its `EngineBinding.cs` facade pair and searched
for callers across the whole managed tree:

    python3 -c "...import shim-facade-audit; facade_map(); Managed().callers(...)"

All twenty-four answered **zero direct facade callers and no second-layer
`abi_route`**. Only two declared a facade at all --
`GetLocalWeaponName -> IEngineHud.LocalWeaponName` and
`SendLayoutCheck -> IEngineNetworking.SendLayoutCheck` -- and neither had a
caller. A second, independent pass grepped every slot name across the managed
tree and read each hit: the fifteen ManagedApi slots answer NO-FACADE because
the facade audit only walks `engineApi`, and their real managed surface is the
`pOut->X = &X` binding in `SourceSharp.Bootstrap/Entry.cs`, which is what was
deleted for them.

Native: no compiled translation unit named any of the twenty-four. The only
remaining `IsTeamplay` hits in the tree are stock's own `CGameRules::IsTeamplay`
virtual, which is a different thing entirely.

## What went with each slot

- the field in `host/sourcesharp.h` and in `SourceSharp.Abi/Abi.cs`, with the
  prose block that documented it;
- for the nine EngineApi slots: the `engineApi.X = NULL;` line in
  `sourcesharp_gamesystem.cpp` and the `Engine.cs` wrapper -- including
  `Engine.SendLayoutCheck`'s throwing retirement stub;
- the six facade members those wrappers sat behind, in
  `EngineAreas.Players.cs` (`IEngineHud`), `EngineAreas.Core.cs`
  (`IEngineNetworking`), `EngineBinding.cs` and `RecordingEngine.cs`;
- for the fifteen ManagedApi slots: the `pOut->X = &X` binding in BOTH arms of
  `Entry.LoadSide` where they were two-armed, and the eighteen
  `[UnmanagedCallersOnly]` trampolines behind them;
- the eighteen `CrossingOp` members those trampolines counted, and the enum was
  RENUMBERED 0..166 -- its values index `Crossings.s_Names` and
  `CrossingsAuditTests` states the enum/thunk equality in both directions. That
  renumbering is managed-only and nothing native reads it.

## Tests that moved with the code

- `LayoutHandshakeTests.NoNativeSenderIsLeftToDisagreeWithIt` asserted
  `engineApi.SendLayoutCheck    = NULL;` was in the fill. There is no field to
  null now, so it asserts the fill does not mention the slot at all, and a new
  fact `NeitherHalfOfTheAbiDeclaresTheRetiredSlot` pins the absence of the field
  in both tables -- which is the stronger claim the removal makes available.
- `SideAuditTests`' client-only list lost the five `GetLocal*` names.
- `CoreTests.HudTests` lost the five `Hud.LocalWeapon*` ignores: an ignore for a
  read that cannot happen.
- `managed_runtime_server_test.cpp`'s `kAfterShellBlock` lost the ten `Move*`
  rows and their prose block; the prose was rewritten rather than deleted,
  because the block below it says "the reason the MoveMath block above gives".

## Numbers

    grep -c 'SS_RETIRED' src/sourcesharp/host/sourcesharp.h              -> 0
    grep -c 'SS_RETIRED' src/sourcesharp/managed/SourceSharp.Abi/Abi.cs  -> 0

`SOURCESHARP_ABI_VERSION` and `AbiVersion.Current` were bumped ONCE, 248 -> 249,
in the removal commit. `tools/entrypoint-decls` answers "in sync, 436
prototypes" before and after -- unchanged, because a slot assigned `= NULL`
emits no prototype, so removing it removes nothing from the generated header.

## What this pass deliberately did NOT remove

`IServerModule.GetFallDamage` / `ServerModule.GetFallDamage` and
`IServerModule.IsTeamplay` / `ServerModule.IsTeamplay` / `GameRules.IsTeamplay`
/ `IGameRulesService.IsTeamplay` are still declared and implemented. With the
two ManagedApi slots gone, native cannot reach either of them any more, so they
are managed-internal game logic with no caller outside the managed tree and
their tests (`GameRulesFallDamageTests`, `CoreTests`) pin them directly.

Left standing on purpose: this is an ABI pass, and deleting a rules layer is a
decision about the MOD rather than about the boundary. The same is true of
`Movement.JumpRule` / `FrictionRule` / `MaxSpeedRule`, which are the live
replacements for the three movement slots and are reached from
`MoveProcessMovement` -- those are not orphans at all, they are where the
decisions moved to. A later lane that wants the fall-damage and teamplay rules
gone should take them with their tests and say what now answers those two
questions.

## The one measurement this pass re-took

`tools/shim-census passthrough`, last read as **4 strictly passthrough and 2
near** before the Phase 1 deletions, reads **the same 4 and 2** now.

    bucket  PASSTHROUGH         4
    bucket  NEAR-PASSTHROUGH    2

    PASSTHROUGH       SourceSharp_ActivateOccluder                 shell_standalone_client.cpp:4537
    PASSTHROUGH       SourceSharp_EngineLightStyle                 shell_worldquery.cpp:2062
    PASSTHROUGH       SourceSharp_EntitiesSdkPlayerSkyboxArea      shell_transmitsteps.cpp:81   (EXCLUDED_BY_3G)
    PASSTHROUGH       SourceSharp_GetLocalPlayerIndex              shell_hud.cpp:153
    NEAR-PASSTHROUGH  SourceSharp_ClientMapName                    shell_clientsystems.cpp:218
    NEAR-PASSTHROUGH  SourceSharp_HudLookupBinding                 shell_hud.cpp:455

Taken the only way that is evidence rather than a guess, because the census
refuses on a normal incremental build and neither of its mtime policies answers
the question:

    ninja -f _vpc_/ninja/sdk_csharp_debug.ninja -t cleandead   -> Cleaning... 0 files.
    ninja -f _vpc_/ninja/sdk_csharp_debug.ninja -j8            -> exit 0
    ninja -f _vpc_/ninja/sdk_csharp_debug.ninja -n             -> ninja: no work to do.
    tools/shim-census --mtime-policy off passthrough           -> exit 0

`--mtime-policy off` drops only the mtime heuristic; the census still refuses
when the graph names an object that is not on disk, which is the guard that
catches an unbuilt or half-built tree. Both graphs were generated first --
`vpc_no_protobuf_test` reads the RELEASE one and a missing input reads as a
failing claim, which is exactly how it failed here once before the release graph
existed in this worktree.

**This is a report and not an instruction.** Nothing was done about the six.

## Markers that are NOT in this worklist, and must not be read as leftovers

`grep -c` over the two ABI tables answers **0**, which is what says this
worklist is empty. A grep over the whole tree does NOT answer 0, and the
difference is not a lane having missed something:

- **`src/sourcesharp/game/server/ishim_frame.h` (4) and `ishim_level.h` (2)**
  carry markers from lane `3b-enginebind`: `PhysicsRunThinkFunctions`,
  `ServiceEventQueue`, `FireNetworkPropertyEvents`, `DeliverToSdkEntities`,
  `ResetDeleteList`, `ActivateAllEntities`. These are **pure virtuals on an
  IShim vtable**, not fields of `SourceSharpEngineApi` or `SourceSharpManagedApi`,
  and each says so at its site: "slot kept at its index until the
  SourceSharpFrame002 pass". That is an INTERFACE VERSION bump and a different
  pass from this one. An ABI tail lane must not remove them, and the six will
  not appear in the two-file grep this protocol is written around.
- `shell_scripted.cpp` and `shell_standalone_client.cpp` carry prose reading
  "The SLOT is still in the struct, marked SS_RETIRED in both headers" about
  slots lane `2e-abi` removed at ABI 248. That prose is stale, not a marker; it
  was not touched here because those slots are that pass's subject, not this
  one's. Worth a sweep by whoever next reads those two files.


## plan_engineapi_vtables Phase 0, lane `ea-0` — the unused-slot pass, ABI 251 -> 252

A slot is UNUSED when nothing on its reading side names it: an EngineApi slot
that no managed code outside `Abi.cs` reads through an api value, a
ManagedApi slot that no native code outside `sourcesharp.h` names (comments
stripped). `UnusedAbiSlotTests` computes both sets and requires them to be
empty or marked; at `f157b3c` they were 9 and 28. Each was marked `SS_RETIRED`
(commits `baf9222`, `03bf11c`), the native bodies, fills, thunks, CrossingOp
members and the IServerModule forwards that only a thunk called went
(`143feb0`), and this pass removed the 37 fields and renumbered once;
`FindEntityByName`, unread after the merge of server-novalve's names work,
went in the same pass, making 38.

| slot | struct | why unused |
|---|---|---|
| `FireEffects` | EngineApi | no caller since Combat.Attack left; the stub answered nothing |
| `EntityGetPropData` | EngineApi | prop data is C#'s (PropData.ModelPropData, plan_server_novalve P2.4) |
| `CreateExplosion` | EngineApi | explosions are EnvExplosion.cs / Explosions.Fire |
| `SoundscapeGetIndex` | EngineApi | the soundscape index is C#'s (Soundscapes, P2.4) |
| `HostSteamGameServerReacquire`, `…Clear`, `…RequestGameplayStats` | EngineApi | the Steam game server is C#'s (SteamGameServerFlat, P2.5) |
| `ModelBreakPieceCount`, `ModelBreakPiece` | EngineApi | break pieces are C#'s (BreakModels, break pieces step 5) |
| `FindEntityByName` | EngineApi | names are answered by the managed directory (server-novalve names steps 1-3, 0 native fallbacks on a live gate); removed in the same pass, with `SourceSharp_FindEntityByName` |
| `AllowFlashlight`, `CanRespawn`, `GetRespawnDelay`, `OnGameRulesThink`, `OnPlayerTakeDamage` | ManagedApi | the rules shell that asked them is gone; CanRespawn/GetRespawnDelay stay on IGameRules |
| `OnEntityTouched`, the 13 `OnScripted*` | ManagedApi | entity events are managed (EntityDispatch / class ops) |
| `OnWeaponItemPostFrame`, `OnWeaponPrimaryAttack`, `OnWeaponReload` | ManagedApi | the weapon frame runs in the managed command loop |
| `OnPlayerRunCommand`, `OnStandalonePlayerCommand`, `PlayerCommands` | ManagedApi | named only in comments; PlayerDecodeCommands and PlayerRunCommands replaced them |
| `PredictionDescribeOffset`, `PredictionRecordCompare`, `PredictionTypePredictedFields` | ManagedApi | named only in comments; `SourceSharp_PredictionJudgeFields` no longer exists |

FOUND ON THE WAY, NOT CHANGED: `ServerModule.OnPlayerTakeDamage` held the
armour-absorption rule and `cs_sv_damage_scale`. Native had stopped calling it
before this pass, so neither applies in the game (`SsPlayer.OnDamaged` applies
god/buddha only). `Armour.cs` is kept so restoring the rule is one call.

`grep -c SS_RETIRED` over either header answers **0** after this pass.

## plan_engineapi_vtables C6c (ABI 253)

The 262 EngineApi slots that only the server filled were removed in one pass,
and SourceSharpEngineApi renumbered once. They were not retired: each is a
method of a server shim area. `docs/engineapi-areas.tsv` lists them (`side`
is `server`) with the interface that serves each one. The server has passed
no struct since C6b (`c756cba`), so its fill went too, and the fill is now
client-only. The 190 slots the client fills are unchanged (53 both-sides and
137 client-only).

## plan_engineapi_vtables: CommandLineHasParam, CommandLineGetParamValue

Both slots were retired from the tail of IShimEngineServer, their last two
methods. Removing a vtable's tail renumbers nothing: an older managed side
needs more entries than the area now serves, and ShimAreas.TryAcquire refuses
it by count, so no version bump is needed. The server reads its command line
through ILocalCommandLine (ProcessCommandLine, /proc/self/cmdline), as the
client already did. The bodies left shell_entityapi.cpp; ss_command_line.cpp
stays for its native callers (shell_diagnostics, sound_emitter).

## plan_engineapi_vtables E1: the server runtime's 28 ManagedApi slots

The ServerSystemStart ... ServerSteamGameServerShutdown block (25) and
ServerMapLoadPreEntity, ServerMapUnloadPreEntity and ServerWorldKeyValue: the
managed-built IManagedRuntimeServer serves all 28 (D5, and E1 for the last
three). The server no longer fills them, SourceSharp_ValidateServerApi no
longer requires them, and their Entry.cs thunks are deleted. Marked
SS_RETIRED in sourcesharp.h and Abi.cs; the fields go in E-last's single
removal pass, with the version bump.

## plan_engineapi_vtables E-last (ABI 253 -> 254)

The single removal pass the SS_RETIRED markers named. The 103 server ManagedApi
slots that E1-E18 moved onto managed-built areas (IManagedRuntimeServer and
the fifteen generated IManaged* interfaces of docs/managed-interfaces.tsv)
are REMOVED from SourceSharpManagedApi and Abi.cs's ManagedApi, together with
each field's own comment, in the same order on both sides; 38 remain, all the
client's (filled by Entry.LoadSide's client arm, for LoadSide V1).
LoadSideV2 no longer takes a SourceSharpManagedApi: the server is handed no
table, its SourceSharp_GetManagedApi() answers NULL, and managed hands back
its ABI version through an out parameter so native still refuses a skewed
pair. Removed, in struct order:

GetGameDescription, OnClientPutInServer, OnClientActive, OnClientGamePrecache, OnRespawn, OnPlayerSpawn, OnPlayerKilled, OnClientConnect, OnClientCommand, GetPlayerRelationship, CanTakeDamage, OnPlayerSpawned, OnSelectSpawnPoint, OnPlayerSurfaceChanged, OnFireAddHeatInRadius, OnRagdollMagnetForce, OnEntityIgnite, FramePreThink, FramePostThink, ServerSystemStart, ServerGameStart, ServerGamePostStart, ServerGameStop, ServerMapLoadTransition, ServerMapLoadSaveGame, ServerMapLoadBackground, ServerMapLoadNewGame, ServerMapUnload, ServerActivate, ServerGameFrame, ServerPreClientUpdate, ServerCreateNetworkStringTables, ServerTickInterval, ServerShouldHideServer, ServerBrowserMapOverride, ServerBrowserGameData, ServerIsManualMapChangeOkay, ServerQueryCvarValueFinished, ServerHibernation, ServerStatusLines, ServerThink, ServerInvalidateMdlCache, ServerSteamGameServerActivated, ServerSteamGameServerShutdown, ServerMapLoadPreEntity, ServerDecalIndexForName, ServerDecalNameForIndex, ServerDecalTranslateForGameMaterial, ServerPropDataRandomChunkModel, ServerSoundEntInit, ServerSoundEntShutdown, ServerSoundEntInsert, ServerSoundEntResetClientSound, ServerSoundControllerCreate, ServerSoundControllerDestroy, ServerSoundControllerPlay, ServerSoundControllerChangePitch, ServerSoundControllerChangeVolume, ServerSoundControllerFadeOut, ServerSoundControllerShutdown, ServerSoundControllerCommandAdd, ServerSoundControllerCommandClear, ServerSoundControllerPlayEnvelope, ServerSoundControllerReset, ServerSoundControllerGetPitch, ServerSoundControllerGetVolume, ServerWorldKeyValue, ServerMapUnloadPreEntity, ServerSkyboxAreaForClient, ServerEarPositionForClient, TemplatesAddNamed, Dispatch, HierarchyParentMoved, HierarchyChildMoved, HierarchySetParent, HierarchyGetParentAttachment, HierarchySetParentName, HierarchySetParentAttachmentName, HierarchyEntityRemoved, HierarchyResolveParentNames, HierarchyToLocal, HierarchyToWorld, HierarchySetLocal, EntityRemoveRequested, PlayerRunCommands, PhysicsEvents, ServerGlobalStateAdd, ServerGlobalStateIndexOf, ServerGlobalStateCount, ServerGlobalStateGetState, ServerGlobalStateSetState, ServerGlobalStateGetCounter, ServerGlobalStateSetCounter, ServerGlobalStateAddToCounter, ServerGlobalStateGetName, ServerGlobalStateGetMap, ServerGlobalStateSetMap, ServerGlobalStateEnableUpdates, ServerGlobalStateDump, ShellLessFindByName, ShellLessFindByClassname, PlayerDecodeCommands, ModelPrecacheComponents

