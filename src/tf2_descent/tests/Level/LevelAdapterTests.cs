using System.Collections.Immutable;
using SourceSharp.Abi;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Automap;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Server.Level;
using Xunit;
using LevelMarker = TF2.Descent.Server.Level.LevelMarker;
using LevelMarkerKind = TF2.Descent.Server.Level.LevelMarkerKind;

namespace TF2.Descent.Tests.Level;

/// <summary>The level as each consumer takes it (WO1, mine plan D5), over the real depth 1 seed 1 level.</summary>
public sealed class LevelAdapterTests
{
    private static readonly LevelData Level = LevelFixture.Committed().Read();

    [Fact]
    public void The_horde_gets_every_room_by_id_with_its_pack_markers()
    {
        HordeLevel horde = LevelAdapters.ToHorde(Level);

        Assert.Equal(Level.Rooms.Length, horde.RoomCount);
        Assert.Equal(1UL, horde.Seed);
        Assert.Equal(1, horde.Depth);
        for (int i = 0; i < horde.RoomCount; i++)
        {
            Assert.Equal(i, horde.Rooms[i].Id);
            Assert.Equal(Level.In(i).Where(m => m.Kind == LevelMarkerKind.PackSpawn).Select(m => (m.Position.X, m.Position.Y, m.Position.Z, m.Yaw)),
                horde.Rooms[i].Markers.Select(m => (m.Position.X, m.Position.Y, m.Position.Z, m.Yaw)));
        }
        Assert.Equal(26, horde.Rooms.Sum(r => r.Markers.Length));
    }

    [Fact]
    public void The_hordes_doors_point_from_room_a_into_room_b_and_join_the_room_graph()
    {
        HordeLevel horde = LevelAdapters.ToHorde(Level);

        Assert.Equal(Level.Doors.Length, horde.Doors.Length);
        foreach (HordeDoor d in horde.Doors)
        {
            HordeRoom b = horde.Rooms[d.RoomB];
            var toB = new SourceSharp.Ai.AiVector((b.Min.X + b.Max.X) / 2 - d.Position.X, (b.Min.Y + b.Max.Y) / 2 - d.Position.Y, 0);
            Assert.True(d.Normal.X * toB.X + d.Normal.Y * toB.Y > 0);
            Assert.Equal(208, d.Width, 0.01);
        }
        // Every room reaches the exit room through doors.
        int exit = Level.Exit!.Room;
        Assert.All(Enumerable.Range(0, horde.RoomCount), r => Assert.NotEqual(HordeLevel.Unreachable, horde.Distance(r, exit)));
    }

    [Fact]
    public void A_pair_joined_twice_keeps_its_lowest_doorway_for_the_horde()
    {
        LevelDoor first = Level.Doors[0];
        var second = first with { Id = Level.Doors.Length, Position = first.Position + new Vector(0, 0, 1) };
        LevelData twice = Level with { Doors = Level.Doors.Add(second) };

        HordeLevel horde = LevelAdapters.ToHorde(twice);

        Assert.Equal(Level.Doors.Length, horde.Doors.Length);
        Assert.Equal(first.Position.Z, horde.Doors[horde.DoorBetween(first.RoomA, first.RoomB)].Position.Z);
    }

    [Fact]
    public void A_level_with_no_seed_yet_gives_the_horde_none_to_roll_from()
    {
        var unseeded = Level with { Seed = null, SeedFrom = "not yet: Booting has not answered" };

        var e = Assert.Throws<InvalidOperationException>(() => LevelAdapters.ToHorde(unseeded));
        Assert.Contains("Booting has not answered", e.Message, StringComparison.Ordinal);
        Assert.Equal(7UL, LevelAdapters.ToHorde(unseeded, 7).Seed);
    }

