using SourceSharp.Abi;

using SourceSharp.Server;

using SourceSharp.Shared;

using SourceSharp.Tests;

using TF2.Server.Econ;

using TF2.Server.Player;

using TF2.Shared;

using TF2.Shared.Econ;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The item meters of weapons put away — stock charges every carried item's
/// meter each frame, held or holstered
/// (<c>CTFPlayerShared::UpdateItemChargeMeters</c>,
/// <c>tf_player_shared.cpp:7870-7904</c>), and the count the emptied bar
/// buys returns while the weapon waits, as the regen's finish grants it
/// (<c>EffectBarRegenFinished</c>, <c>tf_weaponbase.cpp:6567-6584</c>). The
/// lane's defect was the holstered half: our meter ticked only in the
/// hands' own frame tail, so the jar and the sandvich came back one throw
/// per life. Every fact here runs the whole chain — the kit, the throw or
/// the bite, the holster, the idle commands — never a hand-set meter; the
/// one hand-set word is the sandvich's charge rate, the shipped item's own
/// (<c>items_game.txt</c>'s <c>item_meter_charge_rate 30</c> on the
/// Sandvich), the harness's owners seam standing in for the attribute
/// component as the lunchbox facts' do.
/// </summary>
public sealed class TFItemMeterHoldFacts
{
    private const int Attack = (int)InputButtons.Attack;

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void AThrownJarComesBackWhilePutAway()
    {
        var world = new HoldWorld(TFClass.Sniper, HoldWorld.Jar, HoldWorld.JarDefindex);
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Jar));

        // The first lob, from the hands.
        for (int k = 0; k < 200 && world.Launches.Count < 1; k++)
            world.Command(Attack);

        Assert.Single(world.Launches);

        // Put away, the hands idle on the rifle: the holstered meter crosses
        // its twenty seconds on the commands, and the count it buys is back
        // — once, stock's single grant per finished charge.
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(HoldWorld.Jar, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        for (int k = 0; k < 1600; k++)          // 24 s of hold, the charge past
            world.Command(0);

        Assert.Equal(1, world.Count);
        Assert.Single(world.Launches);  // the charging spent nothing

        // And the jar it buys is the hands': the deploy credits the seconds
        // away — the grant's restarted charge resumed, not the hands'
        // leftover, nor the full the grant spent.
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Jar));
        Assert.True(world.Meter is > 0.1f and < 1f, $"restored meter {world.Meter}");

        for (int k = 0; k < 1600 && world.Launches.Count < 2; k++)
            world.Command(Attack);

        Assert.Equal(2, world.Launches.Count);
        Assert.Equal(0, world.Count);           // the second throw spends it
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void AnEatenSandvichComesBackWhilePutAway()
    {
        var world = HoldWorld.Sandvich();
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Lunchbox));
        int purse = world.Count;

        // The bite: the sandvich spent, the bar at nought — the primary's
        // taunt-eat, the behaviour's before-the-shot veto.
        for (int k = 0; k < 200 && world.Eats < 1; k++)
            world.Command(Attack);

        Assert.Equal(1, world.Eats);
        Assert.Equal(purse - 1, world.Count);

        // Put away past the sandwich's thirty: the count returns held aside,
        // as the jar's does — the one grant the window spans.
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(HoldWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        for (int k = 0; k < 2200; k++)          // 33 s of hold, the 30 past
            world.Command(0);

        Assert.Equal(purse, world.Count);
        Assert.Equal(1, world.Eats);            // no bite while holstered

        // And the second bite waits on nothing: the deploy's restored bar
        // hands the sandvich back, the eat spends it again.
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Lunchbox));
        Assert.True(world.Meter > 0f, $"restored meter {world.Meter}");

        for (int k = 0; k < 200 && world.Eats < 2; k++)
            world.Command(Attack);

        Assert.Equal(2, world.Eats);
        Assert.Equal(purse - 1, world.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void SelectingAnEmptyJarDoesNothing()
    {
        var world = new HoldWorld(TFClass.Sniper, HoldWorld.Jar, HoldWorld.JarDefindex);
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Jar));

        for (int k = 0; k < 200 && world.Launches.Count < 1; k++)
            world.Command(Attack);

        Assert.Single(world.Launches);

        // Put away and barely into the charge: the purse empty, the jar is
        // one the selection refuses — the hud's Empty bit's own predicate,
        // the press that plays the deny and selects nothing — where the
        // lane's defect bounced the player through a deploy.
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));

        for (int k = 0; k < 600; k++)           // 9 s of hold, the charge shy
            world.Command(0);

        Assert.False(world.Arsenal.Selectable(PlayerRig.Player, HoldWorld.Jar));

        // Charged, it selects: the same predicate the press reads.
        for (int k = 0; k < 1600; k++)
            world.Command(0);

        Assert.True(world.Arsenal.Selectable(PlayerRig.Player, HoldWorld.Jar));
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Jar));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void TheHolsteredChargeNeitherSpendsNorDoubles()
    {
        var world = new HoldWorld(TFClass.Sniper, HoldWorld.Jar, HoldWorld.JarDefindex);
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HoldWorld.Jar));

        for (int k = 0; k < 200 && world.Launches.Count < 1; k++)
            world.Command(Attack);

        Assert.Single(world.Launches);
        int spent = world.Count;
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));

        // Held far past the bar — three charges' worth: the hands' weapon
        // pays nothing to the charging, and the grants are the charges the
        // window spans, one apiece — never a grant a command, never none.
        var type = world.Weapons.Catalog.For(HoldWorld.Jar)
                   ?? throw new InvalidOperationException("no jar row in the catalog");
        float seconds = world.Weapons.Behaviours.For(type.Behaviour)?
                          .ItemMeterSeconds(in type, PlayerRig.Index)
                        ?? throw new InvalidOperationException("the jar row runs no behaviour");

        for (int k = 0; k < 4800; k++)          // 72 s, past three charges
            world.Command(0);

        Assert.Single(world.Launches);
        Assert.InRange(world.Count, spent + 1, spent + 1 + (int)(4800 * HoldWorld.Step / seconds));
    }

    /// <summary>
    /// A kit wearing one metered item: the carrier rig joined as the class,
    /// the kit's loadout over the shipped schema with the item in the second
    /// slot, and the core arsenal carrying the TF behaviours — the machine
    /// the commands run is the one the game runs. The carried pair are the
    /// pre-spawned entities the first give adopts.
    /// </summary>
    private sealed class HoldWorld
    {
        public const string Jar = "tf_weapon_jar";
        public const string Lunchbox = "tf_weapon_lunchbox";
        public const string Grenades = "TF_AMMO_GRENADES1";
        public const int JarDefindex = 58;
        private const int LunchboxDefindex = 42;

        /// <summary>The command step, the jar facts' cadence.</summary>
        internal const float Step = 0.015f;

        /// <summary>The sandvich's world: the heavy's, the shipped item's own thirty-second charge.</summary>
        public static HoldWorld Sandvich()
        {
            var item = new AttributeContainer();
            item.Set(AttributeValue.OfFloat(
                new AttributeDefinition(LunchboxDefindex, TFItemMeter.ChargeRateAttribute, TFItemMeter.ChargeRateAttribute,
                                        AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                        false, false, AttributeValueType.Default, MergedKeys.Block("meter")),
                30f));
            return new HoldWorld(TFClass.HeavyWeapons, Lunchbox, LunchboxDefindex, item);
        }

        public readonly PlayerRig Rig;
        public readonly TFWeaponWorld Weapons = new(extras: true);
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly TFPlayerResupply Resupply;
        public readonly RecordingLog Log;

        private readonly EntityHandle _weapon = new(0x0500);
        private readonly EntityHandle _viewModel = new(0x0501);
        private int _command;

        /// <summary>The clock, the commands' own.</summary>
        public float Now;

        public HoldWorld(TFClass playerClass, string weaponName, int defindex, IAttributeProvider? item = null)
        {
            Rig = new PlayerRig().As(playerClass);

            var created = new Queue<EntityHandle>([_weapon, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", PlayerRig.Index)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", default(Vector))
                .Returns("Entities.GetAngles", default(Vector))
                .Returns("Players.IsAlive", true)
                .Returns("Entities.GetEyeAngles", default(Vector))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .ReturnsEachCall("Clock.Now", () => Now)
                .ReturnsEachCall("Entities.Create", () => created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "fixture_player");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            var log = new RecordingLog();
            Log = log;
            Weapons.Owners.Classes[PlayerRig.Index] = playerClass;

            if (item is { } held)
                Weapons.Owners.ByName[PlayerRig.Index] = new() { [weaponName] = held };

            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  behaviours: Weapons.Behaviours);

            var schema = ShippedSchema(log);
            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), schema, log),
                                               schema, new NoClients(), log);
            inventory.OnClientActive(PlayerRig.Index);
            var held1 = inventory.Grant(PlayerRig.Index, defindex)
                        ?? throw new InvalidOperationException($"the shipped schema has no {defindex}");
            Assert.True(inventory.Equip(PlayerRig.Index, playerClass, TFLoadoutSlot.Secondary, held1.Id));

            var loadout = LoadoutDeps.Loadout(inventory, schema, classes: Rig.Carriers, items: new TFPlayerItems(),
                                                table: Rig.Classes, catalog: Weapons.Catalog, carriers: Rig.Carriers,
                                                players: Rig.Engine, precache: Rig.Engine, log: Rig.Log);
            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine, new TF2.Tests.Rules.ListHooks<ITFConditionHook>(), Rig.Engine);
            Resupply = new TFPlayerResupply(Rig.Carriers, Rig.Classes, new ArsenalPlayers(Engine, Arsenal),
                                            Rig.Engine, conditions, loadout, Rig.Engine,
                                            new TFPlayerItems(), new AttributeHooks(),
                                            scripts: new TFWeaponScripts(new RecordingLog(),
                                                                         WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses())),
                                            catalog: Weapons.Catalog, arsenal: Arsenal,
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: Engine, equipHooks: new TF2.Tests.Rules.ListHooks<ITFWeaponEquipHook>());
        }

        /// <summary>The shipped schema, the kit's items.</summary>
        private static ItemSchema ShippedSchema(RecordingLog log)
        {
            string path = RepoLayout.Repo("bin", RepoLayout.Config, "content", "tf", "scripts", "items", "items_game.txt");
            Assert.True(File.Exists(path), $"{path} is missing: the game's content dir holds the shipped schema");
            return EconTestFiles.Schema(File.ReadAllText(path), log);
        }

        public void Command(int buttons)
        {
            Now += Step;
            var cmd = new UserCmd { CommandNumber = ++_command, Buttons = buttons };
            Arsenal.OnPlayerCommand(PlayerRig.Player, in cmd, firstTime: true);
        }

        /// <summary>The hands' bar, as the live blob holds it.</summary>
        public float Meter => ((SsWeapon)Registry.Get(_weapon)!).State.ItemMeter;

        /// <summary>The count the pool holds, the bar's own purse.</summary>
        public int Count => Arsenal.PoolOf(PlayerRig.Player, Grenades, out int count) ? count : 0;

        public IReadOnlyList<(string Weapon, ShotDamage Shot, float Charge)> Launches => Weapons.Effects.Projectiles;

        public int Eats => Weapons.Effects.LunchboxEats;

        /// <summary>A connected client set that answers nothing: the inventory's own.</summary>
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
    }
}
