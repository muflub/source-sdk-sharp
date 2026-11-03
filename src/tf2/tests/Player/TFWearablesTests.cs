using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The <c>tf_wearable</c> cosmetics of the class loadout (Stage 2.13): one
/// entity per equipped WEARABLE-slot item at spawn (owner, player-model index
/// and team skin), the class's set replaced at the next spawn, the set gone at
/// death and gone for good at departure. Stock: the wearable arm of
/// <c>CTFPlayer::ManageRegularWeapons</c> (tf_player.cpp:4565-4660),
/// <c>CEconWearable::Equip</c>'s skin (econ_wearable.cpp:156-206), and
/// wearables that die and respawn with their player.
/// </summary>
public sealed class TFWearablesTests
{
    /// <summary>The trimmed schema's wearable: the MvM canteen, <c>item_slot</c> "action", model_player set.</summary>
    private const int Canteen = 1163;
    private const string CanteenModel = "models/player/items/mvm_loot/all_class/mvm_flask_generic.mdl";

    /// <summary>A second wearable for the class-change fact: the grappling hook, also the action slot.</summary>
    private const int GrapplingHook = 1152;

    private const int ModelIndex = 42;

    private sealed class WearableRig
    {
        public readonly PlayerRig Player = new();
        public readonly WearableDirectory Directory = new();
        public readonly LocalInventory Inventory;
        public readonly TFLoadoutProvider Loadout;
        public readonly TFWearables Wearables;
        public readonly TFWearableSpawns Spawns;

        private uint _next = 0x0003_0050;
        private bool _joined;

        public WearableRig()
        {
            Player.Engine.Returns("Precache.PrecacheModel", ModelIndex);
            Inventory = new LocalInventory(
                new InventoryStore(new ModInfo(_ => null), Player.Schema, Player.Log),
                Player.Schema, new NoSteam(), Player.Log);
            Inventory.OnClientActive(PlayerRig.Index);
            Loadout = new TFLoadoutProvider(Inventory, Player.Schema, Player.Carriers, Player.Items, Player.Classes);

            Wearables = new TFWearables(Player.Carriers, Player.Engine, Directory, Player.Engine, Player.Log);
            Player.Extra.Add(Wearables);
            Spawns = new TFWearableSpawns(Player.Carriers, Loadout, Player.Engine, Player.Log);
        }

        /// <summary>The player joins as <paramref name="cls"/> and spawns their kit and wearables.</summary>
        public WearableRig SpawnAs(TFClass cls)
        {
            if (!_joined)
            {
                // The carrier first, on the constant Create stub the rig made;
                // the wearable creates must see distinct handles, so the
                // factory only goes in once the carrier is made.
                Player.Join();
                _joined = true;
            }

            Player.Carriers.SetClass(PlayerRig.Index, cls);
            Player.Engine.ReturnsEachCall("Entities.Create", () =>
            {
                var handle = new EntityHandle(_next++);
                Directory.Add(handle, Player.Engine);
                return handle;
            });
            Spawns.OnSpawned(PlayerRig.Player, 0f);
            return this;
        }

        /// <summary>Owns and equips <paramref name="defindex"/> in the action slot of <paramref name="cls"/>.</summary>
        public ItemInstance Equip(TFClass cls, int defindex)
        {
            ItemInstance? item = Inventory.Grant(PlayerRig.Index, defindex);
            Assert.NotNull(item);
            Assert.True(Inventory.Equip(PlayerRig.Index, cls, TFLoadoutSlot.Action, item.Id));
            return item;
        }

        public void Die() => Spawns.OnPlayerDied(new PlayerDeath(PlayerRig.Player, PlayerRig.Index,
                                                                  EntityHandle.None, default, EntityHandle.None));

        public void Leave() => Player.Leave();
    }

    /// <summary>The wearable entities the rig's <c>Entities.Create</c> hands out, by handle.</summary>
    private sealed class WearableDirectory : IEntityDirectory
    {
        private readonly Dictionary<EntityHandle, TFWearableEntity> _byHandle = new();

        public void Add(EntityHandle handle, IEngineEntityRender render)
        {
            var entity = new TFWearableEntity(render);
            entity.State.BindLocal();
            _byHandle[handle] = entity;
        }

        public TFWearableEntity? Wearable(EntityHandle h) => _byHandle.GetValueOrDefault(h);

        ScriptedEntity? IEntityDirectory.Get(EntityHandle h) => _byHandle.GetValueOrDefault(h);
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className) => [];
        public bool IsRegistered(string className) => className == TFEntityNames.Wearable;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private sealed class NoSteam : IConnectedClients
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

    private static int WearableCreates(WearableRig rig)
        => rig.Player.Engine.AllOf("Entities.Create").Count(c => c.ArgAs<string>(0) == TFEntityNames.Wearable);

