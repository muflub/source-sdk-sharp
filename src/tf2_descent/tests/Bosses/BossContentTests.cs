using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Bosses;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Robots;
using Xunit;

using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// bosses.json and the bosses on paper (plan_descent_logic HO13; plan_tf2_rpg D13; mine plan D4,
/// D11): who holds each boss depth, the phases by life, the camps a kill opens, the Tank's
/// immunities, and one load error per rule of the table.
/// </summary>
public sealed class BossContentTests
{
    private static readonly TF2DescentRules Rules = new();

    private static BossSettings S => Rules.Content.Bosses;

    private static MemoryContentSource Bosses(Action<JsonObject> mutate) => Planted(BossTable.TableName, n => mutate(n.AsObject()));

    private static JsonObject Boss(JsonObject table, string id) => Row(table["bosses"]!, id);

    private static IReadOnlyList<ContentError> BossErrors(IContentSource source) =>
        [.. Errors(source).Where(e => e.Table == BossTable.TableName)];

    [Fact]
    public void The_shipped_bosses_load_with_no_error()
    {
        Assert.Empty(Errors(ShippedCopy()));
        Assert.Equal(3, S.Bosses.Count);   // the stimulus: the table was read
    }

    [Fact]
    public void Each_boss_depth_holds_its_boss_of_d13()
    {
        Assert.Equal("sir_nukesalot", Rules.Bosses.AtDepth(5)!.Id);
        Assert.Equal("major_crits", Rules.Bosses.AtDepth(10)!.Id);
        Assert.Equal("captain_punch", Rules.Bosses.AtDepth(15)!.Id);
        Assert.Null(Rules.Bosses.AtDepth(4));
    }

    [Fact]
    public void Major_crits_brings_the_tank_and_the_others_bring_no_companion()
    {
        Assert.Equal(S.Tank.Template, Rules.Bosses.Of("major_crits")!.Companion);
        Assert.Equal("", Rules.Bosses.Of("sir_nukesalot")!.Companion);
        Assert.Equal("", Rules.Bosses.Of("captain_punch")!.Companion);
    }

    [Fact]
    public void Each_boss_fights_with_an_attack_row_of_its_own()
    {
        foreach (var id in Rules.Bosses.Ids)
        {
            var t = Rules.Robots.Template(id);
            Assert.NotNull(t.AttackRow);
            Assert.NotNull(Rules.Content.RobotAttacks.Of(t.Attack));
            Assert.DoesNotContain(Rules.Content.Robots.Rows, r => r.Id != id && r.Attack == t.Attack);
        }
    }

    [Fact]
    public void A_boss_is_in_its_first_phase_at_full_life()
    {
        var crits = Rules.Bosses.Of("major_crits")!;

        Assert.Equal(0, BossRules.PhaseAt(crits, 1000, 1000));
        Assert.Equal(0, BossRules.PhaseAt(crits, 661, 1000));
    }

    [Fact]
    public void A_boss_enters_each_phase_at_its_threshold_and_not_before()
    {
        var crits = Rules.Bosses.Of("major_crits")!;

        Assert.Equal(1, BossRules.PhaseAt(crits, 660, 1000));
        Assert.Equal(1, BossRules.PhaseAt(crits, 331, 1000));
        Assert.Equal(2, BossRules.PhaseAt(crits, 330, 1000));
        Assert.Equal(2, BossRules.PhaseAt(crits, 0, 1000));
    }

    [Fact]
    public void Captain_punch_has_four_phases_at_quarters()
    {
        var punch = Rules.Bosses.Of("captain_punch")!;

        Assert.Equal([100.0, 75.0, 50.0, 25.0], punch.Phases.Select(p => p.At));
        Assert.Equal(3, BossRules.PhaseAt(punch, 250, 1000));
        Assert.Equal(2, BossRules.PhaseAt(punch, 251, 1000));
    }

    [Fact]
    public void A_phase_once_entered_holds_when_the_boss_is_healed_back_over_its_threshold()
    {
        var crits = Rules.Bosses.Of("major_crits")!;

        Assert.Equal(1, BossRules.Phase(crits, 900, 1000, reached: 1));
        Assert.Equal(0, BossRules.Phase(crits, 900, 1000, reached: 0));
    }

    [Fact]
    public void A_phase_names_its_special_and_its_adds()
    {
        var nuke = Rules.Bosses.Of("sir_nukesalot")!;

        Assert.Equal("nuke_volley", Rules.Bosses.SpecialOf(nuke, 0)!.Id);
        Assert.Empty(BossRules.AddsOf(nuke, 0));
        Assert.Equal([("demoman", 2)], BossRules.AddsOf(nuke, 1).Select(a => (a.Template, a.Count)));
        Assert.Null(Rules.Bosses.SpecialOf(Rules.Bosses.Of("captain_punch")!, 0));
    }

    [Fact]
    public void A_boss_kill_is_recorded_on_the_sheet_once_and_only_once()
    {
        var sheet = new HeroSheet { ClassId = "soldier" };

        var once = Rules.Bosses.RecordKill(sheet, "sir_nukesalot", out bool first);
        var twice = Rules.Bosses.RecordKill(once, "sir_nukesalot", out bool second);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(["sir_nukesalot"], twice.BossKills);
        Assert.Same(once, twice);
    }

