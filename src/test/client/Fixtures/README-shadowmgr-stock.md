# `shadowmgr-stock.tsv`: where it came from

Stock's client shadow manager, `game/client/clientshadowmgr.cpp` (C7
cluster 12, `docs/plans/c7_shadow_manager.md` S0): `CClientShadowMgr`, its
visible list and its texture allocator, and the two material proxies
(`Shadow`, `ShadowModel`). Written by
`src/test/oracle/native/shadowmgr_oracle_test.cpp`, which compiles the
pristine file whole from the clone at `valve-sdk.lock` into a scripted world
(`shadowmgr_stub/shadowmgr_harness.h` says what is stock and what is
stand-in). `ShadowManagerOracleTests` replays it against
`src/client/managed/Render/Shadows`.

THE GOLDEN CARRIES ITS OWN STIMULUS. Every thing is described (`T`) before
it is used and again whenever it changes, every setting (`CV`, `ENV`) is
written before it is read, and every call the scenario makes is a `C` row
before it is made, with its answer as an `R` row after. A replay rebuilds the
world from these rows alone. Every float is its IEEE bits, eight hex digits;
a vector is three of them joined by commas, a matrix sixteen (row by row), a
3x4 matrix twelve.

## Rows

| row | fields | what |
|---|---|---|
| `#` | text | comments |
| `X` | name | an interface the TU registers by name (the two proxies) |
| `L` | struct, size, offsets | the layout of an engine answer the managed side copies (`ShadowInfo_t`, `cplane_t`, `Frustum_t`), held by ShadowManagerFactsTests to ShadowsAbi.cs |
| `ENV` | key, values | what a stand-in answers from here on (below) |
| `F` | frame, curtime | `gpGlobals->framecount` and `curtime` |
| `CV` | name, value | the scenario set a ConVar |
| `T` | see below | a thing (a renderable), as it now is |
| `RCV` | leaf, thing, kind | a receiver the leaf system holds in a leaf (`ShadowReceiver_t`) |
| `C` | call, args | a call into the manager, through `IClientShadowMgr` |
| `R` | value | the call's answer |
| `E` | what, args | an effect: a call the manager made on the engine, the leaf system, a renderable, a material, a render target or the render context |
| `Q` | what, args, answer | a question the manager asked, and the stand-in's answer |
| `M` | spew type, text | `Msg` (0) or `Warning` (1) text |
| `A` | text | an assertion of the subject's own (or `utlrbtree.h`'s) that failed |
| `AL` | count | the library assertions counted and not written (vector2d.h's `IsValid` on a shadow's uninitialised members) |
| `END` | | the end |

`T`: id, has-model, model type (`modtype_t`), origin, angles, render mins,
render maxs, RTT mins, RTT maxs (what `GetShadowRenderBounds` answers for a
render-to-texture type; the render box for any other), world mins, world maxs
(`GetRenderBoundsWorldspace`), `RenderableToWorldTransform`, cast-distance
override flag and value, cast-direction override flag and vector, shadow
parent, first shadow child, next shadow peer (ids, -1 for none), the
projected-texture flags it receives, its shadow handle (`GetShadowHandle`),
its shadow-dirty bit, its model instance, body, skin, whether
`DrawModelShadowSetup` answers bones, whether its render origin re-dirties its
shadow (as an aiment's does), and whether its entity is gone (the entity list
answers NULL).

`C` calls: `Init`, `Shutdown`, `LevelInitPreEntity`, `LevelShutdownPostEntity`,
`PreRender`, `AdvanceFrame`, `CreateShadow thing flags`, `DestroyShadow h`,
`CreateFlashlight state`, `UpdateFlashlightState h state`, `DestroyFlashlight h`,
`UpdateProjectedTexture h force`, `AddToDirtyShadowListHandle h force`,
`AddToDirtyShadowList thing force`, `MarkRenderToTextureShadowDirty h`,
`AddShadowToReceiver h thing kind`, `RemoveAllShadowsFromReceiver thing kind`,
`ComputeShadowTextures view leaves`, `ComputeShadowDepthTextures view`,
`UnlockAllShadowDepthTextures`, `RenderShadowTexture w h`,
`SetShadowDirection dir`, `GetShadowDirection`, `SetShadowColor r g b`,
`SetShadowDistance d`, `SetShadowBlobbyCutoffArea a`, `SetFalloffBias h bias`,
`SetFlashlightLightWorld h on`, `SetFlashlightTarget h` (always NULL),
`SetShadowsDisabled on`, `Restore` (the material system's restore callback the
manager registered), `Command line` (one of the five console commands, or
`r_shadowbreak`), `ProxyInit name vars` and `ProxyBind name h`. A flashlight
state is: origin, orientation (x,y,z,w), near, far, horizontal and vertical
FOV, attenuation (quadratic, linear, constant), colour, spotlight texture,
shadows on, texture frame, shadow map resolution, shadow attenuation, slope
scale depth bias, depth bias. A view is: origin, angles, near, far, FOV, aspect.
A leaf list is `count:leaf,leaf,...`.

## What the stand-ins answer (the replay answers the same way)

