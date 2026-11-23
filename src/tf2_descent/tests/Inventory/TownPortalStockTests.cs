using System.Text;
using System.Text.Json.Nodes;
using SourceSharp.Abi;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Server.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>
/// The Town Portal can be got (the whole run's F14): the Medic, the town's healer, keeps a
/// Teleporter Blueprint in stock at every restock, beside his rolled potions (Diablo's healer
/// sells the scroll with the potions; D16 lists the town portal in the potions row), at its
/// base value.
/// </summary>
public sealed class TownPortalStockTests
{
    private static readonly TF2DescentRules R = NewRules();
    private const string A = "76561190000000001";
    private const string Blueprint = "town_portal";

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    [Fact]
    public async Task A_fresh_heros_medic_has_a_blueprint_in_stock_at_every_restock()
    {
        var ledger = new InMemoryItemLedger(R, new NoHooks(), new StartingKit(R));
        Assert.True((await ledger.Load(A)).Ok);

        for (long restock = 0; restock < 48; restock++)
        {
            var stock = await ledger.Stock(A, "medic", restock);
            Assert.Single(stock.Items, i => i.Item.BaseId == Blueprint);
        }
    }

    [Fact]
    public void The_medics_stock_keeps_its_rolled_potions_beside_the_blueprint()
    {
        var stock = R.Vendors.StockOf("medic@1@soldier", 7);

        Assert.Equal(R.Content.Vendors["medic"].Stock, stock.Count(i => R.Content.ItemBases[i.BaseId].Kind == "potion"));
        Assert.Equal(R.Content.Vendors["medic"].Stock + 1, stock.Count);
    }

    [Fact]
    public async Task A_new_hero_buys_a_blueprint_for_its_base_value()
    {
        var ledger = new InMemoryItemLedger(R, new NoHooks(), new StartingKit(R));
        var h = (await ledger.Load(A)).Hero!;
        long value = R.Content.ItemBases[Blueprint].Value;
        ledger.Replace(h with { Inventory = h.Inventory with { Australium = value } });
        var offer = (await ledger.Stock(A, "medic", 0)).Items.Single(i => i.Item.BaseId == Blueprint);

        var bought = await ledger.Buy(A, "medic", offer.Id);

        Assert.True(bought.Ok, bought.Refusal);
        Assert.Equal(value, offer.Price);
        Assert.Equal(0, ledger.Find(A)!.Inventory.Australium);
        Assert.Contains(ledger.Find(A)!.Inventory.Backpack.Values, held => held.Item.BaseId == Blueprint);
    }

    [Fact]
    public void A_vendor_that_always_stocks_an_unknown_base_is_a_load_error()
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        source.TryRead(VendorTable.TableName, out var bytes);
        var node = JsonNode.Parse(bytes)!;
        node[2]!["always"] = new JsonArray("no_such_base");
        source.Set(VendorTable.TableName, Encoding.UTF8.GetBytes(node.ToJsonString()));

        var errors = new ContentLoader().Load(source).Errors;

        var e = Assert.Single(errors);
        Assert.Equal(VendorTable.TableName, e.Table);
        Assert.Equal(ContentRule.DanglingReference, e.Rule);
    }
}
