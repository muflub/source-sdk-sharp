using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arithmetic <c>CEntityFlame</c> was made of, now that it is C#.
///
/// The three questions worth asking of it are the three places a number gets
/// derived from another number: a size from a pair of bounds, a radius from a
/// size, and a deadline from a duration. Each has a value stock accepts and
/// this rejects, and each rejection is asserted rather than assumed.
/// </summary>
public class FlameRulesTests
{
    private static Vector V(float x, float y, float z) => new(x, y, z);

    // ---- SizeForBounds ---------------------------------------------------

    [Fact]
    public void ASquareTargetBurnsAsWideAsItIs()
    {
        // 40 x 40, so both extents are 40 and the mean is 40.
        float size = FlameRules.SizeForBounds(V(-20f, -20f, 0f), V(20f, 20f, 70f));

        Assert.True(size == 40f, "a 40-unit-wide box gets a 40-unit flame");
    }

    [Fact]
    public void TheMeanIsOfXAndYOnly()
    {
        // 40 wide, 20 deep, 400 tall. Stock's mean is (40 + 20) / 2 = 30, and
        // the height plays no part: a flame is as wide as what is burning.
        float size = FlameRules.SizeForBounds(V(-20f, -10f, 0f), V(20f, 10f, 400f));

        Assert.True(size == 30f, "the height of the thing burning does not widen its flame");
    }

    [Fact]
    public void AThinTargetStillGetsAVisibleFlame()
    {
        // 2 x 2. Stock's floor is what stops a wire fence burning invisibly.
        float size = FlameRules.SizeForBounds(V(-1f, -1f, 0f), V(1f, 1f, 60f));

        Assert.True(size == FlameRules.MinimumSize, "a thin target burns at the minimum size, not at its own width");
    }

    [Fact]
    public void AnEntityWithNoCollisionModelBurnsAtTheMinimum()
    {
        // Both bounds the same vector, which is what an entity with no
        // collision model answers.
        float size = FlameRules.SizeForBounds(Vector.Zero, Vector.Zero);

        Assert.True(size == FlameRules.MinimumSize, "a sizeless target burns at the minimum size");
    }

    [Fact]
    public void InvertedBoundsDoNotProduceANegativeFlame()
    {
        float size = FlameRules.SizeForBounds(V(20f, 20f, 0f), V(-20f, -20f, 0f));

        Assert.True(size == FlameRules.MinimumSize, "bounds the wrong way round burn at the minimum size, not at a negative one");
    }

    [Fact]
    public void ANaNBoundDoesNotBecomeANaNFlame()
    {
        // This is the one a floor alone does not catch: NaN < 16 is false, so
        // stock's `if ( size < 16.0f )` lets it straight through.
        float size = FlameRules.SizeForBounds(V(float.NaN, -20f, 0f), V(20f, 20f, 0f));

        Assert.True(size == FlameRules.MinimumSize, "a NaN bound burns at the minimum size rather than at NaN");
    }

    [Fact]
    public void AnInfiniteBoundDoesNotBecomeAnInfiniteFlame()
    {
        float size = FlameRules.SizeForBounds(V(float.NegativeInfinity, -20f, 0f), V(20f, 20f, 0f));

        Assert.True(size == FlameRules.MinimumSize, "an infinite bound burns at the minimum size rather than filling the map");
    }

    // ---- DamageRadius ----------------------------------------------------

    [Fact]
    public void TheBurnReachesHalfTheFlamesWidth()
    {
        Assert.True(FlameRules.DamageRadius(64f) == 32f, "a 64-unit flame burns 32 units around it");
    }

    [Fact]
    public void ARadiusIsNeverZero()
    {
        // The engine's falloff is damage / radius, so a zero here is a divide
        // by zero inside RadiusDamage.
        float radius = FlameRules.DamageRadius(0f);

        Assert.True(radius > 0f, "a flame with no size still has a strictly positive burn radius");
        Assert.True(radius == FlameRules.MinimumSize * 0.5f, "and it is the minimum flame's radius");
    }

