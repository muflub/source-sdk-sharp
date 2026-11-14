using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// A glance off the path (plan_hl2mp_ai §3.4, plan_bots_teams_tf2_part_g G.2.1): begun at the
/// skill's rate a second, 50 to 100 degrees to either side of the way to the goal, at a point 256 u
/// off at the eye's height, held 0.4 s; never at a rate of zero; one draw a decide and two more
/// for a glance begun.
/// </summary>
public sealed class PathGlanceTests
{
    private const float Dt = 0.015f, Eye = 68f;
    private static readonly AiVector Self = new(100f, 200f, 10f), Goal = new(1100f, 200f, 10f);

    private static Tick At(int decide) => new(100 + decide * DecideSchedule.Period);

    /// <summary>The signed angle off the way east of each glance begun over <paramref name="decides"/> decides.</summary>
    private static List<float> Angles(PathGlance g, float rate, int decides, List<AiVector>? points = null)
    {
        var angles = new List<float>();
        int taken = 0;
        for (int i = 0; i < decides; i++)
        {
            if (g.Step(At(i), Dt, rate, Self, Goal, Eye, out AiVector at) && g.Taken > taken)
            {
                taken = g.Taken;
                points?.Add(at);
                angles.Add(MathF.Atan2(at.Y - Self.Y, at.X - Self.X) * (180f / MathF.PI));
            }
        }

        return angles;
    }

    [Fact]
    public void GlancesBeginAtAboutTheSkillsRateASecond()
    {
        const int Decides = 200_000;
        var g = new PathGlance(new AiRandom(5));
        Angles(g, 0.3f, Decides);
        float perSecond = g.Taken / (Decides * DecideSchedule.Period * Dt);
        // A little under the rate: no glance begins while one is held.
        Assert.InRange(perSecond, 0.24f, 0.30f);
    }

    [Fact]
    public void AtARateOfZeroThereIsNoGlance()
    {
        var g = new PathGlance(new AiRandom(5));
        Assert.Empty(Angles(g, 0f, 20_000));
    }

    [Fact]
    public void AGlanceTurnsFiftyToAHundredDegreesToEitherSideOfTheWay()
    {
        List<float> angles = Angles(new PathGlance(new AiRandom(8)), 2f, 20_000);
        Assert.True(angles.Count > 500, $"the control: {angles.Count} glances");
        Assert.All(angles, a => Assert.InRange(MathF.Abs(a), 50f - 0.01f, 100f + 0.01f));
        Assert.Contains(angles, a => a > 0f);
        Assert.Contains(angles, a => a < 0f);
        Assert.True(angles.Max(MathF.Abs) > 95f && angles.Min(MathF.Abs) < 55f, "the whole span is used");
    }

    [Fact]
    public void AGlanceLooksAtAPointOffToTheSideAtTheEyesHeight()
    {
        var points = new List<AiVector>();
        Angles(new PathGlance(new AiRandom(8)), 2f, 200, points);
        Assert.NotEmpty(points);
        Assert.All(points, p =>
        {
            Assert.Equal(Self.Z + Eye, p.Z, 3);
            Assert.Equal(PathGlance.Reach, new AiVector(p.X - Self.X, p.Y - Self.Y, 0f).Length, 2);
        });
    }

    /// <summary>0.4 s is 26 ticks: the decide it begins and the four after look at the same point, the sixth is free again.</summary>
    [Fact]
    public void AGlanceIsHeldForItsMoment()
    {
        var g = new PathGlance(new AiRandom(8));
        int i = 0;
        AiVector first;
        while (!g.Step(At(i), Dt, 2f, Self, Goal, Eye, out first))
            i++;
        for (int k = 1; k <= 4; k++)
        {
            Assert.True(g.Step(At(i + k), Dt, 0f, Self, Goal, Eye, out AiVector held));
            Assert.Equal(first, held);
        }

        Assert.False(g.Step(At(i + 5), Dt, 0f, Self, Goal, Eye, out _));
        Assert.Equal(1, g.Taken);
    }

    [Fact]
    public void ADecideDrawsOnceAndAGlanceBegunTwiceMore()
    {
        var used = new AiRandom(31);
        var g = new PathGlance(used);
        int draws = 0;
        for (int i = 0; i < 400; i++)
        {
            int before = g.Taken;
            bool glancing = g.Step(At(i), Dt, 1f, Self, Goal, Eye, out _);
            draws += g.Taken > before ? 3 : glancing ? 0 : 1;
        }

        Assert.True(g.Taken > 5, $"the control: {g.Taken} glances");
        var plain = new AiRandom(31);
        for (int i = 0; i < draws; i++)
            plain.NextFloat();
        Assert.Equal(plain.NextUInt64(), used.NextUInt64());
    }
}
