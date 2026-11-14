using SourceSharp.Abi;
using TF2.Descent.Rules.Layout;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>
/// The level as data (plan_descent_logic WO1) over real linked levels: the committed depth 1
/// seed 1 fixture always, and depth 1 seed 2 and depth 2 seed 1 when this tree built them.
/// </summary>
public sealed class LevelReaderTests
{
    private static readonly LevelFixture Fixture = LevelFixture.Committed();

    /// <summary>The gate's checks over one level (the plan's WO1 row).</summary>
    private static void HoldsTheGate(LevelFixture fixture)
    {
        LevelData level = fixture.Read();

        // The room count equals the layout's: the generator's own count, and the generator run again.
        var (rooms, joints) = fixture.GeneratorCounts();
        Assert.Equal(rooms, level.Rooms.Length);
        Assert.Equal(joints, level.Doors.Length);
        LayoutResult again = new LevelLayout().Generate(fixture.Depth, fixture.Seed, fixture.LibraryRooms());
        Assert.True(again.Ok, again.Error);
        Assert.Equal(
            again.Plan!.Placements.Select(p => (p.Room, p.Column, p.Row, p.Rotation / 90 % 4)).Order(),
            level.Rooms.Select(r => (r.Name, r.Column, r.Row, r.Rotation)).Order());

        // Every room has the marker kinds its library entry gives it (the tools' own arrival point aside).
        var library = fixture.LibraryMarkers();
        Assert.True(level.Markers.Length > 0);
        foreach (LevelRoom room in level.Rooms)
        {
            var have = level.In(room.Id).Where(m => m.Kind != LevelMarkerKind.Arrival).Select(m => m.PoiType).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(library[room.Name].Order(StringComparer.Ordinal), have);
        }

        // Every marker stands in the room it is filed under.
        Assert.All(level.Markers, m => Assert.True(m.Room >= 0 && level.Rooms[m.Room].Contains(m.Position),
            $"marker {m.Id} {m.PoiType} at {LevelSummary.At(m.Position)} is not inside room {m.Room}"));

        // A party of four arrives at four distinct points, in the arrival room.
        Assert.True(level.Arrivals.Length >= 4, $"{level.Arrivals.Length} arrival points");
        var party = new List<int>();
        for (int hero = 0; hero < 4; hero++)
            party.Add(ArrivalPicker.Pick(level.Arrivals.Length, party, null));
        Assert.Equal(4, party.Select(i => level.Arrivals[i].Position).Distinct().Count());
        Assert.All(level.Arrivals, a => Assert.Equal(LevelRoomRole.Arrival, level.Rooms[a.Room].Role));
    }

    [Fact]
    public void Depth_1_seed_1_holds_the_gate() => HoldsTheGate(Fixture);

    [BuiltLevelFact(1, 2)]
    public void Depth_1_seed_2_holds_the_gate() => HoldsTheGate(LevelFixture.Built(1, 2));

    [BuiltLevelFact(2, 1)]
    public void Depth_2_seed_1_holds_the_gate() => HoldsTheGate(LevelFixture.Built(2, 1));

    [Fact]
    public void Every_piece_of_a_linked_level_is_shipped()
    {
        LevelData level = Fixture.Read();

        Assert.Empty(level.Notes);
        foreach (LevelPiece piece in new[] { LevelPiece.Rooms, LevelPiece.Doors, LevelPiece.Markers, LevelPiece.Arrivals, LevelPiece.Exit, LevelPiece.Seed })
            Assert.Equal(PieceSource.Shipped, level.Source(piece));
        Assert.False(level.Sources.ContainsKey(LevelPiece.BossArena));   // depth 1 is no boss depth
        Assert.Equal(1UL, level.Seed);
    }

    [Fact]
    public void Rooms_take_their_role_from_the_transition_and_their_name()
    {
        LevelData level = Fixture.Read();

        LevelRoom arrival = Assert.Single(level.Rooms, r => r.Role == LevelRoomRole.Arrival);
        LevelRoom exit = Assert.Single(level.Rooms, r => r.Role == LevelRoomRole.Exit);
        Assert.Equal("arrival", arrival.Family);
        Assert.Equal("exit", exit.Family);
        Assert.Equal("Way in", arrival.Label);
        Assert.Single(level.Rooms, r => r.Role == LevelRoomRole.Treasure);
        Assert.Contains(level.Rooms, r => r.Low);
    }

