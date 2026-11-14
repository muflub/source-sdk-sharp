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
    /// <c>mult_switch_from_wep_deploy_time</c> at full strength
    /// (<c>tf_weaponbase.cpp:1270</c>): the Bread Bite's own item is a whole
    /// and a half, and the axe it is switched from had been up well past the
    /// switch time — the draw lands half again as long, the held axe's own
    /// seconds answering the :1262 gate.
    /// </summary>
    [Fact]
    public void SwitchingFromTheAxeSlowsTheDeploy()
    {
        var world = new TFWeaponWorld();
        world.Owners.ByName[Incoming] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>
        {
            ["tf_weapon_fireaxe"] = Item("mult_switch_from_wep_deploy_time", 1.5f),
        };
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, "tf_weapon_fireaxe", secondsSincePreviousDeploy: 10f);

        Assert.Equal(TFWeaponTypes.DeployTime * 1.5f, delay, precision: 4);
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

    /// <summary>
    /// The Escape Plan's mark (<c>tf_weaponbase_melee.cpp:148-159</c>): the
    /// outgoing melee's item answers <c>self_mark_for_death</c> and its owner
    /// catches the silent mark for the attribute's own seconds — the hold's
    /// length never gates it, unlike the switch multiplier beside it. The
    /// mark then runs out its seconds while nobody re-stamps it, and the
    /// ticks between are watched every tick.
    /// </summary>
    [Fact]
    public void EscapePlanMarksItsOwnerForItsOwnSeconds()
    {
        var world = new TFWeaponWorld();
        world.Owners.ByName[Incoming] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>
        {
            [Pistol] = CountItem("self_mark_for_death", 3f),
        };
        var set = world.Owners.Of(Incoming);
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 10f);

        Assert.True(set.Has(TFCond.MarkedfordeathSilent));
        Assert.Equal(3f, set.Duration(TFCond.MarkedfordeathSilent), precision: 4);

        // The seconds are the attribute's (:157): seven halves run the mark out.
        Assert.True(set.Has(TFCond.MarkedfordeathSilent));
        set.Tick(0.5f, healers: 0, expiresFast: default);     // :356's held refresh rides on this build's
        set.Tick(0.5f, healers: 0, expiresFast: default);     // deploy cadence — the mark re-stamps on
        set.Tick(0.5f, healers: 0, expiresFast: default);     // arrival and runs out past its seconds
        set.Tick(0.5f, healers: 0, expiresFast: default);
        set.Tick(0.5f, healers: 0, expiresFast: default);
        set.Tick(0.5f, healers: 0, expiresFast: default);
        Assert.False(set.Has(TFCond.MarkedfordeathSilent));
    }

    /// <summary>
    /// The mark beside the gated multiplier: a too-short hold skips the
    /// outgoing item's deploy scale (<c>tf_weaponbase.cpp:1262-</c>) but never
    /// the mark, whose stock arms (<c>:153</c>) ask nothing of the hold.
    /// </summary>
    [Fact]
    public void AShortHoldSkipsTheMultiplierButNotTheMark()
    {
        var world = new TFWeaponWorld();
        world.Owners.ByName[Incoming] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>
        {
            [Pistol] = CountItem("self_mark_for_death", 2f),
        };
        var set = world.Owners.Of(Incoming);
        var row = world.Row(Scattergun);

        float delay = world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 0.4f);

        Assert.Equal(TFWeaponTypes.DeployTime, delay, precision: 4);     // the multiplier's gate
        Assert.True(set.Has(TFCond.MarkedfordeathSilent));               // the mark stands (:153)
    }

    /// <summary>No attribute, no mark: the switch lands on nobody.</summary>
    [Fact]
    public void NoMarkAttributeLeavesTheOwnerUnmarked()
    {
        var world = new TFWeaponWorld();
        world.Owners.ByName[Incoming] = new System.Collections.Generic.Dictionary<string, IAttributeProvider>();
        var set = world.Owners.Of(Incoming);
        var row = world.Row(Scattergun);

        world.Behaviour(Scattergun)
            .ModifyDeployTime(row, TFWeaponTypes.DeployTime, Incoming, Pistol, secondsSincePreviousDeploy: 10f);

        Assert.False(set.Has(TFCond.MarkedfordeathSilent));
    }

    private static int _nextDef = 5200;

    /// <summary>An item holding one additive attribute: its value reaches an int hook whole.</summary>
    private static IAttributeProvider CountItem(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(5290, "attr 5290", hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

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
