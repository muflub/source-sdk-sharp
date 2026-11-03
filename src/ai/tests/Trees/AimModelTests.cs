using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// Where a fighting bot means to look (plan_hl2mp_ai §3.4.1, §10): the error
/// drifts rather than jumps, is widest at first sight, and a moving target is
/// allowed for by Accuracy.
/// </summary>
public sealed class AimModelTests
{
    private static readonly AiVector At = new(600f, 0f, 0f);

    private static float Sideways(in IntentCommand c) => c.Look.Point.Y;

    private static Duel Fight(string preset = "normal")
    {
        var d = new Duel(Duel.Preset(preset));
        d.Step(d.Self(), [(Duel.Opponent, At)], d.SeeAt(Duel.Opponent, At));
        return d;
    }

    [Fact]
    public void TheAimDriftsItDoesNotJump()
    {
        Duel d = Fight();
        for (int i = 0; i < 40; i++)
            d.Step(d.Self(), [(Duel.Opponent, At)]);   // past the first-sight error

        float[] y = new float[400];
        for (int i = 0; i < y.Length; i++)
            y[i] = Sideways(d.Step(d.Self(), [(Duel.Opponent, At)]));
        double size = y.Average(v => Math.Abs(v));
        double step = y.Skip(1).Zip(y, (a, b) => Math.Abs(a - b)).Average();
        Assert.True(size > 1.0, $"the control: the aim has an error ({size:F2} u)");
        // A new draw at each decide steps about 1.4 times its size, about 0.8 through the smoothing alone; this about a quarter.
        Assert.True(step < 0.5 * size, $"steps of {step:F2} u for an error of {size:F2} u");
    }

    [Fact]
    public void TheAimIsWidestAtFirstSight()
    {
        Duel d = Fight();
        double first = 0.0, settled = 0.0;
        int rounds = 200;
        for (int round = 0; round < rounds; round++)
        {
            // Out of sight, then back: a new engagement.
            d.Step(d.Self(), []);
            first += Math.Abs(Sideways(d.Step(d.Self(), [(Duel.Opponent, At)])));
            IntentCommand last = default;
            for (int i = 0; i < 30; i++)
                last = d.Step(d.Self(), [(Duel.Opponent, At)]);
            settled += Math.Abs(Sideways(last));
        }

        Assert.True(first > 1.5 * settled, $"first {first / rounds:F2} u, settled {settled / rounds:F2} u");
    }

    /// <summary>
    /// The pitch 49° and 55° on flat floor the user's game showed: a bot
    /// reaching the place it chased to, looking at chest height there from a pace away.
    /// </summary>
    [Theory]
    [InlineData(100f)]
    [InlineData(16f)]
    [InlineData(4f)]
    [InlineData(0f)]
    public void AChaseArrivingLooksLevelNotAtTheFloor(float away)
    {
        var place = new AiVector(1000f, 0f, 0f);
        var d = new Duel(Duel.Preset("hard"));
        d.Step(d.Self(), [(Duel.Opponent, place)], d.SeeAt(Duel.Opponent, place));
        SelfState there = d.Self() with { Origin = place - new AiVector(away, 0f, 0f) };
        IntentCommand c = d.Step(there, []);
        Assert.Equal(Duel.Opponent, c.Look.Actor);
        float pitch = SourceSharp.Ai.Hl2mp.BotCommandApplier.AnglesTo(there.Origin + new AiVector(0f, 0f, 64f), c.Look.Point).Pitch;
        Assert.InRange(pitch, -10f, 10f);
    }

    private static double MeanAhead(string preset, AiVector velocity)
    {
        Duel d = Fight(preset);
        double sum = 0.0;
        for (int i = 0; i < 500; i++)
            sum += Sideways(d.StepMoving(d.Self(), Duel.Opponent, At, velocity));
        return sum / 500.0;
    }

    [Fact]
    public void AMovingTargetIsAimedAheadAndAccuracyAllowsForMore()
    {
        var strafe = new AiVector(0f, 190f, 0f);
        double easy = MeanAhead("easy", strafe), expert = MeanAhead("expert", strafe);
        Assert.True(easy > 3.0, $"easy aims {easy:F1} u ahead");
        Assert.True(expert > easy, $"expert {expert:F1} u, easy {easy:F1} u");
        Assert.True(MeanAhead("expert", new AiVector(0f, -190f, 0f)) < 0.0, "the other way, the other side");
    }

    /// <summary>
    /// The view lags whatever moves it, the bot's own sidestep too: at 500 u a
    /// run across costs a lagging view as much as a strafing target does.
    /// </summary>
    [Fact]
    public void ABotAllowsForItsOwnSidestep()
    {
        Duel d = Fight("expert");
        double sum = 0.0;
        for (int i = 0; i < 500; i++)
            sum += Sideways(d.StepMoving(d.Self() with { Velocity = new AiVector(0f, 190f, 0f) }, Duel.Opponent, At, AiVector.Zero));
        Assert.True(sum / 500.0 < -3.0, $"{sum / 500.0:F1} u the other way");
    }

    /// <summary>The sidestep's on and off is by the stretch: flipping at each decide jerks the body and costs the bot its own aim.</summary>
    [Fact]
    public void ASidestepHoldsForItsStretch()
    {
        Duel d = Fight("hard");
        var near = new AiVector(300f, 0f, 0f);
        int changes = 0, steps = 600;
        bool last = false;
        for (int i = 0; i < steps; i++)
        {
            bool moving = d.Step(d.Self(), [(Duel.Opponent, near)]).Move.Kind == MoveKind.Strafe;
            changes += moving != last ? 1 : 0;
            last = moving;
        }

        Assert.True(changes > 4, $"the control: it does stop and start ({changes})");
        Assert.True(changes < steps / 6, $"{changes} changes in {steps} decides");
    }

    [Fact]
    public void AStandingTargetIsNotAimedAhead()
    {
        // The wander is centred on the target: over a long fight it averages out.
        double mean = MeanAhead("normal", AiVector.Zero);
        Assert.InRange(mean, -15.0, 15.0);
    }
}
