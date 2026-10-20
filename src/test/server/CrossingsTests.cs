using System.Globalization;
using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The crossing counter itself — <c>plan_entity.md</c> step 0c.
///
/// <para>
/// <b>Why every fact here resets first.</b> <see cref="Crossings"/> is
/// deliberately process-wide and deliberately never zeroed except by
/// <see cref="_crossings.Reset"/>, which is what lets it survive a hot reload of
/// either side. In a test process that makes it shared mutable state, so each
/// fact opens its own interval. Parallelisation is off for this assembly
/// (<c>AssemblyInfo.cs</c>), so an interval opened here is not walked into by
/// another fact.
/// </para>
///
/// <para>
/// <b>The ops these facts use are arbitrary on purpose.</b> Nothing here should
/// pin a particular thunk's meaning — that is <c>CrossingsAuditTests</c>' job,
/// against <c>Entry.cs</c> — so these use whichever ops make the arithmetic
/// legible, except the two that ARE named contracts:
/// <see cref="CrossingOp.ServerGameFrame"/> and <see cref="CrossingOp.ClientFrame"/>
/// are the per-frame denominators and each has a fact of its own.
/// </para>
/// </summary>
public class CrossingsTests
{
    // This test class's own counters (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's counts.
    private readonly Crossings _crossings = new();

