using SourceSharp.Abi;

using TF2.Descent.Rules.Inventory;
using TF2.Descent.Server.Loot;
using TF2.Descent.Shared;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// Taking a seat (plan_descent_logic WO2): a hero's use takes his item into
/// his backpack through the ledger and his Australium into his purse; his
/// touch takes the Australium alone; a full backpack refuses the item, which
/// stays, and he is told; nobody takes a seat that is not his; a drop whose
/// seats are all empty leaves the floor and its entity parks.
/// </summary>
public sealed class LootPickupTests
{
    [Fact]
    public async Task Using_a_drop_puts_the_item_in_the_backpack_and_empties_the_seat()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));
        long id = drop.SeatOf(SlotA)!.Item!.Id;

        var outcome = (await r.Floor.Take(drop, SlotA, touch: false));

        Assert.Equal(TakeOutcome.Taken, outcome);
        Assert.Contains(r.Memory.Find(A)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.True(drop.SeatOf(SlotA)!.Empty);
        Assert.DoesNotContain(r.Memory.Floor, f => f.Id == id);
        Assert.Equal((byte)LootHolds.None, r.SeatsSentTo(SlotA)[^1].Seat.Holds);
    }

    [Fact]
    public async Task A_use_takes_the_Australium_with_the_item()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0), australiumA: 40);

        await r.Floor.Take(drop, SlotA, touch: false);

        Assert.Equal(40, r.Memory.Find(A)!.Inventory.Australium);
        Assert.True(drop.SeatOf(SlotA)!.Empty);
    }

    [Fact]
    public async Task The_entity_parks_when_the_last_seat_empties_and_not_before()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));
        var entity = drop.Entity!;

        await r.Floor.Take(drop, SlotA, touch: false);
        Assert.False(entity.Parked);
        Assert.Contains(drop, r.Floor.Drops);
        await r.Floor.Take(drop, SlotB, touch: false);

        Assert.True(entity.Parked);
        Assert.DoesNotContain(drop, r.Floor.Drops);
        Assert.Equal(64, r.Floor.Free);
        Assert.Equal(0, entity.State.Drop);
    }

    [Fact]
    public async Task A_full_backpack_refuses_the_item_which_stays_and_the_hero_is_told()
    {
        var r = new LootRig();
        await r.Memory.Load(A);
        while ((await r.Memory.Give(A, "shovel", 0, 1)).Ok) { }
        var drop = r.Drop(InRoom(0));
        long id = drop.SeatOf(SlotA)!.Item!.Id;

        var outcome = (await r.Floor.Take(drop, SlotA, touch: false));

        Assert.Equal(TakeOutcome.Refused, outcome);
        Assert.Equal(id, drop.SeatOf(SlotA)!.Item!.Id);
        Assert.True(drop.OnEntity);
        Assert.Contains(r.Memory.Floor, f => f.Id == id);
        var notice = Assert.Single(r.NoticesTo(SlotA));
        Assert.Equal(LootFloor.FullToken, notice.Text);
        Assert.Equal(SourceSharp.Shared.TransmitAnswer.Default, r.SentTo(drop, SlotA));
    }

    [Fact]
    public async Task A_hero_cannot_take_another_heros_seat()
    {
        var r = new LootRig();
        var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, r.Item(A), 30)])!;
        long id = drop.SeatOf(SlotA)!.Item!.Id;

        var byB = (await r.Floor.Take(drop, SlotB, touch: false));
        var touchedByB = (await r.Floor.Take(drop, SlotB, touch: true));

        Assert.Equal(TakeOutcome.NotYours, byB);
        Assert.Equal(TakeOutcome.NotYours, touchedByB);
        Assert.Equal(id, drop.SeatOf(SlotA)!.Item!.Id);
        Assert.Equal(30, drop.SeatOf(SlotA)!.Australium);
        Assert.DoesNotContain(r.Memory.Find(B)?.Inventory.Backpack.Values ?? [], h => h.Id == id);
    }

    [Fact]
    public void Walking_onto_Australium_takes_it_by_touch()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0), australiumA: 25, itemA: false);

        drop.Entity!.OnTouch(PlayerOf(SlotA));

        Assert.Equal(25, r.Memory.Find(A)!.Inventory.Australium);
        Assert.True(drop.SeatOf(SlotA)!.Empty);
    }

    [Fact]
    public async Task A_touch_takes_no_item()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0), australiumA: 25);
        long id = drop.SeatOf(SlotA)!.Item!.Id;

        drop.Entity!.OnTouch(PlayerOf(SlotA));

        Assert.Equal(id, drop.SeatOf(SlotA)!.Item!.Id);
        Assert.Equal(0, drop.SeatOf(SlotA)!.Australium);
        Assert.Equal((byte)LootHolds.Item, r.SeatsSentTo(SlotA)[^1].Seat.Holds);
        Assert.Equal(TakeOutcome.Nothing, (await r.Floor.Take(drop, SlotA, touch: true)));
    }

    [Fact]
    public void A_use_fired_at_the_entity_takes_the_users_seat()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));
        long id = drop.SeatOf(SlotB)!.Item!.Id;

        r.Registry.Input(drop.Entity!.Handle, "Use", "", 0f, PlayerOf(SlotB));

        Assert.Contains(r.Memory.Find(B)!.Inventory.Backpack.Values, h => h.Id == id);
        Assert.False(drop.SeatOf(SlotA)!.Empty);
    }

    [Fact]
    public void A_parked_entity_answers_no_use()
    {
        var r = new LootRig();
        var parked = r.Registry.OfClass(LootNetState.ClassName).Cast<LootEntity>().First();

        Assert.Equal(UseCaps.None, parked.ObjectCaps);
    }

    [Fact]
    public void A_lent_entity_is_used_from_the_crosshair_or_the_radius()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));

        Assert.Equal(UseCaps.ImpulseUse | UseCaps.UseInRadius, drop.Entity!.ObjectCaps);
    }

    [Fact]
    public async Task A_take_answered_after_the_level_ended_does_nothing()
    {
        var ledger = new HeldLedger();
        var r = new LootRig(ledger: ledger.Proxy);
        var drop = r.Drop(InRoom(0));

        var take = r.Floor.Take(drop, SlotA, touch: false);
        r.NextLevel();
        ledger.Claims.Single().SetResult(new TF2.Descent.Server.Items.LedgerResult(true, null, null, null));

        Assert.Equal(TakeOutcome.LevelEnded, (await take));
        Assert.Empty(r.Floor.Drops);
        Assert.Equal(64, r.Floor.Free);
    }

    [Fact]
    public async Task A_seat_being_taken_is_not_taken_twice()
    {
        var ledger = new HeldLedger();
        var r = new LootRig(ledger: ledger.Proxy);
        var drop = r.Drop(InRoom(0));

        var first = r.Floor.Take(drop, SlotA, touch: false);
        var second = (await r.Floor.Take(drop, SlotA, touch: false));
        ledger.Claims.Single().SetResult(new TF2.Descent.Server.Items.LedgerResult(true, null, null, null));

        Assert.Equal(TakeOutcome.Busy, second);
        Assert.Equal(TakeOutcome.Taken, (await first));
        Assert.Single(ledger.Claims);
    }

    [Fact]
    public async Task Another_refusal_tells_the_hero_why()
    {
        var ledger = new HeldLedger { Answer = TF2.Descent.Server.Items.LedgerResult.Refused(InventoryRefusals.NoItem) };
        var r = new LootRig(ledger: ledger.Proxy);
        var drop = r.Drop(InRoom(0));

        await r.Floor.Take(drop, SlotA, touch: false);

        var notice = Assert.Single(r.NoticesTo(SlotA));
        Assert.Equal((LootFloor.RefusedToken, InventoryRefusals.NoItem), (notice.Text, notice.Arg));
        Assert.False(drop.SeatOf(SlotA)!.Empty);
    }
}
