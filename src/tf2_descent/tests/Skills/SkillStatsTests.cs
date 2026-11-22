using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Skills;
using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The skills' share of a hero's stats and of TF2's attributes (HE6): passive
/// upgrades, family-scoped upgrades (R17), the triggered passives on the
/// BlastJumping state, and the hit and kill triggers.
/// </summary>
public sealed class SkillStatsTests
{
    [Fact]
    public void A_passives_contribution_is_in_the_heros_stats_after_the_buy_and_gone_after_a_respec()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        double before = hero.Stats!.Total("damage_weapon_pct");

        rig.Learn("marksmanship");
        double bought = hero.Stats!.Total("damage_weapon_pct");
        Assert.True(hero.Apply(sheet => rig.Heroes.Rules.Heroes.Respec(sheet).Sheet).Ok);
        double respecced = hero.Stats!.Total("damage_weapon_pct");

        Assert.Equal(0, before, 6);
        Assert.Equal(5, bought, 6);   // Marksmanship rank 1 (RU14's retune: 3 was)
        Assert.Equal(0, respecced, 6);
    }

    [Fact]
    public void A_family_scoped_upgrade_applies_only_with_that_family_in_hand()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 18);
        rig.Learn("boot_camp");
        rig.Learn("pain_tolerance");
        rig.Learn("trench_fighter");

        rig.World.Weapon[hero.Player] = "tf_weapon_rocketlauncher";
        rig.Run(1);
        double launcher = hero.Stats!.Total("damage_weapon_pct");
        rig.World.Weapon[hero.Player] = "tf_weapon_shovel";
        rig.Run(1);
        double shovel = hero.Stats!.Total("damage_weapon_pct");
        rig.World.Weapon[hero.Player] = null;
        rig.Run(1);
        double none = hero.Stats!.Total("damage_weapon_pct");

        Assert.Equal(0, launcher, 6);
        Assert.True(shovel > 0);
        Assert.Equal(0, none, 6);
    }

    [Fact]
    public void A_class_word_on_an_item_class_still_names_its_family()
    {
        var rig = new SkillRig();

        Assert.Equal("shotgun", rig.Runtime.FamilyOf("tf_weapon_shotgun_soldier"));
        Assert.Equal("rocket_launcher", rig.Runtime.FamilyOf("tf_weapon_rocketlauncher"));
        Assert.Null(rig.Runtime.FamilyOf("tf_weapon_builder"));
        Assert.Null(rig.Runtime.FamilyOf(null));
    }

    [Fact]
    public void Death_From_Above_holds_its_damage_while_blast_jumping_and_lets_go_on_landing()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("jump_training");
        rig.Learn("death_from_above");
        rig.Run(1);
        double grounded = hero.Stats!.Total("damage_all_pct");

        rig.World.Jumping.Add(hero.Player);
        rig.Run(1);
        double jumping = hero.Stats!.Total("damage_all_pct");
        rig.World.Jumping.Remove(hero.Player);
        rig.Run(1);

        Assert.Equal(0, grounded, 6);
        Assert.Equal(20, jumping, 6);
        Assert.Equal(0, hero.Stats!.Total("damage_all_pct"), 6);
        Assert.Contains("death_from_above holds while blast jumping: damage_all_pct +20", rig.Heroes.Log.Text);
        Assert.Contains("landed from a blast jump", rig.Heroes.Log.Text);
    }

    [Fact]
    public void A_kill_while_blast_jumping_refunds_Death_From_Aboves_mana()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("jump_training");
        rig.Learn("death_from_above");
        hero.SetMana(5);

        rig.Runtime.Trigger(SkillRig.Index, "on_kill_airborne");

        Assert.Equal(15, hero.Mana, 6);
    }

    [Fact]
    public void Jump_Training_cuts_the_self_blast_and_raises_its_push_as_TF_attributes()
    {
        var rig = new SkillRig();
        rig.Join(level: 3);
        rig.Run(1);
        var without = rig.Attributes.Of(SkillRig.Index);

        rig.Learn("jump_training", 2);
        rig.Run(1);
        var with = rig.Attributes.Of(SkillRig.Index);
        var container = rig.Attributes.AttributesOf(SkillRig.PlayerOf())!;

        Assert.Equal(SkillTFAttributes.None, without);
        Assert.Equal(0.70f, with.SelfBlastDamage, 4); // -15 % a rank
        Assert.Equal(1.06f, with.SelfBlastPush, 4);   // +3 % a rank
        Assert.Contains(container.Attributes.ToArray(), a => a.Definition.AttributeClass == SkillAttributes.SelfBlastDamageClass);
    }

    [Fact]
    public void Death_From_Above_crits_airborne_melee_only_with_a_melee_weapon_in_hand()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("jump_training");
        rig.Learn("death_from_above");

        rig.World.Weapon[hero.Player] = "tf_weapon_rocketlauncher";
        rig.Run(1);
        bool launcher = rig.Attributes.Of(SkillRig.Index).CritWhileAirborne;
        rig.World.Weapon[hero.Player] = "tf_weapon_shovel";
        rig.Run(1);

        Assert.False(launcher);
        Assert.True(rig.Attributes.Of(SkillRig.Index).CritWhileAirborne);
    }

    [Fact]
    public void An_order_cast_ends_the_order_in_force_and_its_aura()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("battle_orders");
        rig.Learn("forward_march");
        rig.Slot(1, "battle_orders");
        rig.Slot(2, "forward_march");

        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 1).Kind);
        Assert.Contains(rig.Buffs.Of(SkillRig.Index), b => b.SourceId == "battle_orders");
        hero.RefillLifeAndMana();
        Assert.Equal(SkillUseKind.Cast, rig.Runtime.Press(SkillRig.Index, 2).Kind);

        Assert.DoesNotContain(rig.Buffs.Of(SkillRig.Index), b => b.SourceId == "battle_orders");
        Assert.Contains(rig.Buffs.Of(SkillRig.Index), b => b.SourceId == "forward_march");
    }

    //-------------------------------------------------------------------------
    // The hooks
    //-------------------------------------------------------------------------

    private sealed class NoSkillHit : ISkillHits
    {
        public string? Skill { get; set; }
        public string? Current() => Skill;
    }

    private static SkillTriggers Triggers(SkillRig rig, NoSkillHit hits)
    {
        rig.Heroes.Engine.ReturnsEachCall("Entities.IsPlayer", () => (rig.Heroes.Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex & 0xFFFF) < 33);
        return new SkillTriggers(rig.Runtime, rig.Casters, rig.Heroes.Heroes, hits, rig.Binders, rig.Heroes.Engine, rig.Heroes.Log);
    }

    [Fact]
    public void A_kill_by_a_blast_jumping_hero_raises_on_kill_airborne_through_the_death_hook()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("jump_training");
        rig.Learn("death_from_above");
        rig.World.Jumping.Add(hero.Player);
        rig.Run(1);
        hero.SetMana(5);
        var robot = new EntityHandle(0x0004_0000u | 77);

        ((IEntityDeathHook)Triggers(rig, new NoSkillHit())).OnEntityDied(new EntityDeath(robot, hero.Player, default));

        Assert.Equal(15, hero.Mana, 6);
    }

    [Fact]
    public void TF2s_BlastJumping_condition_is_the_heros_state_even_when_it_comes_and_goes_inside_one_tick()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("jump_training");
        rig.Learn("death_from_above");
        rig.Run(1);
        var hook = (ITFConditionHook)Triggers(rig, new NoSkillHit());

        hook.OnAdded(hero.Player, TFCond.Blastjumping);
        double jumping = hero.Stats!.Total("damage_all_pct");
        hook.OnRemoved(hero.Player, TFCond.Blastjumping);

        Assert.Equal(20, jumping, 6);
        Assert.Equal(0, hero.Stats!.Total("damage_all_pct"), 6);
        Assert.Contains("landed from a blast jump", rig.Heroes.Log.Text);
        hook.OnAdded(hero.Player, TFCond.Critboosted);
        Assert.False(rig.Casters.Of(SkillRig.Index)!.BlastJumping);
    }

    [Fact]
    public void A_skill_hit_carries_its_casts_on_hit_effects_to_the_victim()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("patriot_missile");
        rig.Learn("emp_charge");
        rig.Slot(1, "emp_charge");
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(2);
        rig.Runtime.Release(SkillRig.Index, 1);
        var robot = new EntityHandle(0x0004_0000u | 77);

        ((IEntityHurtHook)Triggers(rig, new NoSkillHit { Skill = "emp_charge" }))
            .OnEntityHurt(new EntityHurt(robot, hero.Player, 60, 99_000));

        Assert.Equal(["emp_stun", "slowed"], rig.Robots.Applied.Select(a => a.Condition));
        Assert.Equal(1.0, rig.Robots.Applied[0].Seconds, 6);
    }

    [Fact]
    public void A_weapon_hit_carries_no_skills_on_hit_effects()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("patriot_missile");
        rig.Learn("emp_charge");
        rig.Slot(1, "emp_charge");
        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(2);
        rig.Runtime.Release(SkillRig.Index, 1);

        ((IEntityHurtHook)Triggers(rig, new NoSkillHit())).OnEntityHurt(new EntityHurt(new EntityHandle(0x0004_0000u | 77), hero.Player, 60, 99_000));

        Assert.Empty(rig.Robots.Applied);
    }
}
