using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// How hot a fire gets and how far it reaches.
///
/// The arithmetic is worth pinning because none of it is visible: a fire that
/// grows at the wrong rate still looks like a fire, and the only symptom is a
/// map where the sprinkler trips at the wrong moment or the barrel never
/// catches. The curve in particular — damage rises with the SQUARE of heat —
/// is the kind of thing a rewrite flattens by accident.
/// </summary>
public class FireTests
{

    [Fact]
    public void Reach()
    {
        // A 128-unit fire at full heat: half its width, doubled by the spread
        // multiplier. 128 * (128/256) * 0.5 * 2 = 64.
        float full = FireRules.DamageRadius(64f, 64f, 128f);
        Assert.True(Math.Abs(full - 64f) < 0.001f, "a full fire reaches half its own size");

        // The same fire at a quarter heat reaches a quarter as far.
        float quarter = FireRules.DamageRadius(16f, 64f, 128f);
        Assert.True(Math.Abs(quarter - 16f) < 0.001f, "reach falls off with heat");

        // But never to nothing. Without this floor a fire that has only just
        // caught cannot reach the thing that lit it, and nothing ever spreads.
        Assert.True(FireRules.DamageRadius(0.1f, 64f, 128f) >= 16f, "a barely-lit fire still has some reach");

        Assert.True(FireRules.DamageRadius(10f, 0f, 128f) == 0f, "and a fire with no maximum has none at all");

        // Burning reach is half of spreading reach. A fire crosses a room to
        // another fire before it crosses it to a crate.
        Assert.True(Math.Abs(FireRules.HurtRadius(full) - 32f) < 0.001f, "a fire spreads twice as far as it burns");
    }

    [Fact]
    public void Curve()
    {
        float full = FireRules.DamagePerInterval(64f, 64f, 1f);
        float half = FireRules.DamagePerInterval(32f, 64f, 1f);

        // Base 1, plus (64/64)*64 * 0.1 * 1 = 6.4. So 7.4.
        Assert.True(Math.Abs(full - 7.4f) < 0.001f, "a full fire deals its full dose");

        // At half heat the heat term is (0.5)*32 = 16, so 1 + 1.6 = 2.6 —
        // well under half of 7.4. That gap IS the curve.
        Assert.True(half < full * 0.5f, "half heat is much less than half damage");

        Assert.True(Math.Abs(FireRules.DamagePerInterval(64f, 64f, 2f) - 13.8f) < 0.001f, "damagescale multiplies only the heat part");
    }

    [Fact]
    public void Ignition()
    {
        // The keyvalue looks like heat and is not: a twentieth of it becomes
        // absorption. A fire keyed 100 soaks 5.
        Assert.True(Math.Abs(FireRules.IgnitionAbsorb(100f) - 5f) < 0.001f, "the ignition point is a twentieth of what was keyed");

        // A cold fire with 30 points of absorption. Heat is consumed at the
        // absorb rate, so 30 points of absorption soaks 10 heat.
        var (through, left) = FireRules.Absorb(5f, 30f, burning: false, selfHeat: false);

        Assert.True(through == 0f, "a small dose does not get through");
        Assert.True(Math.Abs(left - 15f) < 0.001f, "but it eats into the ignition point");

        // A dose big enough to exhaust what is left gets the remainder through.
        var (over, none) = FireRules.Absorb(20f, 30f, burning: false, selfHeat: false);

        Assert.True(Math.Abs(over - 10f) < 0.001f, "a big enough dose gets the rest through");
        Assert.True(none == 0f, "and empties the absorption");
    }

    [Fact]
    public void Feedback()
    {
        // Heat from ANOTHER fire, into one already burning, is scaled right
        // down. This is the whole reason a row of fires does not run away.
        var (other, _) = FireRules.Absorb(10f, 0f, burning: true, selfHeat: false);
        Assert.True(Math.Abs(other - 1f) < 0.001f, "heat from a neighbour is damped");

        // A fire's own growth is not.
        var (self, _) = FireRules.Absorb(10f, 0f, burning: true, selfHeat: true);
        Assert.True(Math.Abs(self - 10f) < 0.001f, "a fire's own growth is not damped");

        // Nor is a neighbour's heat, if this one has not caught yet — that is
        // how one fire lights another in the first place.
        var (cold, _) = FireRules.Absorb(10f, 0f, burning: false, selfHeat: false);
        Assert.True(Math.Abs(cold - 10f) < 0.001f, "nor is it damped into a cold fire");
    }
}
