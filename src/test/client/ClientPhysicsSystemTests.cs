using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Physics;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's physics system (stock's game/client/physics.cpp CPhysicsSystem
/// and PhysicsLevelInit), held to stock's calls and their order: one recorded
/// line per vphysics call, ragdoll ask and step-tail ask.
/// </summary>
public class ClientPhysicsSystemTests
{
    private const nint Env = 0xE00;
    private const nint Events = 0xA10;
    private const nint Solver = 0xA20;

    private readonly List<string> _calls = [];
    private readonly ClientPhysicsEnvironment _environment = new();
    private readonly Clock _clock = new();
    private nint _made = Env;

    private sealed class Clock : IClientPhysicsClock
    {
        public float FrameTime { get; set; } = 0.016f;
        public float IntervalPerTick { get; set; } = 0.015f;
        public float Gravity { get; set; } = 600f;
        public float TimeScale { get; set; } = 1f;
    }

    private sealed class Calls(ClientPhysicsSystemTests t) : IClientPhysicsLevelCalls
    {
        public nint CreateEnvironment() { t._calls.Add("CreateEnvironment"); return t._made; }
        public void DestroyEnvironment(nint e) => t._calls.Add($"DestroyEnvironment {e:x}");
        public void DestroyAllCollisionSets() => t._calls.Add("DestroyAllCollisionSets");
        public void SetGravity(nint e, Vector g) => t._calls.Add($"SetGravity {e:x} {g.X} {g.Y} {g.Z}");
        public void SetSimulationTimestep(nint e, float s) => t._calls.Add($"SetSimulationTimestep {e:x} {s}");
        public void SetCollisionEventHandler(nint e, nint h) => t._calls.Add($"SetCollisionEventHandler {e:x} {h:x}");
        public void SetCollisionSolver(nint e, nint s) => t._calls.Add($"SetCollisionSolver {e:x} {s:x}");
        public void SetQuickDelete(nint e, bool q) => t._calls.Add($"SetQuickDelete {e:x} {q}");
        public void Simulate(nint e, float dt) => t._calls.Add($"Simulate {e:x} {dt}");
        public void ResetSimulationClock(nint e) => t._calls.Add($"ResetSimulationClock {e:x}");
    }

    private sealed class World(ClientPhysicsSystemTests t) : IClientPhysicsWorld
    {
        public nint CreateWorld() { t._calls.Add("CreateWorld"); return 0x3D; }
        public void CreateStaticProps() => t._calls.Add("CreateStaticProps");
        public int ParseSurfaceData() { t._calls.Add("ParseSurfaceData"); return 1; }
        public int PrecacheSurfaceSounds() { t._calls.Add("PrecacheSurfaceSounds"); return 1; }
        public SolidKeyDefaults? Solids => null;
    }

    private sealed class Ragdolls(ClientPhysicsSystemTests t) : IClientRagdollStep
    {
        public int StepPoses() { t._calls.Add("StepPoses"); return 0; }
        public int DestroyAll() { t._calls.Add("DestroyAll"); return 0; }
    }

    private sealed class Tail(ClientPhysicsSystemTests t) : IClientPhysicsStepTail
    {
        public void UpdateFrictionSounds() => t._calls.Add("UpdateFrictionSounds");
        public void UpdateFluidEvents() => t._calls.Add("UpdateFluidEvents");
        public void PlayImpactSounds() => t._calls.Add("PlayImpactSounds");
        public void RemoveAllImpactSounds() => t._calls.Add("RemoveAllImpactSounds");
    }

    private ClientPhysicsSystem System(bool tables = true)
    {
        var system = new ClientPhysicsSystem(new Calls(this), _clock, _environment, () => new World(this),
                                             () => new Ragdolls(this), new Tail(this), new RecordingLog());
        if (tables)
        {
            system.CollisionEvents = Events;
            system.CollisionSolver = Solver;
        }
        return system;
    }

    [Fact]
    public void TheLevelsEnvironmentIsMadeAsStockMadeIt()
    {
        var system = System();
        system.LevelInitPostEntity();

        // PhysicsLevelInit: gravity down at the game's gravity, the tick's step,
        // the handler and the solver, then the world, then the static props.
        Assert.Equal(new[]
        {
            "CreateEnvironment", "SetGravity e00 0 0 -600", "SetSimulationTimestep e00 0.015",
            "SetCollisionEventHandler e00 a10", "SetCollisionSolver e00 a20",
            "CreateWorld", "CreateStaticProps",
        }, _calls);
        Assert.Equal(Env, _environment.Environment());
        Assert.Equal((nint)0x3D, system.World);
        Assert.True(system.Installed);
    }

    [Fact]
    public void NoEnvironmentAsksNothingFurther()
    {
        _made = 0;
        var system = System();
        system.LevelInitPostEntity();

        Assert.Equal(new[] { "CreateEnvironment" }, _calls);
        Assert.Equal((nint)0, _environment.Environment());
    }