    [Fact]
    public void A_room_is_its_cells_up_to_its_height()
    {
        LevelRoom arrival = Fixture.Read().Rooms.Single(r => r.Role == LevelRoomRole.Arrival);

        // arrival_shaftb-ew-h at column 2, row 4, a hall 768 high (the dump of 2026-11-11).
        Assert.Equal((2, 4, 1, 1), (arrival.Column, arrival.Row, arrival.CellsX, arrival.CellsY));
        Assert.Equal(new Vector(2048, 4096, 0), arrival.Min);
        Assert.Equal(new Vector(3072, 5120, 768), arrival.Max);
    }

    [Fact]
    public void A_door_is_its_opening_with_a_normal_from_the_lower_room_into_the_higher()
    {
        LevelData level = Fixture.Read();

        Assert.All(level.Doors, d =>
        {
            Assert.True(d.RoomA < d.RoomB);
            Assert.Equal(208, d.Width, 0.01);
            Assert.Equal(192, d.Height, 0.01);
            Assert.Equal(1, d.Normal.Length, 1e-4);
            Assert.Equal(0, d.Normal.Z);
            Assert.True(d.Normal.Dot(level.Rooms[d.RoomB].Centre - d.Position) > 0, $"door {d.Id} faces away from room {d.RoomB}");
            Assert.True(d.Normal.Dot(level.Rooms[d.RoomA].Centre - d.Position) < 0, $"door {d.Id} faces into room {d.RoomA}");
            // On the face the two rooms' cells share.
            LevelRoom a = level.Rooms[d.RoomA], b = level.Rooms[d.RoomB];
            Assert.True(MathF.Abs(d.Position.X - (d.Normal.X > 0 ? a.Max.X : d.Normal.X < 0 ? a.Min.X : d.Position.X)) < 0.01f);
            Assert.True(MathF.Abs(d.Position.Y - (d.Normal.Y > 0 ? a.Max.Y : d.Normal.Y < 0 ? a.Min.Y : d.Position.Y)) < 0.01f);
            Assert.True(b.Contains(d.Position) && a.Contains(d.Position));
        });
        Assert.Equal(level.Doors.Length, level.Doors.Select(d => (d.RoomA, d.RoomB)).Distinct().Count());
    }

    [Fact]
    public void The_exit_is_the_way_downs_volume_in_the_exit_room()
    {
        LevelData level = Fixture.Read();

        LevelExit exit = Assert.IsType<LevelExit>(level.Exit);
        Assert.Equal(LevelRoomRole.Exit, level.Rooms[exit.Room].Role);
        Assert.Equal("tf2_descent-2", exit.Map);
        Assert.True(level.Rooms[exit.Room].Contains(exit.Position));
    }

    [Fact]
    public void The_arrival_points_are_the_up_rooms_four_spawns_then_its_arrival()
    {
        LevelData level = Fixture.Read();

        Assert.Equal(5, level.Arrivals.Length);
        var up = level.Rooms.Single(r => r.Role == LevelRoomRole.Arrival).Id;
        Assert.Equal(level.In(up).Where(m => m.Kind == LevelMarkerKind.Spawn).Select(m => m.Position), level.Arrivals.Take(4).Select(a => a.Position));
        Assert.Equal(level.In(up).Single(m => m.Kind == LevelMarkerKind.Arrival).Position, level.Arrivals[4].Position);
    }

