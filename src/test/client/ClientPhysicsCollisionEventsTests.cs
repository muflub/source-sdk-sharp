using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's managed IPhysicsCollisionEvent table (plan_client_novalve C6,
/// cluster 9; lane c6-input): the impact slot answered by the client's impact
/// sounds, every other slot forwarded unchanged to native's g_Collisions, each
/// slot counted; and the installer that puts the table on the client's
/// environment once per environment.
/// </summary>
public unsafe class ClientPhysicsCollisionEventsTests : IDisposable
{
    [ThreadStatic] private static List<string>? t_seen;
    private static void Saw(string what) => (t_seen ??= new()).Add(what);

    [UnmanagedCallersOnly] private static void Pre(void* self, void* ev) => Saw($"Pre {(nint)ev:x}");
    [UnmanagedCallersOnly] private static void Post(void* self, void* ev) => Saw($"Post {(nint)ev:x}");
    [UnmanagedCallersOnly] private static void Fric(void* self, void* o, float e, int s, int h, void* d) => Saw($"Friction {(nint)o:x} {e} {s} {h} {(nint)d:x}");
    [UnmanagedCallersOnly] private static void Start(void* self, void* a, void* b, void* d) => Saw($"StartTouch {(nint)a:x} {(nint)b:x} {(nint)d:x}");
    [UnmanagedCallersOnly] private static void End(void* self, void* a, void* b, void* d) => Saw($"EndTouch {(nint)a:x} {(nint)b:x} {(nint)d:x}");
    [UnmanagedCallersOnly] private static void FStart(void* self, void* o, void* f) => Saw($"FluidStart {(nint)o:x} {(nint)f:x}");
    [UnmanagedCallersOnly] private static void FEnd(void* self, void* o, void* f) => Saw($"FluidEnd {(nint)o:x} {(nint)f:x}");
    [UnmanagedCallersOnly] private static void PostSim(void* self) => Saw("PostSimulationFrame");
    [UnmanagedCallersOnly] private static void Enter(void* self, void* t, void* o) => Saw($"Enter {(nint)t:x} {(nint)o:x}");
    [UnmanagedCallersOnly] private static void Leave(void* self, void* t, void* o) => Saw($"Leave {(nint)t:x} {(nint)o:x}");

    private readonly PhysicsCollisionEventVtbl* _vtbl;
    private readonly nint* _native;

    public ClientPhysicsCollisionEventsTests()
    {
        t_seen = new();
        _vtbl = (PhysicsCollisionEventVtbl*)NativeMemory.AllocZeroed((nuint)sizeof(PhysicsCollisionEventVtbl));
        _vtbl->PreCollision = &Pre;
        _vtbl->PostCollision = &Post;
        _vtbl->Friction = &Fric;
        _vtbl->StartTouch = &Start;
        _vtbl->EndTouch = &End;
        _vtbl->FluidStartTouch = &FStart;
        _vtbl->FluidEndTouch = &FEnd;
        _vtbl->PostSimulationFrame = &PostSim;
        _vtbl->ObjectEnterTrigger = &Enter;
        _vtbl->ObjectLeaveTrigger = &Leave;
        _native = (nint*)NativeMemory.AllocZeroed((nuint)sizeof(nint));
        *_native = (nint)_vtbl;
    }

    public void Dispose()
    {
        NativeMemory.Free(_native);
        NativeMemory.Free(_vtbl);
    }

    private sealed class RecordingImpacts : IClientImpactSink
    {
        public readonly List<string> Seen = new();
        public void PostCollision(in VCollisionEvent ev) => Seen.Add($"post {ev.Object0:x} {ev.CollisionSpeed}");
    }

    private readonly RecordingImpacts _impacts = new();

    private sealed class RecordingFluids : IClientFluidSink
    {
        public readonly List<string> Seen = new();
        public void FluidStart(nint physicsObject, nint fluid) => Seen.Add($"start {physicsObject:x} {fluid:x}");
    }

