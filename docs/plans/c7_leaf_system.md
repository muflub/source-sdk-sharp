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

## Decisions

- 2026-10-19 (user): the registry phase (L3 in the reviewed draft, L2 now)
  may land before clientshadowmgr.cpp's own rewrite.
- 2026-10-19 (user, "why keep any leaf in native code?"): no leaf logic
  stays native. Native keeps only the generated forwarder class; the
  registry moves to managed with the decisions.
