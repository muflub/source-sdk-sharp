using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// The hosted ledger's SDK 2.7 calls against a host that keeps items as the hosting service does
/// (<see cref="FakeHostItems"/> asks the rules' 1.1 members as DescentLedger does): the three
/// containers on load, Move for equip, unequip and the belt, Consume for a drink and a scroll, Wear,
/// both repairs, and OpenContainer. After each, the ledger holds what the host holds, item for item,
/// slot for slot, byte for byte.
/// </summary>
public sealed class HostedInventoryFacts
{
    private static readonly TF2DescentRules R = ItemFixtures.NewRules();
    private const string A = "76561190000000001";

    private readonly FakeHostItems _host = new(R);
    private readonly HostedItemLedger _ledger;
    private uint _kills;

    public HostedInventoryFacts()
    {
        _host.AddCharacter(A, R.Heroes.New("soldier", "a"));
        _ledger = new HostedItemLedger(R, new NoHooks(), _host, new NoLog());
    }

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }

    private LedgerHero Held => _ledger.Find(A)!;

    private static ItemInstance Roll(string baseId, ulong seed = 1) =>
        R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, 1, 0, "base:" + baseId, "craft"), seed);

    private static ItemInstance Potions(int count, ulong seed = 1) => Roll("health_small", seed) with { Count = count };

    /// <summary>An item into the hero's backpack through a reserve drop and a claim; its ledger id.</summary>
    private async Task<long> Put(ItemInstance item)
    {
        _host.Reserve(A, 0, item);
        var drop = await _ledger.KillDrop(new KillDrop(A, 0, ++_kills, "soldier", 1, default));
        var floor = Assert.Single(drop.Items);
        Assert.True((await _ledger.Claim(A, floor.Id)).Ok);
        return floor.Id;
    }

    private int SlotOf(long id) => Held.Inventory.Backpack.Single(p => p.Value.Id == id).Key;

    private HostItem OnHost(long id) => _host.Items[_ledger.HostIdOf(id)!];

    /// <summary>Every item the hero carries, as (container, slot, host id, bytes): the ledger's view and the host's must be equal.</summary>
    private IReadOnlyList<string> Mirror()
    {
        var names = R.EquipSlots.ToList();
        var inv = Held.Inventory;
        return [.. inv.Backpack.Select(p => Row(HostContainer.Backpack, p.Key, p.Value))
            .Concat(inv.Equipped.Select(p => Row(HostContainer.Equipped, names.IndexOf(p.Key), p.Value)))
            .Concat(inv.Belt.Select(p => Row(HostContainer.Belt, p.Key, p.Value)))
            .Order(StringComparer.Ordinal)];
    }

    private string Row(HostContainer c, int slot, Held h) => $"{c}:{slot}:{_ledger.HostIdOf(h.Id)}:{Convert.ToHexString(ItemCodec.Encode(h.Item))}";

    private IReadOnlyList<string> Hosted() =>
        [.. _host.Owned(HostOwnerKind.Character, A).Select(i => $"{i.Container}:{i.Slot}:{i.Id}:{Convert.ToHexString(i.Instance.Data)}").Order(StringComparer.Ordinal)];

    private void AssertMirrored()
    {
        var hosted = Hosted();
        Assert.NotEmpty(hosted);
        Assert.Equal(hosted, Mirror());
    }

    //-------------------------------------------------------------------------
    // Load: the three containers
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_hero_the_host_kitted_out_loads_with_his_kit_worn_and_his_potions_belted()
    {
        foreach (var k in R.StartingKit("soldier", R.SheetCodec.Write(R.Heroes.New("soldier", "a")), 3))
            _host.Carry(A, R.ItemRules.Codec.Instance(k.Item), (HostContainer)(int)k.Container, k.Slot);

        await _ledger.Load(A);

        Assert.Equal(["melee", "primary", "secondary"], Held.Inventory.Equipped.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["health_small", "health_small", "mana_small"], Held.Inventory.Belt.Values.Select(h => h.Item.BaseId));
        Assert.Empty(Held.Inventory.Backpack);
        AssertMirrored();
    }

    //-------------------------------------------------------------------------
    // Move: equip, unequip, the belt
    //-------------------------------------------------------------------------

    [Fact]
    public async Task Equipping_over_a_worn_item_puts_the_old_one_where_the_new_one_was_on_both_sides()
    {
        await _ledger.Load(A);
        long first = await Put(Roll("rocket_launcher", 1));
        long second = await Put(Roll("rocket_launcher", 2));
        Assert.True((await _ledger.Equip(A, SlotOf(first), "primary")).Ok);
        int cell = SlotOf(second);

        var swap = await _ledger.Equip(A, cell, "primary");

        Assert.True(swap.Ok, swap.Refusal);
        Assert.Equal(second, Held.Inventory.Equipped["primary"].Id);
        Assert.Equal(cell, SlotOf(first));
        AssertMirrored();
    }

    [Fact]
    public async Task Unequipping_moves_the_item_into_the_backpack_cell_the_rules_chose()
    {
        await _ledger.Load(A);
        long id = await Put(Roll("shovel"));
        Assert.True((await _ledger.Equip(A, SlotOf(id), "melee")).Ok);

        var off = await _ledger.Unequip(A, "melee");

        Assert.True(off.Ok, off.Refusal);
        Assert.Equal(HostContainer.Backpack, OnHost(id).Container);
        AssertMirrored();
    }

    [Fact]
    public async Task A_potion_moved_to_the_belt_is_on_the_hosts_belt()
    {
        await _ledger.Load(A);
        long id = await Put(Potions(2));

        var belt = await _ledger.ToBelt(A, SlotOf(id), 1);

        Assert.True(belt.Ok, belt.Refusal);
        Assert.Equal((HostContainer.Belt, 1), (OnHost(id).Container, OnHost(id).Slot));
        AssertMirrored();
    }

    [Fact]
    public async Task A_potion_onto_a_belt_stack_is_not_asked_of_the_host_which_has_no_merge()
    {
        await _ledger.Load(A);
        long a = await Put(Potions(1, 1));
        long b = await Put(Potions(1, 2));
        Assert.True((await _ledger.ToBelt(A, SlotOf(a), 0)).Ok);
        _host.Calls.Clear();

        var merge = await _ledger.ToBelt(A, SlotOf(b), 0);

        Assert.Equal(LedgerRefusals.HostUnsupported, merge.Refusal);
        Assert.Empty(_host.Calls);
        AssertMirrored();
    }

    [Fact]
    public async Task Taking_a_potion_off_the_belt_lands_it_in_the_backpack_on_both_sides()
    {
        await _ledger.Load(A);
        long id = await Put(Potions(2));
        Assert.True((await _ledger.ToBelt(A, SlotOf(id), 2)).Ok);

        var off = await _ledger.FromBelt(A, 2);

        Assert.True(off.Ok, off.Refusal);
        Assert.Equal(HostContainer.Backpack, OnHost(id).Container);
        AssertMirrored();
    }

    [Fact]
    public async Task A_move_the_host_refuses_changes_nothing_and_says_why()
    {
        await _ledger.Load(A);
        long id = await Put(Potions(2));
        _host.Failing.Add("move");
        var before = Mirror();

        var belt = await _ledger.ToBelt(A, SlotOf(id), 0);

        Assert.StartsWith(LedgerRefusals.HostPrefix, belt.Refusal);
        Assert.Equal(before, Mirror());
    }

    //-------------------------------------------------------------------------
    // Consume
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_drink_from_the_belt_uses_one_on_the_host()
    {
        await _ledger.Load(A);
        long id = await Put(Potions(3));
        Assert.True((await _ledger.ToBelt(A, SlotOf(id), 0)).Ok);

        var drink = await _ledger.Drink(A, 0);

        Assert.True(drink.Ok, drink.Refusal);
        Assert.Equal((2, 2), (OnHost(id).Count, Held.Inventory.Belt[0].Item.Count));
        AssertMirrored();
    }

    [Fact]
    public async Task Drinking_the_last_ends_the_item_on_the_host()
    {
        await _ledger.Load(A);
        long id = await Put(Potions(1));
        long keep = await Put(Roll("shovel"));
        Assert.True((await _ledger.ToBelt(A, SlotOf(id), 0)).Ok);
        string hostId = _ledger.HostIdOf(id)!;

        var drink = await _ledger.Drink(A, 0);

        Assert.True(drink.Ok, drink.Refusal);
        Assert.False(_host.Items.ContainsKey(hostId));
        Assert.False(Held.Inventory.Belt.ContainsKey(0));
        Assert.Equal([id], drink.Change!.Gone);
        Assert.NotEqual(0, keep);
        AssertMirrored();
    }

    [Fact]
    public async Task A_drink_that_empties_a_belt_slot_moves_the_refill_onto_the_hosts_belt()
    {
        await _ledger.Load(A);
        long first = await Put(Potions(1, 1));
        long refill = await Put(Potions(2, 2));
        Assert.True((await _ledger.ToBelt(A, SlotOf(first), 0)).Ok);

        var drink = await _ledger.Drink(A, 0);

        Assert.True(drink.Ok, drink.Refusal);
        Assert.Equal(refill, Held.Inventory.Belt[0].Id);
        Assert.Equal((HostContainer.Belt, 0), (OnHost(refill).Container, OnHost(refill).Slot));
        AssertMirrored();
    }

    [Fact]
    public async Task A_scroll_used_from_the_backpack_uses_one_on_the_host()
    {
        await _ledger.Load(A);
        long id = await Put(Roll("town_portal") with { Count = 2, Durability = 0, MaxDurability = 0 });

        var used = await _ledger.Consume(A, SlotOf(id));

        Assert.True(used.Ok, used.Refusal);
        Assert.Equal(1, OnHost(id).Count);
        AssertMirrored();
    }

    //-------------------------------------------------------------------------
    // Wear
    //-------------------------------------------------------------------------

    [Fact]
    public async Task Wear_goes_to_the_host_as_each_items_uses_or_hits_in_one_write()
    {
        await _ledger.Load(A);
        long launcher = await Put(Roll("rocket_launcher"));
        long cap = await Put(Roll("cap"));
        Assert.True((await _ledger.Equip(A, SlotOf(launcher), "primary")).Ok);
        Assert.True((await _ledger.Equip(A, SlotOf(cap), "hat")).Ok);
        int hits = 2 * R.Content.DurabilitySettings.ArmourHitsPerPoint;

        var worn = await _ledger.Wear(A, [new WearEvents(launcher, 9, 4), new WearEvents(cap, 3, hits)]);

        Assert.True(worn.Ok, worn.Refusal);
        var batch = Assert.Single(_host.WearBatches);
        Assert.Equal([new HostWear(_ledger.HostIdOf(launcher)!, 9), new HostWear(_ledger.HostIdOf(cap)!, hits)], batch.OrderBy(w => w.Amount == 9 ? 0 : 1));
        Assert.True(Held.Inventory.Equipped["primary"].Item.Durability < Roll("rocket_launcher").Durability);
        AssertMirrored();
    }

    [Fact]
    public async Task Wear_no_event_counts_for_is_not_sent()
    {
        await _ledger.Load(A);
        long launcher = await Put(Roll("rocket_launcher"));
        Assert.True((await _ledger.Equip(A, SlotOf(launcher), "primary")).Ok);

        var worn = await _ledger.Wear(A, [new WearEvents(launcher, 0, 5)]);

        Assert.True(worn.Ok, worn.Refusal);
        Assert.Empty(_host.WearBatches);
    }

    //-------------------------------------------------------------------------
    // Repairs
    //-------------------------------------------------------------------------

    private async Task<long> WornLauncher(int durability)
    {
        await _ledger.Load(A);
        long id = await Put(Roll("rocket_launcher"));
        Assert.True((await _ledger.Equip(A, SlotOf(id), "primary")).Ok);
        var damaged = Held.Inventory.Equipped["primary"].Item with { Durability = durability };
        var host = OnHost(id);
        _host.Items[host.Id] = host with { Instance = new OpaquePayload(ItemCodec.Encode(damaged), ItemCodec.Schema) };
        _ledger.Replace(Held with { Inventory = Held.Inventory with { Equipped = Held.Inventory.Equipped.SetItem("primary", new Held(id, damaged)) } });
        return id;
    }

    private async Task<long> PutKit() =>
        await Put(Roll("repair_kit") with { Durability = 0, MaxDurability = 0 });

    [Fact]
    public async Task The_engineers_repair_of_what_is_worn_is_the_hosts_town_repair_and_is_charged()
    {
        long id = await WornLauncher(10);
        long price = R.Repairs.Price(Held.Inventory.Equipped["primary"].Item);
        _host.Australium[A] = price + 3;
        _ledger.Replace(Held with { Inventory = Held.Inventory with { Australium = price + 3 } });

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Town);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal((3L, 3L), (_host.Australium[A], Held.Inventory.Australium));
        Assert.Equal(Held.Inventory.Equipped["primary"].Item.MaxDurability, Held.Inventory.Equipped["primary"].Item.Durability);
        Assert.Equal([null], _host.KitsNamed);
        Assert.NotEqual(0, id);
        AssertMirrored();
    }

    [Fact]
    public async Task A_town_repair_away_from_the_hub_is_refused_by_the_host_and_changes_nothing()
    {
        await WornLauncher(10);
        _host.InHub = false;
        _host.Australium[A] = 1000;
        _ledger.Replace(Held with { Inventory = Held.Inventory with { Australium = 1000 } });
        var before = Mirror();

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Town);

        Assert.Equal($"{LedgerRefusals.HostPrefix}{HostError.NotAHub}:not_a_hub", r.Refusal);
        Assert.Equal(before, Mirror());
        Assert.Equal(1000, Held.Inventory.Australium);
    }

    [Fact]
    public async Task A_field_repair_names_the_kit_and_the_host_uses_it_up_with_the_repair()
    {
        long id = await WornLauncher(0);
        long kit = await PutKit();
        string kitOnHost = _ledger.HostIdOf(kit)!;

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Field);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal([kitOnHost], _host.KitsNamed);
        Assert.False(_host.Items.ContainsKey(kitOnHost));
        Assert.DoesNotContain(Held.Inventory.Backpack.Values, h => h.Id == kit);
        Assert.True(Held.Inventory.Equipped["primary"].Item.Durability > 0);
        Assert.Equal(id, Held.Inventory.Equipped["primary"].Id);
        AssertMirrored();
    }

    [Fact]
    public async Task A_field_repair_away_from_the_hub_is_the_hosts_too()
    {
        await WornLauncher(0);
        await PutKit();
        _host.InHub = false;

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Field);

        Assert.True(r.Ok, r.Refusal);
        AssertMirrored();
    }

    [Fact]
    public async Task The_waived_town_repair_has_no_host_call()
    {
        await WornLauncher(10);
        _host.Calls.Clear();

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.TownUncharged);

        Assert.Equal(LedgerRefusals.HostUnsupported, r.Refusal);
        Assert.Empty(_host.Calls);
    }

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    /// <summary>The first container number whose roll gives the hero <paramref name="items"/> items.</summary>
    private static uint SeqGiving(int items)
    {
        var sheet = R.SheetCodec.Write(R.Heroes.New("soldier", "a"));
        for (uint seq = 0; seq < 500; seq++)
            if (R.ContainerDrops(42, seq, 2, sheet).Count == items)
                return seq;
        throw new InvalidOperationException($"no container of 500 gives {items} items");
    }

    private static ContainerDrop Opened(uint seq) => new(A, "m", 0, -1, null, 2, new Vector(1, 2, 3)) { Seq = seq };

    [Fact]
    public async Task Opening_a_container_places_what_the_host_minted_for_the_opener_and_he_can_claim_it()
    {
        await _ledger.Load(A);
        uint seq = SeqGiving(1);

        var drop = await _ledger.ContainerDrop(Opened(seq));

        var floor = Assert.Single(drop.Items);
        var expected = Assert.Single(R.ContainerDrops(42, seq, 2, R.SheetCodec.Write(R.Heroes.New("soldier", "a"))));
        Assert.Equal(Convert.ToHexString(expected.Instance), Convert.ToHexString(ItemCodec.Encode(floor.Item)));
        Assert.Equal(A, floor.ForHero);
        Assert.True((await _ledger.Claim(A, floor.Id)).Ok);
        AssertMirrored();
    }

    [Fact]
    public async Task A_second_open_of_the_same_container_is_refused_already_opened()
    {
        await _ledger.Load(A);
        uint seq = SeqGiving(1);
        Assert.True((await _ledger.ContainerDrop(Opened(seq))).Ok);

        var again = await _ledger.ContainerDrop(Opened(seq));

        Assert.Equal($"{LedgerRefusals.HostPrefix}{HostError.Rejected}:already_opened", again.Refusal);
        Assert.Single(_ledger.Floor);
    }

    [Fact]
    public async Task An_empty_container_is_opened_once_all_the_same()
    {
        await _ledger.Load(A);
        uint seq = SeqGiving(0);

        var first = await _ledger.ContainerDrop(Opened(seq));
        var again = await _ledger.ContainerDrop(Opened(seq));

        Assert.True(first.Ok, first.Refusal);
        Assert.Empty(first.Items);
        Assert.EndsWith(":already_opened", again.Refusal);
    }
}
