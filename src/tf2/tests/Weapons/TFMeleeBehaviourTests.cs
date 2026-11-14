using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFMeleeBehaviour"/>: <c>CTFWeaponBaseMelee</c>'s swing, then its smack.</summary>
public sealed class TFMeleeBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Bat = "tf_weapon_bat";

    private static int NoCrit => TFWeaponWorld.MeleeSeed(1500, 10000);
    private static int Crit => TFWeaponWorld.MeleeSeed(0, 1500);

    [Fact]
    public void A_swing_shows_its_gesture_once_at_the_swing_and_not_again_at_the_smack()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);
        Assert.Equal(0, world.Arms.SwingsShown);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        Assert.Equal(1, world.Arms.SwingsShown);

        world.Run(w, vm, 0, now: 10.215f);
        Assert.Single(world.Effects.Swings);
        Assert.Equal(1, world.Arms.SwingsShown);
    }

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

    [Fact]
    public void A_hit_self_on_miss_item_asks_the_arm_at_the_smack()
    {
        // hit_self_on_miss == 1 (tf_weaponbase_melee.cpp:759): the held item's
        // one turns the miss tax on, and the smack carries the flag.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("hit_self_on_miss", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.True(Assert.Single(world.Effects.Swings).Trace.SelfOnMiss);
    }

    [Fact]
    public void A_hit_self_on_miss_item_notes_the_reach_at_the_swing_start()
    {
        // Stock's Swing notes the potential victims before the smack
        // (tf_weaponbase_melee.cpp:305-319), over the swing's own range.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("hit_self_on_miss", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(TFWeaponTypes.SwingRange, Assert.Single(world.Effects.SwingStarts));
    }

    [Fact]
    public void A_hit_self_on_miss_two_does_not_ask_the_arm()
    {
        // Stock's gate is equality with one (:759), not mere nonzero: a two
        // leaves the miss tax off.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("hit_self_on_miss", AttributeDescriptionFormat.Additive, 2f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.False(Assert.Single(world.Effects.Swings).Trace.SelfOnMiss);
    }

    [Fact]
    public void A_silent_crit_item_zeroes_the_crit_smacks_damage()
    {
        // crit_does_no_damage (tf_weaponbase_melee.cpp:989-1002): with the held
        // item's one, a crit smack carries no damage — stock zeroes it inside
        // GetMeleeDamage right after the mult_dmg scale (:987).
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("crit_does_no_damage", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: Crit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.Equal(new ShotDamage(0f, true), Assert.Single(world.Effects.Swings).Shot);
    }

    [Fact]
    public void A_silent_crit_item_leaves_a_plain_smack_at_the_rows_damage()
    {
        // The arm gates on the crit (:993): a plain swing of the same item
        // keeps the row's damage.
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("crit_does_no_damage", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.Equal(new ShotDamage(35f), Assert.Single(world.Effects.Swings).Shot);
    }

    [Fact]
    public void A_laughing_item_puts_the_laugh_on_the_trace()
    {
        // crit_forces_victim_to_laugh (tf_weaponbase_melee.cpp:884-885): the
        // held item's one makes the smack ask the forced-laugh arm; the crit
        // gate itself waits at the hit (:886).
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("crit_forces_victim_to_laugh", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.True(Assert.Single(world.Effects.Swings).Trace.ForcesLaugh);
    }

    [Fact]
    public void A_tickling_item_puts_the_tickle_on_the_trace()
    {
        // tickle_enemies_wielding_same_weapon (:903-905): the held item's one
        // asks the arm for every hit; no crit gate is stock's (:909).
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("tickle_enemies_wielding_same_weapon", AttributeDescriptionFormat.Additive, 1f);
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);
        world.Run(w, vm, 0, now: 10.25f);

        Assert.True(Assert.Single(world.Effects.Swings).Trace.TicklesSameWeapon);
    }

    [Fact]
    public void The_held_fists_restamp_the_silent_mark_each_command()
    {
        // self_mark_for_death (tf_weaponbase_melee.cpp:354-365): the held
        // fists' seconds ride onto the owner with every command, refreshed on
        // the ask.
        var world = new TFWeaponWorld();
        world.Owners.Of(TFWeaponWorld.Owner);
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith("self_mark_for_death", AttributeDescriptionFormat.Additive, 3f);
        var (w, vm) = world.Armed("tf_weapon_fists", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.True(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.MarkedfordeathSilent));
        Assert.Equal(3f, world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Duration(TFCond.MarkedfordeathSilent), 4);
    }

    [Fact]
    public void Fists_without_the_mark_attribute_leave_the_owner_unmarked()
    {
        // Stock starts the hook at zero (:356): no attribute, no mark.
        var world = new TFWeaponWorld();
        world.Owners.Of(TFWeaponWorld.Owner);
        var (w, vm) = world.Armed("tf_weapon_fists", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.False(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.MarkedfordeathSilent));
    }

    // The c_heavy_arms table (HeavyArmsSequences): the fists' cells and the
    // scout's plain melee pair, as the shipped mdl tags them.
    private const int FistsLeft = 4;
    private const int FistsRight = 5;
    private const int FistsCrit = 6;
    private const int BatPlain = 4;
    private const int BatCrit = 6;

    [Fact]
    public void The_left_fist_plays_the_left_hook()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_fists", clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.HitLeft] = FistsLeft;

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(FistsLeft, vm.Sequence);
    }

    [Fact]
    public void The_right_fist_is_the_same_punch_playing_the_right_hook()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_fists", clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.HitRight] = FistsRight;

        world.Run(w, vm, Attack2, now: 10f, seed: NoCrit);
        Assert.Equal(FistsRight, vm.Sequence);
        // Punch arms both clocks from the one delay (tf_weapon_fists.cpp:119).
        Assert.Equal(w.NextPrimaryAttack, w.NextSecondaryAttack, 4);

        world.Run(w, vm, 0, now: 10.25f);
        Assert.Single(world.Effects.Swings);
    }

    [Fact]
    public void The_fists_crit_swing_plays_the_fists_hard_swing()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_fists", clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.SwingHard] = FistsCrit;

        world.Run(w, vm, Attack, now: 10f, seed: Crit);

        Assert.Equal(FistsCrit, vm.Sequence);
    }

    [Fact]
    public void A_plain_melee_swing_plays_the_hitcenter_pose()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.HitCenter] = BatPlain;

        world.Run(w, vm, Attack, now: 10f, seed: NoCrit);

        Assert.Equal(BatPlain, vm.Sequence);
    }

    [Fact]
    public void A_melee_crit_plays_the_swinghard_pose()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);
        world.Arms.Sequences[WeaponAnim.SwingHard] = BatCrit;

        world.Run(w, vm, Attack, now: 10f, seed: Crit);

        Assert.Equal(BatCrit, vm.Sequence);
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

    // ---- the Gunslinger's combo (CTFRobotArm::PrimaryAttack) ----------------

    private const string Arm = "tf_weapon_robot_arm";

    /// <summary>A world over the extras, whose owner carries a predicted state the grant lands in.</summary>
    private static (TFWeaponWorld World, TFPlayerNetState Carrier) ArmWorld()
    {
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = carrier },
                                      files: WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()));
        return (world, carrier);
    }

    [Fact]
    public void The_gunslingers_third_swing_grants_the_melee_crit()
    {
        var (world, carrier) = ArmWorld();
        var (w, vm) = world.Armed(Arm, clip: -1, reserve: 0);
        w.BehaviourMode = 2 << TFMeleeBehaviour.ComboCountShift;   // two smacks landed
        w.BehaviourTimeB = 9.6f;                                  // the last, inside its second

        world.Behaviour(Arm).BeforePrimaryAttack(world.Attack(w, vm, 10f, seed: NoCrit));

        // SetNextMeleeCrit(MELEE_CRIT) (tf_weapon_wrench.cpp:479-482): the
        // grant the swing's own roll spends, the count itself untouched —
        // the smack's hit is still to fold it.
        Assert.Equal((int)TFMeleeCrit.Crit, carrier.NextMeleeCrit);
        Assert.Equal(2, TFMeleeBehaviour.ComboOf(w));
    }

    [Fact]
    public void A_combo_past_its_second_is_no_combo()
    {
        var (world, carrier) = ArmWorld();
        var (w, vm) = world.Armed(Arm, clip: -1, reserve: 0);
        w.BehaviourMode = 2 << TFMeleeBehaviour.ComboCountShift;
        w.BehaviourTimeB = 8f;                                    // two seconds gone

        world.Behaviour(Arm).BeforePrimaryAttack(world.Attack(w, vm, 10f, seed: NoCrit));

        // The timeout's reset (tf_weapon_wrench.cpp:474-477) ahead of the
        // grant's ask: the swing rolls plain, the count back to none.
        Assert.Equal((int)TFMeleeCrit.NoCrit, carrier.NextMeleeCrit);
        Assert.Equal(0, TFMeleeBehaviour.ComboOf(w));
    }

    [Fact]
    public void A_plain_melee_swing_never_touches_the_combo()
    {
        var (world, carrier) = ArmWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);
        w.BehaviourMode = 2 << TFMeleeBehaviour.ComboCountShift;
        w.BehaviourTimeB = 8f;

        world.Behaviour(Bat).BeforePrimaryAttack(world.Attack(w, vm, 10f, seed: NoCrit));

        Assert.Equal((int)TFMeleeCrit.NoCrit, carrier.NextMeleeCrit);
        Assert.Equal(2, TFMeleeBehaviour.ComboOf(w));
    }

    private const string Wrap = "tf_weapon_bat_giftwrap";

    // tf_weapon_bat.cpp:255-287 CTFBat_Wood::SecondaryAttack, the Wrap
    // Assassin's ball throw through it.
    [Fact]
    public void A_charged_alt_fire_throws_the_ball_and_empties_the_bar()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Wrap, clip: -1, reserve: 0);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack2, now: 10f, seed: NoCrit);

        var throw1 = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(Wrap, throw1.Weapon);
        Assert.Equal(new ShotDamage(35f), throw1.Shot);
        Assert.Equal(0f, w.ItemMeter, 4);
        // :276-277 both clocks, the primary gate the next press reads.
        Assert.Equal(10.25f, w.NextPrimaryAttack, 4);
        Assert.Equal(10.25f, w.NextSecondaryAttack, 4);
        // :272 the launch anim; the swing pose, ACT_VM_PRIMARYATTACK.
        Assert.Equal(SourceSharp.Shared.Anim.WeaponGestureTrigger.Secondary,
                     Assert.Single(world.Arms.Gestures));
    }

    [Fact]
    public void A_crit_roll_flags_the_ball_at_the_throw()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Wrap, clip: -1, reserve: 0);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack2, now: 10f, seed: Crit);

        // tf_weapon_bat.cpp:1089 CalcIsAttackCritical at CreateBall.
        Assert.Equal(new ShotDamage(35f, true), Assert.Single(world.Effects.Projectiles).Shot);
    }

    [Fact]
    public void An_empty_bar_refuses_the_throw()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Wrap, clip: -1, reserve: 0);
        w.ItemMeter = 0f;

        world.Run(w, vm, Attack2, now: 10f, seed: NoCrit);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(0f, w.ItemMeter, 4);
    }

    [Fact]
    public void A_primary_clock_in_the_future_refuses_the_throw()
    {
        var world = new TFWeaponWorld(extras: true);
        var (w, vm) = world.Armed(Wrap, clip: -1, reserve: 0);
        w.ItemMeter = 1f;
        w.NextPrimaryAttack = 20f;

        world.Run(w, vm, Attack2, now: 10f, seed: NoCrit);

        // :264-265 the bat throws no ball while its swing is still owed.
        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(1f, w.ItemMeter, 4);
    }

    // ---- the holster's clear of the granted crit (Holster, :140-161) -------

    /// <summary>A world whose owner carries the charge's grant, and no
    /// second one to hide behind: what the holster's clear must spend.</summary>
    private static (TFWeaponWorld World, TFPlayerNetState Carrier) GrantWorld()
    {
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.NextMeleeCrit = (int)TFMeleeCrit.Crit;
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = carrier });
        return (world, carrier);
    }

    [Fact]
    public void Holstering_the_melee_throws_the_granted_crit_away()
    {
        var (world, carrier) = GrantWorld();
        var (w, vm) = world.Armed(Bat, clip: -1, reserve: 0);

        // The switch away from the melee runs its Holster on the server
        // (Arsenal's Holster of the outgoing weapon); the grant the charge
        // made does not ride into the next weapon's hands (:151).
        world.Behaviour(Bat).Holster(world.Attack(w, vm, now: 10f));

        Assert.Equal((int)TFMeleeCrit.NoCrit, carrier.NextMeleeCrit);
    }

    [Fact]
    public void Holstering_a_plain_gun_leaves_the_grant_alone()
    {
        var (world, carrier) = GrantWorld();
        var (w, vm) = world.Armed("tf_weapon_pistol", clip: -1, reserve: 0);

        // The clear is the melee's own Holster: a switch between two non-melee
        // rows spends nothing of the grant waiting for the next swing.
        world.Behaviour("tf_weapon_pistol").Holster(world.Attack(w, vm, now: 10f));

        Assert.Equal((int)TFMeleeCrit.Crit, carrier.NextMeleeCrit);
    }
}
