using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The item charge meter (stock's <c>m_flItemChargeMeter</c>,
/// <c>CTFPlayerShared::UpdateItemChargeMeters</c>,
/// <c>tf_player_shared.cpp:7832-7904</c>): the bar spends to zero on a use,
/// refills over the item's charge seconds with the mult attribute stretching
/// them, rides the networked weapon blob inside the tier, and an empty bar
/// refuses the next use.
/// </summary>
public sealed class TFItemMeterTests
{
    private const int Owner = 3;
    private const string WeaponName = "tf_weapon_lunchbox";

    private static AttributeDefinition Def(int defindex, string attributeClass) =>
        new(defindex, attributeClass, attributeClass, AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default,
            MergedKeys.Block("x"));

    /// <summary>The item as the schema carries it: the charge seconds, and the mult that stretches them.</summary>
    private static AttributeContainer MeteredItem(float chargeSeconds, float rateMod)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(Def(900, TFItemMeter.ChargeRateAttribute), chargeSeconds));
        // The mult reads as a percentage hook on the base 1 (stock's default).
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(901, TFItemMeter.ChargeRateModAttribute, TFItemMeter.ChargeRateModAttribute,
                AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral, false, false,
                AttributeValueType.Default, MergedKeys.Block("x")),
            rateMod));
        return item;
    }

    private static FakeOwners OwnersWith(AttributeContainer item)
    {
        var owners = new FakeOwners();
        owners.Items[Owner] = item;
        return owners;
    }

    [Fact]
    public void AUsedBarRefillsOverItsRegenTime()
    {
        var hooks = new AttributeHooks();
        var owners = OwnersWith(MeteredItem(chargeSeconds: 2f, rateMod: 1f));
        var weapon = new WeaponNetState();
        weapon.BindLocal();
        weapon.ItemMeter = 1f;

        // The use spends the whole bar.
        TFItemMeter.Use(weapon);
        Assert.Equal(0f, weapon.ItemMeter);

        // Half a second of a two-second refill.
        TFItemMeter.Tick(weapon, 0.5f, hooks, owners, Owner, WeaponName);
        Assert.Equal(0.25f, TFItemMeter.MeterOf(weapon));

        // One second's worth, half full.
        TFItemMeter.Tick(weapon, 0.5f, hooks, owners, Owner, WeaponName);
        Assert.Equal(0.5f, TFItemMeter.MeterOf(weapon));

        // Past full the bar is held at full (stock's clamp to 100, :7891-7899).
        TFItemMeter.Tick(weapon, 1.5f, hooks, owners, Owner, WeaponName);
        Assert.Equal(1f, TFItemMeter.MeterOf(weapon));
    }

    [Fact]
    public void TheChargeRateAttributeScalesTheRefill()
    {
        var hooks = new AttributeHooks();
        // charge_rate 2 with mult 2: a four-second refill, stock's
        // flMeterChargeRate * flMeterChargeMult (:7881-7884).
        var owners = OwnersWith(MeteredItem(chargeSeconds: 2f, rateMod: 2f));
        var weapon = new WeaponNetState();
        weapon.BindLocal();

        TFItemMeter.Tick(weapon, 1f, hooks, owners, Owner, WeaponName);

        // One second of a four-second bar, not of a two-second one.
        Assert.Equal(0.25f, TFItemMeter.MeterOf(weapon));
    }

    [Fact]
    public void ABarAtZeroRefusesTheUse()
    {
        var weapon = new WeaponNetState();
        weapon.BindLocal();
        Assert.False(TFItemMeter.CanUse(weapon));

        // Something charged is spendable again — the stimulus must be non-zero.
        var hooks = new AttributeHooks();
        TFItemMeter.Tick(weapon, 0.5f, hooks, OwnersWith(MeteredItem(2f, 1f)), Owner, WeaponName);
        Assert.True(TFItemMeter.CanUse(weapon));
    }

    [Fact]
    public void TheBarIsNetworkedWithinTheTier()
    {
        var layout = StandaloneNetStates.Of(typeof(WeaponNetState));

        // The bar has to ride the carried blob, not sit client-local.
        Assert.Contains(layout.Fields, f => f.Name == "ItemMeter");

        // And the grown blob still fits the largest standard tier
        // (TFNetStateTierTests' bound).
        Assert.True(layout.Size <= 2048, $"weapon blob is {layout.Size} bytes");
    }

    /// <summary>
    /// The player's think moves the meter through the real caller: stock
    /// fills it once a frame from <c>CTFPlayer::ItemPostFrame</c>
    /// (<c>tf_player_shared.cpp:7870-7904</c>); ours runs in the machine's
    /// tail (<see cref="TFWeaponBehaviour.ItemPostFrame"/>), which the
    /// server's run of a command and the client's replay of it both run, on
    /// the command's own clock. Half a second of the held gun's commands
    /// fills a quarter of the two-second bar; a span longer than a command
    /// interval pays nothing.
    /// </summary>
    [Fact]
    public void ThePlayerThinkMovesTheMeterThroughTheMachineTail()
    {
        var world = new TFWeaponWorld();
        world.Owners.Items[TFWeaponWorld.Owner] = MeteredItem(chargeSeconds: 2f, rateMod: 1f);
        var (weapon, viewModel) = world.Armed("tf_weapon_scattergun", clip: 8, reserve: 30);

        // The first command over the fresh stamp carries its whole age: it
        // stamps the clock and pays nothing, the clamp's doing.
        world.Run(weapon, viewModel, buttons: 0, now: 10f);
        Assert.Equal(0f, TFItemMeter.MeterOf(weapon));

        // Half a second of a two-second bar.
        world.Run(weapon, viewModel, buttons: 0, now: 10.5f);
        Assert.Equal(0.25f, TFItemMeter.MeterOf(weapon));

        // The next command's half second, another quarter.
        world.Run(weapon, viewModel, buttons: 0, now: 11f);
        Assert.Equal(0.5f, TFItemMeter.MeterOf(weapon));

        // A two-second gap (a break, a lag spike) is outside a command
        // interval and pays nothing; the stamp still moves up.
        world.Run(weapon, viewModel, buttons: 0, now: 13f);
        Assert.Equal(0.5f, TFItemMeter.MeterOf(weapon));
    }
}
