using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// C_Sprite's scale and brightness ramps (Sprite.cpp: ClientThink,
/// GetRenderScale, GetRenderBrightness): a timed change is drawn as a lerp from
/// the previous target to the new one over its time, an untimed one at once.
///
/// <para>
/// Found side by side with stock (a crossbow bolt in a wall, 2026-10-25): this
/// client drew a sprite's brightness as it arrived, so a fade (FadeAndDie)
/// was a blink and a charge-up (SetBrightness over half a second) a jump.
/// </para>
/// </summary>
public class SpriteRampTests
{
    [Fact]
    public void AFreshSpriteDrawsWhatArrived()
    {
        var ramp = new SpriteRamp();
        ramp.Created(0.2f, 255);
        ramp.Think(10f, 0.2f, 255);

        Assert.Equal((0.2f, 255), (ramp.Scale(10f, 0.2f, 0f), ramp.Brightness(10f, 255, 0f)));
    }

    [Fact]
    public void ATimedFadeIsHalfWayAtHalfItsTime()
    {
        var ramp = new SpriteRamp();
        ramp.Created(0.2f, 200);

        ramp.Think(10f, 0.2f, 0);

        Assert.Equal(200, ramp.Brightness(10f, 0, 3f));
        Assert.Equal(100, ramp.Brightness(11.5f, 0, 3f));
        Assert.Equal(0, ramp.Brightness(13.1f, 0, 3f));
    }

    [Fact]
    public void ATimedScaleLerpsToItsTarget()
    {
        var ramp = new SpriteRamp();
        ramp.Created(0.1f, 0);

        ramp.Think(5f, 0.3f, 0);

        Assert.Equal(0.2f, ramp.Scale(5.25f, 0.3f, 0.5f), 4);
        Assert.Equal(0.3f, ramp.Scale(5.6f, 0.3f, 0.5f));
    }

    [Fact]
    public void AnUntimedChangeArrivesAtOnce()
    {
        var ramp = new SpriteRamp();
        ramp.Created(0.2f, 0);

        ramp.Think(10f, 0.2f, 80);

        Assert.Equal(80, ramp.Brightness(10f, 80, 0f));
    }

    /// <summary>Stock's quirk: a second change starts from the first's target, not from where it had got to.</summary>
    [Fact]
    public void ASecondChangeStartsFromTheFirstsTarget()
    {
        var ramp = new SpriteRamp();
        ramp.Created(0.1f, 0);

        ramp.Think(10f, 0.1f, 32);    // 0 -> 32 over half a second
        ramp.Think(10.1f, 0.1f, 80);  // a tenth in: 32 -> 80

        Assert.Equal(32, ramp.Brightness(10.1f, 80, 1f));
    }
}
