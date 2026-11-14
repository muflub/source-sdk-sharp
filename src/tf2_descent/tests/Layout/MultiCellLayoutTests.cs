using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// Rooms larger than one cell in the layout (M24, map tools' multi-cell rooms):
/// the codec spells a footprint, the generator lays a square of cells off a
/// fill room as one room against the budget, every socket of it either joined
/// or capped as its wall, and every other rule as before.
/// </summary>
public sealed class MultiCellLayoutTests
{
    private const int Seeds = 60;

    private static IReadOnlyList<RoomInfo> Pool() =>
    [
        .. LayoutCatalogue.Rooms(),
        new RoomInfo("hall_a-nesw-h-2x2-j.b1", [], 1, 2, 4),
        new RoomInfo("grotto_a-nesw-s-2x2-j.b2", [], 1, 6, 14),
        new RoomInfo("grotto_b-nesw-s-3x3-j.b2.b3", [], 1, 6, 14),
    ];

    [Fact]
    public void A_footprint_rides_in_the_name_after_the_height()
    {
        RoomShape shape = RoomName.Parse("hall_a-nesw-h-2x2-j.b1");

        Assert.Equal((2, 2, Doors.All, JumpFlags.Jump | JumpFlags.B1), (shape.CellsX, shape.CellsY, shape.Doors, shape.Jumps));
        Assert.Equal("hall_a-nesw-h-2x2-j.b1", shape.Name);
    }

    [Theory]
    [InlineData("hall_a-nesw-h-1x1")]
    [InlineData("hall_a-nesw-h-0x2")]
    [InlineData("hall_a-nesw-h-17x2")]
    [InlineData("hall_a-nesw-h-02x2")]
    [InlineData("hall_a-nesw-h-j-2x2")]
    public void A_footprint_the_codec_cannot_hold_is_refused(string name)
    {
        Assert.False(RoomName.TryParse(name, out _, out _));
    }

    [Fact]
    public void A_room_of_one_cell_spells_no_footprint()
    {
        Assert.Equal("drift_a-ew-l", new RoomShape("drift", "a", Doors.East | Doors.West, HeightClass.Low, JumpFlags.None).Name);
    }

    private static (LevelPlan Plan, LayoutReport Report)[] Levels(int depth)
    {
        var pool = Pool();
        return [.. Enumerable.Range(1, Seeds).Select(s => new LevelLayout().Generate(depth, (ulong)s, pool))
            .Select(r => r.Ok ? (r.Plan!, r.Report!) : throw new Xunit.Sdk.XunitException(r.Error))];
    }