    private readonly RecordingFluids _fluids = new();

    private sealed class RecordingFriction : IClientFrictionSink
    {
        public readonly List<string> Seen = new();
        public void Friction(nint o, float energy, int s, int h, nint data) => Seen.Add($"friction {o:x} {energy} {s} {h} {data:x}");
    }

    private readonly RecordingFriction _friction = new();

    private ClientPhysicsCollisionEventBinding Sink(Crossings? crossings = null)
        => new(crossings ?? new Crossings(), (nint)_native, _impacts, _fluids, _friction);

    [Fact]
    public void ThePostCollisionGoesToTheImpactSoundsAndIsNotForwarded()
    {
        var sink = Sink();
        var ev = new VCollisionEvent { Object0 = 0x51, Object1 = 0x52, CollisionSpeed = 320f };
        sink.PostCollision(&ev);
        Assert.Equal(new[] { "post 51 320" }, _impacts.Seen);
        Assert.Empty(t_seen!);
    }

    [Fact]
    public void ANullPostCollisionReachesNobody()
    {
        var sink = Sink();
        sink.PostCollision(null);
        Assert.Empty(_impacts.Seen);
        Assert.Empty(t_seen!);
    }

    [Fact]
    public void EveryUntakenSlotForwardsToNativesHandlerUnchanged()
    {
        var sink = Sink();
        sink.PreCollision((void*)0x10);
        sink.StartTouch((void*)0x13, (void*)0x14, (void*)0x15);
        sink.EndTouch((void*)0x16, (void*)0x17, (void*)0x18);
        sink.PostSimulationFrame();
        sink.ObjectEnterTrigger((void*)0x1d, (void*)0x1e);
        sink.ObjectLeaveTrigger((void*)0x1f, (void*)0x20);
        Assert.Equal(new[]
        {
            "Pre 10", "StartTouch 13 14 15", "EndTouch 16 17 18",
            "PostSimulationFrame", "Enter 1d 1e", "Leave 1f 20",
        }, t_seen);
        Assert.Empty(_impacts.Seen);
    }

    [Fact]
    public void AScrapeGoesToTheClientsFrictionAndIsNotForwarded()
    {
        var sink = Sink();
        sink.Friction((void*)0x11, 8000f, 7, 9, (void*)0x12);
        Assert.Equal(new[] { "friction 11 8000 7 9 12" }, _friction.Seen);
        Assert.Empty(t_seen!);
    }

    [Fact]
    public void AFluidEntryGoesToTheFluidEntriesAndTheExitToNobody()
    {
        var sink = Sink();
        sink.FluidStartTouch((void*)0x19, (void*)0x1a);
        sink.FluidEndTouch((void*)0x1b, (void*)0x1c);
        Assert.Equal(new[] { "start 19 1a" }, _fluids.Seen);
        Assert.Empty(t_seen!);
    }

    [Fact]
    public void EverySlotCountsItsCrossing()
    {
        var crossings = new Crossings();
        var sink = Sink(crossings);
        var ev = new VCollisionEvent();
        sink.PreCollision(&ev);
        sink.PostCollision(&ev);
        sink.Friction((void*)1, 1f, 0, 0, (void*)1);
        sink.StartTouch(null, null, null);
        sink.EndTouch(null, null, null);
        sink.FluidStartTouch(null, null);
        sink.FluidEndTouch(null, null);
        sink.PostSimulationFrame();
        sink.ObjectEnterTrigger(null, null);
        sink.ObjectLeaveTrigger(null, null);
        foreach (CrossingOp op in new[]
                 {
                     CrossingOp.ClientPhysicsPreCollision, CrossingOp.ClientPhysicsPostCollision,
                     CrossingOp.ClientPhysicsFriction, CrossingOp.ClientPhysicsStartTouch,
                     CrossingOp.ClientPhysicsEndTouch, CrossingOp.ClientPhysicsFluidStartTouch,
                     CrossingOp.ClientPhysicsFluidEndTouch, CrossingOp.ClientPhysicsPostSimulationFrame,
                     CrossingOp.ClientPhysicsObjectEnterTrigger, CrossingOp.ClientPhysicsObjectLeaveTrigger,
                 })
            Assert.Equal(1, crossings.Read(op));
    }

