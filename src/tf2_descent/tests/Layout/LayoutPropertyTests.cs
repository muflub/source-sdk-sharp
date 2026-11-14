using System.Collections.Concurrent;
using System.Diagnostics;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;
using Xunit.Abstractions;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// Every depth generated from 10,000 seeds against the synthetic catalogue, once,
/// each level checked by <see cref="LevelCheck"/> and generated a second time by
/// another generator to compare. The facts below read the tallies.
/// </summary>
public sealed class LayoutSweep
{
    public const int Seeds = 10_000;

    internal sealed class DepthTally
    {
        public int Levels;
        public int Errors;
        public string? FirstError;
        public int Mismatches;
        public int Fallbacks;
        public string? FirstFallback;
        public readonly Dictionary<string, int> Faults = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> FirstFault = new(StringComparer.Ordinal);
        public long Rooms;
        public long PathRooms;
        public int WithLoop;
        public int WithBranch;
        public int WithTurn;
        public long JumpRooms;
        public long CombatRooms;
        public long Capped;
        public long Wasted;
        public long LowRooms;
        public long LowConnectors;
        public int MaxRooms;
        public long DeadEnds;
        public int MaxDeadEnds;
        public int WithBranchEnd;
        public long Loops;
        public long OnEmptyCells;
        public int DeadEndFallbacks;
        public long BoxRows;
        public long BoxColumns;
        public int MaxSide;
        public double Fill;
        public double Neighbours;
        public long StraightRun;
        public double Clusters;
        public string? FirstDeadEndFallback;
        public readonly long[] Classes = new long[4];
        public readonly Dictionary<string, long> Families = new(StringComparer.Ordinal);
        public long FillRooms;
        public long DistinctFamilies;
        public int FewestFamilies = int.MaxValue;
        public long MostRepeated;
        public long MostRepeatedCombat;
        public long SamePairs;
        public long SameCombatPairs;
    }

    internal IReadOnlyDictionary<int, DepthTally> Tallies { get; }
    internal TimeSpan Elapsed { get; }

    public LayoutSweep()
    {
        var watch = Stopwatch.StartNew();
        var catalogue = LayoutCatalogue.Rooms();
        // Each depth in chunks of seeds, so the large grids spread over the cores.
        const int chunks = 10;
        var parts = new ConcurrentDictionary<(int Depth, int Chunk), DepthTally>();
        Parallel.For(0, DepthTable.Depths * chunks, job =>
        {
            var (depth, chunk) = (job / chunks + 1, job % chunks);
            parts[(depth, chunk)] = Sweep(depth, catalogue, chunk * Seeds / chunks, (chunk + 1) * Seeds / chunks);
        });
        Tallies = Enumerable.Range(1, DepthTable.Depths).ToDictionary(d => d, d => Merge(Enumerable.Range(0, chunks).Select(c => parts[(d, c)])));
        Elapsed = watch.Elapsed;
    }

