using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// Where players hold (lane G4b, <see cref="HoldTracker"/>): a run of one player's samples within 128 u of where it began,
/// as one context, of at least 4 s, handed over when it ends with the middle of its samples, the yaw faced most, its
/// seconds, its kills and whether it ended in a death.
/// </summary>
public sealed class HoldTrackerTests
{
    private const double Frame = 0.105d;
    private static readonly ExperienceContext Red = new(0, 2, 3);
    private readonly HoldTracker _tracker = new();
    private readonly List<HeldSpot> _ended = new();

    /// <summary>One sampled frame with each of <paramref name="players"/> (slot, place, yaw) living.</summary>
    private void Step(params (int Slot, AiVector At, float Yaw)[] players)
    {
        _tracker.BeginFrame();
        foreach ((int slot, AiVector at, float yaw) in players)
            _tracker.Sample(slot, at, yaw, Red, bot: false, Frame, _ended);
        _tracker.EndFrame(_ended);
    }

    /// <summary><paramref name="frames"/> frames of slot 1 at <paramref name="at"/>, swaying a little across its place.</summary>
    private void Stand(int frames, AiVector at, float yaw = 90f)
    {
        for (int i = 0; i < frames; i++)
            Step((1, at + new AiVector(i % 2 == 0 ? 20f : -20f, 0f, 0f), yaw));
    }

    [Fact]
    public void FiveSecondsInOnePlaceIsAHoldWhenThePlayerMovesOn()
    {
        Stand(49, new AiVector(500f, 500f, 0f));
        Assert.Empty(_ended);
        Step((1, new AiVector(900f, 500f, 0f), 0f));
        HeldSpot held = Assert.Single(_ended);
        Assert.Equal(48 * Frame, held.Seconds, 6);
        Assert.Equal(500f, held.At.X, 0);
        Assert.Equal(90f, held.Yaw);
        Assert.Equal(Red, held.Context);
        Assert.Equal(0d, held.Deaths);
    }

    [Fact]
    public void ThreeSecondsIsNoHold()
    {
        Stand(30, new AiVector(500f, 500f, 0f));
        Step((1, new AiVector(900f, 500f, 0f), 0f));
        Assert.Empty(_ended);
    }

    [Fact]
    public void WalkingIsNoHold()
    {
        for (int i = 0; i < 100; i++)
            Step((1, new AiVector(i * 30f, 0f, 0f), 0f));
        Step();
        Assert.Empty(_ended);
    }

    [Fact]
    public void AHoldEndingInADeathCountsTheDeathAndTheKillsMadeThere()
    {
        Stand(50, new AiVector(500f, 500f, 0f));
        _tracker.Kill(1);
        _tracker.Kill(1);
        _tracker.Kill(2);
        _tracker.Died(1, _ended);
        HeldSpot held = Assert.Single(_ended);
        Assert.Equal(2d, held.Kills);
        Assert.Equal(1d, held.Deaths);
    }

    [Fact]
    public void APlayerNoLongerSampledHasLeftItsHold()
    {
        Stand(50, new AiVector(500f, 500f, 0f));
        Step((2, new AiVector(0f, 0f, 0f), 0f));
        Assert.Single(_ended);
    }

    [Fact]
    public void AnotherClassOrSideEndsTheRun()
    {
        Stand(50, new AiVector(500f, 500f, 0f));
        _tracker.BeginFrame();
        _tracker.Sample(1, new AiVector(500f, 500f, 0f), 90f, Red with { Class = 7 }, bot: false, Frame, _ended);
        Assert.Single(_ended);
    }

    [Fact]
    public void ALongHoldIsHandedOverEveryThirtySeconds()
    {
        Stand(700, new AiVector(500f, 500f, 0f));
        Assert.Equal(2, _ended.Count);
        Assert.All(_ended, h => Assert.InRange(h.Seconds, HoldTracker.MaxSeconds, HoldTracker.MaxSeconds + Frame));
    }

    [Fact]
    public void TheRoundsEndHandsOverAHoldUnderWay()
    {
        Stand(50, new AiVector(500f, 500f, 0f));
        _tracker.EndAll(_ended);
        Assert.Single(_ended);
        // And a level's end hands nothing over.
        _ended.Clear();
        Stand(50, new AiVector(500f, 500f, 0f));
        _tracker.Clear();
        Step();
        Assert.Empty(_ended);
    }

    [Fact]
    public void ABotsHoldSaysABotHeldIt()
    {
        for (int i = 0; i < 50; i++)
        {
            _tracker.BeginFrame();
            _tracker.Sample(4, new AiVector(500f, 500f, 0f), 0f, Red, bot: true, Frame, _ended);
            _tracker.EndFrame(_ended);
        }

        _tracker.EndAll(_ended);
        Assert.True(Assert.Single(_ended).Bot);
    }
}