    private sealed class RecordingEnvironment : IVPhysicsEnvironment
    {
        public readonly List<(nint Env, nint Handler)> Installed = new();
        public void SetObjectEventHandler(nint environment, nint handler) => throw new InvalidOperationException("the client installs no object-event table");
        public float SimulationTimestep(nint environment) => 0f;
        public void SetCollisionEventHandler(nint environment, nint handler) => Installed.Add((environment, handler));
        public void SetCollisionSolver(nint environment, nint handler) => throw new InvalidOperationException("the client installs its solver through ClientPhysicsSlots");
        public void SetConstraintEventHandler(nint environment, nint handler) => throw new InvalidOperationException("the client installs no constraint event handler");
    }

    [Fact]
    public void TheTableIsInstalledOnceOnAnEnvironment()
    {
        var env = new RecordingEnvironment();
        var callbacks = new ClientPhysicsCallbacks(() => 0x900, env) { CollisionEvents = 0x700 };
        callbacks.EnsureInstalled();
        callbacks.EnsureInstalled();
        Assert.Equal(new[] { ((nint)0x900, (nint)0x700) }, env.Installed);
        Assert.Equal(1, callbacks.Installs);
    }

    [Fact]
    public void TheSolverGoesOnTheEnvironmentWithTheEventTable()
    {
        var env = new RecordingEnvironment();
        var solvers = new List<(nint Env, nint Solver)>();
        var callbacks = new ClientPhysicsCallbacks(() => 0x900, env, (e, s) => solvers.Add((e, s)))
        {
            CollisionEvents = 0x700,
            CollisionSolver = 0x600,
        };
        callbacks.EnsureInstalled();
        callbacks.EnsureInstalled();
        Assert.Equal(new[] { ((nint)0x900, (nint)0x600) }, solvers);
    }

    [Fact]
    public void WithNoEnvironmentNothingIsInstalled()
    {
        var env = new RecordingEnvironment();
        var callbacks = new ClientPhysicsCallbacks(() => 0, env) { CollisionEvents = 0x700 };
        callbacks.EnsureInstalled();
        Assert.Empty(env.Installed);
    }

    [Fact]
    public void WithNoTableNothingIsInstalled()
    {
        var env = new RecordingEnvironment();
        var callbacks = new ClientPhysicsCallbacks(() => 0x900, env);
        callbacks.EnsureInstalled();
        Assert.Empty(env.Installed);
    }

    [Fact]
    public void InstalledSaysWhetherThisLevelsEnvironmentHasTheTable()
    {
        var env = new RecordingEnvironment();
        nint current = 0;
        var callbacks = new ClientPhysicsCallbacks(() => current, env) { CollisionEvents = 0x700 };
        callbacks.EnsureInstalled();
        Assert.False(callbacks.Installed);
        current = 0x900;
        callbacks.EnsureInstalled();
        Assert.True(callbacks.Installed);
        callbacks.LevelShutdown();
        Assert.False(callbacks.Installed);
    }

    [Fact]
    public void ANewLevelsEnvironmentGetsTheTableAgain()
    {
        var env = new RecordingEnvironment();
        nint current = 0x900;
        var callbacks = new ClientPhysicsCallbacks(() => current, env) { CollisionEvents = 0x700 };
        callbacks.EnsureInstalled();
        callbacks.LevelShutdown();
        current = 0x900;   // the allocator may hand the next level the same address
        callbacks.EnsureInstalled();
        Assert.Equal(2, env.Installed.Count);
    }
}

