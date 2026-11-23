using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// The fight's motion lifted to the core (lane Gfight): the strafe's stretches (<see cref="StrafeRhythm"/>),
/// the crouch's (<see cref="CrouchRhythm"/>) and the place out of sight (<see cref="CoverSearch"/>).
/// </summary>
public sealed class FightMotionCoreTests
{
    private const float Tick = 0.015f;

    /// <summary>The stretch holds its side until it ends: one draw a stretch, not a decide.</summary>
    [Fact]
    public void AStretchHoldsItsSideUntilItEnds()
    {
        var r = new StrafeRhythm(new AiRandom(3));
        int side = r.Step(new Tick(100), Tick, 1f, 1f, StrafeStyle.Coin);
        Tick until = r.Until;
        for (int t = 100; t < until.Value; t += 4)
            Assert.Equal(side, r.Step(new Tick(t), Tick, 1f, 1f, StrafeStyle.Coin));
        Assert.Equal(1, r.Stretches);
    }

    /// <summary>A weight of 0 steps no stretch, a weight of 1 every one.</summary>
    [Fact]
    public void TheWeightIsTheShareOfStretchesThatStep()
    {
        var never = new StrafeRhythm(new AiRandom(5));
        var always = new StrafeRhythm(new AiRandom(5));
        for (int t = 0; t < 20_000; t += 4)
        {
            Assert.Equal(0, never.Step(new Tick(t), Tick, 0f, 0.5f, StrafeStyle.Coin));
            Assert.NotEqual(0, always.Step(new Tick(t), Tick, 1f, 0.5f, StrafeStyle.Coin));
        }
    }

    /// <summary>
    /// The coin draws the side, the step and the wait from the stream in that order, as the HL2MP decider
    /// drew them itself: the same stream gives the same stretches.
    /// </summary>
    [Fact]
    public void TheCoinDrawsAsTheDeciderDid()
    {
        var mine = new AiRandom(11);
        var theirs = new AiRandom(11);
        var r = new StrafeRhythm(mine);
        for (int i = 0; i < 50; i++)
        {
            int at = r.Until.Value;
            int side = r.Step(new Tick(at), Tick, 0.4f, 0.9f, StrafeStyle.Coin);
            int sign = theirs.NextFloat() < 0.5f ? -1 : 1;
            bool strafing = theirs.NextFloat() < 0.4f;
            float u = Math.Max(theirs.NextFloat(), 1e-4f);
            int until = at + Math.Max(4, (int)(-MathF.Log(u) * 0.9f / Tick));
            Assert.Equal((strafing ? sign : 0, until), (side, r.Until.Value));
        }
    }

    /// <summary>A person's style holds a side at least its floor, and its mean stays the curve's.</summary>
    [Fact]
    public void APersonsStretchesHoldTheirFloorAndKeepTheMean()
    {
        var r = new StrafeRhythm(new AiRandom(17));
        var style = new StrafeStyle(0.3f, 0.8f);
        var waits = new List<int>();
        int at = 0;
        for (int i = 0; i < 4000; i++)
        {
            r.Step(new Tick(at), Tick, 1f, 0.9f, style);
            waits.Add(r.Until.Value - at);
            at = r.Until.Value;
        }

        Assert.True(waits.Min() >= (int)(0.3f / Tick), $"shortest {waits.Min()} ticks");
        Assert.InRange(waits.Average() * Tick, 0.85, 0.95);
    }

    /// <summary>A person's keys mostly go the other way when a stretch ends: about the reverse chance of the changes.</summary>
    [Fact]
    public void APersonsStretchesMostlyReverse()
    {
        var r = new StrafeRhythm(new AiRandom(19));
        var style = new StrafeStyle(0.25f, 0.8f);
        for (int i = 0; i < 4000; i++)
            r.Step(r.Until, Tick, 1f, 0.6f, style);
        Assert.InRange(r.Changes / (double)r.Stretches, 0.77, 0.83);
    }

