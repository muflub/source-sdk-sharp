# `view-effects-stock.tsv`: where it came from

The fixture records view_effects.cpp's CViewEffects on a live game, before C7
moved it to C# (`Render/ViewEffects.cs`). It covers every Shake and Fade it
received, every CalcShake (with the engine random draws it made) and every
GetFadeParams.

- **Recorded at:** `client-r-c7` on `283add399`, debug, 1280x720 windowed, through
  the probe copy (`tools/probes/client/view_effects.cpp.probe.diff`,
  `probe_copies.cmake`).
- **Session:** `tools/probes/sessions/c7-vieweffects.steps`, on ss_sandbox. It
  fires the emitters station's env_shake (`emitters_shake`: amplitude 6,
  frequency 40, 1.5 s) and env_fade (`emitters_fade`: 0.4 s, hold 0.2, colour
  255 220 160, alpha 200, fade out and purge). Shakes overlap; a StopShake is
  sent, then a fresh shake is retuned with Amplitude 12 and Frequency 80.
- **Size:** 513 frames. That includes 168 shake redraws, 6 Shake messages
  (commands start, stop, amplitude, frequency) and 2 fades.

```
cmake -S . -B farm/debug/build -DSS_CLIENT_PROBES=ON && ninja -C farm/debug/build client_sourcesharp
tools/probes/c0-session OUT tools/probes/sessions/c7-vieweffects.steps --no-probe-build
grep -E ' fx v[a-z]+(\t|$)' OUT/probe.log | sed -E 's/^[0-9]+ [0-9]+ fx //'
```

| tag | what |
|---|---|
| `vl` | LevelInit |
| `vs` | Shake:<br>- curtime<br>- command, amplitude, frequency, duration<br>- the shakes alive before |
| `vf` | Fade:<br>- curtime<br>- duration and hold (in 1/512 s)<br>- flags<br>- r,g,b,a<br>- the fades alive before |
| `vc` | CalcShake: curtime, frametime, the shakes alive |
| `vcr` | one shake's redraw: its index, its offset, its roll. These are the four engine RandomFloat draws, in order. |
| `vco` | the frame's result:<br>- offset<br>- roll<br>- rumble angle<br>- the shakes left |
| `vg` | GetFadeParams:<br>- curtime<br>- r,g,b,a<br>- modulate<br>- the fades left |

The sandbox never takes some arms; `ViewEffectsTests` holds them synthetically:
- fade-in;
- stay-out;
- modulate;
- several fades averaged;
- the intro override;
- rumble-only;
- the 32-shake cap;
- a retired shake's slot (FastRemove);
- the first of equal-length shakes retuned.
