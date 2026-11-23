using SourceSharp.Host.Contracts;
using SourceSharp.MapGen.Descent;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The long sweep of the layout over the real catalogue (plan section 1.3a,
/// task C4): 200 seeds at every depth, no generator error, jump or
/// dead-end fallback, hallway dead end, capped or wasted door of a room of one
/// cell, missed budget or missing family. In the slow tier
/// (<c>tools/test-tiers.tsv</c>); the routine fact sweeps 20 seeds
/// (<see cref="DescentRoomsVerbTests"/>).
/// </summary>
public sealed class DescentRealCatalogueSweepTests
{
    private const int Seeds = 200;

    /// <summary>The depths the rules module lays: level 5's maze is the map generator's (brief p18).</summary>
    public static TheoryData<int> AllDepths => [.. Enumerable.Range(1, 15).Where(d => d != MazeLevel.Depth)];

    /// <summary>Every problem of the levels of seeds 1 to <paramref name="seeds"/> at a depth from a pool.</summary>
    internal static List<string> Sweep(int depth, IReadOnlyList<RoomInfo> pool, int seeds)
    {
        var profile = DepthTable.For(depth);
        var problems = new List<string>();
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            LayoutResult result = new LevelLayout().Generate(depth, seed, pool);
            if (!result.Ok)
                problems.Add($"seed {seed}: {result.Error}");
            else
                problems.AddRange(DescentRoomsVerbTests.Shortfalls(profile, result.Plan!, result.Report!).Select(p => $"seed {seed}: {p}"));
        }

        return problems;
    }

    [Theory]
    [MemberData(nameof(AllDepths))]
    public void The_real_catalogue_lays_out_every_depth_at_200_seeds_without_a_shortfall(int depth)
    {
        var problems = Sweep(depth, DescentRoomsVerbTests.Pool(depth), Seeds);

        Assert.True(problems.Count == 0, $"depth {depth}: {problems.Count} problems, first: {string.Join(" | ", problems.Take(3))}");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Act_ones_levels_below_the_first_mostly_hold_a_larger_room(int depth)
    {
        var pool = DescentRoomsVerbTests.Pool(depth);
        int with = Enumerable.Range(1, Seeds).Count(s => new LevelLayout().Generate(depth, (ulong)s, pool).Report!.MultiCellRooms == 1);

        Assert.True(with * 2 > Seeds, $"{with} of {Seeds} levels at depth {depth} hold a larger room");
    }
}
