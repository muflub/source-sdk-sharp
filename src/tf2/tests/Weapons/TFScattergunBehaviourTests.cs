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
    private const string Scattergun = "tf_weapon_scattergun";

    private static int _nextDef = 9100;

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
}
