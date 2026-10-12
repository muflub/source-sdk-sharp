# TF2 port — Stage 4: the playable milestone (acceptance gates; no new subsystems)

Status: DRAFT, uncommitted. Part of `docs/plans/plan_tf2_port.md` (§0 D4).
Requires Stages 0–3. This stage adds **instruments and gates**, not features;
a feature gap found here goes back to its stage as a bug with a failing test.

Rules that bind this stage (from CLAUDE.md): a check that cannot fail is not
evidence — every new gate step is first shown red by breaking its subject;
keep the second map load in any gate; concurrent games need their own
`TMPDIR`/`DEBUG_API_DIR`/`DEBUG_API_UI_PORT`/`-port`; the live gate runs once
a day from `tools/daily-gate` — an agent runs `make live-gate` by hand only
to diagnose, or when the user asks; argv must stay under 512 bytes.

## Phase 4.1 — Launch targets

**Make**: Makefile `run-tf2`, `headless-tf2` = the Stage 0.2 targets with
`RUN_MOD_ID=tf2 MAP?=ctf_2fort` (thin aliases, no new logic);
`tools/local_launch.py` accepts `--mod tf2`. `make join` already takes
`JOIN_MOD` (`Makefile:483`).

**Gate**: `tools/launcher-name-test` green; `make -n run-tf2` prints
`-mod tf2`.

## Phase 4.2 — Headless scripted round gate (`tools/tf2-gate`)

A python driver over `tools/debug-api` (the pattern is `tools/live-gate`'s
`step_*` functions, `tools/live-gate:2088-3123`) that starts a dedicated
`-mod tf2` server plus two headless clients (their own instance dirs,
`-instance`), and runs steps:

1. `join`: both clients connected, `[SourceSharp/Hud] first paint` seen on
   each (assert the join before trusting any client reading).
2. `teams`: auto-balance put them on different teams (`/entity` `m_iTeamNum`
   2 and 3).
3. `classes`: `joinclass` scout / heavy; `tf_player_data` class 1 / 6; max
   health 125 / 300; measured speed over 2 s within 5 % of 400 / 230.
4. `combat`: heavy fires at the scout at 200 u; damage events ≥ 1 with
   `hitgroup` present; crit roll logged ≥ 0 (stimulus non-zero: the scout
   loses health).
5. `heal`: scout switches to medic, heals the heavy from a forced 150 to
   300 within the stock time band; overheal to 450.
6. `ctf` (map `ctf_2fort`): scout teleported to the enemy intel, picks up,
   teleported to the own cap zone → `team score 1`, flag home, round
   continues; forced `tf2_dev_win` → `TEAM_WIN → RESTART → PREROUND`.
7. `changelevel pl_badwater` (**the second map load**): both clients
   re-paint; census of managed TF2 classes (`team_train_watcher`,
   `team_control_point`×N, `trigger_capture_area`×N, `info_player_teamspawn`×N)
   equals the recorded counts.
8. `payload`: a blu client on the cart → progress increases over 10 s;
   away for 30 s → recede observed (progress decreases); checkpoint 1
   capture after a `setpos` push → control point owner changes.
9. `mode switch`: `sv_mode null` → warmup → `PREROUND`; `sv_mode pl` back.
10. `teardown`: all three processes exit; thread count of the server
    (`/proc/<pid>/task`) back to the post-join baseline before exit.

Each step records numbers to `tools/.tf2-gate/<run>/report.tsv` with the
tip sha (`TIP=$(git rev-parse HEAD)`) and the exit code (never behind a
pipe). A step that cannot measure (socket missing, no first paint) is a
**failure**, not a skip.

**Proof it can fail**: for steps 3, 6 and 8 the commit message records a
mutation run (`tf_player_data` class write disabled → step 3 red; flag
`Capture` early-return → step 6 red; watcher speed 0 → step 8 red), restored
from a `cp` snapshot with a fresh mtime (never `git checkout --`).

**Gate**: 3 consecutive green runs at one sha; `make tf2-gate` target
(not in `make test`; it is a live gate).

## Phase 4.3 — The daily live gate's `tf2` leg

**Make**: `tools/live-gate --mod tf2` runs `tools/tf2-gate`'s steps as
live-gate steps (reuse the driver; one scenario file
`tools/live-gate-scenarios/ctf_2fort.json` + `pl_badwater.json`), budget
rows recorded with `tools/live-gate --record` into `tools/live-gate-budget.tsv`
(recorded from a build, never typed), skips in `tools/live-gate-skips.tsv`
only for per-tree build output. `tools/daily-gate` gains the leg; read the
**next daily report** before building on it (a new object order is a new one).

**Gate**: one hand run `tools/live-gate --steps <tf2 ids> --passes 3`
green; the next `tools/daily-gate --status` shows the leg green at main's tip.

## Phase 4.4 — Human acceptance

Two humans play one full round each on `ctf_2fort` and `pl_badwater` under
`make run-tf2` + `make join JOIN_MOD=tf2` on the LAN (`+sv_lan 1`), all
nine classes tried at least once between them. Findings go to
`docs/plans/tf2/acceptance_2026-xx-xx.md` as a table (what, expected stock
behaviour with file:line, observed, stage to fix). `pl_upward` is the
second Payload gate if the first passes.

## Stage 4 exit (the D4 milestone)

- `make check` green; `tools/tf2-gate` 3/3 green; the daily gate's `tf2`
  leg green at the tip; the human acceptance table has no `blocking` rows.
- `docs/plans/tf2/milestone_results.md` quotes: census counts per map,
  measured class speeds, damage at two ranges, the prediction report under
  fake lag, and the gate's step timings — each with its command and sha.
- Everything deferred is a row in `docs/plans/tf2/deferred.tsv`
  (`item<TAB>stock file:line<TAB>stage<TAB>reason`), and every dropped
  item is a `docs/divergences.md` row (D5 list + the GC/Steam arms).
