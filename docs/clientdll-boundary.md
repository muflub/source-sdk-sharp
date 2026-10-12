# The `IBaseClientDLL` boundary: what the engine calls on the client DLL

`plan_entity.md` Phase 5, step **5b**. The gate sentence is

> 0e's probe shows every `IBaseClientDLL` method the engine calls answered;
> `FrameStageNotify` is one crossing per stage; the client joins.

This document is the first two thirds of that, per method, with how each was
established. It is the companion of `docs/engine-boundary.md` and not a part of
it: that one is step 0e's, over `IServerGameEnts`, `IServerGameClients` and
`IClientEntityList`, and merging a fourth interface into it would make one
lane's table depend on another lane's enum.

---

## 1. What "answered" means here, and what it does not

`IBaseClientDLL` is resolved BY NAME at DLL load (`plan_entity.md` §2), so all
seventy-nine of its POSIX vtable slots must exist and be callable whether or not
anything ever calls them. "Answered" is therefore not "the slot is present" —
that is free. It means the shell has a real body for the method and did not have
to record a refusal.

| verdict | means |
|---|---|
| **answered** | The live run recorded at least one call and the shell answered every one of them. |
| not called this run | Neither of the three stages reached it. **This is not "never called"** — see §3. |
| not called this run — **refusal armed** | The shell has no answer and says so if it is ever asked. It still returns its harmless value; it records that it had none. **The whole point of 5b's gate is that no row reads `refused=`N with N above zero**, and none does. |

**Seventy-nine, not eighty.** `public/cdll_int.h:590-797` declares eighty
virtuals, one of which — `StartStatsReporting` — is declared twice, under
`#ifdef WIN32` and again under `#ifdef POSIX`. A Linux vtable has seventy-nine
entries, and `shell_clientdll_test` asserts that number by PARSING
`public/cdll_int.h` itself rather than leaving it counted by hand: getting it
wrong shifts every name in the report by one, which is the same class of error
`docs/engine-boundary.md` warns about for `IServerGameEnts`' virtual destructor
pair. It used to assert against the probe's hand-written enum; the probe is
gone and the interface is a better witness than a transcription of it was.

---

## 2. Where the numbers come from

### 2.1 The live run

One process, three stages, driven headlessly on `dm_lockdown`. The stage split
is 0e's, for 0e's reason: a single map load hides a whole bug class, and two
rows of 0e's own table moved only on a level change.

| stage | what it covers |
|---|---|
| S1 | process start, first `+map dm_lockdown`, 30 s of play |
| S2 | counters zeroed, `changelevel dm_lockdown`, 30 s |
| S3 | counters zeroed, `map dm_lockdown` (server restart + client reconnect), 30 s |

**THE PROBE THAT PRODUCED THIS TABLE HAS BEEN DELETED**, by lane
`5b-probedelete`, once `docs/engine-boundary.md` §6a had the capture. So the run
below is how the numbers *were* taken, not something a reader can now repeat:
`ss_cl_clientdll_probe_report` and `ss_cl_clientdll_probe_reset` no longer
exist, and repeating it means **restoring the probe** from the commit before
that deletion (`git show 5034fb3e6:src/sourcesharp/game/client/shell_clientdll_probe.h`,
plus its include, its seventy-nine arming lines and its two `CON_COMMAND`s in
`shell_clientdll.cpp`).

```
export DEBUG_API_DIR=/tmp/ss-debug-api-5b
TMPDIR=/tmp/ss-5b-probe ./mod_sharp_linux64 -debugapi -debugapisocket -condebug \
    -nomessagebox -nocrashdialog -novid -windowed -w 1280 -h 720 \
    -assertlog +map dm_lockdown
# the two probe commands, as they were spelt when this was taken
```

The report was printed with `Msg`, so it landed in `game/mod_sharp/console.log`
and **not** in `debug-api log`, which is the managed log only.

The three summary lines it produced:

```
[SourceSharp] IBaseClientDLL probe: map 'maps/dm_lockdown.bsp', 27/79 methods called, 0 refused
[SourceSharp] IBaseClientDLL probe: map 'maps/dm_lockdown.bsp', 23/79 methods called, 0 refused
[SourceSharp] IBaseClientDLL probe: map 'maps/dm_lockdown.bsp', 23/79 methods called, 0 refused
```

Twenty-seven distinct methods across the three stages. **Zero refusals in any
of them**, which is the gate's first clause.