    private static DepthTally Merge(IEnumerable<DepthTally> parts)
    {
        var all = new DepthTally();
        foreach (var t in parts)
        {
            all.Levels += t.Levels;
            all.Errors += t.Errors;
            all.FirstError ??= t.FirstError;
            all.Mismatches += t.Mismatches;
            all.Fallbacks += t.Fallbacks;
            all.FirstFallback ??= t.FirstFallback;
            foreach (var (property, n) in t.Faults) all.Faults[property] = all.Faults.GetValueOrDefault(property) + n;
            foreach (var (property, message) in t.FirstFault) all.FirstFault.TryAdd(property, message);
            all.Rooms += t.Rooms;
            all.PathRooms += t.PathRooms;
            all.WithLoop += t.WithLoop;
            all.WithBranch += t.WithBranch;
            all.WithTurn += t.WithTurn;
            all.JumpRooms += t.JumpRooms;
            all.CombatRooms += t.CombatRooms;
            all.Capped += t.Capped;
            all.Wasted += t.Wasted;
            all.LowRooms += t.LowRooms;
            all.LowConnectors += t.LowConnectors;
            all.MaxRooms = Math.Max(all.MaxRooms, t.MaxRooms);
            all.DeadEnds += t.DeadEnds;
            all.MaxDeadEnds = Math.Max(all.MaxDeadEnds, t.MaxDeadEnds);
            all.WithBranchEnd += t.WithBranchEnd;
            all.Loops += t.Loops;
            all.OnEmptyCells += t.OnEmptyCells;
            all.DeadEndFallbacks += t.DeadEndFallbacks;
            all.BoxRows += t.BoxRows;
            all.BoxColumns += t.BoxColumns;
            all.MaxSide = Math.Max(all.MaxSide, t.MaxSide);
            all.Fill += t.Fill;
            all.Neighbours += t.Neighbours;
            all.StraightRun += t.StraightRun;
            all.Clusters += t.Clusters;
            all.FirstDeadEndFallback ??= t.FirstDeadEndFallback;
            for (var k = 0; k < 4; k++) all.Classes[k] += t.Classes[k];
            foreach (var family in t.Families.Keys.Order(StringComparer.Ordinal)) all.Families[family] = all.Families.GetValueOrDefault(family) + t.Families[family];
            all.FillRooms += t.FillRooms;
            all.DistinctFamilies += t.DistinctFamilies;
            all.FewestFamilies = Math.Min(all.FewestFamilies, t.FewestFamilies);
            all.MostRepeated += t.MostRepeated;
            all.MostRepeatedCombat += t.MostRepeatedCombat;
            all.SamePairs += t.SamePairs;
            all.SameCombatPairs += t.SameCombatPairs;
        }
        return all;
    }

    private static DepthTally Sweep(int depth, IReadOnlyList<RoomInfo> catalogue, int from, int to)
    {
        var tally = new DepthTally();
        var first = new LevelLayout();
        var second = new LevelLayout();
        var shuffled = catalogue.ToList();
        for (var i = from; i < to; i++)
        {
            var seed = (ulong)i * 0x9E3779B97F4A7C15UL + (ulong)depth;
            var result = first.Generate(depth, seed, catalogue);
            tally.Levels++;
            if (!result.Ok)
            {
                tally.Errors++;
                tally.FirstError ??= $"seed {seed}: {result.Error}";
                continue;
            }
            var plan = result.Plan!;
            var report = result.Report!;
            // The same depth and seed from another generator over the pool in another order: the
            // same plan (M21: no weight may hang on list order, hashing or identity).
            new LayoutRandom(seed).Shuffle(shuffled);
            if (!Same(plan, second.Generate(depth, seed, shuffled).Plan)) tally.Mismatches++;
            if (report.JumpFallback)
            {
                tally.Fallbacks++;
                tally.FirstFallback ??= $"seed {seed}: {report.JumpFallbackReason}";
            }

            var check = new LevelCheck(depth, plan, report, catalogue);
            foreach (var (property, message) in check.Faults)
            {
                tally.Faults[property] = tally.Faults.GetValueOrDefault(property) + 1;
                tally.FirstFault.TryAdd(property, $"seed {seed}: {message}");
            }
            tally.Rooms += check.Rooms;
            tally.PathRooms += report.CriticalPath.Count;
            if (check.Joints - check.Rooms + 1 > 0) tally.WithLoop++;
            if (check.Rooms > report.CriticalPath.Count) tally.WithBranch++;
            if (check.HasRotation) tally.WithTurn++;
            tally.JumpRooms += check.JumpRooms;
            tally.CombatRooms += check.CombatRooms;
            tally.Capped += report.CappedDoors;
            tally.Wasted += report.WastedDoors;
            tally.LowRooms += check.LowRooms;
            tally.LowConnectors += check.LowConnectors;
            tally.MaxRooms = Math.Max(tally.MaxRooms, check.Rooms);
            tally.DeadEnds += check.DeadEnds;
            tally.MaxDeadEnds = Math.Max(tally.MaxDeadEnds, check.DeadEnds);
            for (var k = 0; k < 4; k++) tally.Classes[k] += check.ClassCounts[k];
            foreach (var (family, n) in check.FamilyCounts) tally.Families[family] = tally.Families.GetValueOrDefault(family) + n;
            tally.FillRooms += check.FillRooms;
            tally.DistinctFamilies += check.FamilyCounts.Count;
            if (!DepthTable.For(depth).IsBoss) tally.FewestFamilies = Math.Min(tally.FewestFamilies, check.FamilyCounts.Count);
            tally.MostRepeated += check.MostRepeated;
            tally.MostRepeatedCombat += check.MostRepeatedCombat;
            tally.SamePairs += check.SameFamilyPairs;
            tally.SameCombatPairs += check.SameFamilyCombatPairs;
            if (check.BranchEnds > 0) tally.WithBranchEnd++;
            tally.Loops += check.Joints - check.Rooms + 1;
            tally.OnEmptyCells += check.DoorsOnEmptyCells;
            tally.BoxRows += plan.Rows;
            tally.BoxColumns += plan.Columns;
            tally.MaxSide = Math.Max(tally.MaxSide, Math.Max(plan.Rows, plan.Columns));
            tally.Fill += check.FillRatio;
            tally.Neighbours += check.MeanNeighbours;
            tally.StraightRun += check.LongestStraightRun;
            tally.Clusters += check.TallClusterMean;
            if (report.DeadEndFallbackReason is { } why)
            {
                tally.DeadEndFallbacks++;
                tally.FirstDeadEndFallback ??= $"seed {seed}: {why}";
            }
        }
        return tally;
    }

