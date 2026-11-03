using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The behaviour shells whose effects are Stage 2's (launchers, flamethrower,
/// medigun, builder and PDAs): the ammunition, the timing and the crit run on
/// both sides, and the effect is asked of <see cref="ITFWeaponEffects"/>.
/// </summary>
public sealed class TFShellBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const float Tick = 0.015f;
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    [Fact]
    public void A_rocket_is_one_round_at_ninety_asked_of_the_server()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher", clip: 4, reserve: 20);

        world.Run(w, vm, Attack, 10f, NoCrit);

        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(("tf_weapon_rocketlauncher", new ShotDamage(90f), 0f), launch);
        Assert.Equal(3, w.Clip);
        Assert.Equal(10.8f, w.NextPrimaryAttack, 4);
        Assert.Equal(10.8f, w.TimeWeaponIdle, 4);
        Assert.Empty(world.Arms.Bullets);
        Assert.Empty(world.Arms.Swings);
    }

    [Fact]
    public void A_crit_rocket_carries_its_flag_and_the_untripled_damage()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher", clip: 4, reserve: 20);

        // A fresh bucket (300 + 90) is asked 90 * 3 * 1.889 = 510 and refuses;
        // a full one with ten checks behind it pays 270. The rocket carries 90
        // and the flag: the damage rules triple it (ApplyOnDamageModifyRules).
        var bucket = world.Parts.Buckets.For(TFWeaponWorld.WeaponEntity, w.WeaponTypeId, 1000f);
        for (int i = 0; i < 9; i++)
            bucket.CountCheck();

        world.Run(w, vm, Attack, 10f, TFWeaponWorld.GunSeed(0, 200));

        Assert.Equal(new ShotDamage(90f, true), Assert.Single(world.Effects.Projectiles).Shot);
    }

    [Fact]
    public void A_replayed_launch_spends_the_round_and_asks_nothing()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_grenadelauncher", clip: 4, reserve: 16);

        world.Run(w, vm, Attack, 10f, NoCrit, live: false);

        Assert.Empty(world.Effects.Projectiles);
        Assert.Equal(3, w.Clip);
        Assert.Equal(10.6f, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void An_empty_rocket_launcher_reloads_one_rocket_at_a_time()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_rocketlauncher", clip: 0, reserve: 20);

        world.Run(w, vm, 0, 10f);
        // TimeReloadStart 0.1 + TimeReload 0.83.
        world.Run(w, vm, 0, 10.92f);
        Assert.Equal(0, w.Clip);
        world.Run(w, vm, 0, 10.94f);
        Assert.Equal(1, w.Clip);
        Assert.Equal(19, w.Reserve);
    }

    [Fact]
    public void The_syringe_gun_fires_ten_damage_syringes_from_its_clip()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_syringegun_medic", clip: 40, reserve: 150);

        for (int k = 0; k < 20; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, NoCrit);

        Assert.True(world.Effects.Projectiles.Count >= 2);
        Assert.All(world.Effects.Projectiles, p => Assert.Equal(10f, p.Shot.Damage));
        Assert.Equal(40 - world.Effects.Projectiles.Count, w.Clip);
    }

    [Fact]
    public void A_stickybomb_charges_while_held_and_leaves_on_release_with_its_charge()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_pipebomblauncher", clip: 8, reserve: 24);

        int k = 0;
        for (; 10f + k * Tick < 11f; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, NoCrit);
        Assert.Empty(world.Effects.Projectiles);

        world.Run(w, vm, 0, 10f + k * Tick, NoCrit);

        var launch = Assert.Single(world.Effects.Projectiles);
        Assert.Equal(1f, launch.Charge, 1);
        Assert.Equal(120f, launch.Shot.Damage);
        Assert.Equal(7, w.Clip);
        Assert.Equal(0f, w.BehaviourMeter);
    }

    [Fact]
    public void A_stickybomb_held_past_four_seconds_leaves_by_itself()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_pipebomblauncher", clip: 8, reserve: 24);

        for (int k = 0; 10f + k * Tick < 14.1f; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, NoCrit);

        var launch = Assert.Single(world.Effects.Projectiles);
        // TF_PIPEBOMB_MAX_CHARGE_TIME (tf_weapon_pipebomblauncher.h:21).
        Assert.Equal(4f, launch.Charge);
    }

    [Fact]
    public void The_stickybomb_secondary_detonates()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_pipebomblauncher", clip: 8, reserve: 24);

        world.Run(w, vm, Attack2, 10f);

        Assert.Equal(1, world.Effects.Detonations);
        Assert.Empty(world.Effects.Projectiles);
    }

    [Fact]
    public void The_flamethrower_burns_fourteen_rounds_a_second_through_a_remainder()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);

        // A flame every 0.02 s; each is 14 * 0.02 = 0.28 of a round.
        for (int k = 0; k < 4; k++)
            world.Run(w, vm, Attack, 10f + k * 0.03f, NoCrit);

        Assert.Equal(4, world.Effects.FlameTicks.Count);
        Assert.All(world.Effects.FlameTicks, f => Assert.Equal(new ShotDamage(13f), f.Shot));
        // 1.12 spends one and keeps 0.12.
        Assert.Equal(199, w.Reserve);
        Assert.Equal(0.12f, w.BehaviourMeter, 4);
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void The_flamethrower_remainder_keeps_two_digits_as_stock_truncates_it()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);
        w.BehaviourMeter = 0.999f;

        world.Run(w, vm, Attack, 10f, NoCrit);

        // 0.999 + 0.28 = 1.279: one round, and 0.279 truncated to 0.27.
        Assert.Equal(199, w.Reserve);
        Assert.Equal(0.27f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_flamethrower_fires_every_fiftieth_of_a_second_not_the_scripts_delay()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);

        world.Run(w, vm, Attack, 10f, NoCrit);

        WeaponFixtures.Says(WeaponFixtures.Text("tf_weapon_flamethrower"), "TimeFireDelay", "0.04");
        Assert.Equal(10.02f, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void The_medigun_never_swings_and_tells_the_server_the_trigger()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);
        world.Run(w, vm, Attack, 10.015f);
        world.Run(w, vm, 0, 10.03f);

        Assert.Empty(world.Arms.Swings);
        Assert.Equal(new[] { true, true, false }, world.Effects.Heals);
    }

    [Fact]
    public void The_replayed_medigun_builds_the_charge_while_the_predicted_beam_is_up()
    {
        var data = new FakePredictedData();
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.HealTargetIndex = 4;
        data.Carrier = carrier;

        // The decoded clock is the server's — a command that reads it pays
        // nothing, and the machine's own takes over from there.
        w.BehaviourTimeA = 50f;
        world.Run(w, vm, Attack, 10f, live: false);
        Assert.Equal(0f, w.BehaviourMeter, 5);

        for (int k = 1; k <= 20; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, live: false);

        // Twenty commands of 0.015 s at one meter per forty seconds.
        Assert.Equal(20 * Tick / 40f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_replayed_medigun_builds_at_triple_rate_in_setup()
    {
        // The prediction reads the same setup gate the server's build does
        // (tf_weapon_medigun.cpp:1347-1350) — the round words off the mirrors.
        var data = new FakePredictedData();
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.HealTargetIndex = 4;
        data.Carrier = carrier;
        world.Round.InSetupWithRoundTimer = true;

        w.BehaviourTimeA = 50f;
        world.Run(w, vm, Attack, 10f, live: false);           // the decoded clock pays nothing

        for (int k = 1; k <= 20; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, live: false);

        Assert.Equal(3 * 20 * Tick / 40f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_replayed_medigun_drains_a_released_charge()
    {
        var data = new FakePredictedData();
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.ChargeReleasing = true;
        data.Carrier = carrier;
        w.BehaviourMeter = 1f;
        w.BehaviourTimeB = 9.5f;

        for (int k = 0; k < 8; k++)
            world.Run(w, vm, 0, 10f + k * 0.5f, live: false);

        // Four seconds of release at one meter per eight.
        Assert.Equal(0.5f, w.BehaviourMeter, 4);

        for (int k = 8; k < 16; k++)
            world.Run(w, vm, 0, 10f + k * 0.5f, live: false);

        Assert.Equal(0f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_replayed_medigun_touches_no_charge_without_a_beam_or_a_release()
    {
        var data = new FakePredictedData();
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.HealTargetIndex = -1;
        data.Carrier = carrier;

        w.BehaviourMeter = 0.75f;
        for (int k = 0; k < 10; k++)
            world.Run(w, vm, Attack, 10f + k * Tick, live: false);

        Assert.Equal(0.75f, w.BehaviourMeter, 4);
    }

    [Fact]
    public void The_replayed_medigun_stamps_the_release_clock_as_the_servers_tail_does()
    {
        // The server's arm closes every command the gun answers with
        // BehaviourTimeB = now (TFMedigunWeapon.Heal's tail, the clock the
        // release drain's dt and the compare run on). The replay of a held gun
        // with no beam and no release — the arm that touches no charge —
        // stamps the same, or the clock stands at the decode and the held
        // medigun mispredicts it on every arrival.
        var data = new FakePredictedData();
        var world = new TFWeaponWorld(data);
        var (w, vm) = world.Armed("tf_weapon_medigun", clip: -1, reserve: 0);
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        carrier.HealTargetIndex = -1;
        data.Carrier = carrier;

        world.Run(w, vm, 0, 10f, live: false);
        Assert.Equal(10f, w.BehaviourTimeB, 4);

        world.Run(w, vm, 0, 10.015f, live: false);
        Assert.Equal(10.015f, w.BehaviourTimeB, 4);
    }

    [Theory]
    [InlineData("tf_weapon_builder")]
    [InlineData("tf_weapon_sapper")]
    [InlineData("tf_weapon_pda_engineer_build")]
    [InlineData("tf_weapon_pda_engineer_destroy")]
    [InlineData("tf_weapon_pda_spy")]
    [InlineData("tf_weapon_invis")]
    public void A_tool_has_no_attack_of_its_own(string name)
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(name, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);

        Assert.True(world.Row(name).IsMelee, "a row without ammo would be the core's swing");
        Assert.Empty(world.Arms.Swings);
        Assert.Empty(world.Arms.Bullets);
    }

    /// <summary>
    /// <c>CTFWeaponPDA::PrimaryAttack</c> (<c>shared/tf/tf_weapon_pda.cpp:89-98</c>):
    /// the trigger asks for no ammo and no shot — it only sends the owner back to
    /// the item held before this (<c>SelectLastItem</c>), once per command while
    /// held, nothing after the release.
    /// </summary>
    [Theory]
    [InlineData("tf_weapon_pda_spy")]
    [InlineData("tf_weapon_pda_engineer_build")]
    [InlineData("tf_weapon_pda_engineer_destroy")]
    public void A_PDA_trigger_returns_the_owner_to_the_previous_item(string name)
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed(name, clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f);
        world.Run(w, vm, Attack, 10.015f);
        world.Run(w, vm, 0, 10.03f);

        Assert.Equal([name, name], world.Effects.SelectLasts);
        Assert.Empty(world.Arms.Swings);
        Assert.Empty(world.Arms.Bullets);
        Assert.Empty(world.Arms.Sounds);
    }

    [Fact]
    public void The_clients_replay_of_a_PDA_trigger_asks_nothing_of_the_arsenal()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_pda_spy", clip: -1, reserve: 0);

        world.Run(w, vm, Attack, 10f, live: false);

        Assert.Empty(world.Effects.SelectLasts);
    }

    [Fact]
    public void The_airblast_costs_twenty_and_holds_the_trigger_for_three_quarters()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Equal(["tf_weapon_flamethrower"], world.Effects.AirBlasts);
        Assert.Equal(180, w.Reserve);
        // FireAirBlast (tf_weapon_flamethrower.cpp:1044-1045).
        Assert.Equal(10.75f, w.NextSecondaryAttack, 4);
        Assert.Equal(11.0f, w.NextPrimaryAttack, 4);
        // m_bFiredSecondary (tf_weapon_flamethrower.cpp:556, 595): no tick too.
        Assert.Empty(world.Effects.FlameTicks);
    }

    [Fact]
    public void A_second_airblast_inside_the_delay_is_refused()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);

        world.Run(w, vm, Attack2, 10f, NoCrit);
        world.Run(w, vm, Attack2, 10.5f, NoCrit);

        Assert.Single(world.Effects.AirBlasts);
        Assert.Equal(180, w.Reserve);

        // The delay spent, it blasts again (SecondaryAttack, :1164-1173).
        world.Run(w, vm, Attack2, 10.75f, NoCrit);
        Assert.Equal(2, world.Effects.AirBlasts.Count);
        Assert.Equal(160, w.Reserve);
    }

    [Fact]
    public void An_airblast_short_of_its_twenty_is_refused()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 19);

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Empty(world.Effects.AirBlasts);
        Assert.Equal(19, w.Reserve);
        Assert.Equal(0f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void The_airblast_costs_what_tf_flamethrower_burstammo_says()
    {
        // FireAirBlast's iAmmoPerShot (tf_weapon_flamethrower.cpp:1194).
        var world = new TFWeaponWorld();
        world.SetBurstammo("7");
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Equal(["tf_weapon_flamethrower"], world.Effects.AirBlasts);
        Assert.Equal(193, w.Reserve);
    }

    [Fact]
    public void An_airblast_short_of_what_tf_flamethrower_burstammo_says_is_refused()
    {
        var world = new TFWeaponWorld();
        world.SetBurstammo("25");
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 24);

        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Empty(world.Effects.AirBlasts);
        Assert.Equal(24, w.Reserve);
        Assert.Equal(0f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void A_pyro_with_their_eyes_under_water_throws_no_flame_but_still_spends()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);
        world.Owners.Underwater = true;

        // "Don't attack if we're underwater" (tf_weapon_flamethrower.cpp:827-908):
        // the tick is not thrown, but the ammo and the refires still are (:910-931).
        for (int k = 0; k < 4; k++)
            world.Run(w, vm, Attack, 10f + k * 0.03f, NoCrit);

        Assert.Empty(world.Effects.FlameTicks);
        Assert.Equal(199, w.Reserve);
        Assert.Equal(10.11f, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void An_airblast_with_the_eyes_under_water_is_refused_before_its_ammo()
    {
        var world = new TFWeaponWorld();
        var (w, vm) = world.Armed("tf_weapon_flamethrower", clip: -1, reserve: 200);
        world.Owners.Underwater = true;

        // SecondaryAttack's water return (tf_weapon_flamethrower.cpp:1175-1176)
        // sits before the ammunition count.
        world.Run(w, vm, Attack2, 10f, NoCrit);

        Assert.Empty(world.Effects.AirBlasts);
        Assert.Equal(200, w.Reserve);
        Assert.Equal(0f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void Every_milestone_row_resolves_a_tf_behaviour()
    {
        var world = new TFWeaponWorld();

        foreach (string name in WeaponFixtures.Names())
            Assert.IsAssignableFrom<TFWeaponBehaviour>(world.Behaviour(name));
        Assert.DoesNotContain(world.Log.Warnings(), l => l.Contains("behaviour", StringComparison.OrdinalIgnoreCase));
    }
}
