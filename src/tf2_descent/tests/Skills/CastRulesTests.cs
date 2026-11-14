using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using Xunit;
using static TF2.Descent.Tests.Skills.SkillRulesTests;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// Casting as pure rules (plan_tf2_rpg 2e, A.4; RU6 step 4): mana spent and refused when
/// short, cooldowns, the BlastJumping requirement, charge stages, toggles and their drain,
/// Orders one at a time until the capstone, and Chain of Command on Orders.
/// </summary>
public sealed class CastRulesTests
{
    private static readonly TF2DescentRules Rules = new();
    private static SkillRules Skills => Rules.Skills;
    private static CastRules Casting => Rules.Casting;
    private static SkillBonuses None => SkillBonuses.None;

    private static CasterState Full(double mana = 500) => new() { Mana = mana };

    private static CastResult Cast(HeroSheet hero, CasterState state, string id, int? stages = null, HeroStats? stats = null) =>
        Casting.Cast(hero, stats ?? Skills.Stats(hero, None), None, state, id, stages);

    /// <summary>A level 30 Soldier through the Command tab to Supreme Commander.</summary>
    private static HeroSheet Commander(bool capstone) =>
        Learn(AtLevel("soldier", 30), [.. new[] { "battle_orders", "basic_training", "dig_in", "forward_march", "chain_of_command" },
            .. capstone ? new[] { "supreme_commander" } : []]);

    //-------------------------------------------------------------------------
    // Mana
    //-------------------------------------------------------------------------

    [Fact]
    public void A_cast_spends_the_skills_mana_cost()
    {
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");
        var r = Cast(hero, Full(50), "patriot_missile");

        Assert.True(r.Ok);
        Assert.Equal(4, r.ManaSpent);
        Assert.Equal(46, r.State.Mana);
        Assert.Equal("patriot_missile", r.Skill!.Id);
    }

    [Fact]
    public void A_cast_short_of_mana_is_refused_and_spends_nothing()
    {
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");
        var state = Full(3.9);
        var r = Cast(hero, state, "patriot_missile");

        Assert.Equal(CastRefusal.NoMana, r.Refusal);
        Assert.Same(state, r.State);
        Assert.Equal(0, r.ManaSpent);
    }

    [Fact]
    public void A_charge_spends_its_mana_per_readied_stage()
    {
        // Salvo: 3 mana a rocket, 3 rockets at rank 1.
        var hero = Learn(AtLevel("soldier", 6), "patriot_missile", "salvo");

        Assert.Equal(9, Cast(hero, Full(), "salvo").ManaSpent);
        Assert.Equal(6, Cast(hero, Full(), "salvo", stages: 2).ManaSpent);
        Assert.Equal(9, Cast(hero, Full(), "salvo", stages: 7).ManaSpent);   // no more than the rank readies
    }

    [Fact]
    public void A_mana_cost_stat_scales_every_cast()
    {
        // Screamin' Eagle's buff: actions cost half mana.
        var hero = Learn(AtLevel("soldier", 2), "patriot_missile");
        var eagle = Skills.Stats(hero, None, [new Contribution("mana_cost_pct", -50, new ContributionSource(SourceKind.Buff, "screamin_eagle"))]);

        Assert.Equal(2, Cast(hero, Full(), "patriot_missile", stats: eagle).ManaSpent);
    }

    //-------------------------------------------------------------------------
    // Refusals
    //-------------------------------------------------------------------------

    [Fact]
    public void A_cooldown_holds_until_it_has_passed()
    {
        var hero = Learn(AtLevel("soldier", 12), "patriot_missile", "emp_charge");   // cooldown 8 s
        var first = Cast(hero, Full(), "emp_charge");

        Assert.True(first.Ok);
        Assert.Equal(CastRefusal.OnCooldown, Cast(hero, first.State.After(7.9), "emp_charge").Refusal);
        Assert.True(Cast(hero, first.State.After(8), "emp_charge").Ok);
        Assert.True(Cast(hero, first.State, "patriot_missile").Ok);   // another skill is not held up
    }

    [Fact]
    public void Rocket_leaps_cooldown_falls_with_rank()
    {
        var one = Learn(AtLevel("soldier", 30), "jump_training", "stomp", "rocket_leap");
        var five = Learn(one, Times("rocket_leap", 4));

        Assert.Equal(10, Cast(one, Full(), "rocket_leap").State.CooldownLeft("rocket_leap"));
        Assert.Equal(6, Cast(five, Full(), "rocket_leap").State.CooldownLeft("rocket_leap"));
    }

    [Fact]
    public void An_upgrade_an_unlearned_skill_and_an_unknown_one_cannot_be_cast()
    {
        var hero = Learn(AtLevel("soldier", 2), "marksmanship");

        Assert.Equal(CastRefusal.NotCast, Cast(hero, Full(), "marksmanship").Refusal);
        Assert.Equal(CastRefusal.NotLearned, Cast(hero, Full(), "patriot_missile").Refusal);
        Assert.Equal(CastRefusal.UnknownSkill, Cast(hero, Full(), "sentry_gun").Refusal);
    }