    private static bool Same(LevelPlan a, LevelPlan? b) =>
        b is not null && a.Rows == b.Rows && a.Columns == b.Columns && a.UpRoom == b.UpRoom && a.DownRoom == b.DownRoom
        && a.Placements.SequenceEqual(b.Placements);
}

/// <summary>Plan section 10's Phase 2 exit: facts over 10,000 seeds per depth (C4).</summary>
public sealed class LayoutPropertyTests(LayoutSweep sweep, ITestOutputHelper output) : IClassFixture<LayoutSweep>
{
    private int Faults(string property) => sweep.Tallies.Values.Sum(t => t.Faults.GetValueOrDefault(property));

    private string First(string property) =>
        string.Join("; ", sweep.Tallies.OrderBy(t => t.Key).Where(t => t.Value.FirstFault.ContainsKey(property))
            .Select(t => $"depth {t.Key} {t.Value.FirstFault[property]}").Take(3));

    private void Holds(string property) => Assert.True(Faults(property) == 0, $"{Faults(property)} level(s) break '{property}': {First(property)}");

    [Fact]
    public void Every_depth_generates_a_level_for_every_seed()
    {
        var errors = sweep.Tallies.OrderBy(t => t.Key).Where(t => t.Value.Errors > 0).Select(t => $"depth {t.Key}: {t.Value.Errors}, {t.Value.FirstError}");

        Assert.Empty(errors);
    }

    [Fact]
    public void Every_room_stands_alone_in_a_cell_of_the_depths_grid() => Holds(LevelCheck.Grid);

    [Fact]
    public void Every_level_has_one_way_up_and_one_way_down_and_the_bottom_none() => Holds(LevelCheck.Roles);

    [Fact]
    public void Every_door_joins_a_door_or_faces_the_edge_or_an_empty_cell() => Holds(LevelCheck.DoorRule);

    [Fact]
    public void Every_room_can_be_walked_to_from_the_arrival() => Holds(LevelCheck.Connected);

    [Fact]
    public void The_critical_path_is_a_shortest_walk_from_the_arrival_to_the_exit_whose_ends_stand_apart() => Holds(LevelCheck.Path);

    [Fact]
    public void No_third_low_combat_room_follows_two_on_the_critical_path() => Holds(LevelCheck.LowRule);

    [Fact]
    public void Every_normal_level_places_its_jump_minimums() => Holds(LevelCheck.Jumps);

    [Fact]
    public void No_level_falls_back_on_its_jump_minimums_when_the_catalogue_offers_every_tier()
    {
        var fallbacks = sweep.Tallies.OrderBy(t => t.Key).Where(t => t.Value.Fallbacks > 0).Select(t => $"depth {t.Key}: {t.Value.Fallbacks}, {t.Value.FirstFallback}");

        Assert.Empty(fallbacks);
    }

