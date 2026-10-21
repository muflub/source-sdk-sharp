# C7 cluster 12: clientshadowmgr.cpp, a survey and a plan

Status: survey done 2026-10-19 at main a001d7a31 (worktree
agent-a1c5c1e9284f43072, `make setup ROOT=../source-sdk-2013` and
`make debug`). Reviewed 2026-10-19: Q1 and Q2 are ruled (Decisions). S0 and S1 are built,
unwired ("S0 and S1 as built", 2026-10-19); S2 is not started. The leaf system's plan
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

## S0 and S1 as built (2026-10-19, lane worktree-agent-a4805d7500e632d68, base 01c4ea691)

Commits, in order: ea24c250c (the recorder's `shadowmgr.` family),
f396a4243 (the witness session), 07738583a (the oracle and its golden),
8e5f8f8fa (the manager in C#), d926f2394 (the golden's layout rows and a
handle dirtied twice), a55145ba2 (Q2's receive rule and the thin families'
facts), and this section. Nothing is wired: the stock manager still runs.

### S0: the oracle

- src/test/oracle/native/shadowmgr_oracle_test.cpp compiles the pristine
  clientshadowmgr.cpp (with the pristine iclientshadowmgr.h, igamesystem.h,
  iclientunknown.h, iclientrenderable.h, toolframework/itoolentity.h and
  public/collisionutils.cpp, each copied beside it) over stand-ins in
  shadowmgr_stub/ (shadowmgr_harness.h says what is stock and what is
  stand-in). The engine's IShadowMgr is a whole stand-in that keeps what it
  is told; the leaf system offers each projection to the receivers
  registered in its leaves and enumerates the shadows projected into them;
  every renderable's GetBaseEntity() is NULL.
- The golden is src/test/client/Fixtures/shadowmgr-stock.tsv (3.5 MB, README
  beside it): every E (effect), Q (question and answer), R (answer) and M
  (console) row stock wrote, floats as bits, after the stimulus rows (T, ENV,
  CV, F, RCV, C) the replay rebuilds the world from, and L rows with the
  engine answers' layouts. The scenario: a low-end lifetime without depth
  textures, a -tools lifetime with render-to-texture on, and the game: blobs
  on the three ortho axes, a brush model, overrides, no model, a transparent
  shadow, a retired one, a sprite, an aiment, rings of receivers;
  render-to-texture over a five-child hierarchy, seven big casters and a
  huge one (the 256s overflow), a budget of two for two frames, a caster
  grown across sizes, a fragment stolen from a caster that left the view, a
  crowd of 257 that exhausts the 16s; four flashlights (two depth ones in
  view, one out of it, one without shadows), version2, light-world and
  models off, the depth resolution raised past the card, off, on and
  unsupported, 1025 depth flashlights in one view; the proxies; a level
  shutdown with shadows left and render-to-texture on.
- Facts (4): StockProducesTheGolden; TheScenarioReachesEveryArm (177 arms of
  D1-D9 and the proxies, every count non-zero, each named from the row stock
  wrote or from a probe of the subject's own state taken just before it
  decided, and held to the rows stock wrote next; the probes read the
  subject's members, which the oracle opens with `#define private public`
  over the subject's text alone); TheBaseEntityArmsFireZeroTimes (asked and
  told NULL, never touched); TheFarmDiffersFromThePristineOnlyByTheEditFamilies
  (a Myers diff of the farm's TU against the pristine, every hunk named by a
  family, with a planted edit caught).
- **Two corrections to §1, measured:** the pristine file resolves a
  renderable through `ClientEntityList()` at 12 sites, each respelled (not
  14; the 13th ClientEntityList() call is the clip plane's
  GetBaseEntityFromHandle, which went with that family); and the fork has a
  seventh edit family §1 does not list: DrawRenderToTextureDebugInfo's entity
  index comes from the renderable's networkable, not its C_BaseEntity. It
  moves only cl_drawshadowtexture's text; the managed port prints the fork's.
- Reach counts (the fact's table, debug build): d1.create.blob 29,
  d1.create.rtt 259, d1.create.brushflag 2, d1.create.norenderable 1,
  d1.flashlight.depth 1028, d1.flashlight.nodepth 1, d1.retire 1,
  d1.type.rttcollapsed 17; d2.init.lowend 1, d2.init.rtt 1,
  d2.init.depthunsupported 1, d2.prerender.depthresize 1, d2.prerender.rtton
  2, d2.prerender.rttoff 1, d2.levelinit.clamped 2, d2.restore 1, each
  console command printing 1 and setting 1-2; d3.walk.handleorder 2,
  d3.walk.stopsatretire 1, d3.transparent.keptdirty 1, d3.child.disabled 16,
  d3.update.moved 362, d3.update.notmoved 4, d3.dirty.ignoredwhileupdating
  1; d4.ortho 58 (axes 0/1/2: 3/2/4), d4.rtt 304, d4.bounds.childrenadded 9;
  d5.version2 1026, d5.lightworld 7, d5.lightworld.off 1, d5.models.off 1;
  d6.shadow.boxmiss 165, d6.plane.centers 106, d6.plane.separating 120,
  d6.plane.none 67, d6.side.pos.culled 6, d6.side.pos.kept 108,
  d6.side.neg.culled 42, d6.side.neg.kept 70, each receiver kind 4-181;
  d7.enum.accepted 391, d7.budget.lod 16, d7.lod.back.texcoordsonly 5,
  d7.alloc.keepnear 1, d7.alloc.keepold 30, d7.alloc.lower 4,
  d7.alloc.none 1, d7.alloc.steal 1, d7.redraw.animating 5; d8.active.overflow
  1, d8.lock.failed 10, d8.viewstate.v2.one 2; the proxies 1-3 each.
- **Unreachable in stock, so no arm:** DrawRenderToTextureShadow's "Didn't
  draw shadow hierarchy" (DrawShadowHierarchy always draws a non-NULL root,
  and a NULL one dereferences first); BuildOrthoShadow's Vector2DMax to 10
  (the bloat just before it makes every size at least 10); the threaded arm
  and the X360 arms (§8).
- **Found in stock** (README): a shadow retired during PreRender's walk (its
  entity gone) frees its node under the walk, NextInorder of a freed node is
  no node, and the walk ends; RemoveAll then drops the shadows after it in
  handle order, unprojected, and their renderables keep the dirty bit, so
  AddToDirtyShadowList( renderable ) refuses them from then on. The dirty set
  is a multiset (CUtlRBTree::Insert does not refuse a key it holds), and the
  walk visits each copy. SetViewFlashlightState hands
  SetFlashlightRenderState the client handle.

