using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Tests;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Director;
using TF2.Descent.Server.Mode;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's life with the level (plan_descent_logic HO2): one controller a
/// generated level when the Descent mode plays, none on the town or another
/// map, nothing left at shutdown, and a fresh one on the next level.
/// </summary>
public sealed class HordeServiceTests
{
    private sealed class Mode(bool active) : IDescentModeState
    {
        public bool Active => active;
    }

    private sealed class Levels(HordeLevel? level) : IHordeLevelSource
    {
        public List<(string Map, int Depth, ulong Seed)> Asked { get; } = [];

        public HordeLevel? LevelFor(string mapName, int depth, ulong seed)
        {
            Asked.Add((mapName, depth, seed));
            return level;
        }
    }

    private sealed class Clock : IHordeClock
    {
        public long Tick { get; set; } = 500;
        public double TicksPerSecond => ServerHordeRig.Tps;
    }

    private sealed class Kit
    {
        public Kit(bool active = true, HordeLevel? level = null)
        {
            Rig = new ServerHordeRig(begin: false);
            Levels = new Levels(level ?? Row(3));
            Census = new HordeCensus(Rig.Spawner, Rig.Parking, Rig.Log);
            Service = new HordeService(new Mode(active), Levels, Rig.Spawner, Clock, Rig.Parking, Census, new HordeTickTimer(), Rig.Log,
                new MarkerClearances(Rig.World, new RigRules()), Party);
        }

        public ServerHordeRig Rig { get; }
        public Levels Levels { get; }
        public Clock Clock { get; } = new();
        public FixedParty Party { get; } = new(1);
        public HordeCensus Census { get; }
        public HordeService Service { get; }
    }

    [Fact]
    public void A_generated_level_gets_one_controller_with_its_pool()
    {
        var kit = new Kit();

        kit.Service.OnLevelReady("tf2_descent-1-s1");

        Assert.Equal(1, kit.Service.ControllersMade);
        Assert.True(kit.Service.Current!.Begun);
        Assert.Equal(1, kit.Rig.Spawner.Controllers);
        Assert.Equal(49, kit.Rig.Spawner.Bodies);
        Assert.Equal(("tf2_descent-1-s1", 1, 1UL), kit.Levels.Asked.Single());
    }

    [Fact]
    public void The_town_gets_no_horde()
    {
        var kit = new Kit();

        kit.Service.OnLevelReady("tf2_descent_town");

        Assert.Null(kit.Service.Current);
        Assert.Equal(0, kit.Rig.Spawner.Controllers);
        Assert.Empty(kit.Levels.Asked);
    }

    [Fact]
    public void Another_mode_gets_no_horde_on_a_level()
    {
        var kit = new Kit(active: false);

        kit.Service.OnLevelReady("tf2_descent-2-s9");

        Assert.Null(kit.Service.Current);
        Assert.Equal(0, kit.Rig.Spawner.Controllers);
    }

    [Fact]
    public void The_level_ready_line_names_robots_packs_bodies_weapons_and_entities()
    {
        var kit = new Kit();

        kit.Service.OnLevelReady("tf2_descent-1-s1");

        var roll = kit.Service.Current!.Roll!;
        Assert.Contains(kit.Rig.Log.Lines,
            m => !m.Warning && m.Text.Contains($"[TF2Descent/Horde] level ready: {roll.Robots} robots in {roll.Packs} packs, 49 bodies, 49 weapons, {25 + 98} entities (map 25)"));
    }

    [Fact]
    public void The_census_warns_once_past_the_maps_count_plus_512()
    {
        var kit = new Kit();
        kit.Service.OnLevelReady("tf2_descent-1-s1");
        var controller = kit.Service.Current!;
        Assert.False(kit.Census.OverBudget);

        kit.Rig.Spawner.EdictCount = 25 + 513;
        kit.Census.Line(controller);
        kit.Census.Line(controller);

        Assert.True(kit.Census.OverBudget);
        Assert.Single(kit.Rig.Log.Lines, w => w.Warning && w.Text.Contains("OVER THE EDICT BUDGET"));
    }

