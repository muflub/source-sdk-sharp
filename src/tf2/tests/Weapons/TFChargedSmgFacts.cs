using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Cleaner's Carbine's boost (<see cref="TFWeaponBehaviour.SecondaryAttack"/>'s
/// ChargedSmg arms, stock <c>CTFChargedSMG</c>, tf_weapon_smg.cpp): the alt-fire
/// that answers nothing on a short meter, spends a full one on the mini-crit
/// buff and holds that buff for the held item's seconds (:101-172).
/// </summary>
public sealed class TFChargedSmgFacts
{
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int Owner = TFWeaponWorld.Owner;

    /// <summary>A seed whose gun roll draws no crit: the press's own halves stay plain.</summary>
    private static int NoCrit => TFWeaponWorld.GunSeed(200, 10000);

    private static int _nextDef = 5800;

    /// <summary>An item holding one attribute under the named hook class.</summary>
    private static IAttributeProvider ItemWith(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>A world with the extras fixtures and one armed carbine.</summary>
    private static TFWeaponWorld Armed(out WeaponNetState w, out ViewModelNetState vm)
    {
        var world = new TFWeaponWorld(files: WeaponFixtures.WithExtras(WeaponFixtures.Shipped()));
        (w, vm) = world.Armed("tf_weapon_charged_smg", clip: 25, reserve: 0);
        world.Owners.Of(Owner); // the press's cond grant lands where Of reads
        return world;
    }

    [Fact]
    public void A_full_meter_holds_the_buff_on_the_held_seconds()
    {
        var world = Armed(out var w, out var vm);
        world.Owners.Items[Owner] = ItemWith("minicrit_boost_when_charged", 8f);
        w.BehaviourMeter = TFWeaponBehaviour.ChargedSmgMeterCap;

        world.Run(w, vm, Attack2, 10f, NoCrit);

        // The base's half armed the refire (:157), the meter is spent (:159)
        // and the buff rides the held item's seconds (:164-170).
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.Equal(10.5f, w.NextSecondaryAttack, 3);
        Assert.True(world.Owners.Of(Owner)!.Has(TFCond.EnergyBuff));
        Assert.Equal(8f, world.Owners.Of(Owner)!.Duration(TFCond.EnergyBuff), 3);
    }

    [Fact]
    public void An_unfilled_meter_refuses_the_boost_entirely()
    {
        var world = Armed(out var w, out var vm);
        world.Owners.Items[Owner] = ItemWith("minicrit_boost_when_charged", 8f);
        w.BehaviourMeter = TFWeaponBehaviour.ChargedSmgMeterCap - 40f;

        world.Run(w, vm, Attack2, 10f, NoCrit);

        // CanPerformSecondaryAttack's door (:101-104): with the meter short
        // the press answers nothing — not even the refire arm, as in stock.
        Assert.Equal(0f, w.NextSecondaryAttack, 4);
        Assert.Equal(60f, w.BehaviourMeter, 4);
        Assert.False(world.Owners.Of(Owner)!.Has(TFCond.EnergyBuff));
    }

    [Fact]
    public void A_press_without_the_seconds_attribute_only_spends_the_meter()
    {
        var world = Armed(out var w, out var vm);
        w.BehaviourMeter = TFWeaponBehaviour.ChargedSmgMeterCap;

        world.Run(w, vm, Attack2, 10f, NoCrit);

        // The plain-zero duration (:166): the meter still goes (:159), the
        // buff is not granted at all.
        Assert.Equal(0f, w.BehaviourMeter, 4);
        Assert.Equal(10.5f, w.NextSecondaryAttack, 3);
        Assert.False(world.Owners.Of(Owner)!.Has(TFCond.EnergyBuff));
    }
}