    [Fact]
    public void The_treasure_room_stands_off_the_critical_path() => Holds(LevelCheck.Treasure);

    [Fact]
    public void A_boss_level_is_its_fixed_row() => Holds(LevelCheck.Boss);

    [Fact]
    public void No_dead_end_is_a_hallway() => Holds(LevelCheck.DeadEndRule);

    [Fact]
    public void Every_normal_level_places_exactly_its_room_budget() => Holds(LevelCheck.Budget);

    [Fact]
    public void Every_normal_level_is_a_sparse_shape_not_a_filled_rectangle()
    {
        // M19: the old grids were 70 to 80 % full; a level of tunnels fills well under half its bounding box.
        foreach (var (depth, t) in sweep.Tallies.Where(t => !DepthTable.For(t.Key).IsBoss))
            Assert.True(t.Fill / t.Levels < 0.5, $"depth {depth} fills {t.Fill / t.Levels:P0} of its bounding box");
    }

    [Fact]
    public void No_bounding_box_passes_its_depths_span()
    {
        Assert.All(sweep.Tallies, t => Assert.InRange(t.Value.MaxSide, 1, DepthTable.For(t.Key).MaxSpan));
    }

    [Fact]
    public void No_level_passes_a_most_or_misses_a_fewest_of_its_mix() => Holds(LevelCheck.Mix);

    [Fact]
    public void No_level_falls_back_on_its_dead_ends_when_the_catalogue_offers_destinations()
    {
        var fallbacks = sweep.Tallies.OrderBy(t => t.Key).Where(t => t.Value.DeadEndFallbacks > 0)
            .Select(t => $"depth {t.Key}: {t.Value.DeadEndFallbacks}, {t.Value.FirstDeadEndFallback}");

        Assert.Empty(fallbacks);
    }

    [Fact]
    public void Branches_still_end_in_destinations_on_every_normal_depth()
    {
        // The cap must not have pruned all side content: levels still hold a branch ending in a
        // combat room or a shrine, besides the treasure.
        foreach (var (depth, t) in sweep.Tallies.Where(t => !DepthTable.For(t.Key).IsBoss))
            Assert.True(t.WithBranchEnd * 10 > t.Levels, $"depth {depth}: {t.WithBranchEnd} of {t.Levels} levels end a branch in a destination");
    }

    [Fact]
    public void Level_1_is_mostly_low_connector_rooms()
    {
        // Section 7 and M2: level 1 is mostly hallways, counted over every room the sweep placed.
        var t = sweep.Tallies[1];

        Assert.True(t.Rooms > 0);
        Assert.True(2 * t.LowConnectors > t.Rooms, $"{t.LowConnectors} of {t.Rooms} rooms at level 1 are low connectors");
    }

    [Fact]
    public void The_same_seed_gives_the_same_level_from_the_pool_in_any_order()
    {
        Assert.Equal(0, sweep.Tallies.Values.Sum(t => t.Mismatches));
    }

