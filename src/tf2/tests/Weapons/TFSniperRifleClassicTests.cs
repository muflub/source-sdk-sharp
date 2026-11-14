using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFSniperRifleClassicBehaviour"/>: the Classic's charge on the
/// press and shot on the release (CTFSniperRifleClassic::ItemPostFrame,
/// tf_weapon_sniperrifle.cpp:1870-1965).
/// </summary>
public sealed class TFSniperRifleClassicTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const string Classic = "tf_weapon_sniperrifle_classic";
    private const float Tick = 0.015f;

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Sniper()
    {
        var world = new TFWeaponWorld(extras: true);
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Sniper;
        world.Owners.Of(TFWeaponWorld.Owner);
        var (w, vm) = world.Armed(Classic, clip: -1, reserve: 25);
        return (world, w, vm);
    }

    /// <summary>Holds the trigger from <paramref name="from"/>, one tick a frame.</summary>
    private static float Hold(TFWeaponWorld world, WeaponNetState w, ViewModelNetState vm, int buttons, float from, int ticks)
    {
        for (int k = 1; k <= ticks; k++)
            world.Run(w, vm, buttons, from + k * Tick, TFWeaponWorld.GunSeed(200, 10000));
        return from + ticks * Tick;
    }

    [Fact]
    public void The_held_trigger_fires_nothing()
    {
        var (world, w, vm) = Sniper();

        Hold(world, w, vm, Attack, 10f, 20);

        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void A_press_of_a_twelfth_fires_the_fifty_floor_on_the_release()
    {
        // The charge over 0.12 s is 6 (ItemPostFrame's accrual at the 50 a
        // second); GetProjectileDamage's floor is the
        // TF_WEAPON_SNIPERRIFLE_DAMAGE_MIN 50 it never falls under
        // (tf_weapon_sniperrifle.cpp:870-872) — the 3 the gun routing fired.
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 8);
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));

        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal(50f, shot.Shot.Damage, 0);
        Assert.False(shot.Shot.Critical);
    }

    [Fact]
    public void The_release_spends_one_round_and_arms_the_refire()
    {
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 8);
        int before = w.Reserve;
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(before - 1, w.Reserve);
        Assert.Equal(t + Tick + 1.5f, w.NextPrimaryAttack, 2);
    }

    [Fact]
    public void The_release_ends_the_charge_and_the_presss_aiming()
    {
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 8);
        Assert.True(TFSniperRifleBehaviour.Charge(w, t) > 0f);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(0f, w.BehaviourMeter);
        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
    }

    [Fact]
    public void A_longer_press_hits_harder_than_the_floor()
    {
        // Two seconds: the charge 99.75, over the floor and under the cap.
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 133);
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));

        var shot = Assert.Single(world.Arms.Bullets);
        Assert.Equal(99.75f, shot.Shot.Damage, 2);
    }

    [Fact]
    public void An_airborne_release_clicks_and_fires_nothing()
    {
        var (world, w, vm) = Sniper();
        world.Owners.Airborne = true;

        float t = Hold(world, w, vm, Attack, 10f, 8);
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));

        Assert.Empty(world.Arms.Bullets);
        Assert.Equal(25, w.Reserve);
        Assert.Contains(TFSniperRifleClassicBehaviour.DenySound, world.Arms.Sounds);
    }

    [Fact]
    public void A_charge_keeps_building_in_the_air()
    {
        // The accrual has no ground gate (:1922-1929); only the release asks.
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 8);
        float begun = w.BehaviourMeter;
        world.Owners.Airborne = true;
        t = Hold(world, w, vm, Attack, t, 60);

        Assert.Equal(begun, w.BehaviourMeter);
    }

    [Fact]
    public void A_zoomed_shot_stays_up()
    {
        // Nothing reads the unzoom stock's Fire arms (:286 and :313 are the
        // base's HandleZooms, which the Classic overrides).
        var (world, w, vm) = Sniper();

        Hold(world, w, vm, (int)InputButtons.Attack2, 10f, 2);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));

        float t = Hold(world, w, vm, 0, 10.03f, 30);
        t = Hold(world, w, vm, Attack, t, 8);
        world.Run(w, vm, 0, t + Tick, TFWeaponWorld.GunSeed(200, 10000));
        Assert.Single(world.Arms.Bullets);

        // Every tick: the base's timers would kick out at 0.5 s and rezoom by 1.8.
        for (int k = 1; k <= 120; k++)
        {
            world.Run(w, vm, 0, t + Tick + k * Tick, TFWeaponWorld.GunSeed(200, 10000));
            Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        }
    }

    [Fact]
    public void A_taunt_ends_the_charge_with_no_shot()
    {
        var (world, w, vm) = Sniper();

        float t = Hold(world, w, vm, Attack, 10f, 8);
        Assert.True(w.BehaviourMeter > 0f);
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Taunting);
        Hold(world, w, vm, 0, t, 2);

        Assert.Equal(0f, w.BehaviourMeter);
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void The_classic_rows_run_the_release_fire_behaviour()
    {
        var world = new TFWeaponWorld(extras: true);

        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.SniperRifleClassic), world.Row(Classic).Behaviour);
    }
}
