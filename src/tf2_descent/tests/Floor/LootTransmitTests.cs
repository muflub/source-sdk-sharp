using System.Reflection;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Loot;
using TF2.Descent.Shared;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// Seats stay private (plan_descent_logic WO2, R4): a drop's entity reaches
/// only the clients of heroes who own a full seat of it; its networked state
/// holds no seat; each seat goes by message to its owner's client alone. The
/// plan's proving line: client B never receives client A's seat.
/// </summary>
public sealed class LootTransmitTests
{
    // ---- who is sent the entity ----

    [Fact]
    public void The_owner_of_a_seat_is_sent_the_drop_by_the_ordinary_check()
    {
        var r = new LootRig();
        var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, r.Item(A), 0)])!;

        Assert.Equal(TransmitAnswer.Default, r.SentTo(drop, SlotA));
    }

    [Fact]
    public void Another_hero_is_never_sent_a_drop_he_owns_no_seat_of()
    {
        var r = new LootRig();
        var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, r.Item(A), 0)])!;

        Assert.Equal(TransmitAnswer.Never, r.SentTo(drop, SlotB));
    }

    [Fact]
    public void A_spectator_slot_with_no_seat_is_never_sent_the_drop()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));

        Assert.Equal(TransmitAnswer.Never, r.SentTo(drop, SlotC));
        Assert.Equal(TransmitAnswer.Never, r.SentTo(drop, 0));
    }

    [Fact]
    public async Task An_emptied_seat_stops_the_drop_being_sent_to_its_owner_alone()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));
        Assert.Equal(TransmitAnswer.Default, r.SentTo(drop, SlotA));

        Assert.Equal(TakeOutcome.Taken, (await r.Floor.Take(drop, SlotA, touch: false)));

        Assert.Equal(TransmitAnswer.Never, r.SentTo(drop, SlotA));
        Assert.Equal(TransmitAnswer.Default, r.SentTo(drop, SlotB));
    }

    [Fact]
    public void A_parked_entity_is_sent_to_no_one()
    {
        var r = new LootRig();
        var parked = r.Registry.OfClass(LootNetState.ClassName).Cast<LootEntity>().First();

        Assert.True(parked.Parked);
        Assert.Equal(TransmitAnswer.Never, r.Floor.Transmit(parked.Handle, SlotA));
        Assert.Equal(TransmitAnswer.Never, r.Floor.Transmit(parked.Handle, SlotB));
    }

    [Fact]
    public void An_entity_not_of_the_pool_is_left_to_the_ordinary_check()
    {
        var r = new LootRig();
        r.Drop(InRoom(0));

        Assert.Equal(TransmitAnswer.Default, r.Floor.Transmit(new EntityHandle((1u << 16) | 900u), SlotA));
        Assert.Equal(TransmitAnswer.Default, r.Floor.Transmit(PlayerOf(SlotB), SlotA));
    }

    [Fact]
    public void A_hero_who_leaves_takes_his_drops_with_him_and_gets_them_back_on_his_return()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));

        r.Floor.OnClientDisconnected(SlotA);
        Assert.Equal(TransmitAnswer.Never, r.SentTo(drop, SlotA));
        r.Floor.OnClientActive(SlotA);

        Assert.Equal(TransmitAnswer.Default, r.SentTo(drop, SlotA));
        Assert.Equal((byte)LootHolds.Item, r.SeatsSentTo(SlotA)[^1].Seat.Holds);
    }

    [Fact]
    public void The_transmit_rule_answers_the_floors_answer()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));
        var rule = new LootTransmit(r.Floor);

        Assert.Equal(TransmitAnswer.Default, rule.ShouldTransmit(drop.Entity!.Handle, SlotA));
        Assert.Equal(TransmitAnswer.Never, rule.ShouldTransmit(drop.Entity!.Handle, SlotC));
    }

    // ---- what each client is told ----

    [Fact]
    public void Client_B_never_receives_client_As_seat()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0), australiumA: 11, australiumB: 22);
        string aBase = drop.SeatOf(SlotA)!.Item!.Item.BaseId, bBase = drop.SeatOf(SlotB)!.Item!.Item.BaseId;
        Assert.NotEqual(aBase, bBase);

        var toB = r.SeatsSentTo(SlotB);
        var toA = r.SeatsSentTo(SlotA);

        // The stimulus: each was told his own seat.
        Assert.Contains(toA, m => m.Seat.Base == aBase && m.Seat.Australium == 11);
        Assert.Contains(toB, m => m.Seat.Base == bBase && m.Seat.Australium == 22);
        // B's bytes never carry A's item or A's Australium.
        Assert.All(toB, m => Assert.NotEqual(aBase, m.Seat.Base));
        Assert.All(toB, m => Assert.NotEqual(11, m.Seat.Australium));
        Assert.All(toB, m => Assert.DoesNotContain(aBase, Encoding.UTF8.GetString(m.Bytes)));
        // No message for A's seat goes to everyone (slot 0).
        Assert.Empty(r.SeatsSentTo(0));
    }

    [Fact]
    public void The_networked_state_carries_the_drop_number_and_no_seat()
    {
        var declared = typeof(LootNetState).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanWrite).Select(p => (p.Name, p.PropertyType)).ToList();

        Assert.Equal([("Drop", typeof(ushort))], declared);
    }

    [Fact]
    public void A_lent_entity_holds_only_its_drops_number()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0));

        Assert.Equal(drop.Number, drop.Entity!.State.Drop);
        Assert.NotEqual(0, drop.Number);
    }

    [Fact]
    public void An_unidentified_item_is_told_by_its_base_rarity_and_level_and_nothing_more()
    {
        var r = new LootRig();
        var item = r.Item(A, "rocket_launcher", identified: false);
        Assert.NotEmpty(item.Item.Affixes);
        var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, item, 0)])!;

        var told = r.SeatsSentTo(SlotA)[^1];

        Assert.Equal(("rocket_launcher", (byte)item.Item.Rarity, (byte)item.Item.ItemLevel, false, ""),
                     (told.Seat.Base, told.Seat.Rarity, told.Seat.ItemLevel, told.Seat.Identified, told.Seat.Unique));
        Assert.All(item.Item.Affixes, a => Assert.DoesNotContain(a.Id, Encoding.UTF8.GetString(told.Bytes)));
        Assert.Equal(drop.Number, told.Seat.Drop);
    }
}
