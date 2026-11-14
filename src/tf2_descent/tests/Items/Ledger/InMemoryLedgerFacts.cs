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

    public InMemoryLedgerFacts() => _ledger = new InMemoryItemLedger(R, new Hooks(Changes), new StartingKit(R));

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
        var other = new InMemoryItemLedger(R, new Hooks(), new StartingKit(R));
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
        int belted = _ledger.Find(A)!.Inventory.Belt.Count;

        bool died = await _ledger.RecordDeath(A, default);
        var bare = _ledger.Find(A)!;
        var loot = await _ledger.LootCorpse(A);

        Assert.True(died);
        Assert.Empty(bare.Inventory.Items);
        Assert.Equal(0, bare.Inventory.Australium);
        Assert.Equal((true, carried), (loot.Ok, loot.Recovered));
        // Each back where it was carried (InventoryRules.Recover): the kit's potions on the belt, its weapons in the backpack.
        Assert.Equal(carried, _ledger.Find(A)!.Inventory.Items.Count());
        Assert.Equal(belted, _ledger.Find(A)!.Inventory.Belt.Count);
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
    [Fact]
    public async Task Collected_Australium_goes_into_the_purse_and_is_told_once()
    {
        await _ledger.Load(A);
        long before = _ledger.Find(A)!.Inventory.Australium;
        Changes.Seen.Clear();

        var collect = await _ledger.CollectAustralium(A, 37);

        Assert.True(collect.Ok, collect.Refusal);
        Assert.Equal(before + 37, _ledger.Find(A)!.Inventory.Australium);
        var change = Assert.Single(Changes.Seen);
        Assert.Equal((before, before + 37), (change.AustraliumBefore, change.AustraliumAfter));
    }

    [Fact]
    public async Task Collecting_no_Australium_is_refused_and_changes_nothing()
    {
        await _ledger.Load(A);
        string before = Snapshot(A);

        var collect = await _ledger.CollectAustralium(A, 0);

        Assert.Equal(LedgerRefusals.BadAmount, collect.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task A_kill_potion_lies_on_the_floor_for_its_hero_and_replays()
    {
        await _ledger.Load(A);

        var drop = await _ledger.PotionDrop(A, "health_small", 9, 2, new Vector(1, 2, 3));

        Assert.True(drop.Ok, drop.Refusal);
        var potion = Assert.Single(drop.Items);
        Assert.Equal(("health_small", A, new Vector(1, 2, 3)), (potion.Item.BaseId, potion.ForHero, potion.Position));
        Assert.Contains(_ledger.Floor, f => f.Id == potion.Id);
        Assert.True(R.Replays(R.ItemRules.Codec.ToRolled(potion.Item)), "a dropped potion does not replay");
    }

    [Fact]
    public async Task A_containers_item_lies_on_the_floor_for_its_hero_at_the_rules_tier_and_replays()
    {
        await _ledger.Load(A);

        var drop = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 12345, 2, null, 1, new Vector(4, 5, 6)));

        Assert.True(drop.Ok, drop.Refusal);
        var item = Assert.Single(drop.Items);
        Assert.Equal((A, new Vector(4, 5, 6), 2), (item.ForHero, item.Position, item.Item.Rarity));
        Assert.Contains(_ledger.Floor, f => f.Id == item.Id);
        Assert.True(R.Replays(R.ItemRules.Codec.ToRolled(item.Item)), "a container's item does not replay");
    }

    [Fact]
    public async Task A_containers_roll_is_the_same_item_from_the_same_seed()
    {
        await _ledger.Load(A);

        var one = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 777, 1, null, 1, default));
        var two = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 777, 1, null, 1, default));
        var other = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 778, 1, null, 1, default));

        string Bytes(DropResult d) => Convert.ToHexString(R.ItemRules.Codec.ToRolled(d.Items[0].Item).Instance);
        Assert.Equal(Bytes(one), Bytes(two));
        Assert.NotEqual(Bytes(one), Bytes(other));
    }

    [Fact]
    public async Task A_containers_potion_lies_on_the_floor_for_its_hero()
    {
        await _ledger.Load(A);

        var drop = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 9, -1, "mana_small", 1, default));

        Assert.Equal("mana_small", Assert.Single(drop.Items).Item.BaseId);
    }

    [Fact]
    public async Task A_container_roll_of_nothing_puts_nothing_on_the_floor()
    {
        await _ledger.Load(A);

        var drop = await _ledger.ContainerDrop(new ContainerDrop(A, "m7", 9, -1, null, 1, default));

        Assert.True(drop.Ok);
        Assert.Empty(drop.Items);
        Assert.Empty(_ledger.Floor);
    }

    [Fact]
    public async Task A_potion_of_an_unknown_base_is_refused()
    {
        await _ledger.Load(A);

        var drop = await _ledger.PotionDrop(A, "no_such_potion", 9, 2, default);

        Assert.Equal(LedgerRefusals.UnknownBase, drop.Refusal);
        Assert.Empty(_ledger.Floor);
    }
}
