# `input-stock.tsv.gz`: where it came from

Stock's input code (`game/client/in_main.cpp`, `in_mouse.cpp`,
`in_joystick.cpp`, `in_steamcontroller.cpp`, `in_camera.cpp`), recorded off a
live `dm_lockdown` before lane c6-input moved it to C# (plan_client_novalve.md
§2 cluster 13, §6 C6). `InputStockOracleTests` replays it.

Recorded at `client-c6-input` `a9d8f9fd`, debug, through the lane's PROBE
COPIES of `in_main.cpp`, `in_camera.cpp` and `in_steamcontroller.cpp`
(`tools/probes/client/*.probe.diff`, `probe_copies.cmake`) and
`tools/probes/client/c6input_probes_debug.cpp`, driven by
`tools/probes/sessions/c6-input.steps` (`sv_cheats 1`, then
`cs_cl_probe_input 420`).

The probe wraps the input system the stock code reads (`inputsystem` and
tier2's `g_pInputSystem`): raw mouse accumulators, joystick axes and count and
the Steam Controller are SCRIPTED (xorshift32, seed 0x2545F491) and every
read is logged in the order the stock code made it. The script runs five
segments over 420 user commands: keys (0-59), the mouse under every
acceleration mode, filtering, inverted and cheat-bounded pitch, strafe and
lookstrafe (60-159), the joystick under every move curve, both look curves,
virtual pegging, inversion, jlook, strafe, `joy_advanced 0`, relative axes,
hot-unplugging and the wingman hack (160-259), a fake Steam Controller with
toggling digital actions, debounce and over-long analog vectors (260-329), and
third person with the camera keys, snapto, the platformer and screen-space
modes, orthographic and back to first person (330-419). Everything between is
stock's own: the frames' ExtraMouseSample calls, CAM_Think, the sample times.

## The rows

Floats are `%.9g` (exact for a float). In time order:

| tag | what |
|---|---|
| `V name value` | a convar's value: the dump at arming, then each the script set |
| `D line` | a console command the script dispatched |
| `K button d\|u arg` | `KeyDown`/`KeyUp` on a kbutton_t, from any source (`<null>`: the joystick's internal `joyspeed`) |
| `I n` | `impulse n` |
| `S t` | `IN_SetSampleTime(t)` (the shell sets the joystick's sample time to the same t first) |
| `C seq t active pitch yaw roll curVisible curLocked mouseActive camIntercept hudSens keyBits fovAdjust third kbSample joySample` | `CreateMove` entered |
| `c pitch yaw roll fwd side up buttons impulse weaponselect mousedx mousedy seed | engine pitch yaw roll | prevMouseX prevMouseY accumX accumY kbSample joySample joyFwd joySide joyPitch joyYaw env0 env1 prevPitch prevYaw pegged(4 digits) lastFwd | camOffset(3) idealYaw idealPitch third impulse clearInput` | what it built, and the state it left |
| `E ...` / `e ...` | `ExtraMouseSample`, the same columns (`seq` 0) |
| `KS` | every main button's `state` after the event, in in_main.cpp's declaration order, then `in_cancel` |
| `T cmd pitch yaw roll off0 off1 off2 idealPitch idealYaw idealDist third movingWithMouse distanceMove intercepting ortho thirdData camButtons(6)` / `t off0 off1 off2 idealPitch idealYaw idealDist third intercepting camButtons(6)` | `CAM_Think` in and out |
| `R ok x y` | `GetRawMouseAccumulators` |
| `A code value` | `GetAnalogValue` |
| `N count` | `GetJoystickCount` |
| `B code down` | `IsButtonDown` |
| `SP 0\|1` `SI 0\|1` `SA set` | `SteamControllerInterface`, `IsSteamControllerActive`, `ActivateSteamControllerActionSetForSlot` |
| `SINIT` | the probe re-ran `InitializeSteamControllerGameActionSets` against the fake |
| `SC n` `SDH name h` `SAH name h` `SD h state active` `SAD h mode x y active` | the fake controller's answers |
| `Q cmd` | a command the Steam Controller code sent with `ClientCmd_Unrestricted` |
| `RU l r` `RS` | `SetRumble` / `StopRumble` passing through (the rumble probe's, ignored here) |

420 commands (`C`/`c`), 373 extra samples and 373 camera thinks.

## How to record it again

Configure the probe build (`cmake -S . -B farm/debug/build
-DSS_CLIENT_PROBES=ON`, `ninja -C farm/debug/build client_sourcesharp`), then

    tools/probes/c0-session OUT tools/probes/sessions/c6-input.steps --config debug \
        --env SOURCESHARP_C6_FIXTURE_DIR=DIR --arg=+map --arg=dm_lockdown

and `gzip -9 -n` DIR/input-stock.tsv. The probe copies need the stock TUs in
the farm: the recipe works until the commit that evicts them.
