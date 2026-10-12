# `soundscape-stock.tsv`: provenance

This is the client's `C_SoundscapeSystem`, recorded live before it moved to C#.
It is stock `game/client/c_soundscape.cpp`, moved whole into
`src/client/native/shell_soundscape_client.cpp` by plan_entity 7e, with the
de-entitied arms that file's header lists. `SoundscapeOracleTests` replays it
against the managed client soundscape system with zero tolerance (bit-exact).

## How it was recorded

- **Probe.** `tools/probes/c2sims-soundscape-probe.diff`, applied at base
  `a9d8f9fd` and reverted by `cp` afterwards, wraps each engine call the
  system makes and logs it. The calls are `EmitAmbientSound`,
  `GameSound_Emit`, `GameSound_Stop`, `SetRoomType`, `SetPlayerDSP`, and the
  set or revert of `dsp_volume` and `snd_soundmixer`.
- **Script.** The diff also runs a fixed script from the top of the
  system's `Update` (`SsProbeScript`).
- **Draws.** Every draw comes from the probe's seeded stream: both the
  client's `random` and vstdlib's `RandomFloat`/`RandomInt`, which
  `RandomInterval` uses. Draws are logged only while the system's `Update`
  runs.
- **Recorder.** `cs_cl_probe_sim soundscape 1000`, from
  `tools/probes/client/c2sims_probes_debug.cpp`, built with
  `-DSS_CLIENT_PROBES=ON`.
- **Session.** `tools/probes/sessions/c2sims-soundscape.steps`, run on
  dm_lockdown with `--port 27471`.
- **Scripts.** `soundscapes_c2probe.txt` (beside this file) was copied in as
  the map's own `scripts/soundscapes_dm_lockdown.txt`. It reaches every
  branch of the parse that the shipped scripts leave unexercised: `dsp_player`,
  `dsp_volume`, `soundmixer`, `attenuation`, interval soundlevels, unknown
  keys, an empty `rndwave`, `positionoverride`, a missing sub-soundscape and
  the recursion cap.

## The script, by frame

| frame | action |
|---|---|
| 1 | Shutdown + Init (the S rows before the first push stop what the server's soundscape had running; the replay starts after them), then two pushes that put `m_params` in a known state: (-2, ent 0) then (-1, ent 0) |
| 2 | force `c2probe.full`, radius 48 |
| 150 | force `e3_c17_01_battle`, radius 64 |
| 300 | push `c17_02_street_lower_1`, ent 1, bit 0 at (100, 200, 64) |
| 450 | push `c17_02_street_upper_2` at (300, 200, 64): the ambient siren is reused, and the wall loop restarts at its new position |
| 600 | the same push again (deduped) |
| 620 | push index 9999 (refused) |
| 650 | push (-1, ent 0) (latched; starts nothing) |
| 700 | `cl_soundscape_flush`'s body |
| 720 | push `c17_02_street_upper_1` |
| 800 | `stopsoundscape` |
| 850 | force `c2probe.full` again |
| 900 | force a name that does not exist |

Server pushes during the recording are dropped and logged as `X dropped`;
there were none.

## Findings the replay encodes

- **A forced soundscape at frame 2 hears none of its positioned sounds.**
  `ForceSoundscape` starts the soundscape at once, but the forced box
  corners (`localBits` 0x7) are written only by the next `Update`. The
  first force therefore runs with the bits the last push left, which is
  zero here. Stock does the same with `playsoundscape`.
- **A `playrandom` with no soundlevel key emits at soundlevel 0.** Its
  interval is zero-initialised (`randomsound_t::Init`).

## Rows

| tag | columns |
|---|---|
| `F` | frame, curtime, frametime, MainView origin xyz, forward xyz, right xyz, `soundscape_fadetime`, IsBeingRestored |
| `X` | a script action: `reset`, `push idx ent bits p0xyz`, `force name radius`, `name <soundscape the next push names>`, `flush`, `stop` |
| `RF` / `RI` | a draw: min, max, result |
| `A` | EmitAmbientSound: wave, volume, pitch, flags |
| `E` | GameSound_Emit from SOUND_FROM_WORLD on CHAN_STATIC: wave, volume, soundlevel, pitch, flags, origin xyz |
| `S` | GameSound_Stop (SOUND_FROM_WORLD, CHAN_STATIC): wave |
| `D` | `room n` (SetRoomType) or `player n` (SetPlayerDSP) |
| `V` | convar: name, `set value` or `revert` |
| `Q` | after the frame: current soundscape, looping count, random count, looping id |

The indices in `push` rows are the live index, which includes the HL2 VPK
soundscape files. The replay resolves them by the name on the preceding
`name` row.
