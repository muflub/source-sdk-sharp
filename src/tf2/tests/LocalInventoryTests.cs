using SourceSharp.Abi;

using TF2.Server.Commands;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The local inventory and the loadout provider (stage1 1.6): default
/// loadouts from the schema's base items, grants, equips, the JSON file per
/// SteamID, and the provider's weapon column. Each fact writes under a temp
/// game directory of its own.
/// </summary>
public sealed class LocalInventoryTests : IDisposable
{
    private const int Seat = 3;
    private const ulong Steam = 76561198000000001;

    private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "tf2-inventory-" + Guid.NewGuid().ToString("N"));
    private readonly Clients _clients = new();
    private readonly RecordingLog _log = new();
    private readonly ItemSchema _schema = EconTestFiles.TrimmedSchema();

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

    private sealed class Classes(TFClass cls) : ITFPlayerClasses
    {
        public TFClass ClassOf(EntityHandle player) => cls;
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

    private static EntityHandle Player => new((uint)Seat);

    private int[] Defindexes(LocalInventory inventory, TFClass cls) =>
        new TFLoadoutProvider(inventory, _schema).Loadout(Seat, cls).Select(e => e.Item.Defindex).ToArray();

    [Theory]
    [InlineData(TFClass.Scout, new[] { 13, 22, 0 })]
    [InlineData(TFClass.Sniper, new[] { 14, 16, 3 })]
    [InlineData(TFClass.Soldier, new[] { 18, 10, 6 })]
    [InlineData(TFClass.Demoman, new[] { 19, 20, 1 })]
    [InlineData(TFClass.Medic, new[] { 17, 29, 8 })]
    [InlineData(TFClass.HeavyWeapons, new[] { 15, 11, 5 })]
    [InlineData(TFClass.Pyro, new[] { 21, 12, 2 })]
    [InlineData(TFClass.Spy, new[] { 24, 4, 735, 27, 30 })]
    [InlineData(TFClass.Engineer, new[] { 9, 22, 7, 28, 25, 26 })]
    public void TheDefaultLoadoutFillsEveryWeaponSlotWithItsBaseItem(TFClass cls, int[] expected)
    {
        var inventory = Connected();

        Assert.Equal(expected, Defindexes(inventory, cls));
        Assert.All(new TFLoadoutProvider(inventory, _schema).Loadout(Seat, cls), e =>
        {
            Assert.True(e.Item.IsBaseItem);
            Assert.Equal(_schema.BaseItem(cls, e.Slot)?.Defindex, e.Item.Defindex);
        });
    }

    [Fact]
    public void ThePasstimeGunIsABaseItemButNotInTheLoadout()
    {
        // GetBaseItemForClass answers it for the utility slot; ItemIsAllowed refuses it outside passtime.
        Assert.Equal(1155, _schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Utility)?.Defindex);
        Assert.DoesNotContain(1155, Defindexes(Connected(), TFClass.Scout));
    }

    [Fact]
    public void TheWeaponColumnIsTheClassesItemClasses()
    {
        var provider = new TFLoadoutProvider(Connected(), _schema, new Classes(TFClass.Scout));

        Assert.Equal(new[] { "tf_weapon_scattergun", "tf_weapon_pistol", "tf_weapon_bat" }, provider.Weapons(Player));
        Assert.Empty(provider.Ammo(Player));
    }

    [Fact]
    public void WithoutAClassTheProviderHasNoOpinion()
    {
        var inventory = Connected();

        Assert.Empty(new TFLoadoutProvider(inventory, _schema).Weapons(Player));
        Assert.Empty(new TFLoadoutProvider(inventory, _schema, new Classes(TFClass.Undefined)).Weapons(Player));
        Assert.NotEmpty(new TFLoadoutProvider(inventory, _schema, new Classes(TFClass.Pyro)).Weapons(Player));
    }

    [Fact]
    public void TheCivilianHasNoLoadout()
    {
        Assert.Empty(Defindexes(Connected(), TFClass.Civilian));
    }

    [Fact]
    public void AGrantedItemIsOwned()
    {
        var inventory = Connected();

        var item = inventory.Grant(Seat, 441);

        Assert.NotNull(item);
        Assert.False(item.IsBaseItem);
        Assert.Equal(6, item.Quality);
        Assert.Same(item, inventory.Of(Seat)!.Item(item.Id));
    }

    [Fact]
    public void AnEquippedItemReplacesTheBaseItemInItsSlot()
    {
        var inventory = Connected();
        var mangler = inventory.Grant(Seat, 441)!;

        Assert.Equal(18, Defindexes(inventory, TFClass.Soldier)[0]);
        Assert.True(inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, mangler.Id));
        Assert.Equal(441, Defindexes(inventory, TFClass.Soldier)[0]);
        Assert.Equal("tf_weapon_particle_cannon",
            new TFLoadoutProvider(inventory, _schema, new Classes(TFClass.Soldier)).Weapons(Player).First());
    }

    [Fact]
    public void AnItemIsRefusedInASlotOrClassItDoesNotFit()
    {
        var inventory = Connected();
        var mangler = inventory.Grant(Seat, 441)!;

        Assert.False(inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Secondary, mangler.Id));
        Assert.False(inventory.Equip(Seat, TFClass.Scout, TFLoadoutSlot.Primary, mangler.Id));
        Assert.False(inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, 9999));
        Assert.Empty(inventory.Of(Seat)!.Equipped);
    }

    [Fact]
    public void ClearingASlotGivesTheBaseItemBack()
    {
        var inventory = Connected();
        var mangler = inventory.Grant(Seat, 441)!;
        inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, mangler.Id);

        Assert.True(inventory.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, ItemInstance.InvalidItemId));

        Assert.Equal(18, Defindexes(inventory, TFClass.Soldier)[0]);
    }

    [Fact]
    public void ASavedInventoryIsThereAfterAReconnect()
    {
        var first = Connected();
        var mangler = first.Grant(Seat, 441)!;
        first.Equip(Seat, TFClass.Soldier, TFLoadoutSlot.Primary, mangler.Id);
        first.OnClientDisconnected(Seat);

        var second = Connected();

        Assert.Equal(441, second.Of(Seat)!.Item(mangler.Id)?.Defindex);
        Assert.Equal(441, Defindexes(second, TFClass.Soldier)[0]);
    }

    [Fact]
    public void AnotherSteamIdWithoutAFileOwnsNothing()
    {
        // The differential for the fact above: the same seat, another user, no file.
        Connected().Grant(Seat, 441);

        var other = Connected(Steam + 1);

        Assert.Empty(other.Of(Seat)!.Items);
    }

    [Fact]
    public void TheFileIsNamedByTheSteamIdNeverTheSeat()
    {
        Connected().Grant(Seat, 441);

        string dir = Path.Combine(_gameDir, InventoryStore.DirectoryName);
        Assert.True(File.Exists(Path.Combine(dir, "76561198000000001.json")));
        Assert.False(File.Exists(Path.Combine(dir, Seat + ".json")));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public void AnUnknownDefindexIsRefusedAndSaid()
    {
        var inventory = Connected();

        Assert.Null(inventory.Grant(Seat, 99999));
        Assert.Empty(inventory.Of(Seat)!.Items);
        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("defindex 99999"));
        Assert.False(Directory.Exists(Path.Combine(_gameDir, InventoryStore.DirectoryName)));
    }

    [Fact]
    public void APlayerWithNoSteamIdKeepsItemsForTheSessionOnly()
    {
        var inventory = Connected(0);

        Assert.NotNull(inventory.Grant(Seat, 441));
        Assert.Single(inventory.Of(Seat)!.Items);
        Assert.False(Directory.Exists(Path.Combine(_gameDir, InventoryStore.DirectoryName)));
        Assert.Contains(_log.Lines, l => l.Text.Contains("no SteamID"));
    }

    [Fact]
    public void AnItemTheSchemaNoLongerHoldsIsDroppedOnLoad()
    {
        var store = Store();
        var saved = new PlayerInventory(Steam);
        saved.Add(new ItemInstance(1, _schema.Item(441)!, 6, []));
        store.Save(saved);
        string path = store.PathOf(Steam);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"defindex\": 441", "\"defindex\": 99999"));

        var loaded = store.Load(Steam);

        Assert.Empty(loaded.Items);
        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("defindex 99999"));
    }

    [Fact]
    public void AnUnreadableFileIsSaidAndReadAsEmpty()
    {
        var store = Store();
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.PathOf(Steam), "{ not json");

        Assert.Empty(store.Load(Steam).Items);
        Assert.Contains(_log.Lines, l => l.Warning && l.Text.Contains("unreadable"));
    }

    [Fact]
    public void AnItemsOwnAttributesRoundTripBitForBit()
    {
        var store = Store();
        var saved = new PlayerInventory(Steam);
        var def = _schema.Attribute("energy weapon no ammo")!;
        saved.Add(new ItemInstance(5, _schema.Item(441)!, 6, [new AttributeValue(def, 0x12345678u)]));
        store.Save(saved);

        var attribute = Assert.Single(store.Load(Steam).Item(5)!.Attributes);

        Assert.Same(def, attribute.Definition);
        Assert.Equal(0x12345678u, attribute.Bits);
    }

    [Fact]
    public void TheGiveCommandGrantsIntoTheInventory()
    {
        var inventory = Connected();

        new GiveItemCommand(inventory, _log).RunLine($"{Seat} 441");

        Assert.Single(inventory.Of(Seat)!.Items);
        Assert.Contains(_log.Lines, l => l.Text.Contains("granted #1"));
    }

    [Fact]
    public void TheLoadoutCommandListsTheSlots()
    {
        var inventory = Connected();
        var command = new LoadoutCommand(new TFLoadoutProvider(inventory, _schema), inventory, _log);

        string text = command.Describe(Seat, "scout");

        Assert.Contains("as Scout", text);
        Assert.Contains("Primary: base 13 TF_WEAPON_SCATTERGUN (tf_weapon_scattergun)", text);
        Assert.Contains("Melee: base 0 TF_WEAPON_BAT", text);
    }
}
