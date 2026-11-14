using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>The ledger facts against the in-memory ledger (unhosted, E8), and what only it does: the starting kit, the dev mint, equipping, the belt, a map change.</summary>
public sealed class InMemoryLedgerFacts : LedgerFacts
{
    private readonly InMemoryItemLedger _ledger;

    public InMemoryLedgerFacts() => _ledger = new InMemoryItemLedger(R, new Hooks(Changes), new StartingKit());

    protected override ItemLedgerBase Ledger => _ledger;

    protected override Task<FloorItem> DropFor(string hero, ItemInstance item) =>
        Task.FromResult(_ledger.PlaceOnFloor(item, hero, default));

    protected override Task SetAustralium(string hero, long amount)
    {
        var h = _ledger.Find(hero)!;
        _ledger.Replace(h with { Inventory = h.Inventory with { Australium = amount } });
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_new_hero_starts_with_the_starting_kit()
    {
        var load = await _ledger.Load(A);

        var h = load.Hero!;
        Assert.Equal(["rocket_launcher", "shotgun", "shovel"], h.Inventory.Backpack.Values.Select(v => v.Item.BaseId));
        Assert.Equal(["health_small", "health_small", "mana_small"], h.Inventory.Belt.Values.Select(v => v.Item.BaseId));
        Assert.All(h.Inventory.Items, i => Assert.True(i.Item.Identified));
        Assert.Equal(6, Assert.Single(Changes.Seen).Added.Count);
    }

    [Fact]
    public async Task The_stock_launcher_of_the_kit_equips_and_unequips()
    {
        await _ledger.Load(A);

        var equip = await _ledger.Equip(A, 0, "primary");
        var equipped = _ledger.Find(A)!.Inventory.Equipped["primary"];
        var unequip = await _ledger.Unequip(A, "primary");

        Assert.True(equip.Ok, equip.Refusal);
        Assert.Equal("rocket_launcher", equipped.Item.BaseId);
        Assert.True(unequip.Ok, unequip.Refusal);
        Assert.Empty(_ledger.Find(A)!.Inventory.Equipped);
    }

    [Fact]
    public async Task Drinking_a_belt_potion_uses_it_up()
    {
        await _ledger.Load(A);
        long potion = _ledger.Find(A)!.Inventory.Belt[0].Id;

        var drink = await _ledger.Drink(A, 0);

        Assert.True(drink.Ok, drink.Refusal);
        Assert.Equal([potion], drink.Change!.Gone);
        Assert.False(_ledger.Find(A)!.Inventory.Belt.ContainsKey(0));
    }

    [Fact]
    public async Task Give_mints_a_rolled_item_of_the_base_into_the_backpack()
    {
        await _ledger.Load(A);

        var give = await _ledger.Give(A, "black_box", 1, 12);

        Assert.True(give.Ok, give.Refusal);
        var added = Assert.Single(give.Change!.Added);
        Assert.Equal(("black_box", 1, 12), (added.Item.BaseId, added.Item.Rarity, added.Item.ItemLevel));
        Assert.True(R.Replays(R.ItemRules.Codec.ToRolled(added.Item)), "a given item does not replay");
    }

    [Fact]
    public async Task Give_refuses_an_unknown_base()
    {
        await _ledger.Load(A);

        var give = await _ledger.Give(A, "no_such_base", 0, 1);

        Assert.Equal(LedgerRefusals.UnknownBase, give.Refusal);
    }

    [Fact]
    public async Task A_kill_drop_is_the_same_item_for_the_same_session_kill_and_hero()
    {
        var other = new InMemoryItemLedger(R, new Hooks(), new StartingKit());
        var kill = new KillDrop(A, 2, 41, "soldier", 4, default);
        await other.Load(A);
        await other.Give(A, "shovel", 1, 5);           // other rolls first: the kill's seed must not count them

        var one = (await _ledger.KillDrop(kill)).Items[0].Item;
        var two = (await other.KillDrop(kill)).Items[0].Item;
        var forB = (await other.KillDrop(kill with { Hero = B })).Items[0].Item;

        Assert.Equal(ItemCodec.Encode(one), ItemCodec.Encode(two));
        Assert.NotEqual(ItemCodec.Encode(one), ItemCodec.Encode(forB));
    }

    [Fact]
    public async Task A_boss_drops_its_table_for_every_hero()
    {
        await _ledger.Load(A);
        await _ledger.Load(B);

        var drop = await _ledger.BossDrop(new BossDrop([A, B], 9, "sir_nukesalot", 5, default));

        // The size of the boss's table is the rules' to say: ask them for the same boss.
        var row = R.LevelTable(5);
        var ctx = new ItemRollContext("soldier", 1, 5, row.ItemLevel, -1, row.LootTable, "boss:sir_nukesalot");
        int table = R.BossDrops(ctx, "sir_nukesalot", 1).Count;

        Assert.True(drop.Ok);
        Assert.True(table > 0, "the boss's table is empty: nothing to count");
        Assert.Equal(table, drop.Items.Count(i => i.ForHero == A));
        Assert.Equal(table, drop.Items.Count(i => i.ForHero == B));
    }

    [Fact]
    public async Task Heroes_survive_a_map_change_and_the_floor_does_not()
    {
        await _ledger.Load(A);
        var given = await _ledger.Give(A, "shovel", 0, 1);
        await DropFor(A, Roll("cap"));
        var before = Snapshot(A);

        _ledger.NewLevel();
        var again = await _ledger.Load(A);   // the player's client is active again on the new map

        Assert.True(given.Ok);
        Assert.Equal(before, Snapshot(A));
        Assert.Same(again.Hero, _ledger.Find(A));
        Assert.Empty(_ledger.Floor);
    }

    [Fact]
    public async Task A_death_puts_everything_carried_on_the_corpse_and_looting_brings_it_back()
    {
        await _ledger.Load(A);
        await SetAustralium(A, 77);
        int carried = _ledger.Find(A)!.Inventory.Items.Count();

        bool died = await _ledger.RecordDeath(A, default);
        var bare = _ledger.Find(A)!;
        var loot = await _ledger.LootCorpse(A);

        Assert.True(died);
        Assert.Empty(bare.Inventory.Items);
        Assert.Equal(0, bare.Inventory.Australium);
        Assert.Equal((true, carried), (loot.Ok, loot.Recovered));
        Assert.Equal(carried, _ledger.Find(A)!.Inventory.Backpack.Count);
        Assert.Equal(77, _ledger.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public async Task A_stack_merges_metal_and_the_merged_stack_is_gone()
    {
        await _ledger.Load(A);
        long a = await Put(A, Roll("scrap") with { Count = 10 });
        long b = await Put(A, Roll("scrap", seed: 2) with { Count = 5 });

        var stack = await _ledger.Stack(A, SlotOf(A, a), SlotOf(A, b));

        Assert.True(stack.Ok, stack.Refusal);
        Assert.Equal([a], stack.Change!.Gone);
        Assert.Equal(15, _ledger.Find(A)!.Inventory.Backpack.Values.Single(h => h.Id == b).Item.Count);
    }
}
