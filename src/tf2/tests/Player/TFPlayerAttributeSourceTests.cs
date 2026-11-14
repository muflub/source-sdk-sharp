using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Tests.Rules;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The standing attribute sources (<see cref="ITFPlayerAttributeSource"/>):
/// what a source provides is on the player's manager after every build of
/// it, so the numbers that walk the manager — the maximum health, the
/// maximum speed — show it before and after a kit regrant.
/// </summary>
public sealed class TFPlayerAttributeSourceTests
{
    private const int SoldierHealth = 200;
    private const float SoldierSpeed = 240f;

    private static AttributeDefinition Definition(int defindex, string hook, AttributeDescriptionFormat format)
        => new(defindex, "attr " + defindex, hook, format, AttributeEffectType.Positive, false, false,
               AttributeValueType.Default, MergedKeys.Block("x"));

    /// <summary>A character sheet's bonuses: +50 maximum health, 1.1 move speed.</summary>
    private sealed class Sheet : ITFPlayerAttributeSource
    {
        public AttributeContainer Bonuses { get; } = new();

        public int Asked { get; private set; }

        public Sheet()
        {
            Bonuses.Set(AttributeValue.OfFloat(Definition(9101, "add_maxhealth", AttributeDescriptionFormat.Additive), 50f));
            Bonuses.Set(AttributeValue.OfFloat(
                Definition(9102, TFMaxSpeed.MoveSpeedAttrib, AttributeDescriptionFormat.Percentage), 1.1f));
        }

        public AttributeContainer? AttributesOf(EntityHandle player)
        {
            Asked++;
            return player == PlayerRig.Player ? Bonuses : null;
        }
    }

    private sealed class Game
    {
        public PlayerRig Rig { get; } = new();
        public TFLoadoutProvider Loadout { get; }
        public TFPlayerResupply Resupply { get; }

