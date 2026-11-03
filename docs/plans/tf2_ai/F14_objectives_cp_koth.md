# F14: the control-point and KOTH objective feeds

Base: the main tip after F13 lands (coordinator writes it: `________`).
Worktree `../ss-tf2ai-F14`, branch `tf2ai-F14`. Size M, two
commits. Fit: Qwen. Depends on F13 (`ITFObjectives`, `ITFObjectiveSource` in
`src/tf2/server/Objectives/TFObjectives.cs`; grep first, stop if absent).

## What exists

- `src/tf2/server/Entities/TeamControlPoint.cs:54` (`Owner` `:235`,
  `DefaultOwner` `:237`), `TeamControlPointMaster.cs:39`,
  `TeamControlPointRound.cs:24`, `TriggerAreaCapture.cs:73` (`ControlPoint`
  `:138`, `IsActive`, `IsCapturing`, `CapturingTeam`, `OwningTeam` `:140-146`;
  the capture progress: grep `CapPercentage` or `Progress` in the file),
  `KothLogic.cs:129` (`TimerLength`, `TimeToUnlockPoint` `:255-258`;
  `InKothMode` `:62`), `CPTimerLogic` (`tf_logic_cp_timer`).
- The HUD's view: `src/tf2/shared/Resources/ObjectiveResourceNetState.cs`
  (`Positions`, `Locked`, `UnlockTimes`, `LazyCapPerc`, `CappingTeam`,
  `TimerTimes`): read it to see which point fields are already computed for
  the client and reuse the same sources (never a second computation of
  "locked").
- Tests: `src/tf2/tests/Entities/ControlPointTests.cs`, `AreaCaptureTests.cs`,
  `KothTests.cs`, `CPTimerLogicTests.cs`, `ObjectiveRig.cs`.

## Build

1. The CP source (commit 1): one `ControlPoint` per `team_control_point`:
   Owner, MayAct (the teams that may capture it now: from the master's
   rules the HUD already shows as `Locked`), State `Locked/Open/Capturing/Won`,
   Progress = the area's capture fraction, Position = the point's origin,
   UnlockIn from `UnlockTimes`. `LastProgressTick(team)` moves on a capture
   by that team and on capture progress rising for it. Facts over
   `ObjectiveRig`: `ACaptureFlipsOwnerAndMovesLastProgress`,
   `ALockedPointIsLockedForBoth`, `CapturingShowsTheCappingTeamsProgress`.
2. The KOTH source (commit 2): the one point as `KothPoint`, UnlockIn from
   `tf_logic_koth`'s unlock, TimerLeft = the owning team's timer (the
   `team_round_timer` per team KOTH keeps: grep `KothTimer` under
   `src/tf2/server`), State `Locked` until unlocked; `LastProgressTick` on a
   capture and while the team's timer runs. Facts over the KOTH rig in
   `KothTests.cs`: `TheKothPointIsLockedUntilItsUnlockTime`,
   `TheOwnersTimerLeftFallsWhileItHolds`.

## Red first

`ALockedPointIsLockedForBoth` with `MayAct` always `Unassigned`: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFObjectives'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; boot `lane-boot.sh tf2
koth_viaduct <port> 'cmd jointeam red'`: `first paint` 1, `threw` 0.