### 2.2 The caller address is the point, not the count

Every armed method records up to four distinct return addresses, rendered
`module+0xOFFSET` through `CBoundaryProbe::DescribeAddress`. A count says a
method ran; a return address says which module ran it, and whether one call
site or several did. An absolute address moves with ASLR and says neither,
which is why the probe renders it module-relative.

---

## 3. A live zero is not "never called"

The lane brief's worked example is 0e recording `BaseEntityToEdict` as never
called over three map loads, which says only that those runs never reached a
caller of it. The same reading applies to every "not called this run" row
below: the three stages are one headless deathmatch session, and a method none
of them reached is **not called in the measured sessions**, not proven dead.
Concretely, of the 52 methods this run did not reach:

- **48 sit on paths the measured sessions do not take.** They are the
  save/restore cluster, the demo and replay hooks, the matchmaking and presence
  block, the close-caption pair, `Shutdown` and `LevelShutdown`'s siblings —
  none on the path a headless deathmatch run takes. Every one of them has a real
  body in the shell.
- **2 are `GetScreenWidth` / `GetScreenHeight`.** Not called in the measured
  sessions. They are still **answered** — `ScreenWidth()` / `ScreenHeight()` —
  because a slot with a correct one-line body costs nothing and a wrong number
  here is a mis-sized HUD.
- **2 are the refusals**: `BHaveChatSuspensionInCurrentMatch` and
  `DisplayVoiceUnavailableMessage`. Both are TF-only additions whose bodies are
  entirely inside `#if defined( TF_CLIENT_DLL )`, so even a caller would reach a
  body that does nothing. They do NOT assert, crash or change what they return —
  they return the same harmless value they always did and record that the shell
  had no answer, so a live run that DOES reach one says so in the table rather
  than taking the client down. The experiment that would settle them: a TF-based
  mod, or a `chat_suspension` path this build does not have.

### 3.1 A refusal is declared and printed, never a silent no-op

The mechanism is the SERVER's, deliberately —
`sourcesharp/game/client/shell_clientdll_refusals.h` mirrors
`sourcesharp/game/server/shell_gamedll_refusals.h` method for method, because
`IBaseClientDLL` is to be implemented the way `IServerGameDLL` is. The rule both
sides answer is `plan_server.md` Part 5's: *a stub that is never called and a
stub that is called constantly look identical from outside*. `plan_entity.md`
§7 says the same thing in its own words — "a behaviour that is refused is a
`ContractBaseline` entry with a reason, never a silent no-op".

So a refused method:

- **warns**, once per call and not rate limited, naming itself and this
  document's argument, so the first person to hit one sees it rather than
  finds it;
- **counts**, per method and in total, in a FIELD on the one object the engine
  holds — not a file-scope static, which this project's C++ rules forbid;
- is **printed twice**: by `cs_cl_refusals` on demand, and by
  `IBaseClientDLL::Shutdown` on the way out, so a refusal that fired in a
  session nobody was watching still reaches the log.

```
] cs_cl_refusals
[SourceSharp] client refusals: 0 call(s) across 0 method(s)
[SourceSharp]   none -- both refused IBaseClientDLL methods are still unreached
```

Zero is the evidence. A client that did not crash is not: both refusals are
no-ops whose absence nothing would notice.

---

## 4. `FrameStageNotify` is one crossing per stage

Measured, not inferred. `ss_cl_stage_crossings` prints the shell's own call
count beside the number of times it actually crossed:

```
] ss_cl_stage_crossings
[SourceSharp] FrameStageNotify: 33962 call(s), 33962 crossing(s) into managed code
```

Both numbers are read at the same instant by one command, so they cannot drift
apart the way two separately-timed readouts do. The managed side agrees:
`cs_cl_crossings`, taken moments earlier in the same stage, reports

```
[SourceSharp]   ClientStage 33943 (18.397/frame)
[SourceSharp]   ClientFrame 10638 (5.766/frame)
```

— 33,943 rather than 33,962 because nineteen more frames' stages happened
between the two commands.

**3.19 stages per client frame, not 7.** 33,943 `ClientStage` over 10,638
`ClientFrame`. All seven stages exist, but the four `FRAME_NET_UPDATE_*` ones
are raised only when a packet is being processed, so a frame with no update
raises `FRAME_START`, `FRAME_RENDER_START` and `FRAME_RENDER_END` alone. "One
crossing per stage" is a statement about the ratio of crossings to
notifications, and that ratio is exactly 1.

