using SourceSharp.MapGen;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>What <c>MapGen descent-rooms</c> writes (task A13): one depth's library on its kit.</summary>
public sealed class DescentRoomsVerbTests(DescentLibraries libs) : IClassFixture<DescentLibraries>
{
    public static TheoryData<int> AllDepths => [.. Enumerable.Range(1, 15)];

    private static string Dir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "descent-rooms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// The rooms of a depth's library as the host reads them, from its manifest.
    /// The layout reads names, tags, weights and depths only, which dressing does
    /// not change, so the shapes' own rooms stand in for the dressed ones.
    /// </summary>
    internal static IReadOnlyList<Host.Contracts.RoomInfo> Pool(int depth) =>
        DescentLevel.ReadManifest(LibraryManifestWriter.Write(DescentRooms.LibraryName(depth), BuiltRooms(depth))).Rooms;

    private static IReadOnlyList<Room> BuiltRooms(int depth) => DescentRooms.Built(depth, 0);

    /// <summary>The class's own shared copies (the fixture), so a depth is built once for all its facts.</summary>
    private IReadOnlyList<Room> Built(int depth) => libs.Built(depth);

    private IReadOnlyList<Host.Contracts.RoomInfo> PoolOf(int depth) => libs.Pool(depth);

    private static string FamilyOf(string name) => RoomName.Parse(name).Family;