    [Fact]
    public void Rain_of_freedom_casts_only_while_blast_jumping()
    {
        var hero = Learn(AtLevel("soldier", 18), "patriot_missile", "salvo", "rain_of_freedom");

        Assert.Equal(CastRefusal.NeedsBlastJumping, Cast(hero, Full(), "rain_of_freedom").Refusal);
        Assert.True(Cast(hero, Full() with { BlastJumping = true }, "rain_of_freedom").Ok);
    }

    //-------------------------------------------------------------------------
    // Toggles
    //-------------------------------------------------------------------------

    [Fact]
    public void A_toggle_turns_on_then_off_and_drains_while_on()
    {
        var hero = Learn(AtLevel("soldier", 18), "jump_training", "jump_training", "jump_training", "parachute");
        var on = Cast(hero, Full(10), "parachute");

        Assert.Contains("parachute", on.State.Toggles);
        var drained = Casting.Drain(hero, Skills.Stats(hero, None), None, on.State, 3);
        Assert.Equal(10 - 2 * 3, drained.Mana, 9);                         // 2 mana a second at rank 1
        var off = Cast(hero, drained, "parachute");
        Assert.DoesNotContain("parachute", off.State.Toggles);
        Assert.Equal(drained.Mana, off.State.Mana);
        Assert.Equal(off.State.Mana, Casting.Drain(hero, Skills.Stats(hero, None), None, off.State, 3).Mana);
    }

    [Fact]
    public void A_toggle_turns_off_when_its_drain_cannot_be_paid()
    {
        var hero = Learn(AtLevel("soldier", 18), "jump_training", "jump_training", "jump_training", "parachute");
        var on = Cast(hero, Full(3), "parachute");

        var drained = Casting.Drain(hero, Skills.Stats(hero, None), None, on.State, 2);
        Assert.Equal(0, drained.Mana);
        Assert.Empty(drained.Toggles);
    }

    //-------------------------------------------------------------------------
    // Orders
    //-------------------------------------------------------------------------

    [Fact]
    public void Issuing_an_order_ends_the_one_in_force()
    {
        var hero = Commander(capstone: false);
        var first = Cast(hero, Full(), "battle_orders");
        var second = Cast(hero, first.State.After(1), "dig_in");

        Assert.Equal(["battle_orders"], first.State.ActiveOrders);
        Assert.Equal(["battle_orders"], second.OrdersEnded);
        Assert.Equal(["dig_in"], second.State.ActiveOrders);
    }

    [Fact]
    public void With_the_capstone_orders_stack()
    {
        var hero = Commander(capstone: true);
        var first = Cast(hero, Full(), "battle_orders");
        var second = Cast(hero, first.State.After(1), "dig_in");

        Assert.Empty(second.OrdersEnded);
        Assert.Equal(["battle_orders", "dig_in"], second.State.ActiveOrders);
    }

    [Fact]
    public void Supreme_commander_issues_every_order_the_hero_holds()
    {
        var hero = Commander(capstone: true);
        var r = Cast(hero, Full(), "supreme_commander");

        Assert.True(r.Ok);
        Assert.Equal(["battle_orders", "dig_in", "forward_march"], r.OrdersBegun);
        Assert.Equal(["battle_orders", "dig_in", "forward_march"], r.State.ActiveOrders);
        Assert.Empty(r.State.After(10).ActiveOrders);   // for 10 s
    }

    [Fact]
    public void An_order_ends_after_its_duration()
    {
        var hero = Learn(AtLevel("soldier", 2), "battle_orders");   // 8 s at rank 1
        var r = Cast(hero, Full(), "battle_orders");

        Assert.Equal(["battle_orders"], r.State.After(7.9).ActiveOrders);
        Assert.Empty(r.State.After(8).ActiveOrders);
    }

    [Fact]
    public void Chain_of_command_lengthens_widens_and_hastens_orders()
    {
        var hero = Learn(Commander(capstone: false), Times("chain_of_command", 4));   // rank 5: +50 % duration, +40 % radius, -20 % cooldown
        var stats = Skills.Stats(hero, None);
        var bo = Skills.Resolve(hero, stats, None, "battle_orders");
        var aura = bo.Effects.Single(e => e.Primitive == Primitive.Aura);

        Assert.Equal(8 * 1.5, aura["duration"], 9);
        Assert.Equal(450 * 1.4, aura["radius"], 9);
        Assert.Equal(30 * 0.8, bo.Cooldown, 9);
        var maggots = Skills.Resolve(Learn(hero, "maggots"), stats, None, "maggots");   // not an Order: untouched
        Assert.Equal((12.0, 4.0), (maggots.Cooldown, maggots.Effects[0]["duration"]));
    }
}
