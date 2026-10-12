# Live-gate flakes: measurements and suspected causes

This file is uncommitted until it has been reviewed. It was written by the verify lane on 2026-10-04 and is
updated as each measurement loop finishes. It measures and diagnoses only; the fixes are the physics
session's.

All runs are at **main d93e976c** in `../ss-verify`, debug config, map dm_lockdown, unless stated otherwise.
Instruments:
- `tools/3f-gatebisect --steps <gate prefix> --rounds 20 --verbose` runs the gate's own step
  functions in RUN_ORDER, with the gate's level take-down between rounds. Scratch wrapper:
  `scratchpad/flakes.sh`.
- `scratchpad/pushprobe.py` repeats one step's setup in a single level, with no reload. During the hold it
  samples the player and the prop as fast as the dev API answers (17-68 samples in 2 s), and it reads
  `cs_sv_playershadow` and `cs_sv_physics_events` before and after the hold.

Historical baseline: 61 distinct gate logs under $TMPDIR since 2026-10-02, at many tips. Counted
per pass (two passes per gate):

| step | fails / passes | per pass |
|---|---|---|
| push | 9 / 105 | 8.6 % (since pushFloor, physics-managed: 0 / 45) |
| knock-loose | 8 / 104 | 7.7 % (since pushFloor: 0 / 15, not yet enough to replace the row) |
| contact-touch | 4 / 104 | 3.8 % |
| trigger | 4 / 105 | 3.8 % (since the catch-up check and the z+8 target: 0 / 15, not yet enough) |
| hot-reload ("closed outside the gate" and others) | 4 / 105 | 3.8 % |

`tools/daily-gate` reads its known rates from this block alone (step, failures, passes, source; tab
separated), never from the prose, which quotes other counts. Keep it in step with the table above.

```rates
push	0	45	physics-managed pushFloor, 3f-gatebisect rounds 2026-10-05 (30 join,push; 10 + 5 with the steps before it)
knock-loose	8	104	historical baseline, 61 gate logs since 2026-10-02
contact-touch	4	104	historical baseline, 61 gate logs since 2026-10-02
trigger	4	105	historical baseline, 61 gate logs since 2026-10-02
hot-reload	4	105	historical baseline, 61 gate logs since 2026-10-02
```

---

## 1. push: a game bug (the push is far too slow), not gate timing

**Resolved 2026-10-05 (physics-managed): not a game bug.** The jams were the player standing on
one of eight concrete_chunk props the map lays on openFloor (stock turns the lean off on physics
ground). On clear floor (pushFloor) player and crate push at 48-52 u/s, stock's cap; push and
knock-loose moved there. Details: plan_physics_managed.md A39, tools/pushprobe.

### Rate

| run | trials | fails | regime |
|---|---|---|---|
| gate prefix `join..projectile,push`, 20 rounds, d93e976c (`fl-push20.log`) | 20 | **3** (15 %) | load median 8.0, max 51.5 (46 samples). Foreign games were up for most of it: 2-4 `hl2_linux64` in 34 of 46 samples, from DEBUG_API_DIR=/tmp/ts-lg and /tmp/ss-census-api. |
| `pushprobe.py push`, 20 holds in one level | 20 | 3 holds moved the crate < 6.2 (no gate threshold applied) | quiet: one game |
| landing lane, `join,push` only, 2 x 10 rounds, a2dfc790 and main (`$TMPDIR/ce/ld/bisect-*-push-v.log`) | 20 | 0 | not recorded |
| live gate pass 2, 52b6fa25 (`v-gate1.log`) | 1 | 1 | one game |
| gate prefix `join..knock-loose`, 20 rounds, d93e976c (`fl-pre20.log`) | 20 | **2** | load median 16.9, max 76.2. 2-5 games in every sample: heavy contention. |

The second prefix failure was a new shape: "moved it 3.1 units, 3.3 at its furthest, on a walk of
**352.4**". The player walked past the crate, not into it, with the reach check still passing
(closest < 64). That is the gate's push direction grazing a crate that was sitting off-line. It
happened once in 40 rounds, so it is a gate-geometry footnote and not the main cause.

