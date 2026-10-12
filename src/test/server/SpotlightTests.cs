using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The three lights that are applied at draw time rather than baked.
///
/// <para>
/// What is checked here is the arithmetic, which is where these can be wrong in
/// a way nothing reports. A spotlight's end fades out over the second half of
/// its length and widens as it stretches, and both are curves with boundaries
/// at exactly the length and at twice it — a fade that reaches zero one length
/// early leaves a spotlight with no pool of light at the end, and a width that
/// does not clamp asks the engine's beam renderer for a beam it silently
/// refuses to draw.
/// </para>
/// </summary>
public class SpotlightTests
{

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 0.001f;

    /// <summary>
    /// Full colour out to the stated length, then linearly to nothing at twice
    /// it. Both boundaries matter: the first is where the fade starts, the
    /// second is where the entity stops being drawn at all.
    /// </summary>
    [Fact]
    public void SpotlightFade()
    {
        Assert.True(Near(PointSpotlight.EndAlpha(100f, 500f), 1f), "a short spotlight is at full brightness");
        Assert.True(Near(PointSpotlight.EndAlpha(500f, 500f), 1f), "and still is at exactly its length");

        Assert.True(Near(PointSpotlight.EndAlpha(750f, 500f), 0.5f), "halfway past its length it is half faded");

        Assert.True(Near(PointSpotlight.EndAlpha(1000f, 500f), 0f), "at twice its length it is gone");
        Assert.True(Near(PointSpotlight.EndAlpha(5000f, 500f), 0f), "and stays gone beyond that");

        // A zero length is a mapper's mistake the entity corrects at spawn, but
        // the arithmetic must not divide by it on the way there.
        Assert.True(Near(PointSpotlight.EndAlpha(100f, 0f), 0f), "a zero length does not divide by zero");
    }

    /// <summary>
    /// The far end widens in proportion to how far the beam reaches, so it
    /// appears the same width from the emitter. The clamp is the part that
    /// matters: past the engine's maximum a beam is not drawn at all.
    /// </summary>
    [Fact]
    public void SpotlightWidth()
    {
        Assert.True(Near(PointSpotlight.EndWidth(500f, 500f, 10f), 10f), "at its stated length the end is the goal width");

        Assert.True(Near(PointSpotlight.EndWidth(250f, 500f, 10f), 5f), "at half the length it is half as wide");

        Assert.True(Near(PointSpotlight.EndWidth(1000f, 500f, 10f), 20f), "at twice the length it is twice as wide");

        // Past the engine's maximum the beam renderer draws nothing, so the
        // clamp is the difference between a wide spotlight and no spotlight.
        Assert.True(Near(PointSpotlight.EndWidth(100000f, 500f, 90f), PointSpotlight.MaxBeamWidth), "a very long thick beam clamps to the maximum");

        Assert.True(PointSpotlight.EndWidth(0f, 500f, 10f) >= 0f, "and never goes negative");
    }

    /// <summary>
    /// A dynamic light's brightness travels as the engine's shared colour
    /// exponent, which is a signed byte. A mapper's 200 arriving as something
    /// dark is exactly the failure this clamp exists for.
    /// </summary>
    [Fact]
    public void ExponentClamp()
    {
        Assert.True(LightDynamic.ClampExponent(3) == 3, "an ordinary brightness is untouched");
        Assert.True(LightDynamic.ClampExponent(-4) == -4, "a negative one is untouched");
        Assert.True(LightDynamic.ClampExponent(200) == 127, "too bright clamps rather than wraps");
        Assert.True(LightDynamic.ClampExponent(-200) == -127, "and too dark clamps the other way");
    }
}
