using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arithmetic inside the emitters.
///
/// What can be tested here is exactly what does not need the world: how far a
/// blast reaches, how much of it survives the distance, how hard a push field
/// shoves. Which entities are in range and whether a wall is in the way are
/// engine questions, and the parts of these classes that ask them are not
/// covered — that is what the sandbox map is for.
///
/// Worth testing at all because every one of these is a curve a mapper tunes
/// against, and a curve that is subtly wrong reads as "this entity feels off"
/// rather than as a failure.
/// </summary>
public class EmitterTests
{

    private static bool Near(float a, float b, float tolerance = 0.001f)
        => MathF.Abs(a - b) <= tolerance;

    [Fact]
    public void Remapping()
    {
        // The building block under all three falloffs. Source's RemapValClamped
        // maps a value from one range onto another and clamps it, and the two
        // ends are the cases that matter because every falloff sits at one.
        Assert.True(Near(PhysicsEmitter.Remap(0f, 0f, 10f, 0f, 1f), 0f), "a value at the start of the range maps to the start");

        Assert.True(Near(PhysicsEmitter.Remap(10f, 0f, 10f, 0f, 1f), 1f), "a value at the end maps to the end");

        Assert.True(Near(PhysicsEmitter.Remap(5f, 0f, 10f, 0f, 1f), 0.5f), "a value halfway maps halfway");

        // Clamped at both ends, or a distant entity would come out with a
        // negative push and be pulled in rather than shoved away.
        Assert.True(Near(PhysicsEmitter.Remap(20f, 0f, 10f, 0f, 1f), 1f), "past the end is clamped");
        Assert.True(Near(PhysicsEmitter.Remap(-5f, 0f, 10f, 0f, 1f), 0f), "before the start is clamped");

        // The ranges the emitters use run DOWNWARD — from the radius in to a
        // fraction of it — so this direction is the one actually exercised.
        Assert.True(Near(PhysicsEmitter.Remap(100f, 100f, 25f, 0f, 1f), 0f), "a descending range still maps its start");

        Assert.True(Near(PhysicsEmitter.Remap(25f, 100f, 25f, 0f, 1f), 1f), "a descending range still maps its end");

        // A radius of zero makes the range degenerate. Dividing would give NaN,
        // which would propagate into a force and stop the physics object dead.
        Assert.True(!float.IsNaN(PhysicsEmitter.Remap(0f, 5f, 5f, 0f, 1f)), "a zero-width range does not divide by zero");
    }

    [Fact]
    public void BlastFalloff()
    {
        // At the centre a blast does its full magnitude.
        Assert.True(Near(PhysExplosion.DamageAt(100f, 0f), 100f), "full magnitude at the centre");

        // 1/2.5 per unit: 100 units out costs 40 points, whatever the radius is.
        Assert.True(Near(PhysExplosion.DamageAt(100f, 100f), 60f), "damage falls off by distance, not by radius");

        Assert.True(Near(PhysExplosion.DamageAt(100f, 200f), 20f), "twice as far costs twice as much");

        // The floor is the interesting part. Caught in a blast at all means
        // taking something from it, so a distant entity is still moved rather
        // than being handed a zero or a negative that would pull it inward.
        Assert.True(Near(PhysExplosion.DamageAt(100f, 1000f), 1f), "damage never falls below one");
        Assert.True(PhysExplosion.DamageAt(10f, 5000f) > 0f, "damage never goes negative");
    }

