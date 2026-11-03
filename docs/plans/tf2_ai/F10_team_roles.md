# F10: team roles (attackers and defenders) for Attack/Defend maps

Base: `dae055998`. Worktree `../ss-tf2ai-F10`, branch
`tf2ai-F10`. Size M, three commits. Fit: Qwen. Depends on nothing; serialise
with F03 on `TFJoinTeamCommand.cs`/`TFTeams.cs` (F03 first).

## Goal

A map can say which team attacks and which defends; auto-team sends new
players to the attackers on an A/D map; the role is readable. Today
`CPMode.cs:21-27` says "team roles are not ported, so this mode joins no
side by default".

## Stock, read this session

- `tf_shareddefs.h:74-76`: `TEAM_ROLE_NONE = 0`, `TEAM_ROLE_DEFENDERS`,
  `TEAM_ROLE_ATTACKERS`.
- `tf_team.h:57-58`: `CTFTeam::SetRole(int)` / `GetRole()` store one int per
  team.
- `tf_gamerules.cpp:1605-1606`: the `tf_gamerules` proxy's map inputs
  `SetRedTeamRole` and `SetBlueTeamRole` (`FIELD_INTEGER`); their handlers at
  `:1785-1806` set the team's role.
- `tf_player.cpp:6129-6135` inside `CTFPlayer::GetAutoTeam`: with even
  counts, when the game type is escort OR RED's role is `TEAM_ROLE_DEFENDERS`,
  the new player goes to BLUE ("AutoTeam should give new players to the
  attackers on A/D maps if the teams are even"); `:6173` repeats it for the
  other branch (read it).
- Other readers (`tf_gamerules.cpp:5101-5260`) are stopwatch-mode arms, not
  this stage; `tf_player.cpp:16797` and `:19276` are HUD/stat arms (read,
  cite, leave).

## Code today

- `src/tf2/server/Entities/TFGameRulesProxy.cs:21` (`tf_gamerules`): read how
  its existing inputs are declared and add the two the same way.
- `src/tf2/server/Modes/PayloadMode.cs:26` `EvenTeamsJoinAttackers => true`
  and `TFModeBase` (`src/tf2/server/Modes/TFModeBase.cs`): the escort arm is
  already a mode flag; `CPMode.cs:30` has no roles.
- `src/tf2/server/Rules/TFTeams.cs:33` (`IJoinTeamPolicy`) and
  `TFJoinTeamCommand.cs:51`: the auto-team path (grep `AutoTeam` under
  `src/tf2/server/Rules` for the function that picks the smaller team).

## Build

1. `src/tf2/server/Rules/TFTeamRoles.cs` (new, Valve header + row "ports
   CTFTeam::SetRole/GetRole and the tf_gamerules role inputs
   (tf_team.h, tf_gamerules.cpp)"): `public enum TFTeamRole { None = 0,
   Defenders, Attackers }`; `public interface ITFTeamRoles { TFTeamRole
   RoleOf(TFTeam team); void SetRole(TFTeam team, TFTeamRole role); }`;
   `[SideService(Side.Server, As = [typeof(ITFTeamRoles)])] TFTeamRoles`
   with a per-team store, reset to `None` on level init (an `IGameLifecycle`
   in band `999_1xx`'s free slot: `TeamRoundOrders.Band` is `999_100`;
   `TF2LifecycleOrderTests` names a clash). The proxy's inputs
   `SetRedTeamRole`/`SetBlueTeamRole` call `SetRole`.
   Facts (`src/tf2/tests/Rules/TFTeamRolesTests.cs`, new):
   `TheProxyInputSetsRedsRole`, `RolesResetAtLevelInit`, `RoleOfAnUnsetTeamIsNone`.
2. Auto-team: where the even-teams arm reads `EvenTeamsJoinAttackers`, also
   send to BLUE when `roles.RoleOf(Red) == Defenders` (stock's OR). Fact:
   `EvenTeamsJoinBlueWhenRedDefends` beside the existing even-teams fact
   (grep `EvenTeams` under `src/tf2/tests/Rules`); remove the CPMode doc
   comment's "not ported" sentence.
3. `docs/plans/tf2/deferred.tsv`: delete the row that defers team roles (grep
   `role` in its item column; if more than one, delete only the role store
   and auto-team ones; cite the commit). Append a `docs/divergences.md` row
   only if stock does something you did not port (the stopwatch arms: say
   they stay unported and are not a divergence of this build's modes).

## Red first

`EvenTeamsJoinBlueWhenRedDefends` before step 2: `Failed: 1`.

## Commands

`make managed-test FILTER='FullyQualifiedName~TFTeamRoles'` and
`FILTER='FullyQualifiedName~TFTeamsTests'` → `Failed: 0`; `make test`;
`tools/valve-header check` 0; boot `lane-boot.sh tf2 cp_dustbowl <port>
'cmd jointeam auto'`: `first paint` 1, `threw` 0, and the console's team
join line shows BLUE for the first joiner (cp_dustbowl's map logic fires
`SetRedTeamRole 1`; `grep -c` the join line and paste it; if the map's
entity lump does not fire the input, say so: `grep -ac SetRedTeamRole
bin/debug/content/tf/maps/cp_dustbowl.bsp` first).
