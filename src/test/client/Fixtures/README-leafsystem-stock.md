# `leafsystem-stock.tsv`: where it came from

Stock's client leaf system, `game/client/clientleafsystem.cpp` (C7 cluster
12, `docs/plans/c7_leaf_system.md` L0): the registry of renderables and the
BSP leaves they sit in, `BuildRenderablesList` / `CollateRenderablesInLeaf`
(every arm: the static-prop switch, the same-frame dedupe, the translucent
render leaf, alpha 0, the area frustum under `r_PortalTestEnts` and
`r_portalsopenall`, the main frustum, the occlusion test for studio models,
the four opaque size buckets, the two-pass split, the detail objects, the
debug build's `cl_drawleaf` filter and the 4096-entry overflow),
`ComputeTranslucentRenderLeaf` (the nearest leaf, the furthest under
alternate sorting, the once-per-frame-and-view blend), `SortEntities`,
`CollateViewModelRenderables`, and the shadows projected into leaves
(`ProjectShadow`, `ProjectFlashlight`, the receivers, `EnumerateShadowsInLeaves`).
Written by `src/test/oracle/native/leafsystem_oracle_test.cpp`, which compiles
the pristine file whole from the clone at `valve-sdk.lock` into a scripted
world (`leafsystem_stub/leafsystem_harness.h` says what is stock and what is
stand-in). `LeafRegistryOracleTests` replays it against
`src/client/managed/Render/LeafSystem`.

The scenario: a level of 12 leaves (100 units each along x; leaves 0-5 in
area 1, 6-9 in area 2, 10-11 in area 3) holding opaque entities of each
size and three exactly on the buckets' thresholds (30, 80 and 200 tall), brush models, a sprite, view models, translucent boxes for the sort,
two translucent boxes across three leaves (one alternately sorted), two-pass
models at alpha 255 and 128, blends that move a render group, static props
as the engine adds them, detail objects in three leaves, and two shadows.
Frame 1 draws the main view front to back, a shadow depth view and the view
models; frame 2 moves, removes and re-adds renderables and draws the main
view from the far end twice, a reflection, and a view with static props off;
frame 3 draws with each portal ConVar flipped, without detail objects, with
detail objects and no translucent ones, a shadow depth view over three
leaves, and the debug build's `cl_drawleaf 3`. Frame 4 is the registry's
(L2): a shadow whose flags carry more than its kind, two static props added
under it (only the first takes it: AddRenderableToLeaves does not advance the
shadow enumerator), a brush model moved inside it, a renderable moved and
removed in one frame, render groups into and out of the view models' list and
two-pass on and off, a static prop faded to translucent, alternate sorting
off, the shadow projected elsewhere, `IsRenderableInPVS`, `GetRenderableLeaf`
walked, each leaf's detail state and the sub-systems' data replaced. A second
level of 4100 leaves overflows the opaque groups with markers alone; its
change deletes the sub-systems' data, and a handle from the old level is
removed after it.

THE GOLDEN CARRIES ITS OWN STIMULUS. Every call the scenario makes on the
registry is a row before it is made (`T` describes each thing first), so a
replay builds the same world and makes the same calls without this file's
source: `SourceSharp.Client`'s `LeafRegistry` facts do (L2).

To regenerate (only when the scenario or the buffer's format changes, and say
why in the commit): touch `src/test/oracle/native/leafsystem_golden.regen`,
run the oracle test (`farm/debug/src/buildcpptests --oracle
leafsystem_oracle_test`), delete the marker.

## The stand-in answers

The managed replay gets the engine's answers from the buffers, so they are
part of the scenario:

- `EnumerateLeavesInBox` answers, in index order, the leaves a box overlaps.
- `GetLeavesArea` is the leaves' area when they share one, else -1.
- `CullBox` culls a box that misses the view's frustum box (x -1000..2000,
  y and z -400..400); `DoesBoxTouchAreaFrustum` is whether a box meets its
  area's box (area 2's is y -200..200, the others take everything);
  `IsOccluded` is whether a box lies inside the one occluder (x 1000..1100,
  y and z -100..100).
