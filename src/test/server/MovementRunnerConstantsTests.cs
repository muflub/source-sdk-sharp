using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's run takes the constants it is handed, not the blob's
/// constant words (plan_server_native_v2 T4g: the server's are managed's own,
/// and native stopped writing them).
/// </summary>
public sealed unsafe class MovementRunnerConstantsTests
{
    private static MoveStateBlob Airborne()
    {
        MoveStateBlob b = default;
        b.IsServer = 1;
        b.MoveType = 2;            // MOVETYPE_WALK
        b.IsAlive = 1;
        b.FrameTime = 0.015f;
        b.EntityGravity = 1f;
        b.LaggedMovementValue = 1f;
        b.ModelScale = 1f;
        b.SurfaceFriction = 1f;
        b.StepSize = 18f;
        b.OriginZ = 512f;

        // The words the blob carries, and which the run must NOT use.
        b.CurrentGravity = 800f;
        b.CvTickInterval = 0.015f;
        b.CvMaxVelocity = 3500f;
        return b;
    }

    private static MoveFrameConstants Frame(float gravity)
    {
        MovementParams p = Movement.Current;
        var c = new MoveConstants
        {
            MaxVelocity = 3500f, MaxSpeed = 320f, Friction = 4f, StopSpeed = 100f,
            Accelerate = 10f, AirAccelerate = 10f, AirSpeedCap = 30f, DuckSpeedScale = 0.33333333f,
            Optimizations = true, MaxClients = 2, TickInterval = 0.015f,
            View = p.View, HullMin = p.HullMin, HullMax = p.HullMax, DuckHullMin = p.DuckHullMin,
            DuckHullMax = p.DuckHullMax, DuckView = p.DuckView, ObsHullMin = p.ObsHullMin,
            ObsHullMax = p.ObsHullMax, DeadViewHeight = p.DeadViewHeight,
        };
        return new MoveFrameConstants(c, 21f, 0f, 1f, gravity);
    }

    // THE SERVER'S MOVEMENT TRACE, CLEAR: the hull swept the whole way, as
    // an empty world answers. The side traces through its tracer, and a test
    // side installs none (the side then refuses, blocked at the start), so
    // this fact attaches a root whose slot answers clear, and a tracer that
    // filters nothing. The fall is the subject here, not the world.
    [UnmanagedCallersOnly]
    private static void Clear(void* self, float* s, float* e, float* lo, float* hi, int mask, int group,
                              MoveTraceBlob* blob, void* filter, uint* handles)
    {
        *blob = default;
        blob->Fraction = 1f;
        blob->EndPosX = e[0];
        blob->EndPosY = e[1];
        blob->EndPosZ = e[2];
        handles[0] = 0xFFFFFFFFu;
        handles[1] = 0xFFFFFFFFu;
    }

    private sealed class NoFilter : IServerTraceRoute
    {
        public ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup) => default;
        public ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass, int collisionGroup) => default;
        public int PointContents(Vector point) => 0;
        public nint Enter(EntityHandle pass, int collisionGroup) => 0;
        public void Exit() { }
    }

    [Fact]
    public void TheServersFallIsTheGivenGravityNotTheBlobs()
    {
        using var root = new FakeShimRoot();
        root.Live("SourceSharpMovementServer001", nameof(ShimMovementServerFwd.MoveTraceServer),
                  (nint)(delegate* unmanaged<void*, float*, float*, float*, float*, int, int, MoveTraceBlob*, void*, uint*, void>)&Clear);
        EngineSide side = TestSides.Create(Side.Server);
        side.Initialize();
        Assert.True(side.AttachOwnShims(root.Bind(), out string why), why);
        DefaultForwarders.HoldOn(side);
        side.Tracer = new NoFilter();

        var runner = MovementRunner.For(new ModuleEngine(side));
        MoveStateBlob blob = Airborne();
        MoveFrameConstants frame = Frame(400f);

        Assert.Equal(1, runner.ProcessMovement(&blob, MovementRules.Stock, in frame));

        // StartGravity and FinishGravity each take half a tick of gravity.
        float expected = -400f * 0.015f;
        Assert.True(MathF.Abs(blob.VelocityZ - expected) < 1e-3f,
                    $"fell at {blob.VelocityZ}, sv_gravity 400 is {expected}, the blob's 800 is {2 * expected}");
    }
}