    [Fact]
    public void ARadiusIsNeverNaN()
    {
        float radius = FlameRules.DamageRadius(float.NaN);

        Assert.True(float.IsFinite(radius) && radius > 0f, "a NaN size gives a finite, positive burn radius");
    }

    [Fact]
    public void ARadiusIsNeverNegative()
    {
        float radius = FlameRules.DamageRadius(-64f);

        Assert.True(radius > 0f, "a negative size gives a positive burn radius rather than a negative one");
    }

    // ---- ExpiryFor -------------------------------------------------------

    [Fact]
    public void ALifetimeIsCountedFromNow()
    {
        Assert.True(FlameRules.ExpiryFor(100f, 20f) == 120f, "a 20-second flame lit at t=100 goes out at t=120");
    }

    [Fact]
    public void AnUnsetLifetimeGoesOutAtOnce()
    {
        // Stock's behaviour for a mapper who left the key alone, kept
        // deliberately: m_flLifetime < curtime is already true on the first
        // think.
        Assert.True(FlameRules.ExpiryFor(100f, 0f) == 100f, "a flame given no lifetime expires immediately, as stock's does");
    }

    [Fact]
    public void ANaNLifetimeDoesNotBurnForever()
    {
        float expiry = FlameRules.ExpiryFor(100f, float.NaN);

        Assert.True(float.IsFinite(expiry), "a NaN lifetime gives a finite deadline");
        Assert.True(expiry == 100f, "and it is the same deadline an unset lifetime gives");
    }

    [Fact]
    public void AnInfiniteLifetimeDoesNotBurnForever()
    {
        Assert.True(FlameRules.ExpiryFor(100f, float.PositiveInfinity) == 100f,
            "an infinite lifetime is not a request, and goes out at once");
    }

    [Fact]
    public void ANegativeLifetimeGoesOutAtOnceRatherThanInThePast()
    {
        Assert.True(FlameRules.ExpiryFor(100f, -50f) == 100f,
            "a negative lifetime expires now, not fifty seconds ago");
    }

    // ---- The rates -------------------------------------------------------

    [Fact]
    public void ThePerPassDamagesAreTheRatesTimesTheInterval()
    {
        // Stock spells these as two #defines multiplied together, and this is
        // the only thing that keeps that multiplication from drifting: a rate
        // changed without its per-pass value is a fire that burns at the wrong
        // speed with no other symptom.
        Assert.True(FlameRules.DirectDamage == FlameRules.DirectDamagePerSecond * FlameRules.DamageInterval,
            "the direct damage per pass is the per-second rate over one interval");

        Assert.True(FlameRules.RadiusDamage == FlameRules.RadiusDamagePerSecond * FlameRules.DamageInterval,
            "the radius damage per pass is the per-second rate over one interval");
    }

    [Fact]
    public void BeingOnFireIsWorseThanStandingNextToIt()
    {
        Assert.True(FlameRules.DirectDamage > FlameRules.RadiusDamage,
            "the thing that is alight takes more than the thing beside it");
    }

    [Fact]
    public void TheStockNumbersAreTheStockNumbers()
    {
        // Read off EntityFlame.h, which is leaving the build with this port.
        // Named here so the values survive the file that carried them.
        Assert.True(FlameRules.DamageInterval == 0.2f, "FLAME_DAMAGE_INTERVAL is a fifth of a second");
        Assert.True(FlameRules.DirectDamagePerSecond == 5.0f, "FLAME_DIRECT_DAMAGE_PER_SEC is five");
        Assert.True(FlameRules.RadiusDamagePerSecond == 4.0f, "FLAME_RADIUS_DAMAGE_PER_SEC is four");
        Assert.True(FlameRules.MinimumSize == 16.0f, "CEntityFlame::Create floors the size at sixteen units");
        Assert.True(FlameRules.HeatPerPass == 2.0f, "FlameThink warms the fires around it by two");
    }
}
