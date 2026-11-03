# F11: Payload Race rules (`tf_logic_multiple_escort`)

Base: `dae055998`. Worktree `../ss-tf2ai-F11`, branch
`tf2ai-F11`. Size M, three commits. Fit: Qwen. Depends on nothing.

## Goal

A `plr_` map runs as a race: both teams push their own cart, the first to
the end wins, no team switch. Today `tf_logic_multiple_escort` is only a HUD
flag (`src/tf2/server/Modes/PayloadMode.cs:33`, comment at `:32-36`;
`src/tf2/ui/screens/TFHudEscort.cs`).

## Stock, read this session (`tf_gamerules.cpp` unless named)

- `:19666` `LINK_ENTITY_TO_CLASS( tf_logic_multiple_escort, CMultipleEscort )`.
- `:4352-4357` `CTFGameRules::Activate`: with a train watcher the game type
  is escort, and `SetMultipleTrains( pMultipleEscort != NULL )` from a
  `tf_logic_multiple_escort` lookup.
- `:2775` (`ShouldSwitchTeams`'s shape; read the enclosing function name
  and cite it): a switch on round win needs `!HasMultipleTrains()`: a race
  never switches teams.
- `:9633-9650` `SetWinningTeam`: the `else if ( HasMultipleTrains() )` arm
  walks the train watchers and, for the winner's watcher, does something
  per team (read `:9635-9660` whole and write down what: it is the
  win-side bookkeeping for the race; port exactly what it does).
- `team_train_watcher.cpp:255-263`: on `teamplay_round_start`, when
  `HasMultipleTrains()`, the watcher finds its trains (`FindTrainWatchers`).
- `:13742` (`SendWinPanelInfo`) and `tf_gamestats.cpp:2078`: stats/win panel
  arms, not this stage; cite and leave.

## Code today

- `PayloadMode.cs:20-60`: `ModeId "pl"`, `GameType Escort`,
  `EvenTeamsJoinAttackers`, `PointsMayAlwaysBeBlocked`, the multiple-escort
  flag; `TFModeBase` for the round flow; `src/tf2/server/Entities/Payload/
  TeamTrainWatcher.cs:83` (the watcher), its `Vars.Train/StartNode/GoalNode`
  (`:1200-1206`), `AreaCap` (`:220`); tests in `src/tf2/tests/Payload/`
  (`PayloadRig.cs`, `TeamTrainWatcherTests.cs`).

## Build

1. `ITFMultipleTrains` (a bool the mode answers from the entity lump at
   level init, where `PayloadMode` already finds `tf_logic_multiple_escort`):
   expose it on `PayloadMode` as `HasMultipleTrains` and through
   `IActiveTeamRoundMode` if the mode seam has a flags surface (read
   `TeamRoundSeams.cs`'s `ITeamRoundMode`). Fact (`src/tf2/tests/Modes/
   TFModeTests.cs` beside the fixture-map facts at `:82-94`):
   `AMapWithMultipleEscortLogicHasMultipleTrains`.
2. No team switch on a race win: where the round flow decides a switch
   (grep `SwitchTeams` under `src/tf2/server/Rules`), add stock's
   `!HasMultipleTrains` gate. Fact: `ARaceWinNeverSwitchesTeams` in
   `src/tf2/tests/Rules/TeamSwitchTests.cs`.
3. Both teams push: each watcher's `AreaCap` must take its own team's
   pushers, the enemy's blocking it (`PointsMayAlwaysBeBlocked` stays); the
   win: the first watcher whose train reaches its goal wins for its team
   (`SetWinningTeam`'s race arm as read in step 0). Facts in
   `src/tf2/tests/Payload/`: `EachTeamsCartMovesOnlyForItsOwnPushers`,
   `TheFirstCartAtItsGoalWinsForItsTeam`, over `PayloadRig` with two watchers
   (extend the rig if it builds one; say so in the commit).
   Then delete the `deferred.tsv` row for Payload Race (grep `multiple_escort`
   or `race` in the item column) in the same commit.

## Red first

`ARaceWinNeverSwitchesTeams` before step 2: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~Payload'` and
`FILTER='FullyQualifiedName~TeamSwitch'` → `Failed: 0`; `make test`;
`tools/valve-header check` 0; boot `lane-boot.sh tf2 plr_hightower <port>
'cmd jointeam red' 'cmd joinclass scout'`: `first paint` 1, `threw` 0,
`[SourceSharp/Modes] switched to 'pl'` (or the mode's pick line: grep
`Modes]`) present; paste the line.
