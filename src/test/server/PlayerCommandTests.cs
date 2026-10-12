using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_entity.md step 3d — the usercmd queue and the loop that runs it.
///
/// <para>
/// Two things are under test and they fail differently. The FLATTEN is a port
/// of C++ that used to live in <c>CStandalonePlayer::QueueUsercmds</c>: the
/// facts below state the sequence it produces for each shape of packet, and a
/// wrong port produces a wrong sequence. The LOOP is the step's gate: one
/// crossing per <c>ProcessUsercmds</c> whatever N is, with the module's
/// per-command decisions between the two native stages.
/// </para>
/// </summary>
public class PlayerCommandTests
{
    // This class's own command queues and shadow run (statics to zero):
    // xUnit builds one instance per fact, so no fact sees another's.
    private readonly PlayerCommandQueue _queue = new(new RecordingLog());

    //-------------------------------------------------------------------------
    // THE FLATTEN. Commands arrive NEWEST FIRST, so "oldest first" counts down.
    //-------------------------------------------------------------------------

    [Fact]
    public void APacketWithNoDropsRunsItsOwnCommandsOldestFirst()
    {
        Assert.Equal(new[] { 3, 2, 1, 0 }, PlanOf(numCmds: 4, totalCmds: 4, dropped: 0));
    }

    [Fact]
    public void APacketWithNoDropsRunsExactlyItsOwnCommands()
    {
        // The backup commands are NOT run when nothing was dropped: they are
        // there to cover a gap, and there is no gap.
        Assert.Equal(new[] { 1, 0 }, PlanOf(numCmds: 2, totalCmds: 8, dropped: 0));
    }

    [Fact]
    public void DroppedPacketsWithinTheBackupAreCoveredByTheBackupCommands()
    {
        // Two dropped, six backup commands available. The two OLDEST-first
        // backup entries are indices numCmds+1 then numCmds+0.
        Assert.Equal(new[] { 3, 2, 1, 0 }, PlanOf(numCmds: 2, totalCmds: 8, dropped: 2));
    }

    [Fact]
    public void DroppedPacketsBeyondTheBackupReplayTheLastCommand()
    {
        // Four dropped, one backup command. One backup covers one, so three
        // replays of the last known command cover the rest — and they come
        // FIRST, because they are the oldest thing being caught up on.
        Assert.Equal(
            new[] { PlayerCommandQueue.ReplayLast, PlayerCommandQueue.ReplayLast,
                    PlayerCommandQueue.ReplayLast, 2, 1, 0 },
            PlanOf(numCmds: 2, totalCmds: 3, dropped: 4));
    }

    [Fact]
    public void TwentyFourDroppedPacketsReplayNothingAtAll()
    {
        // Stock's cut-off: a client gone this long is resynchronised, not
        // caught up. Only the packet's own commands run.
        Assert.Equal(new[] { 1, 0 },
                     PlanOf(numCmds: 2, totalCmds: 8, dropped: PlayerCommandQueue.MaxDroppedPackets));
    }

    [Fact]
    public void OneUnderTheCutOffStillReplays()
    {
        // The boundary from the other side, so the comparison cannot be `<=`
        // without this going red. 23 dropped, 6 backup: 17 replays then 6
        // backup then the 2 new ones.
        int[] plan = PlanOf(numCmds: 2, totalCmds: 8, dropped: PlayerCommandQueue.MaxDroppedPackets - 1);

        Assert.Equal(25, plan.Length);
        Assert.Equal(17, plan.Count(e => e == PlayerCommandQueue.ReplayLast));
    }

    [Fact]
    public void TheReplaySentinelIsNegativeAndSoCannotCollideWithAnIndex()
    {
        // A default-shaped sentinel would be 0, and 0 is a REAL entry — the
        // newest command in the packet. This tree has lost work to exactly that
        // shape (a default handle is entry zero, which is worldspawn).
        Assert.True(PlayerCommandQueue.ReplayLast < 0);
    }

    [Fact]
    public void AnEmptyPacketPlansNothing()
    {
        Assert.Empty(PlanOf(numCmds: 0, totalCmds: 0, dropped: 0));
    }

