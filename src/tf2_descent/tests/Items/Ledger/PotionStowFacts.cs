using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Items.Ledger;

/// <summary>
/// A potion gained goes to the belt (the whole run's F20; the coordinator's default, Diablo's
/// rule): picked up, bought, bought back or reclaimed, it lands on a belt slot holding the same
/// potion with room, else the first empty one, else the backpack. The starting kit's belt is
/// Sandvich Bite, Sandvich Bite, Bonk! Shot in slots 0 to 2, one each, slot 3 empty.
/// </summary>
public sealed class PotionStowFacts
{
    private static readonly TF2DescentRules R = NewRules();
    private const string A = "76561190000000001";

    private sealed class NoHooks : IHooks<IItemLedgerHook>
    {
        public IReadOnlyList<IItemLedgerHook> All => [];
        public void Each(string what, Action<IItemLedgerHook> call) { }
    }

    private readonly InMemoryItemLedger _ledger = new(R, new NoHooks(), new StartingKit(R));

    private LedgerHero Held => _ledger.Find(A)!;

    private static ItemInstance Potion(string baseId, int count = 1, ulong seed = 9) =>
        R.ItemRules.Roller.Roll(new ItemRollContext("soldier", 1, 1, 1, 0, "base:" + baseId, "potion"), seed) with { Count = count };

    private async Task<LedgerResult> PickUp(ItemInstance item) =>
        await _ledger.Claim(A, _ledger.PlaceOnFloor(item, A, default).Id);

    private int BeltCount(int slot) => Held.Inventory.Belt.TryGetValue(slot, out var h) ? h.Item.Count : 0;

    private bool InBackpack(string baseId) => Held.Inventory.Backpack.Values.Any(h => h.Item.BaseId == baseId);

    /// <summary>Every belt slot full with a stack of four of a potion other than the one about to be gained.</summary>
    private void FillBelt()
    {
        var belt = Held.Inventory.Belt;
        for (int i = 0; i < R.Content.InventorySettings.BeltSlots; i++)
            belt = belt.SetItem(i, new Held(9000 + i, Potion("mana_small", 4, (ulong)(100 + i))));
        _ledger.Replace(Held with { Inventory = Held.Inventory with { Belt = belt } });
    }

    [Fact]
    public async Task A_potion_picked_up_joins_the_same_potions_belt_slot()
    {
        await _ledger.Load(A);

        var pick = await PickUp(Potion("health_small"));

        Assert.True(pick.Ok, pick.Refusal);
        Assert.Equal(2, BeltCount(0));
        Assert.False(InBackpack("health_small"));
    }

    [Fact]
    public async Task A_potion_picked_up_with_no_like_slot_takes_the_first_empty_belt_slot()
    {
        await _ledger.Load(A);

        var pick = await PickUp(Potion("health_medium"));

        Assert.True(pick.Ok, pick.Refusal);
        Assert.Equal("health_medium", Held.Inventory.Belt[3].Item.BaseId);
        Assert.False(InBackpack("health_medium"));
    }

    [Fact]
    public async Task A_potion_picked_up_onto_a_full_belt_stays_in_the_backpack()
    {
        await _ledger.Load(A);
        FillBelt();

        var pick = await PickUp(Potion("health_small"));

        Assert.True(pick.Ok, pick.Refusal);
        Assert.True(InBackpack("health_small"));
        Assert.All(Held.Inventory.Belt.Values, h => Assert.Equal(("mana_small", 4), (h.Item.BaseId, h.Item.Count)));
    }

    [Fact]
    public async Task A_bought_potion_goes_to_the_belt()
    {
        var h = (await _ledger.Load(A)).Hero!;
        _ledger.Replace(h with { Inventory = h.Inventory with { Australium = 1000 } });
        var offer = (await _ledger.Stock(A, "medic", 0)).Items.First(i => i.Item.BaseId == "mana_small");

        var buy = await _ledger.Buy(A, "medic", offer.Id);

        Assert.True(buy.Ok, buy.Refusal);
        Assert.Equal(1 + offer.Item.Count, BeltCount(2));
        Assert.False(InBackpack("mana_small"));
    }

    [Fact]
    public async Task A_potion_reclaimed_from_the_lost_and_found_goes_to_the_belt()
    {
        await _ledger.Load(A);
        // Carried in the backpack when he died, with the Australium the fee is paid from.
        _ledger.Replace(Held with
        {
            Inventory = Held.Inventory with
            {
                Backpack = Held.Inventory.Backpack.Add(10, new Held(8000, Potion("mana_medium"))),
                Australium = 1000,
            },
        });
        Assert.True(await _ledger.RecordDeath(A, default));
        Assert.Equal([A], _ledger.CorpsesLost());

        var reclaim = await _ledger.Reclaim(A);

        Assert.True(reclaim.Ok, reclaim.Refusal);
        Assert.False(InBackpack("mana_medium"));
        Assert.Contains(Held.Inventory.Belt.Values, b => b.Item.BaseId == "mana_medium");
    }

    [Fact]
    public async Task A_potion_taken_off_the_belt_stays_in_the_backpack()
    {
        await _ledger.Load(A);

        var off = await _ledger.FromBelt(A, 0);

        Assert.True(off.Ok, off.Refusal);
        Assert.True(InBackpack("health_small"));
        Assert.False(Held.Inventory.Belt.ContainsKey(0));
    }

    [Fact]
    public async Task Gear_picked_up_goes_to_the_backpack()
    {
        await _ledger.Load(A);
        int before = Held.Inventory.Backpack.Count;

        var pick = await PickUp(Item("boots", 0, 1));

        Assert.True(pick.Ok, pick.Refusal);
        Assert.Equal(before + 1, Held.Inventory.Backpack.Count);
        Assert.Equal(3, Held.Inventory.Belt.Count);
    }
}