    [Fact]
    public void Shutdown_leaves_no_controller_and_nothing_parked()
    {
        var kit = new Kit();
        kit.Service.OnLevelReady("tf2_descent-1-s1");
        var controller = kit.Service.Current!;
        Assert.Equal(98, kit.Rig.Parking.Count);

        kit.Service.OnLevelShutdownPreEntity();

        Assert.Null(kit.Service.Current);
        Assert.False(controller.Begun);
        Assert.Null(controller.Roster);
        Assert.Null(controller.Bodies);
        Assert.Equal(0, kit.Rig.Parking.Count);
    }

    [Fact]
    public void The_next_level_gets_a_fresh_controller()
    {
        var kit = new Kit();
        kit.Service.OnLevelReady("tf2_descent-1-s1");
        var first = kit.Service.Current!;
        kit.Service.OnLevelShutdownPreEntity();

        kit.Service.OnLevelReady("tf2_descent-1-s1");

        Assert.Equal(2, kit.Service.ControllersMade);
        Assert.NotSame(first, kit.Service.Current);
        Assert.True(kit.Service.Current!.Begun);
        Assert.Equal(98, kit.Rig.Parking.Count);
    }

    [Fact]
    public void A_think_ticks_the_controller()
    {
        var kit = new Kit();
        kit.Service.OnLevelReady("tf2_descent-1-s1");

        kit.Clock.Tick = 600;
        kit.Service.OnThink(0.015f);

        Assert.Equal(1, kit.Service.Current!.Ticks);
        Assert.Equal(1, kit.Service.Current.Decisions);
    }

    [Theory]
    [InlineData("tf2_descent-1-s1", true, 1, 1UL)]
    [InlineData("tf2_descent-12-s987654", true, 12, 987654UL)]
    [InlineData("tf2_descent_town", false, 0, 0UL)]
    [InlineData("tf2_descent-1", false, 0, 0UL)]
    [InlineData("tf2_descent-0-s3", false, 0, 0UL)]
    [InlineData("ctf_2fort", false, 0, 0UL)]
    public void Only_a_generated_level_is_a_level(string map, bool level, int depth, ulong seed)
    {
        Assert.Equal(level, DescentLevelName.TryParse(map, out int d, out ulong s));
        if (level)
        {
            Assert.Equal(depth, d);
            Assert.Equal(seed, s);
        }
    }

    [Fact]
    public void The_stand_in_level_is_a_row_of_cells_north_of_the_arrival()
    {
        var level = StandInLevel.Around(new AiVector(2336, 4608, 16), 1, 1, p => p, out int dropped);

        Assert.Equal(0, dropped);
        Assert.Equal(StandInLevel.Rooms, level.RoomCount);
        Assert.Equal(0, level.RoomAt(new AiVector(2336, 4608, 16)));
        Assert.Empty(level.Rooms[0].Markers);
        Assert.All(level.Rooms.Skip(1), r => Assert.Equal(StandInLevel.MarkersPerRoom, r.Markers.Length));
        Assert.All(level.Rooms.Skip(1), r => Assert.All(r.Markers, m => Assert.Equal(r.Id, level.RoomAt(m.Position))));
        Assert.Equal(3, level.Distance(0, 3));
    }

    [Fact]
    public void A_stand_in_marker_with_no_room_to_stand_is_left_out()
    {
        int asked = 0;
        var level = StandInLevel.Around(new AiVector(2336, 4608, 16), 1, 1, p => ++asked % 3 == 0 ? null : p, out int dropped);

        Assert.Equal(12, asked);
        Assert.Equal(4, dropped);
        Assert.Equal(8, level.Rooms.Sum(r => r.Markers.Length));
    }
}
