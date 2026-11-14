using SourceSharp.Ai.Hl2mp.Trees;
using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay cost</c> (<see cref="CostTool"/>): a recording replayed for what its brains cost.
/// Costing changes no intent; the parts account for every reading of the clock in a step but the
/// session's own two; the believed runs it is asked for reach the brains (the same recording at a
/// faster belief spreads in more hops); and a recording that is not there is an error that says so.
/// The recording is <see cref="ThreadDeterminismTests.Make"/>'s: eight bots whose three opponents'
/// beliefs spread over a grid. The clock counts its own readings, so every number is exact.
/// </summary>
public sealed class CostToolTests
{
    private static readonly Lazy<(byte[] Recording, MemoryWorld World)> Session = new(ThreadDeterminismTests.Make);

    private static Func<double> Counting()
    {
        double now = 0;
        return () => now++;
    }

    private static CostReport Measure(string bodies = "-")
        => CostTool.Measure(new MemoryStream(Session.Value.Recording), CostTool.Hl2mp, Session.Value.World.Graph!, null, CostTool.Bodies(bodies), Counting());

    [Fact]
    public void CostingAReplayLeavesItsIntentsTheRecordings()
    {
        AiReplayResult r = AiReplayer.ReplayForCost(new MemoryStream(Session.Value.Recording), new InlineTransport(), _ => (Session.Value.World.Graph, null),
                                                    setup => new Hl2mpDecider(setup), session =>
                                                    {
                                                        Func<double> clock = Counting();
                                                        session.CpuClock = clock;
                                                        session.Costs = _ => new StepCost(clock);
                                                    });
        Assert.True(r.RecordedIntentCount > 300, $"the control: {r.RecordedIntentCount} intents recorded");
        Assert.True(r.Identical, "the costed replay's intents differ from the recording's");
    }

    [Fact]
    public void TheReportCountsEveryBotAndEveryStepOfTheRecording()
    {
        CostReport report = Measure();
        Assert.Equal((8, (long)report.Intents), (report.Bots, report.Steps));
        Assert.True(report.Steps > 300, $"the control: {report.Steps} steps");
    }

    /// <summary>
    /// The parts tile the step: on a clock that counts its readings, the session's clock round a
    /// step sees two readings more than the parts were charged (its own end, and the cost's
    /// beginning), so no reading in a step, and no time, is outside a part.
    /// </summary>
    [Fact]
    public void ThePartsAccountForTheWholeStep()
    {
        CostReport report = Measure();
        Assert.Equal(report.StepMicros, report.Parts.Sum(p => p.Micros) + 2 * report.Steps);
    }

    [Fact]
    public void TheBeliefsSpreadIsCountedInHops()
    {
        CostReport report = Measure();
        PartCost predict = report.Parts.Single(p => p.Part == StepPart.Predict);
        Assert.True(predict.Units > 0 && predict.Micros > 0, $"{predict.Units} hops, {predict.Micros} us");
    }

    /// <summary>The bodies asked for are what the brains believe: everybody running twice as fast spreads the same beliefs in about twice the hops.</summary>
    [Fact]
    public void ABelievedRunTwiceAsFastSpreadsInTwiceTheHops()
    {
        long slow = Measure("-").Parts.Single(p => p.Part == StepPart.Predict).Units;
        long fast = Measure("380").Parts.Single(p => p.Part == StepPart.Predict).Units;
        Assert.InRange(fast, slow * 18 / 10, slow * 23 / 10);
    }

    [Fact]
    public void NoBodiesAskedIsTheRecordingsOwn() => Assert.Null(CostTool.Bodies("-"));

    [Fact]
    public void ANumberIsEverybodysRun()
    {
        BodyKnowledge b = CostTool.Bodies("300")!;
        Assert.Equal((300f, 300f, 0), (b.Of(0).RunSpeed, b.Of(3).RunSpeed, b.Classes.Length));
    }

    /// <summary>Rows are a class's run each; a class with no row, and one not yet seen, runs at their mean.</summary>
    [Fact]
    public void RowsAreEachClasssRunAndTheirMeanForTheRest()
    {
        BodyKnowledge b = CostTool.Bodies("1=400,3=240")!;
        Assert.Equal((400f, 240f, 320f, 320f), (b.Of(1).RunSpeed, b.Of(3).RunSpeed, b.Of(2).RunSpeed, b.Of(0).RunSpeed));
    }

    /// <summary>"tf2" is the nine classes' runs as the shipped scripts have them: the Scout's 400, the Heavy's 230.</summary>
    [Fact]
    public void TF2sBodiesAreItsNineClasses()
    {
        BodyKnowledge b = CostTool.Bodies(CostTool.TF2)!;
        Assert.Equal((9, 400f, 230f), (b.Classes.Length, b.Of(1).RunSpeed, b.Of(6).RunSpeed));
    }

    [Fact]
    public void TheTableHasARowAPartThenTheWholeStep()
    {
        string[] lines = CostTool.Table(Measure()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["part", .. Enum.GetValues<StepPart>().Select(p => p.ToString().ToLowerInvariant()), "parts", "step"],
                     lines.Select(l => l.Split(' ')[0]));
    }

    /// <summary>A recording is build output: one that is not there is an error naming it and how one is made, never an empty table.</summary>
    [Fact]
    public void AMissingRecordingIsAnErrorThatSaysHowOneIsMade()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        string missing = Path.Combine(Path.GetTempPath(), "ss-no-such-recording.airec");
        int rc = CostTool.Run([CostTool.TF2, "ctf_2fort.nav3d", "ctf_2fort.bsp", missing], output, error);
        Assert.Equal((1, ""), (rc, output.ToString()));
        Assert.Contains($"no recording {missing}", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("ai_record start", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AGameWithNoTreesIsRefused()
        => Assert.Throws<ArgumentException>(() => CostTool.Deciders("portal"));
}
