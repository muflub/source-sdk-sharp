using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>C_Plasma::UpdateScale</c>, and the 0/0 that was sitting in it.
///
/// Engine-free: five floats in, one float out.
/// </summary>
public class PlasmaScaleRampTests
{
    [Fact]
    public void ARampInProgressIsPartWayBetweenTheTwoSizes()
    {
        float half = PlasmaScaleRamp.Evaluate(now: 5f, timeStart: 0f, timeEnd: 10f, from: 0f, to: 100f);

        Assert.True(MathF.Abs(half - 50f) < 0.001f, "halfway through a ramp is halfway between the sizes");
    }

    [Fact]
    public void ARampNotYetBegunIsTheSizeItStartedAt()
    {
        Assert.True(PlasmaScaleRamp.Evaluate(now: -5f, timeStart: 0f, timeEnd: 10f, from: 7f, to: 100f) == 7f,
            "before a ramp starts the flame is still its starting size");
    }

    [Fact]
    public void ARampRunPastItsEndIsTheSizeItWasHeadingFor()
    {
        Assert.True(PlasmaScaleRamp.Evaluate(now: 500f, timeStart: 0f, timeEnd: 10f, from: 7f, to: 100f) == 100f,
            "past the end of a ramp the flame is the size it was told to reach, not larger");
    }

    [Fact]
    public void ShrinkingRunsTheSameWayAsGrowing()
    {
        float shrunk = PlasmaScaleRamp.Evaluate(now: 2.5f, timeStart: 0f, timeEnd: 10f, from: 100f, to: 0f);

        Assert.True(MathF.Abs(shrunk - 75f) < 0.001f, "a quarter of the way into a fade the flame is three quarters its size");
    }

    /// <summary>
    /// THE BUG. <c>env_fire</c> creates its flame with a ramp of zero seconds,
    /// so on the first frame stock's <c>time &gt; end</c> was false — the two
    /// were equal — and the else branch computed <c>0 / 0</c>. The NaN became
    /// the scale of seven sprites and reached the vertex builder.
    /// </summary>
    [Fact]
    public void AZeroSecondRampIsTheDestinationSizeImmediatelyRatherThanNaN()
    {
        float now = 12.5f;

        float scale = PlasmaScaleRamp.Evaluate(now, timeStart: now, timeEnd: now, from: 0f, to: 64f);

        Assert.True(float.IsFinite(scale), "a zero-second ramp does not divide zero by zero");
        Assert.True(scale == 64f, "it means be the destination size now, which is what a zero-second ramp asks for");
    }

    [Fact]
    public void AZeroSecondRampIsAlreadyFinished()
    {
        float now = 12.5f;

        Assert.True(PlasmaScaleRamp.IsFinished(now, timeStart: now, timeEnd: now),
            "a ramp with no duration is over the moment it begins");
    }

    /// <summary>
    /// Every shape that could put a NaN or an infinity into a sprite's scale.
    /// The guarantee is stated as a property rather than case by case, because
    /// the failure downstream is the same whichever route reached it: every
    /// comparison against NaN is false, so the <c>MAX(0.000001f, scale)</c>
    /// floor in the renderer does not catch it and the particle is both drawn
    /// and immortal.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 0f)]                                        // the zero-second ramp
    [InlineData(1f, 10f, 5f)]                                       // backwards
    [InlineData(1f, float.NaN, 10f)]
    [InlineData(1f, 0f, float.NaN)]
    [InlineData(float.NaN, 0f, 10f)]
    [InlineData(1f, float.NegativeInfinity, float.PositiveInfinity)]
    [InlineData(1f, 0f, float.PositiveInfinity)]
    [InlineData(float.PositiveInfinity, 0f, 10f)]
    public void TheAnswerIsAlwaysFiniteAndBetweenTheTwoSizes(float now, float start, float end)
    {
        const float from = 8f;
        const float to = 96f;

        float scale = PlasmaScaleRamp.Evaluate(now, start, end, from, to);

        Assert.True(float.IsFinite(scale),
            $"a ramp of ({now}, {start}, {end}) still produces a finite size");
        Assert.True(scale >= from && scale <= to,
            $"a ramp of ({now}, {start}, {end}) stays within the two sizes it was given");
    }

    /// <summary>
    /// A destination that is already NaN cannot be interpolated towards, and the
    /// honest answer is to stay where the flame is rather than to adopt it.
    /// </summary>
    [Fact]
    public void ADestinationThatIsNotANumberDoesNotBecomeTheSize()
    {
        float scale = PlasmaScaleRamp.Evaluate(now: 5f, timeStart: 0f, timeEnd: 10f, from: 32f, to: float.NaN);

        Assert.True(float.IsFinite(scale), "a NaN destination does not become the flame's size");
        Assert.True(scale == 32f, "the flame holds the size it already had");
    }

    [Fact]
    public void ASizeThatIsAlreadyNaNIsLeftBehindRatherThanCarried()
    {
        float scale = PlasmaScaleRamp.Evaluate(now: 5f, timeStart: 0f, timeEnd: 10f, from: float.NaN, to: 40f);

        Assert.True(float.IsFinite(scale), "a NaN starting size does not poison the whole ramp");
        Assert.True(scale == 40f, "the flame snaps to the size it was heading for");
    }

    /// <summary>
    /// Walks a whole ramp frame by frame at 60Hz and requires that nothing along
    /// it leaves the interval — a stepped version of the guarantee, which is how
    /// the game actually reads it.
    /// </summary>
    [Fact]
    public void EveryFrameOfARealRampStaysInsideTheInterval()
    {
        const float start = 100f;
        const float end = 100.5f;
        const float from = 0f;
        const float to = 64f;

        float previous = float.NegativeInfinity;

        for (float now = 99f; now <= 102f; now += 1f / 60f)
        {
            float scale = PlasmaScaleRamp.Evaluate(now, start, end, from, to);

            Assert.True(float.IsFinite(scale), $"the size at {now} is finite");
            Assert.True(scale >= from && scale <= to, $"the size at {now} is between the two sizes");
            Assert.True(scale >= previous, $"the size at {now} never goes backwards during a growing ramp");

            previous = scale;
        }

        Assert.True(previous == to, "and the ramp finishes at the size it was told to reach");
    }
}
