# C7 cluster 12: clientleafsystem.cpp, a survey and a plan

Status: survey done (2026-10-18, lane C7 flashlight, at main b32ca3536 +
the flashlight lane). Reviewed 2026-10-19: the user's rulings are in
Decisions, and L0-L3 below follow them. L0 and L1 done 2026-10-19 (lane
on 9f2f149d5; see "L0 and L1 as built" below); L2 and L3 not started.
flashlighteffect.cpp, the other half of the brief, is ported and evicted
(see docs/client_evictions.md). The leaf system was too big to do in the
same lane. This file is the plan the brief asked for in that case.

## What the TU is (measured)

- `game/client/clientleafsystem.cpp` in the fork: 1,834 lines, plus 219 in
  `clientleafsystem.h`. Preprocessed with its own `compile_commands.json`
  line (farm/debug), the TU's own text is 1,682 lines.
- It **exports `ClientLeafSystem002`**
  (`EXPOSE_SINGLE_INTERFACE_GLOBALVAR`, clientleafsystem.cpp:341), and
  engine.so names both `ClientLeafSystem001` and `ClientLeafSystem002`
  (`strings -a` over the install's bin/linux64/engine.so). The engine calls
  `IClientLeafSystemEngine`'s four methods through it: CreateRenderableHandle
  (its bIsStaticProp argument is for the engine's static props), RemoveRenderable,
  AddRenderableToLeaves and ChangeRenderableRenderGroup. That is reason (a):
  a vtable the engine calls. Some native object must answer that name.
- Entity-census cross-edges: 3, all in the shim's direction
  (`DefaultRenderBoundsWorldspace`, `CClientRenderablesList::s_Allocator`,
  `g_pClientLeafSystem`).
- **No compiled IGameEntity use.** The one site, `SDKEntityFromRenderable` /
  `GetClassname` in AddRenderableToLeaf, sits under
  `#ifdef DUMP_RENDERABLE_LEAFS`, which nothing defines. In the preprocessed
  TU, neither name appears in the file's own text, and AddRenderableToLeaf
  does (proof of read). sdk_player_cast_test still counts the site because
  its guard stack does not know the macro is undefined.

## Who calls it in client.so (grep over src/client/native and the farm's fork TUs)

| caller | methods |
|---|---|
| shell_view_executor_client.cpp | BuildRenderablesList, ComputeTranslucentRenderLeaf, CollateViewModelRenderables, ShouldDrawDetailObjectsInLeaf |
| shell_renderable.h, shell_beam_render.cpp, shell_managed_particles.cpp, shell_new_particles_client.cpp | AddRenderable / CreateRenderableHandle, RenderableChanged, RemoveRenderable, SetRenderGroup |
| shell_standalone_client.cpp | RenderableChanged, SetRenderGroup |
| shell_legacy_tempents_client.cpp, c_te_legacytempents.cpp | SetRenderGroup |
| shell_detail_props.cpp | SetDetailObjectsInLeaf, SetSubSystemDataInLeaf, DrawDetailObjectsInLeaf |
| shell_precipitation_client.cpp | IsRenderableInPVS |
| clientshadowmgr.cpp (fork) | AddShadow, RemoveShadow, ProjectShadow, ProjectFlashlight, EnumerateShadowsInLeaves |

## Registry versus decisions

What the file holds splits in two. plan_client_novalve.md (the leaf system
and shadow manager paragraph) already rules the shape: the file is
"rewritten as ours under a Valve header, kept native as B1 machinery, and
stripped of policy".

**Everything moves to managed** (user ruling 2026-10-19, which supersedes
the "kept native as B1 machinery" wording above for this file: that wording
predates the 2026-10-18 forwarder ruling, under which a Valve interface that
Valve code calls and managed answers is a libss_fwd class over a managed
table, as fwd_physics.cpp and fwd_steam.cpp are). The registry moves too:
the renderable table (`m_Renderables`), the leaf buckets
(`m_RenderablesInLeaf`, `m_ShadowsInLeaf`, `m_ShadowsOnRenderable`), the
spatial-partition insert, remove and re-insert (InsertIntoTree,
RemoveFromTree, PreRender's dirty list), the shadow/flashlight leaf
projection, and the detail-object and sub-system leaf data.

**The native residue is one generated forwarder class** in
src/fwd/native, with no `if` on game state:

- `ClientLeafSystem002` for the engine: IClientLeafSystemEngine's four
  methods. The same class answers IClientLeafSystem for the C++ callers
  left in client.so (clientshadowmgr.cpp, our shells), each slot retired
  as its caller is ported.
- Two callbacks into Valve vtables, carried out rather than decided: the
  engine's EnumerateLeavesInBox (clientleafsystem.cpp:1237) calls
  ISpatialLeafEnumerator::EnumerateLeaf back, and EnumerateShadowsInLeaves
  calls the shadow manager's IClientLeafShadowEnum::EnumShadow
  (clientleafsystem.h:108-112). Managed hands back a list; native loops it.
- `IClientRenderable::RenderHandle()` is a virtual returning a reference
  (clientleafsystem.cpp:624, 718): the forwarder writes managed's handle
  into it.
- The engine queries asked per renderable (DoesBoxTouchAreaFrustum, CullBox,
  IsOccluded; clientleafsystem.cpp:1639-1653) become one generic batched
  call per view: managed sends N boxes, native asks the engine N times, and
  N bits come back.

**Decisions (to managed):**

1. **Which renderables a view draws.** This is CollateRenderablesInLeaf's
   arms (clientleafsystem.cpp:1572-1755): the static-prop and small-object
   switches (DrawStaticProps / DrawSmallEntities), the same-frame dedupe,
   alpha 0 culled when translucent objects are drawn, the area-frustum test
   under r_PortalTestEnts, the main-frustum CullBox, the occlusion test for
   studio models, the render-distance cutoff for things shorter than 100
   units, the opaque size bucket, and the two-pass split. The engine
   queries (CullBox, DoesBoxTouchAreaFrustum, IsOccluded) stay calls. What
   to do with their answers is the decision.
2. **Translucent order.** ComputeTranslucentRenderLeaf (1389-1513) and
   SortEntities (1756-1810): the nearest leaf per translucent renderable,
   then a back-to-front sort.
3. **Detail objects per leaf:** ShouldDrawDetailObjectsInLeaf's frame
   gating (1378). Which detail objects exist is shell_detail_props.cpp's, fed by managed (C7 cluster 10).
4. **Shadow receipt:** ShouldRenderableReceiveShadow (935), the flag test
   that decides which renderables a shadow lands on.
5. **View models:** CollateViewModelRenderables (1514-1571), the opaque or
   translucent split of the view-model groups.

## The plan (phases; each lands with its facts, oracle first)

- **L0, the oracle.** Compile the pristine CClientLeafSystem from the
  valve-sdk.lock clone in a harness, the same shape as
  flashlight_oracle_test (src/test/oracle/native): stub engine queries,
  scripted renderables, leaves and views. Record BuildRenderablesList's and
  ComputeTranslucentRenderLeaf's output (list contents, order, buckets), and
  the shadow enumeration EnumerateShadowsInLeaves makes (the registry moves
  in L2), as a golden under src/test/client/Fixtures. Include a scenario-reach fact: every
  arm in decision 1 fires at least once.
- **L1, the decisions in C#.** Add a managed `RenderableListBuilder` that
  replays the golden bit-exact (decisions 1, 2 and 5) over a batched
  per-view input: one length-prefixed buffer per view, with each candidate
  renderable's group, flags, bounds, alpha, area and leaf, plus the engine
  answers already asked in the same pass. Per the length-prefixed-buffer
  ruling, that is one crossing per view, never one per renderable. This
  lands the decisions while the registry is still stock.
- **L2, the registry in C#.** The renderable table, leaf buckets, partition
  re-insert, shadow projection and detail data move into managed, with
  decisions 3 and 4. Native becomes the forwarder class above (rows in
  docs/managed-interfaces.tsv, generated by tools/managed-areas, plus the
  batched engine query as a generic shim row). Then evict
  clientleafsystem.cpp. This needs an eviction row, `make debug
  GC_SECTIONS=0`, a live A/B with cs_cl_render_trace (the trace is the
  render list's witness), the second map load, and the ledgers. It may land
  before clientshadowmgr.cpp's rewrite (Decisions): the forwarder keeps the
  shadow manager's five methods for it.
- **L3, measure.** Count crossings and time per frame on dm_lockdown (473
  entities) against the base, with numbers: the per-view call, the batched
  query, and RenderableChanged from the native shells. Per the port-budget
  ruling, the measurement follows the move; a blown budget is a batching
  task, never a retreat.

## L0 and L1 as built (2026-10-19)

- **L0**: src/test/oracle/native/leafsystem_oracle_test.cpp compiles the
  pristine clientleafsystem.cpp (with its header, igamesystem.h and the
  pristine iclientunknown.h / iclientrenderable.h) over stand-ins
  (leafsystem_stub/leafsystem_harness.h) and writes
  src/test/client/Fixtures/leafsystem-stock.tsv (README beside it). Its
  facts: the golden; the reach of every arm of decisions 1, 2 and 5 (42 arms
  in the debug build, each a non-zero count held to whether stock listed the
  entry); the live gather's buffers equal the golden's; Apply carries a
  stock-shaped answer out; the layout mirror's offsets equal the pristine
  class's; the farm's class text is the pristine's.
- **L1**: SourceSharp.Client.Render.LeafSystem.RenderableListBuilder makes
  decisions 1, 2 and 5 over one buffer per view
  (src/client/native/leaf_view_gather.h writes it), replaying the golden
  bit-exact (RenderableListBuilderOracleTests). shell_view_executor_client.cpp
  asks IManagedRenderClient::RenderLeafList once per view that draws
  entities and RenderLeafViewModels once per view-model draw, and carries the
  answers out; with no answer, stock's functions run. The managed ConVar
  cs_cl_leaf_lists (default 1) is the A/B switch
  (tools/probes/sessions/c7-leaf-ab.steps).
- **For L2**: the mirror, the gather's registry reads and BlendPass go when
  the registry is managed; the gather then shrinks to the engine questions.
  **For L3**: the gather asks GetRenderBoundsWorldspace, GetRenderBounds,
  GetRenderOrigin, GetFxBlend and the three engine questions of every
  renderable in the view's leaves once per view, where stock asked only the
  renderables its earlier arms kept; measure it.

## L2 and L3 as built (2026-10-19, lane worktree-agent-a64d37100d09fb633, base 62e8b7d00)

Commits, in order: 0492995ba (the oracle's registry surface), 484c8cbf3 (the
managed registry), 70de6086c (the replacement), 75d9ab4fc (a test fix the
new facts exposed), b6ae89424 (the deletion), f672c994e (the eviction),
23806a944, 2826e840a, bd669e8b1 (L3's batching), 91df2566b (the
forwarder fake records the queued slots), e29ec3584 (the shim ratchet,
138432 → 138529, for the queue). `make test-full` at e29ec3584: all 14 legs
green. Counts: checks 28, managed 2130, managed-server 7205, managed-client
3038, tf2_descent 172, tf2 3, native 3526 gtests in 318 binaries plus 13
must-not-compile, entity 282, ui 490, runner 132, checks-full 3,
oracle-managed 186, oracle-native 136 gtests in 30 binaries, census 113 in
7. `make ci-build` (sniper container, gcc) at e29ec3584: green, GLIBC ceiling
held (newest GLIBC_2.29).

- **The oracle first.** leafsystem_oracle_test gained the registry's whole
  surface as rows: handles, render groups, alternate sorting, static props,
  ConVars, PVS, leaf walks, detail and sub-system data, shadows and
  flashlights, level shutdown, the boxes the engine is asked, and each view's
  detail frame, origin and leaves. The golden (leafsystem-stock.tsv) carries
  its own stimulus, so the C# replay rebuilds the scenario from it.
  LeafRegistryOracleTests (10 facts, one with a hot reload after every view
  and registry call) holds the managed log to stock's, kind by kind between
  anchor rows. The facts did not compile before 484c8cbf3; the mutation
  proofs below show them red.
- **The registry** (src/client/managed/Render/LeafSystem/LeafRegistry.cs) is
  DI only, with no statics, and is an IReloadParticipant. It reproduces:
  - CUtlLinkedList's LIFO handles, reset by a purge;
  - CBidirectionalSet's newest-first order both ways;
  - CUtlVector::FastRemove;
  - PreRender's back-to-front dirty list;
  - the m_ShadowEnum quirk: AddRenderableToLeaves does not advance the
    enumerator, so the second static prop added in a leaf misses the shadow.

  Every question it asks native is a batch through ILeafNative (blends, facts
  by mask, receives, EnumerateLeavesInBox, GetLeavesArea, the culling program
  per box, detail models). A view asks the engine only about the boxes its
  earlier checks kept: one BoxQueries call per view.
- **The native residue** is one forwarder class, SSClientLeafSystem in
  src/fwd/native/fwd_leaf_system.cpp (libss_fwd_client).
  - It answers ClientLeafSystem002 and IClientLeafSystem.
  - It makes the EnumerateLeaf (ISpatialLeafEnumerator) and EnumShadow
    callbacks and writes RenderHandle().
  - It carries out managed's LeafOps in order.
  - Its `if`s are bounds checks, null guards and the switch over an op's kind.

  client.so keeps src/client/native/shell_leaf_system_client.cpp: the
  object's storage, the env of client.so addresses, g_pClientLeafSystem, the
  CClientRenderablesList allocator, DefaultRenderBoundsWorldspace and the
  client-system adapter row.
- **Deleted** in b6ae89424:
  - CLeafRegistryMirror and the gather (moved oracle-only, to
    src/test/oracle/native/leafsystem_stub/leafsystem_gather.h);
  - BlendPass (logic-lines 8301 → 8281);
  - the executor's L1 fallback arms, IClientLeafLists and LeafListsAbi.cs.

  RenderLeafList and RenderLeafViewModels are retired slots: they return -1
  and are still counted.
- **Evicted** in f672c994e: clientleafsystem.cpp left the client CMake list
  and sdk-links/client.txt. Its four ConVars are now managed's
  (LeafListConVars.cs), except r_portalsopenall, which the engine owns and is
  still watched. `make debug GC_SECTIONS=0` and nogc-relink are green.
- **Live A/B**: c0-session, debug, dm_lockdown, poses 1 to 3, a second map
  load, and the flashlight (impulse 100, r_flashlightvisualizetrace 1).
  - At bd669e8b1 (tip-live3), every world view's scenebytes is byte-equal to
    base-live (62e8b7d00): pose1, pose2, pose3, reload-pose1, and both of the
    flashlight's scenes (shadow depth and main).
  - The base runs differ among themselves. base-live2 and base-live3 reorder
    6 entities (entindex 40, 83, 90, 135, 162, 292) inside one leaf's
    group-5 bucket at pose2 and pose3, and base-live3 has one entity fewer
    (entindex 264) after the reload. That is process-to-process spawn order,
    present at the base alone.
  - 0 `EXCEPTION IN` lines and 0 `could not acquire "` lines. The view model,
    the static props and the flashlight are in the screenshots.

### L3, measured (c0-session, debug, dm_lockdown, 10 s windows, cs_cl_crossings and the render plan's planTimes)

The windows: pose1 is the render gate's stand, (-4116, 4600) looking south;
pose3 is (-3800, 2500); reload-pose1 is pose1 after a second map load. The
base is 62e8b7d00, from its own worktree build. The tip runs alternated with
the base runs in one session series (ll2-l3-*.steps), under load averages of
10 to 35 from other sessions.

Inbound crossings per client frame. All runs agree to ±0.05.

| window | base 62e8b7d00 (6 runs) | f672c994e, unbatched | 2826e840a (3 runs) | bd669e8b1 (3 runs) |
|---|---|---|---|---|
| pose1 | 54.47 (leaf 2: RenderLeafList, RenderLeafViewModels) | 224 (leaf 172: RenderableChanged 107, ChangeRenderableRenderGroup 57, TakeOps 3) | 58.46 (leaf 6) | **57.47 (leaf 5)** |
| pose3 | 71.47 (leaf 2) | - | 77.8-78.2 (leaf 8.4: Pending 2.05, SetRenderGroup 1.4) | **74.46 (leaf 5)** |
| reload-pose1 | 54.47 (leaf 2) | - | 58.46 (leaf 6) | **57.47 (leaf 5)** |

The tip's five leaf crossings per frame are PreRender,
ComputeTranslucentRenderLeaf, BuildRenderablesList,
CollateViewModelRenderables and DetailLeaves. The base had two. The +3.0 a
frame is the same at every pose.

The render gate's own instrument reads 36.3 a frame at this stand on main
(daily gate 2026-10-18, 11631/320). Its ceiling is 55.164 (render row in
tools/live-gate-budget.tsv). Adding +3 gives about 39.3, so the row is not
expected to move. It was not re-run here; the daily gate measures it.

The engine and renderable batches the managed registry asks, per frame at
pose1 (cs_cl_leaf_stats, bd669e8b1):

| batch | calls | items |
|---|---|---|
| ComputeFxBlends | 2 | 123 |
| Facts | 3 | 351 |
| Receives | 1 | 72 |
| EnumerateLeaves | 1 | 107 |
| LeavesAreas | 1 | 107 |
| BoxQueries | 1 | 122 (the one batched engine query per view) |

The queue (RenderableChanged, ChangeRenderableRenderGroup, SetRenderGroup)
crosses 0 times a frame of its own. It rides in PreRender and the three
per-view calls, and in the blend batch's answer.

Render time, the plan's msAvg per window. These are the min and the median
of the runs, because load from other sessions moved single runs by up to 4x:

| window | base min / median (6) | tip bd669e8b1 min / median (3) |
|---|---|---|
| pose1 | 1.538 / 2.25 ms | 1.314 / 2.59 ms |
| pose3 | 1.950 / 2.52 ms | 1.624 / 2.05 ms |
| reload-pose1 | 1.578 / 1.77 ms | 1.265 / 1.70 ms |

At its least loaded, the tip renders 14 to 20% faster than the base at
its least loaded. The medians overlap. The managed leaf system's own time is
165 to 187 µs a frame in the quietest tip run (tip-4) and up to 519 µs under
load: PreRender 99, BuildRenderablesList 57, ComputeTranslucentRenderLeaf
22 µs in tip-live3's first window. PreRender and the per-view builder
allocate their batch arrays per call, which is the perf pass's first item.

### Mutation proofs (.ll2/mut.py: a byte snapshot, sha256 before, mutated and restored, a fresh mtime on restore)

Each mutation is red, and the source is restored to its pre-mutation
sha256. Managed runs use `make managed-test` over LeafRegistryOracleTests
and LeafSystemClientBindingTests; native runs use `buildcpptests
fwd_leaf_system_test`.

| mutation | file | sha before → mutated | red |
|---|---|---|---|
| PreRender walks the dirty list forwards | LeafRegistry.cs | 58248cfa675a → f17c67b9d032 | 5 of 20 facts |
| freed handles not reused (no LIFO) | LeafRegistry.cs | → 92c09a464d44 | 5 |
| FastRemove keeps order | LeafRegistry.cs | → e03016c225d9 | 4 |
| m_ShadowEnum quirk "fixed" (AddRenderableToLeaves advances) | LeafRegistry.cs | → 3f30c27696e7 | 3 |
| InsertIntoTree does not advance m_ShadowEnum | LeafRegistry.cs | → a20765d2cde0 | 3 |
| queue kinds swapped (ApplyPending) | LeafRegistry.cs | → 8540ee9bf130 | 4 |
| Collate ignores the queue it carries | LeafSystemClientBinding.cs | 1120f8c68fc7 → 26becb035133 | 1 |
| the blend batch's answer not applied | RenderableListBuilder.cs | bdfeb4cb003c → d43a63fbad4b | 1 |
| PreRender drops its queue | fwd_leaf_system.cpp | 8ffb8bbd340e → 2fe9ecdb1850 | 4 gtests |
| SetRenderGroup forwarded, not queued | fwd_leaf_system.cpp | → 0f1ffc1b30fb | 1 gtest |
| no flush before another slot | fwd_leaf_system.cpp | → d8e21cbaae6b | 1 gtest |
| a full queue is dropped, not handed over | fwd_leaf_system.cpp | → c089819559d1 | 1 gtest |

The SetRenderGroup mutation first crashed the fake, which had no slot for
the queued calls. The fake now records those slots, so the mutation fails
an assertion.

### What clientshadowmgr.cpp's rewrite inherits

- **The shadow half of the registry is managed already.** It covers
  AddShadow, RemoveShadow, ProjectShadow, ProjectFlashlight and
  EnumerateShadowsInLeaves, the shadows-in-leaf buckets, and the receiver
  test (decision 4: ShouldRenderableReceiveShadow, asked in one Receives
  batch per operation).
- **A managed shadow manager calls LeafRegistry directly, with no
  crossing.** The ops AddShadowToReceiver and RemoveAllShadowsFromReceiver
  (LeafOpKind 1 and 2) are how the registry tells today's native shadow
  manager. They become direct calls, and the forwarder's five shadow methods
  and the EnumShadow op (8) go with the native manager.
- **m_ShadowsOnRenderable is not kept**, because stock writes it and never
  reads it. A managed manager that needs each renderable's shadows adds the
  set it reads.
- **The m_ShadowEnum quirk is reproduced and held by the oracle.** A rewrite
  that "fixes" it changes which static props receive shadows, so the change
  has to be made on purpose, with the golden re-recorded.

### Divergences (each held by a fact or by the live A/B)

- m_ShadowsOnRenderable is omitted (see above).
- The receive questions are deferred to one batch per operation, in the
  order stock offered them, and skipped when the leaves hold no shadows.
- IsTwoPass is asked of every renderable at CreateRenderableHandle. Stock
  asks it only of a translucent one, and managed still uses the answer only
  then.
- DrawDetailObjectsInLeaf on a leaf out of range answers an empty range
  (0/0). Stock does not check.
- The view models' blends are one batch (stock blended each in its loop).
  A group change a blend queues is applied before the split is read.
- A list overflow warns once per group per view (Con_NPrintf on line 10, as
  stock printed it).
- Calls the forwarder queues reach managed at the next per-frame call, or
  first, before any other slot. A call native carries out that marks a
  renderable dirty lands in the next PreRender, as stock's did.
- The FB order for detail models follows the entity FBs in the golden's
  comparison. The NP warnings compare as a set.

## Rulings made here (dated, the brief's "make rulings yourself")

- 2026-10-18: the leaf system is too big for the flashlight lane (it is the
  per-view render list, with five decision families and an engine-called
  interface), so it is surveyed and planned here rather than ported.
- 2026-10-18: L1 batches per view and never crosses per renderable. The
  design comes first; L3 measures it.
- 2026-10-18: nothing in the leaf TU takes IGameEntity in compiled code, so
  a retype to C_StandaloneEntity* has no subject here. The client's
  compiled IGameEntity users are ours (trace_filter_library.cpp's
  GameEntityFromEntityHandle).
- 2026-10-19 (L1 lane): the stock registry's members are private to the
  fork's clientleafsystem.cpp, and the fork is not ours to edit, so L1 reads
  them through a layout mirror (CLeafRegistryMirror, leaf_view_gather.h):
  the same bases and data members, held by leafsystem_oracle_test to the
  pristine class's offsets and to the farm's class text, and checked at run
  time (each renderable's RenderHandle() must name the slot it was read
  from, else stock's builder runs). It is temporary: L2 deletes it with the
  stock registry.
- 2026-10-19 (L1 lane): ComputeTranslucentRenderLeaf's first loop (each
  renderable's ComputeFxBlend once per frame and view, by the registry's
  stamp) stays native in L1, ruled LOGIC (+20 logic-lines): the blend must
  happen before the group and alpha managed decides on are read, and a
  second crossing per view to decide it was refused by the one-crossing
  rule. It moves with the registry in L2.
- 2026-10-19 (L1 lane): the gather asks CullBox, IsOccluded and (for a
  renderable in one area) DoesBoxTouchAreaFrustum of every renderable in the
  view's leaves, so the decision has every answer it may need in one pass.
- 2026-10-19 (L1 lane): the plan's "render-distance cutoff for things
  shorter than 100 units" is under `#ifdef INVASION_CLIENT_DLL` in stock and
  not compiled in this game: there is no such arm to port.

- 2026-10-19 (L2 lane):
  - **The forwarder is a libss_fwd_client class, built by client.so into its
    own storage through the HAND table's Construct.** libss_fwd_client has
    no RTTI and no heap. client.so therefore keeps:
    - the client-system row, an adapter, because shell_clientsystems' walk
      dynamic_casts;
    - CUtlVector appends, as a callback.
  - **The L1 gather and the layout mirror move oracle-only**, not deleted:
    the oracle still needs stock's buffers.
  - **RenderLeafList and RenderLeafViewModels are retired, not removed**:
    the slots only append.
- 2026-10-19 (L3, batching instead of retreating): the unbatched forwarder
  made 224 inbound crossings a frame at the stand.
  - **RenderableChanged, ChangeRenderableRenderGroup and SetRenderGroup are
    queued in the forwarder**, three int32 an entry, 256 at most.
  - **The queue is handed over in the arguments of PreRender and the three
    per-view calls, in the blend batch's answer, and through Pending.**
    Pending is used only for a full queue or for any other slot.
  - **The op buffer is 512 operations (16 KB of stack)**, so a view's list
    fits in one buffer.

  The IManagedLeafSystemClient rows and the HAND LeafSystem slot were
  changed in place three times. Each change is a deliberate break, named in
  its commit and regenerated on both sides. The tables were born in this
  lane, and nothing on main reads them.

## Decisions

- 2026-10-19 (user): the registry phase (L3 in the reviewed draft, L2 now)
  may land before clientshadowmgr.cpp's own rewrite.
- 2026-10-19 (user, "why keep any leaf in native code?"): no leaf logic
  stays native. Native keeps only the generated forwarder class; the
  registry moves to managed with the decisions.
