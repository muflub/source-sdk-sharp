using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>
/// The inventory (plan_descent_logic RU5; plan_tf2_rpg 2d; R13, D4): capacity derived from skill
/// ranks and never stored, overflow that is withdraw-only and never lost, the equip rules, the
/// belt, stacks, the stash, and vendors' buy, sell and buy-back.
/// </summary>
public sealed class InventoryTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static InventoryRules Inv => R.Inventory;
    private static readonly StashState NoStash = new();

    private static HeroSheet Sheet(int pockets = 0, int storage = 0, int level = 30, int brawn = 20, int aim = 20)
    {
        var s = Soldier(R, level, brawn, aim);
        return s with { SkillRanks = s.SkillRanks.SetItem("deep_pockets", pockets).SetItem("storage_plan", storage) };
    }

    private static long _ids;
    private static Held New(ItemInstance item) => new(Interlocked.Increment(ref _ids), item);
    private static Held Gear(string baseId = "shovel", int tier = 0, int level = 20) =>
        New(R.ItemRules.Roller.Roll(Ctx(tier, level, table: "base:" + baseId, source: "craft"), (ulong)Interlocked.Increment(ref _ids)));
    private static Held Stack(string baseId, int count) => New(R.ItemRules.Roller.Roll(Ctx(0, 1, table: "base:" + baseId, source: "craft"), 1) with { Count = count });

    private static HeroInventory Fill(HeroInventory hero, HeroSheet sheet, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var r = Inv.Pickup(hero, NoStash, sheet, Gear());
            Assert.True(r.Ok, r.Refusal);
            hero = r.Hero;
        }
        return hero;
    }

    //-------------------------------------------------------------------------
    // Capacity (R13, D4)
    //-------------------------------------------------------------------------

    [Fact]
    public void The_backpack_holds_12_at_rank_0_and_40_at_rank_7()
    {
        Assert.Equal(12, Inv.BackpackSlots(Sheet(pockets: 0)));
        Assert.Equal(40, Inv.BackpackSlots(Sheet(pockets: 7)));
    }

    [Fact]
    public void Each_deep_pockets_rank_adds_exactly_4_and_ranks_past_the_last_add_nothing()
    {
        for (int rank = 0; rank < 7; rank++)
            Assert.Equal(4, Inv.BackpackSlots(Sheet(pockets: rank + 1)) - Inv.BackpackSlots(Sheet(pockets: rank)));
        Assert.Equal(40, Inv.BackpackSlots(Sheet(pockets: 9)));
    }

    [Fact]
    public void The_stash_holds_20_at_rank_0_and_100_at_rank_5_each_rank_adding_16()
    {
        Assert.Equal(20, Inv.StashSlots([Sheet(storage: 0)]));
        Assert.Equal(100, Inv.StashSlots([Sheet(storage: 5)]));
        for (int rank = 0; rank < 5; rank++)
            Assert.Equal(16, Inv.StashSlots([Sheet(storage: rank + 1)]) - Inv.StashSlots([Sheet(storage: rank)]));
    }

    [Fact]
    public void The_stash_holds_what_the_highest_storage_plan_rank_among_living_characters_buys()
    {
        Assert.Equal(20 + 16 * 4, Inv.StashSlots([Sheet(storage: 2), Sheet(storage: 4), Sheet(storage: 1)]));
        Assert.Equal(20 + 16 * 2, Inv.StashSlots([Sheet(storage: 2), Sheet(storage: 4) with { Fallen = true }]));
        Assert.Equal(20, Inv.StashSlots([]));
    }

    [Fact]
    public void The_host_members_derive_capacity_from_the_sheets_bytes()
    {
        var sheet = R.SheetCodec.Write(Sheet(pockets: 3, storage: 2));
        var other = R.SheetCodec.Write(Sheet(storage: 4));

        Assert.Equal(24, R.BackpackSlots(sheet));
        Assert.Equal(84, R.StashSlots([sheet, other]));
    }

    //-------------------------------------------------------------------------
    // Shrinking never destroys (withdraw-only overflow)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_respec_that_shrinks_the_backpack_leaves_its_overflow_withdraw_only_and_loses_nothing()
    {
        var big = Sheet(pockets: 7);
        var hero = Fill(new HeroInventory(), big, 40);
        var small = R.Heroes.Respec(big).Sheet;

        Assert.Equal(12, Inv.BackpackSlots(small));
        Assert.Equal(40, hero.Backpack.Count);                                    // nothing lost
        Assert.Equal(28, Inv.Overflow(hero, small).Count);
        Assert.Equal(InventoryRefusals.BackpackFull, Inv.Pickup(hero, NoStash, small, Gear()).Refusal);
        Assert.Equal(InventoryRefusals.WithdrawOnly, Inv.Move(hero, NoStash, small, 3, 30).Refusal);
    }

    [Fact]
    public void An_item_in_a_withdraw_only_slot_can_be_dropped_sold_deposited_or_moved_down()
    {
        var big = Sheet(pockets: 7);
        var hero = Fill(new HeroInventory(), big, 13);   // slots 0..12
        var small = Sheet(pockets: 0);                   // capacity 12: slot 12 is withdraw-only
        hero = Inv.Drop(hero, NoStash, 0).Hero;          // a free slot below capacity

        Assert.True(Inv.Drop(hero, NoStash, 12).Ok);
        Assert.True(Inv.Sell(hero, NoStash, 12, 5).Ok);
        Assert.True(Inv.Deposit(hero, NoStash, 20, 12).Ok);
        var moved = Inv.Move(hero, NoStash, small, 12, 0);
        Assert.True(moved.Ok);
        Assert.Empty(Inv.Overflow(moved.Hero, small));
    }

    [Fact]
    public void A_swap_never_puts_an_item_into_a_withdraw_only_slot()
    {
        var hero = Fill(new HeroInventory(), Sheet(pockets: 1), 13);

        Assert.Equal(InventoryRefusals.WithdrawOnly, Inv.Move(hero, NoStash, Sheet(pockets: 0), 12, 3).Refusal);
    }

    [Fact]
    public void A_shrunk_stash_keeps_its_overflow_withdraw_only()
    {
        var stash = new StashState();
        var hero = Fill(new HeroInventory(), Sheet(pockets: 7), 40);
        int bigCap = Inv.StashSlots([Sheet(storage: 1)]);   // 36
        for (int slot = 0; slot < 36; slot++)
        {
            var r = Inv.Deposit(hero, stash, bigCap, slot);
            Assert.True(r.Ok, r.Refusal);
            (hero, stash) = (r.Hero, r.Stash);
        }
        int smallCap = Inv.StashSlots([Sheet(storage: 1) with { Fallen = true }]);   // the only planner fell: 20

        Assert.Equal(36, stash.Slots.Count);
        Assert.Equal(16, InventoryRules.StashOverflow(stash, smallCap).Count);
        Assert.Equal(InventoryRefusals.StashFull, Inv.Deposit(hero, stash, smallCap, 36).Refusal);
        Assert.True(Inv.Withdraw(Inv.Drop(hero, stash, 0).Hero, stash, Sheet(pockets: 7), 30).Ok);
    }

    //-------------------------------------------------------------------------
    // Equip
    //-------------------------------------------------------------------------

    private static (HeroInventory Hero, int Slot) Holding(ItemInstance item)
    {
        var r = Inv.Pickup(new HeroInventory(), NoStash, Sheet(), New(item));
        return (r.Hero, 0);
    }

    private static string? Equip(ItemInstance item, string slot, HeroSheet? sheet = null)
    {
        var (hero, from) = Holding(item);
        return Inv.Equip(hero, NoStash, sheet ?? Sheet(), from, slot).Refusal;
    }

    [Fact]
    public void Equip_puts_the_item_on_and_the_replaced_one_where_it_came_from()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 2);
        hero = Inv.Equip(hero, NoStash, Sheet(), 0, "melee").Hero;
        long second = hero.Backpack[1].Id;

        var r = Inv.Equip(hero, NoStash, Sheet(), 1, "melee");

        Assert.True(r.Ok);
        Assert.Equal(second, r.Hero.Equipped["melee"].Id);
        Assert.True(r.Hero.Backpack.ContainsKey(1));
        Assert.Equal(2, r.Hero.Items.Count());
    }

    [Fact]
    public void Equip_refuses_a_base_that_does_not_fit_the_slot()
    {
        Assert.Equal(InventoryRefusals.WrongSlot, Equip(Gear("shovel").Item, "primary"));
        Assert.Equal(InventoryRefusals.WrongSlot, Equip(Stack("scrap", 3).Item, "primary"));
        Assert.Null(Equip(Gear("dog_tags").Item, "trinket2"));
    }

    [Fact]
    public void Equip_refuses_another_classes_item()
    {
        Assert.Equal(InventoryRefusals.WrongClass, Equip(Gear("bat").Item, "melee"));
    }

    [Fact]
    public void Equip_refuses_a_base_above_the_heros_level()
    {
        Assert.Equal(InventoryRefusals.Level, Equip(Gear("black_box").Item, "primary", Sheet(level: 8, brawn: 50)));
    }

    [Fact]
    public void Equip_refuses_an_unidentified_item()
    {
        var hidden = R.ItemRules.Roller.Roll(Ctx(2, 20, table: "base:shovel"), 4);

        Assert.False(hidden.Identified);
        Assert.Equal(InventoryRefusals.Unidentified, Equip(hidden, "melee"));
    }

    [Fact]
    public void Equip_refuses_an_unusable_item()
    {
        Assert.Equal(InventoryRefusals.Unusable, Equip(Gear("shovel").Item with { Durability = 0, MaxDurability = 0 }, "melee"));
    }

    [Fact]
    public void Equip_refuses_an_unmet_requirement_and_takes_it_once_met()
    {
        Assert.Equal(InventoryRefusals.Requirement, Equip(Gear("black_box").Item, "primary", Sheet(brawn: 34)));
        Assert.Null(Equip(Gear("black_box").Item, "primary", Sheet(brawn: 35)));
    }

    //-------------------------------------------------------------------------
    // A corpse recovered (WO5's walk back)
    //-------------------------------------------------------------------------

    [Fact]
    public void A_recovered_item_whose_requirement_no_longer_holds_goes_to_the_backpack()
    {
        var box = Gear("black_box");
        var corpse = new[] { new CorpseItem(box, Equipped: "primary") };

        var short_ = Inv.Recover(new HeroInventory(), Sheet(brawn: 34), corpse);
        var met = Inv.Recover(new HeroInventory(), Sheet(brawn: 35), corpse);

        Assert.Equal(box.Id, met.Hero.Equipped["primary"].Id);   // the control: met, it goes back on
        Assert.Empty(short_.Hero.Equipped);
        Assert.Equal(box.Id, Assert.Single(short_.Hero.Backpack).Value.Id);
    }

    [Fact]
    public void A_recovered_potion_whose_belt_slot_is_taken_goes_to_the_backpack()
    {
        var mine = Stack("health_small", 1);
        var taken = new HeroInventory { Belt = ImmutableSortedDictionary<int, Held>.Empty.Add(0, Stack("mana_small", 1)) };

        var r = Inv.Recover(taken, Sheet(), [new CorpseItem(mine, Belt: 0)]);

        Assert.Equal(mine.Id, Assert.Single(r.Hero.Backpack).Value.Id);
        Assert.Equal((0, 1), (r.Belted, r.Packed));
    }

    [Fact]
    public void What_a_full_backpack_cannot_take_stays_on_the_corpse_with_its_place()
    {
        var hero = Fill(new HeroInventory(), Sheet(), Inv.BackpackSlots(Sheet()));
        var worn = new CorpseItem(Gear("shovel"), Equipped: "melee");
        var loose = new CorpseItem(Gear("shovel"));

        var r = Inv.Recover(hero, Sheet(), [worn, loose]);

        Assert.Equal(worn.Item.Id, r.Hero.Equipped["melee"].Id);
        Assert.Equal([loose], r.Left);
    }

    [Fact]
    public void A_broken_item_can_be_equipped_and_stays_broken()
    {
        Assert.Null(Equip(Gear("shovel").Item with { Durability = 0 }, "melee"));
    }

    [Fact]
    public void Unequip_needs_a_free_slot()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 1);
        hero = Inv.Equip(hero, NoStash, Sheet(), 0, "melee").Hero;
        hero = Fill(hero, Sheet(), 12);

        Assert.Equal(InventoryRefusals.BackpackFull, Inv.Unequip(hero, NoStash, Sheet(), "melee").Refusal);
        Assert.True(Inv.Unequip(Inv.Drop(hero, NoStash, 5).Hero, NoStash, Sheet(), "melee").Ok);
    }

    //-------------------------------------------------------------------------
    // Belt, stacks
    //-------------------------------------------------------------------------

    [Fact]
    public void The_belt_holds_four_potions_and_nothing_else()
    {
        var hero = new HeroInventory();
        for (int i = 0; i < 5; i++)
            hero = Inv.Pickup(hero, NoStash, Sheet(), Stack("health_small", 1)).Hero;
        hero = Inv.Pickup(hero, NoStash, Sheet(), Gear()).Hero;

        for (int i = 0; i < 4; i++)
            hero = Inv.ToBelt(hero, NoStash, i, i).Hero;

        Assert.Equal(4, hero.Belt.Count);
        Assert.Equal(InventoryRefusals.NoSuchSlot, Inv.ToBelt(hero, NoStash, 4, 4).Refusal);
        // The same potion on an occupied slot merges into its stack (HE8); another kind is refused (BeltRulesTests).
        Assert.Equal(2, Inv.ToBelt(hero, NoStash, 4, 0).Hero.Belt[0].Item.Count);
        Assert.Equal(InventoryRefusals.NotAPotion, Inv.ToBelt(Inv.FromBelt(hero, NoStash, Sheet(), 0).Hero, NoStash, 5, 0).Refusal);
    }

    [Fact]
    public void Stacks_merge_up_to_100_and_the_rest_stays()
    {
        var hero = Inv.Pickup(new HeroInventory(), NoStash, Sheet(), Stack("scrap", 70)).Hero;
        hero = Inv.Pickup(hero, NoStash, Sheet(), Stack("scrap", 50)).Hero;

        var r = Inv.Stack(hero, NoStash, Sheet(), 1, 0);

        Assert.True(r.Ok);
        Assert.Equal((100, 20), (r.Hero.Backpack[0].Item.Count, r.Hero.Backpack[1].Item.Count));
        Assert.Equal(InventoryRefusals.NotStackable, Inv.Stack(r.Hero, NoStash, Sheet(), 1, 0).Refusal);
    }

    [Fact]
    public void Gear_and_different_metals_do_not_stack()
    {
        var hero = Inv.Pickup(new HeroInventory(), NoStash, Sheet(), Stack("scrap", 1)).Hero;
        hero = Inv.Pickup(hero, NoStash, Sheet(), Stack("reclaimed", 1)).Hero;
        hero = Fill(hero, Sheet(), 2);

        Assert.Equal(InventoryRefusals.NotStackable, Inv.Stack(hero, NoStash, Sheet(), 1, 0).Refusal);
        Assert.Equal(InventoryRefusals.NotStackable, Inv.Stack(hero, NoStash, Sheet(), 3, 2).Refusal);
    }

    //-------------------------------------------------------------------------
    // Vendors
    //-------------------------------------------------------------------------

    [Fact]
    public void Buying_needs_the_australium_and_a_free_slot()
    {
        var item = Gear();
        long price = R.Pricing.Price(item.Item, PriceKind.Buy);
        var rich = new HeroInventory { Australium = price };

        var bought = Inv.Buy(rich, NoStash, Sheet(), item, price);

        Assert.True(bought.Ok);
        Assert.Equal(0, bought.Hero.Australium);
        Assert.Equal(InventoryRefusals.NotEnoughAustralium, Inv.Buy(rich with { Australium = price - 1 }, NoStash, Sheet(), item, price).Refusal);
        Assert.Equal(InventoryRefusals.BackpackFull, Inv.Buy(Fill(rich, Sheet(), 12), NoStash, Sheet(), item, price).Refusal);
    }

    [Fact]
    public void A_sale_pays_the_sell_price_and_the_vendor_keeps_the_item_for_buy_back_at_that_price()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 1);
        var item = hero.Backpack[0];
        long sell = R.Pricing.Price(item.Item, PriceKind.Sell);

        var sold = Inv.Sell(hero, NoStash, 0, sell);
        var back = Inv.BuyBack(sold.Hero, NoStash, Sheet(), 0, sell);

        Assert.Equal(sell, sold.Hero.Australium);
        Assert.Equal(item.Id, Assert.Single(sold.Hero.BuyBack).Id);
        Assert.True(back.Ok);
        Assert.Equal((0L, item.Id), (back.Hero.Australium, back.Hero.Backpack.Values.Single().Id));
    }

    [Fact]
    public void Buy_back_keeps_the_last_six_sales_and_the_oldest_leaves()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 8);
        var ids = hero.Backpack.Values.Select(h => h.Id).ToList();
        var left = new List<Held>();
        for (int slot = 0; slot < 8; slot++)
        {
            var r = Inv.Sell(hero, NoStash, slot, 1);
            hero = r.Hero;
            left.AddRange(r.Left);
        }

        Assert.Equal(ids.Skip(2), hero.BuyBack.Select(h => h.Id));
        Assert.Equal(ids.Take(2), left.Select(h => h.Id));
    }

    [Fact]
    public void A_buy_costs_at_least_what_a_sale_pays()
    {
        foreach (var tier in new[] { 0, 1, 2, 3 })
        {
            var item = R.ItemRules.Roller.Roll(Ctx(tier, 20), 5) with { Identified = true };
            Assert.True(R.Pricing.Price(item, PriceKind.Buy) >= R.Pricing.Price(item, PriceKind.Sell));
            Assert.True(R.Pricing.Price(item, PriceKind.Sell) > 0);
        }
    }

    [Fact]
    public void An_unidentified_item_sells_at_the_stock_price()
    {
        var hidden = R.ItemRules.Roller.Roll(Ctx(3, 25, table: "base:shovel"), 2);
        var stock = hidden with { Rarity = 0, Affixes = [], UnusualEffect = null, Identified = true };

        Assert.False(hidden.Identified);
        Assert.Equal(R.Pricing.Price(stock, PriceKind.Sell), R.Pricing.Price(hidden, PriceKind.Sell));
        Assert.True(R.Pricing.Price(hidden with { Identified = true }, PriceKind.Sell) > R.Pricing.Price(hidden, PriceKind.Sell));
    }

    //-------------------------------------------------------------------------
    // Crafting applied to the backpack
    //-------------------------------------------------------------------------

    [Fact]
    public void A_salvage_applied_to_the_backpack_swaps_the_item_for_its_metal_at_once()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 1);
        var change = R.Salvage(R.ItemRules.Codec.ToRolled(hero.Backpack[0].Item), Ctx(0), 1);

        var r = Inv.Apply(hero, NoStash, Sheet(), [0], change, [.. change.Produced.Select((_, k) => 1000L + k)]);

        Assert.True(r.Ok);
        Assert.Equal(hero.Backpack[0].Id, Assert.Single(r.Left).Id);
        Assert.All(r.Hero.Backpack.Values, h => Assert.Equal(ItemKinds.Metal, R.Content.ItemBases[h.Item.BaseId].Kind));
    }

    [Fact]
    public void A_craft_whose_outputs_do_not_fit_is_refused_and_changes_nothing()
    {
        var hero = Fill(new HeroInventory(), Sheet(), 11);
        hero = Inv.Pickup(hero, NoStash, Sheet(), Stack("refined", 4)).Hero;   // 36 units in slot 11: full
        var change = R.Craft("craft_item:shovel", [R.ItemRules.Codec.ToRolled(hero.Backpack[11].Item)], Ctx(0, 20), 1);
        Assert.True(change.Produced.Count >= 2);   // the item and its change

        var r = Inv.Apply(hero, NoStash, Sheet(), [11], change, [.. change.Produced.Select((_, k) => 2000L + k)]);

        Assert.Equal(InventoryRefusals.BackpackFull, r.Refusal);
        Assert.Same(hero, r.Hero);
    }
}
