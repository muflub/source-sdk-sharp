using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Server.Loot;
using TF2.Descent.Shared;

using Xunit;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// The floor's pool and rooms (plan_descent_logic WO2, section 2): 64
/// entities made once at level ready and parked, lent and parked again and
/// never made in play; a 65th drop in woken rooms folds the oldest to data and
/// loses nothing; a sleeping room's drops are data and come back when it
/// wakes; a new level starts with an empty floor and a full pool.
/// </summary>
public sealed class LootFloorTests
{
    // ---- the pool ----

    [Fact]
    public void Level_ready_makes_the_pool_of_64_once_and_parks_every_entity()
    {
        var r = new LootRig();

        Assert.Equal(LootFloor.PoolSize, r.Spawner.Made);
        Assert.Equal(64, r.Floor.Made);
        Assert.Equal(64, r.Floor.Free);
        Assert.Equal(64, r.Floor.PoolEdicts.After - r.Floor.PoolEdicts.Before);
        Assert.All(r.Registry.OfClass(LootNetState.ClassName).Cast<LootEntity>(), e => Assert.True(e.Parked));
        Assert.Equal(64, r.Registry.OfClass(LootNetState.ClassName).Cast<LootEntity>().Count());
    }

    [Fact]
    public void A_level_off_the_Descent_mode_makes_no_pool()
    {
        var r = new LootRig(ready: false);
        r.Mode.Active = false;

        r.Floor.OnLevelReady("ctf_2fort");

        Assert.Equal(0, r.Spawner.Made);
        Assert.False(r.Floor.Ready);
    }

    [Fact]
    public void The_pool_seats_match_the_rules_party()
    {
        Assert.Equal(LootFloor.Seats, LootRig.Rules.Loot.Seats);
    }

    [Fact]
    public async Task Drops_and_takes_reuse_the_pool_and_never_make_an_entity()
    {
        var r = new LootRig();

        for (int i = 0; i < 150; i++)
        {
            var drop = r.Floor.Place(InRoom(0), [new LootShare(A, SlotA, null, 5)])!;
            Assert.True(drop.OnEntity);
            Assert.Equal(TakeOutcome.Taken, (await r.Floor.Take(drop, SlotA, touch: true)));
        }

        Assert.Equal(64, r.Spawner.Made);
        Assert.Equal(64, r.Floor.Free);
        Assert.Empty(r.Floor.Drops);
        Assert.Equal(150 * 5, r.Memory.Find(A)!.Inventory.Australium);
    }

    [Fact]
    public void The_65th_drop_in_one_room_keeps_64_entities_and_loses_no_item()
    {
        var r = new LootRig();
        var drops = Enumerable.Range(0, 65).Select(_ => r.Drop(InRoom(0))).ToList();

        Assert.Equal(64, r.Spawner.Made);
        Assert.Equal(65, r.Floor.Drops.Count);
        Assert.Equal(64, r.Floor.Drops.Count(d => d.OnEntity));
        Assert.Equal(0, r.Floor.Free);
        // The oldest folded to data; the newest stands on its entity.
        Assert.False(drops[0].OnEntity);
        Assert.True(drops[64].OnEntity);
        // Every item of every seat is still on the floor and still on the ledger's floor.
        var held = r.Floor.Drops.SelectMany(d => d.Seats).Select(s => s.Item!.Id).ToHashSet();
        Assert.Equal(130, held.Count);
        Assert.True(held.SetEquals(r.Memory.Floor.Select(f => f.Id)));
    }

    [Fact]
    public async Task When_an_entity_frees_the_newest_drop_that_is_data_takes_it()
    {
        var r = new LootRig();
        var drops = Enumerable.Range(0, 66).Select(_ => r.Drop(InRoom(0))).ToList();
        Assert.False(drops[0].OnEntity);
        Assert.False(drops[1].OnEntity);

        Assert.Equal(TakeOutcome.Taken, (await r.Floor.Take(drops[65], SlotA, touch: false)));
        Assert.Equal(TakeOutcome.Taken, (await r.Floor.Take(drops[65], SlotB, touch: false)));

        Assert.False(drops[0].OnEntity);
        Assert.True(drops[1].OnEntity);
        Assert.Equal(64, r.Floor.Drops.Count(d => d.OnEntity));
    }

