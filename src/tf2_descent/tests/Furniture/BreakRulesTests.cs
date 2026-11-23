using System.Text;
using System.Text.Json.Nodes;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Loot;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// Crates and barrels a hit breaks (the user, 2026-11-15): the roll's table and seeding, each
/// player's share, the ambush's robots, the bad outcomes' cap, and where the furnishing puts them.
/// Distribution facts state their roll counts.
/// </summary>
public sealed class BreakRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static BreakRules Breaks => Rules.Furniture.Breaks;
    private static BreakableSettings Table => Rules.Content.Furniture.Breakables;

    [Fact]
    public void A_break_rolls_the_same_outcome_from_the_same_level_seed_and_marker()
    {
        var outcomes = Enumerable.Range(0, 200).Select(i => Breaks.Roll(7, "m" + i).Outcome).ToList();

        Assert.Equal(outcomes, Enumerable.Range(0, 200).Select(i => Breaks.Roll(7, "m" + i).Outcome));
        Assert.True(outcomes.Distinct().Count() >= 4, "two hundred markers roll more than one outcome");
    }

    [Fact]
    public void Another_level_seed_rolls_the_same_marker_differently()
    {
        var seen = Enumerable.Range(0, 300).Select(s => Breaks.Roll((ulong)s, "crate_1").Outcome).ToHashSet();

        Assert.True(seen.Count >= 5, $"only {seen.Count} outcomes over 300 level seeds");
    }

    [Fact]
    public void Each_outcome_comes_at_its_tables_share_over_100k_rolls()
    {
        const int n = 100_000;
        var counts = Enum.GetValues<BreakOutcome>().ToDictionary(o => o, _ => 0);
        for (int i = 0; i < n; i++)
            counts[Breaks.Roll(1, "m" + i).Outcome]++;

        double total = Table.Outcomes.Values.Sum();
        foreach (var (outcome, count) in counts)
            Assert.InRange(100.0 * count / n, 100 * Table.Weight(outcome) / total - 0.5, 100 * Table.Weight(outcome) / total + 0.5);
        Assert.True(counts[BreakOutcome.Nothing] == counts.Values.Max(), "nothing is the most common outcome");
    }

    [Fact]
    public void The_bad_outcomes_stay_under_their_cap_over_100k_rolls()
    {
        const int n = 100_000;
        int bad = 0;
        for (int i = 0; i < n; i++)
            if (BreakableSettings.Bad(Breaks.Roll(2, "m" + i).Outcome))
                bad++;

        Assert.True(bad > 0);   // the bad outcomes happen: the cap is a bound, not a ban
        Assert.True(100.0 * bad / n <= Table.BadCapPct, $"{bad} bad of {n}");
    }

    [Fact]
    public void Bad_weight_over_the_cap_is_a_load_error()
    {
        var errors = Errors(n => n["breakables"]!["outcomes"]!["ambush"] = 50);

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("bad outcomes", e.Message);
    }

    [Fact]
    public void The_shipped_breakables_load_with_no_error()
    {
        Assert.Empty(Errors(_ => { }));
    }

    [Fact]
    public void A_cloud_of_an_unknown_condition_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["breakables"]!["cloud"]!["condition"] = "itchy"));

        Assert.Equal((ContentTables.Furniture, ContentRule.DanglingReference), (e.Table, e.Rule));
    }

    [Fact]
    public void A_blast_naming_no_explosive_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["breakables"]!["blast"] = "keg"));

        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Contains("keg", e.Message);
    }

    [Fact]
    public void A_markers_otherwise_naming_no_breakable_is_a_load_error()
    {
        var e = Assert.Single(Errors(n => n["markers"]![1]!["otherwise"] = "chest"));

        Assert.Equal(ContentRule.DanglingReference, e.Rule);
        Assert.Contains("chest", e.Message);
    }

    [Fact]
    public void A_breakable_marker_without_a_variant_is_a_load_error()
    {
        var errors = Errors(n =>
        {
            foreach (var m in n["markers"]!.AsArray())
                if ((string?)m!["kind"] == "breakable") m.AsObject().Remove("variant");
        });

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ContentRule.DanglingReference, e.Rule));
    }

    //-------------------------------------------------------------------------
    // Shares
    //-------------------------------------------------------------------------

    [Fact]
    public void Each_player_rolls_his_own_share_of_a_break()
    {
        var a = Breaks.Request(5, "crate_1", "hero-a");
        var b = Breaks.Request(5, "crate_1", "hero-b");

        Assert.NotEqual(a.Seed, b.Seed);
        Assert.Equal(a, Breaks.Request(5, "crate_1", "hero-a"));
        Assert.Equal(Table.LootTable, a.LootTable);
    }

    [Fact]
    public void An_item_share_is_an_item_of_the_breakables_table()
    {
        var shares = Enumerable.Range(0, 500).Select(i => Rules.Loot.Breakable(Breaks.Request(1, "c" + i, "hero-a"), BreakOutcome.Item, 1)).ToList();

        Assert.All(shares, s => Assert.Equal(LootOutcome.Item, s.Outcome));
        Assert.All(shares, s => Assert.InRange(s.Tier, 0, Rules.Content.Rarities.Rows.Count - 1));
        Assert.True(shares.Select(s => s.Tier).Distinct().Count() >= 2, "five hundred items roll more than one tier");
    }

    [Fact]
    public void A_potion_share_is_a_potion_of_the_depths_band()
    {
        foreach (var row in Rules.Content.Levels.Rows)
        {
            var band = Rules.Content.Loot.Table(row.LootTable).Potions.Select(p => p.Base).ToHashSet();
            for (int i = 0; i < 50; i++)
            {
                var share = Rules.Loot.Breakable(Breaks.Request(3, "b" + i, "hero-a"), BreakOutcome.Potion, row.Depth);
                Assert.Equal(LootOutcome.Potion, share.Outcome);
                Assert.Contains(share.Potion!, band);
            }
        }
    }

    [Fact]
    public void An_australium_share_is_a_little_and_grows_with_depth()
    {
        double At(int depth) => Enumerable.Range(0, 400)
            .Average(i => Rules.Loot.Breakable(Breaks.Request(9, "a" + i, "hero-a"), BreakOutcome.Australium, depth).Australium);

        Assert.All(Enumerable.Range(0, 100), i =>
            Assert.True(Rules.Loot.Breakable(Breaks.Request(9, "a" + i, "hero-a"), BreakOutcome.Australium, 1).Australium >= 1));
        Assert.True(At(1) < Rules.Content.Loot.Australium.Base, $"a pile at depth 1 ({At(1):0.##}) is less than a kill's base");
        Assert.True(At(10) > At(1));
    }

    [Fact]
    public void A_share_of_a_bad_outcome_or_nothing_is_nothing()
    {
        foreach (var outcome in new[] { BreakOutcome.Nothing, BreakOutcome.Blast, BreakOutcome.Cloud, BreakOutcome.Ambush })
            Assert.Equal(new KillLoot(LootOutcome.Nothing, -1, null, 0), Rules.Loot.Breakable(Breaks.Request(1, "x", "hero-a"), outcome, 1));
    }

    [Fact]
    public void An_ambush_wakes_between_the_least_and_most_of_the_depths_templates()
    {
        var templates = Rules.Content.Levels.Rows[0].Templates.Select(t => t.Template).ToHashSet();
        var counts = new HashSet<int>();
        for (int i = 0; i < 300; i++)
        {
            var robots = Breaks.Ambush(4, "m" + i, 1);
            counts.Add(robots.Length);
            Assert.InRange(robots.Length, Table.Ambush.Min, Table.Ambush.Max);
            Assert.All(robots, r => Assert.Contains(r, templates));
        }

        Assert.Equal(Table.Ambush.Max - Table.Ambush.Min + 1, counts.Count);
        Assert.Equal(Breaks.Ambush(4, "m1", 1).ToArray(), Breaks.Ambush(4, "m1", 1).ToArray());
    }

    //-------------------------------------------------------------------------
    // Furnishing
    //-------------------------------------------------------------------------

    private static readonly RoomMarkers[] Level =
    [
        new("hall", [.. Enumerable.Range(0, 6).Select(i => new FurnitureMarker($"hall.c{i}", "descent_mark_container")),
            .. Enumerable.Range(0, 6).Select(i => new FurnitureMarker($"hall.b{i}", "descent_mark_barrel")),
            new FurnitureMarker("hall.shrine", "descent_mark_shrine")]),
        new("crypt", [new FurnitureMarker("crypt.c0", "descent_mark_container"), new FurnitureMarker("crypt.k0", "descent_mark_breakable_crate"),
            new FurnitureMarker("crypt.k1", "descent_mark_breakable_barrel")]),
    ];

    /// <summary>The furnishing over the shipped table with each container and barrel marker's otherwise set (a crate, a barrel) or taken away.</summary>
    private static FurnishingRules Furnishing(bool otherwise) => new(Load(n =>
    {
        foreach (var m in n["markers"]!.AsArray())
        {
            m!.AsObject().Remove("otherwise");
            if (otherwise && (string?)m["poi"] is "descent_mark_container" or "descent_mark_barrel")
                m["otherwise"] = (string?)m["poi"] == "descent_mark_container" ? "crate" : "barrel";
        }
    }));

    private static readonly FurnishingRules WithOtherwise = Furnishing(otherwise: true);

    [Fact]
    public void Every_container_and_barrel_marker_the_furnishing_passes_over_holds_a_breakable()
    {
        for (ulong seed = 0; seed < 300; seed++)
        {
            var f = WithOtherwise.Furnish(seed, Level);
            foreach (var m in Level.SelectMany(r => r.Markers).Where(m => m.Poi is "descent_mark_container" or "descent_mark_barrel"))
            {
                var on = f.Placed.Where(p => p.Marker == m.Id).ToList();
                var only = Assert.Single(on);
                if (only.Kind == FurnitureKind.Breakable)
                    Assert.Equal(m.Poi == "descent_mark_container" ? "crate" : "barrel", only.Variant);
            }

            Assert.DoesNotContain(f.Placed, p => p.Marker == "hall.shrine" && p.Kind == FurnitureKind.Breakable);
        }
    }

    [Fact]
    public void The_shipped_table_stands_breakables_only_on_breakable_markers()
    {
        // The levels ship their own crates and barrels (descent_mark_breakable_*): a container or
        // barrel marker the furnishing passes over stays empty, so a theme with none shows none.
        var markers = Rules.Content.Furniture.Markers.ToDictionary(m => m.Poi);
        int breakables = 0;
        for (ulong seed = 0; seed < 300; seed++)
            foreach (var p in Rules.Furniture.Placement.Furnish(seed, Level).Placed.Where(p => p.Kind == FurnitureKind.Breakable))
            {
                Assert.Contains(p.Marker, (string[])["crypt.k0", "crypt.k1"]);
                breakables++;
            }

        Assert.Null(markers["descent_mark_container"].Otherwise);
        Assert.Null(markers["descent_mark_barrel"].Otherwise);
        Assert.Equal(0, Rules.Content.Furniture.Markers.Count(m => m.Otherwise is not null));
        Assert.Equal(2 * 300, breakables);   // the stimulus: both breakable markers hold theirs on every seed
    }

    [Fact]
    public void A_breakable_marker_holds_its_rules_variant()
    {
        var f = Rules.Furniture.Placement.Furnish(11, Level);

        Assert.Equal(("crate", FurnitureKind.Breakable), f.Placed.Where(p => p.Marker == "crypt.k0").Select(p => (p.Variant, p.Kind)).Single());
        Assert.Equal(("barrel", FurnitureKind.Breakable), f.Placed.Where(p => p.Marker == "crypt.k1").Select(p => (p.Variant, p.Kind)).Single());
    }

    [Fact]
    public void The_breakables_on_passed_over_markers_leave_the_furnished_ones_as_they_were()
    {
        var plain = Furnishing(otherwise: false);
        int passedOver = 0;
        for (ulong seed = 0; seed < 300; seed++)
        {
            var with = WithOtherwise.Furnish(seed, Level).Placed;
            var without = plain.Furnish(seed, Level).Placed;
            int furnished = without.Length;

            Assert.Equal(without.ToArray(), with.Take(furnished).ToArray());   // the same rows, at the same places
            Assert.All(with.Skip(furnished), p => Assert.Equal(FurnitureKind.Breakable, p.Kind));
            passedOver += with.Length - furnished;
        }

        Assert.True(passedOver > 300, $"only {passedOver} markers passed over");
    }

    private static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate) => Loaded(mutate).Errors;

    private static DescentContent Load(Action<JsonNode> mutate) => Loaded(mutate).OrThrow();

    private static ContentLoadResult Loaded(Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Furniture, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return new ContentLoader().Load(source.Set(ContentTables.Furniture, Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
}
