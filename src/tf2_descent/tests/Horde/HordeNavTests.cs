using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Nav;
using TF2.Descent.Tests.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// What a fact and a gated derivation share: the work says it is inside its gate, the fact says
/// when it may go on. The gate also opens by itself after <see cref="CapMs"/>, so a game thread
/// that waited on it fails the fact instead of hanging the run (as <see cref="StallGate"/>).
///
/// <para>
/// The cap is a guard against a hang and nothing else, so it is long: the derivation it waits
/// for is a tenth of a second on a quiet box and took more than 30 s twice in a whole-leg run on
/// a box at a load of 70 to 120 (2026-11-14), which failed a fact that was not wrong.
/// </para>
/// </summary>
internal sealed class DeriveGate : IDisposable
{
    public const int CapMs = 300_000;

    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);
    public ManualResetEventSlim Left { get; } = new(false);

    /// <summary>The nav's work, held at this gate.</summary>
    public (HordeNavData, int[]) Work(byte[] nav, byte[]? bsp)
    {
        Entered.Set();
        Release.Wait(CapMs);
        try
        {
            return HordeNav.DeriveAndList(nav, bsp);
        }
        finally
        {
            Left.Set();
        }
    }

    public void Dispose() => Release.Set();
}

/// <summary>
/// The level's navigation as the server keeps it (<see cref="HordeNav"/>): derived on a thread of
/// its own while the game thread ticks on, the static props probed a few cells a tick, handed to
/// the brains' paths, the stepper chosen, and every piece let go with the level.
/// </summary>
public sealed class HordeNavTests
{
    private static readonly LevelFixture Fixture = LevelFixture.Committed();

    private static (ServerHordeRig Rig, HordeNav Nav) Make(string sync = "0")
    {
        var rig = new ServerHordeRig(HordeNavDataTests.FixtureLevel);
        var nav = rig.Nav(new TF2.Descent.Tests.Level.LevelServiceTests.LevelFilesOnDisk(Fixture), sync: sync);
        return (rig, nav);
    }

    /// <summary>Ticks the nav until the derivation's thread has handed its work over (a gate on the thread, never a clock on the game thread).</summary>
    private static void UntilDerived(ServerHordeRig rig, HordeNav nav)
    {
        for (int i = 0; i < 100_000 && nav.State == HordeNavState.Deriving; i++)
        {
            if (nav.ThreadsAlive > 0)
                Thread.Sleep(1);
            rig.Now++;
            nav.Tick();
        }

        Assert.NotEqual(HordeNavState.Deriving, nav.State);
    }

    private static void UntilReady(ServerHordeRig rig, HordeNav nav)
    {
        UntilDerived(rig, nav);
        for (int i = 0; i < 10_000 && nav.State == HordeNavState.Probing; i++)
        {
            rig.Now++;
            nav.Tick();
        }

        Assert.Equal(HordeNavState.Ready, nav.State);
    }

