using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// Where a fighting bot keeps its crosshair (plan_hl2mp_ai §3.4.1, ruling 2026-11-03: "Bots default
/// aim should be around shoulders thus crouching requires re aiming"), through the decider and
/// the view that follows it.
/// </summary>
public sealed class AimPointTests
{
    private static readonly AiVector Near = new(250f, 0f, 0f);

    private static (Duel Duel, float Z) Settled(string preset = "expert", AiVector? at = null, ActorActivity activity = ActorActivity.None)
    {
        AiVector where = at ?? Near;
        var d = new Duel(Duel.Preset(preset));
        IntentCommand last = default;
        for (int i = 0; i < 40; i++)
            last = d.StepSeeing(d.Self(), Duel.Opponent, where, AiVector.Zero, activity, i == 0 ? d.SeeAt(Duel.Opponent, where) : []);
        return (d, last.Look.Point.Z);
    }

    /// <summary>The decides until the aim's height has gone half way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static int DecidesToFollow(Duel d, ActorActivity activity, float from, float to)
    {
        for (int i = 1; i < 60; i++)
        {
            float z = d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, activity).Look.Point.Z;
            if (MathF.Abs(z - to) < MathF.Abs(z - from))
                return i;
        }

        return int.MaxValue;
    }

    private static float Seconds(int decides) => decides * DecideSchedule.Period * 0.015f;

    [Fact]
    public void TheAimSettlesAtShoulderHeight()
    {
        (Duel d, _) = Settled("hard");
        double sum = 0.0;
        IntentCommand c = default;
        for (int i = 0; i < 200; i++)
        {
            c = d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, ActorActivity.None);
            sum += c.Look.Point.Z;
        }

        Assert.Equal(AimPoint.Shoulder, c.Look.Aim);
        // The models' own number, not the constant's: a chest at 48 is not shoulders.
        Assert.InRange(sum / 200.0, 50.5, 55.0);
    }

    /// <summary>Hard may rise a little, by where it aims and not by how well (user 2026-11-03); Medium stays on the point.</summary>
    [Fact]
    public void AccuracyRaisesThePointNotAtMedium()
    {
        var c = SourceSharp.Ai.Tests.Skills.Curves.Game();
        Assert.Equal(0f, c.For(Duel.Preset("normal")).AimRise);
        Assert.True(c.For(Duel.Preset("hard")).AimRise > 0f);
        Assert.InRange(c.For(Duel.Preset("expert")).AimRise, 0f, 3f);
        double Mean(string preset)
        {
            (Duel d, _) = Settled(preset);
            double sum = 0.0;
            for (int i = 0; i < 200; i++)
                sum += d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, ActorActivity.None).Look.Point.Z;
            return sum / 200.0;
        }

        Assert.True(Mean("hard") > Mean("normal") + 1.0, $"hard {Mean("hard"):F1}, normal {Mean("normal"):F1}");
    }

    /// <summary>The numbers of the game's models (<c>cs_sv_model_heights</c>): shoulders over a chest, a head over those, in both stances.</summary>
    [Fact]
    public void TheHeadStaysAboveTheShouldersAboveTheChestInBothStances()
    {
        foreach (bool ducked in new[] { false, true })
        {
            Assert.True(Hl2mpAimHeights.Of(AimPoint.Head).At(ducked) > Hl2mpAimHeights.Of(AimPoint.Shoulder).At(ducked));
            Assert.True(Hl2mpAimHeights.Of(AimPoint.Shoulder).At(ducked) > Hl2mpAimHeights.Of(AimPoint.Body).At(ducked));
        }

        Assert.InRange(Hl2mpAimHeights.Shoulder.Standing, 50f, 57f);
        Assert.InRange(Hl2mpAimHeights.Shoulder.Crouched, 32f, 37f);
    }

    [Fact]
    public void ACrouchIsBelowTheAimAndTheCrosshairTakesAReactionToFollow()
    {
        (Duel d, _) = Settled();
        float seconds = Seconds(DecidesToFollow(d, ActorActivity.Crouched, Hl2mpAimHeights.Shoulder.Standing, Hl2mpAimHeights.Shoulder.Crouched));
        float reaction = d.Params.RecognizeSeconds + d.Params.ReactionMs / 1000f;
        Assert.InRange(seconds, MathF.Max(Hl2mpDecider.ReAimLeastSeconds, reaction), reaction + 0.5f);
    }

    [Fact]
    public void StandingUpIsTheSameInReverse()
    {
        (Duel d, float low) = Settled(activity: ActorActivity.Crouched);
        Assert.InRange(low, Hl2mpAimHeights.Shoulder.Crouched - 3f, Hl2mpAimHeights.Shoulder.Crouched + 3f);
        float seconds = Seconds(DecidesToFollow(d, ActorActivity.None, Hl2mpAimHeights.Shoulder.Crouched, Hl2mpAimHeights.Shoulder.Standing));
        float reaction = d.Params.RecognizeSeconds + d.Params.ReactionMs / 1000f;
        Assert.InRange(seconds, MathF.Max(Hl2mpDecider.ReAimLeastSeconds, reaction), reaction + 0.5f);
    }

    /// <summary>The re-aim's wait is the bot's own: seeing the change, then reacting, by its Alertness and Accuracy.</summary>
    [Fact]
    public void AnEasyBotFollowsACrouchLaterThanAHardOne()
    {
        float Waits(string preset)
        {
            (Duel d, _) = Settled(preset);
            int decides = 0;
            do
            {
                d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, ActorActivity.Crouched);
                decides++;
            }
            while (d.Decider.AimHeightNow == AimHeightPhase.Reacting && decides < 60);
            return Seconds(decides);
        }

        float easy = Waits("easy"), hard = Waits("hard"), expert = Waits("expert");
        Assert.True(easy > hard + 0.2f, $"easy {easy:F2} s, hard {hard:F2} s");
        Assert.True(hard >= expert, $"hard {hard:F2} s, expert {expert:F2} s");
    }

    /// <summary>A new engagement starts at the default height: someone found crouched costs a re-aim too.</summary>
    [Fact]
    public void SomeoneCrouchedAtFirstSightIsAimedAboveThenReAimedAt()
    {
        var d = new Duel(Duel.Preset("expert"));
        float first = d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, ActorActivity.Crouched, d.SeeAt(Duel.Opponent, Near)).Look.Point.Z;
        Assert.InRange(first, Hl2mpAimHeights.ShoulderStanding - 6f, Hl2mpAimHeights.ShoulderStanding + 6f);
        float last = first;
        for (int i = 0; i < 30; i++)
            last = d.StepSeeing(d.Self(), Duel.Opponent, Near, AiVector.Zero, ActorActivity.Crouched).Look.Point.Z;
        Assert.InRange(last, Hl2mpAimHeights.Shoulder.Crouched - 3f, Hl2mpAimHeights.Shoulder.Crouched + 3f);
    }

    /// <summary>The body's jump is not led either: it is a thing to react to, not a line to extrapolate.</summary>
    [Fact]
    public void ABodyRisingIsNotLedInHeight()
    {
        (Duel d, float z) = Settled();
        float next = d.StepSeeing(d.Self(), Duel.Opponent, Near, new AiVector(0f, 0f, 268f), ActorActivity.None).Look.Point.Z;
        Assert.InRange(next, z - 3f, z + 3f);
    }

    /// <summary>What a person's turn allows holds through the re-aim, whatever a close target asks of the view.</summary>
    [Fact]
    public void TurnLimitsHoldThroughAReAim()
    {
        var at = new AiVector(30f, 0f, 0f);
        (Duel d, _) = Settled(at: at);
        var eye = new AiVector(0f, 0f, 64f);
        var motion = new HumanMotion(new MotionParams(100f, MotionEnvelope.MinPeakDegPerSec, 0f, MotionEnvelope.MinTrackingLagMs), new AiRandom(3));
        IntentCommand c = d.StepSeeing(d.Self(), Duel.Opponent, at, AiVector.Zero, ActorActivity.None);
        motion.Reset(BotCommandApplier.AnglesTo(eye, c.Look.Point));
        float largest = 0f, start = motion.View.Pitch;
        for (int decide = 0; decide < 30; decide++)
        {
            c = d.StepSeeing(d.Self(), Duel.Opponent, at, AiVector.Zero, ActorActivity.Crouched);
            AiAngles target = BotCommandApplier.AnglesTo(eye, c.Look.Point);
            for (int t = 0; t < DecideSchedule.Period; t++)
            {
                float was = motion.View.Pitch;
                motion.Step(target, 0, 0.015f);
                largest = MathF.Max(largest, MathF.Abs(motion.View.Pitch - was));
            }
        }

        Assert.True(motion.View.Pitch - start > 5f, $"the control: the view went down ({start:F1} to {motion.View.Pitch:F1})");
        Assert.True(largest <= motion.MaxStep(0.015f) + 1e-3f, $"{largest:F2} deg in a tick, the most is {motion.MaxStep(0.015f):F2}");
    }

    /// <summary>A blast goes low and a swing at the chest; only the guns keep to the shoulders.</summary>
    [Theory]
    [InlineData("weapon_rpg", AimPoint.Body)]
    [InlineData("weapon_frag", AimPoint.Body)]
    [InlineData("weapon_crowbar", AimPoint.Body)]
    [InlineData("weapon_smg1", AimPoint.Shoulder)]
    [InlineData("weapon_crossbow", AimPoint.Shoulder)]
    public void EachWeaponAimsWhereItShould(string weapon, AimPoint point)
        => Assert.Equal(point, Hl2mpWeaponRanges.For(weapon).Aim);
}