    [Fact]
    public void CountIncrementsTheOpItNames()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        Assert.Equal(1, _crossings.Read(CrossingOp.ServerGameFrame));
    }

    [Fact]
    public void CountLeavesEveryOtherOpAlone()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        Assert.Equal(0, _crossings.Read(CrossingOp.ClientFrame));
        Assert.Equal(0, _crossings.Read(CrossingOp.ServerThink));
    }

    [Fact]
    public void CountAccumulatesWithinAnInterval()
    {
        _crossings.Reset();

        for (int i = 0; i < 7; i++)
            _crossings.Count(CrossingOp.ServerThink);

        Assert.Equal(7, _crossings.Read(CrossingOp.ServerThink));
    }

    /// <summary>
    /// The other half of the gate: after a reset the counter reads zero. Stated
    /// over every op rather than the one just used, because the failure worth
    /// catching is a reset that clears the element it was handed and leaves the
    /// rest of the array standing.
    /// </summary>
    [Fact]
    public void ResetZeroesEveryOp()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ClientFrame);
        _crossings.Count(CrossingOp.ServerThink);

        _crossings.Reset();

        foreach (var op in Enum.GetValues<CrossingOp>())
            Assert.Equal(0, _crossings.Read(op));
    }

    [Fact]
    public void ResetZeroesTheTotal()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Reset();

        Assert.Equal(0, _crossings.Total);
    }

    /// <summary>
    /// A consumer polling the counters needs to know when the interval changed
    /// under it: two polls with different reset counts are not two points on one
    /// curve, and a live gate that subtracted them would report a negative rate.
    /// </summary>
    [Fact]
    public void ResetIsCounted()
    {
        long before = _crossings.Resets;
        _crossings.Reset();

        Assert.Equal(before + 1, _crossings.Resets);
    }

    [Fact]
    public void TotalIsTheSumOfEveryOp()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ClientFrame);

        Assert.Equal(3, _crossings.Total);
    }

    [Fact]
    public void ServerFramesReadsTheServerGameFrameOp()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ServerGameFrame);

        Assert.Equal(2, _crossings.ServerFrames);
    }

    [Fact]
    public void ClientFramesReadsTheClientFrameOp()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ClientFrame);

        Assert.Equal(1, _crossings.ClientFrames);
    }

    [Fact]
    public void ActiveOmitsOpsNothingHasEntered()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        Assert.Equal(new[] { "ServerGameFrame" }, _crossings.Active().Select(a => a.Name));
    }

    [Fact]
    public void ActiveIsBiggestFirst()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ClientFrame);
        _crossings.Count(CrossingOp.ClientFrame);

        Assert.Equal(new[] { "ClientFrame", "ServerGameFrame" }, _crossings.Active().Select(a => a.Name));
    }

    /// <summary>
    /// Ties break by name, so the report is byte-identical between two runs that
    /// counted the same things. A live gate diffing two reports must not see a
    /// change that is only the sort being unstable.
    /// </summary>
    [Fact]
    public void ActiveBreaksTiesByName()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ClientFrame);

        Assert.Equal(new[] { "ClientFrame", "ServerGameFrame" }, _crossings.Active().Select(a => a.Name));
    }

    [Fact]
    public void ReportNamesEachActiveOpWithItsCount()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerThink);
        _crossings.Count(CrossingOp.ServerThink);
        _crossings.Count(CrossingOp.ServerThink);

        Assert.Contains("ServerThink 3", _crossings.Report());
    }

    [Fact]
    public void ReportOmitsOpsNothingHasEntered()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerThink);

        Assert.DoesNotContain("ClientFrame", _crossings.Report());
    }

    /// <summary>
    /// The report says what it is a report OF. A reader who takes the total for
    /// the whole boundary cost has been told a number that is true of half of
    /// it: managed calling an <c>EngineApi</c> slot is not counted here at all.
    /// </summary>
    [Fact]
    public void ReportSaysItIsInboundOnly()
    {
        Assert.Contains("inbound", _crossings.Report());
    }

    [Fact]
    public void ReportSaysSoWhenNothingHasCrossed()
    {
        _crossings.Reset();

        Assert.Contains("nothing has crossed", _crossings.Report());
    }

    [Fact]
    public void ReportCarriesTheResetCount()
    {
        _crossings.Reset();

        Assert.Contains($"resets {_crossings.Resets}", _crossings.Report());
    }

    /// <summary>
    /// The number every budget in the plan is stated in. Four crossings over two
    /// server frames is two per frame, and the report says so rather than making
    /// the reader divide.
    /// </summary>
    [Fact]
    public void ReportGivesEachOpAsARatePerServerFrame()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);
        _crossings.Count(CrossingOp.ServerGameFrame);

        for (int i = 0; i < 4; i++)
            _crossings.Count(CrossingOp.ServerThink);

        Assert.Contains("ServerThink 4 (2.000/frame)", _crossings.Report());
    }

    [Fact]
    public void ReportGivesNoRateBeforeTheFirstServerFrame()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerThink);

        Assert.DoesNotContain("/frame", _crossings.Report());
    }

    [Fact]
    public void PerFrameIsZeroWhenNoFramesHaveRun()
    {
        Assert.Equal("0.000", Crossings.PerFrame(17, 0));
    }

    /// <summary>
    /// Invariant culture, explicitly. This string is parsed by the live gate, and
    /// on a machine whose locale writes a comma decimal separator "1,573" would
    /// be read as one thousand five hundred and seventy-three crossings a frame.
    /// The fact runs under such a culture rather than trusting the build agent's.
    /// </summary>
    [Fact]
    public void PerFrameUsesAnInvariantDecimalPoint()
    {
        var was = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.500", Crossings.PerFrame(3, 2));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = was;
        }
    }

    //-------------------------------------------------------------------------
    // The ABI slot's half: size, then fill.
    //-------------------------------------------------------------------------

    [Fact]
    public void WriteReportSizesAgainstAnEmptyBuffer()
    {
        _crossings.Reset();

        Assert.True(_crossings.WriteReport(Span<byte>.Empty, reset: false) > 1);
    }

    [Fact]
    public void WriteReportCountsItsOwnTerminator()
    {
        _crossings.Reset();

        int needed = _crossings.WriteReport(Span<byte>.Empty, reset: false);

        Assert.Equal(Encoding.UTF8.GetByteCount(_crossings.Report()) + 1, needed);
    }

    /// <summary>
    /// The whole point of size-then-fill: a buffer one byte short gets NOTHING,
    /// not a truncated table that reads as a whole one.
    /// </summary>
    [Fact]
    public void WriteReportWritesNothingIntoAShortBuffer()
    {
        _crossings.Reset();

        int needed = _crossings.WriteReport(Span<byte>.Empty, reset: false);
        var buffer = new byte[needed - 1];
        Array.Fill(buffer, (byte)0xAB);

        _crossings.WriteReport(buffer, reset: false);

        Assert.All(buffer, b => Assert.Equal(0xAB, b));
    }

    [Fact]
    public void WriteReportAnswersTheSizeAgainWhenTheBufferIsShort()
    {
        _crossings.Reset();

        int needed = _crossings.WriteReport(Span<byte>.Empty, reset: false);

        Assert.Equal(needed, _crossings.WriteReport(new byte[needed - 1], reset: false));
    }

    [Fact]
    public void WriteReportFillsTheExactReportText()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        string expected = _crossings.Report();
        var buffer = new byte[_crossings.WriteReport(Span<byte>.Empty, reset: false)];
        _crossings.WriteReport(buffer, reset: false);

        Assert.Equal(expected, Encoding.UTF8.GetString(buffer, 0, buffer.Length - 1));
    }

    [Fact]
    public void WriteReportTerminatesTheBuffer()
    {
        _crossings.Reset();

        var buffer = new byte[_crossings.WriteReport(Span<byte>.Empty, reset: false)];
        _crossings.WriteReport(buffer, reset: false);

        Assert.Equal(0, buffer[^1]);
    }

    /// <summary>
    /// THE ONE THAT MAKES THE TWO-CALL SHAPE SAFE. A caller sizes with an empty
    /// buffer and fills on the second call; a reset honoured on the first would
    /// zero the counters between the two, and the report that came back would
    /// describe the empty interval rather than the one being closed.
    /// </summary>
    [Fact]
    public void WriteReportIgnoresResetOnASizingCall()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        _crossings.WriteReport(Span<byte>.Empty, reset: true);

        Assert.Equal(1, _crossings.Read(CrossingOp.ServerGameFrame));
    }

    [Fact]
    public void WriteReportResetsAfterFilling()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        var buffer = new byte[_crossings.WriteReport(Span<byte>.Empty, reset: false)];
        _crossings.WriteReport(buffer, reset: true);

        Assert.Equal(0, _crossings.Read(CrossingOp.ServerGameFrame));
    }

    /// <summary>
    /// And the reset happens AFTER the text is composed, so
    /// <c>cs_sv_crossings reset</c> prints the interval it just closed rather
    /// than the empty one it opened.
    /// </summary>
    [Fact]
    public void WriteReportFillsTheClosedIntervalNotTheNewOne()
    {
        _crossings.Reset();

        for (int i = 0; i < 5; i++)
            _crossings.Count(CrossingOp.ServerThink);

        var buffer = new byte[_crossings.WriteReport(Span<byte>.Empty, reset: false)];
        _crossings.WriteReport(buffer, reset: true);

        Assert.Contains("ServerThink 5", Encoding.UTF8.GetString(buffer, 0, buffer.Length - 1));
    }

    [Fact]
    public void WriteReportLeavesTheCountersAloneWhenNotAskedToReset()
    {
        _crossings.Reset();
        _crossings.Count(CrossingOp.ServerGameFrame);

        var buffer = new byte[_crossings.WriteReport(Span<byte>.Empty, reset: false)];
        _crossings.WriteReport(buffer, reset: false);

        Assert.Equal(1, _crossings.Read(CrossingOp.ServerGameFrame));
    }

    //-------------------------------------------------------------------------
    // The invariants the implementation rests on.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The name table is indexed by the enum value directly, which is correct
    /// only while the values run 0..OpCount-1 with no gaps and no duplicates.
    /// A hand-written enum is exactly where that stops being true.
    /// </summary>
    [Fact]
    public void OpValuesAreContiguousFromZero()
    {
        var values = Enum.GetValues<CrossingOp>().Select(v => (int)v).ToArray();

        Assert.Equal(Enumerable.Range(0, values.Length), values);
    }

    [Fact]
    public void OpCountIsTheNumberOfOps()
    {
        Assert.Equal(Enum.GetValues<CrossingOp>().Length, Crossings.OpCount);
    }

    /// <summary>
    /// <see cref="_crossings.Count"/> runs at the top of a thunk, above its
    /// try/catch in most of them, so an exception thrown here would unwind into
    /// the engine's C++ frames — the one failure this file is not allowed to
    /// cause. An out-of-range op is dropped instead.
    /// </summary>
    [Fact]
    public void CountIgnoresAnOpOutsideTheEnum()
    {
        _crossings.Reset();
        _crossings.Count((CrossingOp)Crossings.OpCount);
        _crossings.Count((CrossingOp)(-1));

        Assert.Equal(0, _crossings.Total);
    }

    [Fact]
    public void ReadAnswersZeroForAnOpOutsideTheEnum()
    {
        Assert.Equal(0, _crossings.Read((CrossingOp)Crossings.OpCount));
    }

    /// <summary>
    /// Interlocked and not <c>++</c>. Thunks are entered from the engine's main
    /// thread almost always, but not only, and a lost increment on a counter
    /// whose job is to say "this ran once per frame" is indistinguishable from
    /// the thing it watches for. A plain increment fails this in the low
    /// thousands of iterations.
    /// </summary>
    [Fact]
    public void CountLosesNothingUnderConcurrency()
    {
        _crossings.Reset();

        const int Threads = 8;
        const int Each = 20_000;

        var threads = new Thread[Threads];
        for (int t = 0; t < Threads; t++)
        {
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < Each; i++)
                    _crossings.Count(CrossingOp.ServerThink);
            });
            threads[t].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        Assert.Equal((long)Threads * Each, _crossings.Read(CrossingOp.ServerThink));
    }
}
