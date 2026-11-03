# F13: `ITFObjectives`, the CTF and Payload feeds, with progress

Base: `dae055998`. Worktree `../ss-tf2ai-F13`, branch
`tf2ai-F13`. Size M, three commits. Fit: Qwen (GLM-able if split: commit 1
alone is a GLM brief). Depends on nothing; F14 depends on this.

## Goal

A read-only feed of the map's objectives as every client's HUD shows them,
one record per objective, refreshed by a server tick, with a per-team
"last progress" the AI reads to decide a team is stuck (main plan C.6.1).
No behaviour changes.

## What exists

- CTF: `src/tf2/server/Entities/Ctf/ItemTeamFlag.cs:63` (`Team`, `Status`
  `Home/Stolen/Dropped` at `:171-179`, its `TFFlagNetState`),
  `FuncCaptureZone.cs:32`; the capture event path (grep `teamplay_flagevent`
  or `FlagCaptured` under `src/tf2/server/Entities/Ctf`).
- Payload: `src/tf2/server/Entities/Payload/TeamTrainWatcher.cs:83`
  (`TrainSpeedLevel` `:195`, `IsDisabled` `:217`, `AreaCap` `:220`, the
  train and nodes `:1200-1206`); the cart is the core `func_tracktrain`
  (`src/server/managed/Entities/Brushes/FuncTrackTrain.cs`; its origin is
  the entity's), its progress is the watcher's (grep `Progress` or
  `TotalProgress` in `TeamTrainWatcher.cs`).
- The HUD already receives `ObjectiveResourceNetState`
  (`src/tf2/shared/Resources/ObjectiveResourceNetState.cs`: `Positions`,
  `Locked`, `UnlockTimes`, `TimerTimes`, `CapLayoutInHud`, …): what a
  client knows of control points. Flags and carts have their own states.

## Build

1. The contract (commit 1): `src/tf2/server/Objectives/TFObjectives.cs`
   (new dir, original code):

```
public enum TFObjectiveKind : byte { Flag, CaptureZone, ControlPoint, Cart, KothPoint }
public enum TFObjectiveState : byte { Locked, Open, Capturing, Carried, Dropped, Home, Moving, RollingBack, Won }
public readonly record struct TFObjective(
    int Id, TFObjectiveKind Kind, TFTeam Owner, TFTeam MayAct,   // MayAct: Unassigned = both
    TFObjectiveState State, Vector Position, float Progress,      // 0..1: capture %, cart path fraction, flag 0/1
    int CarrierIndex, float UnlockIn, float TimerLeft);
public interface ITFObjectives
{
    ReadOnlySpan<TFObjective> All { get; }
    int LastProgressTick(TFTeam team);   // the tick that team last gained: a capture, a cart advance, a flag pickup/capture
    int Version { get; }                 // bumps when All's contents change
}
```

   `[SideService(Side.Server, As = [typeof(ITFObjectives)])] TFObjectives`:
   an `IFrameLifecycle` (band: pick `999_4xx`'s free slot; `TF2LifecycleOrderTests`
   names a clash) that refills a fixed array each tick from registered
   `ITFObjectiveSource`s (chained, one per kind; this stage writes two) and
   bumps `Version` only when a record differs. Fact
   (`src/tf2/tests/Objectives/TFObjectivesTests.cs`, new):
   `VersionBumpsOnlyWhenARecordChanges`, `AllIsEmptyWithNoSources`.
2. The CTF source (commit 2): one `Flag` per `item_teamflag` (Owner = the
   flag's team, MayAct = the other team, State from `Status`, Carrier from
   the carrier handle's index, Position = home when home else the entity's),
   one `CaptureZone` per `func_capturezone`; `LastProgressTick(team)` moves on
   that team's pickup and capture. Facts over `src/tf2/tests/Ctf/CtfRig.cs`:
   `AStolenFlagIsCarriedByItsTaker`, `ACaptureMovesTheTeamsLastProgress`,
   `AnUntouchedFlagLeavesLastProgressAlone`.
3. The Payload source (commit 3): one `Cart` per watcher (Owner = the
   pushing team, MayAct = it, State Moving/RollingBack/Open by the train's
   speed level sign, Progress = the watcher's path fraction, Position = the
   train's origin); `LastProgressTick` moves when Progress rises for the
   pushers. Facts over `src/tf2/tests/Payload/PayloadRig.cs`:
   `ACartAdvanceMovesTheAttackersLastProgress`, `ARollingBackCartIsRollingBack`.

## Red first

`ACaptureMovesTheTeamsLastProgress` with `LastProgressTick` never written:
`Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFObjectives'` → `Failed: 0`;
`make managed-test FILTER='FullyQualifiedName~TF2LifecycleOrder'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; boot `lane-boot.sh tf2 ctf_2fort
<port> 'cmd jointeam red'`: `first paint` 1, `threw` 0.
