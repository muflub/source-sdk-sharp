# C7 cluster 12: clientshadowmgr.cpp, a survey and a plan

Status: survey done 2026-10-19 at main a001d7a31 (worktree
agent-a1c5c1e9284f43072, `make setup ROOT=../source-sdk-2013` and
`make debug`). Reviewed 2026-10-19: Q1 and Q2 are ruled (Decisions). Nothing is implemented. The leaf system's plan
(docs/plans/c7_leaf_system.md) is the model, and its section "What
clientshadowmgr.cpp's rewrite inherits" is this plan's starting point.

Line numbers `NNNN` without a file name are the fork's
`src/game/client/clientshadowmgr.cpp`, as the farm links it
(`farm/debug/src/game/client/clientshadowmgr.cpp`). The pristine file's
lines differ by the fork's edits listed in §1.

## 1. What the TU is (measured)

- **Size.** The fork's file is 4,484 lines and the pristine clone's
  (valve-sdk.lock 22288b91) is 4,335 (`wc -l`). The fork's edits are 32
  hunks, +246/−97 lines (`diff` pristine fork). Preprocessed with its own
  `compile_commands.json` line (farm/debug), the TU is 72,719 non-blank
  lines, and 2,673 of them are the file's own (by line marker;
  scratchpad `sm-pp.py`). `iclientshadowmgr.h` is 114 lines (fork) against
  112 (pristine): the fork derives IClientShadowMgr from our
  CClientSystemPerFrame instead of IGameSystemPerFrame.
- **The fork's edits.** None of them changes behaviour in a shipping build.
  Each one deletes an arm that has answered NULL or false in every build:
  - 14 `ClientEntityList()` resolves became `SourceSharp_RenderableFromHandle`
    (renderable_facts.h).
  - The bone-setup queue is gone (1053-1067, 3659-3672, 4096-4100).
  - The fourth clip plane is gone (2248-2258).
  - The static-prop receiver arm for NPC/player casters is gone (3527-3538).
  - The flashlight-target tails are gone (2716-2736, 4214-4230).
  - The fork adds `cs_cl_shadow_report` and its counter (98-103, 4377-4484).
- **Exports** (`nm -C` over the object in farm/debug/build):
  - `g_pClientShadowMgr` (D), a plain global (1014). It is not a factory
    interface.
  - Three global ConVars (B): `r_flashlightdepthtexture`,
    `r_flashlightdepthres`, `r_threaded_client_shadow_manager`.
  - 127 T symbols and the vtables of CClientShadowMgr, CVisibleShadowList,
    CShadowProxy and CShadowModelProxy.
  - Two `InterfaceReg`s: `"Shadow" IMATERIAL_PROXY_INTERFACE_VERSION` (4283)
    and `"ShadowModel" ...` (4376).
  - 106 undefined names.
- **What the engine asks for by name.**
  - `grep -a` over every .so in the install's bin/linux64 finds no
    `ClientShadowMgr` string at all. The engine never asks for this object.
    engine.so names only its own `VEngineShadowMgr002`, which the client
    acquires (shell_clientdll.cpp:995) as `shadowmgr`.
  - The material system asks for the two proxies by name. It builds the name
    at run time from the material's proxy block plus `_IMaterialProxy003`,
    and looks it up in client.so's CreateInterface
    (shell_material_proxy_client.cpp:12). The materials that name them are
    `decals/rendershadow.vmt` (proxy `Shadow`) and
    `decals/rendermodelshadow.vmt` (proxy `ShadowModel`), read out of
    hl2_misc_dir.vpk (scratchpad `sm-vpk.py`). `decals/simpleshadow.vmt`, the
    blob shadow, has no proxy.
  - engine.so and materialsystem.so read the TU's ConVars by name:
    - engine.so: `r_shadowrendertotexture`, `r_shadows`,
      `r_shadows_gamecontrol`, `r_flashlightdepthtexture` and
      `r_flashlight_version2`;
    - materialsystem.so: `r_flashlightdepthtexture`,
      `r_shadowrendertotexture` and `r_shadows`.

    Whoever owns these ConVars must keep them registered under those names.
- **Engine and Valve vtables the TU answers:**
  - IMaterialProxy ×2: Init, OnBind, Release, GetMaterial (4236-4376).
  - ISpatialLeafEnumerator::EnumerateLeaf (CShadowLeafEnum, 2292-2302). The
    engine's BSP query calls it back.
  - IClientLeafShadowEnum::EnumShadow (CVisibleShadowList, 1102). Our leaf
    forwarder calls it (op 8).
  - The material system's restore callback (ShadowRestoreFunc, 1317).
