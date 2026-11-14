using SourceSharp.Abi;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// HE8 and HE9 through the in-memory ledger: a consumable used one at a time, wear applied by
/// the rules in one write, the Engineer's repair (lossless, charged) and the field repair
/// (lowering the maximum, never destroying), the dev durability set; and the hosted ledger's
/// refusal of each, the host having no call for them.
/// </summary>
public sealed class WearLedgerFacts
{
    private static readonly TF2.Descent.Rules.TF2DescentRules R = ItemFixtures.NewRules();
    private const string A = "76561190000000001";

    private sealed class Recorder : IItemLedgerHook
    {
        public List<ItemsChanged> Seen { get; } = [];
        public void OnItemsChanged(ItemsChanged change) => Seen.Add(change);
    }

    private sealed class Hooks(IItemLedgerHook hook) : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [hook];
        public void Each(string what, Action<IItemLedgerHook> call) => call(hook);
    }

    private readonly Recorder _changes = new();
    private readonly InMemoryItemLedger _ledger;

    public WearLedgerFacts() => _ledger = new InMemoryItemLedger(R, new Hooks(_changes), new StartingKit(R));

    private LedgerHero Held => _ledger.Find(A)!;

    /// <summary>The kit loaded and its launcher worn in the primary slot.</summary>
    private async Task<Held> Launcher()
    {
        await _ledger.Load(A);
        Assert.True((await _ledger.Equip(A, 0, "primary")).Ok);
        return Held.Inventory.Equipped["primary"];
    }

    private void Plant(string slot, ItemInstance item, long australium = 0) =>
        _ledger.Replace(Held with
        {
            Inventory = Held.Inventory with { Equipped = Held.Inventory.Equipped.SetItem(slot, new Held(Held.Inventory.Equipped[slot].Id, item)), Australium = australium },
        });

    [Fact]
    public async Task A_consumed_backpack_stack_loses_one_and_its_last_leaves_the_world()
    {
        await _ledger.Load(A);
        var floor = _ledger.PlaceOnFloor(ItemFixtures.Item("town_portal", 0, 1) with { Count = 2, Durability = 0, MaxDurability = 0 }, A, default);
        Assert.True((await _ledger.Claim(A, floor.Id)).Ok);
        int slot = Held.Inventory.Backpack.Single(p => p.Value.Id == floor.Id).Key;

        var once = await _ledger.Consume(A, slot);
        var twice = await _ledger.Consume(A, slot);

        Assert.Equal(1, Assert.Single(once.Change!.Changed).Item.Count);
        Assert.Equal([floor.Id], twice.Change!.Gone);
        Assert.False(Held.Inventory.Backpack.ContainsKey(slot));
    }

    [Fact]
    public async Task Nine_shots_of_a_launcher_cost_two_points_and_keep_one_use_toward_the_next_in_one_write()
    {
        var worn = await Launcher();
        _changes.Seen.Clear();

        var r = await _ledger.Wear(A, [new WearEvents(worn.Id, Uses: 9, Hits: 0)]);

        var after = Held.Inventory.Equipped["primary"];
        Assert.True(r.Ok, r.Refusal);
        Assert.Equal((58, 60, 100), (after.Item.Durability, after.Item.MaxDurability, after.Item.WearUses));
        Assert.Equal(worn.Id, after.Id);
        Assert.Single(_changes.Seen);
    }

    [Fact]
    public async Task Hits_wear_armour_and_not_a_weapon()
    {
        var worn = await Launcher();
        var cap = _ledger.PlaceOnFloor(ItemFixtures.Item("cap", 0, 1) with { Durability = 40, MaxDurability = 40 }, A, default);
        Assert.True((await _ledger.Claim(A, cap.Id)).Ok);
        Assert.True((await _ledger.Equip(A, Held.Inventory.Backpack.Single(p => p.Value.Id == cap.Id).Key, "hat")).Ok);

        await _ledger.Wear(A, [new WearEvents(worn.Id, 0, 6), new WearEvents(cap.Id, 0, 6)]);

        Assert.Equal(60, Held.Inventory.Equipped["primary"].Item.Durability);
        Assert.Equal(40 - 6 / R.Content.DurabilitySettings.ArmourHitsPerPoint, Held.Inventory.Equipped["hat"].Item.Durability);
    }

    [Fact]
    public async Task Wear_on_an_item_taken_off_before_the_write_still_counts()
    {
        var worn = await Launcher();
        Assert.True((await _ledger.Unequip(A, "primary")).Ok);

        await _ledger.Wear(A, [new WearEvents(worn.Id, 8, 0)]);

        Assert.Equal(58, Held.Inventory.Backpack.Values.Single(h => h.Id == worn.Id).Item.Durability);
    }

    [Fact]
    public async Task The_engineers_repair_restores_the_maximum_untouched_and_charges_its_price()
    {
        var worn = await Launcher();
        var damaged = worn.Item with { Durability = 10 };
        long price = R.Repairs.Price(damaged);
        Plant("primary", damaged, australium: price + 5);

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Town);

        Assert.True(r.Ok, r.Refusal);
        Assert.True(price > 0);
        Assert.Equal((60, 60), (Held.Inventory.Equipped["primary"].Item.Durability, Held.Inventory.Equipped["primary"].Item.MaxDurability));
        Assert.Equal(5, Held.Inventory.Australium);
    }

    [Fact]
    public async Task The_engineers_repair_without_the_price_is_refused_and_changes_nothing()
    {
        var worn = await Launcher();
        Plant("primary", worn.Item with { Durability = 10 }, australium: 0);

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Town);

        Assert.Equal(InventoryRefusals.NotEnoughAustralium, r.Refusal);
        Assert.Equal(10, Held.Inventory.Equipped["primary"].Item.Durability);
    }

    [Fact]
    public async Task A_field_repair_lowers_the_maximum_by_the_rules_steps()
    {
        var worn = await Launcher();
        var broken = worn.Item with { Durability = 0 };
        Plant("primary", broken);

        var r = await _ledger.RepairWorn(A, "primary", WornRepair.Field);

        var after = Held.Inventory.Equipped["primary"].Item;
        var steps = R.Repairs.Field(broken, Held.Sheet.Level, 1).Steps;
        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(60 - steps.Sum(s => s.MaxLost), after.MaxDurability);
        Assert.True(after.MaxDurability < 60);
        Assert.Equal(after.MaxDurability, after.Durability);
    }

    [Fact]
    public async Task Field_repairs_to_a_maximum_of_zero_leave_the_item_worn_unusable_and_never_remove_it()
    {
        var worn = await Launcher();
        int repairs = 0;
        while (Held.Inventory.Equipped["primary"].Item.MaxDurability > 0 && repairs < 500)
        {
            Plant("primary", Held.Inventory.Equipped["primary"].Item with { Durability = 0 });
            Assert.True((await _ledger.RepairWorn(A, "primary", WornRepair.Field)).Ok);
            repairs++;
        }

        var item = Held.Inventory.Equipped["primary"];
        Assert.Equal(worn.Id, item.Id);
        Assert.Equal(0, item.Item.MaxDurability);
        Assert.True(R.ItemRules.Equipment.IsUnusable(item.Item));
        Assert.Equal(RepairRefusals.Unusable, (await _ledger.RepairWorn(A, "primary", WornRepair.Field)).Refusal);
        Assert.Equal(RepairRefusals.Unusable, (await _ledger.RepairWorn(A, "primary", WornRepair.TownUncharged)).Refusal);
    }

    [Fact]
    public async Task The_dev_durability_set_changes_the_worn_items_numbers_only()
    {
        var worn = await Launcher();

        var r = await _ledger.SetDurability(A, "primary", 0, 60);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(worn.Item with { Durability = 0 }, Held.Inventory.Equipped["primary"].Item);
    }

    //-------------------------------------------------------------------------
    // Hosted: the host has no call for any of them
    //-------------------------------------------------------------------------

    [Fact]
    public async Task The_hosted_ledger_refuses_consume_wear_and_worn_repairs_as_not_supported_by_the_host()
    {
        var host = new FakeHostItems(R);
        host.AddCharacter(A, R.Heroes.New("soldier", "a"));
        var hosted = new HostedItemLedger(R, new Hooks(_changes), host, new NoLog());
        await hosted.Load(A);
        host.Reserve(A, 0, ItemFixtures.Item("town_portal", 0, 1) with { Durability = 0, MaxDurability = 0 });
        var drop = await hosted.KillDrop(new KillDrop(A, 0, 1, "soldier", 1, default));
        Assert.True((await hosted.Claim(A, Assert.Single(drop.Items).Id)).Ok);
        host.Calls.Clear();

        var consume = await hosted.Consume(A, 0);
        var wear = await hosted.Wear(A, []);

        Assert.Equal(LedgerRefusals.HostUnsupported, consume.Refusal);
        Assert.Equal(LedgerRefusals.HostUnsupported, wear.Refusal);
        Assert.Empty(host.Calls);
        Assert.True(hosted.Find(A)!.Inventory.Backpack.ContainsKey(0));
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message) { }
        public void Warning(string message) { }
    }
}
