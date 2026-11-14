extern alias client;

using System.Reflection;

using TF2.Descent.Server.Loot;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientLootEntity = client::TF2.Descent.Client.Loot.ClientLootEntity;
using ClientLootSeat = client::TF2.Descent.Shared.LootSeat;
using ClientLootSeatMessage = client::TF2.Descent.Shared.LootSeatMessage;
using ClientLooks = client::TF2.Descent.Shared.LootLooks;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using ILootSeatHook = client::TF2.Descent.Shared.ILootSeatHook;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;
using LootHolds = client::TF2.Descent.Shared.LootHolds;
using LootOnFloor = client::TF2.Descent.Client.Loot.LootOnFloor;
using LootSeatView = client::TF2.Descent.Shared.LootSeatView;
using SourceSharp.Abi;

using static TF2.Descent.Tests.Floor.LootRig;

namespace TF2.Descent.Tests.Floor;

/// <summary>
/// The client's loot (plan_descent_logic WO2): the local hero's seats as the
/// server sends them, joined to the loot entities the client holds by entity
/// index and drop number; what the UI reads (<c>ILootOnFloor</c>); the model
/// a seat is drawn with; nothing kept past the level.
/// </summary>
public sealed class LootClientTests
{
    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private static LootSeatView Seat(int entity, int drop, LootHolds holds, string baseId = "shovel", long australium = 0) =>
        new(entity, drop, holds, baseId, 1, 5, true, "", australium);

    /// <summary>A client loot entity at <paramref name="index"/> holding <paramref name="drop"/>, as the registry would make it.</summary>
    private static ClientLootEntity Entity(LootOnFloor floor, ClientEngine engine, int index, ushort drop, bool dormant = false)
    {
        var e = new ClientLootEntity(engine, floor);
        typeof(ClientStandaloneEntity).GetProperty("Index")!.SetValue(e, index);
        typeof(ClientStandaloneEntity).GetProperty("Handle")!.SetValue(e, new EntityHandle((uint)((7 << 16) | index)));
        typeof(ClientStandaloneEntity).GetProperty("Entities", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(e, engine);
        e.State.BindLocal();
        e.State.Drop = drop;
        engine.ReturnsEachCall("EntityRender.IsDormant", () => dormant);
        engine.Returns("Entities.GetOrigin", new Vector(10, 20, 30));
        e.OnSpawn();
        return e;
    }

    [Fact]
    public void A_seat_is_held_for_its_entity_and_drop()
    {
        var floor = new LootOnFloor();
        var engine = new ClientEngine();
        var e = Entity(floor, engine, 70, 5);

        floor.OnLootSeat(Seat(70, 5, LootHolds.Item));

        Assert.Equal("shovel", floor.SeatOf(e)!.Base);
        var seen = Assert.Single(floor.Visible());
        Assert.Equal((70, 5, new Vector(10, 20, 30)), (seen.Entity, seen.Drop, seen.Position));
    }

    [Fact]
    public void A_seat_of_an_earlier_drop_of_the_same_entity_is_not_shown()
    {
        var floor = new LootOnFloor();
        var e = Entity(floor, new ClientEngine(), 70, 6);

        floor.OnLootSeat(Seat(70, 5, LootHolds.Item));

        Assert.Null(floor.SeatOf(e));
        Assert.Empty(floor.Visible());
    }

    [Fact]
    public void A_dormant_drop_is_not_visible()
    {
        var floor = new LootOnFloor();
        Entity(floor, new ClientEngine(), 70, 5, dormant: true);

        floor.OnLootSeat(Seat(70, 5, LootHolds.Item));

        Assert.Empty(floor.Visible());
    }

    [Fact]
    public void A_taken_seat_is_forgotten_and_an_old_drops_none_leaves_a_newer_seat()
    {
        var floor = new LootOnFloor();
        Entity(floor, new ClientEngine(), 70, 6);
        floor.OnLootSeat(Seat(70, 6, LootHolds.Item));

        floor.OnLootSeat(Seat(70, 5, LootHolds.None));
        Assert.Equal(1, floor.SeatCount);
        floor.OnLootSeat(Seat(70, 6, LootHolds.None));

        Assert.Equal(0, floor.SeatCount);
        Assert.Empty(floor.Visible());
    }

    [Fact]
    public void Every_seat_is_forgotten_when_the_level_ends()
    {
        var floor = new LootOnFloor();
        floor.OnLootSeat(Seat(70, 6, LootHolds.Item));
        floor.OnLootSeat(Seat(71, 7, LootHolds.Australium, australium: 5));

        ((IClientLevelHook)floor).OnLevelShutdown();

        Assert.Equal(0, floor.SeatCount);
    }

    [Fact]
    public void A_seat_of_Australium_alone_draws_the_currency_pack_and_an_item_the_gift()
    {
        var floor = new LootOnFloor();
        var engine = new ClientEngine();
        var money = Entity(floor, engine, 70, 5);
        var gift = Entity(floor, engine, 71, 6);

        floor.OnLootSeat(Seat(70, 5, LootHolds.Australium, australium: 12));
        floor.OnLootSeat(Seat(71, 6, LootHolds.Item | LootHolds.Australium, australium: 3));

        Assert.Equal(ClientLooks.AustraliumModel, money.ModelOverride);
        Assert.Equal("", gift.ModelOverride);
        Assert.Contains(engine.Calls, c => c.Member == "EntityRender.SetModelOverride" && c.ArgAs<string>(1) == ClientLooks.AustraliumModel);
    }

    [Fact]
    public void The_seat_the_server_writes_is_the_seat_the_client_reads()
    {
        var r = new LootRig();
        var drop = r.Drop(InRoom(0), australiumA: 17);
        var (_, bytes) = r.SeatsSentTo(SlotA)[^1];
        var floor = new LootOnFloor();

        var reader = new ClientReader(bytes);
        var seat = ClientLootSeat.Read(ref reader);
        new ClientLootSeatMessage(new Hooks<ILootSeatHook>(floor)).OnReceived(seat);

        var e = Entity(floor, new ClientEngine(), drop.Entity!.Handle.EntryIndex, drop.Number);
        var held = floor.SeatOf(e)!;
        var item = drop.SeatOf(SlotA)!.Item!.Item;
        Assert.Equal((LootHolds.Item | LootHolds.Australium, item.BaseId, item.Rarity, item.ItemLevel, item.Identified, 17L),
                     (held.Holds, held.Base, held.Rarity, held.ItemLevel, held.Identified, held.Australium));
    }
}