### S0: the base witness

- The recorder's `shadowmgr.` family (ea24c250c) hooks the engine
  IShadowMgr's 21 client calls and staticpropmgr's two shadow calls, armed
  only with SOURCESHARP_RENDER_TRACE_SHADOWS=1, so a default trace and the
  frozen recordings are untouched. It moved the shim ratchet (docs/shim-ratchet.tsv
  shim_lines 129214 → 129581, RAISED), docs/shim-reasons.md's (a) row 33,562
  → 33,929 (total 87,100 → 87,467) and the recorder's shim-scope fnmap and
  rulings (28 INSTRUMENT rows).
- tools/probes/sessions/c7-shadow-ab.steps (f396a4243): dm_lockdown poses 1-3
  traced with r_shadowrendertotexture 0 and 1, the flashlight (impulse 100)
  at pose 1 in each, a second map load, cs_cl_shadow_report around each
  traced frame and a 10 s cs_cl_crossings window at each pose. Run at the
  base in the lane's own instance (port 27463, its own TMPDIR, DEBUG_API_DIR
  and DEBUG_API_UI_PORT 18463): 35 of 35 traces, 0 `EXCEPTION IN`, the join
  confirmed. The recordings are kept for S2 in the lane's scratch
  (/tmp/claude-1000/-home-lodle-git-source-sdk-sharp/d73ee15a-fd4b-4257-815b-3d15f4c86325/scratchpad/sh-witness/base/:
  the 35 traces, console.log, sh-counts.tsv, sh-summary.tsv; /tmp, so
  S2 re-records the base if they are gone).