P(0 fails in 20 | 15 %) = 0.85^20 = 3.9 %. P(0 in 20 | historical 8.6 %) = 16.6 %. **A fix needs about 40
clean rounds** before the old 8.6 % is below 3 %.

### The failure messages (all 15 seen, history included, have one shape)

    walking into the prop moved it 3.2 units, 4.5 at its furthest DURING the hold, on a walk of 32.6
    walking into the prop moved it 3.9 units, 3.9 at its furthest DURING the hold, on a walk of 31.5
    walking into the prop moved it 1.2 units, 8.3 at its furthest DURING the hold, on a walk of 35.8
    (history: 0.0-3.8 units moved, walks 28.1-41.1)

Every failure is a "JAM" hold that moved the crate less than the threshold of 4.0 (`moved > 4.0`, about
tools/live-gate:3370). In the 20-round run, 17 of 20 holds were JAMs and 3 were FREE. The JAMs settled
the crate at 1.2, 3.2, 3.9, 4.1, 5.1, 5.4, 5.6, 5.9, 6.1, 7.2, 7.2, 7.4, 9.4, 12.3, 14.2, 35.4 and 37.5
units. The threshold sits inside the JAM distribution, so the failure rate is the JAM's lower tail.

### What a JAM is, measured (pushprobe, 20 holds)

The gate's own comment calls a JAM a deadlock, "neither moves again while +forward is held"
(tools/live-gate:3279-3306). **That is wrong at d93e976c.** In the second second of each JAM hold,
the player and the crate creep forward together, at the same speed and in contact:

| hold kind | n | player u/s | crate u/s | player-crate xy gap |
|---|---|---|---|---|
| JAM | 16 | 1.3 - 12.3 | 0.6 - 12.0 | 36.2 - 37.2 (16 hull + ~20 crate half-width: touching) |
| FREE | 3 | 87 - 235 | 10 - 118 | 49 - 124 (the crate is shoved clear) |

Stock caps a walking push at **50 u/s** (`SetPushSpeedLimit( 50.0f )`, player.cpp:8233). Ours uses
the same 50 and 350 (SsPlayer.cs:96, :103), but a JAM pushes at 1-12 u/s. The creep also slowed
across one level: holds 14-18 ran at 0.6-2.1 u/s, holds 1-12 at 5-12 u/s. Over a 2 s wall-clock hold,
under ~2 u/s is a failure.

Ruled out by the same probe:
- **The snap**: `snaps` moved 0 in all 20 holds.
- **B7 not running**: per hold, `shadow moves` +35..+95 and `impulses` +19..+95
  (PlayerShadowRecovery.Update). The recovery runs every frame of a JAM, so the stale gate comment's
  "this port does not carry that path" no longer applies.
- **Push limits**: equal to stock (above).
- **The gate's reach**: the closest approach was 41.9-48.0 in every hold, so the player always arrives.

### Suspected cause (for the physics session to verify)

The push speed comes from how far ahead of the blocked player the shadow is aimed. That is
`PlayerShadow.Plan`'s leaning aim, `0.5*Origin + 0.5*(OldOrigin + OutWishVel*frameTime)`
(src/server/interop/PlayerShadow.cs:257). Stock's is player.cpp:4793-4796. So the suspects are the
inputs to that aim, then what the recovery does with the result:

1. **What OutWishVel holds in a JAM (checked: it is not a port gap).** Stock writes `m_outWishVel`
   in `WalkMove` (`+= wishdir*wishspeed`, gamemovement.cpp:1984), `Friction` (:1676) and
   `AirAccelerate` (:1744), but not in `Accelerate` (:1820). Ours writes it in the same three places
   (Movement.cs:1524, :1299, :1266). With the movement code ruled out, the open question is the
   value the shadow step actually receives (`PlayerShadowState.OutWishVel` from the probe), so log it
   in a JAM.
