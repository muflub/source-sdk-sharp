# `screenspaceeffects-stock.tsv`: where it came from

Stock's screen-space effect manager, the whole of
`game/client/ScreenSpaceEffects.cpp` (C7): `CScreenSpaceEffectManager`'s
registration list (each `ADD_SCREENSPACE_EFFECT` pushes onto the head, so the
newest registration is walked first), `InitScreenSpaceEffects` (the
`-filmgrain` parameter enables the effect of that name first; every effect's
enabled flag is read, its `Init` run, and the flag given back),
`ShutdownScreenSpaceEffects`, `GetScreenSpaceEffect` (`Q_stricmp`, and a
warning for an unknown name), parameters, enable and disable by name and by
effect, `DisableAllScreenSpaceEffects`, `RenderEffects`, and the file's own
`CExampleEffect`. Written by
`src/test/oracle/native/screenspaceeffects_oracle_test.cpp`, which includes the
pristine file from the clone at `valve-sdk.lock` into a translation unit with
recording effects registered before it (alpha, and beta, which starts
enabled) and after it (filmgrain, and a registration with no effect)
(`sse_stub/sse_harness.h` says what is stock and what is stand-in).
`ScreenSpaceEffectManagerOracleTests` replays it against the managed
`ScreenSpaceEffectManager`.

The golden carries its stimulus: each `S` row is one step of the script, and
the rows after it are what stock did.

| row | meaning |
|---|---|
| `S STEP ARGS` | a step: `init [cmdline]`, `shutdown`, `get NAME`, `setparams-name NAME KV VALUE [1]`, `setparams-effect NAME KV VALUE`, `enable-name`/`enable-effect`/`disable-name`/`disable-effect NAME`, `disableall`, `render X Y W H`, `example-enabled`; `-` is no effect or no KeyValues, a trailing `1` sets `example_param` |
| `E NAME CALL ARGS` | a recording effect's method was called |
| `G NAME` | the effect the lookup answered, `(null)` for none |
| `W TEXT` | the subject's warning |
| `X 0/1` | CExampleEffect::IsEnabled |

To regenerate (only when the script changes, and say why in the commit):
touch `src/test/oracle/native/screenspaceeffects_golden.regen`, run
`farm/debug/src/buildcpptests --oracle screenspaceeffects_oracle_test`, delete
the marker.
