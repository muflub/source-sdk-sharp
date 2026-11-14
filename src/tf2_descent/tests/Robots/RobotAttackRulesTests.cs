using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using Xunit;

namespace TF2.Descent.Tests.Robots;

/// <summary>
/// robot_attacks.json (plan_descent_logic 3.8, section 2; HO6, HO7): every family a robot holds
/// attacks, the cap is the edict budget's, each kind carries what it needs, and the numbers an
/// attack and a heal use are the rules' own arithmetic.
/// </summary>
public sealed class RobotAttackRulesTests
{
    private static readonly TF2DescentRules Rules = new();

    private static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate) => RobotContentTests.Errors(RobotAttackTable.TableName, mutate);

    private static JsonObject Row(JsonNode n, string id) =>
        n["attacks"]!.AsArray().Select(a => a!.AsObject()).First(a => (string)a["id"]! == id);

    [Fact]
    public void The_shipped_attack_table_loads_clean()
    {
        Assert.Empty(Errors(_ => { }));
    }

    [Fact]
    public void The_attack_table_is_one_of_the_content_tables()
    {
        Assert.Contains(RobotAttackTable.TableName, ContentTables.Names());
    }

    [Fact]
    public void Every_attack_a_robot_holds_has_a_row()
    {
        var attacks = Rules.Content.Robots.Rows.Select(r => r.Attack).Distinct().ToList();

        Assert.Equal(14, attacks.Count);   // HO13: the three bosses' rows and the Tank's; no robot holds the bare melee row now
        Assert.All(Rules.Content.Robots.Rows, t => Assert.Equal(t.Attack, Rules.Attacks.Of(t).Id));
    }

    [Fact]
    public void The_spy_fights_with_its_knife_and_the_buster_with_its_blast()
    {
        Assert.Equal(("melee", "knife"), (Rules.Robots.Template("spy").Family, Rules.Robots.Template("spy").Attack));
        Assert.Equal("sentry_buster", Rules.Robots.Template("sentry_buster").Attack);
        Assert.Equal("scattergun", Rules.Robots.Template("scout").Attack);
    }

    [Fact]
    public void A_template_naming_an_attack_with_no_row_is_refused()
    {
        var errors = RobotContentTests.Errors(ContentTables.Robots,
            n => n.AsArray().Select(r => r!.AsObject()).First(r => (string)r["id"]! == "spy")["attack"] = "garrotte");

        Assert.Contains(errors, e => e.Rule == ContentRule.DanglingReference && e.Message.Contains("'garrotte'"));
    }

    [Fact]
    public void A_backstab_on_an_attack_that_does_not_swing_is_refused()
    {
        var errors = Errors(n =>
        {
            Row(n, "scattergun")["backstabMultiplier"] = 2;
            Row(n, "scattergun")["backstabConeDegrees"] = 90;
        });

        Assert.Contains(errors, e => e.Row == "scattergun" && e.Message.Contains("only a melee attack backstabs"));
    }

    [Fact]
    public void A_backstab_with_no_cone_is_refused()
    {
        var errors = Errors(n => Row(n, "knife")["backstabConeDegrees"] = 0);

        Assert.Contains(errors, e => e.Row == "knife" && e.Message.Contains("cone behind its victim"));
    }

    [Fact]
    public void A_telegraph_with_no_windup_is_refused()
    {
        var errors = Errors(n => Row(n, "sniper_rifle")["windupSeconds"] = 0);

        Assert.Contains(errors, e => e.Row == "sniper_rifle" && e.Message.Contains("telegraph is shown over a windup"));
    }

    [Fact]
    public void An_unknown_telegraph_is_refused()
    {
        var errors = Errors(n => Row(n, "sniper_rifle")["telegraph"] = "fireworks");

        Assert.Contains(errors, e => e.Row == "sniper_rifle" && e.Rule == ContentRule.Malformed && e.Message.Contains("'fireworks'"));
    }

    [Fact]
    public void A_blast_with_no_radius_is_refused()
    {
        var errors = Errors(n => Row(n, "sentry_buster")["blastRadius"] = 0);

        Assert.Contains(errors, e => e.Row == "sentry_buster" && e.Message.Contains("blastRadius"));
    }

    [Fact]
    public void Gravity_on_an_attack_that_throws_nothing_is_refused()
    {
        var errors = Errors(n => Row(n, "minigun")["gravity"] = 320);

        Assert.Contains(errors, e => e.Row == "minigun" && e.Message.Contains("lobbed under gravity"));
    }

    [Fact]
    public void A_row_that_is_no_family_and_names_no_damage_type_is_refused()
    {
        var errors = Errors(n => Row(n, "knife").Remove("damageType"));

        Assert.Contains(errors, e => e.Row == "knife" && e.Rule == ContentRule.DanglingReference);
    }

    [Fact]
    public void The_new_rows_carry_the_numbers_the_families_read()
    {
        Assert.Equal(0.75, Rules.Attacks.Of("minigun").SpinUpSeconds, 9);
        Assert.True(Rules.Attacks.Of("minigun").TurnDegreesPerSecond > 0);
        Assert.True(Rules.Attacks.Of("grenade_launcher").Gravity > 0);
        Assert.Equal(RobotAttack.LaserTelegraph, Rules.Attacks.Of("sniper_rifle").Telegraph);
        Assert.True(Rules.Attacks.Of("flamethrower").AfterburnSeconds > 0);
        Assert.Equal((RobotAttackKind.Blast, RobotAttack.SpinTelegraph), (Rules.Attacks.Of("sentry_buster").Kind, Rules.Attacks.Of("sentry_buster").Telegraph));
        Assert.True(Rules.Attacks.Of("knife").BackstabMultiplier > 1);
    }

    [Fact]
    public void A_family_a_robot_holds_with_no_attack_is_refused()
    {
        var errors = Errors(n =>
        {
            var attacks = n["attacks"]!.AsArray();
            attacks.Remove(Row(n, "minigun"));   // HO13: no robot holds a bare melee family now (Captain Punch names his fists)
        });

        Assert.Contains(errors, e => e.Rule == ContentRule.DanglingReference && e.Message.Contains("family 'minigun'"));
    }

    [Fact]
    public void A_projectile_cap_past_the_edict_budget_is_refused()
    {
        var errors = Errors(n => n["projectileCap"] = 33);

        Assert.Contains(errors, e => e.Table == RobotAttackTable.TableName && e.Rule == ContentRule.OutOfRange && e.Message.Contains("projectileCap"));
    }

    [Fact]
    public void A_projectile_attack_that_names_no_projectile_is_refused()
    {
        var errors = Errors(n => Row(n, "rocket_launcher")["projectile"] = "");

        Assert.Contains(errors, e => e.Row == "rocket_launcher" && e.Message.Contains("names its projectile"));
    }

    [Fact]
    public void A_hitscan_attack_with_no_reach_is_refused()
    {
        var errors = Errors(n => Row(n, "scattergun")["rangeUnits"] = 0);

        Assert.Contains(errors, e => e.Row == "scattergun" && e.Rule == ContentRule.OutOfRange && e.Message.Contains("rangeUnits"));
    }

    [Fact]
    public void A_flame_with_no_cone_is_refused()
    {
        var errors = Errors(n => Row(n, "flamethrower")["coneDegrees"] = 0);

        Assert.Contains(errors, e => e.Row == "flamethrower" && e.Message.Contains("coneDegrees"));
    }

    [Fact]
    public void A_clip_with_no_reload_time_is_refused()
    {
        var errors = Errors(n => Row(n, "rocket_launcher")["reloadSeconds"] = 0);

        Assert.Contains(errors, e => e.Row == "rocket_launcher" && e.Message.Contains("reloadSeconds"));
    }

    [Fact]
    public void An_unknown_attack_kind_is_refused()
    {
        var errors = Errors(n => Row(n, "melee")["kind"] = "laser");

        Assert.Contains(errors, e => e.Rule == ContentRule.Malformed && e.Message.Contains("'laser'"));
    }

    [Fact]
    public void An_attack_row_naming_no_family_is_refused()
    {
        var errors = Errors(n => Row(n, "minigun")["id"] = "railgun");

        Assert.Contains(errors, e => e.Rule == ContentRule.DanglingReference && e.Message.Contains("railgun"));
    }

    [Fact]
    public void The_cap_is_the_edict_budgets_thirty_two()
    {
        Assert.Equal(32, Rules.Attacks.ProjectileCap);
    }

    [Fact]
    public void An_attacks_damage_is_the_robots_damage_by_its_share()
    {
        var soldier = Rules.Robots.Stats(Rules.Robots.Template("soldier"), 1.0, 1, []);
        var flame = Rules.Robots.Stats(Rules.Robots.Template("pyro"), 1.0, 3, []);

        Assert.Equal(90.0, RobotAttackRules.AttackDamage(soldier, Rules.Attacks.Of("rocket_launcher")), 9);
        Assert.Equal(flame.Damage * 0.1, RobotAttackRules.AttackDamage(flame, Rules.Attacks.Of("flamethrower")), 9);
    }

    [Fact]
    public void An_attacks_pellets_add_up_to_its_damage()
    {
        var scout = Rules.Robots.Stats(Rules.Robots.Template("scout"), 1.0, 4, []);
        var a = Rules.Attacks.Of("scattergun");

        Assert.Equal(10, a.Pellets);
        Assert.Equal(RobotAttackRules.AttackDamage(scout, a), RobotAttackRules.PelletDamage(scout, a) * a.Pellets, 9);
    }

    [Fact]
    public void An_attack_is_resisted_as_its_familys_damage_type()
    {
        Assert.Equal("blast", Rules.Attacks.DamageTypeOf(Rules.Attacks.Of("rocket_launcher")));
        Assert.Equal("bullet", Rules.Attacks.DamageTypeOf(Rules.Attacks.Of("melee")));
        Assert.Equal("fire", Rules.Attacks.DamageTypeOf(Rules.Attacks.Of("flamethrower")));
    }

    [Fact]
    public void A_row_that_is_no_family_is_resisted_as_its_own_damage_type()
    {
        Assert.Equal("bullet", Rules.Attacks.DamageTypeOf(Rules.Attacks.Of("knife")));
        Assert.Equal("blast", Rules.Attacks.DamageTypeOf(Rules.Attacks.Of("sentry_buster")));
    }

    [Fact]
    public void A_melee_swing_reaches_as_far_as_the_robots_scale()
    {
        var t = Rules.Robots.Template("spy");

        Assert.Equal(48.0, RobotAttackRules.MeleeReach(Rules.Robots.Stats(t, 1.0, 1, [])), 9);
        Assert.Equal(72.0, RobotAttackRules.MeleeReach(Rules.Robots.Stats(t, 1.5, 1, [])), 9);
    }

    [Fact]
    public void A_heal_at_level_one_is_the_tables_and_rises_with_the_damage_curve()
    {
        double perLevel = Rules.Content.RobotRules.Level.DamagePerLevelPct;

        Assert.Equal(24.0, Rules.Attacks.HealPerSecond(1), 9);
        Assert.Equal(24.0 * Math.Pow(1 + perLevel / 100, 4), Rules.Attacks.HealPerSecond(5), 9);
    }

    [Fact]
    public void Seconds_become_whole_ticks_rounded_and_floored()
    {
        Assert.Equal(53L, RobotAttackRules.Ticks(0.8, 1 / 0.015));
        Assert.Equal(1L, RobotAttackRules.Ticks(0.001, 1 / 0.015, least: 1));
        Assert.Equal(0L, RobotAttackRules.Ticks(0, 1 / 0.015));
    }
}
