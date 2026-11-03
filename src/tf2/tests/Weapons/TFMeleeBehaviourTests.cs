using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFMeleeBehaviour"/>: <c>CTFWeaponBaseMelee</c>'s swing, then its smack.</summary>
public sealed class TFMeleeBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const string Bat = "tf_weapon_bat";

    private static int NoCrit => TFWeaponWorld.MeleeSeed(1500, 10000);
    private static int Crit => TFWeaponWorld.MeleeSeed(0, 1500);

    [Fact]
    public void A_swing_hits_a_smack_delay_later_at_the_scripts_damage()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        Assert.Empty(world.Effects.Swings);
        Assert.Equal(10.5f, w.NextPrimaryAttack, 4);
        // The idle: the next attack plus TimeIdleEmpty (0: stock reads the misspelt key).
        Assert.Equal(10.5f, w.TimeWeaponIdle, 4);

        // GetSmackTime: curtime + 0.2, and the smack waits until strictly after it.
        world.Run(w, vm, 0, now: 10.2f);
        Assert.Empty(world.Effects.Swings);
        world.Run(w, vm, 0, now: 10.215f);

        Assert.Equal((Bat, new ShotDamage(35f), new SwingTrace(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false)),
                     Assert.Single(world.Effects.Swings));
        Assert.Equal(0, w.BehaviourMode & TFMeleeBehaviour.ModeSwinging);
    }

    [Fact]
    public void A_crit_swing_whooshes_its_burst_and_hits_flagged_at_its_own_damage()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: Crit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.Contains("Weapon_Bat.MissCrit", world.Arms.Sounds);
        Assert.Equal(new ShotDamage(35f, true), Assert.Single(world.Effects.Swings).Shot);
    }

    [Fact]
    public void A_plain_swing_whooshes_its_miss()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(new[] { "Weapon_Bat.Miss" }, world.Arms.Sounds);
    }

    [Fact]
    public void The_knife_smacks_a_tenth_of_a_second_after_its_swing()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_knife", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.1f);
        Assert.Empty(world.Effects.Swings);
        world.Run(w, vm, 0, now: 10.115f);

        Assert.Equal(new ShotDamage(40f), Assert.Single(world.Effects.Swings).Shot);
    }

    [Fact]
    public void A_replay_spends_the_smack_without_hitting()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit, live: false);
        world.Run(w, vm, 0, now: 10.25f, live: false);

        Assert.Empty(world.Effects.Swings);
        Assert.Empty(world.Arms.Sounds);
        Assert.Equal(0, w.BehaviourMode & TFMeleeBehaviour.ModeSwinging);
    }

    [Fact]
    public void Holding_the_trigger_swings_once_a_fire_delay()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        for (float t = 10f; t < 11.4f; t += 0.015f)
            world.Run(w, vm, Attack, now: t, seed: NoCrit);

        // Swings at 10.0, 10.5 and 11.0, smacks 0.2 s after each.
        Assert.Equal(3, world.Effects.Swings.Count);
    }

    [Fact]
    public void Every_melee_row_runs_the_melee_behaviour()
    {
        var world = new TFWeaponWorld();
        foreach (string name in new[] { Bat, "tf_weapon_bottle", "tf_weapon_fireaxe", "tf_weapon_club", "tf_weapon_knife",
                                        "tf_weapon_fists", "tf_weapon_shovel", "tf_weapon_wrench", "tf_weapon_bonesaw" })
            Assert.IsType<TFMeleeBehaviour>(world.Behaviour(name));
    }

    [Fact]
    public void A_smack_without_an_attribute_swings_the_rows_reach_and_the_eighteen_unit_hull()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.Equal(new SwingTrace(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false),
                     Assert.Single(world.Effects.Swings).Trace);
    }

    [Fact]
    public void A_melee_range_multiplier_scales_the_smacks_reach_and_leaves_the_hull()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("melee_range_multiplier", AttributeDescriptionFormat.Percentage, 1.5f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        var trace = Assert.Single(world.Effects.Swings).Trace;
        Assert.Equal(1.5f * TFWeaponTypes.SwingRange, trace.Range, 4);
        Assert.Equal(TFMeleeBehaviour.SwingHull, trace.Hull, 4);
    }

    [Fact]
    public void A_melee_bounds_multiplier_widens_the_smacks_hull_and_leaves_the_reach()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("melee_bounds_multiplier", AttributeDescriptionFormat.Percentage, 2f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        var trace = Assert.Single(world.Effects.Swings).Trace;
        Assert.Equal(2f * TFMeleeBehaviour.SwingHull, trace.Hull, 4);
        Assert.Equal(TFWeaponTypes.SwingRange, trace.Range, 4);
    }

    [Fact]
    public void A_crit_from_behind_item_asks_the_arm_at_the_smack()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("crit_from_behind", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.True(Assert.Single(world.Effects.Swings).Trace.CritFromBehind);
    }

    /// <summary>An item holding one named attribute, as the schema's wearables do.</summary>
    private static IAttributeProvider ItemWith(string hook, AttributeDescriptionFormat format, float value)
    {
        int defindex = 1;
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(defindex, "attr " + defindex, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }
}
