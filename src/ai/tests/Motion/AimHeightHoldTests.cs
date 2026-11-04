using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// The crosshair's held height (plan_hl2mp_ai §3.4.1, ruling 2026-11-03): a world height it settles
/// on, which small rises follow and a change of stance has to be reacted to.
/// </summary>
public sealed class AimHeightHoldTests
{
    private const float Dt = 0.015f, Reaction = 0.3f, Standing = 54f, Crouched = 35f, Range = 200f;

    private static AimHeightHold Settled()
    {
        var h = new AimHeightHold(new AiRandom(5));
        h.Start(Standing);
        return h;
    }

    /// <summary>How long after the body went to <paramref name="wanted"/> the height first moves by a unit.</summary>
    private static float SecondsToMove(AimHeightHold h, float wanted)
    {
        float from = h.Height;
        for (int t = 1; t < 400; t++)
        {
            h.Step(wanted, Reaction, 0f, Range, Dt);
            if (MathF.Abs(h.Height - from) > 1f)
                return t * Dt;
        }

        return float.PositiveInfinity;
    }

    [Fact]
    public void ACrouchIsNotFollowedForAReaction()
    {
        float seconds = SecondsToMove(Settled(), Crouched);
        Assert.InRange(seconds, Reaction, Reaction + 0.1f);
    }

    [Fact]
    public void StandingUpIsNotFollowedForAReactionEither()
    {
        var h = new AimHeightHold(new AiRandom(5));
        h.Start(Crouched);
        Assert.InRange(SecondsToMove(h, Standing), Reaction, Reaction + 0.1f);
    }

    [Theory]
    [InlineData(Crouched)]
    [InlineData(Standing + 20f)]
    public void ItSettlesOnTheNewHeight(float wanted)
    {
        AimHeightHold h = Settled();
        for (int t = 0; t < 100; t++)
            h.Step(wanted, Reaction, 20f, Range, Dt);
        Assert.Equal(AimHeightPhase.Settled, h.Phase);
        Assert.Equal(wanted, h.Height, 1);
    }

    /// <summary>A ballistic move as long as Fitts' law says for that angle, not a jump to the height.</summary>
    [Fact]
    public void AReAimIsNoSnap()
    {
        AimHeightHold h = Settled();
        float degrees = MathF.Atan2(Standing - Crouched, Range) * (180f / MathF.PI);
        float seconds = HumanMotion.FittsA + HumanMotion.FittsB * MathF.Log2(1f + degrees / HumanMotion.TargetWidthDeg);
        float peak = 1.875f * (Standing - Crouched) / seconds * Dt;
        float largest = 0f, last = h.Height;
        int moving = 0;
        for (int t = 0; t < 100; t++)
        {
            h.Step(Crouched, Reaction, 0f, Range, Dt);
            float step = MathF.Abs(h.Height - last);
            largest = MathF.Max(largest, step);
            moving += step > 0.01f ? 1 : 0;
            last = h.Height;
        }

        Assert.InRange(largest, 0.5f * peak, 1.1f * peak);
        Assert.True(moving >= (int)(seconds / Dt) - 2, $"{moving} ticks of movement for a {seconds:F3} s move");
    }

    /// <summary>A flight of stairs at a run is pursued: nobody waits a reaction at each step.</summary>
    [Fact]
    public void AStairAtARunIsFollowedWithNoReaction()
    {
        AimHeightHold h = Settled();
        const float Rise = 127f;
        float wanted = Standing, worst = 0f;
        for (int t = 0; t < 100; t++)
        {
            wanted += Rise * Dt;
            h.Step(wanted, Reaction, 0f, Range, Dt);
            Assert.Equal(AimHeightPhase.Settled, h.Phase);
            worst = MathF.Max(worst, MathF.Abs(wanted - h.Height));
        }

        Assert.True(worst > 5f, $"the control: the aim lags a climbing body ({worst:F1} u)");
    }

    /// <summary>The body back before the eye acted: it asks for nothing.</summary>
    [Fact]
    public void ADipTooShortToActOnMovesNothing()
    {
        AimHeightHold h = Settled();
        for (int t = 0; t < 12; t++)
            h.Step(Crouched, Reaction, 0f, Range, Dt);
        Assert.Equal(AimHeightPhase.Reacting, h.Phase);
        Assert.Equal(Standing, h.Height);
        h.Step(Standing, Reaction, 0f, Range, Dt);
        Assert.Equal(AimHeightPhase.Settled, h.Phase);
    }

    /// <summary>A move lands past or short by a signed share of itself, by the stream: never the same every time.</summary>
    [Fact]
    public void AMoveLandsPastOrShortByTheSkillsShare()
    {
        int past = 0, shortOf = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var h = new AimHeightHold(new AiRandom(seed));
            h.Start(Standing);
            while (h.Phase != AimHeightPhase.Moving)
                h.Step(Crouched, Reaction, 40f, Range, Dt);
            // Its last step of movement lands it.
            while (h.Phase == AimHeightPhase.Moving)
                h.Step(Crouched, Reaction, 40f, Range, Dt);
            float off = h.Height - Crouched;
            Assert.InRange(MathF.Abs(off), 0f, 0.4f * (Standing - Crouched) + 0.01f);
            if (off > 0.2f)
                shortOf++;
            else if (off < -0.2f)
                past++;
        }

        Assert.True(past > 5 && shortOf > 5, $"past {past}, short {shortOf} of 40");
    }

    /// <summary>The same stream, the same heights, whatever else the bot's streams do.</summary>
    [Fact]
    public void TheSameSeedMovesAlike()
    {
        float[] Run()
        {
            var h = new AimHeightHold(new AiRandom(9));
            h.Start(Standing);
            float[] z = new float[60];
            for (int t = 0; t < z.Length; t++)
                z[t] = h.Step(Crouched, Reaction, 30f, Range, Dt);
            return z;
        }

        Assert.Equal(Run(), Run());
    }
}
