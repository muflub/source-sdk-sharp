# C2 beams/sprites/ropes recording probes

Patches applied at build time to copies of stock client TUs, so the oracle
fixtures of plan_client_novalve.md §5.1 can be recorded from the code being
replaced without touching the fork. `src/client/native/CMakeLists.txt` compiles
`<tu>.cpp` from `${SS_SRC}` patched with `<tu>.diff` in place of the original
when `farm/debug/build` is configured with `-DSS_CLIENT_PROBES=ON`.

| diff | fixture | session | selection |
|---|---|---|---|
| `c_rope.diff` | `src/test/managed/Fixtures/rope-stock.tsv` | `tools/probes/sessions/c2-ropes.steps` | `select-ropes` |
| `view_beams.diff`, `beamdraw.diff` | `src/test/managed/Fixtures/beam-stock.tsv` | `tools/probes/sessions/c2-beams.steps` | `select-beams` |

Every TU these diffs patch has left the build since its recording (c_rope.cpp
at the rope port, view_beams.cpp and beamdraw.cpp at the beam port), so the
CMake loop that applied them is gone too and the diffs are kept for the
record: re-recording one means checking out the commit its fixture's README
names (444dbb28 for the ropes, a013bba2 for the beams).

How to record each one is in the fixture's README next to it.
