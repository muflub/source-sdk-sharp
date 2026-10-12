using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>PhysicsFrictionEffect against stock's PhysFrictionEffect (physics_shared.cpp).</summary>
public class PhysicsFrictionEffectTests
{
    private static float E(float speed) => PhysicsFrictionEffect.Mass10Energy(speed);

    [Fact]
    public void Dirt_dusts_above_fifteen()
    {
        Assert.Equal(FrictionEffect.None, PhysicsFrictionEffect.Choose('W', PhysicsFrictionEffect.Dirt, E(15) - 1));
        Assert.Equal(FrictionEffect.Dust, PhysicsFrictionEffect.Choose('W', PhysicsFrictionEffect.Dirt, E(15)));
    }

    [Fact]
    public void Concrete_dusts_above_twenty_eight()
    {
        Assert.Equal(FrictionEffect.None, PhysicsFrictionEffect.Choose('W', PhysicsFrictionEffect.Concrete, E(28) - 1));
        Assert.Equal(FrictionEffect.Dust, PhysicsFrictionEffect.Choose('W', PhysicsFrictionEffect.Concrete, E(28)));
    }

    [Fact]
    public void Metal_on_concrete_sparks_above_fifty_and_dusts_too()
    {
        Assert.Equal(FrictionEffect.Dust,
                     PhysicsFrictionEffect.Choose(PhysicsFrictionEffect.Metal, PhysicsFrictionEffect.Concrete, E(50)));
        Assert.Equal(FrictionEffect.Dust | FrictionEffect.MetalSparks,
                     PhysicsFrictionEffect.Choose(PhysicsFrictionEffect.Metal, PhysicsFrictionEffect.Concrete, E(50) + 1));
    }

    [Fact]
    public void Grate_on_metal_sparks_and_wood_on_metal_does_not()
    {
        Assert.Equal(FrictionEffect.MetalSparks,
                     PhysicsFrictionEffect.Choose(PhysicsFrictionEffect.Grate, PhysicsFrictionEffect.Metal, E(60)));
        Assert.Equal(FrictionEffect.None, PhysicsFrictionEffect.Choose('W', PhysicsFrictionEffect.Metal, E(60)));
    }
}