    [Fact]
    public void Without_a_map2d_rooms_and_doors_are_derived_from_the_nav3d_and_agree()
    {
        LevelData shipped = Fixture.Read();
        LevelData derived = LevelReader.Read(Fixture.Files(map2d: false));

        Assert.Equal(PieceSource.Derived, derived.Source(LevelPiece.Rooms));
        Assert.Equal(PieceSource.Derived, derived.Source(LevelPiece.Doors));
        Assert.Equal(PieceSource.Derived, derived.Source(LevelPiece.Exit));
        Assert.Contains(derived.Notes, n => n.Contains("no maps/tf2_descent-1-s1.map2d", StringComparison.Ordinal));
        Assert.Equal(shipped.Rooms.Select(r => (r.Name, r.Column, r.Row, r.Min, r.Max, r.Role)), derived.Rooms.Select(r => (r.Name, r.Column, r.Row, r.Min, r.Max, r.Role)));
        Assert.Equal(shipped.Doors.Select(d => (d.RoomA, d.RoomB)).Order(), derived.Doors.Select(d => (d.RoomA, d.RoomB)).Order());
        foreach (LevelDoor d in derived.Doors)
        {
            LevelDoor s = shipped.Doors.Single(x => x.RoomA == d.RoomA && x.RoomB == d.RoomB);
            Assert.True((s.Position - d.Position).Length < 1, $"door {d.RoomA}-{d.RoomB}: {LevelSummary.At(s.Position)} against {LevelSummary.At(d.Position)}");
            Assert.True((s.Normal - d.Normal).Length < 1e-4f, $"door {d.RoomA}-{d.RoomB}: normal {LevelSummary.At(s.Normal)} against {LevelSummary.At(d.Normal)}");
        }
        Assert.Equal(shipped.Markers.Select(m => (m.PoiType, m.Room)), derived.Markers.Select(m => (m.PoiType, m.Room)));
        Assert.Equal(shipped.Exit!.Room, derived.Exit!.Room);
    }

    [Fact]
    public void Without_a_nav3d_markers_and_arrivals_are_absent_and_said()
    {
        LevelData level = LevelReader.Read(Fixture.Files(nav3d: false));

        Assert.Empty(level.Markers);
        Assert.Empty(level.Arrivals);
        Assert.Equal(PieceSource.Absent, level.Source(LevelPiece.Markers));
        Assert.Equal(PieceSource.Absent, level.Source(LevelPiece.Arrivals));
        Assert.Equal(PieceSource.Shipped, level.Source(LevelPiece.Rooms));
        Assert.Contains("no maps/tf2_descent-1-s1.nav3d", level.Notes);
        Assert.Contains("missing: arrivals, markers", LevelSummary.LoadLine(level, TimeSpan.Zero), StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_files_are_absent_and_never_throw()
    {
        LevelData level = LevelReader.Read(new LevelFiles("tf2_descent-1-s1", 1, null, "none", [1, 2, 3], [4, 5, 6]));

        Assert.Empty(level.Rooms);
        Assert.Equal(PieceSource.Absent, level.Source(LevelPiece.Rooms));
        Assert.Equal(PieceSource.Absent, level.Source(LevelPiece.Seed));
        Assert.Equal(2, level.Notes.Count(n => n.Contains("is unreadable", StringComparison.Ordinal)));
    }

    [BuiltLevelFact(1, 2)]
    public void A_map2d_of_another_level_is_not_read()
    {
        LevelFixture other = LevelFixture.Built(1, 2);

        LevelData level = LevelReader.Read(Fixture.Files() with { Map2d = other.Map2d });

        Assert.Contains(level.Notes, n => n.Contains("is not the .nav3d's level", StringComparison.Ordinal));
        Assert.Equal(PieceSource.Derived, level.Source(LevelPiece.Rooms));
        Assert.Equal(Fixture.Read().Rooms.Select(r => r.Name), level.Rooms.Select(r => r.Name));
    }

    [BuiltLevelFact(2, 1)]
    public void A_larger_room_is_one_room_over_its_cells_shipped_or_derived()
    {
        LevelFixture fixture = LevelFixture.Built(2, 1);
        LevelData shipped = fixture.Read();
        LevelData derived = LevelReader.Read(fixture.Files(map2d: false));

        LevelRoom hall = Assert.Single(shipped.Rooms, r => r.CellsX > 1);
        Assert.Equal((2, 2), (hall.CellsX, hall.CellsY));
        Assert.Equal(shipped.Rooms.Select(r => (r.Name, r.Column, r.Row, r.CellsX, r.CellsY, r.Min.X, r.Max.X, r.Min.Y, r.Max.Y)),
            derived.Rooms.Select(r => (r.Name, r.Column, r.Row, r.CellsX, r.CellsY, r.Min.X, r.Max.X, r.Min.Y, r.Max.Y)));
    }

    [Fact]
    public void Marker_kinds_round_trip_their_poi_types()
    {
        foreach (LevelMarkerKind kind in Enum.GetValues<LevelMarkerKind>())
            Assert.Equal(kind, LevelReader.KindOf(LevelReader.PoiType(kind)));
        Assert.Null(LevelReader.KindOf("door"));
    }
}