    /// <summary>The cells each placement covers, its anchor first.</summary>
    private static Dictionary<(int Row, int Column), int> Cover(LevelPlan plan)
    {
        var cover = new Dictionary<(int, int), int>();
        for (int i = 0; i < plan.Placements.Count; i++)
        {
            Placement p = plan.Placements[i];
            RoomShape shape = RoomName.Parse(p.Room);
            for (int r = 0; r < shape.CellsY; r++)
                for (int c = 0; c < shape.CellsX; c++)
                {
                    Assert.True(p.Row + r < plan.Rows && p.Column + c < plan.Columns, $"{p.Room} leaves the grid");
                    Assert.True(cover.TryAdd((p.Row + r, p.Column + c), i), $"{p.Room} overlaps another room at ({p.Row + r}, {p.Column + c})");
                }
        }
        return cover;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Act_one_below_level_one_lays_at_most_one_larger_room_and_mostly_one(int depth)
    {
        var levels = Levels(depth);
        var counts = levels.Select(l => l.Plan.Placements.Count(p => RoomName.Parse(p.Room).IsMultiCell)).ToArray();

        Assert.All(counts, n => Assert.InRange(n, 0, DepthTable.BigRoomsAt(depth)));
        Assert.True(counts.Count(n => n == 1) * 2 > Seeds, $"{counts.Count(n => n == 1)} of {Seeds} levels hold a larger room");
        Assert.All(levels, l => Assert.Equal(l.Report.MultiCellRooms, l.Plan.Placements.Count(p => RoomName.Parse(p.Room).IsMultiCell)));
    }

    [Fact]
    public void Level_one_lays_no_larger_room()
    {
        Assert.All(Levels(1), l => Assert.DoesNotContain(l.Plan.Placements, p => RoomName.Parse(p.Room).IsMultiCell));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    public void A_larger_room_counts_once_against_the_budget(int depth)
    {
        Assert.All(Levels(depth), l => Assert.Equal(DepthTable.For(depth).Rooms, l.Plan.Placements.Count));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(13)]
    public void Larger_rooms_overlap_nothing_and_stay_on_the_grid(int depth)
    {
        foreach (var (plan, _) in Levels(depth))
            Cover(plan);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(14)]
    public void Every_room_is_reached_from_the_arrival_through_its_doors_and_the_larger_rooms_sockets(int depth)
    {
        foreach (var (plan, _) in Levels(depth))
        {
            var cover = Cover(plan);
            // A door of the room covering a cell on a side: a larger room has one on every perimeter edge.
            bool DoorAt((int Row, int Column) cell, Doors side)
            {
                Placement p = plan.Placements[cover[cell]];
                RoomShape shape = RoomName.Parse(p.Room);
                if (!shape.IsMultiCell)
                    return (DoorSets.Rotate(shape.Doors, p.Rotation) & side) != 0;
                var next = Step(cell, side);
                return !cover.TryGetValue(next, out int other) || other != cover[cell];
            }

            int arrival = plan.Placements.ToList().FindIndex(p => p.Role == "up");
            var seen = new HashSet<int> { arrival };
            var queue = new Queue<int>([arrival]);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (var cell in cover.Where(kv => kv.Value == at).Select(kv => kv.Key))
                    foreach (Doors side in new[] { Doors.North, Doors.East, Doors.South, Doors.West })
                    {
                        var next = Step(cell, side);
                        if (cover.TryGetValue(next, out int other) && other != at && DoorAt(cell, side) && DoorAt(next, DoorSets.Opposite(side)) && seen.Add(other))
                            queue.Enqueue(other);
                    }
            }

            Assert.Equal(plan.Placements.Count, seen.Count);
        }
    }

    private static (int Row, int Column) Step((int Row, int Column) cell, Doors side) => side switch
    {
        Doors.North => (cell.Row + 1, cell.Column),
        Doors.South => (cell.Row - 1, cell.Column),
        Doors.East => (cell.Row, cell.Column + 1),
        _ => (cell.Row, cell.Column - 1),
    };

    [Theory]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(14)]
    public void No_door_of_a_room_of_one_cell_is_capped_or_wasted_beside_a_larger_room(int depth)
    {
        Assert.All(Levels(depth), l => Assert.Equal((0, 0), (l.Report.CappedDoors, l.Report.WastedDoors)));
    }

    [Fact]
    public void The_same_seed_lays_the_same_larger_rooms()
    {
        var pool = Pool();
        var a = new LevelLayout().Generate(7, 42, pool).Plan!;
        var b = new LevelLayout().Generate(7, 42, pool).Plan!;

        Assert.Equal(a.Placements, b.Placements);
    }

    [Fact]
    public void A_pool_without_larger_rooms_lays_the_same_level_as_before_they_existed()
    {
        // The draw for larger rooms costs no number of the generator when the pool has none.
        var plain = LayoutCatalogue.Rooms();
        var withBig = Pool().Where(r => !RoomName.Parse(r.Name).IsMultiCell || r.MinDepth > 4).ToList();
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
            Assert.Equal(new LevelLayout().Generate(2, seed, plain).Plan!.Placements, new LevelLayout().Generate(2, seed, withBig).Plan!.Placements);
    }
}
