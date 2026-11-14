using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The vendors' half of the in-memory ledger (plan_descent_logic WO8): a restock's stock kept and
/// shrinking as it is bought, rolled from the rules' seed; the buyer's price stats (Haggler) on
/// every price he meets; buy-back at the price it sold for, the last N kept.
/// </summary>
public sealed class VendorLedgerFacts
{
    private static readonly TF2DescentRules R = NewRules();
    private const string A = "76561190000000001";

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private readonly InMemoryItemLedger _ledger = new(R, new NoHooks(), new StartingKit(R));

    private async Task<LedgerHero> Hero(long australium = 100_000)
    {
        var h = (await _ledger.Load(A)).Hero!;
        _ledger.Replace(h with { Inventory = h.Inventory with { Australium = australium } });
        return _ledger.Find(A)!;
    }

    private long Purse => _ledger.Find(A)!.Inventory.Australium;

    private static ItemInstance Roll(string baseId, int tier = 0, int itemLevel = 1, ulong seed = 1) =>
        R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, itemLevel, tier, "base:" + baseId, "craft"), seed);

    private async Task<int> Put(ItemInstance item)
    {
        var floor = _ledger.PlaceOnFloor(item, A, default);
        Assert.True((await _ledger.Claim(A, floor.Id)).Ok);
        return _ledger.Find(A)!.Inventory.Backpack.Single(p => p.Value.Id == floor.Id).Key;
    }

    //-------------------------------------------------------------------------
    // Stock
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_stock_is_the_rules_roll_from_the_seed_of_the_session_vendor_hero_and_restock()
    {
        var h = await Hero();

        var stock = await _ledger.Stock(A, "saxton", 3);

        var rules = R.Vendors.StockOf($"saxton@{h.Sheet.Level}@{h.Sheet.ClassId}", Vendors.StockSeed(_ledger.SessionSeed, "saxton", A, 3));
        Assert.Equal(rules.Select(ItemCodec.Encode).Select(Convert.ToHexString), stock.Items.Select(i => Convert.ToHexString(ItemCodec.Encode(i.Item))));
    }

    [Fact]
    public async Task The_same_restock_shows_the_same_stock_and_the_ids_hold()
    {
        await Hero();

        var first = await _ledger.Stock(A, "saxton", 0);
        var again = await _ledger.Stock(A, "saxton", 0);

        Assert.Equal(first.Items.Select(i => i.Id), again.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_bought_item_leaves_the_stock_until_the_next_restock()
    {
        await Hero();
        var first = await _ledger.Stock(A, "saxton", 0);

        Assert.True((await _ledger.Buy(A, "saxton", first.Items[0].Id)).Ok);
        var after = await _ledger.Stock(A, "saxton", 0);
        var next = await _ledger.Stock(A, "saxton", 1);

        Assert.Equal(first.Items.Count - 1, after.Items.Count);
        Assert.DoesNotContain(after.Items, i => i.Id == first.Items[0].Id);
        Assert.Equal(R.Content.Vendors["saxton"].Stock, next.Items.Count);
        Assert.NotEqual(first.Items.Select(i => i.Item.Seed), next.Items.Select(i => i.Item.Seed));
    }

    [Fact]
    public async Task An_offer_of_an_earlier_restock_cannot_be_bought()
    {
        await Hero();
        var first = await _ledger.Stock(A, "saxton", 0);
        await _ledger.Stock(A, "saxton", 1);

        var buy = await _ledger.Buy(A, "saxton", first.Items[0].Id);

        Assert.False(buy.Ok);
        Assert.Equal(LedgerRefusals.NotInStock, buy.Refusal);
    }

    //-------------------------------------------------------------------------
    // The buyer's price stats
    //-------------------------------------------------------------------------

    [Fact]
    public async Task Haggler_lowers_the_price_the_stock_shows_and_the_buy_charges_it()
    {
        await Hero(100_000);
        var plain = (await _ledger.Stock(A, "saxton", 0)).Items[0];
        _ledger.SetPrices(A, new BuyerPrices(-20, 20));

        var offer = (await _ledger.Stock(A, "saxton", 0)).Items[0];
        var buy = await _ledger.Buy(A, "saxton", offer.Id);

        Assert.True(buy.Ok, buy.Refusal);
        Assert.Equal(R.Pricing.ForBuyer(plain.Price, PriceKind.Buy, new BuyerPrices(-20, 20)), offer.Price);
        Assert.True(offer.Price < plain.Price);
        Assert.Equal(100_000 - offer.Price, Purse);
    }

    [Fact]
    public async Task A_sale_pays_the_sell_price_moved_by_the_sellers_stats()
    {
        await Hero(0);
        var item = Roll("rocket_launcher", 1, 20);
        int slot = await Put(item);
        _ledger.SetPrices(A, new BuyerPrices(0, 20));

        Assert.True((await _ledger.Sell(A, "saxton", slot)).Ok);

        long plain = R.Price(R.ItemRules.Codec.ToRolled(item), PriceKind.Sell);
        Assert.Equal(R.Pricing.ForBuyer(plain, PriceKind.Sell, new BuyerPrices(0, 20)), Purse);
        Assert.True(Purse > plain);
    }

    [Fact]
    public async Task Buy_back_costs_what_the_item_sold_for_even_when_prices_have_moved_since()
    {
        await Hero(0);
        int slot = await Put(Roll("rocket_launcher", 1, 20));
        _ledger.SetPrices(A, new BuyerPrices(0, 20));
        await _ledger.Sell(A, "saxton", slot);
        long soldFor = Purse;
        _ledger.SetPrices(A, BuyerPrices.None);

        Assert.Equal(soldFor, _ledger.BuyBackPrice(A, 0));
        var back = await _ledger.BuyBack(A, "saxton", 0);

        Assert.True(back.Ok, back.Refusal);
        Assert.Equal(0, Purse);
    }

    [Fact]
    public async Task Buy_back_keeps_the_last_sales_up_to_the_rules_count()
    {
        await Hero(0);
        int keep = R.Content.InventorySettings.BuyBack;
        var sold = new List<long>();
        for (int i = 0; i < keep + 2; i++)
        {
            int slot = await Put(Roll("cap", seed: (ulong)(10 + i)));
            sold.Add(_ledger.Find(A)!.Inventory.Backpack[slot].Id);
            Assert.True((await _ledger.Sell(A, "saxton", slot)).Ok);
        }

        Assert.Equal(sold.Skip(2), _ledger.Find(A)!.Inventory.BuyBack.Select(b => b.Id));
    }

    [Fact]
    public async Task The_identify_fee_is_moved_by_the_buyers_stats()
    {
        await Hero(10_000);
        var item = Roll("rocket_launcher", 1, 20) with { Identified = false };
        int slot = await Put(item);
        _ledger.SetPrices(A, new BuyerPrices(-20, 0));

        long quoted = _ledger.Price(A, LedgerPrice.Identify, item)!.Value;
        Assert.True((await _ledger.Identify(A, slot)).Ok);

        Assert.Equal(R.Pricing.ForBuyer(R.Pricing.IdentifyFee(item), PriceKind.Buy, new BuyerPrices(-20, 0)), quoted);
        Assert.Equal(10_000 - quoted, Purse);
    }

    [Fact]
    public async Task The_engineers_repair_of_what_is_worn_is_lossless_and_charges_the_quoted_price()
    {
        var h = await Hero(10_000);
        string slot = h.Inventory.Equipped.Keys.FirstOrDefault() ?? await EquipLauncher();
        var worn = _ledger.Find(A)!.Inventory.Equipped[slot].Item;
        await _ledger.SetDurability(A, slot, 3, worn.MaxDurability);
        var damaged = _ledger.Find(A)!.Inventory.Equipped[slot].Item;
        long quoted = _ledger.Price(A, LedgerPrice.Repair, damaged)!.Value;

        var repair = await _ledger.RepairWorn(A, slot, WornRepair.Town);

        var after = _ledger.Find(A)!.Inventory.Equipped[slot].Item;
        Assert.True(repair.Ok, repair.Refusal);
        Assert.Equal(worn.MaxDurability, after.MaxDurability);
        Assert.Equal(after.MaxDurability, after.Durability);
        Assert.Equal(R.Repairs.Price(damaged), quoted);
        Assert.Equal(10_000 - quoted, Purse);
    }

    [Fact]
    public async Task An_unusable_item_is_quoted_no_repair_and_the_engineer_refuses_it()
    {
        var h = await Hero(10_000);
        string slot = h.Inventory.Equipped.Keys.FirstOrDefault() ?? await EquipLauncher();
        await _ledger.SetDurability(A, slot, 0, 0);
        var unusable = _ledger.Find(A)!.Inventory.Equipped[slot].Item;

        var repair = await _ledger.RepairWorn(A, slot, WornRepair.Town);

        Assert.Null(_ledger.Price(A, LedgerPrice.Repair, unusable));
        Assert.False(repair.Ok);
        Assert.Equal(RepairRefusals.Unusable, repair.Refusal);
        Assert.Equal(0, _ledger.Find(A)!.Inventory.Equipped[slot].Item.MaxDurability);
        Assert.Equal(10_000, Purse);
    }

    private async Task<string> EquipLauncher()
    {
        int slot = _ledger.Find(A)!.Inventory.Backpack.First(p => p.Value.Item.BaseId == "rocket_launcher").Key;
        Assert.True((await _ledger.Equip(A, slot, "primary")).Ok);
        return "primary";
    }
}