    // ---- rooms ----

    [Fact]
    public void A_drop_in_a_room_that_sleeps_becomes_data_and_its_entity_goes_back_to_the_pool()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(1));
        var entity = drop.Entity!;
        Assert.Equal(63, r.Floor.Free);

        r.Rooms.Asleep.Add(1);
        int folded = r.Floor.CheckRooms();

        Assert.Equal(1, folded);
        Assert.False(drop.OnEntity);
        Assert.True(entity.Parked);
        Assert.Equal(64, r.Floor.Free);
        Assert.Equal(TransmitAnswer.Never, r.Floor.Transmit(entity.Handle, SlotA));
    }

    [Fact]
    public void Sleep_and_wake_a_room_and_its_drops_come_back_at_the_same_place_with_the_same_seats()
    {
        var r = new LootRig();
        var drops = Enumerable.Range(0, 3).Select(i => r.Drop(InRoom(1) + new Vector(i * 10, 0, 0), australiumA: 7)).ToList();
        var before = drops.Select(d => (d.Number, d.Position, Seats: string.Join(";", d.Seats.Select(s => $"{s.Hero}:{s.Item?.Id}:{s.Australium}")))).ToList();

        r.Rooms.Asleep.Add(1);
        r.Floor.CheckRooms();
        Assert.All(drops, d => Assert.False(d.OnEntity));
        r.Rooms.Asleep.Remove(1);
        r.Floor.CheckRooms();

        Assert.Equal(3, r.Floor.Drops.Count);
        Assert.All(drops, d => Assert.True(d.OnEntity));
        Assert.Equal(before, drops.Select(d => (d.Number, d.Position, Seats: string.Join(";", d.Seats.Select(s => $"{s.Hero}:{s.Item?.Id}:{s.Australium}")))).ToList());
        Assert.All(drops, d => Assert.Equal(d.Position, d.Entity!.Position));
        Assert.Equal(61, r.Floor.Free);
    }

    [Fact]
    public void A_drop_in_a_sleeping_room_is_placed_as_data()
    {
        var r = new LootRig();
        r.Rooms.Asleep.Add(2);

        var drop = r.Drop(InRoom(2));

        Assert.False(drop.OnEntity);
        Assert.Equal(64, r.Floor.Free);
        Assert.Single(r.Floor.Drops);
    }

    [Fact]
    public void A_room_that_sleeps_tells_its_owners_their_seats_are_gone()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(1));
        int sent = r.SeatsSentTo(SlotA).Count;

        r.Rooms.Asleep.Add(1);
        r.Floor.CheckRooms();

        var last = r.SeatsSentTo(SlotA)[^1].Seat;
        Assert.Equal(sent + 1, r.SeatsSentTo(SlotA).Count);
        Assert.Equal((byte)LootHolds.None, last.Holds);
        Assert.Equal(drop.Number, last.Drop);
    }

    // ---- the level ----

    [Fact]
    public void A_second_level_starts_with_an_empty_floor_and_a_full_parked_pool()
    {
        var r = new LootRig();
        r.Drop(InRoom(0));
        r.Drop(InRoom(1));
        int levelBefore = r.Floor.Level;

        r.NextLevel();

        Assert.Empty(r.Floor.Drops);
        Assert.Equal(64, r.Floor.Free);
        Assert.Equal(64, r.Floor.Made);
        Assert.Equal(128, r.Floor.MadeEver);
        Assert.True(r.Floor.Level > levelBefore);
        var drop = r.Drop(InRoom(0));
        Assert.True(drop.OnEntity);
    }
}
