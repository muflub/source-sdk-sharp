using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsFluidEvents against stock's FluidStartTouch / FluidEndTouch
/// (physics.cpp): a 40-unit, 100 kg crate falling at 200 into still water
/// (plane z = 0, normal up, density 1), step 0.015.
/// </summary>
public class PhysicsFluidEventsTests
{
    private const nint CrateObject = 0x51, SdkObject = 0x52, Water = 0x70;
    private static readonly EntityHandle Crate = new(0x0001_0011u);

    private sealed class Objects : IVPhysicsObjects
    {
        public Vector Linear = new(0f, 0f, -200f);
        public Vector Angular = new(0f, 0f, 90f);
        public readonly List<(Vector Linear, Vector Angular)> Added = new();
        public void RecheckContactPoints(nint physicsObject) { }
        public nint GameData(nint physicsObject) => physicsObject == CrateObject
            ? unchecked((nint)(PhysicsGameData.Tag | (Crate.Value + PhysicsGameData.Bias))) : 0;
        public int ContactsWith(nint physicsObject, EntityHandle other) => 0;
        public Vector ContactPoint(nint collisionData) => Vector.Zero;
        public Vector VelocityAtPoint(nint physicsObject, Vector worldPoint) => Vector.Zero;
        public (Vector Linear, Vector Angular) Velocity(nint physicsObject) => (Linear, Angular);
        public void AddVelocity(nint physicsObject, Vector linear, Vector angular) => Added.Add((linear, angular));
        public float LinearDrag(nint physicsObject, Vector direction) => 2f;
        public float AngularDrag(nint physicsObject, Vector axis) => 4f;
        public float Mass(nint physicsObject) => 100f;
        public ushort GameFlags(nint physicsObject) => 0;
        public int MaterialIndex(nint physicsObject) => 17;
        public (Vector X, Vector Y, Vector Z) Axes(nint physicsObject)
            => (new Vector(1f, 0f, 0f), new Vector(0f, 1f, 0f), new Vector(0f, 0f, 1f));
        public void SetVelocity(nint physicsObject, Vector linear, Vector angular) { }
        public bool IsStatic(nint physicsObject) => false;
        public bool IsMoveable(nint physicsObject) => false;
        public ushort CallbackFlags(nint physicsObject) => 0;
        public Vector SurfaceNormal(nint collisionData) => Vector.Zero;
    }

    private sealed class Fluids : IVPhysicsFluids
    {
        public (Vector Normal, float Dist) SurfacePlane(nint fluid) => (new Vector(0f, 0f, 1f), 0f);
        public float Density(nint fluid) => 1f;
    }

    private sealed class Environment : IVPhysicsEnvironment
    {
        public void SetObjectEventHandler(nint environment, nint handler) { }
        public void SetCollisionEventHandler(nint environment, nint handler) { }
        public void SetCollisionSolver(nint environment, nint handler) { }
        public float SimulationTimestep(nint environment) => 0.015f;
        public void SetConstraintEventHandler(nint environment, nint handler) { }
    }

    private sealed class Temps : TempEntsDouble
    {
        public readonly List<string> Effects = new();
        public override void DispatchEffect(TeFilter filter, float delay, string effectName, in TeEffectData data)
            => Effects.Add(effectName);
    }

    private sealed class World
    {
        public readonly Objects Objects = new();
        public readonly Temps Temps = new();
        public readonly List<PhysicsEvent> Posted = new();
        public readonly RecordingEngine Engine = Fake();
        public float Now;
        public readonly PhysicsFluidEvents Events;

        public World()
        {
            Engine.Strict = false;
            Engine.ReturnsEachCall("Clock.Now", () => Now);
            Engine.Returns("Host.RandomFloat", 0f);
            Engine.ReturnsEachCall("EntityPhysics.CollideExtent", () =>
            {
                Vector dir = Engine.Calls[^1].ArgAs<Vector>(1);
                return new Vector(MathF.Sign(dir.X) * 20f, MathF.Sign(dir.Y) * 20f, MathF.Sign(dir.Z) * 20f);
            });
            Events = new PhysicsFluidEvents(Objects, new Fluids(), new Environment(), Engine, Temps, Engine, Engine,
                                            Engine, Posted.Add);
        }
    }

    [Fact]
    public void An_entry_touches_fluid_splashes_and_is_dragged()
    {
        var world = new World();

        world.Events.FluidStart(CrateObject, Water);

        var touched = Assert.Single(world.Posted);
        Assert.Equal(PhysicsEventKind.FluidStart, (PhysicsEventKind)touched.KindRaw);
        Assert.Equal(Crate.Value, touched.Entity0);
        Assert.Equal(17u, touched.SurfaceProps0);
        Assert.Equal(new[] { PhysicsSplashPlan.Splash }, world.Temps.Effects);

        // linear: 0.5 x dot(down, down) 1 x drag 2 x (1 x 0.015) = 0.015 of the velocity, against it;
        // angular: 0.25 x drag 4 x 0.015 = 0.015 of the spin, against it.
        var (linear, angular) = Assert.Single(world.Objects.Added);
        Assert.Equal(200f * 0.015f, linear.Z, 3);
        Assert.Equal(-90f * 0.015f, angular.Z, 3);
    }

    [Fact]
    public void A_quick_reentry_still_touches_fluid_but_neither_splashes_nor_drags()
    {
        // Stock sets EFL_TOUCHING_FLUID before the half-second check; native
        // returned first and left the entity dry.
        var world = new World();
        world.Events.FluidStart(CrateObject, Water);
        world.Now = 0.2f;

        world.Events.FluidStart(CrateObject, Water);

        Assert.Equal(2, world.Posted.Count(e => (PhysicsEventKind)e.KindRaw == PhysicsEventKind.FluidStart));
        Assert.Single(world.Temps.Effects);
        Assert.Single(world.Objects.Added);
    }

    [Fact]
    public void Leaving_late_enough_splashes_and_ends_the_touch()
    {
        var world = new World();
        world.Events.FluidStart(CrateObject, Water);
        world.Now = 1f;

        world.Events.FluidEnd(CrateObject, Water);

        Assert.Equal(PhysicsEventKind.FluidEnd, (PhysicsEventKind)world.Posted[^1].KindRaw);
        Assert.Equal(2, world.Temps.Effects.Count);
    }

    [Fact]
    public void After_two_seconds_the_stamp_restarts_from_the_next_entry()
    {
        // Stamped at 0; at 2.5 it has expired (UpdateFluidEvents), so that
        // entry is stamped afresh and splashes; 0.2 later is a quick re-entry
        // against the NEW stamp. Without the expiry it would splash again.
        var world = new World();
        world.Events.FluidStart(CrateObject, Water);
        world.Now = 2.5f;
        world.Events.FluidStart(CrateObject, Water);
        world.Now = 2.7f;

        world.Events.FluidStart(CrateObject, Water);

        Assert.Equal(2, world.Temps.Effects.Count);
    }

    [Fact]
    public void An_ownerless_object_does_nothing()
    {
        var world = new World();

        world.Events.FluidStart(SdkObject, Water);
        world.Events.FluidEnd(SdkObject, Water);

        Assert.Empty(world.Posted);
        Assert.Empty(world.Temps.Effects);
    }
}
