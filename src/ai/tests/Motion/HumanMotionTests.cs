using Xunit;

namespace SourceSharp.Ai.Tests.Motion;

/// <summary>
/// The human motion model's first skeleton (plan_hl2mp_ai §3.3, §3.4.1, Phase
/// 3 step 4): the reaction delay, the envelope and its clamp, the flick with
/// its overshoot and correction, and the pursuit lag.
/// </summary>
public class HumanMotionTests
{
    private const float Dt = 0.015f;

    private static HumanMotion Motion(MotionParams p, ulong seed = 7)
    {
        var m = new HumanMotion(p, new AiRandom(seed));
        m.Reset(new AiAngles(0f, 0f, 0f));
        return m;
    }

    private static MotionParams Preset(string name)
    {
        Assert.True(SkillPresets.TryGet(name, out SkillProfile? p));
        return MotionCurves.For(p!, SourceSharp.Ai.Tests.Skills.Curves.Game().For(p!));
    }

    /// <summary>The per-tick yaw turns while the view goes to a point 180° away, for 3 s.</summary>
    private static List<float> YawSteps(HumanMotion m, int ticks = 200)
    {
        var steps = new List<float>();
        var target = new AiAngles(0f, 180f, 0f);
        float last = m.View.Yaw;
        for (int i = 0; i < ticks; i++)
        {
            m.Step(target, 1, Dt);
            steps.Add(MathF.Abs(HumanMotion.AngleDelta(last, m.View.Yaw)));
            last = m.View.Yaw;
        }

        return steps;
    }

    /// <summary>Seconds until the view is within 2° of a point 180° away and stays there.</summary>
    private static float SettleSeconds(MotionParams p, ulong seed = 7)
    {
        HumanMotion m = Motion(p, seed);
        var target = new AiAngles(0f, 180f, 0f);
        int lastOutside = -1;
        for (int i = 0; i < 400; i++)
        {
            m.Step(target, 1, Dt);
            if (MathF.Abs(HumanMotion.AngleDelta(m.View.Yaw, 180f)) > 2f)
                lastOutside = i;
        }

        return (lastOutside + 1) * Dt;
    }

    [Fact]
    public void NoTickTurnsFasterThanThePeak()
    {
        foreach (string preset in SkillPresets.Names)
        {
            MotionParams p = Preset(preset);
            List<float> steps = YawSteps(Motion(p));

            // The stimulus reached the subject: the view turned all the way.
            Assert.True(steps.Sum() >= 179f, $"{preset} turned {steps.Sum()}°");
            Assert.All(steps, s => Assert.True(s <= p.TurnPeakDegPerSec * Dt + 1e-3f,
                                               $"{preset}: {s}° in a tick, cap {p.TurnPeakDegPerSec * Dt}°"));
        }
    }

    [Fact]
    public void ACurveOutsideTheEnvelopeIsClamped()
    {
        var wild = new MotionParams(5f, 5000f, 300f, 0f);
        HumanMotion m = Motion(wild);

        Assert.Equal(MotionEnvelope.MaxPeakDegPerSec, m.Parameters.TurnPeakDegPerSec);
        Assert.Equal(MotionEnvelope.MinReactionMs, m.Parameters.ReactionMs);
        Assert.All(YawSteps(m), s => Assert.True(s <= MotionEnvelope.MaxPeakDegPerSec * Dt + 1e-3f, $"{s}° in a tick"));
    }

    [Fact]
    public void ACurveBelowTheEnvelopeIsRaisedToIt()
        => Assert.Equal(MotionEnvelope.MinPeakDegPerSec, Motion(new MotionParams(200f, 10f, 5f, 100f)).Parameters.TurnPeakDegPerSec);

    [Fact]
    public void ThePresetsSpanTheEnvelope()
    {
        Assert.Equal(MotionEnvelope.MinPeakDegPerSec, Preset("easy").TurnPeakDegPerSec);
        Assert.Equal(MotionEnvelope.MaxPeakDegPerSec, Preset("expert").TurnPeakDegPerSec);
    }

    [Fact]
    public void TheSettleTimeRisesAsSkillFalls()
    {
        float[] settle = new[] { "expert", "hard", "normal", "easy" }.Select(n => SettleSeconds(Preset(n))).ToArray();

        for (int i = 1; i < settle.Length; i++)
            Assert.True(settle[i] > settle[i - 1], $"settle times {string.Join(", ", settle)}");
    }

    [Fact]
    public void AimDoesNotMoveBeforeTheReactionDelay()
    {
        MotionParams p = Preset("normal");
        HumanMotion m = Motion(p);
        int still = (int)(p.ReactionMs / 1000f / Dt);

        for (int i = 0; i < still; i++)
            Assert.Equal(0f, m.Step(new AiAngles(0f, 90f, 0f), 1, Dt).Yaw);

        // And then it moves.
        for (int i = 0; i < 4; i++)
            m.Step(new AiAngles(0f, 90f, 0f), 1, Dt);
        Assert.NotEqual(0f, m.View.Yaw);
    }

