using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Skills;
using Xunit;
using static TF2.Descent.Tests.Skills.SkillContent;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The Soldier's tree and the Mercenary tab as content (plan_tf2_rpg Appendix A; RU6 step 3):
/// the shape A.1 gives the tree, the rank totals it counts, the capacity skills the items
/// package reads, and no skill named after a TF2 item. The per-skill numbers are the
/// generated Appendix A facts'.
/// </summary>
public sealed class SoldierTreeTests
{
    private static readonly DescentContent Content = Shipped();

    private static IEnumerable<SkillDef> Tab(string tab) => Content.Skills.Rows.Where(s => s.Tab == tab);

    [Theory]
    [InlineData("ordnance")]
    [InlineData("airborne")]
    [InlineData("command")]
    public void Each_soldier_tab_has_ten_skills_two_per_tier_to_four_then_one_then_a_capstone(string tab)
    {
        var skills = Tab(tab).ToList();

        Assert.Equal(10, skills.Count);
        Assert.All(skills, s => Assert.Equal("soldier", s.Class));
        Assert.Equal([2, 2, 2, 2, 1, 1], Enumerable.Range(1, 6).Select(t => skills.Count(s => s.Tier == t)));
        var capstone = Assert.Single(skills, s => s.Kind == SkillKind.Capstone);
        Assert.Equal((6, 1), (capstone.Tier, capstone.MaxRank));
        Assert.All(skills.Where(s => s.Kind != SkillKind.Capstone), s => Assert.Equal(5, s.MaxRank));
    }

    [Fact]
    public void The_soldier_tabs_offer_138_ranks_and_the_mercenary_tab_47()
    {
        // A.1: "30 points to spend against 138 ranks on offer in the class tabs plus 47 in the Mercenary tab".
        Assert.Equal(138, Content.Skills.Rows.Where(s => s.Class == "soldier").Sum(s => s.MaxRank));
        Assert.Equal(47, Tab("mercenary").Sum(s => s.MaxRank));
    }

    [Fact]
    public void The_mercenary_tab_has_the_nine_utility_upgrades_of_A5_and_no_tiers()
    {
        string[] ids = ["deep_pockets", "storage_plan", "second_wind", "leech", "clear_head", "siphon", "scavenger", "haggler", "field_rations"];
        var tab = Tab("mercenary").ToList();

        Assert.Equal(ids, tab.Select(s => s.Id));
        Assert.All(tab, s => Assert.Equal((SkillKind.Upgrade, "mercenary", 0, "all"), (s.Kind, s.Class, s.Tier, s.Scope)));
        Assert.All(tab, s => Assert.NotNull(s.LevelFloor));
    }

    [Fact]
    public void Deep_pockets_has_seven_ranks_with_floors_4r_minus_3_and_sizes_the_backpack()
    {
        var s = Content.Skills["deep_pockets"];

        Assert.Equal(7, s.MaxRank);
        Assert.Equal([1, 5, 9, 13, 17, 21, 25], Enumerable.Range(1, 7).Select(r => s.LevelFloor!.At(r)));
        Assert.Equal("backpack", Assert.Single(s.Effect).Capacity);
    }

    [Fact]
    public void Storage_plan_has_five_ranks_with_floors_5r_minus_4_and_sizes_the_stash()
    {
        var s = Content.Skills["storage_plan"];

        Assert.Equal(5, s.MaxRank);
        Assert.Equal([1, 6, 11, 16, 21], Enumerable.Range(1, 5).Select(r => s.LevelFloor!.At(r)));
        Assert.Equal("stash", Assert.Single(s.Effect).Capacity);
    }

    [Fact]
    public void The_other_mercenary_skills_need_hero_level_3r()
    {
        var rest = Tab("mercenary").Where(s => s.Id is not ("deep_pockets" or "storage_plan")).ToList();

        Assert.Equal(7, rest.Count);
        Assert.All(rest, s => Assert.Equal([3, 6, 9, 12, 15], Enumerable.Range(1, 5).Select(r => s.LevelFloor!.At(r))));
    }

    [Fact]
    public void The_orders_are_the_three_command_auras()
    {
        Assert.Equal(["battle_orders", "dig_in", "forward_march"], Content.Skills.Rows.Where(s => s.HasTag("order")).Select(s => s.Id));
    }

    [Fact]
    public void No_skill_is_named_after_a_TF2_item()
    {
        var names = ItemNames();

        Assert.True(names.Count > 400, $"only {names.Count} item names read");   // the reading worked
        Assert.Contains(ItemBaseKeys.Normal("Black Box"), names);                   // and normalises as the validator does
        Assert.All(Content.Skills.Rows, s => Assert.DoesNotContain(ItemBaseKeys.Normal(s.Name), names));
    }

    [Fact]
    public void Every_upgrade_scope_is_all_or_a_family_and_every_action_one_damage_type()
    {
        Assert.All(Content.Skills.Rows.Where(s => s.Kind == SkillKind.Upgrade),
            s => Assert.True(s.Scope == SkillDef.AllWeapons || Content.Families.Contains(s.Scope!), s.Id));
        Assert.All(Content.Skills.Rows.Where(s => s.Kind is SkillKind.Action or SkillKind.Capstone),
            s => Assert.True(Content.DamageTypes.Contains(s.DamageType!), s.Id));
        Assert.Equal(["rocket_specialist", "trench_fighter"], Content.Skills.Rows.Where(s => s.Family is not null).Select(s => s.Id));
    }

    /// <summary>items_working.tsv's item names, normalised as <see cref="ItemBaseKeys.Normal"/> does.</summary>
    internal static HashSet<string> ItemNames()
    {
        string path = Path.Combine(RepoRoot(), "game", "mod_tf2", "scripts", "items", "items_working.tsv");
        Assert.True(File.Exists(path), $"{path} is missing");
        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("defindex\tname\titem_class", lines[0]);
        return [.. lines.Skip(1).Select(l => ItemBaseKeys.Normal(l.Split('\t')[1]))];
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SourceSharp.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("SourceSharp.slnx not found above " + AppContext.BaseDirectory);
    }
}