- A renderable's `ComputeFxBlend` sets its blend, and its render group
  through `SetRenderGroup`, when the scenario says (a temp entity's does).
- `ShouldReceiveProjectedTextures( flags )` is whether the flags meet the
  kinds the thing takes (its `T` row's receive column).
- `AreAnyLeavesVisible` is whether any of the leaves is below 6.
- A leaf is `[i * width, (i + 1) * width]` along x and -500..500 in y and z
  (its level's `LV` row gives the width); its area is its `LA` run's.

## The rows

Floats are their IEEE bits in hex (`%08x`).

| tag | columns |
|---|---|
| `LV` | a level: its leaf count, then each leaf's width along x |
| `LA` | a run of the level's leaves in one area: first, last, area |
| `LS` | LevelShutdownPostEntity (each level's start, and the run's end) |
| `WB` | an engine box: `frustum` (CullBox's), `area0`..`area3` (each area's portal frustum), `occluder`; mins, maxs |
| `T` | a thing, before it is registered or made a detail model: id, model type, transparent, two-pass, blend, the blend and the render group its ComputeFxBlend moves to (-1 none), the projected-texture kinds it takes, world mins, world maxs, render mins, render maxs, origin |
| `CH` / `SL` | a static prop's CreateRenderableHandle (id, handle) / the leaves AddRenderableToLeaves is then handed |
| `RH` | after `RemoveRenderable`, the handle the renderable is left with |
| `RG` / `CG` / `AS` / `SP` / `CV` | SetRenderGroup (id, group) / ChangeRenderableRenderGroup (id, group) / EnableAlternateSorting (id, on) / DrawStaticProps (on) / a ConVar set (name, value) |
| `DM` | a detail model: its index in the detail object system, the thing's id (-1 a sprite slot) |
| `DS` / `DSS` / `DD`, `DDA` | SetDetailObjectsInLeaf (leaf, first, count) / SetSubSystemDataInLeaf (leaf, tag) / DrawDetailObjectsInLeaf (leaf, frame), then its first and count |
| `SDEL` | a sub-system's data deleted by the leaf system: its tag |
| `PS` / `PF` | ProjectShadow / ProjectFlashlight: handle, leaves |
| `PV`, `PVQ`, `PVA` | IsRenderableInPVS: the id; the leaves it asked AreAnyLeavesVisible of, and that answer; its answer |
| `GL` | GetRenderableLeaf walked from the first leaf by its iterator: id, leaves |
| `DST` | the detail state asked at a frame: the `GD` rows follow |
| `GD` | per leaf: GetDetailObjectsInLeaf's first and count, whether GetSubSystemDataInLeaf has data, ShouldDrawDetailObjectsInLeaf at the frame |
| `RMS` | RemoveRenderable of a handle from the level before |
| `R` | a renderable registered: id, how (`add` AddRenderable, `static` CreateRenderableHandle + AddRenderableToLeaves), group, model type (1 brush, 2 sprite, 3 studio), transparent, two-pass, blend, world mins, world maxs, handle |
| `MV` / `RM` | a renderable moved (`RenderableChanged`) / removed |
| `F` | a frame: `gpGlobals->framecount`, then `PreRender` |
| `IN` | after `PreRender`, a renderable's handle, area and leaves (`GetRenderableLeaves`) |
| `SX` / `SR` | the shadow manager told to remove all shadows from / add a shadow to a receiver: (shadow,) id, receiver kind |
| `SH` / `SD` | a shadow added (user id, handle, flags) / removed (user id, handle) |
| `ES` / `EN` | `EnumerateShadowsInLeaves` over the leaves / each shadow it enumerated |
| `V` | a view: tag, render frame, view id, leaf count, translucent-leaf pass, draw translucent, draw detail, static props drawn, detail build frame, origin, forward, its leaves |
| `CT` | stock's `ComputeTranslucentRenderLeaf` ran: frame, view id |
| `BL` | a renderable's `ComputeFxBlend`: id, blend after |
| `FB` | a renderable's `GetFxBlend` as stock asked it: id, blend |
| `Q` | an engine question stock asked: `cull` (id, culled), `area` (id, area, touches), `occ` (id, occluded) |
| `NP` | `Con_NPrintf`: line, text |
| `VB` | the view's buffer, the oracle's own reading of stock's registry for the view (`leafsystem_stub/leafsystem_gather.h`; its header says the layout), each word in hex; `TheScenarioReachesEveryArm` names the arms from it |
| `O` | stock's list: group, position, source (`r` a renderable row, `d` a detail-model row, `-` the leaf's marker), row, id, the world list's leaf index, two-pass, render handle |
| `ST` | after the view, each buffer row's registry state: row, id, render frame, render leaf, render frame 2 |
| `VE` | the end of the view |
| `VM` / `VMB` / `VMI` / `VML` | `CollateViewModelRenderables`: the frame; the buffer (count, then each view model's group after its blend, in stock's order); the same view models' ids; stock's opaque (`o`) and translucent (`t`) lists, by id |
| `END` | the end of the run |
