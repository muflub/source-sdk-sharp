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
    private static IEnumerable<Placement> Joined(LevelPlan plan, Placement at)
    {
        Doors mine = DoorSets.Rotate(RoomName.Parse(at.Room).Doors, at.Rotation);
        foreach (Placement p in plan.Placements)
        {
            Doors side = (p.Row - at.Row, p.Column - at.Column) switch
            {
                (1, 0) => Doors.North,
                (-1, 0) => Doors.South,
                (0, 1) => Doors.East,
                (0, -1) => Doors.West,
                _ => Doors.None,
            };
            if (side != Doors.None && (mine & side) != 0 && (DoorSets.Rotate(RoomName.Parse(p.Room).Doors, p.Rotation) & DoorSets.Opposite(side)) != 0)
                yield return p;
        }
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
            LayoutResult result = new LevelLayout().Generate(depth, seed, pool);
            Assert.True(result.Ok, $"depth {depth} seed {seed}: {result.Error}");
            Placement arrival = result.Plan!.Placements.Single(p => IsWayIn(p.Room));
            if (Packs(rooms[arrival.Room]) > 0)
                found.Add($"seed {seed}: the way in {arrival.Room} holds {Packs(rooms[arrival.Room])} pack(s)");
            foreach (Placement next in Joined(result.Plan, arrival))
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
