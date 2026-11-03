using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Rules;

namespace TF2.Tests.Player;

/// <summary>
/// One TF player for the player lane's facts: a carrier attached through the
/// core's composite over a <see cref="RecordingEngine"/>, the class table and
/// weapon scripts as shipped, and the trimmed item schema with no inventory
/// (every slot answers its base item).
/// </summary>
internal sealed class PlayerRig
{
    public const int Index = 3;
    public static readonly EntityHandle Player = new(0x0001_0000u | Index);
    public static readonly EntityHandle CarrierHandle = new(0x0002_0041u);

    public readonly RecordingEngine Engine = new();
    public readonly RecordingLog Log = new();
    public readonly TFPlayerDataEntity Entity;
    public readonly TFPlayerCarriers Carriers = new();
    public readonly PlayerComposites Composites;
    public readonly TFClassTable Classes;
    public readonly ItemSchema Schema = EconTestFiles.TrimmedSchema();
    public readonly WeaponCatalog Catalog;
    public readonly TFLoadoutProvider Loadout;
    public readonly TFPlayerItems Items = new();
    public readonly List<IPlayerComponent> Extra = [];

    public PlayerRig()
    {
        Engine.Returns("Entities.Create", CarrierHandle);
        Engine.Returns("Entities.IsValid", true);
        Engine.Returns("Entities.GetIndex", Index);
        Engine.Returns("Entities.GetOrigin", default(Vector));
        Engine.Returns("Players.GetHandle", Player);
        Engine.Returns("Players.IsAlive", true);
        Engine.Returns("Players.GetTeam", (int)TFTeam.Red);
        Engine.Returns("Players.GiveItem", true);
        Engine.Returns("Players.GiveAmmo", 0);
        Engine.Returns("Players.GetActiveWeapon", string.Empty);
        Engine.Returns("Entities.GetHealth", 100);
        Engine.Returns("GameEvents.EventCreate", IntPtr.Zero);
        Engine.Returns("Clock.Now", 0f);

        Entity = new TFPlayerDataEntity(Engine);
        Entity.State.BindLocal();

        Classes = new TFClassTable(Log, Weapons.WeaponFixtures.ShippedWithClasses());
        Catalog = new WeaponCatalog(() => [new TFWeaponTypes(
            new TFWeaponScripts(Log, Weapons.WeaponFixtures.ShippedWithClasses()), Classes, Schema)], Log);
        var inventory = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), Schema, Log), Schema, new NoClients(), Log);
        Loadout = new TFLoadoutProvider(inventory, Schema, Carriers, Items, Classes, catalog: Catalog, carriers: Carriers);

        var source = new Source(this);
        Composites = new PlayerComposites(Log, new ListHooks<IPlayerComponentSource>(source), Engine, Engine);
    }

    private sealed class Source(PlayerRig rig) : IPlayerComponentSource
    {
        public IEnumerable<IPlayerComponent> Create(EntityHandle player)
        {
            yield return new TFPlayerCarrier(rig.Engine, new Directory(rig.Entity, rig), rig.Carriers, rig.Log);
            foreach (var extra in rig.Extra)
                yield return extra;
        }
    }

    private sealed class Directory(TFPlayerDataEntity entity, PlayerRig rig) : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle h)
            => h == CarrierHandle ? entity
               : rig._mateCarriers.TryGetValue(h, out var mate) ? mate
               : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == TFEntityNames.PlayerData;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class NoClients : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => entIndex;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    /// <summary>The player becomes active: the composite attaches the carrier (and <see cref="Extra"/>).</summary>
    public PlayerRig Join()
    {
        ((IClientLifecycle)Composites).OnClientActive(Index);
        return this;
    }

    private readonly Dictionary<EntityHandle, ScriptedEntity> _mateCarriers = new();

    /// <summary>Another index becomes active with a carrier of its own, as
    /// for a teammate the roster counts against a class limit: its attach
    /// window answers <see cref="IEngineEntities"/> reads for that player
    /// alone (its own carrier entity and index), then the local reads stand
    /// again.</summary>
    public void Join(int index)
    {
        var carrier = new EntityHandle(0x0002_0200u | (uint)index);
        var entity = new TFPlayerDataEntity(Engine);
        entity.State.BindLocal();
        _mateCarriers[carrier] = entity;

        Engine.Returns("Entities.Create", carrier);
        Engine.Returns("Entities.GetIndex", index);
        ((IClientLifecycle)Composites).OnClientActive(index);
        Engine.Returns("Entities.Create", CarrierHandle);
        Engine.Returns("Entities.GetIndex", Index);
    }

    /// <summary>The player leaves.</summary>
    public void Leave() => ((IClientLifecycle)Composites).OnClientDisconnected(Index);

    /// <summary>The carrier's state (after <see cref="Join"/>).</summary>
    public TFPlayerNetState State => Carriers.Get(Index)!;

    /// <summary>Joins and makes the player <paramref name="cls"/>.</summary>
    public PlayerRig As(TFClass cls)
    {
        if (Carriers.Get(Index) is null)
            Join();
        Carriers.SetClass(Index, cls);
        return this;
    }
}

/// <summary>
/// A giver with no inventory: every pool unreadable, which the carrier's
/// mirror writes as an empty row. For the blocks whose facts never ask.
/// </summary>
internal sealed class PoollessGiver : IWeaponGiver
{
    public bool GiveWeapon(EntityHandle player, string className) => false;

    public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;
}