- **Entity-census cross-edges:** 1, `g_pClientShadowMgr`. The client
  total is 25 symbols across 7 fork TUs (`tools/entity-census --print`).
- **Globals:** 31 rows in docs/globals-client-ledger.tsv, all UNRULED.
  `readelf -sW` over the object shows:
  - the two singletons `s_ClientShadowMgr` and `s_VisibleShadowList`;
  - 10 ConVars;
  - 6 ConCommands, plus `r_shadowbreak` (_DEBUG);
  - the 2 InterfaceRegs;
  - `s_nRttShadowTexturesDrawn` and `s_bBreak`;
  - 4 function-local statics.
- **What it calls** (grep over the TU):
  - 45 `shadowmgr->` sites over 21 IShadowMgr methods;
  - 20 distinct IClientRenderable methods (GetRenderOrigin 23 times);
  - 22 IMatRenderContext methods;
  - staticpropmgr's Add/RemoveAllShadows(To|From)StaticProp;
  - modelrender's DrawModelShadowSetup/DrawModelShadow and render's
    DrawBrushModelShadow;
  - `view->UpdateShadowDepthTexture`;
  - engine's CullBox, GetAmbientLightColor and GetBSPTreeQuery.
- **What default play reaches** (the frozen recordings,
  src/test/client/Fixtures/render-stock/):
  - Every recorded frame has `r_shadowrendertotexture 0` and
    `r_flashlightdepthtexture 1`. Shadows are blobs, and
    ComputeShadowTextures returns at its first line (4045).
  - The dm_lockdown frames carry 6 `shadow.*` lines and 0 `~` lines (no
    shadow re-projected in that frame).
  - ss_sandbox-spawn carries 18 `~` lines (9 UpdateShadow re-projections,
    each a FogMode pair at 3182/3198), and ss_sandbox-underwater carries 10.

## 2. Who calls it