/// <summary>cs_cl_physics says what the client physics callbacks have done (lane c6-input).</summary>
public class PhysicsClientCommandTests
{
    private sealed class Log : ILog
    {
        public readonly List<string> Lines = new();
        public void Msg(string text) => Lines.Add(text);
        public void Warning(string text) => Lines.Add(text);
    }

    private sealed class NoWorld : IClientImpactSoundWorld
    {
        public bool IsStatic(nint o) => false;
        public nint GameData(nint o) => 0;
        public Vector Position(nint o) => Vector.Zero;
        public SurfaceImpactAudio? Surface(int s) => null;
        public bool Emit(string name, Vector origin, int channel, float volume) => true;
    }

    private sealed class NoFacts : IClientCollisionFacts
    {
        public ushort GameFlags(nint o) => 0;
        public uint Contents(nint o) => 1;
        public bool HasShadowController(nint o) => false;
        public EngineCollisionGroup GroupOf(EntityHandle h) => EngineCollisionGroup.Debris;
        public bool RagdollCollide => false;
    }

    private sealed class NoFluidWorld : IClientFluidWorld
    {
        public nint GameData(nint o) => 0;
        public ushort GameFlags(nint o) => 0;
        public Vector Velocity(nint o) => Vector.Zero;
        public (Vector Normal, float Dist) SurfacePlane(nint fluid) => (Vector.Zero, 0f);
        public (Vector Origin, Vector Angles) OwnerTransform(EntityHandle owner) => (Vector.Zero, Vector.Zero);
        public Vector CollideExtent(nint o, Vector origin, Vector angles, Vector dir) => Vector.Zero;
        public void Splash(in ClientSplashEffect effect) { }
        public int PointContents(Vector point) => 0;
        public (Vector Color, float Luminosity) SplashLighting(Vector point) => (Vector.Zero, 1f);
    }

    private sealed class NoRamps : ISoundPatchRamps
    {
        public void Destroy(int patch) { }
        public void Play(int patch, float volume, float pitch, float startTime) { }
        public void ChangePitch(int patch, float target, float deltaTime) { }
        public void ChangeVolume(int patch, float target, float deltaTime) { }
    }

    private sealed class NoSurfaces : SourceSharp.Shared.IPhysicsSurfaceTable
    {
        public MoveSurface Of(int surfaceProps) => default;
    }

    private sealed class NoEffects : SourceSharp.Shared.IFrictionEffects
    {
        public void Dust(Vector point, Vector direction) { }
        public void MetalSparks(Vector point, Vector direction) { }
    }

    private sealed class Env : IVPhysicsEnvironment
    {
        public void SetObjectEventHandler(nint environment, nint handler) { }
        public void SetCollisionEventHandler(nint environment, nint handler) { }
        public void SetCollisionSolver(nint environment, nint handler) { }
        public void SetConstraintEventHandler(nint environment, nint handler) { }
        public float SimulationTimestep(nint environment) => 0f;
    }

    [Fact]
    public void ItPrintsTheInstallAndTheImpactCounters()
    {
        var log = new Log();
        var callbacks = new ClientPhysicsCallbacks(() => 0x900, new Env()) { CollisionEvents = 0x700 };
        callbacks.EnsureInstalled();
        new PhysicsClientCommand(log, callbacks, new ClientImpactSounds(new NoWorld()), new ClientCollisionSolver(new NoFacts()),
                                 new ClientFluidEvents(new NoFluidWorld(), null, () => 0f),
                                 new SourceSharp.Shared.FrictionSounds(new RecordingEngine(), new NoRamps(),
                                                                       new NoSurfaces(), new NoEffects())).Execute("");
        Assert.Equal("[SourceSharp/Client] physics: table installed yes (installs 1), impact sounds queued 0, played 0, pairs asked 0, refused 0, splashes 0 (gunshot 0, slime 0, ripple 0), scrapes started 0, playing 0\n",
                     Assert.Single(log.Lines));
    }
}
