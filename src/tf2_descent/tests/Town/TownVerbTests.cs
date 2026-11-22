using SourceSharp.Tests;
using SourceSharp.Abi;
using SourceSharp.Host.Contracts;

using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Town;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Town;

/// <summary>
/// Each town verb against the rules' answer through the real ledger (plan_descent_logic WO8): the
/// price paid, the purse after, the item moved, and each refusal on the window's status line.
/// </summary>
public sealed class TownVerbTests
{
    private static VendorWareView Ware(TownRig rig, int mark, int index = 0) =>
        rig.Of<VendorWare>(mark).Select(w => w.View()).Where(w => w.List == TownList.Stock).ElementAt(index);

    //-------------------------------------------------------------------------
    // A shop
    //-------------------------------------------------------------------------

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task Buying_pays_the_rules_price_and_the_item_is_in_the_backpack()
    {
        var rig = new TownRig();
        rig.SetPurse(100_000);
        int mark = rig.Mark;
        rig.Use("saxton");
        var ware = Ware(rig, mark);

        var r = await rig.Service.Buy(TownRig.Index, ware.Item.Id);

        Assert.True(r!.Ok, r.Refusal);
        var bought = rig.Held.Inventory.Backpack.Values.Single(v => v.Id == ware.Item.Id);
        Assert.Equal(rig.Rules.Pricing.Price(bought.Item, PriceKind.Buy), ware.Price);
        Assert.Equal(100_000 - ware.Price, rig.Purse);
        Assert.False(rig.Status.Error);
        Assert.Equal(TownStatusTokens.Done("buy"), rig.Status.Token);
        Assert.Equal(ware.Item.Id, rig.Status.Item);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task Buying_without_the_australium_is_refused_on_the_status_line_and_changes_nothing()
    {
        var rig = new TownRig();
        rig.SetPurse(0);
        int mark = rig.Mark;
        rig.Use("saxton");
        int items = rig.Held.Items.Count();

        var r = await rig.Service.Buy(TownRig.Index, Ware(rig, mark).Item.Id);

        Assert.False(r!.Ok);
        Assert.Equal("#Descent_Town_" + InventoryRefusals.NotEnoughAustralium, rig.Status.Token);
        Assert.True(rig.Status.Error);
        Assert.Equal(items, rig.Held.Items.Count());
        Assert.Equal(0, rig.Purse);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task Buying_into_a_full_backpack_is_refused()
    {
        var rig = new TownRig();
        rig.SetPurse(100_000);
        ulong seed = 50;
        while (rig.Rules.Inventory.FreeSlot(rig.Held.Inventory, rig.Held.Sheet) >= 0)
            rig.Give(rig.Roll("cap", seed: seed++));
        int mark = rig.Mark;
        rig.Use("saxton");

        var r = await rig.Service.Buy(TownRig.Index, Ware(rig, mark).Item.Id);

        Assert.False(r!.Ok);
        Assert.Equal("#Descent_Town_" + InventoryRefusals.BackpackFull, rig.Status.Token);
        Assert.Equal(100_000, rig.Purse);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task Selling_raises_the_purse_by_the_sell_price_and_buying_back_costs_the_same()
    {
        var rig = new TownRig();
        rig.SetPurse(0);
        var item = rig.Roll("rocket_launcher", 1, 20, seed: 9);
        int slot = rig.Give(item);
        rig.Use("saxton");

        var sold = await rig.Service.Sell(TownRig.Index, slot);
        long price = rig.Rules.Price(rig.Rules.ItemRules.Codec.ToRolled(item), PriceKind.Sell);
        int mark = rig.Mark;
        await rig.Service.Refresh(TownRig.Index);
        var back = rig.Of<VendorWare>(mark).Select(w => w.View()).Single(w => w.List == TownList.BuyBack);
        var bought = await rig.Service.BuyBack(TownRig.Index, back.Index);

        Assert.True(sold!.Ok, sold.Refusal);
        Assert.True(price > 0);
        Assert.Equal(price, back.Price);
        Assert.True(bought!.Ok, bought.Refusal);
        Assert.Equal(0, rig.Purse);
        Assert.Contains(rig.Held.Inventory.Backpack.Values, v => v.Item.Seed == item.Seed);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task Haggler_lowers_a_buy_price_by_the_rules()
    {
        var rig = new TownRig();
        rig.Owner.Gear.Hero.Contributions.Add(new TownRig.SkillPassives(rig.Rules));
        rig.Owner.Level(15);
        for (int rank = 0; rank < 5; rank++)
            Assert.True(rig.Owner.Hero.Apply(s => rig.Rules.Skills.Buy(s, "haggler").Sheet).Ok);
        Assert.Equal(-20, rig.Owner.Hero.Stats!.Total("price_buy_pct"));
        rig.SetPurse(100_000);
        int mark = rig.Mark;

        rig.Use("saxton");
        var ware = Ware(rig, mark);
        await rig.Service.Buy(TownRig.Index, ware.Item.Id);

        var item = rig.Held.Inventory.Backpack.Values.Single(v => v.Id == ware.Item.Id).Item;
        long plain = rig.Rules.Pricing.Price(item, PriceKind.Buy);
        Assert.Equal(rig.Rules.Pricing.ForBuyer(plain, PriceKind.Buy, new BuyerPrices(-20, 20)), ware.Price);
        Assert.True(ware.Price < plain);
        Assert.Equal(100_000 - ware.Price, rig.Purse);
    }

    //-------------------------------------------------------------------------
    // Where a verb may run
    //-------------------------------------------------------------------------

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_verb_at_the_wrong_stall_is_refused()
    {
        var rig = new TownRig();
        int slot = rig.Give(rig.Roll("rocket_launcher", 1, 20) with { Identified = false });
        rig.Use("saxton");

        var r = await rig.Service.Identify(TownRig.Index, slot);

        Assert.Null(r);
        Assert.Equal("#Descent_Town_" + TownRefusals.WrongVendor, rig.Status.Token);
        Assert.False(rig.Held.Inventory.Backpack[slot].Item.Identified);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_verb_out_of_reach_is_refused_and_its_window_closes()
    {
        var rig = new TownRig();
        rig.SetPurse(100_000);
        int mark = rig.Mark;
        rig.Use("saxton");
        rig.World.Feet = rig.World.Feet!.Value + new Vector(0f, TownService.Reach + 50f, 0f);

        var r = await rig.Service.Buy(TownRig.Index, Ware(rig, mark).Item.Id);

        Assert.Null(r);
        Assert.Equal("#Descent_Town_" + TownRefusals.OutOfReach, rig.Status.Token);
        Assert.Null(rig.Service.Open(TownRig.Index));
        Assert.Equal(100_000, rig.Purse);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_verb_off_the_town_is_refused()
    {
        var rig = new TownRig();
        rig.Use("saxton");
        rig.Service.Place("tf2_descent-1-s1");

        var r = await rig.Service.Sell(TownRig.Index, 0);

        Assert.Null(r);
        Assert.Equal("#Descent_Town_" + TownRefusals.NotInTown, rig.Status.Token);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_verb_with_no_window_open_is_refused()
    {
        var rig = new TownRig();

        var r = await rig.Service.StashIn(TownRig.Index, 0);

        Assert.Null(r);
        Assert.Equal("#Descent_Town_" + TownRefusals.NoWindow, rig.Status.Token);
    }

    //-------------------------------------------------------------------------
    // The Spy and the Engineer
    //-------------------------------------------------------------------------

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_spy_identifies_for_the_fee_he_quoted()
    {
        var rig = new TownRig();
        rig.SetPurse(10_000);
        var item = rig.Roll("rocket_launcher", 1, 20) with { Identified = false };
        int slot = rig.Give(item);
        long id = rig.Held.Inventory.Backpack[slot].Id;
        int mark = rig.Mark;
        rig.Use("spy");
        var quotes = Assert.Single(rig.Of<TownQuotes>(mark)).View();

        var r = await rig.Service.Identify(TownRig.Index, slot);

        Assert.Equal(TownQuoteKind.Identify, quotes.Kind);
        long fee = quotes.Prices[quotes.Ids.ToList().IndexOf(id)];
        Assert.Equal(rig.Rules.Pricing.IdentifyFee(item), fee);
        Assert.True(r!.Ok, r.Refusal);
        Assert.True(rig.Held.Inventory.Backpack[slot].Item.Identified);
        Assert.Equal(10_000 - fee, rig.Purse);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_engineer_repairs_a_worn_launcher_to_its_maximum_for_the_rules_price_and_the_maximum_holds()
    {
        var rig = new TownRig();
        rig.SetPurse(10_000);
        string slot = rig.Held.Inventory.Equipped.Single(e => e.Value.Item.BaseId == "rocket_launcher").Key;
        int max = rig.Held.Inventory.Equipped[slot].Item.MaxDurability;
        await rig.Ledger.SetDurability(TownRig.Key, slot, 2, max);
        var worn = rig.Held.Inventory.Equipped[slot].Item;
        int mark = rig.Mark;
        rig.Use("engineer_repair");
        var window = rig.Of<TownWindow>(mark).Last().View();

        var r = await rig.Service.RepairAll(TownRig.Index);

        var after = rig.Held.Inventory.Equipped[slot].Item;
        Assert.True(r!.Ok, r.Refusal);
        Assert.Equal(rig.Rules.Repairs.Price(worn), window.RepairAll);
        Assert.Equal(max, after.MaxDurability);
        Assert.Equal(max, after.Durability);
        Assert.Equal(10_000 - window.RepairAll, rig.Purse);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task An_item_a_field_repair_made_unusable_is_not_repaired_back()
    {
        var rig = new TownRig();
        rig.SetPurse(10_000);
        string slot = rig.Held.Inventory.Equipped.Single(e => e.Value.Item.BaseId == "rocket_launcher").Key;
        await rig.Ledger.SetDurability(TownRig.Key, slot, 0, 0);
        rig.Use("engineer_repair");

        var one = await rig.Service.Repair(TownRig.Index, slot);
        var all = await rig.Service.RepairAll(TownRig.Index);

        Assert.False(one!.Ok);
        Assert.Equal("#Descent_Town_" + RepairRefusals.Unusable, rig.Of<TownStatus>().Select(s => s.View().Token).First(t => t.EndsWith(RepairRefusals.Unusable, StringComparison.Ordinal)));
        Assert.Null(all);
        Assert.Equal("#Descent_Town_" + TownRefusals.NothingToRepair, rig.Status.Token);
        Assert.Equal(0, rig.Held.Inventory.Equipped[slot].Item.MaxDurability);
        Assert.Equal(10_000, rig.Purse);
    }

    //-------------------------------------------------------------------------
    // The bench, the crate, the stash
    //-------------------------------------------------------------------------

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_bench_consumes_exactly_its_recipe()
    {
        var rig = new TownRig();
        rig.Give(rig.Roll("scrap", count: 9));
        int before = rig.Craft.Metal(rig.Held);
        int kits = rig.Held.Inventory.Backpack.Values.Count(v => v.Item.BaseId == "repair_kit");
        rig.Use("bench");

        var r = await rig.Service.Craft(TownRig.Index, (int)BenchRecipe.RepairKit, 0);

        Assert.True(r!.Ok, r.Refusal);
        Assert.Equal(before - rig.Rules.Content.EconomySettings.RepairKitCost, rig.Craft.Metal(rig.Held));
        Assert.Equal(kits + 1, rig.Held.Inventory.Backpack.Values.Count(v => v.Item.BaseId == "repair_kit"));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_bench_short_of_metal_refuses_and_consumes_nothing()
    {
        var rig = new TownRig();
        rig.Give(rig.Roll("scrap", count: 2));
        rig.Use("bench");

        var r = await rig.Service.Craft(TownRig.Index, (int)BenchRecipe.RepairKit, 0);

        Assert.False(r!.Ok);
        Assert.Equal("#Descent_Town_" + CraftRefusals.NotEnoughMetal, rig.Status.Token);
        Assert.Equal(2, rig.Craft.Metal(rig.Held));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_crate_charges_the_scouts_price_and_its_item_is_the_rules_seeded_roll_unidentified()
    {
        var rig = new TownRig();
        rig.SetPurse(100_000);
        int mark = rig.Mark;
        rig.Use("scout");
        var crate = Ware(rig, mark);
        var crateItem = (await rig.Ledger.Stock(TownRig.Key, TownService.CrateVendor, rig.Service.Restock)).Items[0].Item;

        var r = await rig.Service.Crate(TownRig.Index);

        Assert.True(r!.Ok, r.Refusal);
        Assert.Equal(100_000 - crate.Price, rig.Purse);
        var opened = rig.Held.Inventory.Backpack.Values.Single(v => v.Id == rig.Status.Item).Item;
        Assert.False(opened.Identified);
        var row = rig.Rules.LevelTable(1);
        var ctx = new ItemRollContext(rig.Held.Sheet.ClassId, rig.Held.Sheet.Level, 0, row.ItemLevel, 0, row.LootTable, "crate");
        var replay = rig.Rules.OpenCrate(rig.Rules.ItemRules.Codec.ToRolled(crateItem), ctx, rig.Ledger.CrateSeed(crateItem));
        Assert.Equal(Convert.ToHexString(replay.Produced[0].Instance), Convert.ToHexString(rig.Rules.ItemRules.Codec.ToRolled(opened).Instance));
        Assert.DoesNotContain(rig.Held.Inventory.Backpack.Values, v => v.Item.BaseId == crateItem.BaseId);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_stash_keeps_an_item_across_a_level_change()
    {
        var rig = new TownRig();
        var item = rig.Roll("cap", seed: 77);
        int slot = rig.Give(item);
        rig.Use("stash");

        Assert.True((await rig.Service.StashIn(TownRig.Index, slot))!.Ok);
        rig.Service.OnLevelShutdown();
        rig.Ledger.NewLevel();
        rig.Service.Place(TownRig.Town);
        rig.Use("stash");
        int stashSlot = rig.Held.Stash.Slots.Single(p => p.Value.Item.Seed == item.Seed).Key;
        var r = await rig.Service.StashOut(TownRig.Index, stashSlot);

        Assert.True(r!.Ok, r.Refusal);
        Assert.Empty(rig.Held.Stash.Slots);
        Assert.Contains(rig.Held.Inventory.Backpack.Values, v => v.Item.Seed == item.Seed);
    }

    /// <summary>The Lost &amp; Found (R5, D6): in town, with no window open, the hero reclaims what his lost corpse held, for the fee.</summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_reclaim_verb_takes_back_a_lost_corpse_in_town_for_the_fee()
    {
        var rig = new TownRig();
        var item = rig.Roll("rocket_launcher", 1, 20);
        rig.Give(item);
        Assert.True(await rig.Ledger.RecordDeath(TownRig.Key, default));
        Assert.Single(rig.Ledger.CorpsesLost());
        long fee = rig.Ledger.ReclaimFee(TownRig.Key)!.Value;
        rig.SetPurse(fee + 7);

        var r = await rig.Service.Reclaim(TownRig.Index);

        Assert.True(r!.Ok, r.Refusal);
        Assert.Contains(rig.Held.Inventory.Items, h => h.Item.Seed == item.Seed);
        Assert.Equal(7, rig.Purse);
        Assert.Equal(TownStatusTokens.Done("reclaim"), rig.Status.Token);
    }

    /// <summary>
    /// The whole run (2026-11-14): the reclaim needs no window, and a town status draws only in
    /// one, so a refused reclaim showed the hero nothing at all. With no window open the answer
    /// is a notice too.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_reclaim_refused_with_no_window_open_is_told_as_a_notice()
    {
        var rig = new TownRig();
        rig.Give(rig.Roll("rocket_launcher", 1, 20));
        Assert.True(await rig.Ledger.RecordDeath(TownRig.Key, default));
        Assert.Single(rig.Ledger.CorpsesLost());
        rig.SetPurse(0);

        var r = await rig.Service.Reclaim(TownRig.Index);

        Assert.False(r!.Ok);
        var notice = rig.Of<Notice>().Last();
        Assert.Equal(("#Descent_Town_" + LedgerRefusals.LostAndFoundFee, ""), (notice.Text, notice.Arg));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_reclaim_done_with_no_window_open_is_told_as_a_notice()
    {
        var rig = new TownRig();
        rig.Give(rig.Roll("rocket_launcher", 1, 20));
        Assert.True(await rig.Ledger.RecordDeath(TownRig.Key, default));
        Assert.Single(rig.Ledger.CorpsesLost());
        rig.SetPurse(rig.Ledger.ReclaimFee(TownRig.Key)!.Value);

        var r = await rig.Service.Reclaim(TownRig.Index);

        Assert.True(r!.Ok, r.Refusal);
        Assert.Equal(TownStatusTokens.Done("reclaim"), rig.Of<Notice>().Last().Text);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_verb_answered_in_an_open_window_sends_no_notice()
    {
        var rig = new TownRig();
        rig.Use("saxton");
        int mark = rig.Mark;

        var r = await rig.Service.Sell(TownRig.Index, 0);   // nothing in the backpack: refused

        Assert.False(r!.Ok);
        Assert.Equal((1, 0), (rig.Of<TownStatus>(mark).Count, rig.Of<Notice>(mark).Count));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task The_reclaim_verb_with_nothing_lost_is_refused_in_words()
    {
        var rig = new TownRig();

        var r = await rig.Service.Reclaim(TownRig.Index);

        Assert.False(r!.Ok);
        Assert.Equal("#Descent_Town_" + LedgerRefusals.NothingLost, rig.Status.Token);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_stash_window_carries_the_rules_capacity()
    {
        var rig = new TownRig();
        int mark = rig.Mark;

        rig.Use("stash");

        var window = rig.Of<TownWindow>(mark).Last().View();
        Assert.Equal(rig.Rules.Inventory.StashSlots([rig.Held.Sheet]), window.StashCapacity);
        Assert.Equal(20, window.StashCapacity);
    }
}
