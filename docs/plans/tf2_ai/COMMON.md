# TF2 AI prework: the part of every lane brief that is the same

You are one lane of the SourceSharp TF2 port (C# game logic over the Source
SDK 2013 engine). The brief you were given is this file followed by ONE stage
file from `docs/plans/tf2_ai/`. The stage file names your worktree, your
branch, the files you own, the stock lines to read, the types and fact names
to write, and the commands that prove the stage. Do only that stage.

## Start (every lane, in this order)

1. `git log --oneline -1` must print the base sha the stage file names. If it
   does not, stop and say so in your reply: do not work on another base.
2. `make setup` then `make debug`; both must end rc 0 (the worktree is fresh).
3. Read `CLAUDE.md` in the worktree root once.
4. Re-grep every `file:line` the stage cites before relying on it: the
   citation is a pointer, not a promise.

## Hard rules (the launcher refuses most of these; the rest are on you)

- Never write under `../source-sdk-2013-valve` (stock, read only) or any
  other worktree (`../ss-*`). Stock paths in briefs are relative to
  `../source-sdk-2013-valve/src/game/`.
- Never `git push`, `git checkout`, `git reset`, `git stash`, `rm`,
  `git add -A`, `git add .`. Stage files by explicit path.
- Run make only as `make <target> [VAR=value]`. Never `make -f`, never a
  Makefile you wrote, never a wrapper around a refused command. A refused
  command gets ONE try; then write what you needed in your reply and go on.
- Search with plain `grep -rn PATTERN DIR | head -40` or `find` in Bash, not
  the Grep/Glob tools.
- Commit after every step: `git commit -m "<subject>"` with a body that ends
  in the gate command and its numbers, and a last line naming your model as
  co-author (given at launch). Uncommitted work at the turn cap is lost.
- Managed names spell TF uppercase (`TFTeamRoles`, never `TfTeamRoles`).
- No mutable static fields in runtime assemblies (an analyzer fails the
  build). Services are injected by constructor; a constructor only stores
  its parameters. Copy the shape of the neighbouring files.
- Zero compiler warnings. `make debug` ends rc 0.
- A new file that ports Valve logic starts with `tools/valve-header.txt` byte
  for byte and gets one row in `docs/valve-header-new.tsv` in the SAME
  commit (`path<TAB>ports <what> (<stock file>)`). Original work and
  behaviour-only tests get no header. Never copy Valve code: identifiers
  only, the logic rewritten.
- Every behaviour gets one xUnit `[Fact]` (one behaviour per fact) that can
  fail. The stage names at least one fact you must first see RED: write it,
  run it, see `Failed: 1`, then make it green. Never a
  `Run(Action<string,bool>)` test.
- Ledgers are append-only, except where a stage says to edit or delete a
  row in place: `docs/valve-header-new.tsv`, `docs/divergences.md`,
  `docs/plans/tf2/deferred.tsv` (five tab-separated columns; a row whose
  behaviour you ported is DELETED in the same commit), `docs/todo.md`.
- Every `IOrderedHook` / `IGameLifecycle` / `IClientLifecycle` order you add
  is unique inside the band the stage gives; `TF2LifecycleOrderTests` names a
  clash. A duplicate aborts the mod's load live, and `make test` does not
  catch that: boot once (below).
- Scratch files go under `.scratch/` in your worktree, never committed.
  Never leave an untracked `.cs` outside `.scratch/` (the build compiles it
  and the audit scans it). Content extracted into `.scratch/` is deleted
  before `make test`.
- Diagnostics you add to see something live are not committed.
- Touch only the files the stage lists under "may touch". A stage that
  needs a file it does not own is reported, not worked around.

## Commands (what "done" means)

- Managed facts for one class: `make managed-test FILTER='FullyQualifiedName~<Class>'`.
  Judge by the `Failed: 0` line of the summary, never by `[FAIL]` lines
  (they show only on a TTY). TF2's own project: `make tf2-test` (it runs
  `TF2.Tests`); with a filter, `make tf2-test FILTER='FullyQualifiedName~<Class>'`
  if the target accepts it, else the managed-test form above.
- Before the hand-back: `make test` with every leg green (read each leg's
  `Failed: 0`), `tools/valve-header check` with 0 problems, and `git status`
  clean but for `.scratch/`.
- A live boot, when the stage asks for one, goes only through the launcher's
  `lane-boot.sh <mod> <map> <port> '<cmd>' ...` (its path is given at launch;
  it prints `== stopped game <pid>`). Success is the console lines the stage
  names; `grep -c` them in the console log it prints. A screenshot is never
  your proof: the coordinator opens images; you report a count or a line.
- Gate steps go through `tools/tf2-gate --steps <name>`; its report is
  `tools/.tf2-gate/<run>/report.tsv` with one row per step and PASS/FAIL.

## Hand-back

Reply with: the commit shas and subjects; each fact you added and the one
you saw red first (paste its `Failed: 1` line and the `Failed: 0` line
after); the test totals; the stock lines you read and whether they say what
the stage said (if not, say so: a wrong premise is corrected, never ported);
any command refused, verbatim; anything you could not verify.
