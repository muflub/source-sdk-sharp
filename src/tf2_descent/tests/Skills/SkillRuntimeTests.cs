using SourceSharp.Abi;

using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Skills;
using TF2.Server.Projectiles;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The skills runtime (plan_descent_logic HE6) over a fake world: keys,
/// mana, refusals and their notices, cooldowns in server ticks, buying,
/// slots, charges and toggles.
/// </summary>
public sealed class SkillRuntimeTests
{
    private static SkillRig Patriot(int level = 2)
    {
        var rig = new SkillRig();
        rig.Join(level: level);
        rig.Learn("patriot_missile");
        rig.Slot(1, "patriot_missile");
        return rig;
    }

    //-------------------------------------------------------------------------
    // Mana and refusals
    //-------------------------------------------------------------------------

    [Fact]
    public void Casting_with_no_mana_is_refused_says_so_once_and_spends_nothing()
    {
        var rig = Patriot();
        var hero = rig.Hero();
        hero.SetMana(1);
        Assert.True(hero.Mana < rig.Skills.Find("patriot_missile")!.ManaCost.At(1));

        var first = rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);
        var second = rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal((SkillUseKind.Refused, CastRefusal.NoMana), (first.Kind, first.Refusal));
        Assert.Equal((SkillUseKind.Refused, CastRefusal.NoMana), (second.Kind, second.Refusal));
        Assert.Equal(1, hero.Mana, 6);
        Assert.Empty(rig.World.Spawned);
        Assert.Equal([(SkillRig.Index, "Patriot Missile: not enough mana")], rig.Told.Notices);
    }

    [Fact]
    public void A_cast_is_an_act_and_a_refused_one_is_not()
    {
        var rig = Patriot();
        var hero = rig.Hero();
        int before = hero.Acts;

        var cast = rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);
        hero.SetMana(0);
        var refused = rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal((SkillUseKind.Cast, SkillUseKind.Refused), (cast.Kind, refused.Kind));
        Assert.Equal(before + 1, hero.Acts);
    }

    [Fact]
    public void A_refusal_is_told_again_after_a_second()
    {
        var rig = Patriot();
        rig.Hero().SetMana(0);

        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(SkillRuntime.NoticeSeconds);
        rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal(2, rig.Told.Notices.Count);
    }

    [Fact]
    public void A_cast_spends_the_rules_mana_cost_through_the_hero()
    {
        var rig = Patriot();
        var hero = rig.Hero();
        double before = hero.Mana;

        var use = rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal(SkillUseKind.Cast, use.Kind);
        Assert.Equal(4, use.ManaSpent, 6);
        Assert.Equal(before - 4, hero.Mana, 6);
    }

    [Fact]
    public void A_slot_with_no_skill_does_nothing()
    {
        var rig = Patriot();
        double before = rig.Hero().Mana;

        var press = rig.Runtime.Press(SkillRig.Index, 2);
        var release = rig.Runtime.Release(SkillRig.Index, 2);

        Assert.Equal(SkillUseKind.Nothing, press.Kind);
        Assert.Equal(SkillUseKind.Nothing, release.Kind);
        Assert.Empty(rig.World.Spawned);
        Assert.Empty(rig.Told.Notices);
        Assert.Equal(before, rig.Hero().Mana, 6);
    }

    [Fact]
    public void A_dead_or_observing_hero_casts_nothing_and_loses_a_held_charge()
    {
        var rig = Emp();
        var hero = rig.Hero();
        rig.Learn("battle_orders");
        rig.Slot(1, "battle_orders");
        rig.Runtime.Press(SkillRig.Index, 3);
        double before = hero.Mana;

        rig.World.Dead.Add(hero.Player);
        var press = rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);

        Assert.Equal(SkillUseKind.Nothing, press.Kind);
        Assert.Null(rig.Casters.Of(SkillRig.Index)!.Charging);
        Assert.Empty(rig.Buffs.Of(SkillRig.Index));
        Assert.True(hero.Mana >= before);
    }

    //-------------------------------------------------------------------------
    // Cooldowns
    //-------------------------------------------------------------------------

    [Fact]
    public void A_cooldown_holds_for_exactly_its_ticks()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        rig.Learn("battle_orders");
        rig.Slot(1, "battle_orders");
        int ticks = rig.Binders.Ticks(rig.Skills.Find("battle_orders")!.Cooldown.At(1));
        Assert.Equal(2000, ticks); // 30 s at 0.015 s a tick

        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        rig.Hero().SetMana(1000);
        rig.Run(ticks - 1);
        var early = rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);
        var onTime = rig.Runtime.Press(SkillRig.Index, 1);

        Assert.Equal(CastRefusal.OnCooldown, early.Refusal);
        Assert.Equal(SkillUseKind.Cast, onTime.Kind);
    }

    [Fact]
    public void The_owners_bar_carries_the_slots_and_the_tick_a_cooldown_ends()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        rig.Learn("battle_orders");
        rig.Slot(2, "battle_orders");
        int cast = rig.World.Tick;

        rig.Runtime.Press(SkillRig.Index, 2);

        var bar = rig.Told.Bars[^1].Bar;
        Assert.Equal(["", "battle_orders", "", "", "", ""], bar.Skills);
        Assert.Equal([0, cast + 2000, 0, 0, 0, 0], bar.ReadyTicks);
    }

    //-------------------------------------------------------------------------
    // Buying and slots
    //-------------------------------------------------------------------------

    [Fact]
    public void Buying_follows_the_rules_refusals_and_the_sheets_audit_stays_true()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);

        var belowLevel = rig.Runtime.Buy(SkillRig.Index, "emp_charge");
        var bought = rig.Runtime.Buy(SkillRig.Index, "patriot_missile");
        var noPoints = rig.Runtime.Buy(SkillRig.Index, "marksmanship");

        Assert.Equal(BuyRefusal.BelowLevel, belowLevel!.Refusal);
        Assert.True(bought!.Ok);
        Assert.Equal(BuyRefusal.NoPoints, noPoints!.Refusal);
        Assert.Equal(1, SkillRules.RealRank(hero.Sheet!, "patriot_missile"));
        Assert.Equal(0, SkillRules.RealRank(hero.Sheet!, "marksmanship"));
        Assert.Equal(0, hero.Sheet!.SkillPoints);
        Assert.True(rig.Heroes.Rules.Heroes.Audit(hero.Sheet!).Ok);
        Assert.Equal(2, rig.Told.Notices.Count);
        Assert.StartsWith("Cannot learn EMP Charge", rig.Told.Notices[0].Text);
    }

    [Fact]
    public void Only_a_learned_cast_skill_can_be_slotted()
    {
        var rig = new SkillRig();
        rig.Join(level: 3);
        rig.Learn("marksmanship");

        Assert.NotNull(rig.Runtime.Assign(SkillRig.Index, 1, "patriot_missile"));
        Assert.NotNull(rig.Runtime.Assign(SkillRig.Index, 1, "marksmanship"));
        Assert.NotNull(rig.Runtime.Assign(SkillRig.Index, 7, "patriot_missile"));
        rig.Learn("patriot_missile");
        Assert.Null(rig.Runtime.Assign(SkillRig.Index, 1, "patriot_missile"));
        Assert.Equal("patriot_missile", rig.Slots.In(SkillRig.Index, 1));
    }

    //-------------------------------------------------------------------------
    // Charges
    //-------------------------------------------------------------------------

    private static SkillRig Emp()
    {
        var rig = new SkillRig();
        rig.Join(level: 12);
        rig.Learn("patriot_missile");
        rig.Learn("emp_charge");
        rig.Slot(3, "emp_charge");
        return rig;
    }

    [Fact]
    public void Press_without_release_on_a_charge_skill_fires_nothing()
    {
        var rig = Emp();
        double before = rig.Hero().Mana;

        var press = rig.Runtime.Press(SkillRig.Index, 3);
        rig.RunSeconds(5);

        Assert.Equal(SkillUseKind.Charging, press.Kind);
        Assert.Empty(rig.World.Spawned);
        Assert.True(rig.Hero().Mana >= before);
    }

    [Fact]
    public void Release_fires_the_stage_reached()
    {
        var rig = Emp();
        double damage = rig.RulesDamage("emp_charge");

        rig.Runtime.Press(SkillRig.Index, 3);
        rig.RunSeconds(2);
        var release = rig.Runtime.Release(SkillRig.Index, 3);

        Assert.Equal((SkillUseKind.Cast, 1), (release.Kind, release.Stages));
        Assert.Equal(15, release.ManaSpent, 6);
        var shot = Assert.Single(rig.World.Spawned);
        Assert.Equal(TFProjectileEntityNames.EnergyBall, shot.ClassName);
        Assert.Equal((float)damage, shot.Damage, 3);
    }

    [Fact]
    public void Release_before_the_first_stage_fires_nothing_and_spends_nothing()
    {
        var rig = Emp();
        double before = rig.Hero().Mana;

        rig.Runtime.Press(SkillRig.Index, 3);
        rig.RunSeconds(1.5);
        var release = rig.Runtime.Release(SkillRig.Index, 3);
        var again = rig.Runtime.Press(SkillRig.Index, 3);

        Assert.Equal(SkillUseKind.Early, release.Kind);
        Assert.Empty(rig.World.Spawned);
        Assert.True(rig.Hero().Mana >= before);
        Assert.Equal(SkillUseKind.Charging, again.Kind); // no cooldown began
    }

    [Fact]
    public void A_charge_is_refused_at_the_press_when_the_cast_would_be()
    {
        var rig = Emp();
        rig.Hero().SetMana(0);

        var press = rig.Runtime.Press(SkillRig.Index, 3);

        Assert.Equal(CastRefusal.NoMana, press.Refusal);
        Assert.Null(rig.Casters.Of(SkillRig.Index)!.Charging);
    }

    [Fact]
    public void A_charge_held_past_its_misfire_spends_the_cast_and_fires_nothing()
    {
        var rig = new SkillRig();
        rig.Join(level: 6);
        rig.Learn("patriot_missile");
        rig.Learn("salvo");
        rig.Slot(1, "salvo");
        var charge = rig.Skills.Resolve(rig.Hero().Sheet!, rig.Hero().Stats!, SkillBonuses.None, "salvo")
            .Effects.Single(e => e.Primitive == Primitive.Charge);
        double before = rig.Hero().Mana;

        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(charge["time"] + charge["misfireAfter"] + 0.1);
        var release = rig.Runtime.Release(SkillRig.Index, 1);

        Assert.Empty(rig.World.Spawned);
        Assert.Equal(SkillUseKind.Nothing, release.Kind); // the misfire already ended the charge
        Assert.True(rig.Hero().Mana < before);
        Assert.Contains(rig.Told.Notices, n => n.Text == "Salvo misfired");
    }

    [Fact]
    public void A_volley_releases_one_rocket_a_stage_across_the_release_time()
    {
        var rig = new SkillRig();
        rig.Join(level: 6);
        rig.Learn("patriot_missile");
        rig.Learn("salvo");
        rig.Slot(1, "salvo");
        var skill = rig.Skills.Resolve(rig.Hero().Sheet!, rig.Hero().Stats!, SkillBonuses.None, "salvo");
        var charge = skill.Effects.Single(e => e.Primitive == Primitive.Charge);

        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(charge["time"]);
        var release = rig.Runtime.Release(SkillRig.Index, 1);
        int fired = rig.World.Spawned.Count;
        rig.RunSeconds(charge["releaseTime"]);

        Assert.Equal((int)charge["stages"], release.Stages);
        Assert.Equal(1, fired);
        Assert.Equal((int)charge["stages"], rig.World.Spawned.Count);
    }

    //-------------------------------------------------------------------------
    // Toggles
    //-------------------------------------------------------------------------

    [Fact]
    public void A_toggle_drains_and_switches_off_at_zero()
    {
        var rig = new SkillRig();
        rig.Join(level: 18);
        rig.Learn("jump_training", 3);
        rig.Learn("parachute");
        rig.Slot(1, "parachute");
        var hero = rig.Hero();
        double drain = rig.Skills.Resolve(hero.Sheet!, hero.Stats!, SkillBonuses.None, "parachute")
            .Effects.Single(e => e.Primitive == Primitive.Toggle)["drain"];

        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        Assert.Contains("parachute", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        hero.SetMana(drain); // one second's worth
        rig.RunSeconds(0.5);
        Assert.Contains("parachute", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        rig.RunSeconds(1);

        Assert.DoesNotContain("parachute", rig.Casters.Of(SkillRig.Index)!.State.Toggles);
        Assert.Contains(rig.Told.Notices, n => n.Text == "Parachute is off: out of mana");
    }

    [Fact]
    public void A_toggle_on_holds_its_stats_and_a_second_press_switches_it_off()
    {
        var rig = new SkillRig();
        rig.Join(level: 18);
        rig.Learn("jump_training", 3);
        rig.Learn("parachute");
        rig.Slot(1, "parachute");

        rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);
        double on = rig.Hero().Stats!.Total("damage_all_pct");
        var off = rig.Runtime.Press(SkillRig.Index, 1);
        rig.Run(1);

        Assert.True(on > 0);
        Assert.Equal(SkillUseKind.ToggledOff, off.Kind);
        Assert.Equal(0, rig.Hero().Stats!.Total("damage_all_pct"), 6);
    }
}
