# `worldtext-stock.tsv`: provenance

Stock `C_PointWorldText` (c_point_worldtext.cpp), carried in our
`src/client/native/shell_worldtext_sim.cpp` until lane c2-sims, recorded live
before its layout moved to C#. `WorldTextLayoutOracleTests` replays it
against `SourceSharp.Client.Effects.WorldTextLayout`. Tolerance zero
(bit-exact); the layout draws no random numbers.

## How it was recorded

1. Base `eac02e0c` (branch client-c2-sims), with
   `tools/probes/c2sims-worldtext-probe.diff` applied to
   `shell_worldtext_sim.cpp` (`DrawModel` logs its inputs and every vertex it
   hands the mesh builder) and the client built with `-DSS_CLIENT_PROBES=ON`.
   Reverted by `cp` afterwards.
2. `tools/probes/c0-session OUT tools/probes/sessions/c2sims-worldtext.steps
   --port 27471 --arg=+map --arg=dm_lockdown --env SOURCESHARP_C2SIMS_FIXTURE_DIR=DIR`:
   five point_worldtext entities created around the steam jet's pose (fonts
   0, 4, 8, 2 and 6, so each of the five glyph tables; orientations 0, 1 and
   2; letter and line spacing on one; the rainbow on one), then 150
   `DrawModel` calls, 30 per text.

## Rows

| tag | columns |
|---|---|
| `F` | curtime, origin xyz, angles xyz, view forward xyz, right xyz, up xyz, pointworldtext_rainbowspeed_time, pointworldtext_rainbowspeed_char, world width, world height |
| `P` | text, size, spacing x, spacing y, colour rgba, orientation, font, rainbow, material, local bounds min xyz, max xyz |
| `V` | one vertex as built: position xyz, s, t, colour rgba (four per drawn glyph, stock's corner order) |

The world height starts at the glyph table's UNSCALED size (64 or 128),
whatever the text size: stock's `CalcTextTotalSize`, kept.
