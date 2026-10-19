# `detailprops-stock.tsv`: where it came from

Stock's client detail props, the whole of `game/client/detailobjectsystem.cpp`
(C7 cluster 10): the detail lump's parse (`UnserializeModelDict`,
`UnserializeDetailSprites`, `ScanForCounts`, `UnserializeModels`,
`UnserializeModelLighting`, the fast sprites' build-out), the per-view fade and
screen alignment (`BuildDetailObjectRenderLists`, `EnumerateLeaf`), the
back-to-front sorts (`SortSpritesBackToFront`, `BuildOutSortedSprites`) and
every quad the four translucent draws write (`RenderFastSprites`,
`RenderTranslucentDetailObjects`, `RenderFastTranslucentDetailObjectsInLeaf`,
`RenderTranslucentDetailObjectsInLeaf`). Written by
`src/test/oracle/native/detailprops_oracle_test.cpp`, which compiles the
pristine file whole from the clone at `valve-sdk.lock` into a scripted world
(`detailprops_stub/detail_harness.h` says what is stock and what is stand-in).
`DetailPropsOracleTests` replays it against `src/client/managed/Detail`.

Eleven scenarios, each seeded (xorshift32, the seed in its `S` row), each one
level of the same detail system object, so what one level leaves behind is the
next one's start, as in the game: the lump versions (4, 3, 5 and none), the
fast and old sprites, model props, the shape types this build does not draw,
light styles, `cl_detail_multiplier`'s random copies, both fades, the
wireframe, `r_DrawDetailProps` 2 and 0 and `mat_fullbright` switches, a client
mode that draws no detail, and a mesh of five quads that splits every batch.
The local player is NULL, as this build's is, so the FOV factor is 1.

To regenerate (only when the scenario changes, and say why in the commit):
touch `src/test/oracle/native/detailprops_golden.regen`, run the oracle test
(`farm/debug/src/buildcpptests --oracle detailprops_oracle_test`), delete the
marker.

## The stand-in answers

The managed replay gives the same answers, so they are part of the scenario:

- `LinearToGamma`: per channel, `min( 255, (int)( sqrtf( max( x, 0 ) ) * 255 ) ) / 255.0f`.
- `LightStyleValue( s )`: `( s % 5 ) * 0.25f`.
- `ComputeDynamicLighting( pt )`: `x = ( (int)fabsf( pt.x ) % 7 ) * 0.03125f`,
  `y = ( (int)fabsf( pt.y ) % 3 ) * 0.0625f`, `z = x == 0 ? 0 : 0.015625f`.
- A model's answers come from its name: `vlit` is vertex lit, `glass`
  translucent, `proxy` has a material proxy, `brush` is a brush model. Models
  are numbered in first-load order from 1.
- The BSP query enumerates, in leaf order, every leaf whose centre
  (`LeafCenter` in the harness) lies within the radius plus 212 units; the `Q`
  row lists them.
- `rand()`: MSVC's generator (`x = x * 214013 + 2531011`, `( x >> 16 ) & 0x7fff`),
  seeded with the low 16 bits of the scenario's seed.

## The rows

Floats are their IEEE bits in hex (`%08x`); colours are four bytes in hex.

| tag | columns |
|---|---|
| `RSQ` | an input and this CPU's `rsqrtps` estimate of it (the fast sprites' `VectorNormalizeFast`) |
| `S` | scenario: name, seed, detail lump version (0: none), HDR, light style entries per lighting lump (-1: neither lump), the sprite material's mapping width and height, `GetMaxToRender`'s vertices and indices, `cl_detaildist` and `cl_detailfade` at `Init`, `cl_fastdetailsprites`, `cl_detail_multiplier`, the world's `detailmaterial` (`-`: none) |
| `G` | a game lump the map has: id, version, bytes in hex |
| `C` | a call the harness makes: `init`, `post`, `view` (index, `r_DrawDetailProps`, wireframe, `mat_fullbright`, the client mode draws detail, `BuildWorldListsNumber`), `begin` (the view's leaves, back to front), `list` (a `RenderTranslucentDetailObjects` leaf list), `inleaf` (leaf, the closest point or `-`), `shutdown`, `end` |
| `VO` `VC` `VF` `VR` `VU` | the view's origin, `CurrentViewOrigin`, forward, right, up |
| `U` | `cl_detaildist` as the player set it after the level loaded |
| `CV` | `cl_detailfade`, `cl_detaildist` after `LevelInitPostEntity` |
| `GL` | `LoadGameLump`: id, size asked, the lump exists |
| `LM` | `LoadModel`: name, as a prop, the model's number |
| `VL` | `IsModelVertexLit`: model, answer |
| `MI` / `MS` | a `CMaterialReference` initialised (name, group) / shut down |
| `PM` | `PrecacheMaterial` |
| `LS` | `SetSubSystemDataInLeaf`: leaf, subsystem, data given |
| `LD` | `SetDetailObjectsInLeaf`: leaf, first, count |
| `LK` | `DrawDetailObjectsInLeaf`: leaf, frame |
| `Q` | `EnumerateLeavesInSphere`: centre, radius, the leaves it enumerates |
| `RT` | `RecomputeTranslucency`: model, skin, body, a renderable given |
| `M` | a renderable `GetDetailModel` answers: index, model, render origin, render angles, `GetFxBlend`, `IsTransparent` |
| `MC` | that renderable's `GetColorModulation`, which the renderer reads when it gathers a view's lists: asked only in a view whose list build ran |
| `DM` | `IVModelRender::DrawModel` from that renderable's `DrawModel( 1 )`: flags, instance, entity, model, origin, angles, skin, body, hitbox set |
| `XM` `XP` `XI` `XO` | the render context's `MatrixMode`, `PushMatrix`, `LoadIdentity`, `PopMatrix` |
| `XG` | `GetDynamicMesh`: buffered, the material |
| `MB` | `CMeshBuilder::Begin`: primitive (7 = quads), count |
| `V` | a vertex: position, colour, texture coordinate |
| `ME` | `CMeshBuilder::End`: the vertices written since `Begin` |
| `D` | `IMesh::Draw` |
| `R` | a `rand()` draw |
| `W` | a spew line (type, text): the subject's `Warning`s and any assert |
