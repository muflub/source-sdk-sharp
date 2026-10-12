using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's CCollisionEvent::Friction against stock's (game/client/physics.cpp),
/// lane c6-input, C6 cluster 9.
/// </summary>
public class ClientFrictionTests
{
    private static readonly EntityHandle Owner = new(0x0002_0011u);
    private const nint Owned = 0x51, Unowned = 0x52, Data = 0x99;

    private sealed class Objects : IVPhysicsObjects
    {
        public nint GameData(nint o) => o == Owned ? unchecked((nint)(PhysicsGameData.Tag | (Owner.Value + PhysicsGameData.Bias))) : 0;
        public Vector ContactPoint(nint collisionData) => collisionData == Data ? new Vector(1, 2, 3) : Vector.Zero;
        public Vector VelocityAtPoint(nint o, Vector worldPoint) => worldPoint.Equals(new Vector(1, 2, 3)) ? new Vector(30, 40, 0) : Vector.Zero;
        public void RecheckContactPoints(nint o) { }
        public int ContactsWith(nint o, EntityHandle other) => 0;
        public (Vector Linear, Vector Angular) Velocity(nint o) => default;
        public void AddVelocity(nint o, Vector linear, Vector angular) { }
        public float LinearDrag(nint o, Vector direction) => 0f;
        public float AngularDrag(nint o, Vector axis) => 0f;
        public float Mass(nint o) => 0f;
        public ushort GameFlags(nint o) => 0;
        public int MaterialIndex(nint o) => 0;
        public (Vector X, Vector Y, Vector Z) Axes(nint o) => default;
        public void SetVelocity(nint o, Vector linear, Vector angular) { }
        public bool IsStatic(nint o) => false;
        public bool IsMoveable(nint o) => true;
        public ushort CallbackFlags(nint o) => 0;
        public Vector SurfaceNormal(nint collisionData) => Vector.Zero;
    }

    private sealed class Scrapes : IFrictionScrapes
    {
        public readonly List<string> Seen = new();
        public void Scrape(EntityHandle owner, float energy, int s, int h, float now, Vector point, Vector velocity)
            => Seen.Add($"scrape {owner.Value:x} {energy} {s} {h} {now} {point} {velocity}");
        public void Effect(float energy, int s, int h, Vector point, Vector velocity)
            => Seen.Add($"effect {energy} {s} {h} {point} {velocity}");
    }

    private static (ClientFriction Friction, Scrapes Scrapes) Rig()
    {
        var scrapes = new Scrapes();
        return (new ClientFriction(new Objects(), scrapes, () => 7.5f), scrapes);
    }

    [Fact]
    public void UnderFiveHundredthsOfAnEnergyUnitNothingHappens()
    {
        var (f, s) = Rig();
        f.Friction(Owned, 0.049f, 3, 4, Data);
        f.Friction(Unowned, 0.049f, 3, 4, Data);
        Assert.Empty(s.Seen);
    }

    [Fact]
    public void WithNoSurfaceNothingHappens()
    {
        var (f, s) = Rig();
        f.Friction(Owned, 500f, -1, 4, Data);
        Assert.Empty(s.Seen);
    }

    [Fact]
    public void AnOwnedObjectScrapesAtTheContactWithItsVelocityThere()
    {
        var (f, s) = Rig();
        f.Friction(Owned, 500f, 3, 4, Data);
        Assert.Equal(new[] { $"scrape {Owner.Value:x} 500 3 4 7.5 {new Vector(1, 2, 3)} {new Vector(30, 40, 0)}" }, s.Seen);
    }

    [Fact]
    public void AnObjectWithNoOwnerStillThrowsTheEffect()
    {
        var (f, s) = Rig();
        f.Friction(Unowned, 500f, 3, 4, Data);
        Assert.Equal(new[] { $"effect 500 3 4 {new Vector(1, 2, 3)} {new Vector(30, 40, 0)}" }, s.Seen);
    }
}
