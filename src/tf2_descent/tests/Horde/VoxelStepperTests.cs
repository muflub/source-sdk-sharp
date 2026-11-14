using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Ai.TF2.Jump;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Server.Horde.Motion;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The bodies' steps off the game thread (plan_descent_logic 3.7, E5): the game's movement on a
/// worker against the level's voxel columns (<see cref="VoxelMotionWorld"/> over
/// <see cref="VoxelMoveWorld"/>), applied on the game thread through the accept against the
/// engine's world. Over the same geometry it walks, jumps and falls as the inline step over the
/// engine does; what the voxels do not know (a prop, a hero) the accept stops a body at; a stalled
/// worker holds the bodies and the game thread never waits; the steady tick allocates nothing on
/// the game thread.
/// </summary>
public sealed class VoxelStepperTests
{
    private const float Tick = 0.015f;

    /// <summary>How far the worker's body may be from the inline body after the same orders over the same geometry.</summary>
    private const float SameWithin = 0.1f;

    /// <summary>The geometry both worlds hold: a floor at z 0, and a platform 64 high from x 256 to 512, all on the 16-unit grid.</summary>
    private static BoxMotionWorld Boxes() => new BoxMotionWorld { FloorZ = 0f }.Put(new Vector(256, -1024, 0), new Vector(512, 1024, 64));

    private static VoxelColumns Voxels() =>
        VoxelColumns.Of(-1024f, -1024f, 16f, 128, 128, (x, _) =>
        {
            float cx = -1024f + x * 16f;
            return cx >= 256f && cx < 512f ? [new FreeSpan(64f, 1024f)] : [new FreeSpan(0f, 1024f)];
        });

    private static WorkerMotionStepper Worker(VoxelColumns voxels, int capacity = 8) =>
        new(GameMovementStep.Create(new VoxelMotionWorld(new VoxelMoveWorld(voxels)), Tick), capacity, new ThreadCpuClock());

    /// <summary>The same orders, tick for tick, on an inline rig over the boxes and a worker rig over the voxels (accepted against the boxes); the most the two bodies were apart.</summary>
    private static (float Most, AiVector Inline, AiVector Worker) Both(AiVector start, MoveIntent move, int ticks)
    {
        var inline = new MotionRig(world: Boxes());
        WorkerMotionStepper? worker = null;
        var voxel = new MotionRig(world: Boxes(), stepper: _ => worker = Worker(Voxels()));
        foreach (var rig in new[] { inline, voxel })
        {
            rig.Bodies.Lend(1, start);
            rig.Motion.Order(1, move, LookIntent.AlongPath, long.MaxValue);
        }

        float most = 0f;
        for (int t = 0; t < ticks; t++)
        {
            inline.Tick();
            Settle(voxel, worker!);
            most = MathF.Max(most, (inline.At(1) - voxel.At(1)).Length);
        }

        return (most, inline.At(1), voxel.At(1));
    }

    [Fact]
    public void A_walk_on_the_worker_over_the_voxels_is_the_inline_walk_over_the_same_geometry()
    {
        var (most, inline, worker) = Both(At(-400, 0, 1), MotionRig.Follow(At(200, 300)), 260);

        Assert.True(inline.Y > 250f, $"the control: the inline body did not walk ({inline})");
        Assert.True(most <= SameWithin, $"apart by {most} (inline {inline}, worker {worker})");
    }

    [Fact]
    public void A_jump_on_the_worker_is_the_inline_jump()
    {
        var jump = new MoveIntent(MoveKind.Jump, MoveSpeed.Run, 1, At(0, 0), default, default, default, default, false, 0);
        var (most, inline, worker) = Both(At(-400, 0, 1), jump, 120);

        Assert.True(most <= SameWithin, $"apart by {most} (inline {inline}, worker {worker})");
    }

    [Fact]
    public void A_fall_off_the_platform_on_the_worker_is_the_inline_fall()
    {
        var (most, inline, worker) = Both(At(480, 0, 65), MotionRig.Follow(At(700, 0)), 150);

        Assert.True(inline.X > 600f && inline.Z < 1f, $"the control: the inline body did not walk off the platform and land ({inline})");
        Assert.True(most <= SameWithin, $"apart by {most} (inline {inline}, worker {worker})");
    }

