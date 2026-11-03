using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The loadout data path's server half (D2): what
/// <see cref="TFLoadoutServer"/> answers a class's ask with — the class's
/// visible slots and their equipped defindexes, the items the class can
/// use — and what <see cref="TFEquipCommand"/>, the
/// <c>tf2_equip &lt;class&gt; &lt;slot&gt; &lt;item&gt;</c> client command,
/// changes and persists. Each fact writes under a temp game directory of
/// its own.
/// </summary>
public sealed class TFLoadoutServerTests : IDisposable
{
    private const int Seat = 3;
    private const ulong Steam = 76561198000000001;
    private static readonly EntityHandle Player = new((uint)Seat);

    private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "tf2-loadout-" + Guid.NewGuid().ToString("N"));
    private readonly Clients _clients = new();
    private readonly RecordingLog _log = new();
    private readonly ItemSchema _schema = EconTestFiles.TrimmedSchema();
    private readonly Sink _sent = new();

    public void Dispose()
    {
        if (Directory.Exists(_gameDir))
            Directory.Delete(_gameDir, recursive: true);
    }

    private sealed class Clients : IConnectedClients
    {
        public Dictionary<int, ulong> SteamIds { get; } = [];
        public ulong SteamId64(int entIndex) => SteamIds.GetValueOrDefault(entIndex);
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => entIndex;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private sealed class Sink : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Payload, int Player)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((key, payload.ToArray(), player));
    }

    private InventoryStore Store() =>
        new(new ModInfo(key => key == ModInfo.Keys.GameDir ? _gameDir : null), _schema, _log);

    private LocalInventory Inventory() => new(Store(), _schema, _clients, _log);

    private LocalInventory Connected(ulong steamId = Steam)
    {
        _clients.SteamIds[Seat] = steamId;
        var inventory = Inventory();
        inventory.OnClientActive(Seat);
        return inventory;
    }

    private TFLoadoutServer Server(LocalInventory inventory) => new(inventory, _schema, _sent, _log);

    private TFEquipCommand Command(LocalInventory inventory) =>
        new(inventory, _schema, Server(inventory), _log);

    /// <summary>The reply the server would send the connected player.</summary>
    private LoadoutReply ReplyOf(TFLoadoutServer server, LocalInventory inventory, TFClass cls)
        => server.Reply(inventory.Of(Seat)!, cls);

    // ---- the reply ----

    [Fact]
    public void TheReplyNamesTheClassAndEveryVisibleSlot()
    {
        var inventory = Connected();
        var server = Server(inventory);

        var reply = ReplyOf(server, inventory, TFClass.Soldier);

        Assert.Equal((byte)TFClass.Soldier, reply.Class);
        Assert.Equal(new byte[] { 0, 1, 2, 7, 8, 10, 9 }, reply.EquippedSlots);
        // The base items in the weapon slots; the wearables with nothing
        // equipped answer the no-item marker (the screen's empty arm).
        Assert.Equal(new uint[] { 18, 10, 6, LoadoutReply.NoItem, LoadoutReply.NoItem, LoadoutReply.NoItem, LoadoutReply.NoItem }, reply.EquippedDefindexes);
    }

    [Fact]
    public void TheReplyCarriesTheEquippedItemOverTheBaseOne()
    {
        var inventory = Connected();
        var cow = inventory.Grant(Seat, 441)!;
        Assert.True(inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, cow.Id));
        var server = Server(inventory);

        var reply = ReplyOf(server, inventory, TFClass.Soldier);

        Assert.Equal(441u, reply.EquippedDefindexes[0]);
    }

    [Fact]
    public void TheOwnedListIsTheItemsTheClassCanUse()
    {
        var inventory = Connected();
        var cow = inventory.Grant(Seat, 441)!;
        var sniper = inventory.Grant(Seat, 14)!;
        var server = Server(inventory);

        var reply = ReplyOf(server, inventory, TFClass.Soldier);

        Assert.Contains(441u, reply.OwnedDefindexes);
        Assert.DoesNotContain(14u, reply.OwnedDefindexes);     // the sniper rifle, not the soldier's
        int cowIndex = reply.OwnedDefindexes.ToList().IndexOf(441u);
        Assert.Equal((byte)TFLoadoutSlot.Primary, reply.OwnedSlots[cowIndex]);
    }

    [Fact]
    public void TheOwnedListIsCutToItsMost()
    {
        var inventory = Connected();
        var scattergun = _schema.Item(13)!;
        for (ulong id = 1; id <= LoadoutReply.MaxOwned + 1; id++)
            inventory.Of(Seat)!.Add(new ItemInstance(id, scattergun, scattergun.Quality, []));
        var server = Server(inventory);

        var reply = ReplyOf(server, inventory, TFClass.Scout);

        Assert.Equal(LoadoutReply.MaxOwned, reply.OwnedDefindexes.Length);
    }

    [Fact]
    public void AnAskIsClaimedAndAnswered()
    {
        var inventory = Connected();
        var server = Server(inventory);

        Assert.True(server.OnClientMessage(Seat, LoadoutAsk.Key, [(byte)TFClass.Spy]));

        var (key, payload, player) = Assert.Single(_sent.Sent);
        Assert.Equal("TF2:loadout", key);
        Assert.Equal(Seat, player);
        var reader = new UserMessageReader(payload);
        var reply = LoadoutReply.Read(ref reader);
        Assert.Equal((byte)TFClass.Spy, reply.Class);
    }

    [Fact]
    public void AnotherKeyIsNotClaimed()
    {
        var inventory = Connected();
        var server = Server(inventory);

        Assert.False(server.OnClientMessage(Seat, "TF2:other", [(byte)TFClass.Spy]));
        Assert.False(server.OnClientMessage(Seat, LoadoutAsk.Key, [1, 2, 3]));
        Assert.Empty(_sent.Sent);
    }

    // ---- tf2_equip ----

    [Fact]
    public void AnEquipNamesTheSlotAndResendsTheReply()
    {
        var inventory = Connected();
        inventory.Grant(Seat, 441);
        var command = Command(inventory);

        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "441"]));

        Assert.Equal(441, inventory.Of(Seat)!.ItemInLoadout(TFClass.Soldier, TFLoadoutSlot.Primary, _schema)!.Defindex);
        Assert.Single(_sent.Sent);                            // the fresh reply, to this player
        Assert.Equal(Seat, _sent.Sent[0].Player);
    }

    [Fact]
    public void AnEquipPersistsAcrossAReconnect()
    {
        var first = Connected();
        first.Grant(Seat, 441);
        var command = Command(first);
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "441"]));
        first.OnClientDisconnected(Seat);

        var second = Connected();

        Assert.Equal(441, second.Of(Seat)!.ItemInLoadout(TFClass.Soldier, TFLoadoutSlot.Primary, _schema)!.Defindex);
    }

    [Fact]
    public void NoneClearsTheSlotBackToTheBaseItem()
    {
        var inventory = Connected();
        inventory.Grant(Seat, 441);
        var command = Command(inventory);
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "441"]));

        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "none"]));

        Assert.Equal(18, inventory.Of(Seat)!.ItemInLoadout(TFClass.Soldier, TFLoadoutSlot.Primary, _schema)!.Defindex);
    }

    [Fact]
    public void AllNoneClearsEverySlotOfTheClass()
    {
        var inventory = Connected();
        inventory.Grant(Seat, 441);
        var command = Command(inventory);
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "441"]));

        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "all", "none"]));

        Assert.Empty(inventory.Of(Seat)!.Equipped);
        Assert.Equal(18, inventory.Of(Seat)!.ItemInLoadout(TFClass.Soldier, TFLoadoutSlot.Primary, _schema)!.Defindex);
    }

    [Fact]
    public void ARefusedEquipChangesNothing()
    {
        var inventory = Connected();
        inventory.Grant(Seat, 441);
        var command = Command(inventory);

        // No such class word, no such slot word, no such item, an item in
        // a slot it does not fit — each claimed and inert.
        Assert.True(command.Handle(Player, "tf2_equip", ["wizard", "primary", "441"]));
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "trouser", "441"]));
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "primary", "99999"]));
        Assert.True(command.Handle(Player, "tf2_equip", ["soldier", "melee", "441"]));

        Assert.Empty(inventory.Of(Seat)!.Equipped);
        Assert.Empty(_sent.Sent);
    }

    [Fact]
    public void AnotherCommandIsNotClaimed()
    {
        var inventory = Connected();
        var command = Command(inventory);

        Assert.False(command.Handle(Player, "tf2_give", ["3", "441"]));
    }
}