    [Fact]
    public void AMalformedPacketPlansNothing()
    {
        // totalCmds < numCmds cannot happen from the engine, and answering a
        // plan for it would index past the array native decoded.
        Assert.Empty(PlanOf(numCmds: 4, totalCmds: 2, dropped: 0));
        Assert.Empty(PlanOf(numCmds: -1, totalCmds: 4, dropped: 0));
    }

    [Fact]
    public void EveryPlannedIndexNamesACommandThePacketCarries()
    {
        for (int total = 1; total <= 12; total++)
        for (int num = 0; num <= total; num++)
        for (int dropped = 0; dropped <= 26; dropped++)
        {
            foreach (int entry in PlanOf(num, total, dropped))
            {
                Assert.True(entry == PlayerCommandQueue.ReplayLast || (entry >= 0 && entry < total),
                            $"entry {entry} for num={num} total={total} dropped={dropped}");
            }
        }
    }

    [Fact]
    public void APlanTooBigForTheBufferAnswersWhatItNeedsAndFillsNothing()
    {
        // SIZE THEN FILL. The caller must be able to tell "did not fit" from a
        // short plan, or it would queue a prefix and the two queues would
        // disagree by exactly the entries that did not fit.
        Span<int> tiny = stackalloc int[2];

        int needed = PlayerCommandQueue.Plan(numCmds: 4, totalCmds: 4, droppedPackets: 0, tiny);

        Assert.Equal(4, needed);
        Assert.True(needed > tiny.Length, "the answer is the NEED, not what fitted");
    }

    [Fact]
    public unsafe void APlanTooBigForTheBufferQueuesNOTHING()
    {
        // The half that matters. A prefix would leave the two queues
        // disagreeing by exactly the entries that did not fit, and every later
        // index would then name a different command on each side.
        var player = new EntityHandle(0x0BAD10);
        _queue.Forget(player);

        var cmds = new[] { MakeCmd(4), MakeCmd(3), MakeCmd(2), MakeCmd(1) };
        int[] order = new int[2];
        int planned;

        fixed (UserCmd* pCmds = cmds)
        fixed (int* pOrder = order)
            planned = _queue.Queue(player, pCmds, 4, 4, 0, pOrder, order.Length);

        Assert.Equal(4, planned);
        Assert.Equal(0, _queue.Depth(player));
    }

    //-------------------------------------------------------------------------
    // THE QUEUE.
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void QueueingAPacketMakesItsCommandsRunnableOldestFirst()
    {
        var player = new EntityHandle(0x0BAD01);
        _queue.Forget(player);

        QueueNumbered(player, numbers: new[] { 30, 20, 10 }, totalCmds: 3, dropped: 0);

        var seen = RunAndCapture(player, 3);

        Assert.Equal(new[] { 10, 20, 30 }, seen.Select(c => c.CommandNumber).ToArray());
    }

    [Fact]
    public unsafe void TwoPacketsBeforeAFrameBothRunInArrivalOrder()
    {
        // ProcessUsercmds can be reached more than once between frames; the
        // queue APPENDS, it does not replace.
        var player = new EntityHandle(0x0BAD02);
        _queue.Forget(player);

        QueueNumbered(player, new[] { 20, 10 }, totalCmds: 2, dropped: 0);
        QueueNumbered(player, new[] { 40, 30 }, totalCmds: 2, dropped: 0);

        var seen = RunAndCapture(player, 4);

        Assert.Equal(new[] { 10, 20, 30, 40 }, seen.Select(c => c.CommandNumber).ToArray());
    }

    [Fact]
    public unsafe void AReplayIsTheLastCommandWithItsTickAdvanced()
    {
        // The C++ this ports did `m_LastCmd.tick_count++` per replay, and
        // native does the same to its own copy. If the two disagreed about the
        // tick, the replayed commands would run on different clocks either side
        // of the boundary.
        var player = new EntityHandle(0x0BAD03);
        _queue.Forget(player);

        QueueNumbered(player, new[] { 7 }, totalCmds: 1, dropped: 0, tickCount: 100);
        RunAndCapture(player, 1);

        // Now a packet with one command and three dropped packets and no
        // backup: three replays then the new command.
        QueueNumbered(player, new[] { 8 }, totalCmds: 1, dropped: 3, tickCount: 200);

        var seen = RunAndCapture(player, 4);

        Assert.Equal(new[] { 101, 102, 103, 200 }, seen.Select(c => c.TickCount).ToArray());
        Assert.Equal(new[] { 7, 7, 7, 8 }, seen.Select(c => c.CommandNumber).ToArray());
    }

