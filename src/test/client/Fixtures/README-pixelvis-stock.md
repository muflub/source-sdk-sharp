# `pixelvis-stock.tsv`: where it came from

Stock's client pixel visibility, the whole of `game/client/c_pixel_visibility.cpp`
(C7): `CPixelVisibilitySystem`'s query sets (a set per caller, found by the
caller's handle, `(serial << 16) | (node + 1)`), a `CPixelVisibilityQuery` per set
per view with its two occlusion query objects, `GetFractionVisible`'s fade (the
partial arm over the counting query's pixels, the plain arm over the visible
query's, an unready or failed query, the 0.95 snap), the proxies `EndView` draws
(the counting pass between `DepthRange( 0, 0.01 )` and `( 0, 1 )`, then the
visible pass; `PixelVisibility_DrawProxy`'s pyramid, its screen-space and
minimum-pixel sizes and its frustum ratio, a proxy behind the eye), `EndScene`
freeing unused queries and sets, the level's start (the query probe, the two
materials) and end (everything destroyed), `GlowSightDistance` and
`CTraceFilterGlow`, `PixelVisibility_ShiftVisibilityViews`, `IsAvailable`, the
four ConVars and `pixelvis_debug`. Written by
`src/test/oracle/native/pixelvis_oracle_test.cpp`, which includes the pristine
file from the clone at `valve-sdk.lock` into a scripted world over vtable doubles
of the material system (`pixelvis_stub/pixelvis_harness.h` says what is stock and
what is stand-in). `PixelVisibilityOracleTests` replays it against the managed
`PixelVisibilitySystem`.

The golden carries its stimulus: each `S` row is a step, and the rows after it
are what stock did, in order.

| row | meaning |
|---|---|
| `P NAME MATERIALS...` | a client effect precache registration (static init) |
| `S init DX FAILS` / `S shutdown` | the system's LevelInitPreEntity (the DX level; 1 when the query probe fails) / LevelShutdownPostEntity |
| `S frame N FRAMETIME` | gpGlobals' frame count and frame time (float bits) |
| `S view ID ORIGIN FORWARD RIGHT UP M[16]` | the current view and its world-to-screen matrix (row-major, bits) |
| `S noview` | the current view id is -1 |
| `S cvar NAME VALUE` | a ConVar set |
| `S fv CALLER POS SIZE ASPECT FADE SCREEN HANDLE` | `PixelVisibility_FractionVisible` with the caller's handle |
| `S fvnull POS` | the same with no handle (a sight line) |
| `S endview` / `S endscene` | `PixelVisibility_EndCurrentView` / `_EndScene` |
| `S world N FRACTION` | the next traces cross the first N of prop, brush, player, trigger, and the world stops them at FRACTION |
| `S sight POS TRACE` / `S avail` / `S shift SRC DST` / `S debug` | `GlowSightDistance`, `PixelVisibility_IsAvailable`, `PixelVisibility_ShiftVisibilityViews`, `pixelvis_debug` |
| `R ...` | the step's answer (a float's bits, and the caller's handle after it) |
| `MF NAME GROUP COMPLAIN` / `MI NAME` / `MD NAME` | FindMaterial, a material's reference taken / dropped |
| `QC ID` / `QD ID` / `QR ID` / `QP ID PIXELS` | an occlusion query made (0: it could not be), destroyed, reset, its pixels read |
| `PD POS RADIUS DIAMETER` | ComputePixelDiameterOfSphere |
| `B NAME` / `DR NEAR FAR` / `BQ ID` / `EQ ID` | Bind, DepthRange, Begin/EndOcclusionQueryDrawing |
| `GM BUFFERED VOVERRIDE IOVERRIDE MATERIAL` / `MESH PRIM N VERTS...` | GetDynamicMesh, and the mesh drawn (its vertices' bits) |
| `T START END MASK` / `F NAME SOLID HIT` / `FB NAME PASS GROUP` / `TR FRACTION` | a trace, the glow filter's answer per entity, CTraceFilterSimple's stand-in asked, the fraction |
| `D1 TEXT` / `M TEXT` | DevMsg( 1, ... ) / Msg |

The scripted answers, which the replay takes from the rows: a query's pixels are
a fixed rule over its id and the frame (`QP`), a sphere's diameter is
`radius * 400 / distance` (`PD`), and a trace's fraction is the step's world
answer, or 0.25 when the filter let an entity stop it (`TR`).

To regenerate (only when the script changes, and say why in the commit): touch
`src/test/oracle/native/pixelvis_golden.regen`, run
`farm/debug/src/buildcpptests --oracle pixelvis_oracle_test`, delete the marker.