    [Fact]
    public void ATableNotYetBuiltIsNotInstalled()
    {
        var system = System(tables: false);
        system.LevelInitPostEntity();

        Assert.DoesNotContain(_calls, c => c.StartsWith("SetCollision", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePreEntityInitForgetsTheQueuedSoundsThenPrecaches()
    {
        System().LevelInitPreEntity();
        Assert.Equal(new[] { "RemoveAllImpactSounds", "PrecacheSurfaceSounds" }, _calls);
    }

    [Fact]
    public void TheStepScalesTheFrameAndRunsTheTailInStocksOrder()
    {
        var system = System();
        system.LevelInitPostEntity();
        _calls.Clear();
        _clock.FrameTime = 0.02f;
        _clock.TimeScale = 0.5f;

        system.Simulate();

        Assert.Equal(new[]
        {
            "Simulate e00 0.01", "StepPoses", "UpdateFrictionSounds", "UpdateFluidEvents", "PlayImpactSounds",
        }, _calls);
        Assert.Equal(1, system.Steps);
    }

    [Fact]
    public void WithNoEnvironmentTheStepOnlyPlaysTheSounds()
    {
        var system = System();
        system.Simulate();

        Assert.Equal(new[] { "PlayImpactSounds" }, _calls);
        Assert.Equal(0, system.Steps);
    }

    [Fact]
    public void ThePreEntityShutdownAsksForAQuickDelete()
    {
        var system = System();
        system.LevelShutdownPreEntity();
        Assert.Empty(_calls);

        system.LevelInitPostEntity();
        _calls.Clear();
        system.LevelShutdownPreEntity();
        Assert.Equal(new[] { "SetQuickDelete e00 True" }, _calls);
    }

    [Fact]
    public void TheBodiesGoBeforeTheEnvironmentAndTheSetsAfterIt()
    {
        var system = System();
        system.LevelInitPostEntity();
        _calls.Clear();

        system.LevelShutdownPostEntity();

        Assert.Equal(new[] { "DestroyAll", "DestroyEnvironment e00", "DestroyAllCollisionSets" }, _calls);
        Assert.Equal((nint)0, _environment.Environment());
        Assert.Equal((nint)0, system.World);

        _calls.Clear();
        system.Simulate();
        Assert.Equal(new[] { "PlayImpactSounds" }, _calls);
    }

    [Fact]
    public void TheResetRestartsTheClockOnlyInALevel()
    {
        var system = System();
        system.Reset();
        Assert.Empty(_calls);

        system.LevelInitPostEntity();
        _calls.Clear();
        system.Reset();
        Assert.Equal(new[] { "ResetSimulationClock e00" }, _calls);
    }

    [Fact]
    public void AReloadAdoptsTheLevelAndInstallsItsOwnTablesBeforeItsFirstStep()
    {
        var old = System();
        old.LevelInitPostEntity();
        var writer = new ReloadWriter();
        old.Save(writer);

        var env = new ClientPhysicsEnvironment();
        var fresh = new ClientPhysicsSystem(new Calls(this), _clock, env, () => new World(this),
                                            () => new Ragdolls(this), new Tail(this), new RecordingLog())
        {
            CollisionEvents = 0xB10,
            CollisionSolver = 0xB20,
        };
        fresh.Restore(new ReloadReader(writer.ToArray()));
        _calls.Clear();

        Assert.Equal(Env, env.Environment());
        Assert.Equal((nint)0x3D, fresh.World);
        Assert.False(fresh.Installed);

        fresh.Simulate();
        fresh.Simulate();

        Assert.Equal(2, _calls.Count(c => c.StartsWith("Simulate", StringComparison.Ordinal)));
        Assert.Equal(new[] { "SetCollisionEventHandler e00 b10", "SetCollisionSolver e00 b20" },
                     _calls.Where(c => c.StartsWith("SetCollision", StringComparison.Ordinal)));
        Assert.True(_calls.IndexOf("SetCollisionSolver e00 b20") < _calls.IndexOf("Simulate e00 0.016"));
    }

    // ClientPhysicsCallbacks' cases, carried here with the installer they held
    // (the system installs the tables itself).

    [Fact]
    public void TheTablesGoOnAnEnvironmentOnce()
    {
        var system = System();
        system.LevelInitPostEntity();
        system.Simulate();
        system.Simulate();

        Assert.Equal(1, _calls.Count(c => c.StartsWith("SetCollisionEventHandler", StringComparison.Ordinal)));
        Assert.Equal(1, _calls.Count(c => c.StartsWith("SetCollisionSolver", StringComparison.Ordinal)));
        Assert.Equal(1, system.Installs);
    }

    [Fact]
    public void ANewLevelsEnvironmentGetsTheTablesAgainEvenAtTheSameAddress()
    {
        var system = System();
        system.LevelInitPostEntity();
        system.LevelShutdownPostEntity();
        Assert.False(system.Installed);

        system.LevelInitPostEntity();   // the allocator may hand the next level the same address
        Assert.True(system.Installed);
        Assert.Equal(2, _calls.Count(c => c == "SetCollisionEventHandler e00 a10"));
        Assert.Equal(2, system.Installs);
    }
}
