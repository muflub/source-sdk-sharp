using SourceSharp.Tests;
using SourceSharp.Abi;

using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Town;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Town;

/// <summary>
/// What a town window shows, on the wire (plan_descent_logic WO8, UI4): a vendor's stock to its
/// customer alone and within one message each, sent again at each open and at a restock; the
/// crate's published odds; the depth picker's rows by the rules.
/// </summary>
public sealed class TownWireTests
{
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_vendors_stock_goes_to_its_customer_alone_one_ware_a_message_within_a_user_message()
    {
        var rig = new TownRig();
        int mark = rig.Mark;

        rig.Use("saxton");

        var wares = rig.Sent(mark).Where(s => s.Key.EndsWith(VendorWareMessage.Name, StringComparison.Ordinal)).ToList();
        var stock = (await rig.Ledger.Stock(TownRig.Key, "saxton", rig.Service.Restock)).Items;
        Assert.Equal(stock.Count, wares.Count);
        Assert.All(wares, w => Assert.Equal(TownRig.Index, w.Player));
        Assert.All(wares, w => Assert.InRange(w.Bytes.Length, 1, 255));
        Assert.Equal(stock.Select(s => s.Id), rig.Of<VendorWare>(mark).Select(w => w.Id));
        Assert.Equal(stock.Select(s => s.Price), rig.Of<VendorWare>(mark).Select(w => w.Price));
    }

    [Fact]
    public void A_ware_at_its_fullest_fits_one_user_message()
    {
        var full = new VendorWare
        {
            Id = long.MaxValue, Price = long.MaxValue,
            Affixes = new byte[OwnerItem.MaxAffixes], Values = new float[OwnerItem.MaxAffixes],
        };

        Assert.InRange(full.Write(new byte[255]), 1, 255);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_window_opened_again_sends_its_stock_again()
    {
        var rig = new TownRig();
        rig.Use("saxton");
        int first = rig.Of<VendorWare>().Count;
        rig.Service.Close(TownRig.Index);
        int mark = rig.Mark;

        rig.Use("saxton");

        Assert.Equal(first, rig.Of<VendorWare>(mark).Count);
        Assert.Single(rig.Of<VendorList>(mark), l => l.List == (byte)TownList.Stock);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_restock_sends_an_open_window_another_stock()
    {
        var rig = new TownRig();
        rig.Use("saxton");
        var first = rig.Of<VendorWare>().Select(w => w.Id).ToList();
        rig.World.Clock = rig.Rules.Content.EconomySettings.RestockSeconds + 1;
        int mark = rig.Mark;

        for (int f = 0; f < TownService.CheckEvery; f++)
            rig.Service.OnPostThink(0.015f);

        var next = rig.Of<VendorWare>(mark).Select(w => w.Id).ToList();
        Assert.NotEmpty(next);
        Assert.Empty(next.Intersect(first));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task An_unidentified_ware_carries_no_affix_on_the_wire()
    {
        var rig = new TownRig();
        var item = rig.Roll("rocket_launcher", 2, 20) with { Identified = false };
        int slot = rig.Give(item);
        rig.Use("saxton");
        await rig.Service.Sell(TownRig.Index, slot);
        int mark = rig.Mark;

        await rig.Service.Refresh(TownRig.Index);

        var back = rig.Of<VendorWare>(mark).Single(w => w.List == (byte)TownList.BuyBack);
        Assert.Empty(back.Affixes ?? []);
        Assert.Equal(0, back.Flags & (byte)ItemViewFlags.Identified);
        Assert.NotEmpty(item.Affixes);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_crate_stall_publishes_the_rules_odds_out_of_a_hundred_thousand()
    {
        var rig = new TownRig();
        int mark = rig.Mark;

        rig.Use("scout");

        var window = rig.Of<TownWindow>(mark).Last().View();
        var odds = rig.Rules.Crafting.GambleOdds().OrderBy(o => o.Key).ToList();
        Assert.Equal(odds.Select(o => o.Key), window.OddsRarities);
        Assert.Equal(odds.Select(o => (int)Math.Round(o.Value * TownWindow.OddsScale)), window.OddsWeights);
        Assert.Equal(TownWindow.OddsScale, window.OddsWeights.Sum());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_depth_picker_offers_the_depths_the_rules_allow()
    {
        var rig = new TownRig();
        rig.World.Reached = 3;
        rig.World.Feet = new Vector(1200f, 0f, 0f);
        int mark = rig.Mark;

        Assert.True(rig.Service.OpenDepth(TownRig.Index));

        var rows = rig.Of<DepthRows>(mark).Last().View();
        var open = rows.Depths.Where((d, i) => (rows.Flags[i] & DepthFlags.Open) != 0);
        Assert.Equal(TF2.Descent.Rules.TownDescents.Destinations(3, []), open);
        Assert.Contains(5, rows.Depths);
        Assert.Equal(DepthFlags.Boss, rows.Flags[rows.Depths.ToList().IndexOf(5)] & DepthFlags.Boss);
        Assert.Equal((TownRig.Index, "descent_depthpicker", true), rig.World.Panels.Last());
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_depth_the_rules_do_not_allow_is_refused_and_one_they_do_goes_down_and_closes_the_picker()
    {
        var rig = new TownRig();
        rig.World.Reached = 2;
        rig.World.Feet = new Vector(1200f, 0f, 0f);
        rig.Service.OpenDepth(TownRig.Index);

        Assert.False(rig.Service.Descend(TownRig.Index, 4));
        Assert.Equal("#Descent_Town_" + TownRefusals.DepthClosed, rig.Status.Token);
        Assert.True(rig.Service.Descend(TownRig.Index, 2));

        Assert.Equal([(TownRig.Index, 2)], rig.World.Descents);
        Assert.Null(rig.Service.Open(TownRig.Index));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Unhosted_with_no_level_of_that_depth_installed_the_descent_is_refused_and_the_picker_stays()
    {
        var rig = new TownRig();
        rig.World.Feet = new Vector(1200f, 0f, 0f);
        rig.World.DescendRefusal = TownRefusals.NoLevel;
        rig.Service.OpenDepth(TownRig.Index);

        Assert.False(rig.Service.Descend(TownRig.Index, 1));

        Assert.Equal("#Descent_Town_" + TownRefusals.NoLevel, rig.Status.Token);
        Assert.Equal(TownScreen.Depth, rig.Service.Open(TownRig.Index)?.Screen);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_bench_quotes_its_recipes_by_the_rules()
    {
        var rig = new TownRig();
        rig.Give(rig.Roll("scrap", count: 9));
        rig.Give(rig.Roll("cap", 1, 5, seed: 3));
        int mark = rig.Mark;

        rig.Use("bench");

        var q = rig.Of<CraftQuote>(mark).Last().View();
        var e = rig.Rules.Content.EconomySettings;
        Assert.Equal(9, q.Metal);
        Assert.Equal(e.RepairKitCost, q.Costs[(int)BenchRecipe.RepairKit]);
        Assert.NotEqual(0, q.Ready & (1 << (int)BenchRecipe.RepairKit));
        Assert.Equal(0, q.Ready & (1 << (int)BenchRecipe.CraftItem));   // 9 scrap is short of a craft's 27
        Assert.Equal(rig.Held.Inventory.Backpack[rig.Craft.Work(rig.Held)].Id, q.Item);
    }
}