    /// <summary>The coin's sides change about half the time, which is why a person's style reverses.</summary>
    [Fact]
    public void TheCoinChangesSideHalfTheTime()
    {
        var r = new StrafeRhythm(new AiRandom(19));
        for (int i = 0; i < 4000; i++)
            r.Step(r.Until, Tick, 1f, 0.6f, StrafeStyle.Coin);
        Assert.InRange(r.Changes / (double)r.Stretches, 0.47, 0.53);
    }

    /// <summary>A crouch is held at least its least hold, and over a long fit about the share asked for.</summary>
    [Fact]
    public void ACrouchKeepsItsShareAndItsLeastHold()
    {
        var c = new CrouchRhythm(new AiRandom(23));
        int crouched = 0, run = 0, shortest = int.MaxValue, ticks = 200_000;
        for (int t = 0; t < ticks; t++)
        {
            if (c.Step(new Tick(t), Tick, true, 0.2f))
            {
                crouched++;
                run++;
            }
            else if (run > 0)
            {
                shortest = Math.Min(shortest, run);
                run = 0;
            }
        }

        Assert.InRange(crouched / (double)ticks, 0.15, 0.25);
        Assert.True(shortest >= (int)(CrouchRhythm.MinHoldSeconds / Tick) - 1, $"shortest hold {shortest} ticks");
    }

    /// <summary>What ends the fit ends the crouch, and the next waits at least the least gap.</summary>
    [Fact]
    public void AnEndedFitEndsTheCrouchAndTheNextWaitsItsGap()
    {
        var c = new CrouchRhythm(new AiRandom(29));
        int t = 0;
        while (!c.Step(new Tick(t), Tick, true, 0.9f))
            t++;
        Assert.False(c.Step(new Tick(++t), Tick, false, 0.9f));
        int ended = t;
        while (!c.Step(new Tick(++t), Tick, true, 0.9f))
        {
        }

        Assert.True(t - ended >= (int)(CrouchRhythm.MinGapSeconds / Tick), $"crouched again {t - ended} ticks after");
    }

    /// <summary>A share of 0 never crouches.</summary>
    [Fact]
    public void AShareOfNothingNeverCrouches()
    {
        var c = new CrouchRhythm(new AiRandom(31));
        for (int t = 0; t < 20_000; t++)
            Assert.False(c.Step(new Tick(t), Tick, true, 0f));
    }

    /// <summary>A line of cells seen from its west end but for its east end: the nearest unseen cell in reach, none past reach, none when unseen already.</summary>
    [Fact]
    public void CoverIsTheNearestCellTheThreatDoesNotSee()
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[10];
        for (int x = 0; x < 10; x++)
            ids[x] = b.AddCell(new NavCell(x * 64f, 0f, (x + 1) * 64f, 64f, 0f, ClearanceClasses.Stand, NavCellFlags.None, 1f));
        for (int x = 0; x + 1 < 10; x++)
            b.AddBoth(ids[x], ids[x + 1], NavEdgeType.Walk);
        NavGraph graph = b.Build();
        var sight = new Seen(c => c < 7);
        var search = new CoverSearch(graph.CellCount);

        Assert.True(search.Find(graph, sight, new AiVector(160f, 32f, 0f), new AiVector(32f, 32f, 0f), 512f, out AiVector at));
        Assert.Equal(7 * 64f + 32f, at.X);
        Assert.False(search.Find(graph, sight, new AiVector(160f, 32f, 0f), new AiVector(32f, 32f, 0f), 256f, out _));
        Assert.False(search.Find(graph, sight, new AiVector(500f, 32f, 0f), new AiVector(32f, 32f, 0f), 512f, out _));
        Assert.Equal(1, search.Found);
    }

    private sealed class Seen(Func<int, bool> seen) : ICellSight
    {
        public float Range => 4096f;

        public bool Visible(int a, int b) => seen(b);

        public float Exposure(int cell) => seen(cell) ? 1f : 0f;
    }
}