    [Fact]
    public void The_sweep_reached_loops_branches_and_turns_on_every_normal_depth()
    {
        output.WriteLine($"sweep: {DepthTable.Depths} depths x {LayoutSweep.Seeds} seeds in {sweep.Elapsed.TotalSeconds:F1} s");
        output.WriteLine("depth  rooms  box(mean rows x cols, max side)  fill  neighbours  straight  loops  ends(mean max share/target)  cluster  jump%");
        foreach (var (depth, t) in sweep.Tallies.OrderBy(t => t.Key))
            output.WriteLine($"{depth,5}  {(double)t.Rooms / t.Levels,5:F1}  {(double)t.BoxRows / t.Levels,5:F1} x {(double)t.BoxColumns / t.Levels,5:F1}, {t.MaxSide,2}  {t.Fill / t.Levels,4:F2}  {t.Neighbours / t.Levels,10:F2}  {(double)t.StraightRun / t.Levels,8:F2}  {(double)t.Loops / t.Levels,5:F2}  {(double)t.DeadEnds / t.Levels,5:F2} {t.MaxDeadEnds,3} {100.0 * t.DeadEnds / Math.Max(1, t.Rooms),5:F1}%/{DepthTable.For(depth).EndSharePercent}%  {t.Clusters / t.Levels,7:F2}  {(t.CombatRooms == 0 ? 0 : 100.0 * t.JumpRooms / t.CombatRooms),5:F1}");
        output.WriteLine("depth  deadends(mean max cap)  branchend%  loops  capped  onempty  dead-end fallbacks");
        foreach (var (depth, t) in sweep.Tallies.OrderBy(t => t.Key))
            output.WriteLine($"{depth,5}  {(double)t.DeadEnds / t.Levels,5:F2} {t.MaxDeadEnds,3}  {100.0 * t.WithBranchEnd / t.Levels,10:F1}  {(double)t.Loops / t.Levels,5:F2}  {(double)t.Capped / t.Levels,6:F3}  {t.OnEmptyCells,7}  {t.DeadEndFallbacks,8}");
        output.WriteLine("depth  rooms  max  path  loop%  branch%  turn%  combat  jump%  low%  lowconn%  capped  wasted  fallback  errors");
        foreach (var (depth, t) in sweep.Tallies.OrderBy(t => t.Key))
            output.WriteLine($"{depth,5}  {(double)t.Rooms / t.Levels,5:F1}  {t.MaxRooms,3}  {(double)t.PathRooms / t.Levels,4:F1}  {100.0 * t.WithLoop / t.Levels,5:F1}  {100.0 * t.WithBranch / t.Levels,7:F1}  {100.0 * t.WithTurn / t.Levels,5:F1}  {(double)t.CombatRooms / t.Levels,6:F1}  {(t.CombatRooms == 0 ? 0 : 100.0 * t.JumpRooms / t.CombatRooms),5:F1}  {100.0 * t.LowRooms / Math.Max(1, t.Rooms),4:F1}  {100.0 * t.LowConnectors / Math.Max(1, t.Rooms),8:F1}  {t.Capped,6}  {t.Wasted,6}  {t.Fallbacks,8}  {t.Errors,6}");

        output.WriteLine("depth  kinds realised (target) conn/chamber/hall/shaft  families realised (target)  distinct(mean min)  most-repeated(all combat)  same-family pairs(all combat)");
        foreach (var (depth, t) in sweep.Tallies.OrderBy(t => t.Key).Where(t => !DepthTable.For(t.Key).IsBoss))
        {
            var row = DepthTable.For(depth);
            var fill = Math.Max(1, t.FillRooms);
            var kinds = string.Join("/", Enumerable.Range(0, 4).Select(k => $"{100.0 * t.Classes[k] / fill:F0}({row.KindTargetOf((RoomClass)k)?.SharePercent})"));
            var families = string.Join(" ", row.FamilyTargets.Select(f => $"{f.Family} {100.0 * t.Families.GetValueOrDefault(f.Family) / fill:F0}({f.SharePercent})"));
            output.WriteLine($"{depth,5}  {kinds}  {families}  {(double)t.DistinctFamilies / t.Levels:F1} {t.FewestFamilies}  {(double)t.MostRepeated / t.Levels:F1} {(double)t.MostRepeatedCombat / t.Levels:F1}  {(double)t.SamePairs / t.Levels:F1} {(double)t.SameCombatPairs / t.Levels:F2}");
        }

        Assert.All(sweep.Tallies, t => Assert.Equal(LayoutSweep.Seeds, t.Value.Levels));
        foreach (var (depth, t) in sweep.Tallies.Where(t => !DepthTable.For(t.Key).IsBoss))
        {
            Assert.True(t.WithLoop > 0, $"depth {depth}: no level of {t.Levels} closed a loop");
            Assert.True(t.WithBranch > 0, $"depth {depth}: no level of {t.Levels} grew a branch");
            Assert.True(t.WithTurn > 0, $"depth {depth}: no level of {t.Levels} turned a room");
        }
    }
}
