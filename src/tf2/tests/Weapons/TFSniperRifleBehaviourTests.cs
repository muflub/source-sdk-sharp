using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary><see cref="TFSniperRifleBehaviour"/>: <c>CTFSniperRifle</c>'s scope, charge and shot.</summary>
public sealed class TFSniperRifleBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Rifle = "tf_weapon_sniperrifle";
    private const float Tick = 0.015f;

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Sniper(int reserve = 25)
    {
        var world = new TFWeaponWorld();
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Sniper;
        world.Owners.Of(TFWeaponWorld.Owner);
        var (w, vm) = world.Armed(Rifle, clip: -1, reserve: reserve);
        return (world, w, vm);
    }

    private static float Hold(TFWeaponWorld world, WeaponNetState w, ViewModelNetState vm, int buttons, float from, float until, int? seed = null)
    {
        int s = seed ?? TFWeaponWorld.GunSeed(200, 10000);
        int k = 0;
        float t = from;
        for (; t < until; t = from + ++k * Tick)
            world.Run(w, vm, buttons, t, s);
        return t;
    }

    [Theory]
    [InlineData(0f, 50f)]
    [InlineData(75f, 75f)]
    [InlineData(150f, 150f)]
    public void A_charge_of_none_half_and_full_does_stocks_50_75_and_150(float charge, float damage)
    {
        // GetProjectileDamage: Max(m_flChargedDamage, TF_WEAPON_SNIPERRIFLE_DAMAGE_MIN 50),
        // the charge capped at TF_WEAPON_SNIPERRIFLE_DAMAGE_MAX 150 (tf_weapon_sniperrifle.cpp:36-37, 870-872).
        Assert.Equal(damage, TFSniperRifleBehaviour.DamageAt(charge));
    }

    [Fact]
    public void The_charge_grows_fifty_a_second_to_the_cap()
    {
        var w = new WeaponNetState();
        w.BindLocal();
        w.BehaviourMeter = 10f;

        Assert.Equal(0f, TFSniperRifleBehaviour.Charge(w, 10f));
        Assert.Equal(75f, TFSniperRifleBehaviour.Charge(w, 11.5f), 3);
        Assert.Equal(150f, TFSniperRifleBehaviour.Charge(w, 13f), 3);
        Assert.Equal(150f, TFSniperRifleBehaviour.Charge(w, 20f));
    }

    [Fact]
    public void The_secondary_zooms_in_with_aiming_and_tells_the_server()
    {
        var (world, w, vm) = Sniper();

        world.Run(w, vm, Attack2, 10f);

        var conditions = world.Owners.Of(TFWeaponWorld.Owner);
        Assert.True(conditions.Has(TFCond.Zoomed));
        Assert.True(conditions.Has(TFCond.Aiming));
        Assert.Equal(new[] { true }, world.Effects.Zooms);
        Assert.Equal(10.3f, w.NextSecondaryAttack, 4);
    }

    [Fact]
    public void A_second_press_after_the_zoom_time_zooms_out_and_drops_the_charge()
    {
        var (world, w, vm) = Sniper();

        world.Run(w, vm, Attack2, 10f);
        float t = Hold(world, w, vm, 0, 10.015f, 11f);
        Assert.True(TFSniperRifleBehaviour.Charge(w, t) > 0f);

        world.Run(w, vm, Attack2, t);

        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.Equal(0f, TFSniperRifleBehaviour.Charge(w, t));
        Assert.Equal(new[] { true, false }, world.Effects.Zooms);
    }

    [Fact]
    public void A_release_before_the_zoom_time_leaves_the_scope_up()
    {
        // HandleZooms gates the next toggle by m_flNextSecondaryAttack
        // (tf_weapon_sniperrifle.cpp:310): the zoom time of a press is 0.3,
        // and a release before that gate leaves the single toggle standing —
        // the scope clears with the next held frame past the gate, not with
        // the release itself.
        var (world, w, vm) = Sniper();

        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, 0, 10.1f);

        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
    }

    [Fact]
    public void Zoomed_shots_at_none_half_and_full_charge_hit_for_50_75_and_150()
    {
        foreach (var (wait, damage) in new[] { (0f, 50f), (1.5f, 75f), (3f, 150f) })
        {
            var (world, w, vm) = Sniper();
            world.Run(w, vm, Attack2, 10f);
            // Charging begins once the zoom time has passed (10.3).
            float t = Hold(world, w, vm, 0, 10.015f, 10.3f + wait);
            world.Run(w, vm, Attack, t, TFWeaponWorld.GunSeed(200, 10000));

            var shot = Assert.Single(world.Arms.Bullets);
            Assert.Equal(damage, shot.Shot.Damage, 0);
            Assert.False(shot.Shot.Critical);
        }
    }

    [Fact]
    public void A_zoomed_shot_unzooms_half_a_second_later_and_rezooms_after_more()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        float t = Hold(world, w, vm, 0, 10.015f, 11f);
        world.Run(w, vm, Attack, t);
        float shotAt = t;

        Hold(world, w, vm, 0, t + Tick, shotAt + 0.49f);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Hold(world, w, vm, 0, shotAt + 0.49f, shotAt + 0.55f);
        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Hold(world, w, vm, 0, shotAt + 0.55f, shotAt + 1.5f);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.Equal(new[] { true, false, true }, world.Effects.Zooms);
    }

    [Fact]
    public void An_unzoomed_shot_does_the_minimum_and_waits_the_fire_delay()
    {
        var (world, w, vm) = Sniper();

        world.Run(w, vm, Attack, 10f, TFWeaponWorld.GunSeed(200, 10000));

        Assert.Equal(new ShotDamage(50f), Assert.Single(world.Arms.Bullets).Shot);
        Assert.Equal(11.5f, w.NextPrimaryAttack, 4);
        Assert.Equal(24, w.Reserve);
    }

    [Fact]
    public void An_empty_rifle_does_not_zoom()
    {
        var (world, w, vm) = Sniper(reserve: 0);

        world.Run(w, vm, Attack2, 10f);

        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.Empty(world.Effects.Zooms);
    }

    [Fact]
    public void The_rifle_never_random_crits_but_a_boost_crits()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack, 10f, TFWeaponWorld.GunSeed(0, 200));
        Assert.False(Assert.Single(world.Arms.Bullets).Shot.Critical);

        var (boosted, bw, bvm) = Sniper();
        boosted.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.CritboostedUserBuff);
        boosted.Run(bw, bvm, Attack, 10f, TFWeaponWorld.GunSeed(200, 10000));
        Assert.Equal(new ShotDamage(50f, true), Assert.Single(boosted.Arms.Bullets).Shot);
    }

    [Fact]
    public void Zoomed_the_rifle_holds_aiming_and_unzoomed_it_does_not()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        world.Run(w, vm, 0, 10.1f);
        Assert.True(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
    }

    // ---- the decay and the jump (ItemPostFrame :360-402) --------------------

    [Fact]
    public void Aiming_stopped_but_still_zoomed_the_charge_runs_back_down_at_75_a_second()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        for (int k = 21; k <= 86; k++)      // 10.315..11.29: a second of charge
            world.Run(w, vm, 0, 10f + k * Tick);
        Assert.Equal(48.75f, TFSniperRifleBehaviour.Charge(w, 10f + 86 * Tick), 1);

        world.Owners.Of(TFWeaponWorld.Owner).Remove(TFCond.Aiming);
        for (int k = 87; k <= 120; k++)     // 11.305..11.8: half a second of decay
            world.Run(w, vm, 0, 10f + k * Tick);

        // 49.5 frozen when the decay's clock began, 75 a second off it since.
        Assert.Equal(49.5f - 75f * 33 * Tick, TFSniperRifleBehaviour.Charge(w, 10f + 120 * Tick), 1);
    }

    [Fact]
    public void The_decay_runs_the_charge_down_to_none()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        for (int k = 21; k <= 86; k++)
            world.Run(w, vm, 0, 10f + k * Tick);

        world.Owners.Of(TFWeaponWorld.Owner).Remove(TFCond.Aiming);
        for (int k = 87; k <= 154; k++)     // a second and a bit of decay
            world.Run(w, vm, 0, 10f + k * Tick);

        Assert.Equal(0f, TFSniperRifleBehaviour.Charge(w, 10f + 154 * Tick));
    }

    [Fact]
    public void Aiming_again_the_charge_resumes_from_what_the_decay_left()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        for (int k = 21; k <= 86; k++)
            world.Run(w, vm, 0, 10f + k * Tick);

        world.Owners.Of(TFWeaponWorld.Owner).Remove(TFCond.Aiming);
        for (int k = 87; k <= 120; k++)
            world.Run(w, vm, 0, 10f + k * Tick);

        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Aiming);
        for (int k = 121; k <= 140; k++)    // 11.815..12.1
            world.Run(w, vm, 0, 10f + k * Tick);

        // 12.375 grown at 50 a second for the 0.3 s back on the aim — not the
        // 89 a restart from the zoom would give.
        Assert.Equal(12.375f + 50f * 20 * Tick, TFSniperRifleBehaviour.Charge(w, 10f + 140 * Tick), 1);
    }

    [Fact]
    public void Leaving_the_ground_unzooms_and_drops_the_charge()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        float t = Hold(world, w, vm, 0, 10.015f, 11.3f);
        Assert.True(TFSniperRifleBehaviour.Charge(w, t) > 40f);

        world.Owners.Airborne = true;
        Hold(world, w, vm, 0, t + Tick, t + 0.1f);

        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Aiming));
        Assert.Equal(0f, TFSniperRifleBehaviour.Charge(w, t + 0.1f));
        Assert.Equal(new[] { true, false }, world.Effects.Zooms);
    }

    [Fact]
    public void Leaving_the_ground_cancels_the_shot_rezoom()
    {
        var (world, w, vm) = Sniper();
        world.Run(w, vm, Attack2, 10f);
        float t = Hold(world, w, vm, 0, 10.015f, 11f);
        world.Run(w, vm, Attack, t, TFWeaponWorld.GunSeed(200, 10000));

        world.Owners.Airborne = true;
        Hold(world, w, vm, 0, t + Tick, t + 2f);

        // Zoomed out at once, and the unzoom-then-rezoom never comes.
        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.Equal(new[] { true, false }, world.Effects.Zooms);
    }

    [Fact]
    public void A_jump_press_does_not_zoom_in()
    {
        var (world, w, vm) = Sniper();
        world.Owners.Airborne = true;

        world.Run(w, vm, Attack2, 10f);

        Assert.False(world.Owners.Of(TFWeaponWorld.Owner).Has(TFCond.Zoomed));
        Assert.Empty(world.Effects.Zooms);
    }

    // ---- the charge's rate (ItemPostFrame :377-384, ApplyChargeSpeedModifications :643-645) ----

    private static AttributeContainer PercentageItem(int defIndex, string hook, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(defIndex, "attr " + defIndex, hook, AttributeDescriptionFormat.Percentage,
                                                                AttributeEffectType.Positive, false, false, AttributeValueType.Default,
                                                                MergedKeys.Block("x")), value));
        return item;
    }

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) ItemSniper(AttributeContainer? item)
    {
        var world = new TFWeaponWorld();
        if (item is not null)
            world.Owners.Items[TFWeaponWorld.Owner] = item;
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Sniper;
        world.Owners.Of(TFWeaponWorld.Owner);
        var (w, vm) = world.Armed(Rifle, clip: -1, reserve: 25);
        return (world, w, vm);
    }

    /// <summary>The zoomed-shot skeleton: zoom, hold the charge to <paramref name="until"/>, fire.</summary>
    private static float ChargedShot(TFWeaponWorld world, WeaponNetState w, ViewModelNetState vm, float until)
    {
        world.Run(w, vm, Attack2, 10f);
        float t = Hold(world, w, vm, 0, 10.015f, until);
        world.Run(w, vm, Attack, t, TFWeaponWorld.GunSeed(200, 10000));
        return Assert.Single(world.Arms.Bullets).Shot.Damage;
    }

    [Fact]
    public void An_items_charge_rate_doubles_the_shots_charge()
    {
        // ApplyChargeSpeedModifications (tf_weapon_sniperrifle.cpp:643-645) puts
        // the item's mult_sniper_charge_per_sec on the accrual's rate
        // (ItemPostFrame :377-378), and the shot's damage is that charge.
        var (bare, bw, bvm) = ItemSniper(null);
        float stock = ChargedShot(bare, bw, bvm, 11.7f);

        var (two, tw, tvm) = ItemSniper(PercentageItem(9120, "mult_sniper_charge_per_sec", 2f));
        float doubled = ChargedShot(two, tw, tvm, 11.7f);

        Assert.Equal(2 * stock, doubled, 1);
    }

    [Fact]
    public void The_charge_rate_is_clamped_to_twice_the_stock()
    {
        // :381-382 clamps the rate to 0..2 * TF_WEAPON_SNIPERRIFLE_CHARGE_PER_SEC
        // after the hooks, so a four times item charges at the doubled rate.
        var (bare, bw, bvm) = ItemSniper(null);
        float stock = ChargedShot(bare, bw, bvm, 11.7f);

        var (four, fw, fvm) = ItemSniper(PercentageItem(9121, "mult_sniper_charge_per_sec", 4f));
        float clamped = ChargedShot(four, fw, fvm, 11.7f);

        Assert.Equal(2 * stock, clamped, 1);
    }

    [Fact]
    public void The_decay_runs_on_the_modified_rates_ledger()
    {
        // The charge is the time it began, so the decay's begin-move divides by
        // the rate the accrual used: charged at twice stock, the Aiming-less
        // zoom leaves the doubled charge less the rate-free 75 a second
        // (:392-400), and the ledger reads back at the doubled rate.
        var (charged, cw, cvm) = ItemSniper(PercentageItem(9122, "mult_sniper_charge_per_sec", 2f));
        charged.Run(cw, cvm, Attack2, 10f);
        for (int k = 21; k <= 86; k++)          // the stock-rate fact's second of charge
            charged.Run(cw, cvm, 0, 10f + k * Tick);
        charged.Run(cw, cvm, Attack, 10f + 87 * Tick, TFWeaponWorld.GunSeed(200, 10000));
        float full = Assert.Single(charged.Arms.Bullets).Shot.Damage;

        var (decayed, dw, dvm) = ItemSniper(PercentageItem(9123, "mult_sniper_charge_per_sec", 2f));
        decayed.Run(dw, dvm, Attack2, 10f);
        for (int k = 21; k <= 86; k++)
            decayed.Run(dw, dvm, 0, 10f + k * Tick);
        decayed.Owners.Of(TFWeaponWorld.Owner).Remove(TFCond.Aiming);
        for (int k = 87; k <= 120; k++)         // the stock-rate fact's half second of decay
            decayed.Run(dw, dvm, 0, 10f + k * Tick);

        Assert.Equal(TFSniperRifleBehaviour.ChargePerSec * 2 * 0.99f, full, 1);
        Assert.Equal(full - UnchargePerSecond * 33 * Tick,
                     TFSniperRifleBehaviour.Charge(dw, 10f + 120 * Tick, 2 * TFSniperRifleBehaviour.ChargePerSec), 1);
    }

    // ---- the fully charged shot's bonus (GetProjectileDamage :896-899) ----

    [Fact]
    public void A_fully_charged_shots_damage_takes_the_items_full_charge_bonus()
    {
        // IsFullyCharged asks the bonus on the damage the chain has scaled
        // (tf_weapon_sniperrifle.cpp:896-899): a 1.5 item on a full charge.
        var (bare, bw, bvm) = ItemSniper(null);
        float stock = ChargedShot(bare, bw, bvm, 13.5f);

        var (bonus, cw, cvm) = ItemSniper(PercentageItem(9124, "sniper_full_charge_damage_bonus", 1.5f));
        float boosted = ChargedShot(bonus, cw, cvm, 13.5f);

        Assert.Equal(1.5 * stock, boosted, 1);
    }

    [Fact]
    public void A_part_charge_is_not_the_full_charge_bonus()
    {
        var (bare, bw, bvm) = ItemSniper(null);
        float stock = ChargedShot(bare, bw, bvm, 11.8f);

        var (bonus, cw, cvm) = ItemSniper(PercentageItem(9125, "sniper_full_charge_damage_bonus", 1.5f));
        float boosted = ChargedShot(bonus, cw, cvm, 11.8f);

        Assert.Equal(stock, boosted, 1);
    }

    [Fact]
    public void A_full_charge_without_the_attribute_is_stocks_damage()
    {
        var (bare, bw, bvm) = ItemSniper(null);

        Assert.Equal(TFSniperRifleBehaviour.DamageMax, ChargedShot(bare, bw, bvm, 13.5f), 1);
    }

    /// <summary>TF_WEAPON_SNIPERRIFLE_UNCHARGE_PER_SEC (tf_weapon_sniperrifle.cpp:39).</summary>
    private const float UnchargePerSecond = 75f;
}
