using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>Vendor stock (plan_tf2_rpg 2d, D15): per vendor, per seed, at the buyer's level, identified.</summary>
public sealed class VendorTests
{
    private static readonly TF2DescentRules R = NewRules();

    [Fact]
    public void Vendor_stock_is_seed_stable_and_changes_with_the_seed()
    {
        var a = R.VendorStock("saxton@12", 7);
        var b = R.VendorStock("saxton@12", 7);
        var c = R.VendorStock("saxton@12", 8);

        Assert.Equal(a.Select(i => Convert.ToHexString(i.Instance)), b.Select(i => Convert.ToHexString(i.Instance)));
        Assert.NotEqual(a.Select(i => Convert.ToHexString(i.Instance)), c.Select(i => Convert.ToHexString(i.Instance)));
    }

    [Fact]
    public void Each_vendor_stocks_its_own_kinds()
    {
        Assert.All(R.VendorStock("saxton@12", 1), i => Assert.Equal(ItemKinds.Weapon, R.Content.ItemBases[i.BaseType].Kind));
        Assert.All(R.VendorStock("engineer@12", 1), i => Assert.Equal(ItemKinds.Armour, R.Content.ItemBases[i.BaseType].Kind));
        Assert.All(R.VendorStock("medic@12", 1), i => Assert.True(
            R.Content.ItemBases[i.BaseType].Kind == ItemKinds.Potion || R.Content.Vendors["medic"].Always.Contains(i.BaseType), i.BaseType));
        Assert.All(R.VendorStock("spy@12", 1), i => Assert.Equal("dossier", i.BaseType));
        Assert.All(R.VendorStock("scout@12", 1), i => Assert.Equal(ItemKinds.Crate, R.Content.ItemBases[i.BaseType].Kind));
        Assert.Equal(R.Content.Vendors["saxton"].Stock, R.VendorStock("saxton", 1).Count);
    }

    [Fact]
    public void Vendor_stock_is_rolled_at_the_buyers_level_for_the_buyers_class()
    {
        var stock = Enumerable.Range(0, 50).SelectMany(s => R.VendorStock("saxton@7@soldier", (ulong)s)).ToList();

        Assert.All(stock, i => Assert.Equal(7, i.ItemLevel));
        Assert.All(stock, i => Assert.True(R.Content.ItemBases[i.BaseType].AllowsClass("soldier") && R.Content.ItemBases[i.BaseType].Level <= 7, i.BaseType));
    }

    [Fact]
    public void Vendor_stock_arrives_identified_without_curses_and_replays()
    {
        var stock = Enumerable.Range(0, 50).SelectMany(s => R.VendorStock("engineer@25", (ulong)s)).ToList();

        Assert.All(stock, i => Assert.True(i.Identified));
        Assert.All(stock, i => Assert.True(R.Replays(i)));
        Assert.All(stock, i => Assert.DoesNotContain(R.ItemRules.Codec.Instance(i).Affixes, a => R.Content.Affixes[a.Id].IsCursed));
        Assert.Contains(stock, i => i.Rarity == 1);   // the odds reach Vintage
    }

    [Fact]
    public void An_unknown_vendor_stocks_nothing()
    {
        Assert.Empty(R.VendorStock("smith", 1));
    }
}
