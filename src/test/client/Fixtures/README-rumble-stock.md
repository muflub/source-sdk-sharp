# `rumble-stock.tsv`: where it came from

Stock's rumble mixer (`game/client/c_rumble.cpp`, `CRumbleEffects`), recorded
off a live `dm_lockdown` before lane c6-input moved it to C#
(plan_client_novalve.md §6 C6: "`c_rumble` as a managed user-message
handler"). `RumbleStockOracleTests` replays it.

Recorded at `client-c6-input` `a9d8f9fd`, debug, through the PROBE COPY
`tools/probes/client/c_rumble.cpp.probe.diff`, in the same session as
`input-stock.tsv.gz` (`cs_cl_probe_rumble 400`).

Two things about stock that the recording shows and the port keeps:

- `UpdateRumbleEffects` gives up when `C_BasePlayer::GetLocalPlayer()` is
  NULL, which it always is on this build, and `ClientModeShared::Update` (its
  only caller) is not called either. So the mixer never ran past its alive
  check: an arm we bypassed, ported, not deleted. The probe drives it once a
  frame with the check bypassed.
- `GenerateSquareWaveEffect` writes past its 30 samples when 30 is not a
  multiple of `2 * steps`. For `RUMBLE_PHYSCANNON_LOW` (6 cycles, right
  channel) the two extra writes land in the waveform's `numSamples`
  (0.25f's bits, 1048576000) and in `RUMBLE_PHYSCANNON_MEDIUM`'s first left
  sample (0.25). The `W` rows show both.

## The rows

| tag | what |
|---|---|
| `W index numSamples l,r x30` | the waveform table after `Init` |
| `V cl_rumblescale v` | the scale in force |
| `E curtime effect data flags` | `RumbleEffect` (the user message's call) |
| `RR scale` | the `random->RandomFloat( 0.1, 1 )` a `RUMBLE_FLAG_RANDOM_AMPLITUDE` start drew |
| `SS shake balance` | `UpdateScreenShakeRumble` |
| `EN 0\|1` | `EnableRumbleOutput` |
| `U curtime paused scale left right` | one `UpdateEffects`: the motor values sent to `SetRumble` |
| `CH c in_use waveform start priority flags scale` | each of the 3 channels after it |

400 frames.
