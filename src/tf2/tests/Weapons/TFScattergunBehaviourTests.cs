using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFScattergunBehaviour"/>: the Force-a-Nature's shove, driven
/// through the core machine over the shipped script.
/// </summary>
public sealed class TFScattergunBehaviourTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const string Scattergun = "tf_weapon_scattergun";

    private static int _nextDef = 9100;

    private const string SodaPopper = "tf_weapon_soda_popper";

    /// <summary>A world over the extras whose predicted carrier is the one
    /// <see cref="FakePredictedData"/> answers: the popper's facts, the meter
    /// the drink reads.</summary>
    private static TFWeaponWorld HypedWorld(TFPlayerNetState carrier)
    {
        var data = new FakePredictedData { Carrier = carrier };
        carrier.BindLocal();
        return new(data, null, null, WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses()), null);
    }

    /// <summary>An item whose named hook holds one additive value.</summary>
    private static IAttributeProvider ItemWith(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    private static TFWeaponWorld World(TFPlayerNetState carrier)
    {
        var data = new FakePredictedData { Carrier = carrier };
        carrier.BindLocal();
        return new(data);
    }

    [Fact]
    public void The_shove_replaces_the_forward_component_and_keeps_the_rest()
    {
        // The local frame's x becomes -300, the sideways and vertical parts
        // the velocity they were (:347-351), fifty more up (:356).
        Vector shoved = TFScattergunBehaviour.Shoved(new Vector(100f, 40f, 10f), new Vector(1f, 0f, 0f));
        Assert.Equal(new Vector(-300f, 40f, 60f), shoved);
    }

    [Fact]
    public void An_airborne_shot_shoves_the_wielder_back_along_their_view_once()
    {
        var carrier = new TFPlayerNetState();
        var world = World(carrier);
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);
        var playerState = new PlayerNetState();
        playerState.BindLocal();
        playerState.Velocity = new Vector(100f, 40f, 10f);
        world.Arms.Carrier = playerState;
        world.Arms.AimAnswers = true;
        world.Owners.Airborne = true;
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith(TFScattergunBehaviour.KnockbackAttribute, 1f);

        world.Run(w, vm, Attack, 10f);

        Assert.True(carrier.ScattergunJump);
        Assert.Equal(new Vector(-300f, 40f, 60f), world.Arms.Carrier!.Velocity);

        // The latch holds for the airtime: the next shot shoves nothing new
        // (:331's m_bScattergunJump arm), the lift stays the one fifty.
        world.Run(w, vm, Attack, 10.7f);
        Assert.Equal(new Vector(-300f, 40f, 60f), world.Arms.Carrier.Velocity);
    }

    [Fact]
    public void A_grounded_shot_or_a_plain_scattergun_shoves_nothing()
    {
        var groundedCarrier = new TFPlayerNetState();
        var grounded = World(groundedCarrier);
        var (gw, gvm) = grounded.Armed(Scattergun, clip: 6, reserve: 32);
        var groundedState = new PlayerNetState();
        groundedState.BindLocal();
        groundedState.Velocity = new Vector(100f, 40f, 10f);
        grounded.Arms.Carrier = groundedState;
        grounded.Arms.AimAnswers = true;
        grounded.Owners.Items[TFWeaponWorld.Owner] = ItemWith(TFScattergunBehaviour.KnockbackAttribute, 1f);

        grounded.Run(gw, gvm, Attack, 10f);

        Assert.False(groundedCarrier.ScattergunJump);
        Assert.Equal(new Vector(100f, 40f, 10f), grounded.Arms.Carrier!.Velocity);

        var plainCarrier = new TFPlayerNetState();
        var plain = World(plainCarrier);
        var (pw, pvm) = plain.Armed(Scattergun, clip: 6, reserve: 32);
        var plainState = new PlayerNetState();
        plainState.BindLocal();
        plainState.Velocity = new Vector(100f, 40f, 10f);
        plain.Arms.Carrier = plainState;
        plain.Arms.AimAnswers = true;
        plain.Owners.Airborne = true;

        plain.Run(pw, pvm, Attack, 10f);

        Assert.False(plainCarrier.ScattergunJump);
        Assert.Equal(new Vector(100f, 40f, 10f), plain.Arms.Carrier!.Velocity);
    }

    [Fact]
    public void A_full_meter_at_the_secondary_arms_the_hype()
    {
        // CTFSodaPopper::SecondaryAttack (tf_weapon_shotgun.cpp:484-494): the
        // pressed secondary is the drink, the buff's own standing the only
        // debounce, and no base's class skill runs with it.
        var carrier = new TFPlayerNetState();
        var world = HypedWorld(carrier);
        carrier.HypeMeter = TFHype.FullMeter;
        var (w, vm) = world.Armed(SodaPopper, clip: 6, reserve: 12);
        var conditions = world.Owners.Of(TFWeaponWorld.Owner);

        world.Run(w, vm, Attack2, 10f);

        Assert.True(conditions.Has(TFCond.SodapopperHype));
        Assert.Empty(world.Effects.ClassSkills);
    }

    [Fact]
    public void A_short_meter_or_the_standing_buff_arms_nothing()
    {
        var shortCarrier = new TFPlayerNetState();
        var shortWorld = HypedWorld(shortCarrier);
        shortCarrier.HypeMeter = TFHype.FullMeter - 1f;
        var (sw, svm) = shortWorld.Armed(SodaPopper, clip: 6, reserve: 12);
        var shortConditions = shortWorld.Owners.Of(TFWeaponWorld.Owner);

        shortWorld.Run(sw, svm, Attack2, 10f);

        Assert.False(shortConditions.Has(TFCond.SodapopperHype));

        var hypedCarrier = new TFPlayerNetState();
        var hypedWorld = HypedWorld(hypedCarrier);
        var (hw, hvm) = hypedWorld.Armed(SodaPopper, clip: 6, reserve: 12);
        var hypedConditions = hypedWorld.Owners.Of(TFWeaponWorld.Owner);
        hypedConditions.Add(TFCond.SodapopperHype); // IsHypeBuffed, :487

        hypedWorld.Run(hw, hvm, Attack2, 10f);

        Assert.Equal(0f, hypedCarrier.HypeMeter, 4); // the drink touched no meter
    }

    [Fact]
    public void The_reload_leaves_the_drink_open()
    {
        // ItemBusyFrame's arm (:469-481): the held secondary drinks while the
        // gun reloads, the machine's own alt-fire arm behind its reload
        // return — the busy door is the only one this press has.
        var carrier = new TFPlayerNetState();
        var world = HypedWorld(carrier);
        carrier.HypeMeter = TFHype.FullMeter;
        var (w, vm) = world.Armed(SodaPopper, clip: 0, reserve: 12);
        var conditions = world.Owners.Of(TFWeaponWorld.Owner);

        world.Run(w, vm, (int)InputButtons.Reload, 10f); // the reload starts
        Assert.True(w.InReload);
        world.Run(w, vm, Attack2 | (int)InputButtons.Reload, 10.1f);

        Assert.True(conditions.Has(TFCond.SodapopperHype));
    }

    [Fact]
    public void The_plain_scatterguns_secondary_is_the_bases()
    {
        var carrier = new TFPlayerNetState();
        var world = HypedWorld(carrier);
        carrier.HypeMeter = TFHype.FullMeter;
        var (w, vm) = world.Armed(Scattergun, clip: 6, reserve: 32);

        world.Run(w, vm, Attack2, 10f);

        Assert.Single(world.Effects.ClassSkills);
        Assert.Equal(TFHype.FullMeter, carrier.HypeMeter, 4);
    }
}
