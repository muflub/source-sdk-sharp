using SourceSharp.Host.Contracts;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// No pack in sight of the way in (brief p9, an ask of the game-logic session: a whole-run playtest
/// at depth 3 seed 1 lost the hero within seconds of arriving, twice, to the packs of the room next
/// to the arrival). The arrival room holds no pack spawn, and neither does a room one door from it:
/// the layout puts only quiet rooms there (<see cref="RoomCatalogue.QuietTwins"/>, the name's flag
/// <c>q</c>). A boss level's row (arrival, antechamber, arena) takes a quiet lobby between the way in
/// and the antechamber, whose packs were in the arrival's line of sight through the doorway.
/// </summary>
public sealed class DescentArrivalTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    private const int Seeds = 20;

    public static TheoryData<int> NormalDepths => [.. Enumerable.Range(1, 14).Where(d => !DepthTable.For(d).IsBoss)];

    public static TheoryData<int> AllDepths => [.. Enumerable.Range(1, 15)];

    public static TheoryData<int> BossDepths => [.. Enumerable.Range(1, 15).Where(d => DepthTable.For(d).IsBoss)];

    private static int Packs(Room room) => room.Markers.Count(m => m.Kind == MarkerKind.PackSpawn);

    private static bool IsWayIn(string name) => RoomName.Parse(name).Family is "arrival" or "camp";

    /// <summary>The placements joined to <paramref name="at"/>: one cell over, each with a door facing the other.</summary>
    /// <summary>Each door of a placement as the level grid's cell and the side it opens on: a larger room's (unturned, level 5's on the 512 kit) on each of its sockets.</summary>
    private static IEnumerable<(int Row, int Column, Doors Side)> DoorEdges(Placement p)
    {
        var shape = RoomName.Parse(p.Room);
        if (!shape.IsMultiCell)
        {
            Doors doors = DoorSets.Rotate(shape.Doors, p.Rotation);
            foreach (Doors side in (Doors[])[Doors.North, Doors.East, Doors.South, Doors.West])
                if ((doors & side) != 0)
                    yield return (p.Row, p.Column, side);
            yield break;
        }

        for (int k = 0; k < shape.CellsX; k++)
        {
            if (shape.HasSocket(Doors.South, k)) yield return (p.Row, p.Column + k, Doors.South);
            if (shape.HasSocket(Doors.North, k)) yield return (p.Row + shape.CellsY - 1, p.Column + k, Doors.North);
        }

        for (int k = 0; k < shape.CellsY; k++)
        {
            if (shape.HasSocket(Doors.West, k)) yield return (p.Row + k, p.Column, Doors.West);
            if (shape.HasSocket(Doors.East, k)) yield return (p.Row + k, p.Column + shape.CellsX - 1, Doors.East);
        }
    }

    private static IEnumerable<Placement> Joined(LevelPlan plan, Placement at)
    {
        var mine = DoorEdges(at).Select(e => e.Side switch
        {
            Doors.North => (e.Row + 1, e.Column, Doors.South),
            Doors.South => (e.Row - 1, e.Column, Doors.North),
            Doors.East => (e.Row, e.Column + 1, Doors.West),
            _ => (e.Row, e.Column - 1, Doors.East),
        }).ToHashSet();
        foreach (Placement p in plan.Placements)
            if (p != at && DoorEdges(p).Any(mine.Contains))
                yield return p;
    }

    [Theory]
    [MemberData(nameof(AllDepths))]
    public void The_way_in_and_every_room_one_door_from_it_hold_no_pack(int depth)
    {
        var rooms = libs.Built(depth).ToDictionary(r => r.Name);
        var pool = libs.Pool(depth);
        var found = new List<string>();
        int neighbours = 0;
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            // Level 5's maze is laid by the map generator (brief p18), every other depth by the rules module.
            var (plan, _, error) = DescentLevel.Lay(depth, seed, pool, _ => 0);
            Assert.True(plan is not null, $"depth {depth} seed {seed}: {error}");
            Placement arrival = plan!.Placements.Single(p => IsWayIn(p.Room));
            if (Packs(rooms[arrival.Room]) > 0)
                found.Add($"seed {seed}: the way in {arrival.Room} holds {Packs(rooms[arrival.Room])} pack(s)");
            foreach (Placement next in Joined(plan, arrival))
            {
                neighbours++;
                if (Packs(rooms[next.Room]) > 0)
                    found.Add($"seed {seed}: {next.Room} beside the way in holds {Packs(rooms[next.Room])} pack(s)");
            }
        }

        Assert.True(neighbours >= Seeds, $"depth {depth}: only {neighbours} rooms beside the way in over {Seeds} seeds");
        Assert.True(found.Count == 0, $"depth {depth}: {string.Join(" | ", found.Take(4))}");
    }

    [Theory]
    [MemberData(nameof(NormalDepths))]
    public void A_quiet_room_stands_only_beside_the_way_in(int depth)
    {
        var pool = libs.Pool(depth);
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            LevelPlan plan = new LevelLayout().Generate(depth, seed, pool).Plan!;
            Placement arrival = plan.Placements.Single(p => IsWayIn(p.Room));
            var beside = Joined(plan, arrival).ToHashSet();
            Assert.All(plan.Placements.Where(p => RoomName.Parse(p.Room).Quiet), p => Assert.Contains(p, beside));
        }
    }

    [Fact]
    public void A_familys_preview_camera_looks_at_its_ordinary_room_before_its_quiet_twin()
    {
        var rooms = libs.Built(3);
        string manifest = LibraryManifestWriter.Write(DescentRooms.LibraryName(3), rooms);
        var pool = libs.Pool(3);
        int quiet = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            LevelPlan plan = new LevelLayout().Generate(3, seed, pool).Plan!;
            var shapes = plan.Placements.Select(p => RoomName.Parse(p.Room)).ToList();
            quiet += shapes.Count(s => s.Quiet);
            foreach (string camera in DescentLevel.Cameras(plan, manifest))
            {
                var shape = RoomName.Parse(camera.Split(' ')[^1]);
                if (shape.Quiet)
                    Assert.DoesNotContain(shapes, s => s.Family == shape.Family && !s.Quiet);
            }
        }

        Assert.True(quiet >= 10, $"only {quiet} quiet rooms placed over 10 levels");
    }

    [Fact]
    public void A_quiet_twin_is_its_room_without_the_packs()
    {
        var rooms = libs.Built(3);
        var twins = rooms.Where(r => r.Entry.Quiet).ToArray();

        Assert.Equal(RoomCatalogue.QuietFamilies.Count, twins.Length);
        foreach (Room twin in twins)
        {
            var shape = RoomName.Parse(twin.Name);
            Room first = rooms.First(r => !r.Entry.Quiet && RoomName.Parse(r.Name).Family == shape.Family);
            Assert.True(shape.Quiet);
            Assert.Equal(0, Packs(twin));
            Assert.True(Packs(first) > 0);
            Assert.Equal(first.Doors, twin.Doors);
            Assert.Equal(first.Markers.Count(m => m.Kind != MarkerKind.PackSpawn), twin.Markers.Count());
            Assert.Empty(RoomLint.Check(twin));
        }
    }

    [Fact]
    public void A_quiet_room_with_a_pack_is_refused_by_the_lint()
    {
        Room twin = libs.Built(3).First(r => r.Entry.Quiet);
        Room drift = libs.Built(3).First(r => !r.Entry.Quiet && RoomName.Parse(r.Name).Family == RoomName.Parse(twin.Name).Family);

        Assert.Contains(RoomLint.Check(drift with { Entry = drift.Entry with { Quiet = true } }), f => f.Rule == RoomLint.MarkerCount);
    }
}
