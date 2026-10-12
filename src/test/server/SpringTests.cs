using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// phys_spring's one piece of arithmetic, and it is worth a test because
/// getting it wrong is invisible rather than loud.
///
/// A spring given no length in the map rests at whatever separation it was
/// drawn at. Default it to zero instead and the spring yanks both ends together
/// the instant the map loads — which does not look like a broken spring, it
/// looks like two props being thrown at each other by something else entirely.
/// </summary>
public class SpringTests
{
    private static PhysSpring Spawn()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PhysSpring).Assembly);
        registry.Services = Ignoring("Entities.GetOrigin");

        var handle = new EntityHandle(1);
        registry.Spawn(handle, EntityNames.PhysSpring);

        return (PhysSpring)registry.Get(handle)!;
    }

    private static PhysSpring Rested(Vector a, Vector b, float length)
    {
        var spring = Spawn();

        spring.Configure(a, b, length);
        spring.RestAtCurrentSeparation();

        return spring;
    }

    [Fact]
    public void AnUnsetLengthRestsWhereTheMapDrewIt()
        => Assert.True(Rested(new Vector(0f, 0f, 128f), new Vector(0f, 0f, 48f), length: 0f)
                       .RestLength == 80f, "an unset length rests where the map drew it");

    [Fact]
    public void TheRestLengthIsTheDistanceNotOneAxis()
    {
        // Diagonal, because taking one axis rather than the distance is the
        // mistake that survives a vertical test.
        Assert.True(Rested(new Vector(0f, 0f, 0f), new Vector(3f, 4f, 0f), length: 0f)
                    .RestLength == 5f, "the rest length is the distance, not one axis");
    }

    [Fact]
    public void TwoEndsInOnePlaceRestAtZeroNotAtNothing()
    {
        // Both ends in the same place is a map error, but it must not produce a
        // negative or a NaN: the value goes straight to the solver.
        var spring = Rested(new Vector(8f, 8f, 8f), new Vector(8f, 8f, 8f), length: 0f);

        Assert.True(spring.RestLength == 0f && !float.IsNaN(spring.RestLength), "two ends in one place rest at zero, not at nothing");
    }

    [Fact]
    public void AnExplicitLengthIsLeftAlone()
    {
        // An explicit length is the mapper saying the spring starts stretched or
        // compressed, so it must not be recomputed from where it was drawn.
        Assert.True(Rested(new Vector(0f, 0f, 200f), new Vector(0f, 0f, 0f), length: 40f)
                    .RestLength == 40f, "an explicit length is left alone");
    }
}