    [Fact]
    public void A_robot_that_is_no_boss_records_nothing()
    {
        var sheet = new HeroSheet { ClassId = "soldier" };

        var after = Rules.Bosses.RecordKill(sheet, "tank", out bool added);

        Assert.False(added);
        Assert.Empty(after.BossKills);
    }

    [Fact]
    public void The_level_5_kill_opens_the_camp_at_6_through_the_town_s_rules()
    {
        var depths = Rules.Bosses.DepthsKilled(["sir_nukesalot"]);

        Assert.Equal([5], depths);
        Assert.Contains(TownDescents.FirstCamp, TownDescents.Destinations(5, depths));
        Assert.DoesNotContain(TownDescents.FirstCamp, TownDescents.Destinations(5, Rules.Bosses.DepthsKilled([])));
        Assert.Equal([5, 10], Rules.Bosses.DepthsKilled(["major_crits", "sir_nukesalot", "tank"]));
    }

    [Fact]
    public void The_tank_shrugs_stuns_slows_and_throws_and_takes_a_burn()
    {
        Assert.True(Rules.Bosses.Shrugs("tank", "stun"));
        Assert.True(Rules.Bosses.Shrugs("tank", "knock_up"));
        Assert.True(Rules.Bosses.Shrugs("tank", "slowed"));
        Assert.False(Rules.Bosses.Shrugs("tank", "burn"));
        Assert.False(Rules.Bosses.Shrugs("soldier", "stun"));
    }

    // ------------------------------------------------------------------ one load error a rule

    [Fact]
    public void A_boss_template_with_no_script_is_refused()
    {
        var errors = BossErrors(Bosses(n => n["bosses"]!.AsArray().Remove(Boss(n, "captain_punch"))));

        Assert.Contains(errors, e => e.Row == "captain_punch" && e.Rule == ContentRule.DanglingReference);
    }

    [Fact]
    public void A_script_for_a_robot_that_is_no_boss_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "captain_punch")["id"] = "heavy"));

        Assert.Contains(errors, e => e.Row == "heavy" && e.Rule == ContentRule.OutOfRange);
    }

    [Fact]
    public void A_boss_at_a_depth_that_has_no_arena_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "sir_nukesalot")["depth"] = 4));

        Assert.Contains(errors, e => e.Row == "sir_nukesalot" && e.Message.Contains("not a boss depth", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_bosses_at_one_depth_are_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "major_crits")["depth"] = 5));

        Assert.Contains(errors, e => e.Rule == ContentRule.DuplicateId && e.Message.Contains("depth 5", StringComparison.Ordinal));
    }

    [Fact]
    public void Phases_that_do_not_fall_are_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "major_crits")["phases"]![2]!["at"] = 70));

        Assert.Contains(errors, e => e.Row == "major_crits" && e.Message.Contains("not below phase 1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_first_phase_short_of_full_life_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "major_crits")["phases"]![0]!["at"] = 90));

        Assert.Contains(errors, e => e.Row == "major_crits" && e.Message.Contains("first phase begins at 100", StringComparison.Ordinal));
    }

    [Fact]
    public void An_add_that_is_a_boss_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "major_crits")["phases"]![1]!["adds"]![0]!["template"] = "captain_punch"));

        Assert.Contains(errors, e => e.Row == "major_crits" && e.Message.Contains("a boss: an arena holds one", StringComparison.Ordinal));
    }

    [Fact]
    public void More_adds_than_a_boss_may_take_of_the_pool_are_refused()
    {
        var errors = BossErrors(Bosses(n =>
        {
            foreach (var p in Boss(n, "captain_punch")["phases"]!.AsArray())
                p!["adds"] = JsonNode.Parse("""[ { "template": "heavy", "count": 6 } ]""");
        }));

        Assert.Contains(errors, e => e.Row == "captain_punch" && e.Message.Contains($"more than the {BossTable.MaxAdds}", StringComparison.Ordinal));
    }

    [Fact]
    public void A_special_a_phase_names_that_is_no_row_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "sir_nukesalot")["phases"]![1]!["special"] = "meteor"));

        Assert.Contains(errors, e => e.Row == "sir_nukesalot" && e.Rule == ContentRule.DanglingReference);
    }

    [Fact]
    public void A_volley_of_an_attack_that_is_no_projectile_is_refused()
    {
        var errors = BossErrors(Bosses(n => Row(n["specials"]!, "crit_salvo")["attack"] = "boss_fists"));

        Assert.Contains(errors, e => e.Row == "crit_salvo" && e.Message.Contains("names a projectile attack", StringComparison.Ordinal));
    }

    [Fact]
    public void A_telegraph_too_short_to_see_is_refused()
    {
        var errors = BossErrors(Bosses(n => Row(n["specials"]!, "ground_slam")["windupSeconds"] = 0.05));

        Assert.Contains(errors, e => e.Row == "ground_slam" && e.Rule == ContentRule.OutOfRange);
    }

    [Fact]
    public void A_companion_that_is_a_boss_is_refused()
    {
        var errors = BossErrors(Bosses(n => Boss(n, "major_crits")["companion"] = "sir_nukesalot"));

        Assert.Contains(errors, e => e.Row == "major_crits" && e.Message.Contains("'companion'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_condition_the_tank_shrugs_that_no_table_knows_is_refused()
    {
        var errors = BossErrors(Bosses(n => n["tank"]!["immune"]!.AsArray().Add("petrify")));

        Assert.Contains(errors, e => e.Row == "tank" && e.Message.Contains("petrify", StringComparison.Ordinal));
    }
}
