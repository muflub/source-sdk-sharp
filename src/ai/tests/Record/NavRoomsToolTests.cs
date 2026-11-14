using SourceSharp.Ai.Replay;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Record;

/// <summary>
/// <c>ai-replay navrooms</c> (<see cref="NavRoomsTool"/>): a map's regions of free space, what the
/// map puts in each, and what one region costs to derive alone. The navigation is
/// <see cref="NavHullRegionsTests.Sealed"/>: two boxes of three columns by eight, x 0 to 48 and 80
/// to 128, floors at z 16, solid between. The map has a spawn in the west box, a teleport there
/// whose destination is in the east box, and a destination in the solid between.
/// </summary>
public sealed class NavRoomsToolTests
{
    private const string Entities =
        "{\n\"classname\" \"worldspawn\"\n}\n"
        + "{\n\"classname\" \"info_player_teamspawn\"\n\"origin\" \"24 24 17\"\n}\n"
        + "{\n\"classname\" \"trigger_teleport\"\n\"target\" \"east_start\"\n\"origin\" \"24 64 17\"\n}\n"
        + "{\n\"classname\" \"info_teleport_destination\"\n\"targetname\" \"east_start\"\n\"origin\" \"104 24 17\"\n}\n"
        + "{\n\"classname\" \"info_teleport_destination\"\n\"targetname\" \"in_the_wall\"\n\"origin\" \"64 24 17\"\n}\n";

    private static readonly byte[] Bsp = TinyBsp.Make(Entities);
    private static readonly byte[] Hull = NavHullRegionsTests.Sealed().Write();

    private static RoomsSurvey Survey()
    {
        NavHullReader reader = NavHullReader.Open(Hull);
        return NavRoomsTool.Survey(reader, NavHullRegions.Of(reader), NavRoomsTool.ReadEntities(Bsp));
    }

    /// <summary>A clock that moves 250 µs each time it is read, so a step read twice costs 0.25 ms.</summary>
    private static Func<double> Ticking()
    {
        double now = 0;
        return () => now += 250;
    }

    private static NavSizeReport Measure(int region, string stop = "cache")
        => NavRoomsTool.Measure(CostTool.Hl2mp, "dm_test", Hull, Bsp, region, stop, Ticking());

    [Fact]
    public void TheEntitiesAreReadWithTheirNamesAndTargets()
    {
        IReadOnlyList<RoomEntity> all = NavRoomsTool.ReadEntities(Bsp);
        Assert.Equal(5, all.Count);
        Assert.Equal("east_start", all.Single(e => e.ClassName == "trigger_teleport").Target);
    }

    [Fact]
    public void TheSurveyHasARowPerRegionWithItsLeaves()
        => Assert.Equal([24, 24], Survey().Rows.Select(r => r.Leaves));

    [Fact]
    public void AnEntityIsInTheRegionWhoseBoxHoldsIt()
    {
        RoomsSurvey s = Survey();
        Assert.Equal(1, s.Rows[0].Entities["info_player_teamspawn"]);
        Assert.Equal(1, s.Rows[1].Entities["info_teleport_destination"]);
        Assert.Equal(["east_start"], s.Rows[1].Destinations);
    }

    [Fact]
    public void AnEntityInNoBoxIsCountedApart()
        => Assert.Equal(1, Survey().Unplaced["info_teleport_destination"]);

    [Fact]
    public void ATeleportToAnotherRegionIsListedFromItsTriggersRegion()
    {
        RoomsSurvey s = Survey();
        Assert.Equal([(0, 1)], s.Teleports);
        Assert.Equal([1], s.Rows[0].TeleportsTo);
        Assert.Empty(s.Rows[1].TeleportsTo);
    }

    [Fact]
    public void TheDescriptionSaysHowManyRegionsAndTeleports()
    {
        string[] lines = [.. NavRoomsTool.Describe(Survey(), 1)];
        Assert.StartsWith("  2 regions of free space, 48 leaves; the largest 24", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("  1 teleports: 0 land in their own region, 1 in another", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void AStepsCpuTimeIsTheClocksAdvanceAcrossIt()
        => Assert.All(Measure(1).Steps, s => Assert.Equal(0.25, s.CpuMs));

    [Fact]
    public void TheStepsRunInOrderUpToTheStop()
        => Assert.Equal(["open", "regions", "region", "derive"], Measure(1, "derive").Steps.Select(s => s.Name));

    [Fact]
    public void ADerivedRegionHasTheCellsOfItsOwnBoxAndNoOther()
    {
        DerivedNav d = Measure(1, "derive").Nav!;
        Assert.Equal(24, d.Graph.CellCount);
        Assert.All(Enumerable.Range(0, d.Graph.CellCount), c => Assert.True(d.Graph.Cell(c).MinX >= 80f));
    }

    [Fact]
    public void ARegionTheFileDoesNotHaveIsRefusedNamingHowManyItHas()
        => Assert.Contains("the file has 2", Assert.Throws<ArgumentException>(() => Measure(2)).Message, StringComparison.Ordinal);

    [Fact]
    public void TheCachesSizeIsTheEncodedRegionGraph()
    {
        NavSizeReport r = Measure(0);
        float jump = NavHullReader.Open(Hull).JumpHeight;
        string key = $"{NavLoader.HullKey(NavGraphCache.NumbersKey(NavNumbers.Stock(jump), jump))}-r0";
        byte[] encoded = new NavGraphCache(Path.GetTempPath(), NavEdgeCosts.Default)
            .Encode("dm_test", BuiltNavStore.Sha256(Bsp), BuiltNavStore.Sha256(Hull), r.Nav!, key);
        Assert.True(r.CacheBytes > 0);
        Assert.Equal(encoded.Length, r.CacheBytes);
    }
}

/// <summary>
/// The entities of a shipped map, whose lumps are LZMA-compressed, read through map-tools' bsp
/// reader: jump_academy2_rc9a's teleports as plan_bots_teams_tf2_part_g G.7 counts them. Skips
/// when this tree's TF2 content has not the map (a download).
/// </summary>
public sealed class NavRoomsToolJumpMapTests
{
    private static string BspPath => SourceSharp.Tests.RepoLayout.Repo("bin", PerTreeNav.Config, "content", "tf", "download", "maps", "jump_academy2_rc9a.bsp");

    public sealed class JumpMapFactAttribute : FactAttribute
    {
        public JumpMapFactAttribute()
        {
            if (!File.Exists(BspPath))
                Skip = $"this tree has no {BspPath}";
        }
    }

    [JumpMapFact]
    public void AShippedMapsCompressedEntitiesAreRead()
    {
        IReadOnlyList<RoomEntity> all = NavRoomsTool.ReadEntities(File.ReadAllBytes(BspPath));
        Assert.Equal((264, 200), (all.Count(e => e.ClassName == "trigger_teleport"), all.Count(e => e.ClassName == "info_teleport_destination")));
        Assert.All(all.Where(e => e.ClassName == "trigger_teleport"), e => Assert.NotNull(e.Mins));
    }
}