---

## 5. The cost this step adds

Recorded here because `plan_entity.md`'s performance pass needs a list rather
than a fresh survey.

- **One indirect call and one null test per stage**, plus the interlocked
  increment every counted thunk pays. Seven stages defined, ~3.19 raised per
  client frame.
- **+18.4 inbound crossings per SERVER frame** in the probe run
  (`ClientStage 33943 (18.397/frame)`), which is the largest single number this
  lane adds. It is a RISE and it is said out loud here because §5 says a phase
  may only lower the budget. It is paid back by construction: Phase 6a and 6b
  hang interpolation, client think and render policy off these same stages
  instead of adding slots, so the client's per-frame crossing count stays at
  this number as they land rather than growing.
- **Every live-gate stage still passes its recorded ceiling** (`make live-gate`,
  exit 0, two passes). The rows nearest their ceilings: `hot-reload` 242.642 of
  298.368, `push` 221.790 of 313.351, `move` 215.849 of 299.377. Nothing in
  `tools/live-gate-budget.tsv` was edited.
- **One probe counter table per client DLL**: 79 × (2 × `uint64_t` + `int` +
  4 × pointer + 4 × `uint64_t` + `uint64_t`) ≈ 6.6 KB of static memory in
  `client.so`, and one `Hit` — a bounded loop over at most four pointers — per
  engine call into the interface. The busiest row, `FrameStageNotify`, has ONE
  caller address, so that loop compares one pointer.

---

## 6. The table

Slot numbers are POSIX vtable order (`public/cdll_int.h`, `WIN32` undefined,
`POSIX` defined), which is the order `ClientDllProbeId_t` declares and the order
the report prints.

