# Coordinator notice 1 to every P7 class lane (2026-11-21), from the first two hand-backs and their reviews

1. **A HUD meter reader registers as itself, with its own hook order.** The lane instructions' "A meter" section is
   wrong twice:
   - `As = [typeof(ITFHudMeters)]` makes the game fatal at boot ("registered twice") once a second reader exists.
     Register your reader with a plain `[SideService(Side.Server)]` that implements `ITFHudMeters`.
   - Every reader also implements `IOrderedHook` with `public int HookOrder => 100 + (int)TFClass.<YourClass>;`.
     Two readers at the default 0 made `AiSenses.OnPostThink` throw every tick, and no bot ever saw itself alive.
   - Do not edit `TFMedigunMeters` (`Senses/TFHudReader.cs:39`); the coordinator drops its `As` at landing.
   Worked example: `src/ai/tf2/server/Classes/TFDemomanMeters.cs` on branch `tf2ai-P7-demoman`.
2. **Keep your harness control clean.** `make headless`, and so every harness arm, builds and deploys the working
   tree. A Ready flip, or any edit deployed during a batch, changes the arms that run after it: once your class is
   Ready, the control's unpinned bots can draw your class. One lane's ctf_2fort table compared its class with a
   control that was mostly the same class. Rules:
   - No build, test run or deploy while a batch is running.
   - Check `control.classes.json` in every control run: it must show only Soldiers. Once your class is Ready, pin the
     control to `class=soldier`.
   - The tables you report name the build they ran on, and that build is the one deployed for the whole batch.
3. **Shared reds you may see** are the coordinator's to fix at landing; do not edit them:
   - `TFClassPickTests.OnlyAReadyClassIsFielded`;
   - `TFBotSetupTests.cs:161`;
   - a census list needing your meter reader (`TFPerceptionCensusTests.cs:48-51`).
   Name them in your hand-back.
4. **Valve header (added 2026-11-21, after the Demoman review).** A class file of yours that restates stock
   `tf_bot` logic (its gates, distances, timings: for example `CTFBotPrepareStickybombTrap`'s) starts with
   `tools/valve-header.txt` byte for byte and gets a row in `docs/valve-header-new.tsv` (`path<TAB>ports
   CTFBot... (file.cpp)`), appended at the end only. A file with only original rules gets none.
5. **The harness's first time was wrong (added 2026-11-21, after the Scout review).** Every map restarts the round
   about 31 s in (waiting for players); `tools/ai-tf-class` counted from its own first sample, so every
   first-on-objective time carried the waiting round, and granary read a false 30 s floor. Fixed in commit
   `46b17f0e3` on branch `tf2ai-P7`: take it with `git cherry-pick 46b17f0e3` in your worktree (a tools-only commit;
   it drops out at landing). Then:
   - new runs report the time from the counted round's start (`round_start_s` beside it in report.json);
   - re-score every run you already report with `tools/ai-tf-class --rescore <run dir>` and use those times in
     your hand-back (old and new, side by side).
   The captures column reads 0 in every run because the port only counts payload captures (a game gap the
   coordinator is fixing); use points changed and flag pickups meanwhile.