- **The level** (`ENV leaves count width`): leaf i is `[i*width, (i+1)*width)`
  along x and reaches over all y and z. A swept box (`EnumerateLeavesAlongRay`)
  touches the leaves its swept bounds overlap in x, a box the leaves it
  overlaps, in ascending order.
- **The engine**: `CullBox` culls a box that does not overlap `ENV viewbox`;
  `GetAmbientLightColor` is `ENV ambient`; `ComputePixelDiameterOfSphere` is
  radius times `ENV ppu`; the DX level is `ENV dxlevel`; depth textures are
  supported per `ENV supportsdepth`; a render target's actual size is clamped
  to `ENV maxrendertarget`; the fog and height-clip modes start as `ENV fog`
  and `ENV heightclip` and are what was last set; `-tools` is on the command
  line per `ENV tools`.
- **The engine's shadow manager** keeps what it was told. Its handles come as
  `CUtlLinkedList`'s do (the newest freed first, else a fresh one). A new
  shadow's `ShadowInfo_t` is zeroes with a texture size of 1,1 and bias 0;
  `ProjectShadow` sets its matrix, max distance, falloff offset and amount;
  `SetShadowTexCoord` its texture origin and size; `SetFalloffBias` its bias;
  `ProjectFlashlight` its matrix, and the flashlight's frustum is the six
  inward axis planes (min x, y, z, then max x, y, z) of
  `CalculateAABBFromProjectionMatrix` of that matrix; `UpdateFlashlightState`
  the state `GetFlashlightState` answers.
- **The leaf system** keeps each shadow's leaves. Projecting a shadow or a
  flashlight offers it (`leaf.receiver`, then `AddShadowToReceiver`) to the
  receivers registered in its leaves, leaf by leaf in the order given, each
  receiver once per projection; enumerating offers (`leaf.enum`, then
  `EnumShadow`) each live shadow projected into the given leaves once, leaf by
  leaf, oldest shadow first.
- **The renderables** answer from their `T` row. A model pointer names its
  thing (`m<id>`), a renderable `r<id>`. `CreateModelInstance` gives an
  instance of 100 + id when there is none. Every renderable's
  `GetBaseEntity()` is NULL (the fork's deletions made every C_BaseEntity arm
  answer NULL; `TheBaseEntityArmsFireZeroTimes`).
- **Textures and materials** are tokens with a name. `GetActualWidth` is the
  clamped size.

## The scenario

Three manager lifetimes: a low-end card without depth textures (Init leaves
render-to-texture off and turns the depth ConVar off); `-tools` with
render-to-texture on (four depth textures; the atlas at Init); then the game.
The game's level holds blob casters for each of the ortho bases' three axes,
a brush model, one overriding its cast distance and direction, one with no
model, one made transparent, one whose entity goes, a sprite, an aiment-like
one and one with no entity at all, over receivers of each kind and two rings
of small receivers (the cull's every case). Frames 2-6 project blobs, re-dirty
out of handle order, retire a shadow mid-walk, call the receivers directly,
run the setters and the console commands, destroy and reuse a handle. Frames
7-20 turn render-to-texture on over a shadow hierarchy (a child with its own
shadow, a child with none whose child has one, a brush child, a blob child),
seven big casters and a huge one (the atlas has six 256 fragments), a medium,
an animating, a transparent and an out-of-view caster; they draw, overflow,
run a budget of two for two frames, return, grow a caster across fragment
sizes, steal a fragment from a caster that left the view, exhaust the 16-texel
fragments with a crowd of 257, re-dirty everything through the direction and
the restore, close each gate, draw the debug boxes, bind both proxies, and
turn render-to-texture off. Frames 21-27 run four flashlights (two with depth
textures in view, one out of view, one without shadows), version2, light-world
and models off, the depth textures' resolution, off, on and unsupported, and
1025 depth flashlights in one view. Frame 28 ends the level with shadows left
and render-to-texture on, starts the next, and shuts down.

## What the oracle found in stock

- **A retired shadow stops PreRender's walk.** `UpdateShadow` destroys a
  shadow whose entity is gone, which removes it from the dirty set while the
  set is being walked; `NextInorder` of the freed node answers no node, so the
  walk ends there (utlrbtree.h's `IsValidIndex` assertion fires), and
  `RemoveAll` drops the shadows after it in handle order unprojected. Their
  renderables keep their dirty bit, so `AddToDirtyShadowList( renderable )`
  refuses them from then on (frame 4: the sprite's shadow is never updated
  again).
- `SetViewFlashlightState` hands `SetFlashlightRenderState` the client
  shadow handle, not the engine's.
- `DrawShadowHierarchy` always draws its root, so `DrawRenderToTextureShadow`'s
  "Didn't draw shadow hierarchy" message cannot be reached.

## How the replay compares

Between two consecutive stimulus rows (`C`, `T`, `CV`, `ENV`, `F`, `RCV`), the
managed log must hold stock's `E`, `R` and `M` rows, each kind in stock's
order. `Q` rows are the stand-ins' answers and are not compared in order (the
managed manager asks through its own seams); `A` and `AL` rows are stock's
assertions, which the managed manager does not carry.
