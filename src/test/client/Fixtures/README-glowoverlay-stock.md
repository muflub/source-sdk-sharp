# `glowoverlay-stock.tsv`: where it came from

Stock's client glow overlays, the whole of `game/client/glow_overlay.cpp` and
`warp_overlay.cpp` (C7): the active list (`Activate` adds an overlay once,
`Deactivate` keeps it listed, an `Update` that answers false deletes it at the
next draw, the level's end deletes them all), each overlay's obstruction (the
pixel visibility question for a positional and an in-sky overlay, screen-space
for the sky; the trace and its fade over `cl_sun_decay_rate` when the card
cannot answer; `building_cubemaps`; the cache flags), each sprite's size and
colour by the angle to the view, its basis and quad, the frustum cull, the
default material and its `$hdrcolorscale`, the wireframe pass,
`r_drawsprites`, the clipping switch around the draws (off for a sky overlay,
restored after), the warp overlay's own draw (its range check, `sun/overlay`
found at each draw, no cull, no HDR scale), and `UpdateSkyOverlays`. Written by
`src/test/oracle/native/glowoverlay_oracle_test.cpp`, which includes the
pristine files from the clone at `valve-sdk.lock` into a scripted world over
vtable doubles of the material system (`glowoverlay_stub/glowoverlay_harness.h`
says what is stock and what is stand-in). `GlowOverlayOracleTests` replays it
against the managed `GlowOverlaySystem`.

The golden carries its stimulus: each `S` row is a step, and the rows after it
are what stock did, in order.

| row | meaning |
|---|---|
| `P NAME MATERIALS...` | a client effect precache registration (static init) |
| `S overlay KIND NAME LIVES POS DIRECTIONAL DIR INSKY PROXYRADIUS HDRSCALE N (COLOR HORZ VERT MATERIAL)...` | an overlay made and activated: `glow` (CGlowOverlay) or `warp` (CWarpOverlay); LIVES is how many updates a warp overlay answers true (-1: always), the script's stimulus; MATERIAL is the creator's, `-` for none (the default, found at its first draw) |
| `S frame FRAMETIME` | gpGlobals' frame time (float bits) |
| `S view ORIGIN FORWARD ZFAR (NORMAL DIST)x5` | the current view, the view setup's zFar and the frustum's first five planes |
| `S clipping B` / `S world FRACTION FLAGS` / `S available B` / `S wireframe B` | the context's clipping state, the trace's answer (fraction bits, surface flags), whether the card answers pixel visibility, ShouldDrawInWireFrameMode |
| `S cvar NAME VALUE` | a ConVar set (r_drawsprites, building_cubemaps, cl_sun_decay_rate) |
| `S draw CACHE` / `S sky ZFAR CACHE` | `CGlowOverlay::DrawOverlays` / `UpdateSkyOverlays` |
| `S activate NAME` / `S deactivate NAME` / `S delete NAME` / `S shutdown` | Activate, Deactivate, the owner's delete, the system's LevelShutdownPostEntity |
| `PA B` | PixelVisibility_IsAvailable and its answer |
| `PV POS SIZE ASPECT FADE SCREEN HIN ANSWER HOUT` | PixelVisibility_FractionVisible: the question, the handle in, the answer, the handle out |
| `T START END MASK IGNORE GROUP FRACTION FLAGS` | UTIL_TraceLine and its answer |
| `EC NEW OLD` | EnableClipping and the state it returned |
| `MF NAME GROUP COMPLAIN` | FindMaterial |
| `FV MATERIAL VAR FOUND` / `HS MATERIAL VALUE` | FindVarFast and whether it found one, SetFloatValue |
| `B NAME` / `GM BUFFERED VOVERRIDE IOVERRIDE MATERIAL` / `MESH PRIM N (POS/RGBA/S,T)...` | Bind, GetDynamicMesh, the mesh drawn (MATERIAL_QUADS arrives as two triangles over four vertices) |
| `DEL NAME` | an overlay deleted |

The scripted answers, which the replay takes from the rows: a pixel visibility
question's answer and handle (`PV`), a trace's fraction and surface flags
(`T`), the clipping state `EnableClipping` returns (`EC`), and whether a
material has `$hdrcolorscale` (`FV`: a material named `*_noscale` does not).

To regenerate (only when the script changes, and say why in the commit): touch
`src/test/oracle/native/glowoverlay_golden.regen`, run
`farm/debug/src/buildcpptests --oracle glowoverlay_oracle_test`, delete the marker.
