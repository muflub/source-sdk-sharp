using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Layout;
using Xunit;

namespace TF2.Descent.Tests.Layout;

/// <summary>
/// M21 (2026-11-10): the room draw adapts to what the level already holds. Each steering rule on
/// a small pool, over the same seeds, against depth 8's row with one rule changed.
/// </summary>
public sealed class CompositionTests
{
    private const int Seeds = 120;

    private static RoomInfo R(string name, params string[] tags) => new(name, tags, 1, 1, 15);

    private static List<RoomInfo> Pool() =>
    [
        R("arrival_cage-e-c", "start"),
        R("exit_cage-w-c", "stairs"),
        R("drift_a-ew-l", "corridor"),
        R("bend_a-ne-l", "corridor"),
        R("tee_a-esw-l", "junction"),
        R("cross_a-nesw-l", "junction"),
        R("stub_a-e-l", "shrine"),
        R("pillar_a-ew-c"),
        R("pillar_b-ne-c"),
        R("pillar_e-e-c"),
        R("split_a-ew-c-j.s2"),
        R("split_b-ne-c-j.s2"),
        R("split_e-e-c-j.s2"),
        R("gallery_a-ew-h-j.b1.b2"),
        R("gallery_b-ne-h-j.b1.b2"),
        R("gallery_e-e-h-j.b1.b2"),
        R("vault_a-e-c", "treasure"),
    ];

    private static List<LevelCheck> Levels(Func<DepthProfile, DepthProfile> change)
    {
        var profile = change(DepthTable.For(8) with { FamilyTargets = [], Kinds = [] });
        var pool = Pool();
        var checks = new List<LevelCheck>();
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var result = new LevelLayout().Generate(profile, seed, pool);
            Assert.True(result.Ok, result.Error);
            checks.Add(new LevelCheck(8, result.Plan!, result.Report!, pool));
        }
        return checks;
    }

    private static int Count(LevelCheck check, string family) => check.FamilyCounts.GetValueOrDefault(family);

    [Fact]
    public void A_family_under_its_share_is_drawn_more_than_one_over_it()
    {
        var pillars = Levels(p => p with { FamilyTargets = [new("pillar", 40), new("split", 5)] });
        var splits = Levels(p => p with { FamilyTargets = [new("pillar", 5), new("split", 40)] });

        Assert.True(pillars.Sum(c => Count(c, "pillar")) > 2 * pillars.Sum(c => Count(c, "split")),
            $"pillar {pillars.Sum(c => Count(c, "pillar"))}, split {pillars.Sum(c => Count(c, "split"))}");
        Assert.True(splits.Sum(c => Count(c, "split")) > 2 * splits.Sum(c => Count(c, "pillar")),
            $"split {splits.Sum(c => Count(c, "split"))}, pillar {splits.Sum(c => Count(c, "pillar"))}");
    }

    [Fact]
    public void A_kind_under_its_share_is_drawn_more()
    {
        static KindTarget[] Mix(int connector, int chamber) =>
            [new(RoomClass.Connector, connector), new(RoomClass.Chamber, chamber), new(RoomClass.Hall, 100 - connector - chamber), new(RoomClass.Shaft, 0)];
        var hallways = Levels(p => p with { Kinds = Mix(70, 15) }).Average(c => c.ClassCounts[0]);
        var rooms = Levels(p => p with { Kinds = Mix(20, 40) }).Average(c => c.ClassCounts[0]);

        Assert.True(hallways > rooms + 3, $"hallways {hallways:F1} asked 70 %, {rooms:F1} asked 20 %");
    }

    [Fact]
    public void A_family_never_passes_its_most()
    {
        var levels = Levels(p => p with { FamilyTargets = [new("pillar", 40, Max: 1)] });

        Assert.All(levels, c => Assert.InRange(Count(c, "pillar"), 0, 1));
        Assert.Contains(levels, c => Count(c, "pillar") == 1);
    }

    [Fact]
    public void A_family_with_a_fewest_appears_on_every_seed()
    {
        var levels = Levels(p => p with { FamilyTargets = [new("split", 0, Min: 3)], Heights = new(1, 1, 1, 0) });

        Assert.All(levels, c => Assert.True(Count(c, "split") >= 3, $"{Count(c, "split")} split rooms"));
    }

    [Fact]
    public void The_shrines_never_pass_the_levels_count()
    {
        var levels = Levels(p => p with { MaxShrines = 1, EndSharePercent = 30 });

        Assert.All(levels, c => Assert.InRange(c.FamilyCounts.GetValueOrDefault("stub"), 0, 1));
        Assert.Contains(levels, c => c.FamilyCounts.GetValueOrDefault("stub") == 1);
    }

    [Fact]
    public void Variety_draws_a_family_less_beside_itself()
    {
        var varied = Levels(p => p with { Variety = 1 }).Average(c => c.SameFamilyCombatPairs);
        var plain = Levels(p => p with { Variety = 0 }).Average(c => c.SameFamilyCombatPairs);

        Assert.True(varied < plain, $"same-family pairs {varied:F2} with variety, {plain:F2} without");
    }

    [Fact]
    public void Variety_repeats_a_variant_less()
    {
        var varied = Levels(p => p with { Variety = 1 }).Average(c => c.MostRepeatedCombat);
        var plain = Levels(p => p with { Variety = 0 }).Average(c => c.MostRepeatedCombat);

        Assert.True(varied < plain, $"most-repeated combat room {varied:F2} with variety, {plain:F2} without");
    }
}
