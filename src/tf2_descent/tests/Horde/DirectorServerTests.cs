using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Director;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The spawn director on the server (plan_descent_logic HO9): each pack marker's clearance
/// measured from the world at level ready, the roster rolled for the party present then, and
/// "live, a level's roster matches its replay": the roster the server built for the committed
/// depth 1 seed 1 level equals the director's answer for the same inputs, robot by robot.
/// </summary>
public sealed class DirectorServerTests
{
    private static MarkerClearances Measurer(BoxMotionWorld world) => new(world, new RigRules());

    [Fact]
    public void A_marker_on_open_floor_reaches_as_far_as_the_measure_looks()
    {
        var c = Measurer(new BoxMotionWorld { FloorZ = 0 }).At(At(0, 0, 0));

        float far = MarkerClearances.Reach + MarkerClearances.ProbeHalf;
        Assert.Equal((far, far, far, far), (c.West, c.East, c.South, c.North));
        Assert.Equal(MarkerClearances.Lift + MarkerClearances.Reach, c.Height, 3);
    }

    [Fact]
    public void A_wall_and_a_ceiling_bound_a_markers_clearance()
    {
        var world = new BoxMotionWorld { FloorZ = 0 }
            .Put(new Vector(100, -500, 0), new Vector(116, 500, 400))      // a wall 100 east
            .Put(new Vector(-500, -500, 120), new Vector(500, 500, 136));  // a ceiling 120 up

        var c = Measurer(world).At(At(0, 0, 0));

        Assert.Equal(100, c.East, 0);
        Assert.Equal(120, c.Height, 0);
        Assert.True(c.West > 500);
        Assert.False(c.Holds(24 * 1.6f, 82 * 1.6f));   // a giant's 131 does not stand under 120
        Assert.True(c.Holds(24, 82));
    }

    [Fact]
    public void A_step_beside_a_marker_is_no_wall()
    {
        var world = new BoxMotionWorld { FloorZ = 0 }.Put(new Vector(40, -500, 0), new Vector(600, 500, 16));

        Assert.True(Measurer(world).At(At(0, 0, 0)).East > 500);
    }

    [Fact]
    public void A_marker_inside_a_solid_holds_nothing_and_is_counted()
    {
        var world = new BoxMotionWorld { FloorZ = 0 }.Put(new Vector(200, 200, -10), new Vector(400, 400, 300));
        var level = HordeLevel.Create(1, 1, [new HordeRoom(0, At(0, 0, -64), At(1024, 1024, 512), [new PackMarker(At(300, 300), 0), new PackMarker(At(700, 700), 0)])], []);
        var measurer = Measurer(world);

        var measured = measurer.Measure(level);

        Assert.Equal(0, measured.Rooms[0].Markers[0].Clearance!.Value.Narrowest);
        Assert.True(measured.Rooms[0].Markers[1].Clearance!.Value.Narrowest > 100);
        Assert.Equal((2, 1), measurer.Last);
    }

