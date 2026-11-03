# F03: the mod's bot arms (autobalance and humans-must-join)

Base: `dae055998`. Worktree `../ss-tf2ai-F03`, branch
`tf2ai-F03`. Size S, two commits. Fit: GLM or Qwen. Depends on nothing
(F01 edits a comment in the same file: rebase if both land; the comment is
not yours).

## Goal

Two stock arms that exist only because bots exist, ported over the core's
bot seam `IBotPlayers` (`src/server/managed/BotPlayers.cs:15`: `IsBot(EntityHandle)`).

## Stock, read this session

- `tf_player.cpp:19822-19833` `CTFPlayer::CanBeAutobalanced`: returns false
  for a player in a duel, for `IsBot()`, for one coaching, for one with a
  coach; otherwise true (the remaining lines). Only the bot arm is ported
  here: duels and coaching do not exist in this build.
- `tf_player.cpp:6280-6289` (inside `HandleCommand_JoinTeam`): `if ( !IsBot()
  && iTeam != TEAM_SPECTATOR )` then the `mp_humans_must_join_team` override
  applies. So a bot's pick is never forced to the humans' team.

## Code today

- `src/tf2/server/Rules/TFAutobalance.cs:38` (class), its `Candidate` list at
  `:75`; candidates are picked from the heaviest team with no bot test (the
  doc comment at `:32-35` says so).
- `src/tf2/server/Rules/TFJoinTeamCommand.cs:85`: `if (requested !=
  (int)TFTeam.Spectator && HumanTeam() is { } forced) requested = (int)forced;`
  with no bot test (`HumanTeam()` at `:117`).

## Build

1. `TFAutobalance`: inject `IBotPlayers` (constructor parameter, stored, as
   the other parameters are) and skip a candidate for whom `IsBot` is true,
   where candidates are gathered. Fact in
   `src/tf2/tests/Rules/TFAutobalanceTests.cs`:
   `ABotIsNeverAnAutobalanceCandidate` (a rig where the heaviest team's only
   dead player is a bot: nobody is moved) and
   `AHumanOnTheHeaviestTeamIsStillACandidate` (the arm you did not break).
   Look at the file's existing rig for how a player is made and how
   `IBotPlayers` can be faked (a test double answering `IsBot` true for one
   handle; if the rig has no `IBotPlayers`, add a small fake in the test
   file).
2. `TFJoinTeamCommand`: inject `IBotPlayers`; the humans-must-join override
   applies only when `!bots.IsBot(player)`. Fact in
   `src/tf2/tests/Rules/TFJoinTeamPanelTests.cs` (or the file that holds the
   `mp_humans_must_join_team` facts: grep `HumansMustJoin` under
   `src/tf2/tests`): `ABotsPickIsNotForcedToTheHumansTeam` and the existing
   human fact unchanged.
3. In the same commits, update the doc comments that said "no bots".

Both classes are `[SideService(Side.Server)]`; `IBotPlayers` is registered by
the core (`ServerModule`), so the constructor change composes. **Boot once**
to prove it: `lane-boot.sh tf2 ctf_2fort <port> 'cmd jointeam red'` must
print `[SourceSharp/Hud] first paint` and no `StageCollision`/`threw` line
(grep the console it prints: `grep -c 'first paint'` = 1, `grep -c threw` = 0).

## Red first

Write `ABotIsNeverAnAutobalanceCandidate` before the change: it fails with
the bot moved (`Failed: 1`). Paste that line, then the `Failed: 0` line.

## Commands

- `make managed-test FILTER='FullyQualifiedName~TFAutobalanceTests'` → `Failed: 0`
- `make managed-test FILTER='FullyQualifiedName~TFJoinTeam'` → `Failed: 0`
- `make test` all legs `Failed: 0`; `tools/valve-header check` → 0 problems.