2. **The recovery pulls the player back toward the shadow every frame.** The probe sees 19-95
   impulses per 2 s JAM hold, one nearly every physics frame. That is `ApplyVelocityImpulse(newVelocity
   - dir*dot)` (PlayerShadowRecovery.cs, the `deltaV >= maxVel` arm). If its sign or frame differs
   from stock's (player.cpp:8404-8440), each impulse cancels the next command's advance and the pair
   creeps. Record the impulse vector per frame in a JAM to settle this.
3. **The arrival time.** `Plan` asks for `(CommandIndex+1)*TickInterval` (PlayerShadow.cs:209).
   Stock's `PostThinkVPhysics` passes `frametime` to `UpdateVPhysicsPosition`, and the ramp only
   applies inside `PhysicsSimulate`. A longer arrival is a slower controller.

4. **"Touched" from the controller contact** (the physics session's audit). Plan counts
   `IsInContact` (ContactController, set in the probe at shell_standalone_player.cpp:1672) as
   touching. Stock's plan sees only `Touch()`: PlayerRunCommand clears `m_touchedPhysObject`
   (player.cpp:3782) before PostThinkVPhysics reads it, and only VPhysicsShadowUpdate sets it from
   IsInContact (:8326). **What the probe can and cannot say:** `cs_sv_playershadow`'s counters are
   bumped in the native probe (shell_standalone_player.cpp:1688-1694), and that probe is shared by
   the command-time Plan and the B7 recovery, so per-Plan attribution needs new logging. Per hold,
   "controller-only" probes (contacts minus touches) were 125-214 in the slow JAMs (holds 14-18,
   crate 0.6-2.1 u/s) and 122-203 in the faster ones (holds 3-12, 5-12 u/s). That does **not**
   discriminate. Both sources fire throughout every JAM. FREE holds had fewer of both (contacts
   139-234, touches 9-99, against 310-391 and 14-258 in JAMs).

**How to verify:** pushprobe's JAM holds (player and crate speed in the second second) are the
measurement, and stock's ceiling is 50 u/s. A fix should move every JAM's creep toward 50 u/s,
which removes the lower tail entirely, rather than only making FREE walks more common.

Gate-side note: the 4.0 threshold and the 2 s hold are wall-clock (tools/live-gate:3315, `time.time()
+ 2.0`). The failures came at load up to 51, but the probe reproduced the slow creep with one game on
a quiet box. Load is not the cause, so do not widen the threshold.

---

## 2. trigger's setpos control: a gate bug (its target is inside the floor)

### Rate

| run | trials | fails | regime |
|---|---|---|---|
| gate prefix `join..knock-loose`, 20 rounds (`fl-pre20.log`) | 20 | **3** (15 %) | load median 16.9, 2-5 games |
| `pushprobe.py trigger`, the control alone, target z = 0 (the gate's) | 30 | **6** (20 %) | load 11, one game |
| the same with target z = 8 (above the floor) | 30 | **0** | load 12.6, one game |

P(0 in 30 | 20 %) = 0.8^30 = 0.12 %. Only the target's height differs between the last two arms.

### The failure message

    and a plain setpos left it behind, so the snap caught it: snaps 8 -> 8   (also 25 -> 25, 60 -> 60)

### Cause, measured

The control is `setpos inside.x inside.y outside.z` (tools/live-gate, end of step_trigger), which is
`-3395 5838 0`. The floor there is at about z 4: `inside` is z 92, "88 units over the floor". So the
target is 4 units **into the floor**. After a plain setpos there are two orders:
- **The next usercmd runs first.** Plan sees the shadow ~2978 units behind, snaps it, and `snaps` +1.
  The player stays at z 0.0-0.2, or is nudged up to 3.9-4.0.
- **The physics frame runs first.** PlayerShadowRecovery.Update finds the player in solid, and its
  stuck check puts them back at the last valid position, which is the shadow (stock's
  `bCheckStuck`, player.cpp:~8440). The probe shows the player end **exactly at `outside`**
  (0 units from it), `shadow moves` +1 and `snaps` +0, in all 6 failing holds.

Both orders occur, depending on whether a usercmd arrives in the tick that executes the setpos. That
is why the control flakes. It was added at 0b76b768 (2026-10-02 20:08), B7 landed at 2182466e (20:50),
and the first recorded failure is 22:43 the same day: the stuck check is what pulls the player back.
That is stock behaviour, so **this is not a game bug**.

**Fix direction (gate):** setpos to a point in open space above the inside floor (z 8 gave 30/30),
so the only way back is the touch arm. A mutation proof: with the snap disabled (`SnapSlack`
huge, PlayerShadow.cs), the fixed control must still go red.

Related lead, already fixed: the "missed OnStartTouch at seq 4868 while re-paging 1,776 events" is
the step's ENTRY check, not this control. `events_all` pages at 2000 (tools/live-gate:1525-1539)
against the server's cap of `Math.Min(m, 2000)` (src/debug_api/DebugApiServer.cs:1917), and
neither prefix run nor the probes saw an entry miss.

---

## 3. contact-touch: a gate precondition flake (crate A lands on the map's own props)

### Rate

| run | trials | fails | regime |
|---|---|---|---|
| gate prefix `join..knock-loose`, 20 rounds (`fl-pre20.log`) | 20 | **2** (10 %) | load median 16.9, 2-5 games |

### The failure messages (both failures had both lines)

    the second crate came to rest on the first (A at [-3806.39, 3034.12, 22.93], B at [-3763.18, 3004.02, 20.17])
    two crates in contact touch each other: A(395) touches [64], B(397) touches []

### Cause, measured

In **all 20 rounds**, crate A, made at the aim point from openFloor, came to rest against the map's
own physics props **#61 and #65** (sometimes #60 and #64 as well). Its listed touches always include
them. It rests raised and tilted, at z 22.7-26.3, where a crate flat on the floor sits at
19.3-20.5. Crate B is dropped onto that tilted top:
- **in 18 rounds B stayed on A**, and in all 18 **A and B were linked both ways** within the
  step's 5 s wait. The touch mechanism (CCollisionEvent::StartTouch -> g_SolverHeld ->
  EntityOverlapReport, physics.cpp:2773 / shell_standalone_base.cpp:1569) worked every time;
- **in 2 rounds B slid off** onto the floor (z ~20) on the first drop **and on the re-drop**.
  Then "B touches []" is simply true: B is not touching A.

The historical "B touches []" messages have the same shape. So this is not a game bug: the
precondition fails, and the step reports it as a touch failure as well.

**Fix direction (gate):** make crate A somewhere clear of the map's props. For example, aim at a
fixed point away from #60-#65, or check that A rests flat (z within 1 of a floor crate) before
dropping B. Alternatively gate the touch check on the precondition, so a slide-off reads as a
precondition failure. A test that the step still fails when the touch path is broken (e.g. a
mutation dropping `owner1.StartTouch( owner0 )` in DispatchStartTouch) proves the touch check
still bites.

---

## 4. knock-loose: rare and context-dependent; the walk line crosses the map's own props

### Rate

| run | trials | fails | regime |
|---|---|---|---|
| gate prefix `join..knock-loose`, 20 rounds (`fl-pre20.log`) | 20 | **2** (rounds 13, 19) | load median 16.9, 2-5 games |
| `knockctx.py`: the gate's own steps `join..prop-hurts-player`, then an instrumented copy of the knock step, 20 rounds | 20 | **0** | load 10-30, one or more foreign games |
| `pushprobe.py knock`, no prior steps, 30 holds in one level | 30 | **0** | load 31, one game |
| landing lane, `join,knock-loose`, 2 x 10 rounds | 20 | 1 | not recorded |
| live gate pass 2, 52b6fa25 | 1 | 1, but the **scrape** check: "scrapes started 36 -> 36" (the crate WAS knocked loose) | one game |

In the gate's context that is 2 in 40 (5 %), against 7.7 % historically per pass. P(0 in 20 | 7.7 %) =
20 %, so the instrumented run's 0/20 does not reject the old rate. **This loop could not catch an
instrumented failure**: the gate step reports nothing about the walk, and the copy that records it
did not fail.

### The failure messages

    walking into the pinned crate knocked it loose, once: 0 OnMotionEnabled          (all 8 historical, 2 here)
    the loosed crate scraped across the floor: scrapes started 36 -> 36              (once, 52b6fa25)

### What the instrumented rounds show

- **The walk line crosses the map's own physics props #60-#65 in every round.** These are not
  gibs. They sit within 48 units of the player-to-crate line in 20 of 20 rounds, with #61 within
  0.0-7.3 units of the line in 13 of 20. The step's comment (tools/live-gate, step_knock_loose)
  already says the floor "holds earlier steps' crates within 48 units of the walk, props 60-63".
  These are the map's.
- The coordinator's lead, "a gib in the path in 13 of 14 failures", is the **older** finding that
  the step's comment records ("42 measured 13 of 14"). It was fixed by moving knock-loose before
  impact-damage in RUN_ORDER (tools/live-gate:5466). prop-hurts-player, now the step before,
  passed its own "no debris" check in all 40 rounds. So gibs are not today's cause; map props on
  the line are the remaining candidate.
- In 5 of 20 rounds the walk went FREE (walked 249-291, closest 67-72): the knocked crate was
  shoved away down the line. Every round knocked it.
- Aside, possibly a game bug: in the isolated probe, **6 of 30 knocked crates broke**
  (`OnHealthChanged`, `OnBreak`) from a plain walk-in. Compare stock's
  ComputeEnablingImpulse/knock (props.cpp:2997-3011) with PropPhysics.KnockLoose
  (src/server/managed/Entities/Props/PropPhysics.cs:515-549), which pushes with
  `contactVelocity * otherMass` at the contact point, and check whether a stock crate survives
  the same walk.

### Suspected cause

A map prop (#61 most often) between the player and the pinned crate takes the walk. The player
shoves #61, and the shadow never reaches the crate at the 11.8 u/s it needs (1000 / 85 kg,
PropPhysics.cs:529). This is a **gate-geometry** flake, not shown directly. To prove it: (a) make
the step report `walked` and `closest` as push does, then (b) spawn the pinned crate where no map
prop is within 48 units of the line. A check that the knock still fails when
`Vars.ForceToEnableMotion` is never spent (mutation) keeps the step honest.

---

## 5. hot-reload "the game was CLOSED OUTSIDE THE GATE": unexplained; the signature equals SIGTERM

### Occurrences (all logs since 2026-10-02)

| when | tip | pass | where in the step |
|---|---|---|---|
| 2026-10-04 20:17 (`$TMPDIR/ce/ld/gate5.log`) | 79d3b8af | 1 | the first `status` after the "travelled" note |
| 2026-10-04 21:39 (`scratchpad/v-gate1.log`) | 52b6fa25 | 2 | **the same line** |

(The coordinator's third "occurrence", `scratchpad/debugapi-gaps.md`, is an analysis that quotes
these, not a run.) That is 2 in about 120 passes in total. Both died at the **same point** in
step_hot_reload: after the queued output had been delivered and the probes' heights had been read
(`reload_probe_height`), on the next `g.api.status()`.

### What it is not

- **Not a crash**: exit status 0, an orderly shutdown in the console ("Dropped player from server
  (Server shutting down)", Host_WriteConfiguration), no core in `coredumpctl`.
- **Not the gate**: every kill in tools/live-gate is `SIGKILL` (:1425, :1727, :1755, :1763; the
  debug-api client at :1425 kills its own process group), and SIGKILL cannot exit 0. The gate's
  only `quit` is at :7383 (final teardown), and it was never reached.
- **Not game code issuing quit**: the only managed `quit` is `ServerConsole.Quit`
  (src/server/managed/HostingServices.cs:248). Its callers are tf2_descent's HostService /
  CommandService, and the gate's mod `test` loads only SourceSharp.Server/Client (bin/debug/mods.yaml).
- **Not OOM**: no earlyoom or oom-kill lines in the journal; earlyoom reported 40 GB available at
  21:38:21.
- **No evidence of cross-lane signalling**: a scan of every session transcript under
  ~/.claude/projects for 04:25-04:40 UTC (21:25-21:40 PDT) found only one kill, at 21:38:06: the
  host lane's `kill 1425534` (`bash live/s1.sh`) and its kubectl child. Both kills are args-guarded,
  and its lib.sh trap kills only `LiveDriver` processes (source-sdk-sharp-host/live/lib.sh:41-64).
  run-test-legs' `reap_children` sends `kill -TERM -- -<group pid>` (tools/run-test-legs:757), but
  only to group PIDs it created during its own ~85 s life, and PIDs were advancing ~35k/min against
  pid_max ~4.2M, so reuse within a run is not plausible.

### Measured

`scratchpad/termtest.py` sent SIGTERM to a gate-launched game. It produced **exactly** this
signature: launcher exit 0 after 4.5 s, "Server shutting down", Host_WriteConfiguration, no
`] quit` echo, and nothing logged about a signal. So from the logs, an external SIGTERM (or SIGINT,
or a window-close request) cannot be told apart from whatever did this.

### Verdict and next step

**Unresolved; most likely not a game bug, but not proven cross-lane either.** Two deaths at one code
line argue against a random external signal, and nothing found so far sends one. The cheapest
discriminator is to **log the sender**. A debug-only native handler with `SA_SIGINFO` that prints
`si_pid`, `si_code` and the sender's `/proc/<pid>/cmdline` for SIGTERM, SIGINT and SIGHUP before
chaining to the engine's handler would make the next occurrence self-explaining. It belongs in a
`*_debug.cpp` (CLAUDE.md memory: debug-only console commands). Measuring a rate would take about
50 passes per expected event, so the logging is the better spend.

### Addendum 2026-10-05: the daily gate's hot-reload failure at 0f744da3 is the census race, not a crash

Report `~/.local/state/sourcesharp/daily-gate/2026-10-05-0f744da34faa.{log,out}`, pass 2: census
"0 entities, was 481", "481 lost", then "dev API stopped answering" (`status` returned `''`). An
assert minidump, `/tmp/dumps/assert_20261005073635_5.dmp`, was written at 07:36:35 inside that
live-gate step (07:28:57-07:37:29, from the step times in the report).

- **The dump is the gate's own game.** Its proc-status stream reads `Pid: 240462` (the report's
  "== game pid 240462"), `Name: hl2_linux64`, and its environment `PWD=../ss-daily-gate/bin/debug`,
  `DEBUG_API_DIR=/tmp/ss-dg-220264`, `TMPDIR=/tmp/sourcesharp-live-gate-240447/server`
  (`strings -n 6` over the dump; minidump_stackwalk is not installed).
- **The assert is Steam's, not ours**: `Assert( Failed connecting to webhelper process ):/data/src/common/html/chrome_ipc_client.cpp:1215`,
  Steam's HTML-surface IPC in steamclient.so, written by Steam's own breakpad into /tmp/dumps. It is
  background noise: 13 of the 15 assert dumps in /tmp/dumps (since 2026-10-02) carry that same line.
  No stack beyond the module was extracted.
- **The game did not crash.** After the failed step the same process passed map-reload, map-change
  and shutdown, "no assertion fired (0 found)" in its log, and it "exited when asked to quit".
- **The signature is the race fixed on main by 15a7628b** ("wait for the reloaded module before taking
  its census", which postdates 0f744da3): its message describes a pass that "read 0 entities and a
  dev API that did not answer, then carried on normally". A census of 0 is an answer, so it cannot
  be explained by the breakpad freeze alone. Verdict: the gate's race, fixed at 15a7628b; not a
  hot-reload crash. The rate block's hot-reload row (4/105) predates the fix and still includes it.
