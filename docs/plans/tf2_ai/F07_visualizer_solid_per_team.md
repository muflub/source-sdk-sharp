# F07: the respawn-room visualizer: solid to the other team, sent only to it

Base: the main tip after F04 and F06 land (coordinator writes it:
`________`). Worktree `../ss-tf2ai-F07`, branch `tf2ai-F07`.
Size M, two commits. Fit: Qwen. Depends on F04 (`IEntityCollideRule`) and
F06 (`ITransmitRule`): grep both interfaces under `src/shared/interface`
first; if either is missing, stop and say so.

## Goal

Enemies cannot walk into a respawn room, and a team does not see its own
room's visualizer, as stock. Today `FuncRespawnRoomVisualizer` is solid to
nobody and drawn for everyone (`src/tf2/server/Entities/Rooms/
FuncRespawnRoomVisualizer.cs:24-26`, `OnSpawn` sets `BrushSolidity.None`;
`docs/divergences.md`, the row naming the file).

## Stock, read this session (`func_respawnroom.cpp`)

- `:393-400` `Spawn`: the brush is given `SetCollisionGroup(
  TFCOLLISION_GROUP_RESPAWNROOMS)`; it keeps its brush solidity.
- `:405-414` `InputRoundActivate`: finds the named room, `AddVisualizer`,
  and `ChangeTeam(room's team)` (the visualizer's team is the room's).
- `:454` `InputSetSolid`: a map input that toggles solidity.
- `:464-468` `UpdateTransmitState` → `FL_EDICT_ALWAYS`; `:474-489`
  `ShouldTransmit`: while the room is active and the rules are not in
  `GR_STATE_TEAM_WIN` and the team is assigned, send to a recipient on a
  team above the shared teams who is NOT on the same team; otherwise
  `FL_EDICT_DONTSEND`.
- The solidity per team comes from the player's `ShouldCollide` /
  `PlayerSolidMask` contents bits (F05's note): the brush is in the
  respawn-room collision group and carries the room's team contents.

## Build

1. **Solid per team** (commit 1): `OnSpawn` keeps the brush solid
   (`BrushSolidity.Solid`, or whatever the enum names the normal brush
   state: read `ScriptedEntity`'s `SetSolidity`) and registers a
   `IEntityCollideRule` (`TFRespawnRoomCollide`, `[SideService(Side.Server)]`,
   Valve header + row "ports CFuncRespawnRoomVisualizer solidity
   (func_respawnroom.cpp)") answering: for a visualizer entity, collide only
   with a trace whose contents mask carries the OTHER team's bit (the bits
   F05 declared), and never while `SolidToEnemies` is false or the room is
   inactive. Facts in `src/tf2/tests/Entities/Rooms/RespawnRoomTests.cs` (or
   a new `RespawnRoomVisualizerTests.cs` beside it): `AnEnemyIsBlockedByTheVisualizer`,
   `ATeammatePassesTheVisualizer`, `SetSolidFalseLetsEnemiesThrough`,
   `AnInactiveRoomsVisualizerBlocksNobody`.
2. **Sent per team** (commit 2): implement `ITransmitRule` on the same
   class: `Never` for a recipient on the visualizer's team or on no team,
   `Always` for the other team while the room is active and the round state
   is not `TeamWin` (`ITeamRoundState.State`, `src/tf2/server/Rules/
   TeamRoundSeams.cs`), else `Never`. Replace `Shown` (`:30`) with the rule.
   Facts: `TheVisualizerIsSentToTheOtherTeamOnly`,
   `NothingIsSentDuringTeamWin`. Then append to `docs/divergences.md`: the
   old row stays (append-only), a new row says "resolved by F07" with the
   commit.
3. Live: `lane-boot.sh tf2 ctf_2fort <port> 'cmd jointeam red' 'cmd
   joinclass scout' 'wait 3' 'noclip' 'setpos <inside the blue spawn>'
   'wait 2' 'noclip'`: hmm, a noclip bypasses collision. Instead: `tools/
   tf2-gate --steps join,teams,respawn` must pass unchanged (the step
   `respawn` spawns in rooms), and `tools/debug-api` entity list for the red
   client must not carry a red visualizer: `/entity` rows of class
   `func_respawnroomvisualizer` with the client's team: count 0 for own
   team's, > 0 for the other's. Paste the counts.

## Red first

`AnEnemyIsBlockedByTheVisualizer` before commit 1 (`Failed: 1`: solid to
nobody).

## Commands

`make managed-test FILTER='FullyQualifiedName~RespawnRoom'` → `Failed: 0`;
`tools/tf2-gate --steps join,teams,respawn` → every row PASS; `make test`;
`tools/valve-header check` 0.