    [Fact]
    public unsafe void QueueDepthIsWhatWasPlanned()
    {
        var player = new EntityHandle(0x0BAD04);
        _queue.Forget(player);

        QueueNumbered(player, new[] { 3, 2, 1 }, totalCmds: 3, dropped: 0);

        Assert.Equal(3, _queue.Depth(player));
    }

    [Fact]
    public unsafe void ForgettingAPlayerDropsTheirQueue()
    {
        var player = new EntityHandle(0x0BAD05);
        _queue.Forget(player);

        QueueNumbered(player, new[] { 1 }, totalCmds: 1, dropped: 0);
        _queue.Forget(player);

        Assert.Equal(0, _queue.Depth(player));
    }

    //-------------------------------------------------------------------------
    // THE LOOP — the step's gate.
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void ManyCommandsCostOneQueueingCrossing()
    {
        // THE CLAIM, hermetically: the number of PlayerCommands calls is the
        // number of PACKETS, not the number of commands. Measured at two very
        // different N so a per-command crossing cannot hide in a small one.
        var player = new EntityHandle(0x0BAD06);

        foreach (int n in new[] { 1, 40 })
        {
            _queue.Forget(player);
            _queue.ResetCounters();

            QueueNumbered(player, Enumerable.Range(0, n).Reverse().ToArray(), totalCmds: n, dropped: 0);

            var (batches, queued, _) = _queue.Counters();

            Assert.Equal(1, batches);
            Assert.Equal(n, queued);

            int ran = RunAndCapture(player, n).Count;

            Assert.Equal(n, ran);
        }
    }

    [Fact]
    public unsafe void TheRunLoopPutsTheModulesDecisionsBetweenTheTwoNativeStages()
    {
        // THE ORDER IS THE PORT. The native function ran: prepare, phase 0, the
        // move, phase 1, the buttons, then the teardown and the publish.
        // Anything that reorders those is a behaviour change wearing a
        // refactor's clothes — publishing before the decisions would put every
        // state change they make one command late in the blob, and a phase 0
        // before the prepare would run with no current command and the previous
        // command's SharedRandom seed.
        var player = new EntityHandle(0x0BAD07);
        _queue.Forget(player);
        QueueNumbered(player, new[] { 1 }, totalCmds: 1, dropped: 0);

        var trace = new List<string>();
        var module = new RecordingModule(trace);

        _queue.Run(player, 1, module, (_, i, stage) =>
        {
            trace.Add($"native:{i}:{stage}");
            return 1;
        });

        Assert.Equal(
            new[] { "native:0:0", "phase0", "native:0:1", "phase1", "buttons", "native:0:2" },
            trace.ToArray());
    }

