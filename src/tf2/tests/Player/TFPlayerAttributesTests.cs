using SourceSharp.Abi;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The player's runtime attribute list (<c>CTFPlayer::AddCustomAttribute</c>
/// and friends): the value lands on the player manager's own list where the
/// hooks walk it, the name resolves through the schema, the expiry clock
/// drops it, and an unknown name is stock's silent early return.
/// </summary>
public sealed class TFPlayerAttributesTests
{
    private const string HookClass = "mult_crit_chance";

    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerRig.Index);

    private (PlayerRig Rig, TFPlayerAttributes Attributes) RigWith(float now = 5f)
    {
        var rig = new PlayerRig();
        rig.Engine.Returns("Clock.Now", now);
        rig.Extra.Add(new TFPlayerAttributes(rig.Carriers, rig.Items, rig.Schema, rig.Engine));
        rig.Join();

        var manager = new AttributeContainer();
        rig.Items.SetManager(PlayerRig.Index, manager);
        var attributes = rig.Carriers.Part<TFPlayerAttributes>(PlayerRig.Index)!;
        return (rig, attributes);
    }

    private static AttributeHooks Hooks() => new();

    private static float Walked(PlayerRig rig, float value = 4f, string? hookClass = HookClass)
        => Hooks().Float(value, hookClass, rig.Items.ManagerOf(PlayerRig.Index));

    [Fact]
    public void AnAddedAttributeIsOnTheManagerTheHooksWalk()
    {
        var (rig, attributes) = RigWith();

        attributes.Add("crit mod disabled", 0.5f);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void AReaddReplacesTheValueAndKeepsOneEntry()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Add("crit mod disabled", 0.25f);

        Assert.Equal(1, manager.Attributes.Length);
        Assert.Equal(0.25f * 4f, Walked(rig));
    }

    [Fact]
    public void AnUnknownNameIsStocksSilentEarlyReturn()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("no such attribute", 0.5f);

        Assert.Equal(0, manager.Attributes.Length);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void RemoveTakesTheEntryOffTheManager()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Remove("crit mod disabled");

        Assert.Equal(0, manager.Attributes.Length);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void RemovingANameTheMapNeverHeldLeavesTheManagerAlone()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        manager.Set(AttributeValue.OfFloat(rig.Schema.Attribute("crit mod disabled")!, 0.5f));
        attributes.Remove("crit mod disabled");

        Assert.Equal(1, manager.Attributes.Length);
    }

    [Fact]
    public void AExpiredDurationLeavesAtTheTickAfterItsTime()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f, duration: 3f);
        attributes.Tick(8f);
        Assert.Equal(2f, Walked(rig));   // 8 > 8 is false: stock's strict compare keeps it

        attributes.Tick(9f);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void APermanentDurationOutlivesAnyTick()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Tick(1_000f);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void TheExpiryWalksOneNamePerPassAsStocksRestartLoopDoes()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f, duration: 1f);
        attributes.Tick(7f);

        Assert.Equal(4f, Walked(rig));
    }

    /// <summary>Stock's ReapplyProvision mode (<c>CTFWeaponBase::ReapplyProvision</c>,
    /// tf_weaponbase.cpp:5709-5740): an item whose <c>provide_on_active</c> mode
    /// is 1 provides its attributes to the player manager only while its weapon
    /// row is the engine's active weapon; the next grant of another active row
    /// stops it. The mode itself is stock's int hook over the item, so it reads
    /// through the same walk as every other attribute.</summary>
    [Fact]
    public void AProvideOnActiveWeaponsAttributesApplyOnlyWhileActive()
    {
        var rig = new PlayerRig().As(TFClass.Scout);

        var gameDir = Path.Combine(Path.GetTempPath(), "tf2-provide-on-active-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameDir);
        try
        {
            var clients = new SeatClients();
            clients.SteamIds[PlayerRig.Index] = 76561198000000001;
            var inventory = new LocalInventory(
                new InventoryStore(new ModInfo(key => key == ModInfo.Keys.GameDir ? gameDir : null), rig.Schema, rig.Log),
                rig.Schema, clients, rig.Log);
            inventory.OnClientActive(PlayerRig.Index);

            // The scout's scattergun, equipped over its own base item, carrying
            // the mode and a halving probe the walk can see.
            var scattergunDef = rig.Schema.BaseItem(TFClass.Scout, TFLoadoutSlot.Primary)!;
            var item = new ItemInstance(1, scattergunDef, scattergunDef.Quality,
            [
                AttributeValue.OfFloat(ProvideMode(), 1f),
                AttributeValue.OfFloat(rig.Schema.Attribute("crit mod disabled")!, 0.5f),
            ]);
            inventory.Of(PlayerRig.Index)!.Add(item);
            Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Scout, TFLoadoutSlot.Primary, item.Id));

            var loadout = new TFLoadoutProvider(inventory, rig.Schema, rig.Carriers, rig.Items, rig.Classes,
                                                 catalog: rig.Catalog, carriers: rig.Carriers,
                                                 hooks: new AttributeHooks(), players: rig.Engine);
            var hooks = new AttributeHooks();

            // The scattergun's row is active: the halving reaches the manager.
            rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_scattergun");
            Assert.Contains("tf_weapon_scattergun", loadout.Weapons(PlayerRig.Player));
            var manager = Assert.IsType<AttributeContainer>(rig.Items.ManagerOf(PlayerRig.Index));
            Assert.Equal(2f, hooks.Float(4f, HookClass, manager), 4);

            // Another row is active: the gated item stops providing, and the
            // other kit items carry no attribute of the hook class anyway.
            rig.Engine.Returns("Players.GetActiveWeapon", "tf_weapon_bat");
            loadout.Weapons(PlayerRig.Player);
            manager = Assert.IsType<AttributeContainer>(rig.Items.ManagerOf(PlayerRig.Index));
            Assert.Equal(4f, hooks.Float(4f, HookClass, manager), 4);
        }
        finally
        {
            Directory.Delete(gameDir, recursive: true);
        }
    }

    /// <summary>An int hook definition: the additive format so a mode read
    /// starting at 0 answers the number it holds (as the other int-hook facts
    /// build theirs).</summary>
    private static AttributeDefinition ProvideMode() =>
        new(30440, "attr 30440", "provide_on_active", AttributeDescriptionFormat.Additive,
            AttributeEffectType.Neutral, false, false, AttributeValueType.Default, MergedKeys.Block("x"));

    /// <summary>The one-seat host <see cref="LocalInventory"/> needs to
    /// authenticate a player and load their inventory.</summary>
    private sealed class SeatClients : IConnectedClients
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
}
