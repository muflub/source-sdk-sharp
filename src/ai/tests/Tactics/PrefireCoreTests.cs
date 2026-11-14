using SourceSharp.Ai.Tests.TF2;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// The core's prefire (lifted from the HL2MP decider, plan_bots_teams_tf2_part_g G.2.1): the line channel, the belief,
/// the opening and the run's states, each on its own. <see cref="PrefireTests"/> holds HL2MP's decider over them.
/// </summary>
public sealed class PrefireCoreTests
{
    private const float TickSeconds = 0.015f;
    private static readonly ActorId Bot = new(1, 1), Them = new(2, 1);
    private static readonly AiVector There = new(500f, 0f, 0f);

    private static (int, AiVector, bool) Answer(LineAsks lines, bool clear, AiVector? at = null, int? ask = null) => (ask ?? lines.Ask, at ?? lines.At, clear);

    // ------------------------------------------------------------ the line channel

    [Fact]
    public void AnAnswerToTheAskOutstandingIsReadOnce()
    {
        var lines = new LineAsks();
        lines.Reask(There, default, again: false);
        Assert.True(lines.TryAnswer(Answer(lines, true)));
        Assert.False(lines.TryAnswer(Answer(lines, true)));
    }

    [Fact]
    public void AnAnswerToAnotherAskIsNotRead()
    {
        var lines = new LineAsks();
        lines.Reask(There, default, again: false);
        Assert.False(lines.TryAnswer(Answer(lines, true, ask: lines.Ask + 1)));
        Assert.False(lines.TryAnswer((0, There, true)));
    }

    [Fact]
    public void NothingAskedIsNothingAnswered()
        => Assert.False(new LineAsks().TryAnswer((0, default, true)));

    [Fact]
    public void AnAskOutstandingIsNotAskedAgainUnlessItsPointMoved()
    {
        var lines = new LineAsks();
        lines.Reask(There, default, again: false);
        lines.Reask(There, default, again: false);
        Assert.Equal(1, lines.Ask);
        lines.Reask(There * 2f, new AiVector(1f, 2f, 3f), again: true);
        Assert.Equal((2, There * 2f, new AiVector(1f, 2f, 3f)), (lines.Ask, lines.At, lines.From));
    }

    [Fact]
    public void AnAnsweredAskIsAskedAgain()
    {
        var lines = new LineAsks();
        lines.Reask(There, default, again: false);
        lines.TryAnswer(Answer(lines, false));
        lines.Reask(There, default, again: false);
        Assert.Equal((2, 1), (lines.Ask, lines.Answered));
    }

    // ------------------------------------------------------------ the belief

    private static BotMemory Memory()
        => new(Bot, 33, MemoryParams.For(Duel.Preset("normal")), new MemoryWorld(MapKnowledge.Empty(new WeaponKnowledge([])), null, TickSeconds, id => id.ToString()), new HabitStore());

    /// <summary>Seen at <paramref name="seenAt"/> where it stood at <paramref name="at"/>, and lost from sight there.</summary>
    private static KnownActor Lost(BotMemory m, int seenAt, AiVector at)
    {
        m.Apply(AiEvent.SightGained(Bot, new Tick(seenAt), Them, at, default, 1f));
        m.Apply(AiEvent.SightLost(Bot, new Tick(seenAt), Them, at, default));
        return m.Board.Of(Them, make: false)!;
    }

    [Fact]
    public void JustLostFromSightIsBelievedWithItsAgeAndWindow()
    {
        KnownActor a = Lost(Memory(), 100, There);
        Assert.True(PrefireBelief.Of(a, new Tick(160), TickSeconds, null, null, out AiVector at, out AiVector last, out float age, out float window));
        Assert.Equal((There, There, PrefireBelief.SeenSeconds), (at, last, window));
        Assert.Equal(60 * TickSeconds, age, 4);
    }

    [Fact]
    public void LostFromSightTooLongAgoIsNoBelief()
    {
        KnownActor a = Lost(Memory(), 100, There);
        int past = 100 + (int)(PrefireBelief.SeenSeconds / TickSeconds) + 2;
        Assert.True(PrefireBelief.Of(a, new Tick(past - 4), TickSeconds, null, null, out _, out _, out _, out _));
        Assert.False(PrefireBelief.Of(a, new Tick(past), TickSeconds, null, null, out _, out _, out _, out _));
    }

