using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// A held aim's error (plan_hl2mp_ai §3.4.1's drift, plan_bots_teams_tf2_part_g G.2.1): it wanders,
/// so one decide's error is close to the last one's and not a new draw; it is 2.5 times its settled
/// size at first sight and falls back; it is smaller up than across; a new target, or one back in
/// sight, starts it over; and it draws four numbers a step whatever their values.
/// </summary>
public sealed class AimWanderTests
{
    private const float Dt = 0.015f, Deg = 3f, Range = 1000f;
    private static readonly ActorId Them = new(2, 1), Other = new(4, 1);
    private static readonly AiVector From = new(0f, 0f, 0f), Point = new(Range, 0f, 50f);

    private static Tick At(int decide) => new(100 + decide * DecideSchedule.Period);

    /// <summary>The wander across over <paramref name="decides"/> decides on one target held in sight.</summary>
    private static float[] Held(ulong seed, int decides)
    {
        var w = new AimWander(new AiRandom(seed));
        var across = new float[decides];
        for (int i = 0; i < decides; i++)
        {
            w.Step(Them, sighted: true, At(i), Dt);
            across[i] = w.Across;
        }

        return across;
    }

    private static float Correlation(float[] a, float[] b)
    {
        float ma = a.Average(), mb = b.Average(), ab = 0f, aa = 0f, bb = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            ab += (a[i] - ma) * (b[i] - mb);
            aa += (a[i] - ma) * (a[i] - ma);
            bb += (b[i] - mb) * (b[i] - mb);
        }

        return ab / MathF.Sqrt(aa * bb);
    }

    [Fact]
    public void OneDecidesErrorIsCloseToTheLastOnes()
    {
        float[] x = Held(11, 4000);
        float r = Correlation(x[..^1], x[1..]);
        Assert.True(r > 0.9f, $"lag-1 correlation {r:F3}");
    }

    /// <summary>The control for the fact above: the same measure over a new draw each decide (a new target each time) finds none.</summary>
    [Fact]
    public void ANewDrawEachDecideIsNotCorrelated()
    {
        var w = new AimWander(new AiRandom(11));
        var x = new float[4000];
        for (int i = 0; i < x.Length; i++)
        {
            w.Step(i % 2 == 0 ? Them : Other, sighted: true, At(i), Dt);
            x[i] = w.Across;
        }

        Assert.InRange(Correlation(x[..^1], x[1..]), -0.1f, 0.1f);
    }

    [Fact]
    public void TheErrorIsTwoAndAHalfTimesItsSettledSizeAtFirstSightAndFallsBack()
    {
        var w = new AimWander(new AiRandom(3));
        w.Step(Them, sighted: true, At(0), Dt);
        Assert.Equal(1f + AimWander.SettleExtra, w.FirstSight(At(0), Dt), 5);
        Assert.Equal(1f + AimWander.SettleExtra / MathF.E, w.FirstSight(new Tick(At(0).Value + (int)(AimWander.SettleSeconds / Dt)), Dt), 2);
        Assert.InRange(w.FirstSight(new Tick(At(0).Value + (int)(5f / Dt)), Dt), 1f, 1.001f);
    }

    /// <summary>Over many first sights, the miss at the first decide is about 2.5 times the miss three seconds in.</summary>
    [Fact]
    public void TheFirstDecideOnANewlySeenTargetMissesByMoreThanALaterOne()
    {
        double first = 0, later = 0;
        const int Bots = 400, Decides = 34;
        for (ulong seed = 1; seed <= Bots; seed++)
        {
            var w = new AimWander(new AiRandom(seed));
            for (int i = 0; i < Decides; i++)
            {
                w.Step(Them, sighted: true, At(i), Dt);
                AiVector e = w.Error(From, Point, Deg, Range, At(i), Dt);
                if (i == 0)
                    first += Math.Abs(e.Y);
                else if (i == Decides - 1)
                    later += Math.Abs(e.Y);
            }
        }

        Assert.InRange(first / later, 2.0, 3.6);
    }

    [Fact]
    public void TheErrorUpIsASmallerShareOfTheErrorAcross()
    {
        double across = 0, up = 0;
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var w = new AimWander(new AiRandom(seed));
            w.Step(Them, sighted: true, At(0), Dt);
            AiVector e = w.Error(From, Point, Deg, Range, At(0), Dt);
            across += e.Y * e.Y;
            up += e.Z * e.Z;
        }

        Assert.InRange(Math.Sqrt(up / across), 0.54, 0.66);
    }

    /// <summary>The error lies across the line to the point and up it, by the settled error's size at the distance.</summary>
    [Fact]
    public void TheErrorIsAcrossTheLineAndUpByTheSkillsAngleAtTheDistance()
    {
        var w = new AimWander(new AiRandom(4));
        Tick later = new(At(0).Value + (int)(30f / Dt));
        w.Step(Them, sighted: true, At(0), Dt);
        AiVector e = w.Error(From, Point, Deg, Range, later, Dt);
        float size = MathF.Tan(Deg * MathF.PI / 180f) * Range;
        Assert.Equal(0f, e.X, 4);
        Assert.Equal(size * w.Across, e.Y, 2);
        Assert.Equal(size * w.Up * 0.6f, e.Z, 2);
        Assert.True(MathF.Abs(w.Across) > 0.1f && MathF.Abs(w.Up) > 0.1f, $"the control: there is an error to place ({w.Across}, {w.Up})");
    }

    [Fact]
    public void TheWanderStopsAtItsClip()
    {
        foreach (float x in Held(5, 20000))
            Assert.InRange(x, -AimWander.Clip, AimWander.Clip);
    }

    [Fact]
    public void ANewTargetOrOneBackInSightStartsOver()
    {
        var w = new AimWander(new AiRandom(2));
        Assert.True(w.Step(Them, sighted: true, At(0), Dt), "the first sight");
        Assert.False(w.Step(Them, sighted: true, At(1), Dt), "held");
        Assert.True(w.Step(Other, sighted: true, At(2), Dt), "another target");
        Assert.False(w.Step(Other, sighted: true, At(3), Dt));
        w.Lost();
        Assert.True(w.Step(Other, sighted: true, At(4), Dt), "back in sight");
        Assert.Equal(1f + AimWander.SettleExtra, w.FirstSight(At(4), Dt), 5);
    }

    /// <summary>A step on a target not in sight leaves the next one new, as a look at where it was is no aim on it.</summary>
    [Fact]
    public void AStepOutOfSightLeavesTheNextOneNew()
    {
        var w = new AimWander(new AiRandom(2));
        w.Step(Them, sighted: true, At(0), Dt);
        w.Step(Them, sighted: false, At(1), Dt);
        Assert.True(w.Step(Them, sighted: true, At(2), Dt));
    }

    [Fact]
    public void TheSameSeedWandersAlike()
        => Assert.Equal(Held(21, 500), Held(21, 500));

    /// <summary>Its stream does not depend on what it drew: four numbers a step, new target or held.</summary>
    [Fact]
    public void EachStepDrawsFourNumbers()
    {
        var used = new AiRandom(77);
        var w = new AimWander(used);
        for (int i = 0; i < 50; i++)
            w.Step(i < 25 ? Them : Other, sighted: i % 7 != 0, At(i), Dt);
        var plain = new AiRandom(77);
        for (int i = 0; i < 200; i++)
            plain.NextFloat();
        Assert.Equal(plain.NextUInt64(), used.NextUInt64());
    }
}
