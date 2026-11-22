using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;

namespace TF2.Descent.Tests.Items;

/// <summary>
/// The Host.Contracts 1.1 members of the Descent rules module, over the rules' own data: what the
/// host asks when it moves, uses, wears, repairs, kits out and opens a container, each answer the
/// one the unhosted ledger gives for the same thing.
/// </summary>
public sealed class ContractRulesFacts
{
    private static readonly TF2DescentRules R = ItemFixtures.NewRules();

    private static IGameRules Contract => R;

    private static CharacterSheet Sheet(string cls = "soldier", int level = 1) =>
        R.SheetCodec.Write(R.Heroes.New(cls, "ann") with { Level = level });

    private static RolledItem Rolled(ItemInstance item) => R.ItemRules.Codec.ToRolled(item);

    /// <summary>An item as its bytes: two instances alike are the same bytes (their arrays are not equal as records).</summary>
    private static string Bytes(ItemInstance? item) => item is null ? "null" : Convert.ToHexString(ItemCodec.Encode(item));

    private static string Bytes(RolledItem item) => Bytes(R.ItemRules.Codec.Instance(item));

    private static ItemInstance Potion(int count) => ItemFixtures.Item("health_small", 0, 1) with { Count = count, Durability = 0, MaxDurability = 0 };

    private static ItemInstance Kit(int heroLevel = 7) =>
        ItemFixtures.Item("repair_kit", 0, 1) with { Durability = 0, MaxDurability = 0, Context = ItemFixtures.Ctx(0) with { CharacterLevel = heroLevel } };

    //-------------------------------------------------------------------------
    // The three containers
    //-------------------------------------------------------------------------

    [Fact]
    public void The_equip_slots_are_the_inventorys_equipment_slots_in_order()
    {
        Assert.Equal(["primary", "secondary", "melee", "hat", "body", "feet", "trinket1", "trinket2", "amulet"], Contract.EquipSlots);
    }

    [Fact]
    public void The_containers_hold_the_backpacks_capacity_nine_equipment_slots_and_the_four_belt_slots()
    {
        var sheet = Sheet();

        Assert.Equal((R.BackpackSlots(sheet), 9, 4),
            (Contract.ContainerSlots(sheet, ItemContainer.Backpack), Contract.ContainerSlots(sheet, ItemContainer.Equipped),
             Contract.ContainerSlots(sheet, ItemContainer.Belt)));
    }

    [Fact]
    public void The_belt_refuses_anything_but_a_potion()
    {
        var shovel = Rolled(ItemFixtures.Item("shovel", 0, 1));

        Assert.Equal(InventoryRefusals.NotAPotion, Contract.PlaceRefusal(Sheet(), shovel, ItemContainer.Belt, 0));
        Assert.Null(Contract.PlaceRefusal(Sheet(), Rolled(Potion(2)), ItemContainer.Belt, 0));
    }

    [Fact]
    public void An_equip_slot_takes_only_gear_of_its_own_base_slot()
    {
        var shovel = Rolled(ItemFixtures.Item("shovel", 0, 1));
        int melee = Contract.EquipSlots.ToList().IndexOf("melee"), primary = Contract.EquipSlots.ToList().IndexOf("primary");

        Assert.Null(Contract.PlaceRefusal(Sheet(), shovel, ItemContainer.Equipped, melee));
        Assert.Equal(InventoryRefusals.WrongSlot, Contract.PlaceRefusal(Sheet(), shovel, ItemContainer.Equipped, primary));
        Assert.Equal(InventoryRefusals.WrongSlot, Contract.PlaceRefusal(Sheet(), Rolled(Potion(1)), ItemContainer.Equipped, melee));
    }

    [Fact]
    public void An_equip_slot_refuses_what_the_inventory_would_refuse_to_equip()
    {
        var shovel = ItemFixtures.Item("shovel", 0, 1);
        int melee = Contract.EquipSlots.ToList().IndexOf("melee");

        Assert.Equal(InventoryRefusals.Unidentified, Contract.PlaceRefusal(Sheet(), Rolled(shovel with { Identified = false }), ItemContainer.Equipped, melee));
        Assert.Equal(InventoryRefusals.WrongClass, Contract.PlaceRefusal(Sheet("medic"), Rolled(shovel), ItemContainer.Equipped, melee));
        Assert.Equal(InventoryRefusals.Unusable, Contract.PlaceRefusal(Sheet(), Rolled(shovel with { MaxDurability = 0, Durability = 0 }), ItemContainer.Equipped, melee));
    }

