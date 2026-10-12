using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsSplashPlan against stock's PhysicsSplash (physics_fx.cpp). A 40-unit
/// box (extents 20 x 20 on the surface) at the origin, the water plane z = 0
/// with its normal up; numbers worked from stock's formula.
/// </summary>
public class PhysicsSplashPlanTests
{
    private static readonly Vector Up = new(0f, 0f, 1f);
    private static readonly Vector[] Axes = { new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 1f) };

    // The box's farthest point along a direction: 20 along each axis.
    private static Vector Box(Vector dir) => new(MathF.Sign(dir.X) * 20f, MathF.Sign(dir.Y) * 20f, MathF.Sign(dir.Z) * 20f);

    private static List<SplashEffect> Plan(float fallSpeed, float mass = 100f, bool ragdoll = false, bool slime = false,
                                           float floatDraw = 0f, int intDraw = 1)
        => PhysicsSplashPlan.Plan(Up, 0f, Axes, Box, new Vector(0f, 0f, -fallSpeed), mass, ragdoll, slime,
                                  (lo, hi) => floatDraw, (lo, hi) => intDraw);

    // speed = v^2 x 20 x 20 / 2,500,000 x mass x 0.01; at mass 100: v^2 / 6250.
    [Fact]
    public void A_fast_heavy_body_splashes_at_the_centre_of_its_footprint()
    {
        // v 200: 40000 / 6250 = 6.4 -> size RemapVal( 6.4, 0.35, 50, 8, 18 ) = 9.2185..., + draw 0.
        var effects = Plan(200f);

        var first = effects[0];
        Assert.Equal(PhysicsSplashPlan.Splash, first.Name);
        Assert.Equal(new Vector(0f, 0f, 0f), first.Origin);
        Assert.Equal(Up, first.Normal);
        Assert.Equal(8f + (6.4f - 0.35f) / (50f - 0.35f) * 10f, first.Scale, 3);
    }

    [Fact]
    public void A_slow_entry_only_ripples()
    {
        // v 40: 1600 / 6250 = 0.256: over 0.1, under 0.35.
        Assert.Equal(PhysicsSplashPlan.Ripple, Plan(40f)[0].Name);
    }

    [Fact]
    public void Slower_still_throws_nothing()
        => Assert.Empty(Plan(20f));   // 400 / 6250 = 0.064

    [Fact]
    public void A_ragdoll_part_throws_nothing()
        => Assert.Empty(Plan(200f, ragdoll: true));

    [Fact]
    public void Slime_is_flagged()
        => Assert.True(Plan(200f, slime: true)[0].InSlime);

    [Fact]
    public void A_footprint_over_five_hundred_adds_corner_splashes()
    {
        // 20 x 20 = 400 is under; a 30 x 30 box is 900, over: one draw of 3 adds three.
        var effects = PhysicsSplashPlan.Plan(Up, 0f, Axes,
            dir => new(MathF.Sign(dir.X) * 30f, MathF.Sign(dir.Y) * 30f, MathF.Sign(dir.Z) * 30f),
            new Vector(0f, 0f, -200f), 100f, false, false, (lo, hi) => 0f, (lo, hi) => 3);

        Assert.Equal(4, effects.Count);
        Assert.Single(Plan(200f));
    }
}
