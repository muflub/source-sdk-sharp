# `tempents-*-stock.tsv`: where they came from

The decisions still native in `game/client/c_te_legacytempents.cpp`, recorded
off a live game before lane c2-fx moved them to C# and the TU left client.so
(plan_client_novalve.md §2 cluster 4; integrator's round-2 item 3). Recorded
at `client-c2-fx` `07f83455` on client-novalve `a9d8f9fd`, debug, through the
lane's PROBE COPY (`tools/probes/client/c_te_legacytempents.cpp.probe.diff`,
`probe_copies.cmake`) driven by `tools/probes/sessions/fx-tempents.steps`:
dm_lockdown's openFloor stand, two `cs_te_fire all` volleys and two
breakmodels in view, and the probe's `cs_cl_probe_tebattery` (147 sprite
tempents over every render fx 0..20 and seven render modes, a quarter of
them FTENT_BEOCCLUDED facing towards or away; 21 studio tempents; every
muzzle-flash type through both MuzzleFlash overloads, first person and not,
on the local player and on entry 0; PlaySound over every hit sound, nine
vertical speeds and three damps, three times each; AttachTentToPlayer over
good and bad clients and models). `tools/probes/tempent-select` cut the log
into the four files.

Every random draw (`fxr f|i lo hi value`) is recorded where it was made: the
probe copy draws through FxProbeRandomFloat/Int, the same engine stream.

## Rows

`tempents-render-stock.tsv.gz` (one row in four of each kind, an IsTransparent kept with the GetRenderGroup it answered, every row of a
kind/fx/mode seen fewer than 64 times, every breakable-helper event):

| tag | what |
|---|---|
| `tfx-in` / `tfx-out` | C_LocalTempEntity::ComputeFxBlend past its frame guard: the tempent, frame, render fx, alpha, mode, index, curtime, origin, view origin and forward; then the blend and the alpha it left |
| `ttr` | IsTransparent: the model's translucency and the mode |
| `trg` | GetRenderGroup's answer: mode, the blend it read, the two-pass flag, the group |
| `tdm-in` / `tdm-a` | DrawModel: mode, flags, alpha, tempent_renderamt, normal, origin, main view origin, model type, frame, sprite scale, colour, fx, skin; then the alpha after the occlusion arm |
| `tbh-insert` / `tbh-remove` / `tbh-clear` / `tlo` | the breakable helper's groups and the lighting origin each breakmodel draws with |

`tempents-muzzle-stock.tsv` (re-cut from a later run of the same session,
after the probe learned Combine_NPC's matrix attachment and elight and the
CRT draws of Vector::Random):
`tmf-call1`/`tmf-call2` (the two MuzzleFlash
overloads' arguments), `tmf-begin`/`tmf-end` (a variant's scope), `tmf-fx`
(an FX_MuzzleEffect* call), `tmf-renderable`, `tmf-att` (the attachment
answered), `tmf-create`/`tmf-cfg` (the emitter and its settings), `tmf-add`
(each AddParticle: material, position), `tmf-simple`/`tmf-trail` (every
particle's fields as the variant left them), `tmf-attm` (the matrix
attachment), `tmf-lightcvar`/`tmf-light`/`tmf-elight` (muzzleflash_light, the
owner, the elight allocated).

`tempents-sound-stock.tsv`: `tps-in` (hit sound, damp, velocity, origin),
`tps-params` (the sound script's parameters), `tps-emit` (what was played).

`tempents-misc-stock.tsv`: `tat-in`/`tat-out` (AttachTentToPlayer), `teb-in`
(EjectBrass's type, `cl_ejectbrass`, the shell model), `tbat-*` (the
battery's own spawns).

## How to record them again

As `README-ieffects-stock.md`, with `fx-tempents.steps`, then
`tools/probes/tempent-select OUT/probe.log src/test/client/Fixtures`. The
probe copy needs `c_te_legacytempents.cpp` in the farm: the recipe works
until the commit that evicts it.
