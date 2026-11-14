using System.Text;
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
/// The item ledger's facts (plan_descent_logic HE0), one suite run against both ledgers: the
/// in-memory one over the rules alone and the hosted one over a host that keeps items as the
/// hosting service does (<see cref="FakeHostItems"/>). Each derived class says how an item
/// reaches a hero's backpack and how a hero gets Australium; every fact here holds for both.
/// </summary>
public abstract class LedgerFacts
{
    protected static readonly TF2DescentRules R = NewRules();

    protected const string A = "76561190000000001";
    protected const string B = "76561190000000002";

    protected sealed class Recorder : IItemLedgerHook
    {
        public List<ItemsChanged> Seen { get; } = [];
        public void OnItemsChanged(ItemsChanged change) => Seen.Add(change);
    }

    protected sealed class Hooks(params IItemLedgerHook[] hooks) : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => hooks;
        public void Each(string what, Action<IItemLedgerHook> call) { foreach (var h in hooks) call(h); }
    }

    protected Recorder Changes { get; } = new();

    protected abstract ItemLedgerBase Ledger { get; }

    /// <summary>A drop of exactly <paramref name="item"/> for <paramref name="hero"/>, through the ledger's floor.</summary>
    protected abstract Task<FloorItem> DropFor(string hero, ItemInstance item);

    /// <summary>The hero's Australium, set on both sides of the ledger.</summary>
    protected abstract Task SetAustralium(string hero, long amount);

    //-------------------------------------------------------------------------
    // Helpers
    //-------------------------------------------------------------------------

    /// <summary>An item of a base, rolled as the rules roll one (a craft: identified, never cursed).</summary>
    protected static ItemInstance Roll(string baseId, int tier = 0, int itemLevel = 1, ulong seed = 1) =>
        R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 0, itemLevel, tier, "base:" + baseId, "craft"), seed);

    /// <summary>Puts <paramref name="item"/> in the hero's backpack through a drop and a claim; answers its id.</summary>
    protected async Task<long> Put(string hero, ItemInstance item)
    {
        var floor = await DropFor(hero, item);
        var claimed = await Ledger.Claim(hero, floor.Id);
        Assert.True(claimed.Ok, $"claim refused: {claimed.Refusal}");
        return floor.Id;
    }

    protected int SlotOf(string hero, long id) => Ledger.Find(hero)!.Inventory.Backpack.Single(p => p.Value.Id == id).Key;

    /// <summary>Fills every free backpack slot of the hero.</summary>
    protected async Task Fill(string hero)
    {
        int seed = 100;
        while (R.Inventory.FreeSlot(Ledger.Find(hero)!.Inventory, Ledger.Find(hero)!.Sheet) >= 0)
            await Put(hero, Roll("cap", seed: (ulong)seed++));
    }

    /// <summary>Everything about the hero a change could touch, as text: two snapshots equal is nothing changed.</summary>
    protected string Snapshot(string hero)
    {
        var h = Ledger.Find(hero)!;
        var sb = new StringBuilder($"aus={h.Inventory.Australium};");
        foreach (var i in h.Items.OrderBy(i => i.Id))
            sb.Append($"{i.Id}@{i.Place}{i.Slot}{i.EquipSlot}:{Convert.ToBase64String(ItemCodec.Encode(i.Item))};");
        return sb.ToString();
    }

    //-------------------------------------------------------------------------
    // Drops and the floor
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_kill_drop_lands_for_its_hero_and_replays()
    {
        await Ledger.Load(A);

        var drop = await Ledger.KillDrop(new KillDrop(A, 1, 7, "soldier", 3, new Vector(1, 2, 3)));

        Assert.True(drop.Ok, drop.Refusal);
        var item = Assert.Single(drop.Items);
        Assert.Equal(A, item.ForHero);
        Assert.Contains(item, Ledger.Floor);
        Assert.True(R.Replays(R.ItemRules.Codec.ToRolled(item.Item)), "the drop does not replay");
    }

    [Fact]
    public async Task A_kill_that_rolled_no_drop_puts_nothing_on_the_floor()
    {
        await Ledger.Load(A);

        var drop = await Ledger.KillDrop(new KillDrop(A, -1, 8, "soldier", 3, default));

        Assert.True(drop.Ok);
        Assert.Empty(drop.Items);
        Assert.Empty(Ledger.Floor);
    }

    [Fact]
    public async Task A_claim_into_a_full_backpack_is_refused_and_the_item_stays_on_the_floor()
    {
        await Ledger.Load(A);
        await Fill(A);
        var before = Snapshot(A);
        var floor = await DropFor(A, Roll("shovel"));

        var claim = await Ledger.Claim(A, floor.Id);

        Assert.False(claim.Ok);
        Assert.Equal(InventoryRefusals.BackpackFull, claim.Refusal);
        Assert.Contains(Ledger.Floor, f => f.Id == floor.Id);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task Only_the_hero_a_drop_fell_for_may_claim_it()
    {
        await Ledger.Load(A);
        await Ledger.Load(B);
        var floor = await DropFor(A, Roll("shovel"));

        var byB = await Ledger.Claim(B, floor.Id);
        var byA = await Ledger.Claim(A, floor.Id);

        Assert.Equal(LedgerRefusals.WrongOwner, byB.Refusal);
        Assert.True(byA.Ok, byA.Refusal);
        Assert.DoesNotContain(Ledger.Floor, f => f.Id == floor.Id);
    }

    [Fact]
    public async Task An_item_a_hero_drops_is_anyones_to_pick_up()
    {
        await Ledger.Load(A);
        await Ledger.Load(B);
        long id = await Put(A, Roll("shovel"));

        var drop = await Ledger.Drop(A, SlotOf(A, id), new Vector(5, 5, 5));
        var byB = await Ledger.Claim(B, id);

        Assert.True(drop.Ok, drop.Refusal);
        Assert.True(byB.Ok, byB.Refusal);
        Assert.Contains(Ledger.Find(B)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.DoesNotContain(Ledger.Find(A)!.Items, i => i.Id == id);
    }

    //-------------------------------------------------------------------------
    // Equipment
    //-------------------------------------------------------------------------

    [Fact]
    public async Task Equip_refuses_an_unidentified_item_and_changes_nothing()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("rocket_launcher") with { Identified = false });
        var before = Snapshot(A);

        var equip = await Ledger.Equip(A, SlotOf(A, id), "primary");

        Assert.Equal(InventoryRefusals.Unidentified, equip.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task Equip_refuses_an_unmet_requirement()
    {
        await Ledger.Load(A);
        Ledger.SetSheet(A, Soldier(R, level: 30, brawn: 20));
        long id = await Put(A, Roll("black_box", itemLevel: 10));   // brawn 35 required

        var equip = await Ledger.Equip(A, SlotOf(A, id), "primary");

        Assert.Equal(InventoryRefusals.Requirement, equip.Refusal);
    }

    //-------------------------------------------------------------------------
    // Transactions: each applies its change at once, or changes nothing
    //-------------------------------------------------------------------------

    [Fact]
    public async Task Identify_pays_the_fee_and_keeps_the_id()
    {
        await Ledger.Load(A);
        var item = Roll("rocket_launcher", tier: 2, itemLevel: 10) with { Identified = false };
        long id = await Put(A, item);
        await SetAustralium(A, 1000);

        var identify = await Ledger.Identify(A, SlotOf(A, id));

        Assert.True(identify.Ok, identify.Refusal);
        var held = Ledger.Find(A)!.Inventory.Backpack.Values.Single(h => h.Id == id);
        Assert.True(held.Item.Identified);
        Assert.Equal(1000 - R.Pricing.IdentifyFee(item), Ledger.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public async Task Identify_without_the_fee_is_refused_and_changes_nothing()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("rocket_launcher", tier: 2, itemLevel: 10) with { Identified = false });
        await SetAustralium(A, 0);
        var before = Snapshot(A);

        var identify = await Ledger.Identify(A, SlotOf(A, id));

        Assert.Equal(InventoryRefusals.NotEnoughAustralium, identify.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task Salvage_consumes_the_gear_and_gives_its_metal()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("rocket_launcher", tier: 1, itemLevel: 10));

        var salvage = await Ledger.Salvage(A, SlotOf(A, id));

        Assert.True(salvage.Ok, salvage.Refusal);
        Assert.Contains(id, salvage.Change!.Gone);
        Assert.DoesNotContain(Ledger.Find(A)!.Items, i => i.Id == id);
        Assert.NotEmpty(salvage.Change.Added);
        Assert.All(salvage.Change.Added, a => Assert.Equal(ItemKinds.Metal, R.Content.ItemBases[a.Item.BaseId].Kind));
    }

    [Fact]
    public async Task Salvaging_a_potion_is_refused_and_changes_nothing()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("health_small"));
        var before = Snapshot(A);

        var salvage = await Ledger.Salvage(A, SlotOf(A, id));

        Assert.Equal(CraftRefusals.NotSalvageable, salvage.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task An_unknown_recipe_is_refused_and_changes_nothing()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("scrap"));
        var before = Snapshot(A);

        var craft = await Ledger.Craft(A, "no_such_recipe", [SlotOf(A, id)]);

        Assert.Equal(CraftRefusals.UnknownRecipe, craft.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task Buying_pays_the_price_and_puts_the_item_in_the_backpack()
    {
        await Ledger.Load(A);
        await SetAustralium(A, 100_000);
        var stock = await Ledger.Stock(A, "saxton");
        var offer = stock.Items[0];

        var buy = await Ledger.Buy(A, "saxton", offer.Id);

        Assert.True(buy.Ok, buy.Refusal);
        Assert.Contains(Ledger.Find(A)!.Inventory.Backpack.Values, h => h.Id == offer.Id);
        Assert.Equal(100_000 - offer.Price, Ledger.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public async Task Buying_without_the_price_is_refused_and_changes_nothing()
    {
        await Ledger.Load(A);
        await SetAustralium(A, 0);
        var stock = await Ledger.Stock(A, "saxton");
        var before = Snapshot(A);

        var buy = await Ledger.Buy(A, "saxton", stock.Items[0].Id);

        Assert.Equal(InventoryRefusals.NotEnoughAustralium, buy.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task Selling_pays_the_sell_price_and_buying_back_returns_the_item_for_it()
    {
        await Ledger.Load(A);
        await SetAustralium(A, 0);
        var item = Roll("rocket_launcher", tier: 1, itemLevel: 10);
        long id = await Put(A, item);
        long price = R.Price(R.ItemRules.Codec.ToRolled(item), PriceKind.Sell);

        var sell = await Ledger.Sell(A, "saxton", SlotOf(A, id));
        var sold = Ledger.Find(A)!;
        var back = await Ledger.BuyBack(A, "saxton", 0);

        Assert.True(sell.Ok, sell.Refusal);
        Assert.Equal(price, sold.Inventory.Australium);
        Assert.Equal(id, Assert.Single(sold.Inventory.BuyBack).Id);
        Assert.True(back.Ok, back.Refusal);
        Assert.Contains(Ledger.Find(A)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.Equal(0, Ledger.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public async Task Selling_an_empty_slot_is_refused_and_changes_nothing()
    {
        await Ledger.Load(A);
        var before = Snapshot(A);

        var sell = await Ledger.Sell(A, "saxton", 11);

        Assert.Equal(InventoryRefusals.NoItem, sell.Refusal);
        Assert.Equal(before, Snapshot(A));
    }

    [Fact]
    public async Task A_deposit_and_a_withdrawal_move_the_item_through_the_stash()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("shovel"));

        var deposit = await Ledger.Deposit(A, SlotOf(A, id));
        var stashed = Ledger.Find(A)!.Stash.Slots.Single(p => p.Value.Id == id).Key;
        var withdraw = await Ledger.Withdraw(A, stashed);

        Assert.True(deposit.Ok, deposit.Refusal);
        Assert.True(withdraw.Ok, withdraw.Refusal);
        Assert.Contains(Ledger.Find(A)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.Empty(Ledger.Find(A)!.Stash.Slots);
    }

    //-------------------------------------------------------------------------
    // The change hook
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_claim_is_told_once_with_the_item_added()
    {
        await Ledger.Load(A);
        var floor = await DropFor(A, Roll("shovel"));
        int seen = Changes.Seen.Count;

        await Ledger.Claim(A, floor.Id);

        var change = Assert.Single(Changes.Seen.Skip(seen));
        Assert.Equal("claim", change.Operation);
        Assert.Equal(A, change.Hero);
        Assert.Equal(floor.Id, Assert.Single(change.Added).Id);
        Assert.Empty(change.Removed);
        Assert.Empty(change.Changed);
    }

    [Fact]
    public async Task A_refused_operation_is_not_told()
    {
        await Ledger.Load(A);
        int seen = Changes.Seen.Count;

        await Ledger.Sell(A, "saxton", 11);
        await Ledger.Claim(A, 999_999);

        Assert.Equal(seen, Changes.Seen.Count);
    }

    [Fact]
    public async Task A_move_is_told_as_a_change_of_place()
    {
        await Ledger.Load(A);
        long id = await Put(A, Roll("shovel"));
        int from = SlotOf(A, id);
        int to = R.Inventory.BackpackSlots(Ledger.Find(A)!.Sheet) - 1;
        int seen = Changes.Seen.Count;

        var move = await Ledger.Move(A, from, to);

        Assert.True(move.Ok, move.Refusal);
        var change = Assert.Single(Changes.Seen.Skip(seen));
        var moved = Assert.Single(change.Changed);
        Assert.Equal((id, to), (moved.Id, moved.Slot));
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
    }

    //-------------------------------------------------------------------------
    // Count in, count out
    //-------------------------------------------------------------------------

    [Fact]
    public async Task No_sequence_of_ledger_operations_duplicates_or_loses_an_item()
    {
        string[] heroes = [A, B];
        string[] bases = ["shovel", "rocket_launcher", "cap", "health_small", "scrap", "dog_tags"];
        foreach (var h in heroes)
        {
            await Ledger.Load(h);
            await SetAustralium(h, 50_000);
        }
        var random = new Random(4242);
        var seen = new HashSet<long>();
        var gone = new HashSet<long>();
        var ok = new Dictionary<string, int>();
        void Note(string op, bool done) { if (done) ok[op] = ok.GetValueOrDefault(op) + 1; }
        foreach (var h in heroes)
            seen.UnionWith(Ledger.Find(h)!.Items.Select(i => i.Id));

        for (int step = 0; step < 600; step++)
        {
            string hero = heroes[random.Next(heroes.Length)];
            var h = Ledger.Find(hero)!;
            var slots = h.Inventory.Backpack.Keys.ToList();
            int AnySlot() => slots.Count == 0 ? 0 : slots[random.Next(slots.Count)];
            LedgerResult? r = null;
            switch (random.Next(11))
            {
                case 0 or 1:
                {
                    var f = await DropFor(hero, Roll(bases[random.Next(bases.Length)], tier: random.Next(2), itemLevel: 1 + random.Next(20), seed: (ulong)step));
                    seen.Add(f.Id);
                    Note("drop_for", true);
                    break;
                }
                case 2:
                    if (Ledger.Floor.Count > 0)
                        r = await Ledger.Claim(hero, Ledger.Floor.ElementAt(random.Next(Ledger.Floor.Count)).Id);
                    Note("claim", r?.Ok == true);
                    break;
                case 3:
                    r = await Ledger.Drop(hero, AnySlot(), default);
                    Note("drop", r.Ok);
                    break;
                case 4:
                    r = await Ledger.Move(hero, AnySlot(), random.Next(14));
                    Note("move", r.Ok);
                    break;
                case 5:
                    r = await Ledger.Sell(hero, "saxton", AnySlot());
                    Note("sell", r.Ok);
                    break;
                case 6:
                    r = await Ledger.BuyBack(hero, "saxton", 0);
                    Note("buy_back", r.Ok);
                    break;
                case 7:
                    r = await Ledger.Salvage(hero, AnySlot());
                    Note("salvage", r.Ok);
                    break;
                case 8:
                    r = await Ledger.Identify(hero, AnySlot());
                    Note("identify", r.Ok);
                    break;
                case 9:
                    r = await Ledger.Deposit(hero, AnySlot());
                    Note("deposit", r.Ok);
                    break;
                default:
                    r = h.Stash.Slots.Count == 0 ? null : await Ledger.Withdraw(hero, h.Stash.Slots.Keys.First());
                    Note("withdraw", r?.Ok == true);
                    break;
            }
            if (r?.Change is { } c)
            {
                seen.UnionWith(c.Added.Select(i => i.Id));
                gone.UnionWith(c.Gone);
            }
        }

        // The stimulus reached every operation the sequence drives.
        foreach (var op in new[] { "claim", "drop", "move", "sell", "buy_back", "salvage", "deposit", "withdraw" })
            Assert.True(ok.GetValueOrDefault(op) > 0, $"no {op} succeeded: {string.Join(", ", ok)}");

        var live = heroes.SelectMany(h => Ledger.Find(h)!.Items.Select(i => i.Id)).Concat(Ledger.Floor.Select(f => f.Id)).ToList();
        Assert.Equal(live.Count, live.Distinct().Count());
        foreach (long id in seen)
            Assert.True(live.Contains(id) ^ gone.Contains(id), $"item {id} is {(live.Contains(id) ? "both held and gone" : "neither held nor gone")}");
        Assert.All(live, id => Assert.Contains(id, seen));
    }
}
