using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFMinigunBehaviour"/>: <c>CTFMinigun</c>'s spin-up state machine.</summary>
public sealed class TFMinigunBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Minigun = "tf_weapon_minigun";
    private const float Tick = 0.015f;

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Heavy(int reserve = 200)
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.HeavyWeapons;
        world.Owners.Of(TFWeaponWorld.Owner);
        var (w, vm) = world.Armed(Minigun, clip: -1, reserve: reserve);
        return (world, w, vm);
    }

    /// <summary>Runs ticks from <paramref name="from"/> until before <paramref name="until"/>, on a crit-free seed.</summary>
    private static float Hold(TFWeaponWorld world, WeaponNetState w, ViewModelNetState vm, int buttons, float from, float until)
    {
        int seed = TFWeaponWorld.GunSeed(200, 10000);
        int k = 0;
        float t = from;
        for (; t < until; t = from + ++k * Tick)
            world.Run(w, vm, buttons, t, seed);
        return t;
    }

    [Fact]
    public void A_pull_winds_up_three_quarters_of_a_second_and_sets_aiming()
    {
        var (world, w, vm) = Heavy();

        world.Run(w, vm, Attack, now: 10f);

        Assert.Equal(TFMinigunBehaviour.State.StartFiring, TFMinigunBehaviour.StateOf(w));
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
        Assert.Equal(10.75f, w.NextPrimaryAttack, 4);
        Assert.Empty(world.Arms.Bullets);
    }

    /// <summary>The item whose named hook the <paramref name="scale"/> sets, percentage multiplied or additive added.</summary>
    private static AttributeContainer Scaling(string hook, float scale, AttributeDescriptionFormat format = AttributeDescriptionFormat.Percentage)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(7100, "attr 7100", hook, format,
                                                                AttributeEffectType.Positive, false, false,
                                                                AttributeValueType.Default, MergedKeys.Block("x")), scale));
        return item;
    }

    [Fact]
    public void A_half_spinup_attribute_halves_the_wind_up()
    {
        var (world, w, vm) = Heavy();
        world.Owners.ByName.TryAdd(TFWeaponWorld.Owner, new() { [Minigun] = Scaling("mult_minigun_spinup_time", 0.5f) });

        world.Run(w, vm, Attack, now: 10f);

        // SharedAttack's idle case (tf_weapon_minigun.cpp:259-260, :272-274):
        // 0.75 x 0.5 on all three timers.
        Assert.Equal(TFMinigunBehaviour.State.StartFiring, TFMinigunBehaviour.StateOf(w));
        Assert.Equal(10.375f, w.NextPrimaryAttack, 4);
        Assert.Equal(10.375f, w.NextSecondaryAttack, 4);
        Assert.Equal(10.375f, w.TimeWeaponIdle, 4);
    }

    [Fact]
    public void The_first_shot_follows_the_spin_up_and_a_tenth_more()
    {
        var (world, w, vm) = Heavy();

        Hold(world, w, vm, Attack, 10f, 10.84f);
        Assert.Empty(world.Arms.Bullets);
        Assert.Equal(TFMinigunBehaviour.State.Firing, TFMinigunBehaviour.StateOf(w));

        Hold(world, w, vm, Attack, 10.84f, 10.9f);
        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal(4, shot.Pellets);
        Assert.Equal(199, w.Reserve);
    }

    [Fact]
    public void The_damage_ramps_from_half_to_full_over_the_first_second_spun()
    {
        // GetProjectileDamage: RemapValClamped(spin, 0.2, 1, 0.5, 1) under the penalty period.
        Assert.Equal(0.5f, TFMinigunBehaviour.DamageScale(0.1f));
        Assert.Equal(0.75f, TFMinigunBehaviour.DamageScale(0.6f), 4);
        Assert.Equal(1f, TFMinigunBehaviour.DamageScale(1.5f));
        Assert.Equal(1.5f, TFMinigunBehaviour.SpreadScale(0f));
        Assert.Equal(1f, TFMinigunBehaviour.SpreadScale(1f));

        var (world, w, vm) = Heavy();
        Hold(world, w, vm, Attack, 10f, 13f);

        Assert.Equal(new ShotDamage(4.5f), world.Arms.Bullets[0].Shot);
        Assert.Equal(new ShotDamage(9f), world.Arms.Bullets[^1].Shot);
    }

    [Fact]
    public void Held_fire_shoots_a_fire_delay_from_each_shot_on_the_tick()
    {
        var (world, w, vm) = Heavy();

        // 0.1 s at 15 ms ticks is every seventh tick (0.105 s): curtime + delay, no catch-up.
        Hold(world, w, vm, Attack, 10f, 10.85f + 70 * Tick);

        Assert.Equal(10, world.Arms.Bullets.Count);
    }

    /// <summary>
    /// <c>CTFMinigun::SendWeaponAnim</c>'s firing gate
    /// (<c>tf_weapon_minigun.cpp:961-970</c>): the fire anim loops, and the
    /// send that finds it already playing is answered without a restart
    /// ("If we're already playing the fire anim, let it continue"). A machine
    /// that re-sent it per shot restarted the anim at fire rate, and the
    /// restart frame is where the merged gun hangs out of view.
    /// </summary>
    [Fact]
    public void The_fire_anim_is_sent_once_for_a_held_trigger_and_loops_after()
    {
        var (world, w, vm) = Heavy();
        world.Arms.Sequences[WeaponAnim.Primary] = 21;   // c_heavy_arms' m_fire

        float t = Hold(world, w, vm, Attack, 10f, 10.9f);
        Assert.NotEmpty(world.Arms.Bullets);
        Assert.Equal(21, vm.Sequence);
        int firstShotParity = vm.AnimationParity;

        Hold(world, w, vm, Attack, t, 11.5f);             // the burst continues

        Assert.True(world.Arms.Bullets.Count >= 4);       // the burst reaches the subject
        Assert.Equal(21, vm.Sequence);
        Assert.Equal(firstShotParity, vm.AnimationParity);
    }

    /// <summary>
    /// Stock's <c>WindUp</c> (:757) sends the held spin-up pose
    /// (<c>ACT_MP_ATTACK_STAND_PREFIRE</c>) before the state change, and the
    /// fire loop (<c>ACT_VM_PRIMARYATTACK</c> through <c>Fire</c>) takes it at
    /// the first shot; the idle pose in between is why a gun that never gets
    /// the prefire send hangs low out of view for the whole burst.
    /// </summary>
    [Fact]
    public void The_wind_up_lifts_the_gun_to_the_prefire_pose_and_the_fire_pose_takes_it()
    {
        var (world, w, vm) = Heavy();
        world.Arms.Sequences[WeaponAnim.Prefire] = 22;   // c_heavy_arms' spool-up pose
        world.Arms.Sequences[WeaponAnim.Primary] = 21;   // its looping fire pose

        world.Run(w, vm, Attack, now: 10f);
        Assert.Equal(22, vm.Sequence);

        Hold(world, w, vm, Attack, 10f, 10.9f);           // past the spin-up: the first shot
        Assert.NotEmpty(world.Arms.Bullets);
        Assert.Equal(21, vm.Sequence);
    }

    /// <summary>
    /// Stock's Idle arm keeps the pose's clock matched to the hook's spin-up
    /// (:262-270): <c>SetPlaybackRate(TF_MINIGUN_SPINUP_TIME / flSpinUpTime)</c>,
    /// so a half-second wind-up plays the pose at double speed and still ends
    /// when the gun does.
    /// </summary>
    [Fact]
    public void A_half_spinup_runs_the_prefire_pose_at_the_inverse_rate()
    {
        var (world, w, vm) = Heavy();
        world.Arms.Sequences[WeaponAnim.Prefire] = 22;
        world.Owners.ByName.TryAdd(TFWeaponWorld.Owner, new() { [Minigun] = Scaling("mult_minigun_spinup_time", 0.5f) });

        world.Run(w, vm, Attack, now: 10f);

        Assert.Equal(2f, vm.PlaybackRate, 4);             // 0.75 / 0.375
    }

    /// <summary>
    /// Stock's <c>WindDown</c> (:855) sends the settle pose
    /// (<c>ACT_MP_ATTACK_STAND_POSTFIRE</c>) before the state change; the fire
    /// loop's pose is what a released trigger leaves on the viewmodel without it.
    /// </summary>
    [Fact]
    public void Letting_go_sends_the_postfire_settle_pose()
    {
        var (world, w, vm) = Heavy();
        world.Arms.Sequences[WeaponAnim.Primary] = 21;
        world.Arms.Sequences[WeaponAnim.Postfire] = 23;   // c_heavy_arms' spool-down pose

        float t = Hold(world, w, vm, Attack, 10f, 11.5f);
        Assert.Equal(21, vm.Sequence);
        float lastShot = w.NextPrimaryAttack - 0.1f;

        Hold(world, w, vm, 0, t, lastShot + 0.19f);       // still holding the fire pose
        Assert.Equal(21, vm.Sequence);
        Hold(world, w, vm, 0, lastShot + 0.19f, lastShot + 0.25f);   // past the idle: wind down
        Assert.Equal(TFMinigunBehaviour.State.Idle, TFMinigunBehaviour.StateOf(w));
        Assert.Equal(23, vm.Sequence);
    }

    /// <summary>
    /// Stock's spin arms: the empty click sends the spin pose on the way out
    /// (<c>HandleFireOnEmpty</c> :984, the <c>DryFire</c> tail :410) and the
    /// spinning hold keeps re-sending it (<c>Spinning</c> :433). Same
    /// <c>ACT_VM_SECONDARYATTACK</c> ask, same looping answer.
    /// </summary>
    [Fact]
    public void A_spun_gun_holds_the_spin_pose_and_the_dry_click_starts_it()
    {
        var (world, w, vm) = Heavy();
        world.Arms.Sequences[WeaponAnim.Secondary] = 3;   // the alt-fire hold pose

        Hold(world, w, vm, Attack2, 10f, 11f);
        Assert.Equal(TFMinigunBehaviour.State.Spinning, TFMinigunBehaviour.StateOf(w));
        Assert.Equal(3, vm.Sequence);                     // Spinning's :433 send
    }

    [Fact]
    public void The_secondary_spins_without_firing_and_holds_aiming()
    {
        var (world, w, vm) = Heavy();

        // The spin-up ends straight into spinning, never through firing.
        Hold(world, w, vm, Attack2, 10f, 10.76f);
        Assert.Equal(TFMinigunBehaviour.State.Spinning, TFMinigunBehaviour.StateOf(w));

        Hold(world, w, vm, Attack2, 10.76f, 12f);

        Assert.Equal(TFMinigunBehaviour.State.Spinning, TFMinigunBehaviour.StateOf(w));
        Assert.Empty(world.Arms.Bullets);
        Assert.Equal(200, w.Reserve);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
    }

    [Fact]
    public void Letting_go_of_a_spun_gun_winds_it_down_and_aiming_ends()
    {
        var (world, w, vm) = Heavy();
        float t = Hold(world, w, vm, Attack2, 10f, 11f);

        // Spinning sets no idle of its own: the first command without a trigger winds down.
        world.Run(w, vm, 0, t);

        Assert.Equal(TFMinigunBehaviour.State.Idle, TFMinigunBehaviour.StateOf(w));
        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
        Assert.Equal(0f, w.BehaviourMeter);
        Assert.Equal(t + 2f, w.TimeWeaponIdle, 4);
    }

    [Fact]
    public void Letting_go_of_a_firing_gun_winds_it_down_a_fifth_of_a_second_after_the_last_shot()
    {
        var (world, w, vm) = Heavy();
        float t = Hold(world, w, vm, Attack, 10f, 11.5f);
        float lastShot = w.NextPrimaryAttack - 0.1f;

        Hold(world, w, vm, 0, t, lastShot + 0.19f);
        Assert.Equal(TFMinigunBehaviour.State.Firing, TFMinigunBehaviour.StateOf(w));
        Hold(world, w, vm, 0, lastShot + 0.19f, lastShot + 0.25f);
        Assert.Equal(TFMinigunBehaviour.State.Idle, TFMinigunBehaviour.StateOf(w));
    }

    [Fact]
    public void An_empty_pool_dry_fires()
    {
        var (world, w, vm) = Heavy(reserve: 2);

        Hold(world, w, vm, Attack, 10f, 12f);

        Assert.Equal(2, world.Arms.Bullets.Count);
        Assert.Equal(0, w.Reserve);
        Assert.Equal(TFMinigunBehaviour.State.DryFire, TFMinigunBehaviour.StateOf(w));
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
    }

    [Fact]
    public void The_minigun_never_clicks_empty()
    {
        var (world, _, _) = Heavy();

        Assert.Equal("", world.Row(Minigun).EmptySound);
        WeaponFixtures.Says(WeaponFixtures.Text(Minigun), "empty", "Weapon_Minigun.ClipEmpty");
    }

    [Fact]
    public void An_attack_projectiles_item_sweeps_on_every_fired_shot()
    {
        var (world, w, vm) = Heavy();
        world.Owners.ByName.TryAdd(TFWeaponWorld.Owner, new() { [Minigun] = Scaling("attack_projectiles", 2f, AttributeDescriptionFormat.Additive) });

        Hold(world, w, vm, Attack, 10f, 11.6f);

        Assert.NotEmpty(world.Effects.Sweeps);
        Assert.Equal(world.Arms.Bullets.Count, world.Effects.Sweeps.Count);
        Assert.All(world.Effects.Sweeps, s => Assert.Equal(2, s.Projectiles));
        Assert.All(world.Effects.Sweeps, s => Assert.False(s.Critical));
    }

    [Fact]
    public void Without_the_item_no_sweep_is_asked()
    {
        var (world, w, vm) = Heavy();

        Hold(world, w, vm, Attack, 10f, 11.6f);

        Assert.NotEmpty(world.Arms.Bullets);
        Assert.Empty(world.Effects.Sweeps);
    }

    [Fact]
    public void A_ring_of_fire_item_rings_on_every_spun_command()
    {
        var (world, w, vm) = Heavy();
        world.Owners.ByName.TryAdd(TFWeaponWorld.Owner, new() { [Minigun] = Scaling("ring_of_fire_while_aiming", 12f, AttributeDescriptionFormat.Additive) });

        Hold(world, w, vm, Attack, 10f, 11.9f);

        // Spun from the first shot on (tf_weapon_minigun.cpp:440-448): the
        // item's damage on every command past the wind-up.
        Assert.NotEmpty(world.Effects.Rings);
        Assert.All(world.Effects.Rings, r => Assert.Equal(12, r));
    }

    [Fact]
    public void Without_the_item_no_ring_is_asked()
    {
        var (world, w, vm) = Heavy();

        Hold(world, w, vm, Attack, 10f, 11.9f);

        Assert.NotEmpty(world.Arms.Bullets);
        Assert.Empty(world.Effects.Rings);
    }

    [Fact]
    public void Every_return_to_a_spun_state_rings_the_clock()
    {
        var (world, w, vm) = Heavy();

        // The wind-up leaves an unspun state (SetWeaponState :475-480).
        world.Run(w, vm, Attack, now: 10f);
        int onWindUp = world.Effects.RearmedCount;
        Assert.Equal(1, onWindUp);

        // So does the hand-over to the spun machine.
        Hold(world, w, vm, Attack, 10f, 10.9f);
        Assert.NotEqual(onWindUp, world.Effects.RearmedCount);

        // The wind-down out of the spun states does not; the next wind-up does.
        int spun = world.Effects.RearmedCount;
        float t = Hold(world, w, vm, 0, 10.9f, 12.5f);
        Assert.Equal(spun, world.Effects.RearmedCount);

        world.Run(w, vm, Attack, now: t + 1f);
        Assert.Equal(spun + 1, world.Effects.RearmedCount);
    }

    /// <summary>Winds up at 10 and spins from <paramref name="spinAt"/> on, draining <paramref name="perSecond"/> a second while spun.</summary>
    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Draining(float perSecond, float spinAt)
    {
        var (world, w, vm) = Heavy();
        world.Owners.ByName.TryAdd(TFWeaponWorld.Owner, new() { [Minigun] = Scaling("uses_ammo_while_aiming", perSecond, AttributeDescriptionFormat.Additive) });
        world.Run(w, vm, Attack, now: 10f);
        world.Run(w, vm, Attack2, now: spinAt);
        return (world, w, vm);
    }

    [Fact]
    public void A_spun_gun_drains_the_item_s_rounds_a_second_and_carries_the_remainder()
    {
        // The transition into the spin restarts the drain's clock
        // (SetWeaponState, tf_weapon_minigun.cpp:475-480), so the first spun
        // second pays nothing; the accrual cuts whole rounds past one
        // (:456-464) and carries the fraction.
        var (world, w, vm) = Draining(2f, spinAt: 11f);

        Assert.Equal(200, w.Reserve);                       // 2 x (11 - 11): the clock restarted on the transition
        world.Run(w, vm, Attack2, now: 11.5f);              // 2 x 0.5 accrued, not a whole round yet
        Assert.Equal(200, w.Reserve);
        world.Run(w, vm, Attack2, now: 12f);                // 1 + 1: the second round cut
        Assert.Equal(198, w.Reserve);
        world.Run(w, vm, Attack2, now: 12.5f);              // 1 accrued, carried
        Assert.Equal(198, w.Reserve);
        Assert.Equal(1f, w.BehaviourMeterB, 4);
    }

    [Fact]
    public void The_drains_clock_restarts_when_the_gun_leaves_the_spun_states()
    {
        // A wind-up at 10 spun only at 13: without the transition's reset the
        // first drain would take the three unspun seconds too (2 x 3 = 6
        // rounds); with it the spun time alone pays.
        var (world, w, vm) = Draining(2f, spinAt: 13f);

        Assert.Equal(200, w.Reserve);
        world.Run(w, vm, Attack2, now: 13.5f);
        world.Run(w, vm, Attack2, now: 14f);
        Assert.Equal(198, w.Reserve);

        // Released, wound down and wound up again, the clock restarts once more.
        world.Run(w, vm, 0, now: 14.5f);
        Assert.Equal(TFMinigunBehaviour.State.Idle, TFMinigunBehaviour.StateOf(w));
        world.Run(w, vm, Attack, now: 20f);
        world.Run(w, vm, Attack2, now: 21f);
        Assert.Equal(198, w.Reserve);
        world.Run(w, vm, Attack2, now: 21.5f);
        world.Run(w, vm, Attack2, now: 22f);
        Assert.Equal(196, w.Reserve);
    }

    [Fact]
    public void A_replay_spins_up_and_fires_alike_without_the_arms()
    {
        var (live, lw, lvm) = Heavy();
        var (replay, rw, rvm) = Heavy();
        int seed = TFWeaponWorld.GunSeed(200, 10000);

        for (int k = 0; k < 80; k++)
        {
            live.Run(lw, lvm, Attack, 10f + k * Tick, seed);
            replay.Run(rw, rvm, Attack, 10f + k * Tick, seed, live: false);
        }

        Assert.Empty(replay.Arms.Bullets);
        Assert.NotEmpty(live.Arms.Bullets);
        Assert.Equal(lw.Reserve, rw.Reserve);
        Assert.Equal(lw.BehaviourMode, rw.BehaviourMode);
        Assert.Equal(lw.NextPrimaryAttack, rw.NextPrimaryAttack);
    }
}