    [Fact]
    public void It_writes_the_depths_library_and_its_manifest()
    {
        string dir = Dir();
        try
        {
            // A boss depth: its few grey rooms write in a second, where an Act I depth dresses for minutes.
            var files = DescentRooms.Write(dir, 5, 0).Select(Path.GetFileName);

            Assert.Equal(["descent_d05.vmf", "library.json"], files);
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
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(1)]
    [InlineData(14)]
    public void Every_depth_builds_on_the_normal_kits_cell_but_level_5_on_its_512(int depth)
    {
        // Level 5 is a maze of 512 cells (brief p18, M36).
        int cell = depth == MazeLevel.Depth ? 512 : 1024;
        Assert.Equal(cell, DescentRooms.KitFor(depth).Cell);
        Assert.All(Built(depth), r => Assert.Equal(cell, r.Kit.Cell));
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(10, 3)]
    [InlineData(15, 4)]
    public void A_boss_depths_arena_is_a_larger_room_of_its_size(int depth, int cells)
    {
        Room arena = Built(depth).Single(r => r.Entry.Tags.Contains("boss_arena"));

        Assert.Equal(cells, arena.Cells);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void A_boss_depths_arena_opens_only_where_its_row_joins_it(int depth)
    {
        Room arena = Built(depth).Single(r => r.Entry.Tags.Contains("boss_arena"));

        // Level 5's: the maze's two doors and the way down's (brief p18).
        Assert.Equal(depth switch { 15 => 1, 5 => 3, _ => 2 }, arena.Sockets.Count());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void A_boss_library_holds_only_its_rows_rooms(int depth)
    {
        // The row's rooms, and (brief p9) the quiet drift that stands between its way in and its antechamber.
        var families = Built(depth).Where(r => !r.Entry.Quiet).Select(r => RoomName.Parse(r.Name).Family).Distinct().Order();
        var quiet = Built(depth).Where(r => r.Entry.Quiet).Select(r => r.Name).ToArray();

        Assert.Equal(depth == 15 ? ["antechamber", "arena", "arrival"] : ["antechamber", "arena", "arrival", "exit"], families);
        Assert.Equal("drift", RoomName.Parse(Assert.Single(quiet)).Family);
    }

    [Fact]
    public void A_camp_stands_only_at_six_and_eleven()
    {
        // The library's rooms as the shapes built them: dressing changes no room's name or family.
        var withCamp = Enumerable.Range(1, 15).Where(d => Built(d).Any(r => RoomName.Parse(r.Name).Family == "camp"));

        Assert.Equal([6, 11], withCamp);
    }

    [Fact]
    public void Every_room_of_the_written_library_has_a_manifest_entry()
    {
        var rooms = Built(2);
        string json = LibraryManifestWriter.Write(DescentRooms.LibraryName(2), rooms);

        Assert.All(rooms, r => Assert.Contains($"\"{r.Name}\": {{", json));
    }

    /// <summary>One depth of each library the catalogue makes: a normal depth, the two camp depths and the three boss kits.</summary>
    public static TheoryData<int> DistinctLibraries => [1, 5, 6, 10, 11, 15];

    [Theory]
    [MemberData(nameof(DistinctLibraries))]
    public void Every_depths_library_lints_clean(int depth)
    {
        var rooms = libs.Rooms(depth, 0);

        Assert.NotEmpty(rooms);
        Assert.Empty(rooms.SelectMany(RoomLint.Check));
    }

    [Theory]
    [MemberData(nameof(RulesDepths))]
    public void The_layout_makes_every_depths_level_from_its_library(int depth)
    {
        var pool = PoolOf(depth);

        Assert.All(Enumerable.Range(1, 25), seed =>
        {
            LayoutResult result = new LevelLayout().Generate(depth, (ulong)seed, pool);
            Assert.True(result.Ok, $"depth {depth} seed {seed}: {result.Error}");
        });
    }

    [Theory]
    [MemberData(nameof(RulesDepths))]
    public void A_levels_jump_minimums_are_met_from_its_library(int depth)
    {
        LayoutResult result = new LevelLayout().Generate(depth, 7, PoolOf(depth));

        Assert.True(result.Ok, result.Error);
        Assert.False(result.Report!.JumpFallback, result.Report.JumpFallbackReason);
    }

    /// <summary>
    /// What a generated level of <paramref name="profile"/>'s depth falls short
    /// of, one line each: a jump minimum fallen back on, a dead end that is a
    /// hallway or a dead-end fallback, a capped or wasted door, a room count off
    /// the budget, and a family the depth's mix counts a fewest of missing.
    /// </summary>
    internal static IEnumerable<string> Shortfalls(DepthProfile profile, Host.Contracts.LevelPlan plan, LayoutReport report)
    {
        if (report.JumpFallback)
            yield return "jump fallback: " + report.JumpFallbackReason;
        if (report.DeadEndConnectors > 0)
            yield return $"{report.DeadEndConnectors} hallway dead ends";
        if (report.DeadEndFallbackReason is { } why)
            yield return "dead-end fallback: " + why;
        if (report.QuietFallbackReason is { } quiet)
            yield return "quiet fallback: " + quiet;
        if (report.CappedDoors > 0 || report.WastedDoors > 0)
            yield return $"{report.CappedDoors} capped doors, {report.WastedDoors} wasted";
        if (!profile.IsBoss && report.Rooms != profile.Rooms)
            yield return $"{report.Rooms} rooms for a budget of {profile.Rooms}";
        var families = plan.Placements.Select(p => FamilyOf(p.Room)).ToArray();
        foreach (FamilyTarget f in profile.FamilyTargets.Where(f => f.Min > 0))
            if (families.Count(x => x == f.Family) < f.Min)
                yield return $"{families.Count(x => x == f.Family)} {f.Family} rooms, fewer than {f.Min}";
    }

    /// <summary>The routine sweep's seeds; the long one (200 a depth) is <see cref="DescentRealCatalogueSweepTests"/>, in the slow tier.</summary>
    private const int RoutineSeeds = 20;

    [Theory]
    [MemberData(nameof(RulesDepths))]
    public void The_real_catalogue_lays_out_every_depth_at_20_seeds_without_a_shortfall(int depth)
    {
        var problems = DescentRealCatalogueSweepTests.Sweep(depth, PoolOf(depth), RoutineSeeds);

        Assert.True(problems.Count == 0, $"depth {depth}: {problems.Count} problems, first: {string.Join(" | ", problems.Take(3))}");
    }

    /// <summary>The depths the rules module's generator lays: all but level 5, whose maze the map generator lays (brief p18; <c>DescentMazeLevelTests</c>).</summary>
    public static TheoryData<int> RulesDepths => [.. Enumerable.Range(1, 15).Where(d => d != MazeLevel.Depth)];

    public static TheoryData<int> NormalDepths => [.. Enumerable.Range(1, 15).Where(d => !DepthTable.For(d).IsBoss)];

    [Theory]
    [MemberData(nameof(NormalDepths))]
    public void Every_family_a_depths_mix_names_is_in_its_library(int depth)
    {
        var held = PoolOf(depth).Select(r => FamilyOf(r.Name)).ToHashSet();
        var named = DepthTable.For(depth).FamilyTargets.Where(f => f.Max > 0).Select(f => f.Family);

        Assert.DoesNotContain(named, f => !held.Contains(f));
    }

    [Theory]
    [MemberData(nameof(DistinctLibraries))]
    public void Every_room_of_a_library_is_of_a_family_the_layout_knows_with_only_its_tags(int depth)
    {
        var wrong = PoolOf(depth).Where(r => !RoomFamilies.IsKnown(FamilyOf(r.Name)) || r.Tags.Any(t => !RoomFamilies.Tags[FamilyOf(r.Name)].Contains(t)));

        Assert.Empty(wrong.Select(r => $"{r.Name} [{string.Join(",", r.Tags)}]"));
    }

    [Fact]
    public void Every_family_the_layout_knows_has_a_generator()
    {
        var generated = RoomCatalogue.Mine.Concat(RoomCatalogue.Boss).Select(f => f.Family).ToHashSet();

        Assert.DoesNotContain(RoomFamilies.Tags.Keys, f => !generated.Contains(f));
    }

    [Theory]
    [InlineData("cavern")]
    [InlineData("pool")]
    [InlineData("crypt")]
    public void Act_one_holds_no_room_of_a_family_from_below_it(string family)
    {
        Assert.All(Enumerable.Range(1, 4), depth => Assert.DoesNotContain(PoolOf(depth), r => FamilyOf(r.Name) == family));
    }
}