        public Game(params ITFPlayerAttributeSource[] sources)
        {
            Rig.Engine.Returns("Clock.Now", 5f);
            Rig.Engine.Returns("Players.GetHandle", PlayerRig.Player);
            Rig.Engine.Returns("Players.GetActiveWeapon", string.Empty);
            Rig.Engine.Returns("Precache.PrecacheModel", 0);
            Rig.As(TFClass.Soldier);

            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), Rig.Schema, Rig.Log),
                                               Rig.Schema, new TFWeaponOwnersTests.NoClients(), Rig.Log);
            Loadout = LoadoutDeps.Loadout(inventory, Rig.Schema, classes: Rig.Carriers, items: Rig.Items,
                                          table: Rig.Classes, catalog: Rig.Catalog, carriers: Rig.Carriers,
                                          hooks: new AttributeHooks(), players: Rig.Engine,
                                          precache: Rig.Engine, log: Rig.Log, attributeSources: sources);
            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine, new ListHooks<ITFConditionHook>(), Rig.Engine);
            Resupply = new TFPlayerResupply(
                Rig.Carriers, Rig.Classes, Rig.Engine, Rig.Engine, conditions, Loadout, Rig.Engine,
                Rig.Items, new AttributeHooks(),
                scripts: new TFWeaponScripts(Rig.Log, WeaponFixtures.ShippedWithClasses()),
                catalog: Rig.Catalog,
                arsenal: new Arsenal(Rig.Log, Rig.Engine, Rig.Engine, Rig.Engine, Rig.Engine, Rig.Engine, Rig.Engine,
                                     Rig.Engine, Rig.Engine, new ScriptedEntityRegistry(TestEngines.Server()),
                                     Rig.Engine, new DamageHitGroups()),
                hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                clock: Rig.Engine, equipHooks: new ListHooks<ITFWeaponEquipHook>());

            // The spawn's kit: the manager is built for the first time.
            Assert.NotEmpty(Loadout.Weapons(PlayerRig.Player));
        }

        /// <summary>The locker's regrant: the kit again, a new manager.</summary>
        public void Regrant() => Assert.NotEmpty(Loadout.Weapons(PlayerRig.Player));

        public int MaxHealth => Resupply.MaxHealth(PlayerRig.Player);

        public float MaxSpeed => TFMaxSpeed.Calculate(Rig.Classes, new TFMoveInputs(TFClass.Soldier, default), 999f,
                                                      new AttributeHooks(), Rig.Items.ManagerOf(PlayerRig.Index),
                                                      _ => null, PlayerRig.Index, null);
    }

    [Fact]
    public void WithNoSourceTheMaximumHealthIsTheClasss()
    {
        var game = new Game();

        Assert.Equal(SoldierHealth, game.MaxHealth);
    }

    [Fact]
    public void WithNoSourceTheMaximumSpeedIsTheClasss()
    {
        var game = new Game();

        Assert.Equal(SoldierSpeed, game.MaxSpeed, 0.01f);
    }

    [Fact]
    public void ASourcesMaximumHealthShowsOnTheFirstKit()
    {
        var game = new Game(new Sheet());

        Assert.Equal(SoldierHealth + 50, game.MaxHealth);
    }

    [Fact]
    public void ASourcesMaximumHealthShowsAfterARegrant()
    {
        var game = new Game(new Sheet());

        game.Regrant();

        Assert.Equal(SoldierHealth + 50, game.MaxHealth);
    }

    [Fact]
    public void ASourcesMoveSpeedShowsOnTheFirstKit()
    {
        var game = new Game(new Sheet());

        Assert.Equal(SoldierSpeed * 1.1f, game.MaxSpeed, 0.01f);
    }

    [Fact]
    public void ASourcesMoveSpeedShowsAfterARegrant()
    {
        var game = new Game(new Sheet());

        game.Regrant();

        Assert.Equal(SoldierSpeed * 1.1f, game.MaxSpeed, 0.01f);
    }

    [Fact]
    public void ARegrantAsksTheSourceAgain()
    {
        var sheet = new Sheet();
        var game = new Game(sheet);
        int first = sheet.Asked;

        game.Regrant();

        Assert.Equal(1, first);
        Assert.Equal(2, sheet.Asked);
    }

    [Fact]
    public void ASourcesProviderIsOnTheManagerOnceHoweverManyKits()
    {
        var sheet = new Sheet();
        var game = new Game(sheet);

        game.Regrant();
        game.Regrant();

        var manager = Assert.IsType<AttributeContainer>(game.Rig.Items.ManagerOf(PlayerRig.Index));
        Assert.Single(manager.Providers, p => ReferenceEquals(p, sheet.Bonuses));
        Assert.Single(sheet.Bonuses.Receivers); // the managers gone by were let go
    }

    [Fact]
    public void AValueTheSourceChangesShowsWithoutARebuild()
    {
        var sheet = new Sheet();
        var game = new Game(sheet);

        sheet.Bonuses.Set(AttributeValue.OfFloat(Definition(9101, "add_maxhealth", AttributeDescriptionFormat.Additive), 80f));

        Assert.Equal(SoldierHealth + 80, game.MaxHealth);
    }

    [Fact]
    public void ASourceThatStopsAnsweringIsOffTheNextManager()
    {
        var sheet = new Sheet();
        var gate = new Gated(sheet);
        var game = new Game(gate);
        Assert.Equal(SoldierHealth + 50, game.MaxHealth);

        gate.Open = false;
        game.Regrant();

        Assert.Equal(SoldierHealth, game.MaxHealth);
    }

    [Fact]
    public void ASourceThatThrowsProvidesNothingAndIsNamed()
    {
        var game = new Game(new Broken());

        Assert.Equal(SoldierHealth, game.MaxHealth);
        Assert.Contains(game.Rig.Log.Warnings(), line => line.Contains(nameof(Broken)) && line.Contains("no sheet"));
    }

    /// <summary>A source that can be told to stop answering.</summary>
    private sealed class Gated(ITFPlayerAttributeSource inner) : ITFPlayerAttributeSource
    {
        public bool Open { get; set; } = true;

        public AttributeContainer? AttributesOf(EntityHandle player) => Open ? inner.AttributesOf(player) : null;
    }

    private sealed class Broken : ITFPlayerAttributeSource
    {
        public AttributeContainer? AttributesOf(EntityHandle player) => throw new InvalidOperationException("no sheet");
    }
}
