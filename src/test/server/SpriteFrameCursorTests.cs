using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>C_Plasma::UpdateAnimation</c>, restated as a function of elapsed time, and
/// the modulo-by-zero that was hiding in its wrap.
/// </summary>
public class SpriteFrameCursorTests
{
    [Fact]
    public void AStoppedSpriteStaysOnTheFrameItStartedOn()
    {
        Assert.True(SpriteFrameCursor.Advance(3f, frameRate: 0f, elapsed: 10f, frameCount: 8) == 3f,
            "a sprite with no frame rate holds the frame it began on");
    }

    [Fact]
    public void ASpriteAdvancesAtItsFrameRate()
    {
        float f = SpriteFrameCursor.Advance(0f, frameRate: 20f, elapsed: 0.25f, frameCount: 16);

        Assert.True(MathF.Abs(f - 5f) < 0.001f, "a quarter second at twenty frames a second is five frames on");
    }

    [Fact]
    public void TheCursorWrapsRatherThanRunningOffTheEnd()
    {
        float f = SpriteFrameCursor.Advance(0f, frameRate: 10f, elapsed: 1.5f, frameCount: 10);

        Assert.True(MathF.Abs(f - 5f) < 0.001f, "fifteen frames into a ten-frame sprite is frame five");
    }

    [Fact]
    public void ANegativeRateWalksBackwardsAndStaysInRange()
    {
        float f = SpriteFrameCursor.Advance(0f, frameRate: -10f, elapsed: 0.3f, frameCount: 10);

        Assert.True(f >= 0f && f < 10f, "a sprite running backwards is still on one of its own frames");
        Assert.True(MathF.Abs(f - 7f) < 0.001f, "three frames back from zero is frame seven of ten");
    }

    /// <summary>
    /// THE MODULO-BY-ZERO. The frame count comes from the loaded model, so a
    /// sprite whose model failed to load or whose index never arrived has zero
    /// of them — and <c>x % 0</c> is the same fault as <c>x / 0</c>.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ASpriteWithNoFramesIsOnFrameZeroRatherThanDividingByIt(int frameCount)
    {
        float f = SpriteFrameCursor.Advance(4f, frameRate: 18f, elapsed: 7f, frameCount: frameCount);

        Assert.True(float.IsFinite(f), $"a sprite with {frameCount} frames still produces a finite cursor");
        Assert.True(f == 0f, $"a sprite with {frameCount} frames is on frame zero, the only one it could be on");
    }

    /// <summary>
    /// The guarantee stated as a property, over every shape that could reach it.
    /// </summary>
    [Theory]
    [InlineData(float.NaN, 18f, 1f)]
    [InlineData(4f, float.NaN, 1f)]
    [InlineData(4f, 18f, float.NaN)]
    [InlineData(4f, float.PositiveInfinity, 1f)]
    [InlineData(4f, 18f, float.PositiveInfinity)]
    [InlineData(-9f, 18f, 1f)]
    [InlineData(4f, 18f, -5f)]
    [InlineData(1e30f, 1e30f, 1e30f)]
    public void TheCursorIsAlwaysFiniteAndInsideTheSprite(float startFrame, float rate, float elapsed)
    {
        const int frames = 12;

        float f = SpriteFrameCursor.Advance(startFrame, rate, elapsed, frames);

        Assert.True(float.IsFinite(f),
            $"({startFrame}, {rate}, {elapsed}) still produces a finite cursor");
        Assert.True(f >= 0f && f < frames,
            $"({startFrame}, {rate}, {elapsed}) still lands on one of the sprite's own frames");
    }

    /// <summary>
    /// Walks a whole second of a real flame at 60Hz. Fifteen to twenty frames a
    /// second over a sixteen-frame sprite is what a plasma flame runs at.
    /// </summary>
    [Fact]
    public void EveryFrameOfARealFlameLandsOnTheSprite()
    {
        const int frames = 16;

        for (float rate = 15f; rate <= 20f; rate += 1f)
        {
            for (float t = 0f; t <= 1f; t += 1f / 60f)
            {
                float f = SpriteFrameCursor.Advance(3.5f, rate, t, frames);

                Assert.True(f >= 0f && f < frames,
                    $"at {rate}fps and {t}s the cursor is on one of the sixteen frames");
            }
        }
    }
}