    [Fact]
    public void The_level_ready_roster_is_rolled_for_the_party_present_on_measured_markers()
    {
        var rig = new ServerHordeRig(begin: false);
        var levels = new OneLevel(Row(4, markers: 3, depth: 3));
        var party = new FixedParty(3);
        var service = new HordeService(new ActiveMode(), levels, rig.Spawner, new FixedClock(), rig.Parking,
            new HordeCensus(rig.Spawner, rig.Parking, rig.Log), new HordeTickTimer(), rig.Log, Measurer(rig.World), party, rig.Nav());

        service.OnLevelReady("tf2_descent-3-s7");

        var c = service.Current!;
        Assert.Equal(3, c.PartySize);
        Assert.Equal(3, c.Plan!.PartySize);
        Assert.Equal(0, c.Plan.Unmeasured);
        Assert.All(c.Level!.Rooms.SelectMany(r => r.Markers), m => Assert.NotNull(m.Clearance));
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("measured 12 pack markers (0 in solid); the roster is rolled for a party of 3"));
        party.Size = 1;   // a hero joining later changes nothing
        Assert.Equal(3, c.PartySize);
    }

    [Fact]
    public void The_servers_roster_for_depth_1_seed_1_is_the_directors_replay_robot_by_robot()
    {
        var level = LevelAdapters.ToHorde(LevelFixture.Committed().Read());
        var world = new BoxMotionWorld { FloorZ = -1 };
        var measured = Measurer(world).Measure(level);
        var rig = new ServerHordeRig(measured);

        var held = rig.Roster.Rows().ToList();
        var replay = new SpawnDirector(new TF2.Descent.Rules.TF2DescentRules()).Plan(Measurer(new BoxMotionWorld { FloorZ = -1 }).Measure(level), 1);

        Assert.Empty(RosterRow.Differences(held, [.. replay.Rows()]));
        Assert.Equal(replay.Digest(), RosterRow.Digest(held));
        Assert.Equal(26, level.Rooms.Sum(r => r.Markers.Length));
        Assert.Equal((48, 16), (rig.Roster.Count, rig.Roster.PackCount));
        Assert.Equal(0, replay.Crowded);
    }

    /// <summary>
    /// The server plans with the level's navigation (HO9): at level ready the nav is derived first,
    /// on the game thread, and the controller's roster is planned with its reach, so the director
    /// command replays with the same reach and finds the same roster.
    /// </summary>
    [Fact]
    public void The_level_ready_roster_is_planned_with_the_levels_navigation_reach()
    {
        var rig = new ServerHordeRig(begin: false);
        var level = LevelAdapters.ToHorde(LevelFixture.Committed().Read());
        var nav = rig.Nav(new TF2.Descent.Tests.Level.LevelServiceTests.LevelFilesOnDisk(LevelFixture.Committed()));
        var service = new HordeService(new ActiveMode(), new OneLevel(level), rig.Spawner, new FixedClock(), rig.Parking,
            new HordeCensus(rig.Spawner, rig.Parking, rig.Log), new HordeTickTimer(), rig.Log, Measurer(rig.World), new FixedParty(1), nav);

        service.OnLevelReady("tf2_descent-1-s1");

        var c = service.Current!;
        Assert.IsType<TF2.Descent.Server.Horde.Nav.NavHullReach>(c.Reach);
        Assert.Equal(TF2.Descent.Server.Horde.Nav.HordeNavState.Probing, nav.State);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("at level ready, for the spawn director"));
        new DirectorCommand(service, new RigRules(), rig.World, rig.Log).Execute();
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("[TF2Descent/Director] roster ") && l.Text.Contains(": the same; census: "));
    }

    [Fact]
    public void The_director_command_says_the_roster_is_its_replay_and_names_a_row_that_is_not()
    {
        var rig = new ServerHordeRig(begin: false);
        var service = new HordeService(new ActiveMode(), new OneLevel(Row(4, markers: 3, depth: 3)), rig.Spawner, new FixedClock(), rig.Parking,
            new HordeCensus(rig.Spawner, rig.Parking, rig.Log), new HordeTickTimer(), rig.Log, Measurer(rig.World), new FixedParty(2), rig.Nav());
        service.OnLevelReady("tf2_descent-3-s7");
        var command = new DirectorCommand(service, new RigRules(), rig.World, rig.Log);

        command.Execute();
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("[TF2Descent/Director] roster ") && l.Text.Contains(": the same; census: "));

        var c = service.Current!;
        c.SpawnByHand("scout", 1.0, Centre(1), 0, 9, rig.Now, out _);
        command.Execute();
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains(": 1 differences;"));
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains($"row {c.Roster!.Count - 1}: roster pack ") && l.Text.EndsWith("/ replay (none)\n"));
    }

}

/// <summary>The Descent mode on.</summary>
internal sealed class ActiveMode : TF2.Descent.Server.Mode.IDescentModeState
{
    public bool Active => true;
}

/// <summary>A level source with one level for every map.</summary>
internal sealed class OneLevel(HordeLevel level) : IHordeLevelSource
{
    public HordeLevel? LevelFor(string mapName, int depth, ulong seed) => level;
}

/// <summary>The horde's clock, set by a fact.</summary>
internal sealed class FixedClock : IHordeClock
{
    public long Tick { get; set; } = 500;
    public double TicksPerSecond => ServerHordeRig.Tps;
}
