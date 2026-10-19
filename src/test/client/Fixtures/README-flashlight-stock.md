# `flashlight-stock.tsv`: where it came from

Stock's client flashlight, the whole of `game/client/flashlighteffect.cpp`
(C7 cluster 12): `CFlashlightEffect`'s projected texture (`UpdateLightNew`:
the swing, the two hull traces and their filter, the pull-back and its drag,
the orthonormal basis, every field of the `FlashlightState_t` it hands the
shadow manager, the locked position and the trace visualiser), the old
dynamic-light flashlight (`UpdateLightOld`), `TurnOn` / `TurnOff` /
`LightOff`, the constructor's texture and DX-level checks, and the
`r_newflashlight` change callback. Written by
`src/test/oracle/native/flashlight_oracle_test.cpp`, which compiles the
pristine file whole from the clone at `valve-sdk.lock` into a scripted world
(`flashlight_stub/flashlight_harness.h` says what is stock and what is
stand-in). `FlashlightOracleTests` replays it against
`src/client/managed/Flashlight`.

Each frame is driven as stock's `C_BasePlayer::UpdateFlashlight`
(c_baseplayer.cpp) drives it, from `C_BasePlayer::Simulate` for the local
player: with EF_DIMLIGHT on, a new `CFlashlightEffect( index )` and `TurnOn`
when there is none, then `UpdateLight( eye, forward, right, up, 1000 )`; with
it off, the effect deleted. The player's index is 1.

Two scenarios: `new` (DX level 95, the border-colour texture) walks every arm
of the projected texture, then the old flashlight (near, far, and walls either
side of its falloff's 500-unit knee) and the switches between them, then
`TurnOff` / `TurnOn`; `dx6` (DX level 60, no border colour) shows
the constructor forcing the old flashlight and the callback refusing the new
one, then `dx9-noborder` lights the projected texture with the other texture.

To regenerate (only when the scenario changes, and say why in the commit):
touch `src/test/oracle/native/flashlight_golden.regen`, run the oracle test
(`farm/debug/src/buildcpptests --oracle flashlight_oracle_test`), delete the
marker.

## The stand-in answers

The managed replay gets the same answers from the rows, so they are part of
the scenario:

- A trace is a swept box against the scenario's boxes (`WB` rows): a box
  stops it when its contents meet the mask and, for an entity's box, the
  subject's filter says to hit it. The nearest entry, backed off by
  DIST_EPSILON (0.03125) and never below zero, is the fraction; a start inside
  a box is `startsolid`, and a box the whole sweep stays inside is `allsolid`
  with fraction 0. The `T` row carries the request and the answer.
- `CL_AllocDlight( key )` answers the light already holding the key, cleared,
  or else the first expired one, cleared and keyed.
- The shadow manager numbers flashlights from 0 in creation order.
- `GetDXSupportLevel` and `SupportsBorderColor` are the `S` row's.

## The rows

Floats are their IEEE bits in hex (`%08x`); masks and contents are hex.

| tag | columns |
|---|---|
| `S` | scenario: name, DX level, border colour supported |
| `V` | a ConVar the scenario set, and the value it set |
| `CV` | a ConVar's value after a set or a revert (`r_newflashlight`'s callback shows here) |
| `WC` | the world was cleared |
| `WB` | a box: mins, maxs, contents, what it is (`world`, or the entity the filter is asked about) |
| `U` | a frame: number, curtime, EF_DIMLIGHT, the player's movetype (2 walk, 9 ladder), eye, forward, right, up |
| `X` | `CTextureReference::Init`: texture, group, complain |
| `NEW` / `DEL` | the driver made / deleted the effect |
| `ON` / `OFF` | the scenario called `TurnOn` / `TurnOff` directly |
| `F` | the filter was asked: the entity, the mask, its answer (harness-side; the replay does not ask a filter) |
| `T` | a trace: `hull` or `line`, start, end, mins, maxs, mask, then the answer: fraction, end, startsolid, allsolid, what it stopped on |
| `C` / `W` | `CreateFlashlight` / `UpdateFlashlightState`: the handle, then the state's origin, quaternion (x y z w), near, far, horizontal and vertical FOV, quadratic, linear and constant attenuation, colour (4), texture, texture frame, shadows enabled, draw shadow frustum, shadow map resolution, filter size, slope-scale depth bias, depth bias, jitter seed, shadow attenuation, shadow quality |
| `P` | `UpdateProjectedTexture`: handle, force |
| `D` | `DestroyFlashlight`: handle |
| `A` | `CL_AllocDlight`: key |
| `L` | `TouchLight`: the light's key, flags, origin, radius, r, g, b, exponent, die, decay, minlight, style, direction, inner and outer angle |
| `BX` / `LN` | the trace visualiser's box and line overlays |
| `Q` | after a frame, each keyed light in the pool: slot, key, die |
| `END` | the end of the run |
