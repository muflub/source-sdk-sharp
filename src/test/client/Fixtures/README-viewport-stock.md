# `viewport-stock.tsv`: where it came from

Stock's `CViewportClientSystem`, the whole of `game/client/vgui_game_viewport.cpp`
(C7): the game system CHLClient::Init hands the system list, whose `Init` lays
the client mode out (`g_pClientMode->Layout()`) and answers true, whose every
other stage is empty, which names itself `CViewportClientSystem` and has no
per-frame half. Written by `src/test/oracle/native/viewport_oracle_test.cpp`,
which compiles the pristine file from the clone at `valve-sdk.lock` with its
`igamesystem.h` over a recording client mode (`viewport_stub/viewport_harness.h`
says what is stock and what is stand-in). `ViewportClientSystemOracleTests`
replays it against the managed `ViewportClientSystem`.

| row | meaning |
|---|---|
| `S STAGE` | the system was asked: `Name`, `IsPerFrame`, or an IGameSystem stage |
| `L Layout` | the client mode's Layout was called |
| `R VALUE` | the stage's answer |

To regenerate (only when the script changes, and say why in the commit):
touch `src/test/oracle/native/viewport_golden.regen`, run
`farm/debug/src/buildcpptests --oracle viewport_oracle_test`, delete the marker.
