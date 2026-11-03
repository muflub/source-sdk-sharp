# F09: `ITFRoundFacts`, what a bot reads of the round (read-only)

Base: `dae055998`. Worktree `../ss-tf2ai-F09`, branch
`tf2ai-F09`. Size S, one or two commits. Fit: GLM or Qwen. Depends on
nothing.

## Goal

One read-only service that answers what every client's HUD shows about the
round, so the AI binding (later) reads it without reaching into the round
machinery. No behaviour changes.

## What exists

- `src/tf2/server/Rules/TeamRoundSeams.cs`: `ITeamRoundState` (`State`,
  the `RoundState` enum), `ITeamRespawnWaves`, `ITeamRoundSettings`,
  `ITeamRoundRoster` (`:128`: `Players`, `TeamOf`, `IsAlive`, `DeathTime`, …).
- `src/tf2/server/Rules/TeamRoundTimer.cs:38` (the `team_round_timer`
  entity; setup length at `:686`); `TeamRoundFlow.cs:53` (waves at `:111`);
  `TFRespawnPolicy.cs:14`; `src/tf2/shared/Rules/TeamRoundTimerNetState.cs:22`
  (what the HUD timer receives).
- Facts already there: `src/tf2/tests/Rules/RoundTimerTests.cs`,
  `TeamRoundRespawnWaveTests.cs`, `RoundSetupSidesTests.cs`.

## Build

`src/tf2/server/Rules/TFRoundFacts.cs` (new; original code, no Valve
header): 

```
public interface ITFRoundFacts
{
    RoundState State { get; }            // the round machine's state
    bool InSetup { get; }                // setup time running (stock InSetup)
    float SetupSecondsLeft { get; }      // 0 when not in setup
    float RoundSecondsLeft { get; }      // the HUD timer; -1 when no timer
    float NextRespawnWaveIn(TFTeam team);// seconds until that team's next wave
    bool IsWaitingForPlayers { get; }
    TFTeam WinningTeam { get; }          // Unassigned unless State is TeamWin
}
```

`[SideService(Side.Server, As = [typeof(ITFRoundFacts)])] public sealed class
TFRoundFacts(ITeamRoundState round, ITeamRespawnWaves waves, ...)`, reading
the existing services only (constructor stores them; read the timer through
whatever service `TeamRoundTimerNetState` is written from: grep `SetupLength`
and `TimerTimes` under `src/tf2/server/Rules` and name the source in the
commit). Each member is a plain read; nothing is cached.

Facts, `src/tf2/tests/Rules/TFRoundFactsTests.cs` (new), over
`src/tf2/tests/Rules/RoundRig.cs` (read it; it drives the round machine):
`InSetupIsTrueWhileTheSetupTimerRuns`, `SetupSecondsLeftFallsWithTheClock`,
`RoundSecondsLeftIsMinusOneWithoutATimer`, `NextRespawnWaveInMatchesTheWaves`
(compare with `ITeamRespawnWaves`'s own answer), `WinningTeamIsSetOnlyInTeamWin`.
One `TF2TestLegRuns`-style composition fact if the file has that pattern
(`src/tf2/tests/TF2TestLegRuns.cs`): the service resolves from the
registry.

## Red first

`InSetupIsTrueWhileTheSetupTimerRuns` with `InSetup => false` hard-coded:
`Failed: 1`; then the real read: `Failed: 0`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFRoundFacts'` → `Failed: 0`;
`make test`; `tools/valve-header check` 0; a boot `lane-boot.sh tf2
ctf_2fort <port> 'cmd jointeam red'` with `first paint` 1 and `threw` 0
(a new `[SideService]` composes live).
