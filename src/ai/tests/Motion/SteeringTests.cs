using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// Steering (plan_hl2mp_ai §9 Phase 5 step 4, §8.2): separation keeps
/// bots apart, the fight's strafe is not periodic, and glances follow
/// Alertness.
/// </summary>
public sealed class SteeringTests
{
    /// <summary>
    /// Four bots crossing a flat 320 u square through its middle at a run
    /// keep 40 u between any two with separation; without it they meet (the
    /// control: the stimulus reaches the subject).
    /// </summary>
    [Fact]
    public void SeparationKeepsFourCrossingBotsFortyUnitsApart()
    {
        Assert.True(Closest(separate: true) >= 40f, $"closest {Closest(true):F1} u with separation");
        Assert.True(Closest(separate: false) < 20f, $"the control: closest {Closest(false):F1} u without it");
    }

    private static float Closest(bool separate)
    {
        AiVector[] at = [new(0, 0, 0), new(320, 320, 0), new(320, 0, 0), new(0, 320, 0)];
        AiVector[] goal = [new(320, 320, 0), new(0, 0, 0), new(0, 320, 0), new(320, 0, 0)];
        float closest = float.PositiveInfinity;
        var others = new AiVector[3];
        for (int t = 0; t < 200; t++)
        {
            var next = new AiVector[4];
            for (int i = 0; i < 4; i++)
            {
                int n = 0;
                for (int j = 0; j < 4; j++)
                    if (j != i) others[n++] = at[j];
                AiVector way = (goal[i] - at[i]).Length < 8f ? default : (goal[i] - at[i]).Normalised();
                if (separate && way.LengthSquared > 0f)
                    way = Steering.Bend(way, Steering.Separation(at[i], others));
                next[i] = at[i] + way * (190f * 0.015f);
            }

            at = next;
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    closest = MathF.Min(closest, at[i].DistanceTo(at[j]));
        }

        return closest;
    }

    /// <summary>
    /// Two coming straight at each other each turn to their own right, so they
    /// pass: the push alone only slowed them to a stand, with nothing to choose a
    /// side (user report 2026-11-02: "head on").
    /// </summary>
    [Fact]
    public void TwoComingStraightAtEachOtherEachKeepToTheirRight()
    {
        AiVector east = new(1f, 0f, 0f);
        AiVector bent = Steering.Bend(east, Steering.Separation(new AiVector(0f, 0f, 0f), [new AiVector(40f, 0f, 0f)]));
        Assert.True(bent.Y < -0.2f, $"going east with someone ahead it bends south: {bent}");
        AiVector back = Steering.Bend(-east, Steering.Separation(new AiVector(40f, 0f, 0f), [new AiVector(0f, 0f, 0f)]));
        Assert.True(back.Y > 0.2f, $"going west it bends north: {back}");
    }

    /// <summary>A push from beside only bends the way, as before: nothing to the right of it.</summary>
    [Fact]
    public void APushFromBesideDoesNotTurnRight()
    {
        AiVector bent = Steering.Bend(new AiVector(1f, 0f, 0f), new AiVector(0f, 0.5f, 0f));
        Assert.True(bent.Y > 0f, $"pushed north it bends north: {bent}");
    }

    /// <summary>§8.2: the strafe's turns are not periodic: the intervals spread (CV &gt; 0.4) and do not follow one another (lag-1 autocorrelation under 0.3).</summary>
    [Fact]
    public void TheFightsStrafeIsNotPeriodic()
    {
        SkillProfile p = Duel.Preset("normal");
        var d = new Duel(p.With(p.IndexOf("agility"), 10));
        var at = new AiVector(380f, 0f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, at)], d.SeeAt(Duel.Opponent, at));
        var turns = new List<int>();
        float last = 0f;
        for (int i = 0; i < 3000; i++)
        {
            IntentCommand c = d.Step(d.Self(), [(Duel.Opponent, at)]);
            float side = c.Move.Kind == MoveKind.Strafe ? MathF.Sign(c.Move.Direction.Y) : 0f;
            if (side != 0f && side != last)
            {
                turns.Add(d.Tick);
                last = side;
            }
        }

        double[] gaps = [.. turns.Zip(turns.Skip(1), (a, b) => (double)(b - a))];
        Assert.True(gaps.Length > 40, $"{gaps.Length} turns");
        double mean = gaps.Average(), sd = Math.Sqrt(gaps.Select(g => (g - mean) * (g - mean)).Average());
        double r = gaps.Zip(gaps.Skip(1), (a, b) => (a - mean) * (b - mean)).Average() / (sd * sd);
        Assert.True(sd / mean > 0.4, $"coefficient of variation {sd / mean:F2}");
        Assert.True(Math.Abs(r) < 0.3, $"lag-1 autocorrelation {r:F2}");
    }

    private static int Glances(int alertness)
    {
        SkillProfile p = Duel.Preset("normal");
        var d = new Duel(p.With(p.IndexOf("alertness"), alertness),
                         new ItemSpawn(0, "item_battery", ItemKind.Armour, new AiVector(3000, 0, 0), 30f),
                         new ItemSpawn(1, "item_battery", ItemKind.Armour, new AiVector(-3000, 0, 0), 30f));
        for (int i = 0; i < 3000; i++)
            d.Step(d.Self(), []);
        return d.Decider.Glances;
    }

    [Fact]
    public void ABotGlancesOffItsPathMoreTheMoreAlertItIs()
    {
        int low = Glances(0), high = Glances(10);
        Assert.True(low > 0 && high > 2 * low, $"glances over 3,000 decides: {low} at Alertness 0, {high} at 10");
    }
}