    /// <summary>
    /// A person's reaction after noticing (§10): recognition already holds
    /// stock's 1.0 / 0.5 / 0.3 / 0.2 s, so the reaction on top of it is short,
    /// and falls with Alertness. It used to repeat those same numbers: a Medium
    /// bot took a full second before its view moved at all.
    /// </summary>
    [Fact]
    public void TheReactionAfterRecognitionIsAPersonsAndFallsWithAlertness()
    {
        float[] reaction = [.. new[] { "easy", "normal", "hard", "expert" }.Select(n => Preset(n).ReactionMs)];
        for (int i = 1; i < reaction.Length; i++)
            Assert.True(reaction[i] <= reaction[i - 1], string.Join(", ", reaction));
        Assert.True(reaction[0] > reaction[^1], string.Join(", ", reaction));
        Assert.All(reaction, r => Assert.InRange(r, MotionEnvelope.MinReactionMs, 300f));
    }

    /// <summary>The tuned recognition times (§10, §12.3): half stock's, since a reaction, the decide and a flick follow them.</summary>
    [Fact]
    public void RecognitionIsTheTunedTimesAndFallsWithAlertness()
    {
        Assert.Equal([0.5f, 0.25f, 0.12f, 0.06f], new[] { 2, 4, 6, 7 }.Select(l =>
            VisionCurves.For(new SkillProfile("x", StandardSkills.Ten, [0, l, 0, 0, 0, 0, 0, 0, 0, 0])).RecognizeSeconds));
        float[] all = [.. Enumerable.Range(0, 11).Select(l => VisionCurves.For(new SkillProfile("x", StandardSkills.Ten, [0, l, 0, 0, 0, 0, 0, 0, 0, 0])).RecognizeSeconds)];
        for (int l = 1; l < all.Length; l++)
            Assert.True(all[l] <= all[l - 1], string.Join(", ", all));
    }

    [Fact]
    public void FlicksOvershootAndUndershootBySeed()
    {
        // Signed, random per flick: across seeds some flicks pass a 90° target
        // and some stop short of it before the correction.
        var p = new MotionParams(100f, 600f, 20f, 100f);
        int over = 0, under = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            HumanMotion m = Motion(p, seed);
            float extreme = 0f;
            while (true)
            {
                m.Step(new AiAngles(0f, 90f, 0f), 1, Dt);
                extreme = MathF.Max(extreme, m.View.Yaw);
                if (m.Phase == AimPhase.Correct)
                    break;
            }

            if (extreme > 90.5f)
                over++;
            else if (extreme < 89.5f)
                under++;
        }

        Assert.True(over > 0 && under > 0, $"{over} over, {under} under");
    }

    [Fact]
    public void AFlickEndsOnTheTarget()
    {
        HumanMotion m = Motion(Preset("normal"));
        for (int i = 0; i < 300; i++)
            m.Step(new AiAngles(10f, 120f, 0f), 1, Dt);

        Assert.Equal(AimPhase.Pursuit, m.Phase);
        Assert.InRange(HumanMotion.AngleDelta(m.View.Yaw, 120f), -0.5f, 0.5f);
        Assert.InRange(m.View.Pitch, 9.5f, 10.5f);
    }

    [Fact]
    public void ALongerFlickTakesLonger()
    {
        // Fitts: the time grows with the distance.
        MotionParams p = Preset("hard");
        float Flick(float yaw)
        {
            HumanMotion m = Motion(p);
            int ticks = 0;
            while (m.Phase != AimPhase.Correct && ticks < 400)
            {
                m.Step(new AiAngles(0f, yaw, 0f), 1, Dt);
                if (m.Phase is AimPhase.Flick or AimPhase.Correct)
                    ticks++;
            }

            return ticks * Dt;
        }

        float[] t = [Flick(10f), Flick(45f), Flick(170f)];
        Assert.True(t[0] < t[1] && t[1] < t[2], string.Join(", ", t));
    }

    [Fact]
    public void PursuitErrorGrowsWithTheTargetsSpeed()
    {
        float Error(float degPerSec)
        {
            HumanMotion m = Motion(Preset("normal"));
            float yaw = 0f;
            for (int i = 0; i < 200; i++)
            {
                yaw += degPerSec * Dt;
                m.Step(new AiAngles(0f, yaw, 0f), 0, Dt);
            }

            return MathF.Abs(HumanMotion.AngleDelta(m.View.Yaw, yaw));
        }

        float slow = Error(30f), fast = Error(120f);
        Assert.True(slow > 0f, "the pursuit lags");
        Assert.True(fast > slow * 2f, $"{slow}° at 30°/s, {fast}° at 120°/s");
    }

    [Fact]
    public void TheSameSeedMovesTheSameWay()
    {
        HumanMotion a = Motion(Preset("hard"), 99), b = Motion(Preset("hard"), 99);
        for (int i = 0; i < 120; i++)
            Assert.Equal(a.Step(new AiAngles(-5f, 150f, 0f), 3, Dt), b.Step(new AiAngles(-5f, 150f, 0f), 3, Dt));
    }
}
