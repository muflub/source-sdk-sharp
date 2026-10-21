using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's PhysicsSplash against stock's (game/client/physics.cpp), lane
/// c6-input, C6 cluster 9.
/// </summary>
public class ClientSplashPlanTests
{
    private const ushort PartOfRagdoll = 0x0008;
    private const int ContentsWater = 0x20, ContentsSlime = 0x10;

    /// <summary>Hands out min + 1/4 of the range, and records every range asked.</summary>
    private sealed class Random : IUniformRandom
    {
        public readonly List<(float Min, float Max)> Asked = new();
        public float RandomFloat(float min, float max)
        {
            Asked.Add((min, max));
            return min + (max - min) * 0.25f;
        }
        public int RandomInt(int min, int max) => min;
    }

    private sealed class World(int contents) : IClientSplashWorld
    {
        public readonly List<Vector> ContentsAt = new();
        public readonly List<Vector> LightingAt = new();
        public int PointContents(Vector point)
        {
            ContentsAt.Add(point);
            return contents;
        }
        public (Vector Color, float Luminosity) SplashLighting(Vector point)
        {
            LightingAt.Add(point);
            return (new Vector(0.5f, 0.25f, 0.125f), 0.75f);
        }
    }

    // An axis-aligned body: identity axes, a 32 x 16 footprint centred on (100, 200),
    // the water's surface at z = 10.
    private static readonly Vector[] Axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
    private static readonly Vector Up = new(0, 0, 1);

    private static Vector Extent(Vector dir)
        => new(100 + (dir.X > 0 ? 16 : dir.X < 0 ? -16 : 0), 200 + (dir.Y > 0 ? 8 : dir.Y < 0 ? -8 : 0), 0);

    private static List<ClientSplashEffect> Plan(float speed, int contents, out World world, out Random random,
                                                 ushort flags = PartOfRagdoll)
    {
        world = new World(contents);
        random = new Random();
        return ClientSplashPlan.Plan(flags, new Vector(0, 0, -speed), Up, 10f, Axes, Extent, world, random);
    }

    [Fact]
    public void APieceThatIsNoRagdollPartMakesNothing()
    {
        Assert.Empty(Plan(200f, ContentsWater, out _, out _, flags: 0));
    }

    [Fact]
    public void SlowerThan25MakesNothing()
    {
        Assert.Empty(Plan(24.9f, ContentsWater, out var world, out _));
        Assert.Empty(world.ContentsAt);
    }

    [Fact]
    public void TheContentsAreReadTwoUnitsUnderTheFootprintsCentre()
    {
        Plan(100f, ContentsWater, out var world, out _);
        Assert.Equal(new Vector(100, 200, 8), Assert.Single(world.ContentsAt));
    }

    [Fact]
    public void TheLightingIsReadEightUnitsAboveTheCentreAlongTheNormal()
    {
        Plan(100f, ContentsWater, out var world, out _);
        Assert.Equal(new Vector(100, 200, 18), Assert.Single(world.LightingAt));
    }

    [Fact]
    public void InSlimeTheLightingIsNotRead()
    {
        Plan(100f, ContentsSlime, out var world, out _);
        Assert.Empty(world.LightingAt);
    }

    [Fact]
    public void ASlowBodyRipplesAtTheCentreAndAtEachCorner()
    {
        var fx = Plan(100f, ContentsWater, out _, out _);
        Assert.Equal(5, fx.Count);
        Assert.All(fx, e => Assert.Equal(ClientSplashKind.WaterRipple, e.Kind));
        var centre = fx[0];
        Assert.Equal(new Vector(100, 200, 10), centre.Origin);
        Assert.Equal(1.5f, centre.Scale);
        Assert.Equal(1.5f, centre.Lifetime);
        Assert.Equal(0.75f, centre.Alpha);
        Assert.Equal(new Vector(0.5f, 0.25f, 0.125f), centre.Color);
        // Corner 0 is centre - axis0 - axis1 = (84, 192, 10), plus RandomVector(-32, 32)'s x and y
        // (each -32 + 64/4 = -16); scale RandomFloat(0.25, 0.5) = 0.3125, lifetime the
        // luminosity, alpha RandomFloat(0.5, 1.0) = 0.625.
        Assert.Equal(new Vector(68, 176, 10), fx[1].Origin);
        Assert.Equal(0.3125f, fx[1].Scale);
        Assert.Equal(0.75f, fx[1].Lifetime);
        Assert.Equal(0.625f, fx[1].Alpha);
        Assert.Equal(new Vector(116 - 16, 192 - 16, 10), fx[2].Origin);
        Assert.Equal(new Vector(116 - 16, 208 - 16, 10), fx[3].Origin);
        Assert.Equal(new Vector(84 - 16, 208 - 16, 10), fx[4].Origin);
    }

    [Fact]
    public void AFastBodySplashesFiveTimesAllAtTheCentre()
    {
        var fx = Plan(151f, ContentsWater, out _, out _);
        Assert.Equal(5, fx.Count);
        Assert.All(fx, e => Assert.Equal(ClientSplashKind.GunshotSplash, e.Kind));
        Assert.All(fx, e => Assert.Equal(new Vector(100, 200, 10), e.Origin));
        Assert.All(fx, e => Assert.Equal(Up, e.Normal));
        Assert.Equal(8.5f, fx[0].Scale);   // RandomFloat( 8, 10 )
        Assert.Equal(4.5f, fx[1].Scale);   // RandomFloat( 4, 6 )
    }

    [Fact]
    public void AFastBodyInSlimeSplashesSlime()
    {
        var fx = Plan(151f, ContentsSlime, out _, out _);
        Assert.Equal(5, fx.Count);
        Assert.All(fx, e => Assert.Equal(ClientSplashKind.GunshotSlimeSplash, e.Kind));
    }

    [Fact]
    public void ASlowBodyInSlimeMakesNothingButStillDrawsTheCorners()
    {
        var fx = Plan(100f, ContentsSlime, out _, out var random);
        Assert.Empty(fx);
        Assert.Equal(12, random.Asked.Count);   // four RandomVector( -32, 32 ), three draws each
    }

    [Fact]
    public void TheDrawsAreTakenInStocksOrder()
    {
        Plan(100f, ContentsWater, out _, out var random);
        (float, float) v = (-32f, 32f), s = (0.25f, 0.5f), a = (0.5f, 1.0f);
        var corner = new[] { v, v, v, s, a };
        Assert.Equal(corner.Concat(corner).Concat(corner).Concat(corner), random.Asked);

        Plan(151f, ContentsWater, out _, out random);
        var fast = new[] { v, v, v, (4f, 6f) };
        Assert.Equal(new[] { (8f, 10f) }.Concat(fast).Concat(fast).Concat(fast).Concat(fast), random.Asked);
    }
}