| # | method | live calls S1/S2/S3 | verdict |
|---|---|---|---|
| 0 | `Init` | 1 / 0 / 0 | **answered** |
| 1 | `PostInit` | 1 / 0 / 0 | **answered** |
| 2 | `Shutdown` | 0 / 0 / 0 | not called this run |
| 3 | `ReplayInit` | 0 / 0 / 0 | not called this run |
| 4 | `ReplayPostInit` | 0 / 0 / 0 | not called this run |
| 5 | `LevelInitPreEntity` | 1 / 1 / 1 | **answered** |
| 6 | `LevelInitPostEntity` | 1 / 1 / 1 | **answered** |
| 7 | `LevelShutdown` | 4 / 2 / 5 | **answered** |
| 8 | `GetAllClasses` | 59 / 55 / 55 | **answered** |
| 9 | `HudVidInit` | 1 / 1 / 1 | **answered** |
| 10 | `HudProcessInput` | 1845 / 1882 / 1906 | **answered** |
| 11 | `HudUpdate` | 10632 / 11041 / 11045 | **answered** |
| 12 | `HudReset` | 0 / 0 / 0 | not called this run |
| 13 | `HudText` | 1 / 1 / 1 | **answered** |
| 14 | `IN_ActivateMouse` | 10549 / 10879 / 10917 | **answered** |
| 15 | `IN_DeactivateMouse` | 87 / 185 / 152 | **answered** |
| 16 | `IN_Accumulate` | 0 / 0 / 0 | not called this run |
| 17 | `IN_ClearStates` | 2 / 1 / 1 | **answered** |
| 18 | `IN_IsKeyDown` | 0 / 0 / 0 | not called this run |
| 19 | `IN_OnMouseWheeled` | 0 / 0 / 0 | not called this run |
| 20 | `IN_KeyEvent` | 0 / 0 / 0 | not called this run |
| 21 | `CreateMove` | 1820 / 1851 / 1861 | **answered** |
| 22 | `ExtraMouseSample` | 10549 / 10879 / 10916 | **answered** |
| 23 | `WriteUsercmdDeltaToBuffer` | 5460 / 5553 / 5583 | **answered** |
| 24 | `EncodeUserCmdToBuffer` | 0 / 0 / 0 | not called this run |
| 25 | `DecodeUserCmdFromBuffer` | 0 / 0 / 0 | not called this run |
| 26 | `View_Render` | 10549 / 10881 / 10916 | **answered** |
| 27 | `RenderView` | 0 / 0 / 0 | not called this run |
| 28 | `View_Fade` | 0 / 0 / 0 | not called this run |
| 29 | `SetCrosshairAngle` | 0 / 0 / 0 | not called this run |
| 30 | `InitSprite` | 30 / 0 / 0 | **answered** |
| 31 | `ShutdownSprite` | 0 / 0 / 0 | not called this run |
| 32 | `GetSpriteSize` | 30 / 0 / 0 | **answered** |
| 33 | `VoiceStatus` | 0 / 0 / 0 | not called this run |
| 34 | `InstallStringTableCallback` | 18 / 18 / 18 | **answered** |
| 35 | `FrameStageNotify` | 33925 / 35031 / 35115 | **answered** |
| 36 | `DispatchUserMessage` | 1 / 1 / 1 | **answered** |
| 37 | `SaveInit` | 0 / 0 / 0 | not called this run |
| 38 | `SaveWriteFields` | 0 / 0 / 0 | not called this run |
| 39 | `SaveReadFields` | 0 / 0 / 0 | not called this run |
| 40 | `PreSave` | 0 / 0 / 0 | not called this run |
| 41 | `Save` | 0 / 0 / 0 | not called this run |
| 42 | `WriteSaveHeaders` | 0 / 0 / 0 | not called this run |
| 43 | `ReadRestoreHeaders` | 0 / 0 / 0 | not called this run |
| 44 | `Restore` | 0 / 0 / 0 | not called this run |
| 45 | `DispatchOnRestore` | 0 / 0 / 0 | not called this run |
| 46 | `GetStandardRecvProxies` | 1 / 1 / 1 | **answered** |
| 47 | `WriteSaveGameScreenshot` | 0 / 0 / 0 | not called this run |
| 48 | `EmitSentenceCloseCaption` | 0 / 0 / 0 | not called this run |
| 49 | `EmitCloseCaption` | 0 / 0 / 0 | not called this run |
| 50 | `CanRecordDemo` | 0 / 0 / 0 | not called this run |
| 51 | `OnDemoRecordStart` | 0 / 0 / 0 | not called this run |
| 52 | `OnDemoRecordStop` | 1 / 1 / 1 | **answered** |
| 53 | `OnDemoPlaybackStart` | 0 / 0 / 0 | not called this run |
| 54 | `OnDemoPlaybackStop` | 0 / 0 / 0 | not called this run |
| 55 | `ShouldDrawDropdownConsole` | 0 / 0 / 0 | not called this run |
| 56 | `GetScreenWidth` | 0 / 0 / 0 | not called this run |
| 57 | `GetScreenHeight` | 0 / 0 / 0 | not called this run |
| 58 | `WriteSaveGameScreenshotOfSize` | 0 / 0 / 0 | not called this run |
| 59 | `GetPlayerView` | 0 / 0 / 0 | not called this run |
| 60 | `SetupGameProperties` | 0 / 0 / 0 | not called this run |
| 61 | `GetPresenceID` | 0 / 0 / 0 | not called this run |
| 62 | `GetPropertyIdString` | 0 / 0 / 0 | not called this run |
| 63 | `GetPropertyDisplayString` | 0 / 0 / 0 | not called this run |
| 64 | `InvalidateMdlCache` | 0 / 0 / 0 | not called this run |
| 65 | `IN_SetSampleTime` | 10633 / 11041 / 11045 | **answered** |
| 66 | `ReloadFilesInList` | 0 / 0 / 0 | not called this run |
| 67 | `StartStatsReporting` | 0 / 0 / 0 | not called this run |
| 68 | `HandleUiToggle` | 0 / 0 / 0 | not called this run |
| 69 | `ShouldAllowConsole` | 0 / 0 / 0 | not called this run |
| 70 | `GetRenamedRecvTableInfos` | 0 / 0 / 0 | not called this run |
| 71 | `GetClientUIMouthInfo` | 0 / 0 / 0 | not called this run |
| 72 | `FileReceived` | 0 / 0 / 0 | not called this run |
| 73 | `TranslateEffectForVisionFilter` | 4 / 4 / 4 | **answered** |
| 74 | `ClientAdjustStartSoundParams` | 4 / 4 / 4 | **answered** |
| 75 | `DisconnectAttempt` | 0 / 0 / 0 | not called this run |
| 76 | `IsConnectedUserInfoChangeAllowed` | 0 / 0 / 0 | not called this run |
| 77 | `BHaveChatSuspensionInCurrentMatch` | 0 / 0 / 0 | not called this run — **refusal armed** |
| 78 | `DisplayVoiceUnavailableMessage` | 0 / 0 / 0 | not called this run — **refusal armed** |