    [Fact]
    public void BlastRadius()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysExplosion).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(1);
        registry.KeyValue(handle, EntityNames.EnvPhysexplosion, "magnitude", "100");

        var blast = (PhysExplosion)registry.Get(handle)!;

        // No radius given: the blast reaches as far as its damage does, at the
        // same 2.5 units per point combat uses. A mapper who sets only a
        // magnitude still gets a bounded explosion rather than a map-wide one.
        Assert.True(Near(blast.EffectiveRadius, 250f), "an unset radius is derived from the magnitude");

        var explicitHandle = new EntityHandle(2);
        registry.KeyValue(explicitHandle, EntityNames.EnvPhysexplosion, "magnitude", "100");
        registry.KeyValue(explicitHandle, EntityNames.EnvPhysexplosion, "radius", "64");

        var bounded = (PhysExplosion)registry.Get(explicitHandle)!;
        Assert.True(Near(bounded.EffectiveRadius, 64f), "an explicit radius wins");

        // The player curve is relative to the radius, unlike the damage curve.
        // Both live in the same entity, which is exactly why it is worth having
        // a test say so out loud.
        Assert.True(Near(PhysExplosion.PlayerFalloff(0f, 200f), 1f), "a player at the centre takes the full push");

        Assert.True(Near(PhysExplosion.PlayerFalloff(200f, 200f), 0f), "a player at the edge takes none");

        Assert.True(Near(PhysExplosion.PlayerFalloff(150f, 200f), 1f), "a player inside three quarters takes the full push");

        Assert.True(PhysExplosion.PlayerFalloff(175f, 200f) is > 0f and < 1f, "a player between the two tails off");
    }

    [Fact]
    public void PushFalloff()
    {
        // A much steeper curve than the blast's: full strength for the inner
        // quarter, then away to nothing. That is what makes a push field feel
        // like a fan rather than an explosion.
        Assert.True(Near(PointPush.Falloff(0f, 200f), 1f), "full strength at the centre");
        Assert.True(Near(PointPush.Falloff(50f, 200f), 1f), "full strength within a quarter of the radius");
        Assert.True(Near(PointPush.Falloff(200f, 200f), 0f), "nothing at the edge");
        Assert.True(Near(PointPush.Falloff(125f, 200f), 0.5f), "half strength halfway down the tail");

        // The push is proportional to mass, so heavy and light accelerate the
        // same. Doubling the mass doubles the impulse.
        float light = PointPush.Impulse(magnitude: 10f, falloff: 1f, mass: 50f, frameTime: 0.015f);
        float heavy = PointPush.Impulse(magnitude: 10f, falloff: 1f, mass: 100f, frameTime: 0.015f);
        Assert.True(Near(heavy, light * 2f), "twice the mass takes twice the impulse");

        // And proportional to the server frame time, as stock's
        // gpGlobals->frametime (point_push's think passes it; PointPushStockTests).
        float shortTick = PointPush.Impulse(10f, 1f, 100f, 0.015f);
        float longTick = PointPush.Impulse(10f, 1f, 100f, 0.030f);
        Assert.True(Near(longTick, shortTick * 2f), "twice the frame time takes twice the impulse");

        Assert.True(Near(PointPush.Impulse(10f, 0f, 100f, 0.05f), 0f), "no falloff means no impulse");
    }

    [Fact]
    public void ImpactFalloff()
    {
        // The flags below are named from the entity's own enum. This test used
        // to declare its own "const int NoFalloff = 1" pair -- a second copy of
        // the numbers, free to drift away from the ones the entity reads.
        //
        // Falloff is by trace fraction: something hit immediately takes the
        // full magnitude, something at the far end of the line takes nothing.
        Assert.True(Near(PhysImpact.Force(600f, fraction: 0f, spawnFlags: 0), 600f), "a contact hit takes the full magnitude");

        Assert.True(Near(PhysImpact.Force(600f, 0.5f, 0), 300f), "halfway along the line takes half");

        Assert.True(Near(PhysImpact.Force(600f, 1f, 0), 0f), "the far end takes nothing");

        // Both flags turn the curve off, and the second one has to: over the
        // 56,000 units an infinite trace covers, every real fraction is close
        // enough to 1 that the impact would do nothing at all.
        Assert.True(Near(PhysImpact.Force(600f, 0.9f, PhysImpact.SpawnFlag.NoFalloff), 600f), "the no-falloff flag keeps the full magnitude");

        Assert.True(Near(PhysImpact.Force(600f, 0.999f, PhysImpact.SpawnFlag.InfiniteLength), 600f), "an infinite-length trace also ignores falloff");
    }
}
