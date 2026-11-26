using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Hitman's Heatmaker's Focus: <c>CTFSniperRifle::ItemPostFrame</c>'s rage
/// arm (<c>tf_weapon_sniperrifle.cpp:420-431</c>) — reload held with any rage
/// over 1 lights the item's <c>set_buff_type</c> 6 through
/// <c>ActivateRageBuff</c> (<c>tf_player_shared.cpp:13753-13795</c>), which
/// lets the Focus past the full-meter gate and scales its ten pulses by the
/// meter. The rifle runs on the real command loop over the shipped script.
/// </summary>
public sealed class TFHeatmakerFocusFacts
{
    private const string Rifle = "tf_weapon_sniperrifle";
    private const int Reload = (int)InputButtons.Reload;

    private static (TFWeaponWorld World, TFPlayerNetState Carrier, WeaponNetState W, ViewModelNetState Vm)
        Heatmaker(float rage, int buffType = TFSniperRifleBehaviour.FocusBuffType)
    {
        var carrier = new TFPlayerNetState();
        carrier.BindLocal();
        var world = new TFWeaponWorld(new FakePredictedData { Carrier = carrier });
        world.Owners.Classes[TFWeaponWorld.Owner] = TFClass.Sniper;
        world.Owners.Of(TFWeaponWorld.Owner);
        if (buffType > 0)
        {
            var item = new AttributeContainer();
            item.Set(AttributeValue.OfFloat(
                new AttributeDefinition(752, "set_buff_type", TFRageMeter.BuffTypeAttribute,
                                        AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                        false, false, AttributeValueType.Default, MergedKeys.Block("x")),
                buffType));
            world.Owners.Items[TFWeaponWorld.Owner] = item;
        }
        var (w, vm) = world.Armed(Rifle, clip: -1, reserve: 25);
        carrier.RageMeter = rage;
        return (world, carrier, w, vm);
    }

    /// <summary>
    /// Half a meter and the reload key: the Focus lights below a full meter,
    /// for half its ten pulses, the first at once, the next a second on, with
    /// the battle cry.
    /// </summary>
    [Fact]
    public void ReloadWithHalfAMeterLightsTheFocusForHalfItsPulses()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 50f);

        world.Run(w, vm, Reload, 10f);

        Assert.True(carrier.RageDraining);
        Assert.Equal(TFSniperRifleBehaviour.FocusBuffType, carrier.RageBuffType);
        Assert.Equal(5, carrier.RageBuffPulsesLeft);
        Assert.Equal(11f, carrier.RageBuffNextPulseTime, 4);
        Assert.Equal(new[] { (TFWeaponWorld.Owner, TFSniperRifleBehaviour.FocusBuffType) }, world.Effects.Buffs);
        Assert.Equal(1, world.Effects.FocusCries);
    }

    /// <summary>
    /// Reload held for ten commands: the Focus is re-stamped every command, as
    /// stock's every-frame ask does, but the battle cry is the lighting press's
    /// alone (stock's speech refuses while the cry still plays,
    /// tf_player.cpp:19882-19886).
    /// </summary>
    [Fact]
    public void AHeldReloadCriesOnce()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 50f);

        for (int k = 0; k < 10; k++)
            world.Run(w, vm, Reload, 10f + k * 0.015f);

        Assert.True(carrier.RageDraining);
        Assert.Equal(10, world.Effects.Buffs.Count);
        Assert.Equal(1, world.Effects.FocusCries);
    }

    /// <summary>The pulse count is an int scaled by the meter's share (:13790): 99 rage is nine pulses.</summary>
    [Fact]
    public void ThePulsesTruncateTheMetersShare()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 99f);

        world.Run(w, vm, Reload, 10f);

        Assert.Equal(9, carrier.RageBuffPulsesLeft);
    }

    /// <summary>The control: the same rifle and meter without the reload key lights nothing.</summary>
    [Fact]
    public void WithoutTheReloadKeyNothingLights()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 50f);

        world.Run(w, vm, 0, 10f);

        Assert.False(carrier.RageDraining);
        Assert.Empty(world.Effects.Buffs);
    }

    /// <summary>The meter must stand over 1 (:423's <c>GetRageMeter() &gt; 1.0f</c>).</summary>
    [Fact]
    public void AMeterOfOneLightsNothing()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 1f);

        world.Run(w, vm, Reload, 10f);

        Assert.False(carrier.RageDraining);
        Assert.Empty(world.Effects.Buffs);
    }

    /// <summary>The stock rifle carries no <c>set_buff_type</c> (:426-427): the key lights nothing.</summary>
    [Fact]
    public void ARifleWithoutABuffTypeLightsNothing()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 50f, buffType: 0);

        world.Run(w, vm, Reload, 10f);

        Assert.False(carrier.RageDraining);
        Assert.Empty(world.Effects.Buffs);
    }

    /// <summary>
    /// Another buff type keeps <c>ActivateRageBuff</c>'s full-meter gate
    /// (:13756): a rifle wearing the banner's type 1 at half a meter lights nothing.
    /// </summary>
    [Fact]
    public void AnotherBuffTypeKeepsTheFullMeterGate()
    {
        var (world, carrier, w, vm) = Heatmaker(rage: 50f, buffType: 1);

        world.Run(w, vm, Reload, 10f);

        Assert.False(carrier.RageDraining);
        Assert.Empty(world.Effects.Buffs);
    }
}
