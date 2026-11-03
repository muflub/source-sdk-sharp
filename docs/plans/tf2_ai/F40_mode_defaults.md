# F40: `IGameMode.Teamplay` and `JoinPolicy` default members, with no-owner facts

Base: `dae055998`. Worktree `../ss-tf2ai-F40`, branch
`tf2ai-F40`. Size S, two commits. Fit: Qwen. Depends on nothing. This is
P1's step 2 of the main plan (A.3 items 3 and 6), cut out so TF2's bots
get the seams whether or not HL2DM TDM has landed.

## Goal

Two default members on the core's `IGameMode` so a mode can say it is a
team game and own the join policy, without colliding with the single-owner
`IJoinTeamPolicy` registration TF2 holds. No behaviour change for any
existing mode (the no-owner facts prove it).

## Code today

- `src/server/managed/Modes/GameModeContracts.cs:21` `IGameMode` with
  default members `Respawn`, `Spawns`, `Scoring`, `Setup`, `Cleanup`
  (`:38-56`); the doc comment at `:15-19` says a default is "the core's own
  rule, unchanged" and every no-owner fact in the modes' tests proves it.
- `src/server/managed/Teams.cs:86` `IJoinTeamPolicy` (single owner, `:78-92`:
  zero owners means any real team is accepted); `Teams.Join` (`:277`, the
  policy ask at `:52`'s comment); TF2's owner `src/tf2/server/Rules/TFTeams.cs:33`.
- `src/server/managed/HostSay.cs:145` `public static bool Teammates(...) =>
  false;` used by `CanHear` (`:128-131`); the comment says "HL2MP without
  teamplay has no teammates".
- Tests: `src/test/server/TeamJoinTests.cs`, `TeamTests.cs`, the modes' tests
  (grep `NoOwner` or `NullMode` under `src/test/server`), `src/ai/tests/Chat/
  Hl2mpBotChatTests.cs` `ATeamLineIsNotHeardWithoutTeammates`,
  `tools/live-gate:4976` (a needle "has no teammates": do not change it).

## Build

1. (commit 1) `IGameMode` gains `bool Teamplay => false;` and
   `IJoinTeamPolicy? JoinPolicy => null;` beside `Spawns`, each with a doc
   comment in the file's style. `Teams.Join` asks the active mode's
   `JoinPolicy` first (through `GameModes.Active`, the way `SelectSpawn`
   asks `IGameMode.Spawns`: read that path and copy its shape), then the
   registered owner. `HostSay.Teammates` becomes an instance method:
   `Teammates(a, b)` is true only when the active mode's `Teamplay` is true
   and both players are on the same team ≥ 2 (`ITeams.Team`); `CanHear`
   calls it. TF2's modes answer `Teamplay => true` in `TFModeBase`
   (`src/tf2/server/Modes/TFModeBase.cs`; its `say_team` then reaches a
   team: check `src/tf2/server/Player/TFChatAudience.cs:35` only vetoes,
   so this is the grant).
2. (commit 2) Facts, `src/test/server/GameModeDefaultsTests.cs` (new):
   `NoModeMeansNoTeamplay` (the core's rules: `Teammates` false for two
   players on team 2), `AModesTeamplayMakesSameTeamPlayersTeammates`,
   `AModesJoinPolicyIsAskedBeforeTheRegisteredOwner` (a fake mode whose
   policy refuses team 3: `Join` refuses with its reason while the
   registered owner would allow), `NoJoinPolicyKeepsTheSingleOwnerRule`
   (the existing `TeamJoinTests` stay green). TF2: `src/tf2/tests/Rules/
   TFTeamsTests.cs` gains `ATFModeIsTeamplay` and a `say_team` fact beside
   the chat audience facts (grep `TFChatAudience` under `src/tf2/tests`):
   `SayTeamReachesATeammateUnderATFMode`.

## Red first

`AModesTeamplayMakesSameTeamPlayersTeammates` while `Teammates` is still
`=> false`: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~GameModeDefaults'`,
`FILTER='FullyQualifiedName~TeamJoin'`, `FILTER='FullyQualifiedName~Hl2mpBotChat'`
→ `Failed: 0` each; `make test`; `tools/valve-header check` 0; a boot of
each mod: `lane-boot.sh test dm_lockdown <port> 'say_team hello'` and
`lane-boot.sh tf2 ctf_2fort <port> 'cmd jointeam red' 'say_team hello'`:
`first paint` 1, `threw` 0.
