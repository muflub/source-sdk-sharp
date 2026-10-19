using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity I2 (8g): which entities are touching fluid, and which
/// of those float. Stock sets EFL_TOUCHING_FLUID in CCollisionEvent::
/// FluidStartTouch and clears it in FluidEndTouch; CBaseEntity::IsFloating is
/// that flag and a physics material less dense than water (density &lt; 1000),
/// and a floating object feels impacts from anything but the world at three
/// times the energy (CalculatePhysicsImpactDamage).
/// </summary>
public class FluidTouchesTests
{
    private sealed class Densities : ISurfaceDensity
    {
        public float DensityOf(int surfaceProps) => surfaceProps switch { 1 => 500f, 2 => 1000f, _ => 2000f };
    }

    private static readonly EntityHandle Crate = new(41);
    private const int Wood = 1;
    private const int Water = 2;

    [Fact]
    public void An_entity_that_enters_fluid_is_touching_it()
    {
        var fluid = new FluidTouches(new Densities());

        fluid.Start(Crate, Wood);

        Assert.True(fluid.IsTouching(Crate));
    }

    [Fact]
    public void Leaving_the_fluid_clears_it()
    {
        var fluid = new FluidTouches(new Densities());

        fluid.Start(Crate, Wood);
        fluid.End(Crate);

        Assert.False(fluid.IsTouching(Crate));
        Assert.False(fluid.IsFloating(Crate));
    }

    [Fact]
    public void A_material_lighter_than_water_in_fluid_floats()
    {
        var fluid = new FluidTouches(new Densities());

        fluid.Start(Crate, Wood);

        Assert.True(fluid.IsFloating(Crate));
    }

    [Fact]
    public void A_material_as_dense_as_water_does_not_float()
    {
        // Stock's test is density < 1000, strictly.
        var fluid = new FluidTouches(new Densities());

        fluid.Start(Crate, Water);

        Assert.False(fluid.IsFloating(Crate));
    }

    [Fact]
    public void Nothing_floats_out_of_fluid()
    {
        var fluid = new FluidTouches(new Densities());

        Assert.False(fluid.IsFloating(Crate));
    }
}