    [Fact]
    public void A_spawn_with_one_equipped_wearable_creates_one_tf_wearable()
    {
        var rig = new WearableRig();
        rig.Equip(TFClass.Scout, Canteen);

        rig.SpawnAs(TFClass.Scout);

        Assert.Equal(1, WearableCreates(rig));
        Assert.Equal(1, rig.Wearables.Count);

        EntityHandle wearable = rig.Wearables.Wearables[0];
        var entity = rig.Directory.Wearable(wearable)!;
        Assert.Equal(PlayerRig.Index, entity.State.OwnerIndex);
        Assert.Equal(Canteen, entity.State.ItemDefIndex);
        Assert.Equal(ModelIndex, entity.State.ModelIndex);

        // The index is the item's own player model, precached as it is equipped.
        Assert.Equal(CanteenModel, Assert.Single(rig.Player.Engine.AllOf("Precache.PrecacheModel")).ArgAs<string>(0));
        Assert.Contains(rig.Player.Engine.AllOf("Entities.Spawn"), c => c.ArgAs<EntityHandle>(0) == wearable);
    }

    [Fact]
    public void Respawning_as_another_class_removes_the_old_wearable_and_creates_the_classs()
    {
        var rig = new WearableRig();
        rig.Equip(TFClass.Scout, Canteen);
        rig.Equip(TFClass.Soldier, GrapplingHook);

        rig.SpawnAs(TFClass.Scout);
        int wornBefore = rig.Wearables.Count;
        EntityHandle scoutWearable = rig.Wearables.Wearables[0];
        Assert.Equal(Canteen, rig.Directory.Wearable(scoutWearable)!.State.ItemDefIndex);

        rig.SpawnAs(TFClass.Soldier);

        Assert.Equal(1, wornBefore);
        Assert.Equal(1, rig.Wearables.Count);

        // The old one is gone: removed once, and the survivor is the new class's item.
        var removed = Assert.Single(rig.Player.Engine.AllOf("Entities.Remove"));
        Assert.Equal(scoutWearable, removed.ArgAs<EntityHandle>(0));
        EntityHandle soldierWearable = rig.Wearables.Wearables[0];
        Assert.NotEqual(scoutWearable, soldierWearable);
        Assert.Equal(GrapplingHook, rig.Directory.Wearable(soldierWearable)!.State.ItemDefIndex);

        Assert.Equal(2, WearableCreates(rig));
    }

    [Fact]
    public void Death_removes_the_wearables()
    {
        var rig = new WearableRig();
        rig.Equip(TFClass.Scout, Canteen);
        rig.SpawnAs(TFClass.Scout);
        EntityHandle wearable = rig.Wearables.Wearables[0];

        rig.Die();

        Assert.Equal(0, rig.Wearables.Count);
        Assert.Equal(wearable, Assert.Single(rig.Player.Engine.AllOf("Entities.Remove")).ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void A_departure_leaves_no_wearable_behind()
    {
        var rig = new WearableRig();
        rig.Equip(TFClass.Scout, Canteen);
        rig.SpawnAs(TFClass.Scout);
        EntityHandle wearable = rig.Wearables.Wearables[0];

        // The carrier leaves too; count only the wearable's removals.
        int removedBefore = rig.Player.Engine.AllOf("Entities.Remove").Count(c => c.ArgAs<EntityHandle>(0) == wearable);

        rig.Leave();

        int removedAfter = rig.Player.Engine.AllOf("Entities.Remove").Count(c => c.ArgAs<EntityHandle>(0) == wearable);
        Assert.Equal(0, removedBefore);
        Assert.Equal(1, removedAfter);
        Assert.NotEqual(removedBefore, removedAfter);
        Assert.Equal(0, rig.Wearables.Count);
    }

    [Theory]
    [InlineData(TFTeam.Red, 0)]
    [InlineData(TFTeam.Blue, 1)]
    public void The_wearable_shows_its_owners_team_skin(TFTeam team, int skin)
    {
        var rig = new WearableRig();
        rig.Equip(TFClass.Scout, Canteen);
        rig.Player.Engine.Returns("Players.GetTeam", (int)team);

        rig.SpawnAs(TFClass.Scout);

        Assert.Equal(skin, rig.Directory.Wearable(rig.Wearables.Wearables[0])!.State.Skin);
    }

    /// <summary>A class with nothing equipped in a wearable slot wears nothing (the base loadout has no cosmetics).</summary>
    [Fact]
    public void An_empty_wardrobe_wears_nothing()
    {
        var rig = new WearableRig();

        rig.SpawnAs(TFClass.Scout);

        Assert.Equal(0, rig.Wearables.Count);
        Assert.Equal(0, WearableCreates(rig));
    }

    /// <summary>
    /// The connect's first spawn comes before the player's components attach,
    /// and the player has no class yet: nothing to wear, and nothing to say
    /// (seen live: it warned "has no wearable component" at every join).
    /// </summary>
    [Fact]
    public void A_classless_spawn_before_the_components_attach_is_quiet()
    {
        var rig = new WearableRig();

        rig.Spawns.OnSpawned(PlayerRig.Player, 0f);

        Assert.DoesNotContain(rig.Player.Log.Lines, l => l.Text.Contains("[TF2/Wearables]"));
    }
}
