# F05: teammates pass through each other, push apart, and spawn rules

Base: the main tip after F04 lands (the coordinator writes the sha here:
`________`; `git log --oneline -1` must print it). Worktree
`../ss-tf2ai-F05`, branch `tf2ai-F05`. Size M, three commits.
Fit: Qwen. Depends on F04 (`IEntityCollideRule` in `src/shared/interface/
EntityCollideRule.cs`; grep it first; if absent, stop and say so).

## Goal

Stock TF2's teammate collision: teammates pass through each other and push
apart when overlapping; enemies, including a disguised Spy, still collide.

## Stock, read this session

- `tf_gamemovement.cpp:44-45`: `tf_avoidteammates` (default 1: "Teammates
  pass through each other, but push each other away") and
  `tf_avoidteammates_pushaway` (default 1). Both already declared here:
  `src/tf2/shared/ConVars/TFSharedConVars.cs:22-27`
  (`TFAvoidTeammatesConVar`, `TFAvoidTeammatesPushawayConVar`); nothing reads
  them (`grep -rn TFAvoidTeammates src/tf2 --include=*.cs` finds only the
  declarations).
- `tf_player.cpp:10159-10175` `CTFPlayer::ShouldCollide`: for collision group
  `COLLISION_GROUP_PLAYER_MOVEMENT` when `tf_avoidteammates` is on (and for
  the rocket groups always), a RED player refuses a trace whose contents
  mask lacks `CONTENTS_REDTEAM`, a BLUE one lacking `CONTENTS_BLUETEAM`. The
  team is `GetTeamNumber()`, the **real** team: a disguised Spy keeps
  colliding with enemies.
- `c_tf_player.cpp:6318` inside `C_TFPlayer::AvoidPlayers(CUserCmd*)`: with
  both convars on, the local player is pushed out of overlapping teammates by
  adjusting the usercmd's move (the push-away). The server has the same
  shape for prediction parity; read the function whole before porting and
  cite the lines in the commit.
- `tf_player.cpp:6129-6135` is not this stage.

## Code today

- `src/tf2/shared/Movement/TFMovementRules.cs:60` (`IMovementRules`, shared
  both sides): where the player's solid mask and movement hooks live.
- `src/tf2/server/Entities/TFSpawnNoFrag.cs` (`ISpawnFragRule`, answers false
  always): spawn frags are off in TF2 already; nothing to change there.

## Build

1. **The collide rule** (commit 1): `src/tf2/shared/Movement/TFTeamCollide.cs`
   (new, Valve header, a `valve-header-new.tsv` row "ports
   CTFPlayer::ShouldCollide (tf_player.cpp)"), `[SideService(Side.Server)]` and
   registered on the client the same way, implementing `IEntityCollideRule`:
   for a player entity and group `PlayerMovement`, when
   `TFAvoidTeammatesConVar` is true, answer false when the mask lacks the
   player's real team's contents bit (`CONTENTS_REDTEAM`/`CONTENTS_BLUETEAM`:
   take the values from `src/shared/interface` if declared, else declare
   them beside `TraceFilterRules.ContentsMonster` with stock's values from
   `bspflags.h`, cited). The real team is `ITeamRoundRoster.TeamOf`
   (`src/tf2/server/Rules/TeamRoundSeams.cs:128`), never the disguise. The
   player's solid mask in `TFMovementRules` adds the OTHER team's contents
   bit, as stock's `PlayerSolidMask` does (read it: `tf_player.cpp`, grep
   `PlayerSolidMask`, cite the lines).
   Facts (`src/tf2/tests/TFMovementTests.cs` or a new
   `TFTeamCollideTests.cs` beside it): `TeammatesDoNotCollideInMovement`,
   `EnemiesStillCollide`, `ADisguisedEnemyStillCollides` (the Spy rig in
   `src/tf2/tests/Spy/SpyRig.cs` disguises one player; the rule answers as
   for its real team), `WithAvoidTeammatesOffTeammatesCollide`,
   `BulletsStillStopOnATeammate` (a non-movement group is untouched).
2. **The push-away** (commit 2): the `AvoidPlayers` port in the shared
   movement (`TFMovementRules` or a new `TFTeamPushaway.cs`, header + row),
   applied to the usercmd both sides so prediction agrees; a fact
   `OverlappingTeammatesArePushedApart` (two players at one origin: after a
   tick their distance grows) and `NoPushawayWhenTheConVarIsOff`; and the
   prediction fact in the `TF2ClientPredictionTests` shape: predicted and
   server positions agree through a pass.
3. **Live** (commit 3 is the hand-back note, no code): `lane-boot.sh tf2
   ctf_2fort <port> 'cmd jointeam red' 'cmd joinclass scout'`: the console
   has no `MISPREDICTION` line beyond the previous tip's count (run the same
   boot on the base first and `grep -c MISPREDICTION` both logs).

## Red first

`ADisguisedEnemyStillCollides` written against a rule that reads the
disguise team (write that wrong version first, see `Failed: 1`, then read
the real team).

## Commands

`make managed-test FILTER='FullyQualifiedName~TFTeamCollide'` and
`FILTER='FullyQualifiedName~TF2ClientPrediction'` → `Failed: 0`; `make test`
all legs; `tools/valve-header check` 0.
