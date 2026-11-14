using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFLunchboxBehaviour"/>: the heavy's sandvich — the primary's
/// taunt-eat (<c>CTFLunchBox::PrimaryAttack</c>,
/// shared/tf/tf_weapon_lunchbox.cpp:175-190, the bite the server's taunt
/// attack asks) and the secondary's thrown plate (:195-295), the sandvich the
/// meter hands back (<c>OnResourceMeterFilled</c>, :437-444).
/// </summary>
public sealed class TFLunchboxTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Lunchbox = "tf_weapon_lunchbox";

    private static (TFWeaponWorld World, WeaponNetState W, ViewModelNetState Vm) Heavy(int reserve = 1)
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.HeavyWeapons;
        var (w, vm) = world.Armed(Lunchbox, clip: 0, reserve: reserve);
        return (world, w, vm);
    }

    private static AttributeContainer Item(float defindex, string attribute, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition((int)defindex, attribute, attribute, AttributeDescriptionFormat.Additive,
                AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x")),
            value));
        return item;
    }

    [Fact]
    public void The_lunchbox_row_runs_the_lunchbox_behaviour()
    {
        var (world, _, _) = Heavy();

        // The kind keyed from the script's id (TFWeaponKinds.Of's Lunchbox
        // case), the row's ammo the sandvich pool (TF_AMMO_GRENADES1).
        Assert.Equal("tf2.lunchbox", world.Row(Lunchbox).Behaviour);
        Assert.Equal("TF_AMMO_GRENADES1", world.Row(Lunchbox).AmmoName);
        Assert.IsType<TFLunchboxBehaviour>(world.Behaviour(Lunchbox));
    }

    [Fact]
    public void The_primary_attack_eats_and_spends_the_sandvich()
    {
        var (world, w, vm) = Heavy();

        world.Run(w, vm, Attack, now: 10f);

        // The taunt asked once, the sandvich and the meter with it
        // (:182, DrainAmmo's spend, SetItemChargeMeter's nought).
        Assert.Equal(1, world.Effects.LunchboxEats);
        Assert.Equal(0, w.Reserve);
        Assert.Equal(0f, w.ItemMeter, 4);

        // The next bite past the taunt (:188's client arm, the condition
        // absent): stock's 2.0, and no shot body ran for the veto.
        Assert.Equal(10f + TFLunchboxBehaviour.ClientEatRepeat, w.NextPrimaryAttack, 4);
        Assert.Empty(world.Arms.Swings);
        Assert.Empty(world.Arms.Bullets);
    }

    [Fact]
    public void The_eat_waits_for_the_taunts_last_bite()
    {
        var (world, w, vm) = Heavy();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Taunting, 1.8f);

        world.Run(w, vm, Attack, now: 10f);

        // :186: the condition's remainder plus the tail.
        Assert.Equal(10f + 1.8f + TFLunchboxBehaviour.EatTail, w.NextPrimaryAttack, 4);
    }

    [Fact]
    public void A_held_trigger_eats_once_per_taunt()
    {
        var (world, w, vm) = Heavy();

        world.Run(w, vm, Attack, now: 10f);
        world.Run(w, vm, Attack, now: 10.5f);

        // The stamp holds the next bite off till 12 s (:186).
        Assert.Equal(1, world.Effects.LunchboxEats);
        Assert.Equal(0, w.Reserve);
    }

    [Fact]
    public void An_empty_hand_does_not_eat()
    {
        var (world, w, vm) = Heavy(reserve: 0);

        world.Run(w, vm, Attack, now: 10f);

        // Stock's head (:179): no sandvich, no eat, nothing spent.
        Assert.Equal(0, world.Effects.LunchboxEats);
        Assert.Equal(0, w.Reserve);
    }

    [Fact]
    public void The_secondary_throws_the_plate()
    {
        var (world, w, vm) = Heavy();

        world.Run(w, vm, Attack2, now: 10f);

        // The plain sandvich's plate (mode 0, no item), the sandvich and the
        // meter gone (:291, :294) and the hands on the next best (:292).
        var drop = Assert.Single(world.Effects.LunchboxDrops);
        Assert.Equal((Lunchbox, 0f), (drop.Weapon, (float)drop.Mode));
        Assert.Equal(0, w.Reserve);
        Assert.Equal(0f, w.ItemMeter, 4);
        Assert.Equal(1, world.Arms.NextBestSwitches);

        // Held past the throw, the empty hand refuses the next one (:197).
        world.Run(w, vm, Attack2, now: 10.5f);
        Assert.Single(world.Effects.LunchboxDrops);
    }

    [Fact]
    public void No_plate_leaves_mid_taunt()
    {
        var (world, w, vm) = Heavy();
        world.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.Taunting, 1.8f);

        world.Run(w, vm, Attack2, now: 10f);

        // DropAllowed (:160-170): the taunt holds the throw.
        Assert.Empty(world.Effects.LunchboxDrops);
        Assert.Equal(1, w.Reserve);
    }

    [Fact]
    public void The_throw_is_the_items_shape()
    {
        var (world, w, vm) = Heavy();
        world.Owners.Items[TFWeaponWorld.Owner] = Item(910, "set_weapon_mode", 2f);

        world.Run(w, vm, Attack2, now: 10f);

        // GetLunchboxType (tf_weapon_lunchbox.h:63): the Buffalo Sandwich's
        // mode names the plate the server throws.
        var drop = Assert.Single(world.Effects.LunchboxDrops);
        Assert.Equal((Lunchbox, 2), (drop.Weapon, drop.Mode));
    }

    [Fact]
    public void A_filled_meter_hands_the_sandvich_back()
    {
        var (world, w, vm) = Heavy(reserve: 0);
        world.Owners.Items[TFWeaponWorld.Owner] = Item(911, TFItemMeter.ChargeRateAttribute, 1f);
        w.ItemMeter = 0.5f;
        w.ItemMeterTime = 9f;

        world.Run(w, vm, 0, now: 10f);

        // OnResourceMeterFilled (:437-444): the second on the bar is the
        // eaten sandvich again.
        Assert.Equal(1f, w.ItemMeter, 4);
        Assert.Equal(1, w.Reserve);
    }

    [Fact]
    public void A_bar_that_never_fills_leaves_the_hand_empty()
    {
        var (world, w, vm) = Heavy(reserve: 0);
        w.ItemMeter = 0.5f;
        w.ItemMeterTime = 9.5f;

        world.Run(w, vm, 0, now: 10f);

        // Half a second of no charge rate: the bar stands, no sandvich.
        Assert.Equal(0.5f, w.ItemMeter, 4);
        Assert.Equal(0, w.Reserve);
    }
}
