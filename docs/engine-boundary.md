# The engine boundary: what the engine actually calls, and what it does with the answer

plan_entity.md Phase 0, step **0e**. Phase 1 and Phase 5 delete native entity
machinery, and the question they cannot answer from the source is *may this
method answer NULL?* Only two things settle it: the engine never calls the
method, or the engine ignores what it returns. This document is the evidence
for **thirty-four** methods — `IServerGameEnts`'s six, `IServerGameClients`'s
nineteen and `IClientEntityList`'s nine — plus `IPlayerInfo` and
`IPlayerInfoManager` in §6, with a verdict per method and, for every verdict,
how it was established.

**An honest "unknown" is the point.** Phase 1 will act on this table. Where the
evidence does not reach, the row says so and names the experiment that would
settle it, rather than reading a plausible zero as a fact
(`a-one-sided-slot-answers-a-plausible-zero`).

---

## 1. How to read the verdicts

| verdict | means |
|---|---|
| **never called** | No live run recorded a call from the engine. It holds for the paths the runs took (§2.1) and says nothing beyond them. |
| **called, result ignored** | Called, and the engine has nothing to read back: a `void` method, or one that answers only through state the engine does not ask for. |
| **called, result NULL-tolerant** | Called, and a NULL answer is survived: the runs recorded the method answering NULL with the engine carrying on, or the stock SDK implementation answers NULL routinely. |
| **called, NULL not shown safe** | Called (or plainly reachable on a path the runs did not take), and nothing measured shows a NULL answer is survivable on every path. Treat NULL as forbidden. |
| **called, result used (non-pointer)** | Called, and the answer is an integer, a float or an out-param the engine reads. NULL is not the question; a wrong number is. |
| **unknown** | The evidence does not decide. The row says what would. |

**No verdict here rests on a call count alone.** A count says a method ran; it
does not say the engine cared. Every "result" verdict below rests on the
method's signature in the public header (a `void`, an out-param, a number) or
on a NULL answer the live run recorded and survived. Where neither reaches, the
row says so.

---

## 2. Where the numbers come from

### 2.1 The live runs

Three runs of the game on this branch, driven headlessly:

```
cd .claude/worktrees/<lane>
make headless                                   # dm_lockdown, -debugapi -debugapisocket -condebug
DEBUG_API_PID=<pid> ./debug-api cmd ss_boundary_probe_report      # server.so's counters
DEBUG_API_PID=<pid> ./debug-api cmd ss_cl_boundary_probe_report   # client.so's counters
DEBUG_API_PID=<pid> ./debug-api cmd ss_boundary_probe_reset       # zero between stages
```

The report is printed with `Msg`, so it lands in `game/mod_sharp/console.log`
and **not** in `debug-api log`, which is the managed log only
(`debug-api-log-is-managed-only`).

**HOW MANY LOADS THE NUMBERS COVER — read this before using any count.**
The table below is measured over the run marked *run 3* (lane 0e at `0a2738f2`
plus the caller-table widening), which covers **three map loads**:

| stage | what it covers | server ticks |
|---|---|---|
| S1 `load1` | process start, first `+map dm_lockdown`, ~40 s of play | 4,036 |
| S2 `changelevel` | counters zeroed, then `changelevel dm_lockdown`, ~30 s | 1,961 |
| S3 `mapcmd` | counters zeroed, then `map dm_lockdown` (server restart + reconnect), ~32 s | 2,109 |
| S4 `idle30` | counters zeroed, 30 s of play with **no** level change | 4,115 |

Two earlier runs (run 1, server only, five stages including a
`disconnect` + `map`; run 2, both sides, three stages) agree with run 3 on
every verdict. Raw captures for all three are in the lane's scratchpad
(`0e-live-server-run1.txt`, `0e-live-run2.txt`, `0e-live-run3.txt`).

**The four-stage split is not decoration — it moved two rows.**
`IServerGameClients::NetworkIDValidated` and
`IClientEntityList::GetClientNetworkableFromHandle` both read **zero** over a
map load and a minute of play, and both are non-zero the moment the level
changes. `GetPlayerState` answers NULL exactly once, and only on a level
change; over S1 and S4 it never answers NULL at all. A one-load measurement
would have called all three of those "never called" and "never NULL"
(`one-map-load-hides-a-whole-bug-class`).

**What the runs do NOT cover, and it matters:**

- **One map.** `game/mod_sharp/maps/` holds exactly one BSP (`dm_lockdown`);
  `maps *` in the console lists nothing else. The three loads are three loads
  of the same map. To extend: `make map MAP=ss_sandbox` compiles
  `maps/ss_sandbox.vmf` (needs the Wine map tools), then
  `./debug-api cmd 'map ss_sandbox'`.
- **One local player, never a remote one.** `ClientDisconnect` reads zero
  across all three runs including an explicit `disconnect` — a listen server's
  own client does not take that path. A second machine, or a second instance
  connecting over the loopback, is what would exercise it.
- **No HLTV, no Replay, no dedicated server, no save/load.** Whatever the
  engine does only on those paths was never reached, so a "never called" or a
  "NULL survived" below does not extend to them.
- **A debug build.** Counter overhead is real but small; nothing here is a
  performance measurement.

### 2.2 Why the recorded caller address is the whole instrument

The probe records `__builtin_return_address(0)` and prints it as
`module+0xOFFSET` via `dladdr`. That does two things a count cannot:

1. **It separates the engine from us.** `IClientEntityList::GetClientEntity`
   is called 62,989 times over S1 — 37,896 of them from `engine.so` and
   25,093 from `client.so`. A count alone would have read as "the engine leans
   on this hard"; half of it is the client DLL talking to itself, and that half
   moves with Phase 5a rather than constraining it.
