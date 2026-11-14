using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>What <c>MapGen descent-rooms</c> writes (task A13): one depth's library on its kit.</summary>
public sealed class DescentRoomsVerbTests
{
    public static TheoryData<int> AllDepths => [.. Enumerable.Range(1, 15)];

    private static string Dir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "descent-rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IReadOnlyList<Host.Contracts.RoomInfo> Pool(int depth) =>
        DescentLevel.ReadManifest(LibraryManifestWriter.Write(DescentRooms.LibraryName(depth), DescentRooms.Rooms(depth, 0))).Rooms;

    [Fact]
    public void It_writes_the_depths_library_and_its_manifest()
    {
        string dir = Dir();
        try
        {
            var files = DescentRooms.Write(dir, 3, 0).Select(Path.GetFileName);

            Assert.Equal(["descent_d03.vmf", "library.json"], files);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_depth_outside_the_shaft_is_refused()
    {
        string dir = Dir();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DescentRooms.Write(dir, 16, 0));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(5, 2048)]
    [InlineData(10, 4096)]
    [InlineData(15, 3072)]
    [InlineData(1, 1024)]
    [InlineData(14, 1024)]
    public void A_depth_builds_on_its_kits_cell(int depth, int cell)
    {
        Assert.Equal(cell, DescentRooms.KitFor(depth).Cell);
        Assert.All(DescentRooms.Rooms(depth, 0), r => Assert.Equal(cell, r.Kit.Cell));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void A_boss_library_holds_only_its_rows_rooms(int depth)
    {
        var families = DescentRooms.Rooms(depth, 0).Select(r => RoomName.Parse(r.Name).Family).Distinct().Order();

        Assert.Equal(["antechamber", "arena", "stairs", "start"], families);
    }

    [Fact]
    public void A_camp_stands_only_at_six_and_eleven()
    {
        var withCamp = Enumerable.Range(1, 15).Where(d => DescentRooms.Rooms(d, 0).Any(r => RoomName.Parse(r.Name).Family == "camp"));

        Assert.Equal([6, 11], withCamp);
    }

    [Fact]
    public void Every_room_of_the_written_library_has_a_manifest_entry()
    {
        var rooms = DescentRooms.Rooms(2, 0);
        string json = LibraryManifestWriter.Write(DescentRooms.LibraryName(2), rooms);

        Assert.All(rooms, r => Assert.Contains($"\"{r.Name}\": {{", json));
    }

    /// <summary>One depth of each library the catalogue makes: a normal depth, the two camp depths and the three boss kits.</summary>
    public static TheoryData<int> DistinctLibraries => [1, 5, 6, 10, 11, 15];

    [Theory]
    [MemberData(nameof(DistinctLibraries))]
    public void Every_depths_library_lints_clean(int depth)
    {
        var rooms = DescentRooms.Rooms(depth, 0);

        Assert.NotEmpty(rooms);
        Assert.Empty(rooms.SelectMany(RoomLint.Check));
    }

    [Theory]
    [MemberData(nameof(AllDepths))]
    public void The_layout_makes_every_depths_level_from_its_library(int depth)
    {
        var pool = Pool(depth);

        Assert.All(Enumerable.Range(1, 25), seed =>
        {
            LayoutResult result = new LevelLayout().Generate(depth, (ulong)seed, pool);
            Assert.True(result.Ok, $"depth {depth} seed {seed}: {result.Error}");
        });
    }

    [Theory]
    [MemberData(nameof(AllDepths))]
    public void A_levels_jump_minimums_are_met_from_its_library(int depth)
    {
        LayoutResult result = new LevelLayout().Generate(depth, 7, Pool(depth));

        Assert.True(result.Ok, result.Error);
        Assert.False(result.Report!.JumpFallback, result.Report.JumpFallbackReason);
    }
}
