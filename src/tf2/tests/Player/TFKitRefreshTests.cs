using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Tests.Rules;
using TF2.Tests.Weapons;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace TF2.Tests.Player;

/// <summary>
/// The kit refresh (<see cref="ITFKitRefresh"/>): a living soldier's kit
/// settled to a changed item source over a live arsenal — the hands follow,
/// and the health, the pools, the kept weapons and the player's place do not
/// move.
/// </summary>
public sealed class TFKitRefreshTests
{
    private const int Seat = 3;
    private const string Launcher = "tf_weapon_rocketlauncher";
    private const string Shotgun = "tf_weapon_shotgun_soldier";
    private const string Mangler = "tf_weapon_particle_cannon";

    /// <summary>The soldier's primary slot as a fact sets it; null leaves the inventory's base launcher.</summary>
    private sealed class Primary : ITFItemSource
    {
        public ItemInstance? Item { get; set; }

        /// <summary>The secondary slot beside it, for the fact that changes two slots at once.</summary>
        public ItemInstance? Secondary { get; set; }

        public TFSlotItem ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot)
            => Item is not null && slot == TFLoadoutSlot.Primary ? TFSlotItem.Of(Item)
             : Secondary is not null && slot == TFLoadoutSlot.Secondary ? TFSlotItem.Of(Secondary)
             : TFSlotItem.NoOpinion;
    }

    private sealed class Soldiers : ITFPlayerClasses
    {
        public TFClass ClassOf(EntityHandle player) => TFClass.Soldier;
    }

    private sealed class EquipLedger : ITFWeaponEquipHook
    {
        public int Asked { get; private set; }

        public void OnEquipped(EntityHandle player) => Asked++;
    }

    /// <summary>A spawned soldier holding his kit's launcher, over a live arsenal.</summary>
    private sealed class Game
    {
        public readonly RecordingLog Log = new();
        public readonly RecordingEngine Engine = Mock().Ignores("Entities.IsValid")
            .Returns("Entities.GetIndex", Seat)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(10f, 20f, 30f))
            .Returns("Entities.GetEyePosition", new Vector(10f, 20f, 94f))
            .Returns("Entities.GetEyeAngles", default(Vector))
            .Returns("Entities.GetAngles", default(Vector))
            .Returns("Entities.GetVelocity", default(Vector))
            .Returns("Entities.GetHealth", 80)
            .Returns("Players.IsAlive", true)
            .Returns("Players.GetTeam", (int)TFTeam.Red)
            .Returns("Players.GetHandle", EntityHandle.None)
            .Returns("Players.GetActiveWeapon", string.Empty)
            .Returns("Precache.PrecacheModel", 0)
            .Returns("EntityRender.LookupSequence", -1)
            .Returns("EntityRender.LookupSequenceByActivity", -1)
            .Returns("Clock.Now", 100f);

        public readonly EntityHandle Player = new(0x0001_0000u | Seat);
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly TFPlayerItems Items = new();
        public readonly TFLoadoutProvider Loadout;
        public readonly Primary Source = new();
        public readonly EquipLedger Equips = new();
        public readonly TFKitRefresh Refresh;
        public readonly ItemSchema Schema = EconTestFiles.TrimmedSchema();

        public Game()
        {
            Engine.Returns("Entities.IsValid", true);

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = Services(out _);
            Registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

            var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
            var table = new TFClassTable(Log, files);
            var catalog = new WeaponCatalog(
                () => [new TFWeaponTypes(new TFWeaponScripts(Log, files), table, Schema)], Log);
            Arsenal = new Arsenal(Log, new TFDroppedWeaponPickupTests.SpawningEntities(Engine, Registry),
                                  Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: catalog,
                                  ammo: new AmmoCatalog(() => [new TFAmmoTypes()], Log));

            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), Schema, Log),
                                               Schema, new TFWeaponOwnersTests.NoClients(), Log);
            var carriers = new TFPlayerCarriers();
            var classes = new Soldiers();
            Loadout = LoadoutDeps.Loadout(inventory, Schema, classes, items: Items, table: table, catalog: catalog,
                                          carriers: carriers, players: Engine, precache: Engine, log: Log,
                                          sources: Source);
            Refresh = new TFKitRefresh(classes, Loadout, Arsenal, carriers, Engine,
                                       new ListHooks<ITFWeaponEquipHook>(Equips));

            // The spawn's kit, as GiveKit hands it out, and the launcher drawn.
            foreach (string weapon in Loadout.Weapons(Player))
                Arsenal.GiveWeapon(Player, weapon);
            Arsenal.Switch(Player, Launcher);
        }

        public string? Held => Arsenal.Holdings(Player).Where(h => h.Active).Select(h => h.ClassName).SingleOrDefault();

        public string[] Rows => [.. Arsenal.Holdings(Player).Select(h => h.ClassName)];

        public EntityHandle HandsEntity => Arsenal.CarriedPair(Player).Weapon;

        public int ClipOf(string weapon) => Arsenal.Holdings(Player).Single(h => h.ClassName == weapon).Clip;

        /// <summary>The stock launcher as an owned instance carrying <c>mult_dmg</c> 1.5.</summary>
        public ItemInstance RolledLauncher()
        {
            var damage = new AttributeDefinition(9001, "damage bonus", "mult_dmg", AttributeDescriptionFormat.Percentage,
                                                 AttributeEffectType.Positive, false, false,
                                                 AttributeValueType.Default, MergedKeys.Block("x"));
            return new ItemInstance(77, Schema.Item(18)!, 6, [AttributeValue.OfFloat(damage, 1.5f)]);
        }

        /// <summary>The Cow Mangler (defindex 441): another weapon row in the same slot.</summary>
        public ItemInstance CowMangler() => new(78, Schema.Item(441)!, 6, []);
    }

    [Fact]
    public void TheSpawnedSoldierHoldsHisLauncher()
    {
        // The rig's own premise, which every fact below changes from.
        var game = new Game();

        Assert.Equal(Launcher, game.Held);
        Assert.Contains(Shotgun, game.Rows);
    }

    [Fact]
    public void AfterTheSourceChangesTheHandsHoldTheNewWeapon()
    {
        var game = new Game();
        game.Source.Item = game.CowMangler();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(Mangler, game.Held);
    }

    [Fact]
    public void TheHandsFollowTheSlotTheyHeld()
    {
        // Two slots change under a soldier holding his shotgun: the primary
        // arrives first and would take the emptied hands; the slot he held
        // is the one that keeps them.
        var game = new Game();
        game.Arsenal.Switch(game.Player, Shotgun);
        Assert.Equal(Shotgun, game.Held);
        game.Source.Item = game.CowMangler();
        var shotgun = game.Schema.BaseItem(TFClass.Soldier, TFLoadoutSlot.Secondary)!;
        game.Source.Secondary = new ItemInstance(79, shotgun, 6, game.RolledLauncher().Attributes);

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(Shotgun, game.Held);
    }

    [Fact]
    public void TheWeaponTheKitNoLongerNamesIsGone()
    {
        var game = new Game();
        game.Source.Item = game.CowMangler();

        game.Refresh.Refresh(game.Player);

        Assert.DoesNotContain(Launcher, game.Rows);
    }

    [Fact]
    public void ANewItemUnderTheSameWeaponRowIsANewWeapon()
    {
        // ItemsMatch: the row's name is the same and the item is not, so the
        // weapon leaves and the loadout's arrives (ValidateWeapons) — with
        // the give's own clip, not the one the old launcher was fired down
        // to. (The hands' entity is no witness: the arsenal carries one pair
        // per player and dresses it as whichever row is held.)
        var game = new Game();
        int full = game.ClipOf(Launcher);
        game.Arsenal.SetClip(game.Player, Launcher, 1);
        game.Source.Item = game.RolledLauncher();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(Launcher, game.Held);
        Assert.True(full > 1, $"the launcher's clip was {full}");
        Assert.Equal(full, game.ClipOf(Launcher));
    }

    [Fact]
    public void TheNewItemsAttributeIsTheHeldWeapons()
    {
        var game = new Game();
        game.Source.Item = game.RolledLauncher();

        game.Refresh.Refresh(game.Player);

        Assert.Equal(150f, new AttributeHooks().Float(100f, "mult_dmg", game.Items.Of(Seat, Launcher)), 0.001f);
    }

    [Fact]
    public void TheHealthIsNotTouched()
    {
        var game = new Game();
        game.Source.Item = game.CowMangler();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(0, game.Engine.CountOf("Entities.SetHealth"));
    }

    [Fact]
    public void ThePlayerIsNotMoved()
    {
        var game = new Game();
        game.Source.Item = game.CowMangler();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.DoesNotContain(game.Engine.AllOf("Entities.SetOrigin"),
                              call => call.ArgAs<EntityHandle>(0) == game.Player);
        Assert.Equal(0, game.Engine.CountOf("Players.PlaceAtSpawn"));
    }

    [Fact]
    public void ThePoolsAreNotRefilled()
    {
        var game = new Game();
        string rockets = TFAmmoNames.Name(TFAmmo.Primary);
        game.Arsenal.GiveAmmo(game.Player, 7, rockets);
        int before = game.Arsenal.Pools(game.Player).GetValueOrDefault(rockets);
        game.Source.Item = game.CowMangler();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(7, before); // the stimulus: a pool short of its maximum
        Assert.Equal(before, game.Arsenal.Pools(game.Player).GetValueOrDefault(rockets));
    }

    [Fact]
    public void AKeptWeaponKeepsItsClip()
    {
        var game = new Game();
        game.Arsenal.SetClip(game.Player, Shotgun, 2);
        game.Source.Item = game.CowMangler();

        Assert.True(game.Refresh.Refresh(game.Player));

        Assert.Equal(2, game.ClipOf(Shotgun));
    }

    [Fact]
    public void WithNothingChangedTheRefreshAnswersNo()
    {
        var game = new Game();

        Assert.False(game.Refresh.Refresh(game.Player));
    }

    [Fact]
    public void WithNothingChangedTheHandsHoldTheSameEntity()
    {
        var game = new Game();
        EntityHandle before = game.HandsEntity;
        string[] rows = game.Rows;

        game.Refresh.Refresh(game.Player);

        Assert.NotEqual(EntityHandle.None, before);
        Assert.Equal(before, game.HandsEntity);
        Assert.Equal(rows, game.Rows);
    }

    [Fact]
    public void WithNothingChangedTheHeldWeaponIsNotGivenAgain()
    {
        // A weapon given again comes with the give's own clip (the fact
        // above this pair): the fired-down clip standing is the row untouched.
        var game = new Game();
        game.Arsenal.SetClip(game.Player, Launcher, 1);

        game.Refresh.Refresh(game.Player);

        Assert.Equal(Launcher, game.Held);
        Assert.Equal(1, game.ClipOf(Launcher));
    }

    [Fact]
    public void ASecondRefreshOfTheSameAnswerChangesNothing()
    {
        // The item compares by what it is (ItemInstance.SameAs), so a source
        // answering a fresh instance of the same item is no change.
        var game = new Game();
        game.Source.Item = game.RolledLauncher();
        game.Refresh.Refresh(game.Player);
        EntityHandle settled = game.HandsEntity;
        game.Source.Item = game.RolledLauncher();

        Assert.False(game.Refresh.Refresh(game.Player));
        Assert.Equal(settled, game.HandsEntity);
    }

    [Fact]
    public void AChangeAsksTheEquipChain()
    {
        var game = new Game();
        game.Source.Item = game.CowMangler();

        game.Refresh.Refresh(game.Player);

        Assert.Equal(1, game.Equips.Asked);
    }

    [Fact]
    public void NoChangeAsksNobody()
    {
        var game = new Game();

        game.Refresh.Refresh(game.Player);

        Assert.Equal(0, game.Equips.Asked);
    }

    [Fact]
    public void AWeaponTheKitDidNotHandOutIsLeftInTheHands()
    {
        // The builder a placement gave, a weapon off the floor: not the
        // kit's to take, as the locker's regrant leaves it.
        var game = new Game();
        game.Arsenal.GiveWeapon(game.Player, Mangler);
        game.Source.Item = game.RolledLauncher();

        game.Refresh.Refresh(game.Player);

        Assert.Contains(Mangler, game.Rows);
    }
}
