using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's managed IPhysicsCollisionEvent table (plan_client_novalve C6,
/// cluster 9): the impact, scrape and fluid slots answered by their managed
/// sinks, every other slot answered here as stock's client answered it
/// (nothing), each slot counted. Fork physics.cpp's g_Collisions is gone, so
/// nothing is forwarded anywhere.
/// </summary>
public unsafe class ClientPhysicsCollisionEventsTests
{
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
        => new(crossings ?? new Crossings(), _impacts, _fluids, _friction);

    private bool NoSinkSawAnything => _impacts.Seen.Count == 0 && _fluids.Seen.Count == 0 && _friction.Seen.Count == 0;

    [Fact]
    public void ThePostCollisionGoesToTheImpactSoundsAlone()
    {
        var sink = Sink();
        var ev = new VCollisionEvent { Object0 = 0x51, Object1 = 0x52, CollisionSpeed = 320f };
        sink.PostCollision(&ev);
        Assert.Equal(new[] { "post 51 320" }, _impacts.Seen);
        Assert.Empty(_fluids.Seen);
        Assert.Empty(_friction.Seen);
    }

    [Fact]
    public void ANullPostCollisionReachesNobody()
    {
        var sink = Sink();
        sink.PostCollision(null);
        Assert.True(NoSinkSawAnything);
    }

    [Fact]
    public void TheFiveSlotsStockLeftEmptyReachNoSink()
    {
        // Stock's client: PreCollision, PostSimulationFrame and the triggers have
        // empty bodies; a touch between objects with no SDK owner returns at once.
        var sink = Sink();
        var ev = new VCollisionEvent { Object0 = 0x51, CollisionSpeed = 320f };
        sink.PreCollision(&ev);
        sink.StartTouch((void*)0x13, (void*)0x14, (void*)0x15);
        sink.EndTouch((void*)0x16, (void*)0x17, (void*)0x18);
        sink.PostSimulationFrame();
        sink.ObjectEnterTrigger((void*)0x1d, (void*)0x1e);
        sink.ObjectLeaveTrigger((void*)0x1f, (void*)0x20);
        Assert.True(NoSinkSawAnything);
    }

    [Fact]
    public void AScrapeGoesToTheClientsFriction()
    {
        var sink = Sink();
        sink.Friction((void*)0x11, 8000f, 7, 9, (void*)0x12);
        Assert.Equal(new[] { "friction 11 8000 7 9 12" }, _friction.Seen);
        Assert.Empty(_impacts.Seen);
    }

    [Fact]
    public void AFluidEntryGoesToTheFluidEntriesAndTheExitToNobody()
    {
        var sink = Sink();
        sink.FluidStartTouch((void*)0x19, (void*)0x1a);
        sink.FluidEndTouch((void*)0x1b, (void*)0x1c);
        Assert.Equal(new[] { "start 19 1a" }, _fluids.Seen);
        Assert.Empty(_impacts.Seen);
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

    private sealed class NoWorld : IClientImpactSoundWorld, IClientImpactSoundEmitter
    {
        public bool IsStatic(nint o) => false;
        public nint GameData(nint o) => 0;
        public Vector Position(nint o) => Vector.Zero;
        public SurfaceImpactAudio? Surface(int s) => null;
        public bool EmitImpactSound(string name, Vector origin, int channel, float volume) => true;
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

    private sealed class Level : SourceSharp.Client.Interop.IClientPhysicsLevelCalls
    {
        public nint CreateEnvironment() => 0x900;
        public void DestroyEnvironment(nint e) { }
        public void DestroyAllCollisionSets() { }
        public void SetGravity(nint e, Vector g) { }
        public void SetSimulationTimestep(nint e, float s) { }
        public void SetCollisionEventHandler(nint e, nint h) { }
        public void SetCollisionSolver(nint e, nint s) { }
        public void SetQuickDelete(nint e, bool q) { }
        public void Simulate(nint e, float dt) { }
        public void ResetSimulationClock(nint e) { }
    }

    private sealed class Clock : IClientPhysicsClock
    {
        public float FrameTime => 0f;
        public float IntervalPerTick => 0.015f;
        public float Gravity => 600f;
        public float TimeScale => 1f;
    }

    private sealed class NoTail : IClientPhysicsStepTail
    {
        public void UpdateFrictionSounds() { }
        public void UpdateFluidEvents() { }
        public void PlayImpactSounds() { }
        public void RemoveAllImpactSounds() { }
    }

    [Fact]
    public void ItPrintsTheInstallAndTheImpactCounters()
    {
        var log = new Log();
        var system = new ClientPhysicsSystem(new Level(), new Clock(), new SourceSharp.Client.Interop.ClientPhysicsEnvironment(),
                                             () => null, () => null, new NoTail(), log) { CollisionEvents = 0x700 };
        system.LevelInitPostEntity();
        system.Simulate();
        log.Lines.Clear();
        new PhysicsClientCommand(log, system, new ClientImpactSounds(new NoWorld(), new NoWorld()), new ClientCollisionSolver(new NoFacts()),
                                 new ClientFluidEvents(new NoFluidWorld(), new SourceSharp.Abi.DefaultForwarders.LowestRandom(), () => 0f),
                                 new SourceSharp.Shared.FrictionSounds(new RecordingEngine(), new NoRamps(),
                                                                       new NoSurfaces(), new NoEffects())).Execute("");
        // The step's cost is a measurement, so it is matched rather than spelled.
        Assert.Matches(@"^\[SourceSharp/Client\] physics: environment yes, tables installed yes \(installs 1\), steps 1 " +
                       @"\(step \d+ ns, solver \d+ ns\), impact sounds queued 0, played 0, pairs asked 0, refused 0, " +
                       @"splashes 0 \(gunshot 0, slime 0, ripple 0\), scrapes started 0, playing 0\n$",
                       Assert.Single(log.Lines));

        log.Lines.Clear();
        new PhysicsClientCommand(log, system, new ClientImpactSounds(new NoWorld(), new NoWorld()), new ClientCollisionSolver(new NoFacts()),
                                 new ClientFluidEvents(new NoFluidWorld(), new SourceSharp.Abi.DefaultForwarders.LowestRandom(), () => 0f),
                                 new SourceSharp.Shared.FrictionSounds(new RecordingEngine(), new NoRamps(),
                                                                       new NoSurfaces(), new NoEffects())).Execute("reset");
        Assert.Equal(0, system.Steps);
        Assert.Equal(0, system.StepNs);
    }
}
