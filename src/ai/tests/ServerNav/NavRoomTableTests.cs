using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// <see cref="NavRoomTable"/> and <see cref="NavRoomTeleports"/>: the table a map in rooms is
/// built with, and which teleports back are fail floors (R.5's checkpoint risk). The course: a
/// floor at z 0 (columns 0..13 by 0..3) where the room is come into at (1, 1), its far end
/// (12..13) under a teleport walked into; a pit at z -128 beside it (0..9 by 4..5) under a
/// teleport, fallen into; and a ledge at z 160 past a gap (16..19 by 0..3), which no move but a
/// blast reaches, a teleport on its far end (18..19).
/// </summary>
public sealed class NavRoomTableTests
{
    private static HurtVolume Box(int x0, int x1, int y0, int y1, float z0, float z1)
        => new(new AiVector(x0 * 16f, y0 * 16f, z0), new AiVector(x1 * 16f + 16f, y1 * 16f + 16f, z1));

    private static (NavGraph Graph, List<IReadOnlyList<HurtVolume>> Teleports) Course()
    {
        var src = new FakeNavSource();
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                bool floor = x <= 13 && y <= 3, pit = x <= 9 && y >= 4, ledge = x >= 16 && y <= 3;
                if (floor || pit || ledge)
                    src.Add(x, y, pit ? -128f : ledge ? 160f : 0f, (pit ? -128f : ledge ? 160f : 0f) + 300f);
            }
        }

        DerivedNav d = NavGraphFromNav3d.Derive(src, TFNavProfile.ModNumbers, NavEdgeCosts.Default, []);
        List<IReadOnlyList<HurtVolume>> teleports =
        [
            [Box(0, 9, 4, 5, -140f, -100f)],
            [Box(12, 13, 0, 3, -10f, 40f)],
            [Box(18, 19, 0, 3, 150f, 200f)],
        ];
        return (d.Graph, teleports);
    }

    [Fact]
    public void ATeleportFallenIntoIsAFailFloorAndOneWalkedIntoIsKept()
    {
        (NavGraph g, List<IReadOnlyList<HurtVolume>> teleports) = Course();
        bool[] fail = NavRoomTeleports.Classify(g, [new AiVector(24f, 24f, 0f)], teleports);
        Assert.True(fail[0], "the pit is fallen into");
        Assert.False(fail[1], "the walkway's teleport is walked into");
    }

    [Fact]
    public void ATeleportOnALedgeNoMoveButABlastReachesIsAFailFloor()
    {
        // The rule's limit, named: a teleport stepped into only from a place a blast reaches is not seen walked into.
        (NavGraph g, List<IReadOnlyList<HurtVolume>> teleports) = Course();
        Assert.True(NavRoomTeleports.Classify(g, [new AiVector(24f, 24f, 0f)], teleports)[2]);
    }

    [Fact]
    public void WithNoWayInEveryTeleportBackIsAFailFloor()
    {
        (NavGraph g, List<IReadOnlyList<HurtVolume>> teleports) = Course();
        Assert.All(NavRoomTeleports.Classify(g, [], teleports), Assert.True);
    }

    [Fact]
    public void AWayInOnTheLedgeKeepsItsTeleport()
    {
        (NavGraph g, List<IReadOnlyList<HurtVolume>> teleports) = Course();
        bool[] fail = NavRoomTeleports.Classify(g, [new AiVector(24f, 24f, 0f), new AiVector(16 * 16f + 8f, 24f, 160f)], teleports);
        Assert.False(fail[2]);
        Assert.True(fail[0]);
    }

    private static NavRoomTable Table()
    {
        var t = new NavRoomTeleport("t1", "dest", 7, new AiVector(1f, 2f, 3f), [new HurtVolume(new AiVector(0f, 0f, 0f), new AiVector(16f, 32f, 48f))]);
        var exit = new NavRoomTeleport(null, "away", -1, null, []);
        var room = new NavRoom(7, "m.r7.navhull", 1234, [new AiVector(5f, 6f, 7f)], [t], [t with { Name = "kept" }], [exit],
                               [new HurtVolume(new AiVector(-1f, -2f, -3f), new AiVector(1f, 2f, 3f))])
        {
            Spawns = [new AiVector(5f, 6f, 7f)],
        };
        var other = new NavRoom(3, "m.r3.navhull", 10, [new AiVector(1f, 1f, 1f)], [], [], [], [])
        {
            Spawns = [new AiVector(1f, 1f, 1f), new AiVector(2f, 2f, 2f)],
        };
        return new NavRoomTable("m", NavRoomTable.RulesRevision, 12_345_678L, [room, other]);
    }

    [Fact]
    public void ATableReadsBackAsItWasWritten()
    {
        NavRoomTable table = Table();
        NavRoomTable back = NavRoomTable.FromJson(table.ToJson());
        Assert.Equal(table.Map, back.Map);
        Assert.Equal(table.Revision, back.Revision);
        Assert.Equal(table.WholeLeaves, back.WholeLeaves);
        NavRoom a = table.Rooms[0], b = back.Rooms[0];
        Assert.Equal((a.Region, a.File, a.Leaves), (b.Region, b.File, b.Leaves));
        Assert.Equal(a.WaysIn, b.WaysIn);
        Assert.Equal(a.Spawns, b.Spawns);
        Assert.Equal(a.Captures, b.Captures);
        Assert.Equal(a.FailVolumes, b.FailVolumes);
        Assert.Equal(a.Fails[0] with { Brushes = [] }, b.Fails[0] with { Brushes = [] });
        Assert.Equal("kept", b.Kept[0].Name);
        Assert.Equal(a.Exits[0] with { Brushes = [] }, b.Exits[0] with { Brushes = [] });
        Assert.Same(back.Rooms[0], back.Room(7));
        Assert.Null(back.Room(8));
    }

    [Fact]
    public void ALevelStartsInTheRoomWithTheMostSpawns()
    {
        Assert.Equal(3, NavRoomTable.FromJson(Table().ToJson()).StartRoom?.Region);
        Assert.Null(new NavRoomTable("m", 1, 0, []).StartRoom);
    }

    [Fact]
    public void TextThatIsNoTableIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => NavRoomTable.FromJson("{\"map\": \"m\"}"));
        Assert.Throws<InvalidDataException>(() => NavRoomTable.FromJson("not json"));
    }
}
