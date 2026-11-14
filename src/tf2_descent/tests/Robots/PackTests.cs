using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// The roster roll (plan_descent_logic RU7, section 3.3; plan_tf2_rpg 2f, R25, D11):
/// deterministic, sized by depth and party, standing at the room's markers, and over 10k seeds
/// a depth no pack more than half immune to one type, no robot immune to two, and 100 %
/// resistance only through a champion affix.
/// </summary>
public sealed class PackTests
{
    private static readonly TF2DescentRules Rules = new();
    private static PackRules Packs => Rules.Packs;

    [Fact]
    public void A_pack_is_a_pure_function_of_its_inputs()
    {
        var a = Packs.RollPack(77, 3, 8, 2, 4);
        var b = Packs.RollPack(77, 3, 8, 2, 4);

        Assert.Equal(a.Robots.Select(Describe), b.Robots.Select(Describe));
        Assert.NotEqual(a.Robots.Select(Describe), Packs.RollPack(77, 4, 8, 2, 4).Robots.Select(Describe));
    }

    private static string Describe(RolledRobot r) =>
        $"{r.Template}/{r.Scale:R}/{string.Join("+", r.Affixes)}/{string.Join(",", r.Temperament.Levels)}/{r.Stats.Life:R}/{r.Marker}";

    [Fact]
    public void A_room_with_no_pack_marker_has_no_pack()
    {
        Assert.Empty(Packs.RollPack(1, 0, 3, 1, 0).Robots);
    }

    [Fact]
    public void Robots_stand_at_the_rooms_markers_in_turn()
    {
        var pack = Packs.RollPack(5, 1, 15, 4, 2);

        Assert.True(pack.Robots.Length > 2);
        Assert.Equal(pack.Robots.Select(r => r.Index % 2), pack.Robots.Select(r => r.Marker));
    }

    [Fact]
    public void Pack_size_lies_in_the_depths_range_raised_for_the_party()
    {
        foreach (int depth in new[] { 1, 8, 15 })
            foreach (int party in new[] { 1, 2, 4 })
            {
                var row = Rules.Robots.Depth(depth);
                for (ulong seed = 0; seed < 300; seed++)
                {
                    int size = Packs.RollPack(seed, 0, depth, party, 3).Robots.Length;
                    Assert.InRange(size, Packs.PackSize(row.PackMin, party), Packs.PackSize(row.PackMax, party));
                }
            }
    }

    [Fact]
    public void A_pack_draws_only_its_depths_templates()
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var allowed = Rules.Robots.Depth(1).Templates.Select(t => t.Template).ToHashSet();
            Assert.All(Packs.RollPack(seed, 0, 1, 1, 3).Robots, r => Assert.Contains(r.Template, allowed));
        }
    }

    [Fact]
    public void Robots_are_rolled_at_the_depths_monster_level_with_the_partys_life()
    {
        var pack = Packs.RollPack(9, 2, 6, 3, 3);
        var r = pack.Robots[0];

        var expected = Rules.Robots.Stats(Rules.Robots.Template(r.Template), r.Scale, Rules.Robots.MonsterLevel(6), r.Affixes, 3);
        Assert.Equal(expected.Life, r.Stats.Life, 9);
        Assert.Equal(11, r.Stats.Level);
    }

    [Fact]
    public void Champions_appear_with_at_most_the_depths_affixes()
    {
        int champions = 0;
        for (ulong seed = 0; seed < 3000; seed++)
            foreach (var r in Packs.RollPack(seed, 0, 12, 4, 3).Robots.Where(r => r.Champion))
            {
                champions++;
                Assert.InRange(r.Affixes.Length, 1, Rules.Robots.Depth(12).MaxAffixes);
                Assert.Equal(r.Affixes.Length, r.Affixes.Distinct().Count());
            }
        Assert.True(champions > 100);
    }

    [Fact]
    public void Depth_1_has_no_champions()
    {
        Assert.DoesNotContain(Enumerable.Range(0, 2000).SelectMany(s => Packs.RollPack((ulong)s, 0, 1, 4, 3).Robots), r => r.Champion);
    }

    [Fact]
    public void No_generated_pack_is_more_than_half_immune_to_one_type_over_10k_seeds()
    {
        int immune = 0;
        foreach (int depth in new[] { 5, 10, 15 })
            for (ulong seed = 0; seed < 10_000; seed++)
            {
                var pack = Packs.RollPack(seed, (int)(seed % 11), depth, 4, 3);
                foreach (var g in pack.Robots.Where(r => r.Stats.Immunity is not null).GroupBy(r => r.Stats.Immunity))
                {
                    immune += g.Count();
                    Assert.True(g.Count() * 2 <= pack.Robots.Length, $"depth {depth} seed {seed}: {g.Count()} of {pack.Robots.Length} immune to {g.Key}");
                }
            }
        Assert.True(immune > 100);   // the stimulus: immune champions were rolled
    }

    [Fact]
    public void The_half_immune_rule_holds_where_every_robot_would_be_an_immune_champion()
    {
        // Planted: every robot a champion, and the only affixes Fireproof and Extra Fast, so
        // without the rule most of each pack would be fire-immune.
        var content = Planted(
            (ContentTables.Levels, n => { foreach (var d in n.AsArray()) d!["championPct"] = 100; }),
            (ContentTables.Champions, n =>
            {
                var keep = n.AsArray().Where(a => (string?)a!["id"] is "fireproof" or "extra_fast").Select(a => a!.DeepClone()).ToArray();
                n.AsArray().Clear();
                foreach (var a in keep) n.AsArray().Add(a);
            }));
        var robots = new RobotRules(content);
        var packs = new PackRules(content, robots, new TemperamentRules(content, robots));

        int immune = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            var pack = packs.RollPack(seed, 0, 12, 4, 3);
            int fire = pack.Robots.Count(r => r.Stats.Immunity == "fire");
            immune += fire;
            Assert.True(fire * 2 <= pack.Robots.Length, $"seed {seed}: {fire} of {pack.Robots.Length} fire-immune");
        }
        Assert.True(immune > 1000);
    }

    internal static DescentContent Planted(params (string Table, Action<JsonNode> Mutate)[] changes)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        foreach (var (table, mutate) in changes)
        {
            Assert.True(source.TryRead(table, out var bytes));
            var node = JsonNode.Parse(bytes)!;
            mutate(node);
            source.Set(table, Encoding.UTF8.GetBytes(node.ToJsonString()));
        }
        return new ContentLoader().Load(source).OrThrow();
    }

    [Fact]
    public void No_generated_robot_is_immune_to_two_types_and_only_champions_reach_100_percent()
    {
        for (ulong seed = 0; seed < 10_000; seed++)
            foreach (var r in Packs.RollPack(seed, 1, 15, 4, 3).Robots)
            {
                Assert.True(r.Affixes.Count(a => Rules.Content.Champions[a].Immunity is not null) <= 1);
                Assert.True(r.Stats.Resist.Values.Count(v => v >= 100) <= 1);
                foreach (var (type, v) in r.Stats.Resist)
                    if (v >= 100) Assert.Contains(r.Affixes, a => Rules.Content.Champions[a].Immunity == type);
                    else Assert.InRange(v, -50, 75);
            }
    }
}
