using Xunit;

namespace SourceSharp.Ai.Tests.Core;

/// <summary>
/// The noise on a tree's scores (plan_bots_teams_tf2_part_g G.2.2, <see cref="ScoreNoise"/>): a
/// factor about 1 that is the same for a seed, a score and a tick, differs between bots and
/// between a bot's scores, and wanders slowly.
/// </summary>
public sealed class ScoreNoiseTests
{
    private const float Tick = 0.015f, Amplitude = 0.1f;

    private static float At(ulong seed, int score, int tick, float amplitude = Amplitude) => ScoreNoise.Factor(seed, score, new Tick(tick), Tick, amplitude);

    [Fact]
    public void TheFactorStaysWithinItsAmplitudeOfOne()
    {
        for (int t = 0; t < 20000; t += 37)
            Assert.InRange(At(7, 2, t), 1f - Amplitude, 1f + Amplitude);
    }

    [Fact]
    public void NoAmplitudeIsExactlyOne() => Assert.Equal(1f, At(7, 2, 500, amplitude: 0f));

    [Fact]
    public void TheSameSeedScoreAndTickReadTheSame() => Assert.Equal(At(7, 2, 4321), At(7, 2, 4321));

    [Fact]
    public void TwoSeedsReadApart()
    {
        int apart = Enumerable.Range(0, 50).Count(i => MathF.Abs(At(7, 2, 800 * i) - At(8, 2, 800 * i)) > 0.01f);
        Assert.True(apart > 25, $"two bots' noise on one score was apart at {apart} of 50 moments");
    }

    [Fact]
    public void ABotsScoresReadApart()
    {
        int apart = Enumerable.Range(0, 50).Count(i => MathF.Abs(At(7, 2, 800 * i) - At(7, 3, 800 * i)) > 0.01f);
        Assert.True(apart > 25, $"one bot's noise on two scores was apart at {apart} of 50 moments");
    }

    /// <summary>From one decide to the next it moves by a small share of its amplitude: no flip between two close scores each decide.</summary>
    [Fact]
    public void ItWandersSlowly()
    {
        float most = 0f;
        for (int t = 0; t < 20000; t += DecideSchedule.Period)
            most = MathF.Max(most, MathF.Abs(At(7, 2, t + DecideSchedule.Period) - At(7, 2, t)));
        Assert.InRange(most, 1e-5f, Amplitude * 0.05f);
    }

    /// <summary>Over a round it does move: the draws of a score cover both sides of 1.</summary>
    [Fact]
    public void OverARoundItReadsBothAboveAndBelowOne()
    {
        float[] read = [.. Enumerable.Range(0, 200).Select(i => At(7, 2, 800 * i))];
        Assert.True(read.Min() < 1f - Amplitude * 0.5f && read.Max() > 1f + Amplitude * 0.5f, $"read {read.Min()} to {read.Max()}");
    }
}