    [Fact]
    public unsafe void EveryCommandGetsItsOwnDecisionsAndItsOwnTwoStages()
    {
        var player = new EntityHandle(0x0BAD08);
        _queue.Forget(player);
        QueueNumbered(player, new[] { 3, 2, 1 }, totalCmds: 3, dropped: 0);

        var trace = new List<string>();
        var module = new RecordingModule(trace);

        int ran = _queue.Run(player, 3, module, (_, i, stage) =>
        {
            trace.Add($"native:{i}:{stage}");
            return 1;
        });

        Assert.Equal(3, ran);
        Assert.Equal(3, trace.Count(t => t == "phase0"));
        Assert.Equal(3, trace.Count(t => t == "phase1"));
        Assert.Equal(3, trace.Count(t => t == "buttons"));
        Assert.Equal(new[] { "native:0:0", "native:0:1", "native:0:2",
                             "native:1:0", "native:1:1", "native:1:2",
                             "native:2:0", "native:2:1", "native:2:2" },
                     trace.Where(t => t.StartsWith("native:", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public unsafe void TheButtonsReportedAreTheCommandsOwn()
    {
        var player = new EntityHandle(0x0BAD09);
        _queue.Forget(player);

        var cmds = new[] { MakeCmd(2, buttons: (int)InputButtons.Jump),
                           MakeCmd(1, buttons: (int)InputButtons.Attack) };
        QueueRaw(player, cmds, totalCmds: 2, dropped: 0);

        var trace = new List<string>();
        var module = new RecordingModule(trace);

        _queue.Run(player, 2, module, (_, _, _) => 1);

        // Oldest first, so Attack then Jump.
        Assert.Equal(new[] { (int)InputButtons.Attack, (int)InputButtons.Jump },
                     module.Pressed.ToArray());
    }

    [Fact]
    public unsafe void ANativeRefusalStopsTheLoopRatherThanRunningDecisionsForAMoveThatDidNotHappen()
    {
        var player = new EntityHandle(0x0BAD0A);
        _queue.Forget(player);
        QueueNumbered(player, new[] { 3, 2, 1 }, totalCmds: 3, dropped: 0);

        var trace = new List<string>();
        var module = new RecordingModule(trace);

        int ran = _queue.Run(player, 3, module,
                                         (_, i, stage) => i == 1 && stage == 0 ? 0 : 1);

        Assert.Equal(1, ran);
        Assert.Equal(1, trace.Count(t => t == "phase1"));
    }

    [Fact]
    public unsafe void TheLoopRunsTheSHORTEROfTheTwoQueuesWhenTheyDisagree()
    {
        // A disagreement is a defect in the plan and is warned about; running
        // to the longer of the two would walk off the end of one of them.
        var player = new EntityHandle(0x0BAD0B);
        _queue.Forget(player);
        QueueNumbered(player, new[] { 3, 2, 1 }, totalCmds: 3, dropped: 0);

        var trace = new List<string>();
        var module = new RecordingModule(trace);

        int ran = _queue.Run(player, 1, module, (_, _, _) => 1);

        Assert.Equal(1, ran);
    }

    [Fact]
    public unsafe void TheRunClearsTheQueueSoTheNextFrameStartsEmpty()
    {
        var player = new EntityHandle(0x0BAD0C);
        _queue.Forget(player);
        QueueNumbered(player, new[] { 2, 1 }, totalCmds: 2, dropped: 0);

        _queue.Run(player, 2, NoSink, (_, _, _) => 1);

        Assert.Equal(0, _queue.Depth(player));
    }

    //-------------------------------------------------------------------------
    // THE READOUT — what lets the crossing claim fail.
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void TheReadoutCountsTheCommandsACTUALLYRUN()
    {
        // THE CHECK THAT MUST BE ABLE TO FAIL. A build that queued commands and
        // never ran them costs exactly one crossing per packet, same as a
        // working one. `ran` is the number that separates them, and it comes
        // from inside the loop.
        var player = new EntityHandle(0x0BAD0D);
        _queue.Forget(player);
        _queue.ResetCounters();

        QueueNumbered(player, new[] { 4, 3, 2, 1 }, totalCmds: 4, dropped: 0);

        Assert.Equal(0, _queue.Counters().Ran);

        _queue.Run(player, 4, NoSink, (_, _, _) => 1);

        var (batches, queued, ran) = _queue.Counters();

        Assert.Equal(1, batches);
        Assert.Equal(4, queued);
        Assert.Equal(4, ran);
    }

    [Fact]
    public unsafe void ALoopThatRunsNothingReadsOutZeroWithTheCrossingStillCounted()
    {
        // The failure mode spelled out: the crossing happened, the work did not.
        var player = new EntityHandle(0x0BAD0E);
        _queue.Forget(player);
        _queue.ResetCounters();

        QueueNumbered(player, new[] { 4, 3, 2, 1 }, totalCmds: 4, dropped: 0);
        _queue.Run(player, 4, NoSink, (_, _, _) => 0);

        var (batches, _, ran) = _queue.Counters();

        Assert.Equal(1, batches);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void TheReadoutLineNamesTheCommandsRunAndThePerBatchRatio()
    {
        string line = PlayerCommandsReport.Line(batches: 4, queued: 40, ran: 40);

        Assert.Contains("batches=4", line, StringComparison.Ordinal);
        Assert.Contains("queued=40", line, StringComparison.Ordinal);
        Assert.Contains("ran=40", line, StringComparison.Ordinal);
        Assert.Contains("ranPerBatch=10.000", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReadoutLineDoesNotDivideByZero()
    {
        Assert.Contains("ranPerBatch=0.000", PlayerCommandsReport.Line(0, 0, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void ResettingTheReadoutZeroesIt()
    {
        _queue.ResetCounters();

        Assert.Equal((0L, 0L, 0L), _queue.Counters());
    }

    //-------------------------------------------------------------------------
    // THE NATIVE SIDE, read as text. Comment-stripped: 67 of 75 test files that
    // read a native source matched raw text, and a commented-out call reads as
    // a live one.
    //-------------------------------------------------------------------------

    [Fact]
    public void ProcessUsercmdsCrossesOnceAndNotOncePerCommand()
    {
        // plan_server_novalve.md P2.2: the packet's ONE crossing is in
        // ProcessUsercmds now -- decode, clamp, plan and queue all happen in
        // C# behind it -- and QueueUsercmds, whose crossing this watched, is
        // gone. The old slot must not be called from anywhere either.
        string body = NativeFunction(GameClientsPath(), "float CServerGameClients::ProcessUsercmds");

        Assert.Single(Regex.Matches(body, @"pCommands->PlayerDecodeCommands\("));
        Assert.DoesNotContain("pApi->PlayerCommands(", AbiParityTests.CodeOnly(File.ReadAllText(ServerPlayerPath())));
        Assert.DoesNotContain("pApi->PlayerCommands(", AbiParityTests.CodeOnly(File.ReadAllText(GameClientsPath())));
    }

    [Fact]
    public void TheQueueingCrossingIsNotInsideALoop()
    {
        // The count above is satisfied by ONE call SITE; this is what makes it
        // one CALL. A single site inside a `for` is still one crossing per
        // command, which is the exact thing this step removed — and the
        // function does contain loops (the flatten, and the plan's
        // materialisation), so "no loops anywhere" would not be checkable here.
        //
        // Brace depth is one half: the body of the function is depth 1,
        // anything nested inside a braced loop is deeper.
        string body = NativeFunction(GameClientsPath(), "float CServerGameClients::ProcessUsercmds");
        int call = body.IndexOf("pCommands->PlayerDecodeCommands(", StringComparison.Ordinal);

        Assert.True(call > 0, "ProcessUsercmds calls PlayerDecodeCommands");
        Assert.Equal(1, BraceDepthAt(body, call));
    }

    [Fact]
    public void NoBRACELESSLoopWrapsTheQueueingCrossingEither()
    {
        // THE HALF THE DEPTH CHECK CANNOT SEE, and it is not hypothetical: the
        // first mutation written against the fact above was
        //
        //     for ( int nRetry = 0; nRetry < 1; nRetry++ )
        //         nPlan = pApi->PlayerCommands( ... );
        //
        // which compiles, calls once per iteration, and leaves the brace depth
        // at 1 because it opens no brace. It SURVIVED, and this is what killed
        // it. A loop head immediately above the call with no `{` on it is the
        // whole of the shape.
        string body = NativeFunction(GameClientsPath(), "float CServerGameClients::ProcessUsercmds");
        int call = body.IndexOf("pCommands->PlayerDecodeCommands(", StringComparison.Ordinal);
        string[] lines = body[..call].Split('\n');

        foreach (string line in lines.TakeLast(3))
        {
            Assert.False(Regex.IsMatch(line, @"^\s*(for|while)\s*\(") && !line.Contains('{'),
                         $"a braceless loop head stands over the crossing: {line.Trim()}");
        }
    }

    [Fact]
    public void TheQueueingCrossingsResultIsBoundAtFunctionScopeAndReadThere()
    {
        // THE PROPERTY THAT IS ANCHORED ON COMPILABILITY rather than on layout,
        // which is what makes it hard to defeat by reformatting. The call's
        // answer is bound to a `const int` in the function's own scope, and
        // that constant is READ afterwards — so a loop body holding the
        // declaration, braced or not, would scope it to the loop and the reader
        // below would not compile.
        string body = NativeFunction(GameClientsPath(), "float CServerGameClients::ProcessUsercmds");
        int decl = body.IndexOf("const int nQueued = pCommands->PlayerDecodeCommands(", StringComparison.Ordinal);

        Assert.True(decl > 0, "the crossing's answer is bound to a const at function scope");
        Assert.Equal(1, BraceDepthAt(body, decl));

        int read = body.IndexOf("nQueued", decl + 30, StringComparison.Ordinal);

        Assert.True(read > decl, "the bound constant is read after the call");
        Assert.Equal(1, BraceDepthAt(body, read));
    }

    [Fact]
    public void TheDepthMeasureCanTellATopLevelCallFromANestedOne()
    {
        // The check above is only worth anything if its measure separates the
        // two cases, so here it is separating them.
        const string sample = "void f()\n{\n\tA();\n\tfor ( ;; )\n\t{\n\t\tB();\n\t}\n}";

        Assert.Equal(1, BraceDepthAt(sample, sample.IndexOf("A()", StringComparison.Ordinal)));
        Assert.Equal(2, BraceDepthAt(sample, sample.IndexOf("B()", StringComparison.Ordinal)));
    }

    [Fact]
    public void RunQueuedCommandsCrossesOncePerPlayerPerFrame()
    {
        string body = NativeFunction("void CStandalonePlayer::RunQueuedCommands");

        Assert.Single(Regex.Matches(body, @"pCommands->PlayerRunCommands\("));
        Assert.Empty(Regex.Matches(body, @"\bfor\s*\("));
    }

    [Fact]
    public void TheThreePerCommandCrossingsAreGoneFromTheNativeLoop()
    {
        // OnStandalonePlayerCommand at phase 0 and phase 1, and
        // OnPlayerRunCommand: three crossings per command, and the whole cost
        // this step removed. They are managed calls inside the loop now.
        string body = NativeFunction("bool CStandalonePlayer::RunCommandStage");

        Assert.DoesNotContain("OnStandalonePlayerCommand", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPlayerRunCommand", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCommandCrossingSurvivesAnywhereInTheServersPlayerFile()
    {
        // The per-function facts above cannot see a call that moved to a helper.
        string code = AbiParityTests.CodeOnly(File.ReadAllText(ServerPlayerPath()));

        Assert.Empty(Regex.Matches(code, @"OnStandalonePlayerCommand\s*\("));
        Assert.Empty(Regex.Matches(code, @"OnPlayerRunCommand\s*\("));
    }

    [Fact]
    public void TheHandleResolverChecksTheSerialAndRefusesTheZeroSentinel()
    {
        // A default handle is entry ZERO, a real slot — worldspawn. Decoding
        // only the entry index would answer whichever player later occupied the
        // slot, which is how two other lanes measured the wrong path.
        string body = NativeFunction("static CStandalonePlayer *StandalonePlayer_FromManagedHandle");

        Assert.Contains("INVALID_EHANDLE_INDEX", body, StringComparison.Ordinal);
        Assert.Contains("ManagedHandle() == hPlayer", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSeedIsSETInTheStageBEFOREPhaseZeroAndCLEAREDInTheStageAFTERPhaseOne()
    {
        // SharedRandom* reads the seed the command set, and both managed phases
        // are between the stages. A seed set after phase 0 would leave a
        // pre-think seedless; one cleared before phase 1 would leave the weapon
        // frame seedless. Both are silent divergences from what the single
        // native function did, and both are what the three-stage split exists
        // to prevent.
        string stage = NativeFunction("bool CStandalonePlayer::RunCommandStage");

        int clear = stage.IndexOf("EntityPrediction_SetSeedFromCommand( NULL )", StringComparison.Ordinal);
        int set = stage.IndexOf("EntityPrediction_SetSeedFromCommand( ucmd )", StringComparison.Ordinal);
        int stageTwo = stage.IndexOf("nStage == 2", StringComparison.Ordinal);

        Assert.True(set > 0, "stage 0 sets the seed from the command");
        Assert.True(clear > 0, "stage 2 clears it");
        Assert.True(clear > stageTwo && stageTwo > 0, "the clear is inside stage 2");
        Assert.True(set > clear, "the set is in stage 0, which is written after the stage 2 arm");
    }

    [Fact]
    public void ARefusingStageSetsNothingBeforeItRefuses()
    {
        // WHY RunQueuedCommands NEEDS NO BELT-AND-BRACES TEARDOWN. A refusal
        // stops the managed loop, and the command that refused must therefore
        // not have left the seed or the current command set behind it — or the
        // seed would stay live outside a usercmd and every SharedRandom* after
        // it would be answering from a command that is over.
        //
        // The property is an ORDER inside the function: both refusal checks
        // come before the first assignment.
        string stage = NativeFunction("bool CStandalonePlayer::RunCommandStage");

        int indexCheck = stage.IndexOf("nIndex >= m_PendingCmds.Count()", StringComparison.Ordinal);
        int stageCheck = stage.IndexOf("nStage < 0 || nStage > 2", StringComparison.Ordinal);
        int set = stage.IndexOf("EntityPrediction_SetSeedFromCommand( ucmd )", StringComparison.Ordinal);
        int current = stage.IndexOf("m_pCurrentCommand = ucmd", StringComparison.Ordinal);

        Assert.True(indexCheck > 0 && stageCheck > indexCheck, "both refusals are stated");
        Assert.True(set > stageCheck, "the seed is set after both refusal checks");
        Assert.True(current > stageCheck, "the current command is set after both refusal checks");
    }

    [Fact]
    public void TheSeedTeardownIsTheCOMMANDSAndNotTheQUEUES()
    {
        // engine_core_retype_test.cpp's PredictionSeed pins TWO
        // EntityPrediction_SetSeedFromCommand call sites in this file, one per
        // direction, and that count is right: a third here would be a repeat of
        // a teardown the command already does — see the comment in
        // RunQueuedCommands and the fact above for why it is not needed.
        string body = NativeFunction("void CStandalonePlayer::RunQueuedCommands");

        Assert.DoesNotContain("EntityPrediction_SetSeedFromCommand", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePublishIsTheLASTStageAndSoRunsAfterTheDecisions()
    {
        string stage = NativeFunction("bool CStandalonePlayer::RunCommandStage");
        int guard = stage.IndexOf("nStage == 2", StringComparison.Ordinal);
        int sync = stage.IndexOf("SyncToBlob()", StringComparison.Ordinal);

        Assert.True(guard >= 0 && sync > guard,
                    "SyncToBlob is the body of stage 2, not the tail of an earlier one");
    }

    [Fact]
    public void TheTickbaseStepIsInTheSAMEStageAsThePublish()
    {
        // The native function incremented the tickbase AFTER the buttons and
        // immediately before SyncToBlob, and SyncToBlob writes the tickbase into
        // the blob. Splitting them would send the wire a tickbase one command
        // out of step with the origin beside it.
        string stage = NativeFunction("bool CStandalonePlayer::RunCommandStage");
        int step = stage.IndexOf("m_nTickBase++", StringComparison.Ordinal);
        int guard = stage.IndexOf("nStage == 2", StringComparison.Ordinal);
        int sync = stage.IndexOf("SyncToBlob()", StringComparison.Ordinal);

        Assert.True(step > guard && step < sync,
                    "the tickbase step is between stage 2's guard and its publish");
    }

    [Fact]
    public void TheMoveStageCarriesTheMovementBracketAndNothingElseDoes()
    {
        // Step 3e's bracket -- CGameMovement::player is set by
        // StartTrackPredictionErrors and asserted against inside ProcessMovement.
        string move = NativeFunction("bool CStandalonePlayer::RunCommandMove");

        Assert.Contains("StartTrackPredictionErrors", move, StringComparison.Ordinal);
        Assert.Contains("ProcessMovement", move, StringComparison.Ordinal);
        Assert.Contains("FinishTrackPredictionErrors", move, StringComparison.Ordinal);

        string stage = NativeFunction("bool CStandalonePlayer::RunCommandStage");

        Assert.DoesNotContain("StartTrackPredictionErrors", stage, StringComparison.Ordinal);
    }

    //-------------------------------------------------------------------------
    // Helpers.
    //-------------------------------------------------------------------------

    /// <summary>A sink that records nothing, for facts about the queue itself.</summary>
    private sealed class SilentSink : IPlayerCommandSink
    {
        public void Command(EntityHandle player, in UserCmd cmd, int phase) { }
        public void Buttons(EntityHandle player, int buttons) { }
    }

    private static readonly SilentSink NoSink = new();

    private static int[] PlanOf(int numCmds, int totalCmds, int dropped)
    {
        Span<int> buffer = stackalloc int[256];
        int count = PlayerCommandQueue.Plan(numCmds, totalCmds, dropped, buffer);

        Assert.True(count <= buffer.Length, $"plan of {count} fits the probe buffer");

        return buffer[..count].ToArray();
    }

    private static UserCmd MakeCmd(int commandNumber, int tickCount = 0, int buttons = 0)
        => new() { StructSize = (uint)sizeof(int), CommandNumber = commandNumber,
                   TickCount = tickCount, Buttons = buttons };

    private unsafe void QueueNumbered(EntityHandle player, int[] numbers, int totalCmds,
                                             int dropped, int tickCount = 0)
        => QueueRaw(player, numbers.Select(n => MakeCmd(n, tickCount)).ToArray(), totalCmds, dropped);

    private unsafe void QueueRaw(EntityHandle player, UserCmd[] cmds, int totalCmds, int dropped)
    {
        int[] order = new int[256];

        fixed (UserCmd* pCmds = cmds)
        fixed (int* pOrder = order)
        {
            int planned = _queue.Queue(player, pCmds, cmds.Length, totalCmds, dropped,
                                                   pOrder, order.Length);

            Assert.True(planned >= 0 && planned <= order.Length, $"the plan of {planned} was queued");
        }
    }

    private List<UserCmd> RunAndCapture(EntityHandle player, int expected)
    {
        var seen = new List<UserCmd>();
        var module = new CapturingModule(seen);

        int ran = _queue.Run(player, expected, module, (_, _, _) => 1);

        Assert.Equal(expected, ran);

        return seen;
    }

    private sealed class RecordingModule : IPlayerCommandSink
    {
        private readonly List<string> _trace;

        public RecordingModule(List<string> trace) => _trace = trace;

        public List<int> Pressed { get; } = new();

        public void Command(EntityHandle player, in UserCmd cmd, int phase) => _trace.Add($"phase{phase}");

        public void Buttons(EntityHandle player, int buttons)
        {
            _trace.Add("buttons");
            Pressed.Add(buttons);
        }
    }

    private sealed class CapturingModule : IPlayerCommandSink
    {
        private readonly List<UserCmd> _seen;

        public CapturingModule(List<UserCmd> seen) => _seen = seen;

        public void Command(EntityHandle player, in UserCmd cmd, int phase)
        {
            if (phase == 1)
                _seen.Add(cmd);
        }

        public void Buttons(EntityHandle player, int buttons) { }
    }

    private static string ServerPlayerPath()
        => Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "shell_standalone_player.cpp");

    /// <summary>
    /// One function's body from the native file, comment-stripped, from its
    /// signature to the closing brace at column zero.
    /// </summary>
    private static string GameClientsPath()
        => Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "server", "shell_gameclients.cpp");

    private static string NativeFunction(string signature) => NativeFunction(ServerPlayerPath(), signature);

    private static string NativeFunction(string path, string signature)
    {
        string code = AbiParityTests.CodeOnly(File.ReadAllText(path));
        int at = code.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(at >= 0, $"{signature} is in {Path.GetFileName(path)}");

        int end = code.IndexOf("\n}", at, StringComparison.Ordinal);

        Assert.True(end > at, $"{signature} has a body that ends");

        return code[at..(end + 2)];
    }

    /// <summary>
    /// How many unclosed braces stand between the start of the text and
    /// <paramref name="at"/>. The text is already comment-stripped and string
    /// literals were copied whole by <c>CodeOnly</c>, so a brace inside either
    /// cannot reach this.
    /// </summary>
    private static int BraceDepthAt(string text, int at)
    {
        int depth = 0;

        for (int i = 0; i < at; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
        }

        return depth;
    }
}