| caller | what | per |
|---|---|---|
| shell_standalone_client.cpp:1361-1412 (RefreshShadow / DestroyShadow, from AddToLeafSystem :1034) | CreateShadow, DestroyShadow | entity, on a render-policy change |
| shell_standalone_client.cpp:983-988 (OnPositionChanged) | AddToDirtyShadowList(this), MarkRenderToTextureShadowDirty when the pose moved | entity, per frame it moves |
| shell_flashlight_client.cpp:114-142 (IShimFlashlightClient, from FlashlightEffect.cs:293-297, 344) | CreateFlashlight, UpdateFlashlightState, UpdateProjectedTexture(h, true), DestroyFlashlight | frame, while the light is on |
| shell_worldrender.cpp:93-98 (from ClientWorldState.cs:52, shadow_control) | SetShadowDirection, SetShadowColor, SetShadowDistance, SetShadowsDisabled | level / change |
| shell_view_executor_client.cpp:1351, 1363-1365, 1434→465, 1592 (the plan's ops 4, 11, 12, 13, 32, 114) | AdvanceFrame, PreRender, ComputeShadowDepthTextures, UnlockAllShadowDepthTextures, ComputeShadowTextures, RenderShadowTexture | frame / view |
| shell_clientsystems.cpp:144, 290 (row "CClientShadowMgr") | Init, Shutdown, LevelInitPreEntity, LevelShutdownPostEntity, PreRender (in the ClientSystemsPreRender walk) | system |
| fwd_leaf_system.cpp:506-516, 560 (LeafOpKind 1, 2, 8; LeafSystemAbi.cs:109-130) | AddShadowToReceiver, RemoveAllShadowsFromReceiver, IClientLeafShadowEnum::EnumShadow | operation |
| shell_render_trace_client.cpp:956-998, 1095-1099 | patches five slots of our own object's vtable to print `shadow.*` lines (the frozen recordings name them) | recorder only |
| materialsystem (engine) | the Shadow / ShadowModel proxies' OnBind | each bound RTT shadow decal |
| engine BSP query | CShadowLeafEnum::EnumerateLeaf | leaf, per projection |

The other direction, the manager calling ours: the leaf forwarder's five
shadow slots (fwd_leaf_system.cpp:348-386). AddShadow and RemoveShadow run
per shadow. ProjectShadow runs per re-projected shadow (2427, 2609),
ProjectFlashlight per flashlight update (2712), and EnumerateShadowsInLeaves
per world view with RTT on (1197). Each one is a crossing into managed
today. `view->UpdateShadowDepthTexture` (4026) also re-enters managed, through
shell_viewrender_client.cpp:262 into RenderShadowDepthPlan
(docs/managed-interfaces.tsv:257).

No Valve code in client.so calls IClientShadowMgr. The only fork TU left
beside it is in the six-TU lane's set, and none of those imports a shadow
symbol (`nm -u` over the six objects ∩ the TU's `nm --defined-only`: 0).
That makes every caller ours. **No libss_fwd class is needed for
IClientShadowMgr itself.** Our callers are re-pointed at the managed manager
(§4).

## 3. What the engine needs native, and what moves

### Native by layout or by pointer (only these)

1. **The resources the engine holds pointers to:**
   - the three materials, as CMaterialReference: `decals/simpleshadow`,
     `decals/rendershadow` and `decals/rendermodelshadow` (1328, 1463-1464).
     CreateShadowEx and SetShadowMaterial store the pointers;
   - the RTT atlas `_rt_Shadows`, 1024² ARGB8888 (261);
   - the depth targets `_rt_ShadowDepthTexture_%d` and the dummy colour
     target `_rt_ShadowDummy` (1398, 1410-1418). SetFlashlightDepthTexture
     and UpdateShadowDepthTexture take the pointers;
   - the flashlight's spotlight textures (shell_flashlight_client.cpp:54-57).
     FlashlightState_t carries the ITexture pointer by layout.
2. **The two material proxies.** The material system calls OnBind for each
   bound RTT shadow decal. The proxy reads the atlas pointer and the
   engine's own `ShadowInfo_t` for the shadow (GetInfo: matrix, tex origin
   and size, falloff), and writes six material vars (4267-4276, 4351-4369).
   It makes no decision.
3. **FlashlightState_t,** which `shadowmgr->UpdateFlashlightState` takes by
   layout. Today shell_flashlight_client.cpp:59-81 (StateFromWire) builds it.
4. **The engine callbacks:** the ISpatialLeafEnumerator collector for
   EnumerateLeavesAlongRay (2322) and EnumerateLeavesInBox (2662), and
   ShadowRestoreFunc (1317).
5. **The engine calls themselves.** These are IShadowMgr, staticpropmgr,
   modelrender/render shadow draws, IMatRenderContext,
   `IClientRenderable::CreateModelInstance` and `MarkShadowDirty`. The engine
   holds the renderable pointers passed to AddFlashlightRenderable and
   AddShadowToStaticProp, and the model instances passed to AddShadowToModel.
   Native carries these calls out. Managed decides them.

### The decision families (to managed), with the stock functions

| # | family | functions (fork lines) |
|---|---|---|
| D1 | **Which shadows exist, and their type.** Which entities cast, and their flags, is already ClientRenderPolicy.Shadow's; the manager adds the brush flag, the material and proxy-data choice and the create flags. Plus the type collapse (RTT → SIMPLE while RTT is off) and retirement. | CreateShadow 1923-1941, CreateProjectedTexture 1842-1903, GetActualShadowCastType 2262-2286, DestroyShadow 1983-1991, LevelShutdownPostEntity 1583-1604 |
| D2 | **Modes and global state:** the low-end check, the RTT and depth on/off switches and their re-init, the ambient shadow colour, direction, distance, blobby cut-off, RestoreRenderState, the five console commands. | Init 1325-1361, PreRender 2918-2974, Init/Shutdown RTT 1458-1519, SetShadowColor/LevelInitPreEntity 1525-1577, RestoreRenderState 1610-1621, UpdateAllShadows and the setters 1675-1771, commands 1224-1315 |
| D3 | **Update scheduling:** the dirty set (sorted by handle, an RB tree), forced re-projection, the transparent (falloff 255) shadows kept dirty, the moved test, the shadow-parent hierarchy, retiring a shadow whose renderable is gone. | AddToDirtyShadowList ×2 3027-3086, MarkRenderToTextureShadowDirty 3092-3112, PreRender 2976-2996, UpdateShadow 3118-3205, UpdateProjectedTexture(Internal) 3211-3257, RemoveShadowFromDirtyList 1964-1977, WillParentRenderBlobbyShadow / ShouldUseParentShadow / GetParentShadowEntity 2862-2903, 3003-3021 |
| D4 | **Projection math:** the blob (ortho) and RTT shadow bases, sizes, origin, falloff, texel snapping, clip planes, hierarchical bounds, the leaf query's ray, per-renderable distance and direction overrides. | BuildOrthoShadow 2329-2428, BuildRenderToTextureShadow 2509-2610, ComputeLocalShadowOrigin 2065-2114, SortAbsVectorComponents 2120-2149, the matrix builders 1997-2060, 2155-2194, ComputeExtraClipPlanes 2214-2259, AddChildBounds / ComputeHierarchicalBounds 2743-2792, UpdateStudio/BrushShadow 2798-2845, BuildShadowLeafList 2308-2323, GetShadowDistance/Direction(r) 1723-1742, ComputeBoundingSphere 3263-3286, ComputeShadowBBox 3292-3314 |
| D5 | **The flashlight:** creation flags (depth texture or not), the perspective matrix, light-world versus models, the leaf box, the version2 arm, target and light-world setters. | CreateFlashlight 1905-1921, UpdateFlashlightState 1947-1954, BuildFlashlight 2666-2737, BuildFlashlightLeafList 2656-2663, SetFlashlightTarget / SetFlashlightLightWorld / IsFlashlightTarget 4180-4231 |
| D6 | **Receivers:** not onto itself; ShouldReceiveProjectedTextures; CullReceiver's frustum (flashlight) or shadow-volume sphere, then the separating plane; the per-kind engine call. | CullReceiver 3335-3471, ComputeSeparatingPlane 3320-3329, AddShadowToReceiver 3477-3568, RemoveAllShadowsFromReceiver 3574-3607 |
| D7 | **RTT selection, LOD and the atlas:** the visible list (EnumShadow's five filters, screen area, the selection sort), the r_shadows / r_shadows_gamecontrol gates, the r_shadowmaxrendered budget and the LOD fallback, the redraw rule (dirty unless animating), texture size from caster size, and CTextureAllocator, a 64-block LRU atlas whose UseTexture picks a fragment power from screen area. | CVisibleShadowList 1020-1205, ComputeShadowTextures 4041-4148, DrawRenderToTextureShadow(LOD) 3776-3870, DrawShadowHierarchy 3693-3747, SetupRenderToTextureShadow 1627-1669, SetRenderToTextureShadowTexCoords 3613-3629, CTextureAllocator 138-688 (UseTexture 523-626), AdvanceFrame 3876-3880 |
| D8 | **Flashlight depth textures:** the allocation (count 1, or 4 under -tools; the resolution clamp written back to r_flashlightdepthres), which flashlights get one (frustum cull, overflow), the shadow-depth view's setup, lock and unlock. | InitDepthTextureShadows / Shutdown 1378-1453, BuildActiveShadowDepthList 3886-3934, ComputeShadowDepthTextures 3964-4033, Lock/UnlockAllShadowDepthTextures 4153-4178, SetViewFlashlightState 3940-3958 |
| D9 | **Debug:** cl_drawshadowtexture's overlay and boxes, r_flashlightdrawfrustum, cs_cl_shadow_report. | RenderShadowTexture 1802-1836, DrawRenderToTextureDebugInfo 2434-2500, DebugDrawFrustum 2612-2650, PrintShadowReport 4398-4484 |

**Dead in stock itself, not ported (ruled here, §8):**
- the threaded set-up arm (BuildSetupListForRenderToTextureShadow and
  BuildSetupShadowHierarchy, 3635-3771, and 4094-4114): `m_bThreaded = false`
  is hard-coded at 4048;
- the `IsX360()` arms.

**Ported although unreached** (the "port dead stock arms" rule):
- RTT (off by default);
- `r_flashlight_version2`;
- the flashlight-target arm. It has no caller now, so its tail is ported
  from the pristine file.

**Found, not this TU's:** C_StandaloneEntity answers
ShouldReceiveProjectedTextures with `false` for every entity
(shell_standalone_client.h:563). Stock's rule (the fork's c_baseentity.cpp
1458-1481) has an entity receive the flashlight unless it is NODRAW or
invisible, and a non-studio entity receive blob shadows. Today the
flashlight lights only the world and the engine's static props. See Q2.

## 4. The design

Under the four rulings:
- No logic stays native just because the engine calls a vtable.
- Batched, length-prefixed buffers.
- Oracle first.
- Measure after the move.

**The manager is managed:** SourceSharp.Client.Render.Shadows, DI only, no
statics, an IReloadParticipant like LeafRegistry.
- It holds stock's state:
  - the shadow list, as CUtlLinkedList's handle order;
  - the dirty RB tree's in-order walk;
  - the transparent list;
  - the atlas allocator;
  - the depth-texture locks;
  - the mode flags.
- It makes D1-D9.
- LeafRegistry calls it directly, with no crossing:
  - AddShadowToReceiver and RemoveAllShadowsFromReceiver (today's LeafOpKind
    1 and 2);
  - EnumShadow (op 8), which becomes a callback in the same process.
- The manager calls LeafRegistry.AddShadow, RemoveShadow, ProjectShadow and
  EnumerateShadowsInLeaves directly.

**Crossings, one per buffer:**

- **Native → managed:** a new `IManagedShadowsClient` (rows in
  docs/managed-interfaces.tsv, generated by tools/managed-areas). The slots
  are a sketch; S1 fixes them:
  - Init, Shutdown, LevelInitPreEntity and LevelShutdownPostEntity, from the
    client-system row;
  - PreRender(pending, ops) and AdvanceFrame;
  - ComputeShadowTextures(leaves, ops) and ComputeShadowDepthTextures(view,
    ops);
  - UnlockAllShadowDepthTextures(ops) and RestoreRenderState;
  - TakeOps.

  Each slot answers an op buffer, as the leaf system's slots do. `pending`
  carries what the native shells queued since the last call, as L3's queue
  does (c7_leaf_system.md, "L3, batching"): AddToDirtyShadowList and the RTT
  dirty mark from OnPositionChanged, three int32 an entry, collapsed per
  entity. That keeps a moving entity at zero crossings of its own.
- **Managed → native:** a HAND table `ShadowNative` in libss_fwd_client
  (tools/engine-forwarders' HAND list, as `LeafSystem` is):
  - **Execute(ops):** IShadowMgr, staticpropmgr, CreateModelInstance and
    MarkShadowDirty, the render-context state of the RTT pass, the model and
    brush shadow draws, UpdateShadowDepthTexture, the depth bias, and
    SetFlashlightDepthTexture. It also takes UpdateFlashlightState with
    managed's SourceSharpFlashlightState, made into a FlashlightState_t
    there. Its `if`s are bounds checks, null guards and the switch over the
    op kind.
  - **Facts(renderables, what):** in one batch, the renderable answers the
    manager reads: origin, angles, model, model type, shadow render bounds,
    world bounds, the cast distance and direction overrides, the shadow
    parent / child / peer walk as handles, ShouldReceiveProjectedTextures
    and the model instance. It also reads the engine's GetInfo
    (falloff bias, max distance) and GetFlashlightFrustum per shadow handle.
  - **Leaves(rays | boxes):** EnumerateLeavesAlongRay and InBox, with
    native's collector. The leaf forwarder's SSFwd_LeafSystem_EnumerateLeaves
    (fwd_leaf_system.cpp:668) is the InBox precedent.
  - **ViewQueries(boxes, spheres):** engine->CullBox and
    ComputePixelDiameterOfSphere for the visible list. That is one call per
    world view with RTT on.
  - **Resources:** materials, the atlas, depth targets with their actual
    width, flashlight textures, by slot number.

**The native residue, named:**
1. `src/fwd/native/fwd_shadows.cpp` (libss_fwd_client): ShadowNative's
   bodies above, with an env of client.so addresses as fwd_leaf_system.h's.
2. `src/client/native/shell_shadows_client.cpp` (client.so):
   - the resource holders (§3 item 1, absorbing shell_flashlight_client.cpp's
     texture table, a global table behind free functions);
   - the two proxies, registered by name through client.so's CreateInterface
     as shell_material_proxy_client.cpp's are. OnBind reads the atlas and
     `shadowmgr->GetInfo` for the engine handle kept beside the client
     handle. Native gets no decision, and there is no crossing per bind. Q1
     asks whether this is the shape the user wants;
   - ShadowRestoreFunc, which forwards to managed's RestoreRenderState;
   - the client-system adapter row, because shell_clientsystems' walk
     dynamic_casts, as the leaf system's row does;
   - the pending queue the shells write.
3. Nothing else. g_pClientShadowMgr and IClientShadowMgr go.

**Retired or deleted:**
- **Retired (the slots only append):** the leaf forwarder's AddShadow,
  RemoveShadow, ProjectShadow, ProjectFlashlight and
  EnumerateShadowsInLeaves, with their five IManagedLeafSystemClient rows
  (managed-interfaces.tsv:385-389), and LeafOpKind 1, 2 and 8.
- **Removed:** `ppShadowMgr` from the leaf env (fwd_leaf_system.h:40,
  shell_leaf_system_client.cpp:86).
- **Retired:** IShimFlashlightClient's Create, Update, Project and Destroy.
  FlashlightEffect calls the managed manager instead, and
  SupportsBorderColor stays.
- **Removed from C_StandaloneEntity:** m_ShadowHandle, m_bShadowDirty,
  RefreshShadow and DestroyShadow. The policy apply that called them is
  managed, so it creates and destroys the shadow itself.
- **Moved into the executor:** the render trace's five vtable patches move
  to the executor's op cases, which print the same `shadow.*` lines and the
  `~` depth, so the frozen recordings still read. RenderPlanText.cs:206-219
  and 312 name those lines.

**ConVars and commands:**
- The 10 ConVars and the 7 commands move to managed, as LeafListConVars.cs
  did.
- The engine and the material system read five of the ConVars by name (§1).
  The S2 witness checks the frame's `cvar` lines against the base.
- `cl_drawshadowtexture` stays shell_viewrender_client.cpp:658's. The
  manager reads it by name (RenderCvar.cl_drawshadowtexture exists).

## 5. Phases

The leaf system ran decisions first (L1), over a layout mirror of the stock
registry, then the registry (L2), which deleted the mirror and the gather
(c7_leaf_system.md, "For L2"). Here every decision family reads the
manager's private state: 86 sites read `m_Shadows[`, `shadow.m_Flags` or
`m_Flags &`. A decisions-only phase would need the same mirror over
CClientShadowMgr's private members, in a fork TU that is not ours to edit,
and S2 would throw it away. So the manager moves whole, behind the golden,
and the switch is its own phase (ruled here, §8).

### S0, the oracle and the base witness

- **The harness:** `src/test/oracle/native/shadowmgr_oracle_test.cpp`
  compiles the pristine clientshadowmgr.cpp from the valve-sdk.lock clone,
  copied into its own directory, as leafsystem_oracle_test does
  (src/test/oracle/native/CMakeLists.txt:846-868). The stand-ins live in
  `shadowmgr_stub/`:
  - a recording IShadowMgr;
  - staticpropmgr, modelrender and render;
  - a render context that records state calls and answers
    ComputePixelDiameterOfSphere;
  - materials that allocate targets and answer their actual width and
    SupportsShadowDepthTextures;
  - engine (CullBox, GetAmbientLightColor, a scripted BSP query);
  - view (UpdateShadowDepthTexture records the view);
  - a scripted IClientLeafSystem;
  - scripted renderables (origin, angles, bounds, model type, shadow
    hierarchy, receipt).

  `GetBaseEntity()` answers NULL, which is what the fork's four deleted arms
  answered.
- **The golden:** `src/test/client/Fixtures/shadowmgr-stock.tsv`, with a
  README. It holds every engine call in order, with floats as bits, and
  every question asked, with its stimulus, so the C# replay rebuilds the
  scenario as leafsystem-stock.tsv's does. The scenario runs each mode:
  - r_shadowrendertotexture 0 and 1;
  - a depth texture on and off, with the resolution changed;
  - a small r_shadowmaxrendered, to force LOD;
  - a restore;
  - a level shutdown with shadows left.
- **Facts:**
  - the golden;
  - **scenario reach:** every arm of D1-D8 fires at least once, each count
    non-zero and held to the stock row it caused;
  - the pristine C_BaseEntity arms fire 0 times, so the golden is also the
    fork's behaviour;
  - the farm's TU differs from the pristine only by §1's edit families.
- **The base witness, recorded at a001d7a31's descendant before S1:**
  - Add IShadowMgr and the staticprop shadow methods to the render
    recorder's hooks (a new `shadowmgr.` line family). It is new lines only;
    the frozen files are inputs and are not re-recorded.
  - A session `tools/probes/sessions/c7-shadow-ab.steps`, after
    c7-leaf-ab.steps: dm_lockdown poses 1-3. The stimulus is `r_shadowdir
    0.1 0.1 -1` before each traced frame, so that every shadow re-projects
    in it. It runs with r_shadowrendertotexture 0 and 1, the flashlight on
    (impulse 100), and a second map load.
  - `cs_cl_shadow_report` counts at each pose.
  - cs_cl_crossings per frame at each pose. This is S3's baseline.

### S1, the manager in C#

- **Lands** `src/client/managed/Render/Shadows/`: the registry,
  scheduler, projection, receivers, visible list, atlas allocator, depth
  textures and debug (D1-D9), and ShadowsAbi.cs in src/client/interop. It is
  not wired: the stock manager still runs.
- **Facts:**
  - ShadowManagerOracleTests replays the golden bit-exact, kind by kind
    between anchor rows, with one fact doing a hot reload after every call,
    as LeafRegistryOracleTests does;
  - one fact per family on synthetic inputs where the golden is thin;
  - mutation proofs in the leaf plan's table shape, at least these:
    - the dirty walk out of order;
    - the transparent shadows not re-dirtied;
    - LRU order;
    - the fragment-power overflow;
    - the LOD fallback past the budget;
    - the separating-plane side;
    - the animating-source redraw.
- **Live witness:** none (not wired).
- **Ratchets:** none native.

### S2, the switch, then the deletion and the eviction (separate commits)

- **The switch commit:**
  - ShadowNative and shell_shadows_client.cpp;
  - the IManagedShadowsClient rows (run tools/managed-areas --write and
    tools/engine-forwarders --write from the farm);
  - the executor's six op cases calling managed;
  - FlashlightEffect, ClientWorldState and the policy apply calling the
    manager;
  - LeafRegistry calling it directly;
  - the shells queueing;
  - the render trace lines moved.
- **The deletion commit:**
  - the leaf slots retired;
  - IShimFlashlightClient's four slots retired;
  - C_StandaloneEntity's shadow members;
  - shell_flashlight_client.cpp's manager calls.
  - shell_renderable_facts.cpp and renderable_facts.h, whose only importer
    is this TU (`nm -A -u` over the client's 141 link-edge objects), with
    renderable_subject_test and leaf_cycle_test's needles;
  - shell_tools_off_client.cpp, its test, its docs/shim-reasons.md row, and
    the toolframework header links (docs/todo.md:39-48);
  - check mathlib_port's R_CullBox and CalculateAABBFromProjectionMatrix,
    and collisionutils' ComputeSeparatingPlane. Their only client importer is
    this TU (same nm), so `make debug GC_SECTIONS=0` says whether they stay.
- **The eviction commit:**
  - clientshadowmgr.cpp leaves the client CMake list and
    sdk-links/client.txt:58, and iclientshadowmgr.h leaves :87 if
    `make sdk-census` agrees;
  - a docs/client_evictions.md row;
  - Valve headers and docs/valve-header-new.tsv rows for the new files.
- **Facts:**
  - a gtest per ShadowNative op kind and query over a fake, as
    fwd_leaf_system_test does;
  - the proxies' OnBind writes what GetInfo answered;
  - the queue's collapse, overflow and hand-over;
  - S1's facts stay green.
- **Live witness:** c7-shadow-ab.steps at the tip against S0's base:
  - every `shadowmgr.` line and every world view's `scenebytes` is
    byte-equal at each pose and after the reload, with RTT 0 and 1 and the
    flashlight on;
  - `cs_cl_shadow_report` counts are equal;
  - screenshots show the blobs and the flashlight;
  - 0 `EXCEPTION IN` lines;
  - `make debug GC_SECTIONS=0` and nogc-relink are green;
  - `make test-full` passes before the ff (census; memory
    test-full-before-link-landing).
- **Ratchets moved:**
  - client-novalve-ratchet `fork-src` 7 → 6 (0 with the six-TU lane),
    plus `fork-hdr-*`, `sdk-links`, `logic-lines` and
    `native-convar-decls`, all re-measured;
  - entity-census client cross-edges 25/7 → 24/6;
  - the globals-client ledger loses 31 rows (`make globals-ledger`, and
    `client_globals_link_level`);
  - the convar ledger's 2 rows;
  - shim-reasons and the shim ratchet for the shells touched;
  - client_c7_evicted_test's list.

### S3, measure (dm_lockdown, c0-session, debug, against S0's base)

- **What it measures:**
  - inbound crossings per frame (cs_cl_crossings) at poses 1 and 3 and
    after the reload, flashlight off and on, RTT 0 and 1;
  - the manager's own µs per frame;
  - the batch sizes (a cs_cl_shadow_stats in LeafSystemStats' shape).
- **Expected direction, to be measured, not quoted:**
  - plus the plan's shadow ops, about 4-6 a frame: AdvanceFrame,
    PreRender 2-3 (the dm_lockdown recordings show 2), ComputeShadowTextures
    per world view, and depth/unlock;
  - minus one per re-projected shadow (ProjectShadow);
  - minus one a frame with the flashlight on (ProjectFlashlight; Update and
    Project become in-process).
- **If the budget is blown,** batch (the port-budget ruling):
  - fold the ops that touch only managed state into the plan's build;
  - skip ComputeShadowTextures' call when the plan's inputs say RTT is off,
    a flag in the op;
  - flatten the shadow-depth views into the frame's plan instead of the
    nested RenderShadowDepthPlan crossing.

## 6. Interactions

- **The flashlight.**
  - FlashlightEffect.cs (Create / Update / Project / Destroy through
    IFlashlightSlots, FlashlightSlots.cs) calls the manager in-process.
    FlashlightSettings.cs:168-169 reads r_flashlightdepthtexture and
    r_flashlightdepthres by name. It can take the manager's ConVars by
    injection once they are managed's.
  - flashlight-stock.tsv already holds the flashlight→manager edge
    (flashlight_harness.h:175-177).
  - docs/todo.md:1535-1541, the reload that forgets the handle, closes when
    the manager's reload carry holds the flashlight's shadow. S1 adds the
    fact.
- **The leaf system.**
  - The managed LeafRegistry keeps the shadows-in-leaf buckets and decision
    4 (its Receives batch).
  - The registry's m_ShadowEnum quirk (c7_leaf_system.md) stays as it is.
  - leafsystem-stock.tsv and shadowmgr-stock.tsv meet at the
    AddShadowToReceiver / EnumShadow edge. The S1 replay holds both.
- **The render plan and the view executor.**
  - Ops 4, 11, 12, 13, 32 and 114 keep their kinds and their RenderPlanText
    lines, so RenderPlanFixtureTests and the frozen recordings are untouched.
    Only the executor's bodies change.
  - The shadow-depth view stays a nested plan (RenderPlanner.cs:75,
    PlanShadowDepthView; RenderTarget.ShadowColor/ShadowDepth,
    RenderPlanAbi.cs:851-853) until S3 measures it.
  - The RTT pass's render-context calls become Execute ops at the point of
    the WorldViewShadowTextures op.
- **The six small render TUs (the other lane):**
  - **No symbol dependency in either direction** (nm, above).
  - rendertexture.cpp's targets are not the manager's, which allocates its
    own.
  - **Shared files:** both lanes touch the client CMake list,
    sdk-links/client.txt, client_evictions.md, client-novalve-ratchet's
    `fork-src`, the entity census, the globals-client ledger and probably
    shell_view_executor_client.cpp. Land them one after the other and
    re-measure the ratchets at the second landing.
- **docs/todo.md:**
  - 39-48, the tools-off stand-ins: delete them in S2;
  - 1535-1541, the flashlight's reload: closes in S1/S2;
  - 1586-1588 names the fork's threaded leaf/shadow branches as hard-wired
    off. The shadow branch is §3's dead threaded arm. It is not ported, so
    the note's shadow half goes with the TU.

## 7. Open questions (only where the answer changes what is built)

- **Q1. The two material proxies.** The plan keeps them as a native class
  that reads the atlas and the engine's GetInfo, with no managed table and
  no decision. That costs no crossing per bound shadow decal.
  - The alternative is the shape 2c reading: route them through
    CManagedMaterialProxy (shell_material_proxy_client.cpp, MaterialProxyHost)
    like the other 46 proxies. That is one crossing per bind, per RTT shadow
    per frame, which the batching ruling refuses.
  - Recommended: native data-only.
- **Q2. Shadow and flashlight receipt on managed entities.**
  C_StandaloneEntity::ShouldReceiveProjectedTextures is `false`
  (shell_standalone_client.h:563). Port stock's rule (c_baseentity.cpp
  1458-1481) into ClientRenderPolicy in S1, so the flashlight lights props
  and players and brush entities take blob shadows? That changes what
  players see.
  - Recommended: port it in S1, live-checked in S2. The A/B against the base
    then differs by design in the receiver lines only.
  - The alternative is to keep `false` and file it in docs/todo.md.

## 8. Rulings made here (2026-10-19, the survey lane)

- The manager moves whole in S1 (decisions and state). There is no S1
  layout mirror, for §5's reason.
- No libss_fwd class for IClientShadowMgr. All its callers are ours (§2),
  and they are re-pointed.
- The threaded set-up arm and the X360 arms are not ported. Both are dead in
  stock itself (4048; IsX360). Everything else is ported, reached or not.
  The ConVar `r_threaded_client_shadow_manager` stays registered: it is in
  the recordings' `cvar` lines.
- The S0 witness adds a `shadowmgr.` hook family to the render recorder at
  the base, so base and tip record the same lines.

## Decisions

- 2026-10-19 (user, Q1: "move to c#"): the Shadow and ShadowModel proxies
  move to C#. They go through the managed proxy host (CManagedMaterialProxy /
  MaterialProxyHost, shell_material_proxy_client.cpp) like the other 46, so
  OnBind is managed and native keeps only their registration by name. The
  per-bind crossing (one per bound RTT shadow decal) is accepted under the
  port-budget ruling; S3 measures it, and the perf pass batches it if needed.
  RTT is off by default, so default play binds none. shell_shadows_client.cpp
  loses the proxies (§4's residue item 2).
- 2026-10-19 (user, Q2: "match stock"): S1 ports stock's receive rule
  (c_baseentity.cpp 1458-1481) into ClientRenderPolicy. The flashlight lights
  props and players unless NODRAW or invisible, and non-studio entities take
  blob shadows. S2's A/B against the base differs by design in the receiver
  lines only, and the live check shows a flashlit prop.