    [Fact]
    public void JustHeardIsBelievedWhereTheSoundWas()
    {
        BotMemory m = Memory();
        m.Apply(AiEvent.SoundHeard(Bot, new Tick(100), SoundKind.Footstep, There, 0.9f, Them, -1));
        KnownActor a = m.Board.Of(Them, make: false)!;
        Assert.True(PrefireBelief.Of(a, new Tick(120), TickSeconds, null, null, out AiVector at, out AiVector last, out _, out float window));
        Assert.Equal((There, There, PrefireBelief.HeardSeconds), (at, last, window));
        int past = 100 + (int)(PrefireBelief.HeardSeconds / TickSeconds) + 2;
        Assert.False(PrefireBelief.Of(a, new Tick(past), TickSeconds, null, null, out _, out _, out _, out _));
    }

    [Fact]
    public void AHabitIsBelievedOnlyWhenTheApproachPreAimsIt()
    {
        KnownActor a = Lost(Memory(), 100, There);
        var corner = new AiVector(0f, 900f, 0f);
        var late = new Tick(100 + (int)(10f / TickSeconds));
        Assert.False(PrefireBelief.Of(a, late, TickSeconds, null, null, out _, out _, out _, out _));
        Assert.True(PrefireBelief.Of(a, late, TickSeconds, null, corner, out AiVector at, out AiVector last, out _, out float window));
        Assert.Equal((corner, corner, float.MaxValue), (at, last, window));
    }

    // ------------------------------------------------------------ the opening

    /// <summary>A sight table that sees from anywhere only the cells it is given.</summary>
    private sealed class Sees(params int[] cells) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => Array.IndexOf(cells, b) >= 0;