2. **It counts distinct call sites.** Each distinct return address is one
   place in the caller that reached the method, so "three sites, one of them
   hot" is a measurement, not a guess.

One caveat: when a method is reached through a **tail call**, the return
address on the stack belongs to the tail-caller's caller, so two frames can
share one recorded site.

---

## 3. `IServerGameEnts` — six methods

Vtable order verified against `vtable for CServerGameEnts` at `0x327980` in
`server.so`. `IServerGameEnts` has a virtual destructor, which takes **two**
slots before `SetDebugEdictBase`; the slot order is read out of our own built
binary (`nm -C server.so`, §7), not assumed from the header.

| method | slot | live calls (S1/S2/S3) | verdict | how established |
|---|---|---|---|---|
| `SetDebugEdictBase` | +0x10 | 1 / 1 / 1 | **called, result ignored** | `void`. Once per level load, from one site. |
| `MarkEntitiesAsTouching` | +0x18 | 30,435 / 15,955 / 16,579 | **called, result ignored** | `void`. Every call comes from a single engine site. |
| `FreeContainingEntity` | +0x20 | 1,109 / 1,499 / 1,499 | **called, result ignored** | `void`. Three engine sites fire; the counts are 749 + 749 + 1 on a level change (one edict-table sweep per level, twice, plus one single call). |
| `BaseEntityToEdict` | +0x28 | **0 / 0 / 0** | **never called** (on these paths) | Zero across three runs and every stage. The runs include no save/load, HLTV or Replay (§2.1). |
| `EdictToBaseEntity` | +0x30 | **0 / 0 / 0** | **never called** (on these paths) | Zero across three runs and every stage, with the same coverage caveat as the row above. |
| `CheckTransmit` | +0x38 | 1,187 / 565 / 604 | **called, result ignored** | `void`; the answer is the bit vector in `pInfo`. Exactly once per client per transmit frame, and the count tracks `ClientSetupVisibility` to the call. |

**What this says to Phase 1.** `EdictToBaseEntity` and `BaseEntityToEdict` are
the two `IServerGameEnts` methods the definition-of-done requires to answer
`NULL`, and neither is reached by the engine on three map loads.
`BaseEntityToEdict` already returns `NULL` unconditionally under
`SS_CBASEENTITY_ALIAS` and nothing noticed. The paths the runs did not take are
the open side of that: a save/load run with the probe armed is what would
extend it.

**The one caveat on that.** "Never called by `engine.so`" is not "never
called". `IServerGameEnts` is handed to server plugins through the game DLL's
factory, and a plugin could reach either converter. No plugin is loaded in
these runs; `game/bin/linux64` carries `serverplugin_empty.so` and nothing
loads it.

---

## 4. `IServerGameClients` — nineteen methods

Vtable order verified against `vtable for CServerGameClients` at `0x3315c8`.
Live counts decide these rows.

