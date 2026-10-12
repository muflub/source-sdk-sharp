using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_managed B2.1: vphysics' IPhysicsObjectEvent on managed's own
/// table, and the installer that hands it to each level's environment.
/// </summary>
public unsafe class PhysicsObjectEventsTests
{
    private const nint Crate = 0x1000;
    private const nint SdkProp = 0x2000;
    private static readonly EntityHandle Owner = new(0x0001_0005u);

    // A tagged word for Crate (handle + 1), an untagged pointer for SdkProp.
    private static nint GameData(nint physicsObject) => physicsObject switch
    {
        Crate => unchecked((nint)(PhysicsGameData.Tag | (Owner.Value + PhysicsGameData.Bias))),
        SdkProp => unchecked((nint)0x7f00_0000_1234L),
        _ => 0,
    };

    private static (PhysicsObjectEventBinding, List<PhysicsEvent>, Crossings) Sink()
    {
        var posted = new List<PhysicsEvent>();
        var crossings = new Crossings();
        return (new PhysicsObjectEventBinding(crossings, GameData, posted.Add), posted, crossings);
    }

    [Fact]
    public void A_wake_names_the_objects_owner_and_is_queued()
    {
        var (sink, posted, _) = Sink();

        sink.ObjectWake((void*)Crate);

        var ev = Assert.Single(posted);
        Assert.Equal(PhysicsEventKind.Wake, (PhysicsEventKind)ev.KindRaw);
        Assert.Equal(Owner.Value, ev.Entity0);
        Assert.Equal(PhysicsEvent.NoEntity, ev.Entity1);
    }

    [Fact]
    public void A_sleep_is_the_same_with_its_own_kind()
    {
        var (sink, posted, _) = Sink();

        sink.ObjectSleep((void*)Crate);

        Assert.Equal(PhysicsEventKind.Sleep, (PhysicsEventKind)Assert.Single(posted).KindRaw);
    }

    [Fact]
    public void An_object_with_no_managed_owner_is_dropped_as_the_ring_drops_it()
    {
        var (sink, posted, _) = Sink();

        sink.ObjectWake((void*)SdkProp);
        sink.ObjectSleep(null);

        Assert.Empty(posted);
    }

    [Fact]
    public void Each_call_counts_its_crossing()
    {
        var (sink, _, crossings) = Sink();

        sink.ObjectWake((void*)Crate);
        sink.ObjectSleep((void*)Crate);

        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsObjectWake));
        Assert.Equal(1, crossings.Read(CrossingOp.ServerPhysicsObjectSleep));
    }

    [Fact]
    public void A_posted_wake_reaches_its_entity_at_the_next_flush_and_is_counted()
    {
        var registry = new ScriptedEntityRegistry();
        var services = TestServices.Services();
        var router = new PhysicsEventRouter(registry, services.Require<PhysicsDamageQueue>(), services.Require<FluidTouches>(),
                                            services.Require<FrictionSounds>(), services.Require<PlayerShadowRecovery>(),
                                            services.Require<IGameClock>(), services.Require<PhysicsEntitySolver>());

        router.Post(new PhysicsEvent { KindRaw = (uint)PhysicsEventKind.Sleep, Entity0 = Owner.Value, Entity1 = PhysicsEvent.NoEntity });
        router.Flush();

        Assert.Equal(1, router.Posted);
        Assert.True(router.IsAsleep(Owner), "the posted sleep was delivered: the router now reads the owner asleep");
    }

    private sealed class RecordingEnvironment : IVPhysicsEnvironment
    {
        public readonly List<(nint Env, nint Handler)> Set = new();
        public readonly List<(nint Env, nint Handler)> Collision = new();
        public void SetObjectEventHandler(nint environment, nint handler) => Set.Add((environment, handler));
        public void SetCollisionEventHandler(nint environment, nint handler) => Collision.Add((environment, handler));
        public readonly List<(nint Env, nint Handler)> Solver = new();
        public void SetCollisionSolver(nint environment, nint handler) => Solver.Add((environment, handler));
        public float SimulationTimestep(nint environment) => 0.015f;
        public readonly List<(nint Env, nint Handler)> Constraint = new();
        public void SetConstraintEventHandler(nint environment, nint handler) => Constraint.Add((environment, handler));
    }

    private static (PhysicsCallbacks, RecordingEnvironment, RecordingEngine) Installer()
    {
        var engine = TestServices.Fake();
        var env = new RecordingEnvironment();
        return (new PhysicsCallbacks(engine, env) { ObjectEvents = 0x5000 }, env, engine);
    }

    [Fact]
    public void The_table_goes_on_each_environment_once()
    {
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();
        callbacks.EnsureInstalled();

        Assert.Equal(new[] { ((nint)0xE000, (nint)0x5000) }, env.Set);
    }

    [Fact]
    public void The_level_is_installed_before_its_first_step()
    {
        // CPhysicsHook's row creates the environment before SourceSharpManaged's
        // pre-entity row runs, so the first step already calls managed.
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        ((IGameLifecycle)callbacks).OnLevelInitPreEntity("dm_lockdown");

        Assert.Equal(new[] { ((nint)0xE000, (nint)0x5000) }, env.Set);
    }

    [Fact]
    public void The_collision_table_goes_on_beside_the_object_table()
    {
        var (callbacks, env, engine) = Installer();
        callbacks.CollisionEvents = 0x6000;
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();

        Assert.Equal(new[] { ((nint)0xE000, (nint)0x6000) }, env.Collision);
    }

    [Fact]
    public void The_solver_table_goes_on_beside_the_object_table()
    {
        var (callbacks, env, engine) = Installer();
        callbacks.CollisionSolver = 0x7000;
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();

        Assert.Equal(new[] { ((nint)0xE000, (nint)0x7000) }, env.Solver);
    }

    [Fact]
    public void Without_a_solver_table_natives_solver_is_left_in_place()
    {
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();

        Assert.Empty(env.Solver);
    }

    [Fact]
    public void Without_a_collision_table_natives_handler_is_left_in_place()
    {
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();

        Assert.Empty(env.Collision);
    }

    [Fact]
    public void Before_the_table_is_built_nothing_is_installed()
    {
        var (callbacks, env, engine) = Installer();
        callbacks.ObjectEvents = 0;
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);

        callbacks.EnsureInstalled();

        Assert.Empty(env.Set);
    }

    [Fact]
    public void Between_levels_there_is_nothing_to_install_on()
    {
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0);

        callbacks.EnsureInstalled();

        Assert.Empty(env.Set);
    }

    [Fact]
    public void A_new_level_at_the_old_address_is_installed_again()
    {
        var (callbacks, env, engine) = Installer();
        engine.Returns("EntityPhysics.EnvironmentPointer", (nint)0xE000);
        callbacks.EnsureInstalled();

        callbacks.LevelShutdown();
        callbacks.EnsureInstalled();

        Assert.Equal(2, env.Set.Count);
    }
}