        public float Exposure(int cell) => 0f;
    }

    private static int CellAt(NavGraph g, float x, float y) => g.FindCell(new AiVector(x, y, 1f)).Index;

    [Fact]
    public void TheOpeningIsTheNearestSeenCellAlongTheGraphFromTheBelief()
    {
        NavGraph g = TFField.Floor(16);
        // Seen: the cell two east of the belief and the one six east; the nearer along the graph wins.
        int near = CellAt(g, 32f + 64f * 7, 32f), far = CellAt(g, 32f + 64f * 11, 32f);
        var opening = new PrefireOpening(g.CellCount);
        AiVector found = opening.Find(g, new Sees(far, near), new AiVector(32f, 32f, 0f), new AiVector(32f + 64f * 5, 32f, 0f));
        Assert.Equal(g.Cell(near).Centre, found);
    }

    [Fact]
    public void WithNoSightTableOrNoCellTheOpeningIsTheBeliefItself()
    {
        NavGraph g = TFField.Floor(16);
        var opening = new PrefireOpening(g.CellCount);
        var believed = new AiVector(32f + 64f * 5, 32f, 0f);
        Assert.Equal(believed, opening.Find(g, null, default, believed));
        Assert.Equal(believed, opening.Find(null, new Sees(0), default, believed));
        Assert.Equal(new AiVector(9000f, 0f, 0f), opening.Find(g, new Sees(0), default, new AiVector(9000f, 0f, 0f)));
    }

    [Fact]
    public void AnOpeningBeyondItsReachAlongTheGraphIsNotFound()
    {
        // A strip longer than the search's cells: a seen cell past them is not found, one within is.
        NavGraph g = TFField.Floor(PrefireOpening.Cells / 8 + 16);
        int beyond = CellAt(g, 32f + 64f * (PrefireOpening.Cells / 8 + 12), 32f), within = CellAt(g, 32f + 64f * 40, 32f);
        var opening = new PrefireOpening(g.CellCount);
        var believed = new AiVector(32f, 32f, 0f);
        Assert.Equal(believed, opening.Find(g, new Sees(beyond), default, believed));
        Assert.Equal(g.Cell(within).Centre, opening.Find(g, new Sees(within), default, believed));
    }

    [Fact]
    public void SearchesOneAfterAnotherDoNotSeeEachOthersMarks()
    {
        NavGraph g = TFField.Floor(16);
        int seen = CellAt(g, 32f + 64f * 9, 32f);
        var opening = new PrefireOpening(g.CellCount);
        var believed = new AiVector(32f + 64f * 5, 32f, 0f);
        for (int k = 0; k < 3; k++)
            Assert.Equal(g.Cell(seen).Centre, opening.Find(g, new Sees(seen), default, believed));
    }

    // ------------------------------------------------------------ the run

    private static readonly AiVector Spot = new(400f, 0f, 60f), Fallback = new(300f, 100f, 60f);

    private static (PrefireRun Run, LineAsks Lines) Aiming(ulong seed = 1)
    {
        var lines = new LineAsks();
        var run = new PrefireRun(new AiRandom(seed), lines);
        Assert.True(run.Gate(true, new Tick(10), TickSeconds));
        Assert.True(run.Draw(new Tick(10), TickSeconds, 1f, Spot, Fallback));
        return (run, lines);
    }

    [Fact]
    public void AChanceTakenAimsAndWaitsForTheNext()
    {
        (PrefireRun run, _) = Aiming();
        Assert.True(run.Aiming);
        Assert.Equal(10 + (int)(PrefireRun.CheckSeconds / TickSeconds), run.Next.Value);
    }

    [Fact]
    public void AChanceOfNothingIsNeverTaken()
    {
        var run = new PrefireRun(new AiRandom(3), new LineAsks());
        for (int k = 0; k < 200; k++)
            Assert.False(run.Draw(new Tick(10 + k), TickSeconds, 0f, Spot, Fallback));
        Assert.False(run.Aiming);
    }

    [Fact]
    public void AChanceIsTakenAtAboutItsRate()
    {
        var run = new PrefireRun(new AiRandom(5), new LineAsks());
        int taken = 0;
        for (int k = 0; k < 2000; k++)
            taken += run.Draw(new Tick(k), TickSeconds, 0.3f, Spot, Fallback) ? 1 : 0;
        Assert.InRange(taken, 500, 700);
    }

    [Fact]
    public void ItAsksOfTheLineAndWaitsForTheAnswer()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        Assert.Equal(PrefireStep.Aiming, run.Line((0, default, false), new Tick(11), default, true, 10));
        Assert.Equal((1, Spot), (lines.Ask, lines.At));
        Assert.Equal(PrefireStep.Aiming, run.Line((0, default, false), new Tick(12), default, true, 10));
        Assert.Equal(1, lines.Ask);
    }

    [Fact]
    public void AClearAnswerFiresForTheBurst()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        Assert.Equal(PrefireStep.Firing, run.Line(Answer(lines, true), new Tick(12), default, true, 10));
        Assert.Equal((1, 22), (run.Begun, run.Until.Value));
        Assert.True(run.Gate(true, new Tick(21), TickSeconds));
        Assert.False(run.Gate(true, new Tick(22), TickSeconds));
        Assert.Equal((false, 22 + (int)(PrefireRun.CheckSeconds / TickSeconds)), (run.Aiming, run.Next.Value));
    }

    [Fact]
    public void AClearAnswerAboutAnotherPointIsNotClear()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        Assert.NotEqual(PrefireStep.Firing, run.Line(Answer(lines, true, at: Spot + new AiVector(2f, 0f, 0f)), new Tick(12), default, true, 10));
    }

    [Fact]
    public void AClearAnswerToALineAskedFromElsewhereIsNotClear()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        var moved = new AiVector(0f, PrefireRun.LineMoved + 1f, 0f);
        Assert.NotEqual(PrefireStep.Firing, run.Line(Answer(lines, true), new Tick(12), moved, true, 10));
    }

    [Fact]
    public void ABlockedLineTriesTheFallbackOnceThenGivesUp()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        Assert.Equal(PrefireStep.Aiming, run.Line(Answer(lines, false), new Tick(12), default, true, 10));
        Assert.Equal((Fallback, Fallback, true), (run.Spot, lines.At, run.TriedFallback));
        Assert.Equal(PrefireStep.Ended, run.Line(Answer(lines, false), new Tick(13), default, true, 10));
        Assert.False(run.Aiming);
    }

    [Fact]
    public void ABlockedLineWithTheFallbackOutOfReachGivesUp()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        Assert.Equal(PrefireStep.Ended, run.Line(Answer(lines, false), new Tick(12), default, fallbackInReach: false, 10));
    }

    [Fact]
    public void ALineFoundBlockedWhileFiringEndsIt()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        run.Line(Answer(lines, true), new Tick(12), default, true, 10);
        Assert.Equal(PrefireStep.Ended, run.Line(Answer(lines, false), new Tick(13), default, true, 10));
        Assert.False(run.Firing);
    }

    [Fact]
    public void NotAllowedEndsItAtOnce()
    {
        (PrefireRun run, LineAsks lines) = Aiming();
        run.Line((0, default, false), new Tick(11), default, true, 10);
        run.Line(Answer(lines, true), new Tick(12), default, true, 10);
        Assert.False(run.Gate(false, new Tick(13), TickSeconds));
        Assert.Equal((false, false, false), (run.Aiming, run.Firing, run.LineClear));
    }
}