    [Fact]
    public void The_boss_stands_on_its_arenas_first_pack_marker()
    {
        int arena = Level.Rooms.Single(r => r.Role == LevelRoomRole.Treasure).Id;   // any room with pack markers, as an arena
        var boss = Level with { BossArena = arena };

        HordeBoss b = Assert.IsType<HordeBoss>(LevelAdapters.ToHorde(boss, "sir_nukesalot").Boss);

        LevelMarker spot = Level.In(arena).First(m => m.Kind == LevelMarkerKind.PackSpawn);
        Assert.Equal(("sir_nukesalot", arena), (b.Template, b.ArenaRoom));
        Assert.Equal((spot.Position.X, spot.Position.Y), (b.Spot.Position.X, b.Spot.Position.Y));
        Assert.Null(LevelAdapters.ToHorde(Level, "sir_nukesalot").Boss);   // no arena, no boss
    }

    [Fact]
    public void Furniture_gets_each_rooms_furniture_markers_and_nothing_else()
    {
        IReadOnlyList<RoomMarkers> rooms = LevelAdapters.ToFurniture(Level);

        Assert.Equal(Level.Rooms.Length, rooms.Count);
        var markers = rooms.SelectMany(r => r.Markers).ToList();
        Assert.Equal(17, markers.Count);   // barrels 8, containers 4, dispenser 1, shrines 2, traps 2
        Assert.Equal(markers.Count, markers.Select(m => m.Id).Distinct().Count());
        Assert.DoesNotContain(markers, m => m.Poi is "descent_spawn_pack" or "spawn" or "arrival");
        foreach (RoomMarkers room in rooms)
            Assert.All(room.Markers, m => Assert.Equal(room.Room, LevelAdapters.RoomKey(Level.Markers[int.Parse(m.Id[1..])].Room)));
    }

    [Fact]
    public void The_furniture_rules_furnish_the_real_level()
    {
        var rules = new FurnitureRules(DescentContent.Shipped());

        Furnishing furnished = rules.Placement.Furnish(1, LevelAdapters.ToFurniture(Level));

        Assert.NotEmpty(furnished.Placed);
        Assert.All(furnished.Placed, p => Assert.True(Level.Markers[int.Parse(p.Marker[1..])].Room.ToString() == p.Room));
    }

    [Fact]
    public void The_automap_gets_the_room_graph_and_the_fixed_markers()
    {
        string container = LevelAdapters.MarkerKey(Level.OfKind(LevelMarkerKind.Container).First().Id);

        AutomapLevel map = LevelAdapters.ToAutomap(Level, [container]);

        Assert.Equal(Level.Rooms.Length, map.Rooms.Length);
        Assert.Equal(Level.Doors.Length, map.Doors.Length);
        string up = LevelAdapters.RoomKey(Level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id);
        string down = LevelAdapters.RoomKey(Level.Exit!.Room);
        Assert.Equal(up, Assert.Single(map.Markers, m => m.Kind == Rules.Automap.LevelMarkerKind.Arrival).Room);
        Assert.Equal(down, Assert.Single(map.Markers, m => m.Kind == Rules.Automap.LevelMarkerKind.Stairs).Room);
        Assert.Equal(2, map.Markers.Count(m => m.Kind == Rules.Automap.LevelMarkerKind.Shrine));
        Assert.Equal(container, Assert.Single(map.Markers, m => m.Kind == Rules.Automap.LevelMarkerKind.LockedContainer).Id);
    }

    [Fact]
    public void Entering_the_arrival_room_reveals_its_doors_onward()
    {
        AutomapLevel map = LevelAdapters.ToAutomap(Level);
        string up = LevelAdapters.RoomKey(Level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id);

        var seen = AutomapRules.Enter(Rules.Automap.Automap.Empty, map, up);
        AutomapView view = AutomapRules.View(seen, map, "hero", []);

        Assert.Equal([up], view.Rooms.Select(r => r.Id));
        Assert.Equal(2, view.Doors.Count);   // arrival_shaftb-ew-h: two doors, both onward to unentered rooms
        Assert.All(view.Doors, d => Assert.Null(d.To));
    }
}