| method | slot | live calls (S1/S2/S3) | verdict | how established |
|---|---|---|---|---|
| `GetPlayerLimits` | +0x00 | **not armed** | **unknown** — result is out-params by signature | Its body is in `shell_server.cpp`, which step 0e does not own. It would be asked before a map exists, so an arm would have to be at DLL-load time. To settle: add one `SS_BOUNDARY_HIT` to that body. |
| `ClientConnect` | +0x08 | 1 / 1 / 1 | **called, result used** | `bool`; one per client connect. The engine branches on it (that is what rejection is). |
| `ClientActive` | +0x10 | 1 / 1 / 1 | **called, result ignored** | `void`. |
| `ClientDisconnect` | +0x18 | **0 / 0 / 0** | **unknown** | Zero over three runs *including an explicit `disconnect`* — a listen server's own client does not take this path. To settle: connect a second client over the loopback and disconnect it. |
| `ClientPutInServer` | +0x20 | 1 / 1 / 1 | **called, result ignored** | `void`. |
| `ClientCommand` | +0x28 | 1 / 0 / 0 | **called, result ignored** | `void`. Fires on the first load only (the client's initial command), not on a level change. |
| `SetCommandClient` | +0x30 | 5 / 6 / 7 | **called, result ignored** | `void`. Three distinct engine sites, all live. |
| `ClientSettingsChanged` | +0x38 | 3 / 2 / 3 | **called, result ignored** | `void`. |
| `ClientSetupVisibility` | +0x40 | 1,187 / 565 / 604 | **called, result used (out-param)** | The engine passes a PVS buffer and reads it back. Once per client per frame; equals `CheckTransmit` exactly. |
| `ProcessUsercmds` | +0x48 | 3,936 / 1,866 / 1,998 | **called, result used** | Returns a float the engine uses. Roughly 3.3 calls per frame per client. |
| `PostClientMessagesSent_DEPRECIATED` | +0x50 | **0 / 0 / 0** | **never called** (on these paths) | Not reached. The name is the SDK's own verdict. |
| `GetPlayerState` | +0x58 | 1,216 / 580 / 619 | **called, NULL not shown safe** | See §4.1. This is the most load-bearing row in the document. |
| `ClientEarPosition` | +0x60 | 5 / 5 / 3 | **called, result used (out-param)** | The engine reads the `Vector` back. |
| `GetReplayDelay` | +0x68 | **0 / 0 / 0** | **unknown** | Not reached: no Replay in these runs. To settle: a Replay-enabled server. |
| `GetBugReportInfo` | +0x70 | **0 / 0 / 0** | **unknown** | Not reached; by its name only the engine's bug reporter asks. To settle: trigger the engine's bug reporter. |
| `NetworkIDValidated` | +0x78 | 1 / 0 / 1 | **called, result ignored** | `void`, once per *connect* (so: S1 and S3, which reconnect; not S2, which only changes level). |
| `ClientCommandKeyValues` | +0x80 | **0 / 0 / 0** | **unknown** | Not reached; nothing in this mod sends a KeyValues command. |
| `ClientSpawned` | +0x88 | 1 / 1 / 1 | **called, result ignored** | `void`. |
| `ClientVoice` | +0x90 | **0 / 0 / 0** | **unknown** | Not reached; needs a client actually transmitting voice. |

### 4.1 `GetPlayerState` — the one row that forbids NULL

**Verdict: `GetPlayerState` may not answer NULL.** The engine calls it about
once per client per frame, and the answer is a pointer to the player's
`CPlayerState`. The runs recorded exactly
one NULL answer (below), on the level-change frame, and the engine carried on.
That is one survived NULL at one site on one path. It says nothing about the
paths the runs never took — HLTV, Replay, save/load (§2.1) — and a live-only
reading that stretched it to "the engine tolerates NULL" would be wrong in
exactly the way that ships a crash to the first server that takes one of them.

**Which arm answers, which is what Phase 1 actually needs.** The probe counts
the three exits separately:

| run 3 stage | standalone arm | SDK arm | NULL |
|---|---|---|---|
| S1 first load | 1,216 | **0** | 0 |
| S2 changelevel | 579 | **0** | **1** |
| S3 `map` | 619 | **0** | 0 |
| S4 idle | 615 | **0** | 0 |

The SDK arm (`UTIL_SDKPlayerFromEdict` → `&pBasePlayer->pl`) answered **zero
times in three runs across four stages**. Every player state the engine has
ever read on this branch came out of `CStandalonePlayer::PlayerStatePtr()`.
Phase 1 can delete the SDK arm; it cannot delete the method.

The single NULL is the level-change frame. It appears on `changelevel` and on
no other stage, which is precisely the class of fact a one-load measurement
hides.

Phase 3c gives `CPlayerState` real storage in the block's server tail, which
satisfies this unconditionally. Do not narrow that to "only when a player
exists" on the strength of the one survived NULL.

---

## 5. `IClientEntityList` — nine methods

Vtable order verified against `vtable for CClientEntityList` at `0x8e8760` in
`client.so`. `CClientEntityList` inherits `CBaseEntityList` first, so the table
the engine holds is the **secondary** vtable, 0x78 further on; its nine entries
are `icliententitylist.h`'s order unchanged.

Counts are run 3 S1 (first load, ~40 s). `engine`/`client` splits the callers
by the module the recorded return address lands in.

| method | slot | live calls | engine / client.so | verdict | how established |
|---|---|---|---|---|---|
| `GetClientNetworkable` | +0x00 | 818,199 | 673,905 / 144,294 | **called, result NULL-tolerant** | Nine distinct call sites, seven of them in `engine.so`. Stock `CClientEntityList` (`game/client/cliententitylist.cpp`) answers the slot's cached pointer, which is NULL for every empty index, so NULL is the ordinary answer. Busiest method on either interface. |
| `GetClientNetworkableFromHandle` | +0x08 | **0** on the first load, **158** on `changelevel` | 0 / 158 | **never called by the engine** (on these paths) | Every live caller is one `client.so` site. Would have read "never called" from the first load alone. |
| `GetClientUnknownFromHandle` | +0x10 | 475,843 | **0** / 475,843 | **called by us, not by the engine (in these runs)** — engine path **unknown** | Every live caller is `client.so`. Zero engine calls on these paths is not proof of none. To settle: a run that exercises more of the engine's client side with the probe armed; until then treat NULL as forbidden here. |
| `GetClientEntity` | +0x18 | 62,989 (738 NULL) | 37,896 / 25,093 | **called, result NULL-tolerant** | 738 NULL answers on the first load, and the engine carried on; see §5.1 for where they went. |
| `GetClientEntityFromHandle` | +0x20 | 281,121 | **0** / 281,121 | **never called by the engine** (on these paths) | One live caller, a single `client.so` site. |
| `NumberOfEntities` | +0x28 | **0** | 0 / 0 | **never called** | Zero live calls from either module, across three runs and four stages. |
| `GetHighestEntityIndex` | +0x30 | 1,184 | 1,184 / 0 | **called, result used (non-pointer)** | Two live engine sites. Returns an `int` the engine reads; a wrong number is the risk, not NULL. |
| `SetMaxEntities` | +0x38 | 1 | 1 / 0 | **called, result ignored** | `void`, once per level load, from one engine site. |
| `GetMaxEntities` | +0x40 | **0** | 0 / 0 | **unknown** | Zero live calls in three runs. Not safe to treat as never called: the engine sets the value every level load. To settle: exercise the engine's entity debug listings (`cl_showents`, `cl_entityreport`) with the probe armed. |

`replay.so` also carries the `VClientEntityList003` string (§7's `strings`
sweep), so it may ask for this interface too. No other engine-side module under
`bin/` references the string.

### 5.1 The 738 NULLs

`GetClientEntity` answered NULL 738 times on the first load and 0 times in the
idle stage. In every stage the NULL count tracks the hit count of one engine
caller (S1: 738 NULL / 736 hits; S4: 0 / 0), so the NULLs almost certainly go
there — but the probe counts NULLs per *method*, not per *caller*, so this is a
**correlation, not a measurement**. To settle it: give the caller table a
per-caller null counter.

---

## 6. `IPlayerInfo` and `IPlayerInfoManager` — not in the build, not in the engine

The step names `IPlayerInfo`. There is nothing to instrument, for two separate
reasons, either of which alone is decisive:

1. **This game DLL does not expose it.** `playerinfomanager.cpp` is not in
   `src/game/server/server_csharp.vpc`, so `IPlayerInfoManager` is not
   compiled into `server.so` and `CreateInterface("PlayerInfoManager002")`
   fails. The startup log's interface list confirms it: `server.so` exports
   fourteen interfaces and neither `PlayerInfoManager` name is among them.
2. **No engine binary asks for it.** The string `PlayerInfoManager002` does not
   appear in *any* `.so` under the SDK Base install's `bin/` — not `engine.so`,
   not `replay.so`, not the dedicated-server modules. It is a **server-plugin**
   interface: plugins fetch it from the game DLL's own factory, and the engine
   never touches it. Compare `PluginHelpersCheck001`, which `server.so` does
   export and which the engine does call (`grep-the-engine-for-the-interface-string`).

**Verdict: never called, by anything present.** A server plugin that wanted
player info would fail its `CreateInterface` and know it. Phase 1 need not
preserve `IPlayerInfo` at all.

Commands:

```
strings -a .../bin/linux64/engine.so | grep -x PlayerInfoManager002        # no output
grep -c playerinfomanager src/game/server/server_csharp.vpc                 # 0
```

---

## 6a. `IBaseClientDLL` — seventy-nine methods, twenty-nine of them called

Captured from `shell_clientdll_probe.h`, which counted every engine entry into
`client.so`'s `IBaseClientDLL` vtable and recorded the calling address — so a row
below is evidence of the engine's behaviour and not of ours.

**THE PROBE NO LONGER EXISTS.** `plan_shim.md` §4a item 4 made this capture the
precondition for deleting it, and lane `5b-probedelete` deleted it once this
section landed: `shell_clientdll_probe.h`, its two console commands
`ss_cl_clientdll_probe_report` / `ss_cl_clientdll_probe_reset`, and the
seventy-nine arming lines in `shell_clientdll.cpp` are all gone. **This table and
`docs/clientdll-boundary.md` are now the only record of it**, and neither can be
re-derived by running anything.

**THE REGIME, AND READ IT BEFORE USING ANY NUMBER BELOW.** One process, one
capture, taken at the end of a full `tools/live-gate --keep` run on
the plan_shim integration branch with all four Phase 4 lanes merged:

| | |
|---|---|
| passes | 2 of the 21-step checklist, in ONE process |
| map | `dm_lockdown`, plus the checklist's own map change and back |
| level inits | 8 `LevelInitPreEntity`, 42 `LevelShutdown` |
| display | windowed 640x480, listen server, one bot-driven second client |
| result | **29 of 79 called, 0 refused** |

**THE FIFTY UNCALLED ROWS ARE NOT A LIST OF DEAD METHODS.** They are the methods
*this workload* never reached, and the workload has known holes: it never starts
an HLTV or Replay server, never records or plays a demo, never opens the
dropdown console, never uses splitscreen, and never asks for a save-game
screenshot. A count from one run is a FLOOR on what is live and says nothing
about the rest — the same rule the rest of this document is built on.

**The sharpest example, and the reason that warning is not boilerplate:** the
gate's `save and load` step PASSES, twice, and **not one of the nine save/restore
methods below was called** — `SaveInit`, `SaveWriteFields`, `SaveReadFields`,
`PreSave`, `Save`, `WriteSaveHeaders`, `ReadRestoreHeaders`, `Restore`,
`DispatchOnRestore` all read `calls=0`. A passing save-load step is therefore NOT
evidence that the client's save path works, and anyone deleting those nine on the
strength of this table would be deleting them on the strength of a step that
never touched them.

Two more worth naming: `RenderView` reads zero while `View_Render` takes 43,833
calls, so the engine drives the client's view through the second of the two; and
`GetScreenWidth`/`GetScreenHeight` are never asked, which is not what a reader
would guess of a rendering client.

### The twenty-nine, with their call counts

```
IBaseClientDLL::Init                                 calls=1 refused=0 engine callers=1
IBaseClientDLL::PostInit                             calls=1 refused=0 engine callers=1
IBaseClientDLL::LevelInitPreEntity                   calls=8 refused=0 engine callers=1
IBaseClientDLL::LevelInitPostEntity                  calls=6 refused=0 engine callers=1
IBaseClientDLL::LevelShutdown                        calls=42 refused=0 engine callers=1
IBaseClientDLL::GetAllClasses                        calls=467 refused=0 engine callers=3
IBaseClientDLL::HudVidInit                           calls=8 refused=0 engine callers=1
IBaseClientDLL::HudProcessInput                      calls=44090 refused=0 engine callers=1
IBaseClientDLL::HudUpdate                            calls=47037 refused=0 engine callers=1
IBaseClientDLL::HudText                              calls=6 refused=0 engine callers=1
IBaseClientDLL::IN_ActivateMouse                     calls=35865 refused=0 engine callers=1
IBaseClientDLL::IN_DeactivateMouse                   calls=11311 refused=0 engine callers=2
IBaseClientDLL::IN_ClearStates                       calls=14 refused=0 engine callers=3
IBaseClientDLL::IN_KeyEvent                          calls=2 refused=0 engine callers=1
IBaseClientDLL::CreateMove                           calls=34349 refused=0 engine callers=1
IBaseClientDLL::ExtraMouseSample                     calls=43833 refused=0 engine callers=1
IBaseClientDLL::WriteUsercmdDeltaToBuffer            calls=55503 refused=0 engine callers=1
IBaseClientDLL::View_Render                          calls=43833 refused=0 engine callers=1
IBaseClientDLL::InitSprite                           calls=38 refused=0 engine callers=1
IBaseClientDLL::ShutdownSprite                       calls=8 refused=0 engine callers=1
IBaseClientDLL::GetSpriteSize                        calls=38 refused=0 engine callers=1
IBaseClientDLL::InstallStringTableCallback           calls=144 refused=0 engine callers=2
IBaseClientDLL::FrameStageNotify                     calls=178524 refused=0 engine callers=1
IBaseClientDLL::DispatchUserMessage                  calls=13 refused=0 engine callers=1
IBaseClientDLL::OnDemoRecordStop                     calls=8 refused=0 engine callers=1
IBaseClientDLL::IN_SetSampleTime                     calls=47038 refused=0 engine callers=1
IBaseClientDLL::TranslateEffectForVisionFilter       calls=78 refused=0 engine callers=1
IBaseClientDLL::ClientAdjustStartSoundParams         calls=78 refused=0 engine callers=1
IBaseClientDLL::DisconnectAttempt                    calls=2 refused=0 engine callers=1
```

### The fifty this run did not reach

```
Shutdown
ReplayInit
ReplayPostInit
HudReset
IN_Accumulate
IN_IsKeyDown
IN_OnMouseWheeled
EncodeUserCmdToBuffer
DecodeUserCmdFromBuffer
RenderView
View_Fade
SetCrosshairAngle
VoiceStatus
SaveInit
SaveWriteFields
SaveReadFields
PreSave
Save
WriteSaveHeaders
ReadRestoreHeaders
Restore
DispatchOnRestore
GetStandardRecvProxies
WriteSaveGameScreenshot
EmitSentenceCloseCaption
EmitCloseCaption
CanRecordDemo
OnDemoRecordStart
OnDemoPlaybackStart
OnDemoPlaybackStop
ShouldDrawDropdownConsole
GetScreenWidth
GetScreenHeight
WriteSaveGameScreenshotOfSize
GetPlayerView
SetupGameProperties
GetPresenceID
GetPropertyIdString
GetPropertyDisplayString
InvalidateMdlCache
ReloadFilesInList
StartStatsReporting
HandleUiToggle
ShouldAllowConsole
GetRenamedRecvTableInfos
GetClientUIMouthInfo
FileReceived
IsConnectedUserInfoChangeAllowed
BHaveChatSuspensionInCurrentMatch
DisplayVoiceUnavailableMessage
```

**Re-taking this means restoring the probe from history first.** The two
commands above were removed with it, so there is nothing to run:

```bash
git show 5034fb3e6:src/sourcesharp/game/client/shell_clientdll_probe.h \
    > src/sourcesharp/game/client/shell_clientdll_probe.h
```

and then restore its `#include`, its seventy-nine `SS_CLIENTDLL_PROBE` arming
lines and the two `CON_COMMAND`s in `shell_clientdll.cpp` — all of which are in
that same commit, the one before lane `5b-probedelete`'s deletion. Only then do
the commands exist to be driven:

```bash
DEBUG_API_PID=<pid> ./debug-api cmd ss_cl_clientdll_probe_reset
# ... play, or run tools/live-gate --keep ...
DEBUG_API_PID=<pid> ./debug-api cmd ss_cl_clientdll_probe_report
tail -n +<lines-before> game/mod_sharp/console.log
```

The report is printed with `Msg`, so it lands in `game/mod_sharp/console.log` and
**not** in `debug-api log`, which is the managed log only.

---

## 7. The commands behind every number here

```bash
# --- live ---
cd .claude/worktrees/<lane>
make headless                                        # dm_lockdown, debug-api on, -condebug
PID=$(ps -eo pid,comm | awk '$2=="hl2_linux64"{print $1}')
DEBUG_API_PID=$PID ./debug-api cmd ss_boundary_probe_report        # server.so
DEBUG_API_PID=$PID ./debug-api cmd ss_cl_boundary_probe_report     # client.so
DEBUG_API_PID=$PID ./debug-api cmd ss_boundary_probe_reset         # zero, then change level, then report again
tail -60 game/mod_sharp/console.log                          # where the report lands

# --- which binaries ask for which interface ---
ENG="$HOME/.steam/steam/steamapps/common/Source SDK Base 2013 Multiplayer/bin"
find "$ENG" -maxdepth 2 -name '*.so' -print0 | while IFS= read -r -d '' f; do
  for s in ServerGameEnts001 ServerGameClients005 VClientEntityList003 PlayerInfoManager002; do
    strings -a "$f" | grep -qx "$s" && echo "${f#$ENG/} : $s"
  done
done

# --- our own vtable order, which names the slot offsets ---
nm -C game/mod_sharp/bin/linux64/server.so | grep 'vtable for CServerGameEnts'
nm -C game/mod_sharp/bin/linux64/client.so | grep 'vtable for CClientEntityList'
```

---

## 8. What Phase 1 and Phase 5 may take from this

Every line below holds for the paths the runs took (§2.1). A save/load, HLTV,
Replay or second-client run with the probe armed is what would widen it.

**May answer NULL / may be deleted:**

- `IServerGameEnts::EdictToBaseEntity` — never called by the engine in three
  runs.
- `IServerGameEnts::BaseEntityToEdict` — already answers NULL under the alias;
  never called by the engine in three runs.
- `IServerGameClients`' SDK arm of `GetPlayerState` — zero answers in three
  runs.
- `IClientEntityList::NumberOfEntities` — no live call from either module.
- `IClientEntityList::GetClientNetworkableFromHandle` and
  `GetClientEntityFromHandle` — only `client.so` calls them, so Phase 5a
  chooses their contract rather than inheriting it.

**May NOT answer NULL:**

- `IServerGameClients::GetPlayerState` — one survived NULL on one path is not
  evidence for the paths the runs never took; Phase 3c's real storage settles
  it.
- `IClientEntityList::GetClientUnknownFromHandle` — no engine call recorded,
  so the *frequency* is unknown; until a run shows the engine's use, the *risk*
  is not assumed away.

**Must keep answering correctly (not a NULL question):**

- `IClientEntityList::GetHighestEntityIndex` — the engine reads the integer
  1,184 times on a first load. `GetMaxEntities` belongs beside it the moment a
  run shows a caller.
- `IServerGameEnts::CheckTransmit`, `MarkEntitiesAsTouching`,
  `FreeContainingEntity` — hot, `void`, and the game is what they do.

**Still unknown, with the experiment named in the row:** `ClientDisconnect`,
`GetReplayDelay`, `GetBugReportInfo`, `ClientCommandKeyValues`, `ClientVoice`,
`GetMaxEntities`, the engine path into `GetClientUnknownFromHandle`, and
`GetPlayerLimits`' live count. `ClientDisconnect` in particular needs the
second client that step 0d's checklist brings up, so it should fall out of 0d
rather than a separate run.

---

## 9. `CStandaloneEntity`'s vtable, from the built object

Recorded by lane `3c-entityshell` at `plan_shim.md` §5 3b's request, which asks
that this file carry the number. `plan_entity.md:146` says the shell has **39**
engine slots; lane S1 counted **59** `virtual` declarations in
`shell_standalone.h` and could not reconcile the two.

**Both are right about different questions, and neither needs a running game to
settle.** `tools/vtable-dump` reads the `vtable for CStandaloneEntity` object
out of `server.so`'s `.data.rel.ro` — its entries are `R_X86_64_RELATIVE`
relocations whose addends are the function addresses — and finds each
sub-vtable by the `typeinfo for CStandaloneEntity` pointer that begins it,
rather than by guessing which words are relocated:

```
tools/vtable-dump game/mod_sharp/bin/linux64/server.so CStandaloneEntity

vtable for CStandaloneEntity  base=0x1e1d28  bytes=856  words=107
  sub-vtable 0 (words 2..):  60 slots   primary: the IServerEntity chain, IGameEntity, the class's own
  sub-vtable 1 (words 64..): 11 slots   IServerNetworkable   (9 functions + the destructor pair)
  sub-vtable 2 (words 77..): 20 slots   ICollideable
  sub-vtable 3 (words 99..):  8 slots   IGameEntity          (6 functions + the destructor pair)
  TOTAL 99 virtual slots across 4 sub-vtables
```

Sub-vtables 1–3 hold **only** `non-virtual thunk to CStandaloneEntity::...`
entries, each naming a function already in sub-vtable 0: the distinct-name count
over the whole table is 59, and over sub-vtable 0 alone it is also 59 (add
`-v` to see every slot named). The thunk tables add entry points, not functions.

| question | answer |
|---|---|
| distinct virtual functions the shell defines | **59** |
| entries in the primary vtable | **60** — the destructor takes two |
| entries across all four sub-vtables | **99** |
| slots the four ENGINE interfaces declare | **39** — IHandleEntity 2 + IServerUnknown 3 + IServerEntity 3 + IServerNetworkable 9 + ICollideable 20, plus the destructor pair |

`plan_entity.md`'s 39 is the last row: what the engine's own interfaces require,
and therefore what a boundary audit like this document is about. S1's 59 is the
first: what the class declares. They were never the same count.

The 20 that are neither belong to the shell itself — `Spawn`, `Touch`,
`VPhysicsUpdate`, `GetGameEntity` and the rest of the IGameEntity and relay
surface — and a slot census has to exclude them before "the engine calls 39
things" means anything about this boundary.

**The address in the dump above is not stable and is not part of the finding.**
It moves with every relink; the counts do not. Re-run the command rather than
comparing the number in this file to a new dump's `base=`.

`CStandalonePlayer`'s table is `0x6b8` = 1,720 bytes, 215 words, for the same
reason it is larger: it adds `IMovementPlayer` to the four above.
# Appendix: where each entry point's body lives

`sourcesharp_gamesystem.cpp` used to open with the shim's entry-point
prototypes and a paragraph over each group saying which file implements them
and why they are there. The prototypes are generated now
(`tools/entrypoint-decls` -> `src/sourcesharp/game/shared/sourcesharp_entrypoints.h`,
`plan_shim.md` §5 3e), because every one of them was a second spelling of a
signature `SourceSharpEngineApi` already declares and nothing checked the two
against each other.

A generator cannot keep prose honest, so the prose is here, keyed by the slots
each block introduced. Every heading names the functions the paragraph under it
was written about, so a reader who greps for a slot name finds the reason it
exists.

## `SourceSharp_MoveTrace`

plan_entity.md step 3e -- the trace ABI the managed movement loop runs on.

BOTH SIDES, and OUTSIDE the big `#ifndef CLIENT_DLL` block below, which is
where these first went and where they do not belong: gamemovement.cpp
compiles into client.so and server.so alike, and the whole point of the step
is that the client's replay and the server's tick run ONE copy of the loop.
Bound on the server only, the client's managed loop gets a NULL MoveTrace,
every trace answers "nothing in the way", and the player falls through the
floor -- which is exactly what the movemath gate reported.

**This paragraph was already stale when it moved, and the correction is
`plan_shim.md` §5 3e's.** It said the four are "defined in
game/shared/gamemovement.cpp rather than in this tree". They are not: that file
left both vpcs at `plan_entity.md` step 8f-remove, and all four bodies are in
`src/sourcesharp/game/shared/shell_movement.cpp`, serviced against the
`CShellGameMovement` that is running the command. What is still true is the
reason the shape is what it is -- every one of them needs the player whose
command is being run, so they refuse outside a crossing.

## `SourceSharp_PlayerRunMove`

Implemented in server/shell_standalone_player.cpp: ONE QUEUED COMMAND'S
NATIVE MIDDLE -- plan_entity.md step 3d. The run loop is managed and asks
for each command by index into the queue it chose.

## `SourceSharp_PlayerShadowProbe`

Implemented in server/shell_standalone_player.cpp: THE SHADOW'S
PER-COMMAND FOLLOW-UP -- plan_entity.md §3.10, the policy-player lane.
The state machine that used to sit behind these is managed; the probe
READS and the apply WRITES.

## `SourceSharp_EntitiesPurgeMapRefs`

plan_gamedll.md Part 4, Phase 6. LevelInit's nine irreducible steps,
defined in sourcesharp/game/server/shell_levelsteps.cpp. Server only:
there is no client half of LevelInit and never was.

## `SourceSharp_EntitiesSdkPlayerSkyboxArea`

plan_server.md Phase 8b / plan-audit C4. CheckTransmit's recipient
question, as two steps that read ONE HIERARCHY EACH and decide nothing.
Defined in sourcesharp/game/server/shell_transmitsteps.cpp; the order they
are asked in is ServerEntities.SkyboxAreaForClient's, in C#.

## `SourceSharp_ClientsStandalonePlayerEarPosition`

plan_server.md Phase 7 / plan-audit C5. ClientEarPosition's hierarchy
question, as two steps that read ONE HIERARCHY EACH and decide nothing.
Defined in sourcesharp/game/server/shell_clientsteps.cpp; the order they
are asked in is ServerClients.EarPositionForClient's, in C#.

## `SourceSharp_StringTableCreate`

plan_gamedll_after.md Phase A8. Mod-declared string tables, defined in
sourcesharp/game/server/shell_stringtables.cpp beside the seven shared
ones this is deliberately NOT a replacement for. All SIX on this side:
the server is the one that creates a table and the one that writes to it.

## `SourceSharp_FilesGameFileSize`

plan_gamedll_after.md Phase A3. The two "C" systems' native halves:
finding a game script (the engine's IFileSystem, so the READ is a step
and the PARSE is C#'s) and CPropData's KeyValues tree, which stays native
with the blocker named in shell_propdata.cpp.

## `SourceSharp_WorldNetDeclare`

plan_gamedll_after.md Part B2. worldspawn's table, built from the C#
declaration. shell_world_nettable.cpp on this side; the client's own
body is in shell_world_nettable_client.cpp.

## `SourceSharp_EntityFactoryCount`

The `sub-factory` lane. The entity factory dictionary's five questions,
defined in sourcesharp/game/server/shell_standalone_class.cpp beside
CSourceSharpFactoryDictionary itself. SERVER ONLY: there is no factory
dictionary on the client and never was, so these five slots stay NULL
there rather than being filled with a no-op that answers zero.

ALREADY EXERCISED BEFORE THIS BINDING EXISTED -- `dumpentityfactories`
is built on the same five bodies, deliberately, so that none of them is
a function the linker drops and nobody has ever run.

## `SourceSharp_EntitySetParent`

Parenting. Defined in shell_standalone_hierarchy.cpp, beside the registry
they act on rather than with the rest of the entity API, because every one
of them is that file's invariant and splitting them would put half of it
somewhere the header comment does not reach.

## `SourceSharp_EntityParentProbe`

What managed parenting asks of the world (step 2h, made live). Same file,
and for a sharper version of the same reason: the registry that used to be
beside them is gone, so what is left there is exactly the set of questions
SourceSharp.Abi's IHierarchyWorld cannot answer for itself.

## `SourceSharp_EntityOverlapReport` and 1 more

*Slots: `SourceSharp_EntityOverlapReport`, `SourceSharp_MapSpawnCreate`.*

The frame's overlap report -- plan_entity.md step 2b. Server only,
with the parenting block above it and for the same reason: touch links
are diffed on the server and what reaches the client is the effect.

## `SourceSharp_InvisibleRangeReach`

plan_entity.md step 2e -- how many handles from the engine-invisible range
have reached native code. Defined in shell_scripted.cpp; declared again
here because this file forward-declares every slot it wires rather than
including each owner's header.

## `SourceSharp_StandaloneSetModelScale`

The server's half of the model scale. See sourcesharp.h: the client's
copy is what the player sees, this one is what the skeleton attachments
hang off is built with, and what scaled_collision_rule.h asks about.

## `SourceSharp_EntityGetSolidType` and 1 more

*Slots: `SourceSharp_EntityGetSolidType`, `SourceSharp_SendUserMessage`.*

The six missing directions, for the native half of a savegame. All in
shell_entityapi.cpp, appended at its end rather than beside their pairs
so that no ledgered guard in that file moved. See sourcesharp.h.

## `SourceSharp_WorldNetDeclare`

plan_gamedll_after.md Part B2's client half, declared HERE for the same
reason the four above are: this side compiles none of the block that ends
350 lines up, so a declaration written among the server's would not exist.

## `SourceSharp_ConNPrintf`

The four IVEngineClient013 slots a fixed unmanaged signature cannot spell --
Con_NPrintf and Con_NXPrintf are varargs, GetLightForPoint and its Fast twin
return Vector by value. Defined in
sourcesharp/game/shared/shell_vtable_interfaces.cpp.

DECLARED HERE FOR THE THIRD TIME FOR THE REASON THE TWO NOTES ABOVE GIVE.
They were written among the server's declarations first and this side
compiles none of that block, so they did not exist; it cost one build here
too. IVEngineClient013 is the client's interface, so the server's four slots
stay NULL -- and the server's OWN varargs get no proxy at all, being refused
for a second reason as well.

## `SourceSharp_NetHierarchyDeclare`

Implemented per side too, for the same reason: the parallel hierarchy's blob
lives in a different object on each, and only that side knows how long its
own shell is. What C# supplies is the number of BLOB bytes, which is the same
on both. See NetHierarchyDeclare in sourcesharp.h.

## `SourceSharp_NetPublishType`

The generic table's type identity, both sides (Part 14 Step 6). Native builds
its own type table from the same shared C# list, and answers with the tier it
assigned and the id it hashed.

## `SourceSharp_RegisterUserMessage`

Implemented in shared/shell_usermessages.cpp. Shared, not server-only: the
list is registered identically on both sides because a user message travels
as an index into it. The client additionally hooks each one for dispatch.

## `SourceSharp_EffectDispatchIndex`

A name, as an entry in the engine's "EffectDispatch" network string table
(plan_tmp.md Part 8), implemented in server/shell_te_fwd_s4.cpp beside the
forward that already owned this knowledge.

Server only, and not by taste: g_pStringTableEffectDispatch is a server-side
engine interface. The client only ever reads a NAME back out of the table by
the index this answered.

## `SourceSharp_BeamCreatePoints`

The rest of Part 4's backends (C-Phase 2), same file. Fifteen drawing
primitives with no managed caller yet: C-Phase 3's lanes get theirs as each
c_te_*.cpp subtree is evicted, and a lane that had to append an ABI slot of
its own is the collision this phase exists to prevent.

## `SourceSharp_ClientExplosionCore`

C-Phase 3 lane CP's eight, same file. The particle-system subtree: Part 4
pencilled ONE ClientParticleTeSpawn( kind, params ) here and left the call at
C-Phase 3. Eight named primitives instead, because the seven TEs on
DT_TEParticleSystem share a base TABLE and not a backend -- see sourcesharp.h
over the declarations for the whole argument. Sparks needs none of them:
ClientSparks above already is its primitive.

## `SourceSharp_TempEntPoolCapacity`

C-Phase 3 lane C4's six, in client/shell_te_pool_client.cpp. The legacy temp
entity POOL (plan_tmp.md Part 5): allocation, the free list, the priority
classes, the lifetime and the update loop all move to C#, and these are the
managed pool's hands on one pooled C_LocalTempEntity. docs/te-legacy-pool-survey.md
is the map of the 3,869 lines they came out of.

## `SourceSharp_ClientEntityModelBounds`

Lane FIZZ, behaviour ledger row C6. The other question a CLIENT entity is
asked BY INDEX, and it is declared beside the origin above for that reason:
both take an index because the temp entity that asks carries one, and turning
an index into a handle on this side is row C8's unfiltered reinterpret.

## `SourceSharp_TempEntPoolReadStatics`

C-Phase 6 lane FRAME's two, same file. What C_LocalTempEntity::Frame needed
that nothing had asked for yet: the write-once state C# reads on takeover,
and the wind FTENT_WINDBLOWN accelerates towards.

## `SourceSharp_ClientBloodPuff`

C-Phase 5's four, same file. The other direction of the same pool: the six
above are the managed pool's hands on a tempent NATIVE made, and these are
what a MANAGED spawn helper needs -- construct into a reserved slot, write a
whole spawn record, and the two model questions ten of the seventeen helpers
open with. docs/te-legacy-pool-survey.md section 6 settled their shape.

## `SourceSharp_SurfacePropGameMaterial`

And the two model questions those helpers open with, in
client/shell_worldquery_client.cpp beside PointContents. ModelFrameCount is
NOT a new slot -- it is the CLIENT ARM of one that had only a server arm for
the whole port, which is the shape SideAuditTests already records three
instances of. ModelType is new, and is wired on both arms.
And the CLIENT arm of one more existing slot, same file. Found by the side
audit refusing cs_cl_te_trace: SurfacePropGameMaterial was server-only, so a
managed client asking what a surface is made of got 0 -- which is a character
code, not an "unknown".

## `SourceSharp_ClientShatterSurface`

C-Phase 3 lane CE's two, same file. One drawing primitive -- the shattered
pane's per-shard geometry, which is the last of Part 4's "particle-system"
row -- and one string-table read, which is the CLIENT mirror of
EffectDispatchIndex above. The lane's other four temp entities reuse
ClientSmoke, ClientMuzzleFlash and ClientDispatchEffect, and CTEImpact draws
nothing because stock's C_TEImpact draws nothing.

## `SourceSharp_PointContents`

The CLIENT arm of an EXISTING slot, in client/shell_worldquery_client.cpp.
Not a new slot and not a second numbering: the same PointContents managed
code has always called, answered on this side from this side's copy of the
BSP. It was server-only for the whole port, so a managed client asking what
was at a point got CONTENTS_EMPTY and no error -- the "(no model)" shape
SideAuditTests records three previous instances of. C_TEExplosion is what
noticed: stock decides fireball-or-splash with UTIL_PointContents on THIS
side (c_te_explosion.cpp:290) rather than trusting a flag from the server.

## `SourceSharp_ClientDecalStaticProp`

C-Phase 3 lane CX's four: the capabilities the last three client decal halves
were blocked on. Three of them live in client/shell_te_backends_client.cpp
beside the other decal backends; ClientTraceLine lives in
client/shell_worldquery_client.cpp, because a trace is a world QUERY and not
a drawing primitive -- see that file's header for why it is a slot of its own
rather than a second arm of the server's TraceLine.

## `SourceSharp_ClientEntityModelType`

C-Phase 3 lane DECALS's three: behaviour ledger rows C2 and C3, the two arms
of C_BaseEntity::AddDecal this hierarchy never had, plus the question the
switch between them is made from. All three in shell_te_backends_client.cpp,
beside the static prop arm they are the siblings of.

## `SourceSharp_StandaloneSequenceGroundSpeed`

Implemented in client/shell_standalone_client.cpp (Phase 10): the weapon
machine replays here, and the sequence asks must answer the same numbers
the server's copy of the model answered.

## `SourceSharp_EntitySetSkin`

How this instance wears its model. The scale has a server half too; the
SKIN reuses the existing EntitySetSkin slot, which was server-only and
inert on this hierarchy and is now dual. See sourcesharp.h.