    [Fact]
    public void The_backpack_takes_anything()
    {
        Assert.Null(Contract.PlaceRefusal(Sheet(), Rolled(ItemFixtures.Item("shovel", 0, 1)), ItemContainer.Backpack, 3));
        Assert.Null(Contract.PlaceRefusal(Sheet(), Rolled(Potion(1)), ItemContainer.Backpack, 3));
    }

    //-------------------------------------------------------------------------
    // Consume
    //-------------------------------------------------------------------------

    [Fact]
    public void Using_one_of_a_stack_re_encodes_the_stack_one_lower_and_it_still_replays()
    {
        var stack = R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, 1, 0, "base:health_small", StartingKitRow.Source), 5) with { Count = 3 };

        var change = Contract.Consume(Sheet(), Rolled(stack), 1);

        Assert.True(change.Ok, change.Refusal);
        Assert.Empty(change.Consumed);
        var left = Assert.Single(change.Produced);
        Assert.Equal(2, left.Count);
        Assert.Equal(2, R.ItemRules.Codec.Instance(left).Count);
        Assert.True(R.Replays(left));
    }

    [Fact]
    public void Using_the_last_of_a_stack_ends_the_item()
    {
        var change = Contract.Consume(Sheet(), Rolled(Potion(2)), 2);

        Assert.Equal([0], change.Consumed);
        Assert.Empty(change.Produced);
    }

    [Fact]
    public void A_count_outside_the_stack_is_refused()
    {
        Assert.Equal(InventoryRefusals.BadCount, Contract.Consume(Sheet(), Rolled(Potion(2)), 3).Refusal);
        Assert.Equal(InventoryRefusals.BadCount, Contract.Consume(Sheet(), Rolled(Potion(2)), 0).Refusal);
    }

    [Fact]
    public void Gear_is_not_used_up()
    {
        Assert.Equal(InventoryRefusals.NotConsumable, Contract.Consume(Sheet(), Rolled(ItemFixtures.Item("shovel", 0, 1)), 1).Refusal);
    }

    [Fact]
    public void A_use_agrees_with_the_unhosted_drink()
    {
        var held = new Held(1, Potion(3));
        var inventory = new HeroInventory { Belt = ImmutableSortedDictionary<int, Held>.Empty.Add(0, held) };

        var drunk = R.Inventory.Drink(inventory, new StashState(), 0);
        var used = Contract.Consume(Sheet(), Rolled(held.Item), 1);

        Assert.Equal(Bytes(drunk.Hero.Belt[0].Item), Bytes(Assert.Single(used.Produced)));
    }

    //-------------------------------------------------------------------------
    // Wear
    //-------------------------------------------------------------------------

    [Fact]
    public void Wear_on_a_weapon_is_the_unhosted_ledgers_wear_for_as_many_uses()
    {
        var launcher = ItemFixtures.Item("rocket_launcher", 0, 1) with { WearUses = 50 };

        var change = Contract.Wear(Rolled(launcher), 9);

        Assert.True(change.Ok, change.Refusal);
        Assert.Empty(change.Consumed);
        Assert.Equal(Bytes(R.ItemRules.Durability.Use(launcher, 9).Item), Bytes(Assert.Single(change.Produced)));
        Assert.True(R.ItemRules.Codec.Instance(change.Produced[0]).Durability < launcher.Durability);
    }

    [Fact]
    public void Wear_on_armour_is_the_unhosted_ledgers_wear_for_as_many_hits()
    {
        var cap = ItemFixtures.Item("cap", 0, 1) with { Durability = 40, MaxDurability = 40 };
        int hits = 3 * R.Content.DurabilitySettings.ArmourHitsPerPoint;

        var change = Contract.Wear(Rolled(cap), hits);

        Assert.Equal(Bytes(R.ItemRules.Durability.Hit(cap, hits).Item), Bytes(Assert.Single(change.Produced)));
        Assert.Equal(37, R.ItemRules.Codec.Instance(change.Produced[0]).Durability);
    }

    [Fact]
    public void An_amount_of_wear_below_one_is_refused()
    {
        Assert.Equal(WearRefusals.BadAmount, Contract.Wear(Rolled(ItemFixtures.Item("shovel", 0, 1)), 0).Refusal);
    }

    [Fact]
    public void The_wear_amount_of_an_item_is_its_uses_for_a_weapon_and_its_hits_for_armour()
    {
        var d = R.ItemRules.Durability;

        Assert.Equal((7, 5, 0),
            (d.Amount(ItemFixtures.Item("shovel", 0, 1), 7, 5), d.Amount(ItemFixtures.Item("cap", 0, 1), 7, 5), d.Amount(Potion(1), 7, 5)));
    }

    //-------------------------------------------------------------------------
    // The two repairs
    //-------------------------------------------------------------------------

    [Fact]
    public void A_field_repair_changes_the_item_in_place_and_uses_up_the_kit()
    {
        var broken = ItemFixtures.Item("shovel", 0, 1) with { Durability = 0 };

        var change = Contract.Repair(Rolled(broken), RepairKind.Field, Rolled(Kit(7)), 11);

        Assert.True(change.Ok, change.Refusal);
        Assert.Equal([1], change.Consumed);
        Assert.Equal(0, change.Cost);
        Assert.Equal(Bytes(R.Repairs.Field(broken, 7, 11).Item), Bytes(Assert.Single(change.Produced)));
    }

    [Fact]
    public void A_field_repair_runs_at_the_hero_level_the_kit_was_had_at()
    {
        var broken = ItemFixtures.Item("shovel", 0, 1) with { Durability = 0 };

        var low = Contract.Repair(Rolled(broken), RepairKind.Field, Rolled(Kit(1)), 11).Produced[0];
        var high = Contract.Repair(Rolled(broken), RepairKind.Field, Rolled(Kit(30)), 11).Produced[0];

        Assert.Equal(Bytes(R.Repairs.Field(broken, 1, 11).Item), Bytes(low));
        Assert.Equal(Bytes(R.Repairs.Field(broken, 30, 11).Item), Bytes(high));
        Assert.NotEqual(R.ItemRules.Codec.Instance(low).MaxDurability, R.ItemRules.Codec.Instance(high).MaxDurability);
    }

    [Fact]
    public void A_field_repair_without_a_kit_is_refused_and_uses_nothing()
    {
        var broken = Rolled(ItemFixtures.Item("shovel", 0, 1) with { Durability = 0 });

        Assert.Equal(RepairRefusals.NeedsKitAndItem, Contract.Repair(broken, RepairKind.Field, null, 1).Refusal);
        Assert.Equal(RepairRefusals.NeedsKitAndItem, Contract.Repair(broken, RepairKind.Field, Rolled(Potion(1)), 1).Refusal);
    }

    [Fact]
    public void A_field_repair_of_an_item_at_its_maximum_is_refused_and_keeps_the_kit()
    {
        var change = Contract.Repair(Rolled(ItemFixtures.Item("shovel", 0, 1)), RepairKind.Field, Rolled(Kit()), 1);

        Assert.Equal(RepairRefusals.AtMaximum, change.Refusal);
        Assert.Empty(change.Consumed);
    }

    [Fact]
    public void A_town_repair_is_the_engineers_priced_repair()
    {
        var worn = Rolled(ItemFixtures.Item("shovel", 0, 1) with { Durability = 10 });

        var town = Contract.Repair(worn, RepairKind.Town, null, 1);

        Assert.Equal(R.Repair(worn).Cost, town.Cost);
        Assert.True(town.Cost > 0);
        Assert.Equal(R.Repair(worn).Produced[0].Instance, town.Produced[0].Instance);
    }

    //-------------------------------------------------------------------------
    // The starting kit
    //-------------------------------------------------------------------------

    [Fact]
    public void Every_class_kit_replays_and_fits_its_own_containers()
    {
        foreach (var cls in R.Content.StartingKits.Rows.Select(k => k.Id))
        {
            var sheet = Sheet(cls);
            var kit = Contract.StartingKit(cls, sheet, 77);
            Assert.NotEmpty(kit);
            Assert.All(kit, k => Assert.True(R.Replays(k.Item), $"{cls} {k.Item.BaseType} does not replay"));
            Assert.All(kit, k => Assert.InRange(k.Slot, 0, Contract.ContainerSlots(sheet, k.Container) - 1));
            Assert.All(kit, k => Assert.Null(Contract.PlaceRefusal(sheet, k.Item, k.Container, k.Slot)));
            Assert.Equal(kit.Count, kit.Select(k => (k.Container, k.Slot)).Distinct().Count());
        }
    }

    [Fact]
    public async Task The_kit_wears_the_unhosted_heros_starting_gear_and_belts_his_potions()
    {
        var ledger = new InMemoryItemLedger(R, new NoHooks(), new StartingKit(R));
        var unhosted = (await ledger.Load("ann")).Hero!;

        var kit = Contract.StartingKit("soldier", Sheet(), 77);

        Assert.Equal(unhosted.Inventory.Backpack.Values.Select(h => h.Item.BaseId).Order(),
            kit.Where(k => k.Container == ItemContainer.Equipped).Select(k => k.Item.BaseType).Order());
        Assert.Equal(unhosted.Inventory.Belt.Select(p => (p.Key, p.Value.Item.BaseId)),
            kit.Where(k => k.Container == ItemContainer.Belt).Select(k => (k.Slot, k.Item.BaseType)));
        Assert.All(kit.Where(k => k.Container == ItemContainer.Equipped),
            k => Assert.Equal(R.Content.ItemBases[k.Item.BaseType].Slot, R.Content.InventorySettings.Equipment[k.Slot].Accepts));
    }

    [Fact]
    public void The_kit_is_the_same_for_the_same_seed()
    {
        var a = Contract.StartingKit("scout", Sheet("scout"), 5);
        var b = Contract.StartingKit("scout", Sheet("scout"), 5);
        var c = Contract.StartingKit("scout", Sheet("scout"), 6);

        Assert.Equal(a.Select(k => Convert.ToHexString(k.Item.Instance)), b.Select(k => Convert.ToHexString(k.Item.Instance)));
        Assert.NotEqual(a.Select(k => k.Item.Seed), c.Select(k => k.Item.Seed));
    }

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    [Fact]
    public void A_container_gives_some_openers_an_item_and_others_nothing_and_every_item_replays()
    {
        var drops = Enumerable.Range(0, 200).Select(seq => Contract.ContainerDrops(42, (uint)seq, 2, Sheet())).ToList();

        Assert.Contains(drops, d => d.Count == 0);
        Assert.Contains(drops, d => d.Count == 1);
        Assert.All(drops.SelectMany(d => d), i => Assert.True(R.Replays(i)));
    }

    [Fact]
    public void A_container_replays_the_same_for_the_same_instance_container_and_opener()
    {
        var once = Contract.ContainerDrops(42, 9, 2, Sheet());
        var twice = Contract.ContainerDrops(42, 9, 2, Sheet());

        Assert.Equal(once.Select(i => Convert.ToHexString(i.Instance)), twice.Select(i => Convert.ToHexString(i.Instance)));
    }

    [Fact]
    public async Task A_container_gives_the_item_the_unhosted_ledger_gives_for_the_same_roll()
    {
        // The rules' own roll of the request, handed to the in-memory ledger as the furniture hands it.
        var hero = R.Heroes.New("soldier", "ann");
        var ledger = new InMemoryItemLedger(R, new NoHooks(), new StartingKit(R));
        int compared = 0;
        for (uint seq = 0; seq < 60; seq++)
        {
            var request = R.Loot.HostedContainer(42, seq, hero.Name, hero.ClassId);
            var roll = R.Loot.Container(request, 2);
            var hosted = Contract.ContainerDrops(42, seq, 2, Sheet());
            var drop = await ledger.ContainerDrop(new ContainerDrop("ann", "m", request.Seed, roll.Item ? roll.Tier : -1,
                roll.Outcome == TF2.Descent.Rules.Loot.LootOutcome.Potion ? roll.Potion : null, 2, default));
            Assert.Equal(drop.Items.Select(i => Convert.ToHexString(ItemCodec.Encode(i.Item))), hosted.Select(i => Convert.ToHexString(i.Instance)));
            compared += hosted.Count;
        }
        Assert.True(compared > 0, "no container gave an item to compare");
    }

    private sealed class NoHooks : SourceSharp.Abi.IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }
}