    [Fact]
    public void The_graph_is_derived_off_the_game_thread_which_ticks_on_meanwhile_and_is_handed_over_when_done()
    {
        var (rig, nav) = Make();
        using var gate = new DeriveGate();
        nav.Work = gate.Work;

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        Assert.True(gate.Entered.Wait(DeriveGate.CapMs), "the control: the derivation never started");

        // A hundred ticks while the work is held at its gate: each returns; a game thread that waited would sit in the first.
        for (int t = 0; t < 100; t++)
        {
            rig.Now++;
            nav.Tick();
        }

        Assert.False(gate.Left.IsSet, "the game thread waited: the held work had left by the hundredth tick");
        Assert.Equal(HordeNavState.Deriving, nav.State);
        Assert.Equal(0, nav.Paths.Epoch);
        Assert.Null(nav.Data);

        gate.Release.Set();
        UntilReady(rig, nav);
        Assert.Equal(HordePaths.EpochOf(nav.Data!.Identity), nav.Paths.Epoch);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("[TF2Descent/Horde] nav ready: tf2_descent-1-s1"));
    }

    [Fact]
    public void A_level_without_a_nav3d_walks_the_room_route_and_steps_on_the_game_thread()
    {
        var rig = new ServerHordeRig(HordeNavDataTests.FixtureLevel);
        var nav = rig.Nav();

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        rig.Now++;
        nav.Tick();

        Assert.Equal(HordeNavState.Missing, nav.State);
        Assert.Equal(0, nav.Paths.Epoch);
        Assert.IsType<InlineMotionStepper>(rig.Motion.Stepper);
        Assert.Equal(0, nav.ThreadsAlive);
    }

    [Fact]
    public void A_nav3d_that_cannot_be_derived_is_said_once_and_the_level_goes_as_without_one()
    {
        var rig = new ServerHordeRig(HordeNavDataTests.FixtureLevel);
        var broken = Fixture with { Nav3d = [1, 2, 3, 4, 5, 6, 7, 8] };
        var nav = rig.Nav(new TF2.Descent.Tests.Level.LevelServiceTests.LevelFilesOnDisk(broken));

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilDerived(rig, nav);

        Assert.Equal(HordeNavState.Failed, nav.State);
        Assert.Equal(0, nav.Paths.Epoch);
        Assert.Single(rig.Log.Lines, l => l.Text.Contains("cannot be derived"));
        Assert.IsType<InlineMotionStepper>(rig.Motion.Stepper);
    }

    [Fact]
    public void A_derivation_that_throws_anything_fails_the_levels_navigation_and_never_the_thread()
    {
        var (rig, nav) = Make();
        nav.Work = (_, _) => throw new ArgumentException("a file the derivation cannot take");

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilDerived(rig, nav);

        Assert.Equal(HordeNavState.Failed, nav.State);
        Assert.Equal(0, nav.ThreadsAlive);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("ArgumentException: a file the derivation cannot take"));
    }

    [Fact]
    public void Once_the_voxels_are_ready_the_bodies_step_on_a_worker_and_under_motion_sync_on_the_game_thread()
    {
        var (rig, nav) = Make();
        Assert.IsType<InlineMotionStepper>(rig.Motion.Stepper);
        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(rig, nav);
        Assert.IsType<WorkerMotionStepper>(rig.Motion.Stepper);

        var (syncRig, syncNav) = Make(sync: "1");
        syncNav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(syncRig, syncNav);
        Assert.IsType<InlineMotionStepper>(syncRig.Motion.Stepper);
        Assert.Contains("inline", syncNav.StepperName);
    }

    [Fact]
    public void A_second_level_starts_clean_no_graph_path_or_thread_carried_over()
    {
        var (rig, nav) = Make();
        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(rig, nav);
        Assert.NotEqual(0, nav.Paths.Epoch);

        // The level goes while the next level's derivation is held; it ends with the level gone.
        using var gate = new DeriveGate();
        nav.Work = gate.Work;
        nav.End();
        Assert.Equal(0, nav.Paths.Epoch);
        Assert.Null(nav.Data);
        Assert.Equal(PathAnswer.NotReady, nav.Paths.Find(default, default, ClearanceClass.Stand, 1, new AiVector[4], new NavEdgeType[4], out _, out _));
        Assert.IsType<InlineMotionStepper>(rig.Motion.Stepper);

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        Assert.True(gate.Entered.Wait(DeriveGate.CapMs));
        Assert.Equal(1, nav.ThreadsAlive);
        nav.End();
        gate.Release.Set();
        Assert.True(gate.Left.Wait(DeriveGate.CapMs));
        long left = System.Diagnostics.Stopwatch.GetTimestamp();
        while (nav.ThreadsAlive > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(left).TotalMilliseconds < DeriveGate.CapMs)
            Thread.Sleep(1);

        Assert.Equal(0, nav.ThreadsAlive);
        Assert.Equal(1, nav.DroppedStale);
        rig.Now++;
        nav.Tick();
        Assert.Equal(HordeNavState.None, nav.State);
        Assert.Null(nav.Data);
        Assert.Equal(0, nav.Paths.Epoch);
    }

    [Fact]
    public void The_nav_print_names_the_state_the_graph_the_classes_the_searches_and_the_stepper()
    {
        var (rig, nav) = Make();
        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(rig, nav);
        int from = rig.Log.Lines.Count;

        new HordeNavCommand(nav, rig.Motion, rig.Log).Execute();

        string all = string.Join("", rig.Log.Lines.Skip(from).Select(l => l.Text));
        Assert.Contains("nav: Ready on tf2_descent-1-s1", all);
        Assert.Contains($"graph {HordeNavDataTests.Nav.Graph.CellCount} cells", all);
        Assert.Contains("cells by class: stand 26010 x1.15", all);
        Assert.Contains("searches 0:", all);
        Assert.Contains("stepper worker (voxels) (WorkerMotionStepper)", all);
    }

    /// <summary>
    /// A static prop the file does not hold: a crate in the alcove off the drift south of the
    /// arrival (room 12), standing in the engine's world only. The probe blocks the cells a robot
    /// on them would be inside it, and a path across the alcove goes round it.
    /// </summary>
    [Fact]
    public void A_prop_the_file_does_not_hold_is_probed_into_the_graph_and_a_path_goes_round_it()
    {
        var (rig, nav) = Make();
        var crate = new AiVector(2400f, 3600f, 16f);
        rig.World.Put(new Vector(crate.X - 24f, crate.Y - 24f, 16f), new Vector(crate.X + 24f, crate.Y + 24f, 72f));
        nav.Work = (bytes, _) =>
        {
            var data = HordeNavData.Derive(bytes);
            var near = new List<int>();
            data.Graph.CellsNear(crate.X - HordeNav.PropReach, crate.Y - HordeNav.PropReach, crate.X + HordeNav.PropReach, crate.Y + HordeNav.PropReach, near);
            near.Sort();
            return (data, near.ToArray());
        };

        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(rig, nav);
        Assert.True(nav.Blocked > 0, $"no cell blocked of {nav.Candidates} probed");
        Assert.True(nav.Candidates > HordeNav.ProbesPerTick, "the control: the probe fit one tick");

        var a = new AiVector(2300f, 3600f, 16f);
        var b = new AiVector(2520f, 3600f, 16f);
        Assert.True(InCrate(a, b, crate), "the control: the straight line misses the crate");
        var corners = new AiVector[HordeRoute.MaxCorners];
        var arrive = new NavEdgeType[HordeRoute.MaxCorners];
        Assert.Equal(PathAnswer.Found, nav.Paths.Find(a, b, ClearanceClass.Stand, HordeRoute.SearchBudget, corners, arrive, out int n, out _));
        var from = a;
        for (int i = 0; i < n; i++)
        {
            Assert.False(InCrate(from, corners[i], crate), $"the leg {from} to {corners[i]} goes through the crate");
            from = corners[i];
        }
    }

    /// <summary>A body pressed against a prop stands in a blocked cell: it is planned from the nearest free one, never answered unreachable.</summary>
    [Fact]
    public void A_body_standing_in_a_cell_blocked_beside_a_prop_still_gets_a_path()
    {
        var (rig, nav) = Make();
        var crate = new AiVector(2400f, 3600f, 16f);
        rig.World.Put(new Vector(crate.X - 24f, crate.Y - 24f, 16f), new Vector(crate.X + 24f, crate.Y + 24f, 72f));
        nav.Work = (bytes, _) =>
        {
            var data = HordeNavData.Derive(bytes);
            var near = new List<int>();
            data.Graph.CellsNear(crate.X - HordeNav.PropReach, crate.Y - HordeNav.PropReach, crate.X + HordeNav.PropReach, crate.Y + HordeNav.PropReach, near);
            near.Sort();
            return (data, near.ToArray());
        };
        nav.Begin(Fixture.Map, HordeNavDataTests.FixtureLevel);
        UntilReady(rig, nav);
        var against = new AiVector(crate.X - 30f, crate.Y, 16f);
        var under = nav.Data!.Graph.FindCell(against + new AiVector(0f, 0f, 18f));
        Assert.True(under.IsValid && nav.Data.Open.CellBlocked(under.Index), "the control: the body's own cell is not blocked");

        var answer = nav.Paths.Find(against, new AiVector(2560f, 3900f, 16f), ClearanceClass.Stand, HordeRoute.SearchBudget,
                                    new AiVector[HordeRoute.MaxCorners], new NavEdgeType[HordeRoute.MaxCorners], out int n, out _);

        Assert.Equal(PathAnswer.Found, answer);
        Assert.True(n > 0);
    }

    /// <summary>Whether the leg passes inside the crate grown by most of a robot's half width.</summary>
    private static bool InCrate(AiVector a, AiVector b, AiVector crate)
    {
        int n = Math.Max(1, (int)((b - a).Length2D / 2f));
        for (int i = 0; i <= n; i++)
        {
            var p = a + (b - a) * ((float)i / n);
            if (MathF.Abs(p.X - crate.X) < 24f + 16f && MathF.Abs(p.Y - crate.Y) < 24f + 16f)
                return true;
        }

        return false;
    }
}