- **The brief's stimulus re-projects nothing at this base.** `r_shadowdir 0.1
  0.1 -1` and `r_shadowdist 50` dirty every shadow, but every shadow is
  already dirty every frame here, and a dirty one is re-projected only if
  its origin or angles moved; a control frame traced just before the
  stimulated one is the same value for value in both modes. So the session
  adds what does reach the engine: a crate dropped in view at pose 1
  (ProjectShadow, and with RTT 1 the clip planes and texcoords), the switch
  to RTT and back (SetShadowMaterial, SetShadowTexCoord,
  ClearExtraClipPlanes), and the flashlight frames. A trace is one
  View_Render, so CreateShadowEx, DestroyShadow, UpdateFlashlightState and
  ProjectFlashlight (entity create and delete, player simulation) never
  fall inside one; no receiver call appeared in any traced frame.
- What it shows (shadowmgr. lines per traced frame): pose 1 130 (RTT 0: 65
  GetInfo, 65 EnableShadow) and 175 (RTT 1: 110 GetInfo, 65 EnableShadow);
  pose 2 208 and 261; pose 3 208 and 288; the flashlight adds
  GetFlashlightState 2 and SetFlashlightDepthTexture 1; the RTT switch 522
  lines. cs_cl_shadow_report (held/rtt/simple/depth/lost/texturesDrawn):
  pose 1 65/0/65/0/0/0 with RTT 0, 65/65/0/0/0/28 with RTT 1; poses 2-3 104
  held; the flashlight adds a depth shadow with no renderable. Inbound
  crossings per client frame (S3's baseline; total over client frames):

  | pose | RTT 0 | RTT 1 |
  |---|---|---|
  | 1 | 65.46 | 66.47 |
  | 1, flashlight on | 72.58 | 73.54 |
  | 2 | 70.47 | 71.55 |
  | 3 | 86.47 | 88.51 |
  | 1 after the reload | 65.46 | 66.47 |

### S1: the manager in C#

- **The seams, call-shaped (ruled here, below).** SourceSharp.Client.Interface's
  ShadowSeams.cs: IShadowEngine (the engine's IShadowMgr and the static prop
  manager's two shadow calls), IShadowMaterials (the three materials, the
  render targets, the restore callback, the depth formats and support, the
  DX level and -tools), IShadowRenderContext (the render context, the model
  and brush shadow draws, RenderShadowTexture's quad, the depth view),
  IShadowWorld (CullBox, the ambient light, the BSP leaves, the frame),
  IShadowRenderables (the renderables' shadow surface, by pointer) and
  IShadowDebugOverlay, with their data structs (ShadowMatrix,
  ShadowMatrix3x4, ShadowFrustum, ShadowEngineInfo, ShadowViewSetup,
  ShadowDepthView). The logic's own: IShadowSettings (the ConVars, S2 binds
  them to managed ConVars), IShadowConsole, IShadowLeafSystem with
  IShadowLeafEnumerator (S2: LeafRegistry). src/client/interop/ShadowsAbi.cs:
  the flag words (stock's public and private shadow flags, the create flags,
  ShadowType_t, ShadowReceiver_t) and the raw layouts of ShadowInfo_t,
  cplane_t and Frustum_t that S2's binding copies, held to the golden's L
  rows.
- **The manager** (src/client/managed/Render/Shadows, 8 files: the manager in
  six partial files by family, ShadowTextureAllocator, ShadowContainers and
  ShadowMath) is DI only, has no statics, and is an IReloadParticipant whose
  save carries every shadow, the dirty multiset, the transparent list, the
  atlas to the fragment, the depth textures and their locks, the modes and
  stock's warn-once counters. ShadowMath ports the mathlib and collisionutils
  functions term for term (QuaternionMatrix's double first column,
  MatrixBuildPerspective's double tangent, ConcatTransforms' SSE order with
  its masked +0.0f, RAD2DEG's float quotient, ComputeSeparatingPlane's bit
  comparisons).
- **The proxies (Q1):** MaterialProxies/ShadowProxies.cs, ShadowProxy and
  ShadowModelProxy over IShadowProxySource (the manager). Unregistered.
- **The receive rule (Q2):** ClientRenderPolicy.ReceivesProjectedTextures,
  unwired; ClientRenderFacts gains NoReceiveShadow, RenderAlpha and
  RenderMode.
- **Facts:** ShadowManagerOracleTests (12: row for row, kind by kind between
  the stimulus rows, a hot reload after every one of the scenario's 2,782
  calls, and one per family: shadows, modes, projections, receivers, the
  RTT pass, depth, dirty marks and console, proxies, debug), over 22,645
  compared rows; ShadowManagerFactsTests (9), ShadowProxiesTests (7, a theory's three among them),
  ClientReceivePolicyTests (6). Each fact was shown red against a throwing
  stub (the manager's, the allocator's and the proxies' constructors or
  Inits and the receive rule throwing): 31 of 34; the three that stayed green
  hold constants the stub does not replace (the layouts, the degree factors,
  the flag words) and are held red by mutations below.
- **docs/todo.md's flashlight reload** (the module forgets the handle): the
  manager's reload carries the flashlight's shadow
  (AHotReloadCarriesAFlashlightsShadow). The entry closes in S2, when
  LocalPlayerFlashlight's handle reaches this manager.

#### Mutation proofs (sh-mut.py: a cp snapshot, sha256 before, mutated and restored, a fresh mtime on restore; 34 facts)

| mutation | file | sha before → mutated | red |
|---|---|---|---|
| the dirty walk out of handle order (newest dirtied first) | ShadowManager.Scheduling.cs | 8bf4b78318e5 → 6c829835957b | 9 |
| the transparent shadows not re-dirtied | ShadowManager.Scheduling.cs | → 03684c01dc41 | 6 |
| the walk's end at a retired shadow "fixed" (the walk goes on) | ShadowManager.Scheduling.cs | → 0263d0db72f4 | 6 |
| LRU order: a used fragment to its list's head, not its tail | ShadowTextureAllocator.cs | 1969a27c716d → ce13c3dbd997 | 7 |
| the fragment-power overflow: no fall back to a smaller size | ShadowTextureAllocator.cs | → bbfa6c35807a | 7 |
| the LOD fallback past the budget: every shadow drawn | ShadowManager.RenderToTexture.cs | 14c20b675cad → 0ef7ff926174 | 4 |
| the animating source's redraw: its texture cleared like a still one's | ShadowManager.RenderToTexture.cs | → 27f379d39df0 | 4 |
| the separating plane's side: the shadow's direction ignored | ShadowManager.Receivers.cs | 612634edb430 → 1a0fc06018b7 | 4 |
| the side flipped for a shadow cast away from the plane | ShadowManager.Receivers.cs | → fbf2087acceb | 4 |
| the side flipped for a shadow cast toward the plane | ShadowManager.Receivers.cs | → 157ff3de3f76 | 4 |
| the dirty set refuses a handle it holds | ShadowContainers.cs | 1989efdee62d → df2c77af7c05 | 4 |
| RAD2DEG "fixed" to (float)(180 / M_PI) | ShadowMath.cs | 1c07bde3862e → b56457c95380 | 5 |
| the flashlight target's move children not walked | ShadowManager.Projection.cs | 2a4668e65c7d → edecc9f4cefd | 1 |
| the reload drops a flashlight's state | ShadowManager.Debug.cs | abb3d8a8c86e → 49a833786bf7 | 1 |
| the receive rule asks NODRAW after the flashlight arm | ClientRenderPolicy.cs | 6fd7956d37ea → 3b2e075735b6 | 1 |
| the model proxy's offset and scale swapped | ShadowProxies.cs | 699108de5d76 → 8c273e5d502e | 5 |

Each file was restored to its pre-mutation sha256. One planted change is
an equivalent mutant and turned nothing red: `receiverDot <= sourceDot` to
`<` on the toward side (612634edb430 → 67cd895a98a2), because a plane that
separates two boxes cannot have both centres project equally on its
normal, so the boundary is never met.

### Rulings made here (2026-10-19, the S0/S1 lane)

- **The seams are call-shaped, not op buffers.** §4 sketched ShadowNative as
  Execute(ops), Facts(renderables) and query batches. S1 has no native side,
  so the manager calls its seams one engine or renderable call at a time, in
  stock's order, which keeps the port a transliteration the golden can hold.
  S2's binding implements the seams over the HAND table: it queues what
  answers nothing into the Execute op buffer and crosses (after flushing the
  queue) for what answers; S3 measures it and batches the renderable
  questions if the budget asks.
- **A flashlight's state is the manager's copy.** BuildActiveShadowDepthList
  and ComputeShadowDepthTextures read the state the manager last handed the
  engine (stock asks the engine's GetFlashlightState, which answers the same
  state); no crossing. FlashlightState has no DrawShadowFrustum, so the
  per-light frustum overlay is not carried; r_flashlightdrawfrustum is.
- **The proxies' atlas is FindTexture( "_rt_Shadows" ), once per atlas the
  manager makes** (IShadowProxySource.AtlasPage), so a proxy holds one
  reference to each atlas; -1 (NULL) while there is none.

### Divergences (each held by a fact, or out of the golden's reach and named)

- DrawShadowHierarchy with a NULL root draws nothing (stock dereferences it).
- A shadow model proxy bound with a handle that names no shadow sets only its
  texture (stock reads past its list).
- A reused shadow slot starts zeroed (stock's CUtlLinkedList keeps the old
  occupant's POD members; no read of one before a write is in the golden).
- DebugDrawFrustum over a singular matrix draws from zeros (stock: from an
  uninitialised one).
- A duplicate dirty handle retired mid-walk: stock's Find may remove the
  other copy; the managed walk stops when the count of the visited handle
  drops (no such case in stock's scenario or the game).
- The console's %.2f is .NET's "F2", which rounds an exact binary tie away
  from zero where glibc rounds it to even; no printed value is a tie.
- The fork's report omits its resolve line (SourceSharp_RenderableFromHandle's
  native counters); S2 decides what the managed report prints there.
- The engine block carries no render mode, so the receive rule's
  "invisible blended entity" arm reads mode 0 until S2 gives it a source.

### Open, for S2

- Wire: the IManagedShadowsClient rows and the ShadowNative HAND table; the
  seams' binding; the ConVars and commands as managed ConVars; the proxies'
  registration by name; LeafRegistry implementing IShadowLeafSystem and
  calling AddShadowToReceiver directly; the receive rule into
  C_StandaloneEntity's answer (and the render mode into the block).
- The witness at the tip: c7-shadow-ab.steps against the recordings above
  (shadowmgr. lines, cs_cl_shadow_report counts, screenshots; Q2 changes the
  receiver lines by design).
- TheFarmDiffersFromThePristineOnlyByTheEditFamilies reads the farm's TU,
  which S2's eviction removes: retire that fact with it.

## S2 and S3 as built (2026-10-20, lane worktree-agent-a0609c0462febd219, base cd01c2887)

The commits, in order: the switch (11e885ed3), the queue's copy fix (645f7d8aa), the
queue's 512 entries (e88699d0b), the deletion (968201cec), and this section with the S3
session (tools/probes/sessions/c7-shadow-s3.steps). The lane rewrote its own history
twice before landing, to keep each commit's checks green (tags
archive/s2-lane-pre-squash, -2 and -3 hold the earlier shas); the measurements
below name the sha they ran at: c7c7a17c8 is the switch's first form (the
same sources, and an sdk-links list that dropped utlmultilist.h, which the
oracles still compile), ee23411a1 and f6c3f0b33 + the deletion are the
deletion's tree.

### What was built

- **ShadowNative is one HAND slot.** `SSFwd_Shadows_Run( env, records,
  answers )` in libss_fwd_client (src/fwd/native/fwd_shadows.cpp): a buffer of
  records `[kind, nPayload, payload...]`, kinds 1-49 effects (the engine's
  IShadowMgr and static-prop shadow calls, the render context, the material
  system, the debug overlay) and 64-96 questions (82 kinds in all). Effects are
  queued; a question sends the queue with it and is always the last record;
  ClientShadowSystem flushes when the outermost call into the manager returns.
  S1's call-shaped seams are ShadowNative.cs over it. fwd_shadows_test holds
  every kind over fakes (91 facts).
- **The environment is client.so's** (shell_shadows_client.cpp), served by
  name through client.so's internal factory (SourceSharpShadowEnv001) and handed
  to every run: the engine pointers, the reference tables for the shadow
  materials, render targets and the flashlight's spotlight textures, the
  restore callback, RenderableOf, the depth texture's view and
  cl_drawshadowtexture's quad.
- **IManagedShadowsClient** (12 rows, CrossingOps 394-405): Init, Shutdown,
  LevelInitPreEntity, LevelShutdownPostEntity, the five frame slots
  (AdvanceFrame, PreRender, ComputeShadowTextures, ComputeShadowDepthTextures,
  UnlockAllShadowDepthTextures), RestoreRenderState, RenderShadowTexture and
  Pending. kClientSystems[] keeps stock's CClientShadowMgr row
  (CShadowMgrGameSystem); the view executor's six shadow ops call the frame
  slots.
- **The shells' queue** (shadow_pending_queue.h): an entity's dirty mark,
  refresh (type and flags) and destroy, three int32 an entry; a second dirty
  mark ORs into the first, a destroy drops the entity's earlier entries, a full
  queue is handed over (Pending). The five frame slots carry it in their
  arguments, as a copy; any other slot hands it over first.
- **EntityShadows** holds an entity's shadow handle and dirty bit (stock's
  m_ShadowHandle and EFL_DIRTY_SHADOWUPDATE), keyed by entity and renderable,
  and answers the manager's ShadowHandle, IsShadowDirty and MarkShadowDirty of a
  renderable itself; C_StandaloneEntity's members went in the deletion.
- **In-process callers:** the leaf registry's receivers and EnumShadow
  (IShadowReceivers, IShadowLeafEnumerator), the flashlight (IFlashlightShadows,
  through FlashlightWorld), shadow_control (IShadowWorldControl), and the Shadow
  and ShadowModel proxies through the managed proxy host (IShadowProxySource,
  user ruling Q1).
- **The receive rule** (user ruling Q2): ClientRenderPolicy writes both
  answers into the block's ShadowFlags word (ClientShadowFlag.ReceivesShadows,
  1 << 29; ReceivesFlashlight, 1 << 30); C_StandaloneEntity's
  ShouldReceiveProjectedTextures reads the one the mask names; the refresh entry
  masks the flags to 16 bits.
- **The ConVars:** ShadowConVars.cs declares the five client.so ones of the C0
  snapshot (r_flashlightdepthres, r_flashlightdrawfrustum, r_flashlightmodels,
  r_shadowmaxrendered, r_threaded_client_shadow_manager) and the commands
  (r_shadowdir, r_shadowangles, r_shadowcolor, r_shadowdist,
  r_shadowblobbycutoff and cs_cl_shadow_report, r_shadowbreak in a debug build,
  and S3's cs_cl_shadow_stats); r_shadowrendertotexture,
  r_flashlightdepthtexture, r_shadows, r_shadows_gamecontrol and
  r_flashlight_version2 are the engine's and are read or set by name.
- **The trace:** the render trace's shadow lines are the executor's
  (RenderTrace::ShadowEnter/Leave/Note at shell_shadows_client's calls); the
  five IClientShadowMgr hooks went with the TU.

### Rulings made here (2026-10-19/20, the S2/S3 lane)

- R1, commit order: the switch carries clientshadowmgr.cpp out of the link,
  because its statics (ten ConVars, seven commands, the two proxy
  registrations) collide with the replacement's. The deletion and its
  bookkeeping followed in their own commits.
- R2, one HAND slot with records rather than a forwarder row per engine
  call: the manager makes about 1,200-2,900 native calls a frame at these
  poses, almost all questions.
- R3, the environment by name through client.so's own factory, so the
  forwarder library holds no client global.
- R4, the entity's handle and dirty bit are managed (EntityShadows); the
  shells queue rather than call.
- R5, the engine owns five of the manager's ConVars (the first live run
  refused the load, ConVarOwnership, while two were declared);
  ClientShadowSystemTests holds the declarations to the C0 snapshot.
- R6, Q2's storage is two bits above the SHADOW_FLAGS_* bits: no block
  layout change. The render mode is kRenderTransTexture for a blended entity,
  kRenderNormal otherwise (the block carries none).
- R7, the witness is compared by the stable set (canonical shadowmgr. lines
  both base runs hold), kind counts, scenebytes, the engine-read cvars and
  cs_cl_shadow_report, because two base runs differ (shadow handles, atlas
  texcoords, settling casters).
- R8, the queue rides in the frame slots' arguments (S3 at the switch's
  first build counted two Pending crossings a frame), and leaves as a copy.
- R9, the deletion follows the importers: `nm -A -u` over the 138 link-edge
  objects (client.so links no static archive) named clientshadowmgr.cpp as the
  last importer of shell_renderable_facts.cpp, shell_tools_off_client.cpp,
  every collisionutils.cpp symbol (its only compiler was client.so, so the file
  is deleted, not only ComputeSeparatingPlane) and nineteen mathlib_port_client
  forwards; `make debug GC_SECTIONS=0` links and tools/nogc-relink relinks both
  DLLs after it.
- R10, the leaf forwarder's five shadow rows stay in IManagedLeafSystemClient
  (the table only appends; L2's RenderLeafList precedent) and the binding
  answers the refusal, counted. IShimFlashlightClient's four manager slots
  retired with a named `tools/engine-forwarders --write --reorder`.
- R11, iclientshadowmgr.h stays in sdk-links: c_baseentity.h includes it, in
  90 client TUs.

### Divergences (each held by a fact, or named)

- D1, GetFlashlightState: S1's ruling (the manager uses its own copy) leaves
  out stock's two GetFlashlightState questions per flashlight frame (the
  witness's rtt*-flash traces: 2, 2 -> 0).
- D2, FirstMoveChild and NextMovePeer answer 0: no renderable is a
  C_BaseEntity on this link line.
- D3, cs_cl_renderable_report and its lost-renderable counter went with
  shell_renderable_facts.cpp; cs_cl_shadow_report counts and names the shadows
  whose renderable does not resolve (renderable_subject_test).
- D4, the render mode from Translucent (R6).
- D5, the native StandaloneShadowCast's has-shadow answer is written 0 and the
  managed query no longer asks for it: EntityShadows holds the handle
  (IEntityShadowState, the dev API's).

### The live A/B (c7-shadow-ab.steps, dm_lockdown, debug, port 27563)

Two base runs at cd01c2887 (base, base2) are not byte-equal to each other:
shadow handles, atlas texcoords and the values of the shadows whose casters
settle differently move (32-55 canonical lines a trace). So each of the 35
traces is compared on the lines both base runs hold (5,225), each
shadowmgr. kind's count against the two bases' range, the scenebytes, the
five engine-read cvars, and the 28 cs_cl_shadow_report counts (held, rtt,
simple, depth, none, lost, rttActive) per tagged window.

- **A clean run at the tip** (witness-un1, at the deletion's tree): every
  report equal (28 of 28), scenebytes equal in all 35 traces, cvars equal, 0
  `EXCEPTION IN`, 0 `could not acquire "`. 5 of the 5,225 stable lines missing:
  stock's four GetFlashlightState questions (D1) and one line of the crate in
  flight. The new lines are Q2's by design: 1,103 RemoveAllShadowsFromModel,
  378 RemoveAllShadowsFromBrushModel, 182 AddShadowToBrushModel, 2
  AddShadowToModel and 2 AddFlashlightRenderable (the flashlight lights the
  crate), the GetInfo answers for the new receivers' shadows, 23
  GetFlashlightFrustum per flashlit trace (the flashlight's receivers), and 1-2
  crate-in-flight lines in the drop traces.
- **The control** (witness-tip-noq2, the switch's tree with the policy's
  receive answers forced false, restored by sha): the new lines fell to the
  base-vs-base2 noise per trace, receivers 0, every kind's count in the bases'
  range, the reports equal. The switch alone is behaviour-equal within the
  noise; Q2 accounts for every receiver line above.
- **The flashlit crate** (c7-shadow-s3.steps, rtt0-p1-noflash.jpg and
  rtt0-p1-flash.jpg at the deletion's tree): mean luma of the crate's front
  85.2 -> 105.9 and top 71.5 -> 97.3 with the flashlight on, a patch of floor
  outside the beam 61.8 -> 61.8.
- **One intermittent difference, not attributed.** In 2 of 9 runs at the tip
  (the switch's commit, and the deletion's first run) entity entry 264 at
  (-3722, 4082, 49.8) had no shadow after the second map load in RTT mode
  (reports 64 against 65, rtt-off's and reload-rtt1-p1's), its shadow made at
  the RTT switch never rendered (renderFrame -1), and those two traces' scene
  held one entry fewer (190 -> 189). The 0 of 3 runs before (base, base2, the
  switch's tree before commit) and 7 of 9 at the tip (4 with a trace of
  entity 264's queue entries, 3 without) kept it. At a 2-in-9 rate, P(0 of 3)
  is 0.47, so three base runs cannot clear the base. A reload session without
  the A/B's earlier windows (s2-repro: map, RTT on, pose 3, reload, pose 1)
  kept it twice. The deletion's failing run also showed a six-entry rotation
  in the scenebytes of the RTT-on and pose 2/3 traces (the same six entities
  in another order); the switch's failing run and the clean run compared
  (un1) did not. The queue's aliasing (fixed in the copy fix)
  was a real bug found chasing it, but not its cause: it recurs with the fix.
  Open below.

The witness traces, reports and screenshots are in the lane's scratch
(witness-base, -base2, -tip2, -tip3, -tip4, -tip-noq2, -instr, -li1..3, -un1..3).

### S3: the cost (c7-shadow-s3.steps and c7-shadow-ab.steps' crossings windows, debug)

Crossings are cs_cl_crossings over 10 s windows (about 600 client frames) on
dm_lockdown in a debug build; the manager's own calls, time, queue and native
runs are cs_cl_shadow_stats (S3's instrument, ShadowSystemStats) in the same
windows. Base is cd01c2887 (c7-shadow-ab.steps' windows); the tip is the
deletion's tree.

| window | base /frame | tip /frame | delta | manager us/frame | native runs /frame (records, questions) |
|---|--:|--:|--:|--:|---|
| rtt0-p1 | 65.47 | 70.46 | +5.0 | 219.7 | 1,203 (1,268, 1,167) |
| rtt0-p1, flashlight | 72.47 | 76.47 | +4.0 | 504.8 | 1,596 (1,708, 1,554) |
| rtt0-p3 | 86.47 | 91.47 | +5.0 | 281.9 | 1,637 (1,741, 1,577) |
| rtt1-p1 | 66.55 | 71.47 | +4.9 | 322.9 | 1,862 (1,945, 1,823) |
| rtt1-p1, flashlight | 73.47 | 77.47 | +4.0 | 586.1 | 2,244 (2,373, 2,200) |
| rtt1-p3 | 88.47 | 94.47 | +6.0 | 443.0 | 2,791 (2,912, 2,729) |
| reload, rtt1-p1 | 66.47 | 71.47 | +5.0 | 313.9 | 1,834 (1,916, 1,797) |
| reload, rtt0-p1 | 65.53 | 70.47 | +4.9 | 216.2 | 1,195 (1,260, 1,159) |

- **The plus:** the manager's own slots, six a frame in every window
  (AdvanceFrame 1, PreRender 2, ComputeShadowTextures 1,
  ComputeShadowDepthTextures 1, UnlockAllShadowDepthTextures 1); the queue
  rides in them (Pending 0 a frame at every window since the 512-entry queue;
  at 256 it was handed over once a frame wherever more than 256 entries
  queued, 248-269 a frame, 18-25 us).
- **The minus:** the leaf forwarder's EnumerateShadowsInLeaves (1 a frame,
  RTT on) and ProjectFlashlight (1 a frame, flashlight on), and one TakeOps a
  frame (the receivers' operations are in-process): -1 to -2.
- **Q1's per-bind crossings:** ClientMaterialProxyBind rises by 1 a frame at
  rtt1-p1 and the reload (2 -> 3) and by 3 at rtt1-p3 (1 -> 4): the Shadow
  proxy's binds of the RTT shadow decals. With RTT off (the default) it
  binds none (0 change at every rtt0 window).
- **Where the managed time goes** (rtt0-p1): PreRender 90.5 us (the dirty
  walk), the receivers 108.9 us (127 calls a frame from the leaf registry,
  each asking native whether the renderable receives), the RTT pass 17.2 us;
  with the flashlight on the depth textures add 165.8 us and the flashlight's
  four calls 102 us; with RTT on ComputeShadowTextures is 115-187 us.
- **The native runs are questions:** 1.04-1.07 records a run; the largest
  run is 17 records (RTT on). Effects batch; questions do not, because each is
  asked where stock asked it.
- **Budget:** the port-budget ruling allows the rise (+4 to +6 inbound a
  frame). The batching candidates for the perf pass, measured here: the
  receivers' ShouldReceiveProjectedTextures (127-152 a frame) answered from the
  leaf registry's own Receives batch, which already asks the same question for
  its inserts; the dirty walk's renderable facts (origin, angles, bounds, model
  per dirty shadow, 162-284 queue entries a frame) asked in one batch per
  PreRender in the leaf registry's Facts shape. A cache of RenderableOf was
  considered and refused: a caster deleted before its DESTROY is applied would
  answer a freed renderable.

### Mutation proofs (a cp snapshot, sha256 before, mutated, restored with a fresh mtime, sha256 equal)

| mutation | file | red |
|---|---|---|
| a held shadow is created again on refresh | ClientShadowSystem.cs | ARefreshCreatesTheEntitysShadowOnceAndASecondKeepsIt |
| a retired shadow destroyed again | ClientShadowSystem.cs | AShadowTheManagerRetiredIsNotDestroyedAgainByTheEntitysDestroy |
| the dirty entry's texture bit ignored | ClientShadowSystem.cs | ADirtyMarkWithTheTextureBitMarksTheTextureDirty |
| Q2's two answers swapped | ClientRenderPolicy.cs | TheReceiveAnswersAreWrittenAboveTheShadowFlags |
| Q2's render mode always normal | ClientRenderPolicy.cs | ABlendedInvisibleEntityTakesNoFlashlight |
| Q1's ShadowModel made as Shadow | MaterialProxyFactory.cs | TheShadowModelProxyWritesWhatTheEnginesInfoSaysThroughTheHost |
| the queue read one word an entry | ShadowsClientBinding.cs | TheBindingHandsEachSlotToTheHostAndCountsItsCrossing |
| a throwing Init refuses start-up | ShadowsClientBinding.cs | AThrowingHostRefusesNothingAndEverySlotIsCaught |
| a second dirty mark appended | shadow_pending_queue.h | ASecondDirtyMarkCollapsesIntoTheFirstAndKeepsTheTextureBit |
| a destroy keeps earlier entries | shadow_pending_queue.h | ADestroyDropsTheEntitysEarlierEntriesAndNoOthers |
| a full queue not handed over | shadow_pending_queue.h | AFullQueueIsHandedOverBeforeTheNextEntry |
| Take hands the queue's own array (the pre-fix code) | shadow_pending_queue.h | WhatIsQueuedWhileTheCallReadsTheTakenEntriesLeavesThemIntact (six expectations) |
| the forwarder's AddShadow reaches managed | fwd_leaf_system.cpp | FwdLeafSystem.TheShadowHalfCallsNothing |
| a retired binding slot answers operations | LeafSystemClientBinding.cs | TheRetiredShadowSlotsAnswerTheRefusalAndAreCounted |

fwd_shadows_test's facts were each shown red against a scratch copy of
fwd_shadows.cpp with the kind's arm broken (the two short-record pointer bugs
they found are fixed in the switch).

### Open

- CLOSED 2026-10-20: the intermittent missing shadow of entity entry 264
  after the second map load was neither the shadow manager's nor the leaf
  system's. The session's teleport to pose 1 swept the trigger touch from the
  player's spawn and, from two of the ten spawn points, took the shotgun
  pickup that entry is (hidden for its respawn). The standalone teleport no
  longer sweeps (CStandaloneEntity::TeleportTo; docs/todo.md has the numbers).
- The batching above, in the perf pass.
- iclientshadowmgr.h stays in sdk-links while c_baseentity.h includes it.
- mathlib_port_stock_test still holds ss_mathlib's inlines of the deleted
  forwards (VectorITransform, VectorIRotate, GeneratePerspectiveFrustum, ...)
  to stock; no DLL calls them now.
- The C0 snapshot (docs/novalve-snapshot/client-c0/client-commands-contract.txt)
  still lists cs_cl_renderable_report; the snapshot is C0's, and the command
  left at S2's deletion (D3).

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
