# `soundenvelope-stock.tsv`: provenance

Stock's CLIENT `CSoundControllerImp` (`game/shared/soundenvelope.cpp`, a
fork file still in client.so), recorded live before the client controller
moved to C#. `SoundEnvelopeOracleTests` replays it against the shared
`SourceSharp.Shared.SoundEnvelopeController`. Tolerance zero (bit-exact).

## How it was recorded

The fork file cannot be patched, so the recorder drives it from outside:
`cs_cl_probe_sndenv <frames>` in `tools/probes/client/c2sims_probes_debug.cpp`
(`-DSS_CLIENT_PROBES=ON`) creates one patch on the local player
(`ambient/wind/wind1.wav`, `CHAN_STATIC`), runs the start of a fixed script,
and `C2SimsProbe_Tick`, patched into a client system's `PreRender` by
`tools/probes/c2sims-sndenv-probe.diff` (applied at base `e6f017cb`, reverted by
`cp` afterwards), issues the timed commands and reads the volume and pitch
back each frame through `SoundGetVolume`/`SoundGetPitch`. Session:
`tools/probes/sessions/c2sims-sndenv.steps`, `--port 27471 --arg=+map
--arg=dm_lockdown`, 200 frames.

The script: Play(0, 100), a volume ramp to 1 over 0.5 s, a pitch ramp to 150
over 1 s and a pitch command (80 over 0.3 s) queued 0.75 s ahead; at frame 40
a volume ramp to 0.4; at 60 a three-point pitch envelope (140/0.2, 60/0.3,
110/0.15 s, each min equal to its max so the draws do not matter); at 110 a
volume command (0.9 over 0.2 s) 0.1 s ahead; at 140 a second Play(0.5, 95);
at 160 a 0.4 s fade-out.

## The finding the replay encodes

The client controller updates in its game system's `Update`, which runs
before the frame's clock advances: the update in frame k sees frame k-1's
curtime. The script's first frame therefore steps zero.

## Rows

| tag | columns |
|---|---|
| `C` | curtime when called, the command, its arguments (and the envelope's returned length) |
| `W` | curtime, frametime, volume, pitch (once a frame, in PreRender) |

## Live check after the port (49aad3e2)

The same session, rerun with the probe build after `soundenvelope.cpp` left
client.so, so every `SoundGetVolume`/`SoundGetPitch` the recorder reads comes
from the managed `ClientSoundController`: 200 `W` rows, 136 distinct
volume/pitch pairs, the same ten commands. Swapped in for this fixture (by
`cp`, restored afterwards), `SoundEnvelopeOracleTests` passed on it bit-exact:
the managed controller, on the live clock, steps exactly as the model of stock
says it should, including the one-frame lag. No map gives this client a
sliding client-side physics object on demand, so the friction path
(`physics.cpp`'s `PhysFrictionSound`) was driven directly by
`cs_cl_probe_friction` (tools/probes/client/c2sims_probes_debug.cpp,
session tools/probes/sessions/c2sims-friction.steps): 120 frames on
`Metal_Box.ScrapeRough`, then silence. `cs_cl_crossings` read no
ClientSoundController op before, and after: Create 1, Play 1, ChangeVolume
119, ChangePitch 119, Destroy 1 (the Destroy is physics.cpp's own
UpdateFrictionSounds, 0.1 s after the last update).