    [Fact]
    public void Up_a_step_onto_the_platform_the_worker_climbs_as_the_inline_body_does()
    {
        // A stair the voxels hold: a rise of 16 at x 256 (the step the movement climbs), here as the platform's lower twin.
        var boxes = new BoxMotionWorld { FloorZ = 0f }.Put(new Vector(256, -1024, 0), new Vector(512, 1024, 16));
        var voxels = VoxelColumns.Of(-1024f, -1024f, 16f, 128, 128, (x, _) =>
        {
            float cx = -1024f + x * 16f;
            return cx >= 256f && cx < 512f ? [new FreeSpan(16f, 1024f)] : [new FreeSpan(0f, 1024f)];
        });
        WorkerMotionStepper? worker = null;
        var rig = new MotionRig(world: boxes, stepper: _ => worker = Worker(voxels));
        rig.Bodies.Lend(1, At(100, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        for (int t = 0; t < 120; t++)
            Settle(rig, worker!);

        Assert.True(rig.At(1).X > 350f && MathF.Abs(rig.At(1).Z - 16f) < 1f, $"did not climb the step: {rig.At(1)}");
    }

    [Fact]
    public void A_prop_the_voxels_do_not_hold_stops_the_body_at_the_accept()
    {
        var world = new BoxMotionWorld { FloorZ = 0f }.Put(new Vector(100, -64, 0), new Vector(140, 64, 64), entityIndex: 77);
        WorkerMotionStepper? worker = null;
        var flat = VoxelColumns.Of(-1024f, -1024f, 16f, 128, 128, (_, _) => [new FreeSpan(0f, 1024f)]);
        var rig = new MotionRig(world: world, stepper: _ => worker = Worker(flat));
        rig.Bodies.Lend(1, At(0, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        float most = 0f;
        for (int t = 0; t < 120; t++)
        {
            Settle(rig, worker!);
            most = MathF.Max(most, rig.At(1).X);
        }

        Assert.True(most <= 100f - 24f + 0.1f, $"the body went into the prop: x {most}");
        Assert.True(rig.Motion.Clips > 0, "the control: the accept clipped nothing (the worker's step never reached the prop)");
        Assert.Equal(BlockerKind.Prop, rig.Motion.Blocked(1).Kind);
        Assert.Equal(77, rig.Motion.Blocked(1).Who);
    }

    /// <summary>L9: the hero in a doorway blocks the robots; the worker's voxels hold no hero, the accept does.</summary>
    [Fact]
    public void A_hero_in_the_way_blocks_a_body_stepped_on_the_worker()
    {
        var world = new BoxMotionWorld { FloorZ = 0f }.Put(new Vector(100, -24, 0), new Vector(148, 24, 82), entityIndex: 3, isPlayer: true);
        WorkerMotionStepper? worker = null;
        var flat = VoxelColumns.Of(-1024f, -1024f, 16f, 128, 128, (_, _) => [new FreeSpan(0f, 1024f)]);
        var rig = new MotionRig(world: world, stepper: _ => worker = Worker(flat));
        rig.Bodies.Lend(1, At(0, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(400, 0)), LookIntent.AlongPath, long.MaxValue);

        for (int t = 0; t < 80; t++)
            Settle(rig, worker!);

        Assert.Equal(BlockerKind.Hero, rig.Motion.Blocked(1).Kind);
        Assert.Equal(3, rig.Motion.Blocked(1).Who);
        Assert.True(rig.At(1).X < 100f - 24f + 0.1f, $"the body went into the hero: {rig.At(1)}");
    }

    /// <summary>
    /// A worker stalled inside a step (a gate the fact holds, as the session's <see cref="StallGate"/>):
    /// a hundred ticks each return while the step is still held, the bodies hold, and the stale
    /// set is never applied once the worker goes on.
    /// </summary>
    [Fact]
    public void A_stalled_voxel_worker_holds_the_bodies_and_the_game_thread_ticks_on()
    {
        var flat = VoxelColumns.Of(-1024f, -1024f, 16f, 128, 128, (_, _) => [new FreeSpan(0f, 1024f)]);
        var blocking = new BlockingStep(GameMovementStep.Create(new VoxelMotionWorld(new VoxelMoveWorld(flat)), Tick));
        WorkerMotionStepper? worker = null;
        var rig = new MotionRig(world: new BoxMotionWorld { FloorZ = 0f }, stepper: _ => worker = new WorkerMotionStepper(blocking, 8, new ThreadCpuClock()));
        rig.Bodies.Lend(1, At(0, 0, 1));
        rig.Motion.Order(1, MotionRig.Follow(At(900, 0)), LookIntent.AlongPath, long.MaxValue);
        for (int t = 0; t < 40; t++)
            Settle(rig, worker!);
        Assert.True(rig.At(1).X > 50f, $"the control: it never walked ({rig.At(1)})");

        blocking.Gate.Reset();
        rig.Tick();
        for (int i = 0; i < 1_000_000 && blocking.Waiting == 0; i++)
            Thread.Yield();
        Assert.True(blocking.Waiting > 0, "the control: the worker never entered the held step");
        var held = rig.At(1);
        long holds = rig.Motion.Holds;

        rig.Tick(100);

        Assert.True(blocking.Waiting > 0, "the game thread waited: the held step had left by the hundredth tick");
        Assert.Equal(held, rig.At(1));
        Assert.True(rig.Motion.Holds - holds >= 99, $"held {rig.Motion.Holds - holds} ticks");

        blocking.Gate.Set();
        for (int i = 0; i < 1_000_000 && blocking.Waiting > 0; i++)
            Thread.Yield();
        Settle(rig, worker!);
        Settle(rig, worker!);
        Assert.True((rig.At(1) - held).Length2D < 12f, $"jumped {(rig.At(1) - held).Length2D} on going on: a stale step was applied");
        Assert.True(worker!.Stale + rig.Motion.Holds > holds, "the control: nothing was held or dropped");
    }

    [Fact]
    public void Steady_ticks_of_48_bodies_stepped_on_the_voxel_worker_allocate_nothing_on_the_game_thread()
    {
        var flat = VoxelColumns.Of(-1024f, -1024f, 16f, 512, 512, (_, _) => [new FreeSpan(0f, 1024f)]);
        WorkerMotionStepper? worker = null;
        var rig = new MotionRig(bodies: 48, world: new BoxMotionWorld { FloorZ = 0f }, stepper: _ => worker = Worker(flat, 49));
        for (int b = 1; b <= 48; b++)
        {
            rig.Bodies.Lend(b, At(b * 100 - 800, -800, 1));
            rig.Motion.Order(b, MotionRig.Follow(At(b * 100 - 800, 6000), At(6000, 6000)), LookIntent.AlongPath, long.MaxValue);
        }

        for (int t = 0; t < 100; t++)
            Settle(rig, worker!);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int t = 0; t < 500; t++)
            Settle(rig, worker!);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(rig.At(48).Y > 1000f, $"the bodies did not move: {rig.At(48)}");
        Assert.True(worker!.StepsRun > 48 * 400, $"the control: {worker.StepsRun} steps on the worker");
        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// One tick, then until the worker has run the set that tick handed it: a tick whose hand-over
    /// the worker's lock refused (<see cref="WorkerMotionStepper.Contended"/>) is ticked again.
    /// Waits by yielding (allocates nothing), and gives up after a bound of yields, never a clock.
    /// </summary>
    internal static void Settle(MotionRig rig, WorkerMotionStepper worker)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            long ran = worker.StepsRun, refused = worker.Contended;
            rig.Tick();
            if (worker.Contended != refused)
                continue;
            for (int i = 0; i < 10_000_000 && worker.StepsRun == ran && worker.Busy; i++)
                Thread.Yield();
            return;
        }
    }
}
