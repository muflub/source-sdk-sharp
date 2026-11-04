using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <see cref="TFWeaponBehaviour.ModifyDeployTime"/> — stock's
/// <c>CTFWeaponBase::Deploy</c> multiplier (<c>tf_weaponbase.cpp:1255-</c>):
/// the incoming item's <c>mult_single_wep_deploy_time</c> scales the draw
/// always, the outgoing item's <c>mult_switch_from_wep_deploy_time</c> only
/// once that weapon has been held longer than the switch time
/// (<c>:1262-1272</c>).
/// </summary>
public sealed class TFDeployTimeTests
{
    private const int Incoming = TFWeaponWorld.Owner;
    private const string Scattergun = "tf_weapon_scattergun";
    private const string Pistol = "tf_weapon_pistol";

    private static TFWeaponWorld World(IAttributeProvider? incomingItem, IAttributeProvider? previousItem)
    {
        var world = new TFWeaponWorld();
        var byName = new System.Collections.Generic.Dictionary<string, IAttributeProvider>();
        if (incomingItem is not null)
            byName[Scattergun] = incomingItem;
        if (previousItem is not null)
            byName[Pistol] = previousItem;
        world.Owners.ByName[Incoming] = byName;
        return world;
    }

    /// <summary>
    /// <c>mult_single_wep_deploy_time</c> (<c>tf_weaponbase.cpp:1260</c>): the
    /// incoming weapon's own attribute scales its own deploy — a half on the
    /// item, half the switch.
    /// </summary>
    [Fact]
    public void ASingleWeaponDeployTimeAttributeScalesItsOwnDeploy()
    {
        var world = World(Item("mult_single_wep_deploy_time", 0.5f), null);
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, "", secondsSincePreviousDeploy: 10f);

        Assert.Equal(0.25f, delay, precision: 4);
    }

    /// <summary>
    /// <c>mult_switch_from_wep_deploy_time</c> on the weapon being put away
    /// (<c>tf_weaponbase.cpp:1270</c>): held longer than the switch time, it
    /// scales the incoming weapon's draw too.
    /// </summary>
    [Fact]
    public void ASwitchFromAttributeScalesTheNextWeaponsDeploy()
    {
        var world = World(null, Item("mult_switch_from_wep_deploy_time", 0.5f));
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 10f);

        Assert.Equal(0.25f, delay, precision: 4);
    }

    /// <summary>
    /// The condition beside the second hook (<c>tf_weaponbase.cpp:1262-</c>):
    /// the last weapon was NOT held longer than the switch time, so its item
    /// is not asked and the draw keeps its length.
    /// </summary>
    [Fact]
    public void TheSwitchFromAttributeIsSkippedUnderStocksCondition()
    {
        var world = World(null, Item("mult_switch_from_wep_deploy_time", 0.5f));
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 0.4f);

        Assert.Equal(TFWeaponTypes.DeployTime, delay, precision: 4);
    }

    /// <summary>No item, no scaling: the row's stock deploy stands.</summary>
    [Fact]
    public void NoAttributeKeepsTheStockDeployTime()
    {
        var world = World(null, null);
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 10f);

        Assert.Equal(TFWeaponTypes.DeployTime, delay, precision: 4);
    }

    private static int _nextDef = 5200;

    /// <summary>An item holding one percentage attribute under the named hook class.</summary>
    private IAttributeProvider Item(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, AttributeDescriptionFormat.Percentage,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }
}
