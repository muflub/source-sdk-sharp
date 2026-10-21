using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's fluid entries against stock's client CCollisionEvent
/// (FluidStartTouch, DeltaTimeSinceLastFluid, UpdateFluidEvents), lane
/// c6-input, C6 cluster 9.
/// </summary>
public class ClientFluidEventsTests
{
    private const ushort PartOfRagdoll = 0x0008;
    private static readonly EntityHandle A = new(0x0002_0011u), B = new(0x0003_0012u);

    private static nint Word(EntityHandle h) => unchecked((nint)(PhysicsGameData.Tag | (h.Value + PhysicsGameData.Bias)));

    private sealed class Random : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => min;
    }

    private sealed class World : IClientFluidWorld
    {
        public readonly Dictionary<nint, nint> Owners = new();
        public readonly List<ClientSplashEffect> Effects = new();
        public readonly List<(Vector Origin, Vector Angles)> ExtentsAt = new();
        public Vector OwnerOrigin = new(100, 200, 0);
        public nint GameData(nint o) => Owners.TryGetValue(o, out nint d) ? d : 0;
        public ushort GameFlags(nint o) => PartOfRagdoll;
        public Vector Velocity(nint o) => new(0, 0, -100);
        public (Vector Normal, float Dist) SurfacePlane(nint fluid) => (new Vector(0, 0, 1), 10f);
        public (Vector Origin, Vector Angles) OwnerTransform(EntityHandle owner) => (OwnerOrigin, Vector.Zero);
        public Vector CollideExtent(nint o, Vector origin, Vector angles, Vector dir)
        {
            ExtentsAt.Add((origin, angles));
            return origin + dir * 16f;
        }
        public void Splash(in ClientSplashEffect effect) => Effects.Add(effect);
        public int PointContents(Vector point) => 0x20;
        public (Vector Color, float Luminosity) SplashLighting(Vector point) => (Vector.Zero, 1f);
    }

    private static (ClientFluidEvents Fluids, World World, Func<float, float> SetTime) Rig()
    {
        var w = new World();
        w.Owners[1] = Word(A);
        w.Owners[2] = Word(B);
        w.Owners[3] = Word(A);
        float now = 0f;
        var f = new ClientFluidEvents(w, new Random(), () => now);
        return (f, w, t => now = t);
    }

    [Fact]
    public void ANullObjectOrFluidDoesNothing()
    {
        var (f, w, _) = Rig();
        f.FluidStartTouch(0, 9);
        f.FluidStartTouch(1, 0);
        Assert.Empty(w.Effects);
    }

    [Fact]
    public void AnObjectWithNoOwnerDoesNothing()
    {
        var (f, w, _) = Rig();
        f.FluidStartTouch(7, 9);
        Assert.Empty(w.Effects);
    }

    [Fact]
    public void AnEntryPlansFromTheOwnersTransformAndCarriesTheEffectsOut()
    {
        var (f, w, _) = Rig();
        f.FluidStartTouch(1, 9);
        Assert.Equal(5, w.Effects.Count);   // a ripple at the centre and one at each corner
        Assert.All(w.ExtentsAt, e => Assert.Equal(new Vector(100, 200, 0), e.Origin));
        Assert.Equal(4, w.ExtentsAt.Count);
        Assert.Equal(1, f.Splashes);
        Assert.Equal(5, f.EffectsOf(ClientSplashKind.WaterRipple));
        Assert.Equal(0, f.EffectsOf(ClientSplashKind.GunshotSplash));
    }

    [Fact]
    public void TheThrottleCountsFromTheOwnersFirstEntry()
    {
        var (f, w, at) = Rig();
        at(0f);
        f.FluidStartTouch(1, 9);
        at(0.3f);
        f.FluidStartTouch(3, 9);   // another part of the same owner, 0.3 s after the first entry
        Assert.Equal(1, f.Splashes);
        at(0.6f);
        f.FluidStartTouch(1, 9);
        at(0.7f);
        f.FluidStartTouch(1, 9);   // still 0.7 s since the FIRST entry: the time was not refreshed
        Assert.Equal(3, f.Splashes);
    }

    [Fact]
    public void TwoOwnersAreThrottledApart()
    {
        var (f, _, at) = Rig();
        at(0f);
        f.FluidStartTouch(1, 9);
        f.FluidStartTouch(2, 9);
        Assert.Equal(2, f.Splashes);
    }

    [Fact]
    public void AnEntryOlderThanTwoSecondsIsDroppedAtTheFrame()
    {
        var (f, _, at) = Rig();
        at(0f);
        f.FluidStartTouch(1, 9);
        at(2.1f);
        f.Frame();
        at(2.2f);
        f.FluidStartTouch(1, 9);   // a fresh entry: the delta answers two seconds, it splashes
        at(2.4f);
        f.FluidStartTouch(1, 9);   // 0.2 s after the fresh entry: throttled
        Assert.Equal(2, f.Splashes);
    }

    [Fact]
    public void ClearForgetsEveryEntry()
    {
        var (f, _, at) = Rig();
        at(0f);
        f.FluidStartTouch(1, 9);
        f.Clear();
        at(0.1f);
        f.FluidStartTouch(1, 9);
        Assert.Equal(2, f.Splashes);
    }
}
