using SourceSharp.Abi;

using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Loot;
using TF2.Descent.Shared;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// A kill's Australium pile on the floor (the user's ruling L24): one pile, shown on every seat of the
/// drop; whichever of the killer's party takes it (by use or by touch) takes it for all, every member
/// credited his share at once; each seat's owner is told it went, and a drop that held nothing else
/// leaves the floor.
/// </summary>
public sealed class LootPileTests
{
    private static async Task<(LootRig Rig, FloorDrop Drop, AustraliumPile Pile)> PileDrop(long australium, bool items = false)
    {
        var r = new LootRig();
        r.Party.Members.Add(new LootMember(SlotC, C, new Vector(110, 110, 0)));
        var pile = (await r.Memory.KillAustralium(new KillPile(B, 0, "soldier", 3, australium, default))).Pile!;
        var drop = r.Floor.Place(InRoom(0), [
            new LootShare(A, SlotA, items ? r.Item(A) : null, 0),
            new LootShare(B, SlotB, items ? r.Item(B, "rocket_launcher") : null, 0),
            new LootShare(C, SlotC, null, 0),
        ], pile)!;
        return (r, drop, pile);
    }

    private static long Purse(LootRig r, string hero) => r.Memory.Find(hero)?.Inventory.Australium ?? 0;

    [Fact]
    public async Task Every_seat_shows_the_pile_to_its_owner()
    {
        var (r, drop, _) = await PileDrop(31);

        foreach (int slot in new[] { SlotA, SlotB, SlotC })
        {
            var seat = r.SeatsSentTo(slot)[^1].Seat;
            Assert.Equal(31, seat.Australium);
            Assert.Equal((byte)LootHolds.Australium, seat.Holds);
            Assert.Equal(SourceSharp.Shared.TransmitAnswer.Default, r.SentTo(drop, slot));
        }
    }

    [Fact]
    public async Task One_member_takes_the_pile_and_every_member_is_credited_his_share_at_once()
    {
        var (r, drop, _) = await PileDrop(31);

        var outcome = await r.Floor.Take(drop, SlotC, touch: true);

        Assert.Equal(TakeOutcome.Taken, outcome);
        // The killer (B) first, then by slot: B, A, C; the remainder's unit is B's.
        var split = LootRules.Split(31, 3);
        Assert.Equal((split[0], split[1], split[2]), (Purse(r, B), Purse(r, A), Purse(r, C)));
        Assert.Empty(r.Memory.Piles);
        Assert.Equal(0, drop.Pile!.Australium);
    }

    [Fact]
    public async Task When_the_pile_goes_every_seats_owner_is_told_and_a_drop_of_nothing_else_leaves()
    {
        var (r, drop, _) = await PileDrop(31);
        var entity = drop.Entity!;

        await r.Floor.Take(drop, SlotA, touch: false);

        foreach (int slot in new[] { SlotA, SlotB, SlotC })
            Assert.Equal((byte)LootHolds.None, r.SeatsSentTo(slot)[^1].Seat.Holds);
        Assert.DoesNotContain(drop, r.Floor.Drops);
        Assert.True(entity.Parked);
    }

    [Fact]
    public async Task Taking_the_pile_leaves_the_others_items_on_their_seats()
    {
        var (r, drop, _) = await PileDrop(31, items: true);

        await r.Floor.Take(drop, SlotC, touch: true);

        Assert.NotNull(drop.SeatOf(SlotA)!.Item);
        Assert.Equal(0, drop.SeatOf(SlotA)!.Shown);
        Assert.Equal((byte)LootHolds.Item, r.SeatsSentTo(SlotA)[^1].Seat.Holds);
        Assert.Contains(drop, r.Floor.Drops);
    }

    [Fact]
    public async Task The_pile_is_asked_for_with_the_killers_party_on_the_level()
    {
        var held = new HeldLedger();
        var r = new LootRig(ledger: held.Proxy);
        r.Party.Members.Add(new LootMember(SlotC, C, new Vector(110, 110, 0)));
        r.Party.Strangers.Add(C);
        var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, null, 0), new LootShare(B, SlotB, null, 0)],
            new AustraliumPile(5, 10, B))!;

        await r.Floor.Take(drop, SlotA, touch: true);

        var claim = Assert.Single(held.PileClaims);
        Assert.Equal((A, 5L), (claim.Hero, claim.Pile));
        Assert.Equal([B, A], claim.Members);
    }

    [Fact]
    public async Task A_second_take_of_a_taken_pile_asks_nothing()
    {
        var (r, drop, _) = await PileDrop(31, items: true);
        await r.Floor.Take(drop, SlotC, touch: true);
        long before = Purse(r, A);

        var again = await r.Floor.Take(drop, SlotA, touch: true);

        Assert.Equal(TakeOutcome.Nothing, again);
        Assert.Equal(before, Purse(r, A));
    }
}
