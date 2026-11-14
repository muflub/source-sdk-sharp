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
/// The jars at the kit, over a real arsenal and the TF behaviours: stock's
/// spawn fills every pool to its maximum (<c>tf_player.cpp:4246-4250</c>),
/// the effect-bar count with them, so the jar is regen-finished — count at
/// maximum, the timer <c>CheckEffectBarRegen</c> holds at zero
/// (<c>tf_weaponbase.cpp:6549-6552</c>) — and the hands hold it from the
/// first command. The lane's defect was that first command: our kit left the
/// bar at its first float, the purse derived empty, and
/// <c>ReloadOrSwitchWeapons</c> handed the rifle back the frame after the
/// deploy. Every fact here runs the whole chain — the kit, the switch, the
/// commands — never a hand-set meter.
/// </summary>
public sealed class TFJarSpawnFacts
{
    private const int Attack = (int)InputButtons.Attack;

    [Fact]
    public void AJarIsFullAtSpawn()
    {
        var world = new JarWorld();

        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(1f, world.JarMeter);
    }

    [Fact]
    public void A_denied_bar_keeps_the_charge_it_carries()
    {
        // Regenerate's deny walk (tf_player.cpp:4103-4112, and the respawn
        // walk tf_player_shared.cpp:1006-1010): the item answering over zero
        // on item_meter_resupply_denied keeps the bar it carries — the
        // refills leave the charge where it stands, unlike the plain fill of
        // AJarIsFullAtSpawn. Held to 0.25 by the denial, the refill arm would
        // otherwise slam it to the full the other facts hold.
        var world = new JarWorld(deniedBar: true);
        world.PrimeMeter(0.25f);

        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(0.25f, world.JarMeter);
    }

    /// <summary>The held item's denial, the hook's stock ordinal.</summary>
    private static AttributeDefinition DeniedBar() =>
        new(906, "item_meter_resupply_denied", "item_meter_resupply_denied",
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
            false, false, AttributeValueType.Default, MergedKeys.Block("x"));

    [Fact]
    public void ASpawnLeavesTheJarFullToo()
    {
        var world = new JarWorld();

        // The spawn the game runs: the core's grant over the TF loadout, the
        // resupply riding it as the spawn hook — stock fills the pools in the
        // spawn's InitClass as well (tf_player.cpp:4246-4250), not only under
        // the resupply.
        world.Spawn();

        Assert.Equal(1f, world.JarMeter);
    }

    [Fact]
    public void SelectingAFullJarKeepsItInHand()
    {
        var world = new JarWorld();
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, JarWorld.Jar));

        // Idle commands past the deploy: the purse the frame head derives is
        // the regen's one, HasAnyAmmo holds, and no ReloadOrSwitchWeapons ask
        // leaves the hands — the rifle takes over only under the mutation.
        for (int k = 0; k < 200; k++)
            world.Command(0);

        Assert.Equal(JarWorld.Jar, world.Arsenal.ActiveWeaponName(PlayerRig.Player));
    }

    [Fact]
    public void AfterTheThrowTheBarChargesAndTheJarComesBack()
    {
        var world = new JarWorld();
        world.Resupply.Regenerate(PlayerRig.Player);
        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, JarWorld.Jar));

        // The trigger held from the first command past the deploy: the throw,
        // then the empty purse clicking while the frame tail charges the bar,
        // and the full bar the next throw waits on nothing — the jar back,
        // spent again with the second lob.
        for (int k = 0; k < 1600; k++)
            world.Command(Attack);

        Assert.Equal(2, world.Launches.Count);
        Assert.Equal(JarWorld.Jar, world.Launches[^1].Weapon);
        Assert.Equal(JarWorld.Jar, world.Arsenal.ActiveWeaponName(PlayerRig.Player));
    }

    /// <summary>
    /// The locker's world wearing the jars: the carrier rig joined as a
    /// sniper, the kit's loadout over the shipped schema with the jar
    /// equipped in the second slot, and the core arsenal carrying the TF
    /// behaviours — the machine the commands run is the one the game runs.
    /// The carried pair are the pre-spawned entities the first give adopts.
    /// </summary>
    private sealed class JarWorld
    {
        public const string Jar = "tf_weapon_jar";

        /// <summary>The command step, the frag facts' cadence.</summary>
        private const float Step = 0.015f;

        public readonly PlayerRig Rig = new PlayerRig().As(TFClass.Sniper);
        public readonly TFWeaponWorld Weapons = new(extras: true);
        public readonly TFPlayerItems Items = new();
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly TFPlayerResupply Resupply;
        public readonly TFLoadoutProvider Loadout;
        public readonly RecordingLog Log;

        private readonly EntityHandle _weapon = new(0x0500);
        private readonly EntityHandle _viewModel = new(0x0501);
        private int _command;

        /// <summary>The clock, the commands' own.</summary>
        public float Now;

        public JarWorld(bool deniedBar = false)
        {
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
            Created = created;

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "fixture_player");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            var log = new RecordingLog();
            Log = log;
            Weapons.Owners.Classes[PlayerRig.Index] = TFClass.Sniper;
            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  behaviours: Weapons.Behaviours);

            var schema = ShippedSchema(log);
            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), schema, log),
                                               schema, new NoClients(), log);
            inventory.OnClientActive(PlayerRig.Index);
            // The denial rides the jar instance itself: the grant's hand-out
            // is the items table the refill's deny walk reads, so a held
            // item_meter_resupply_denied reaches it only through the kit.
            var jarDefinition = schema.Item(JarDefindex)
                                ?? throw new InvalidOperationException("the shipped schema has no jar");
            var jar = deniedBar
                ? new ItemInstance(1, jarDefinition, jarDefinition.Quality,
                                   [AttributeValue.OfFloat(DeniedBar(), 1f)])
                : inventory.Grant(PlayerRig.Index, JarDefindex)
                  ?? throw new InvalidOperationException("the shipped schema has no jar");
            if (deniedBar)
                inventory.Of(PlayerRig.Index)!.Add(jar);
            Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Sniper, TFLoadoutSlot.Secondary, jar.Id));

            var loadout = LoadoutDeps.Loadout(inventory, schema, classes: Rig.Carriers, items: Items,
                                                table: Rig.Classes, catalog: Weapons.Catalog, carriers: Rig.Carriers,
                                                players: Rig.Engine, precache: Rig.Engine, log: Rig.Log);
            Loadout = loadout;
            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine, new ListHooks<ITFConditionHook>(), Rig.Engine);
            Resupply = new TFPlayerResupply(Rig.Carriers, Rig.Classes, new ArsenalPlayers(Engine, Arsenal),
                                            Rig.Engine, conditions, loadout, Rig.Engine,
                                            Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(new RecordingLog(),
                                                                         WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses())),
                                            catalog: Weapons.Catalog, arsenal: Arsenal,
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: Engine, equipHooks: new ListHooks<ITFWeaponEquipHook>());
        }

        private Queue<EntityHandle> Created { get; }
        private const int JarDefindex = 58;

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

        /// <summary>
        /// The spawn the game runs: the core's grant over the TF loadout
        /// (<see cref="GameRules.OnPlayerSpawned"/>), the resupply riding it
        /// as the spawn hook — the machine the live spawn is.
        /// </summary>
        public void Spawn()
        {
            var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
            convars.RegisterAll(typeof(GameRules).Assembly);
            var rules = new GameRules(Log, convars.Holder<GameRulesConVars>(), Engine,
                                      new ArsenalPlayers(Engine, Arsenal), Engine,
                                      new Scoreboard(Engine, new PlayerDisconnectEvent(), Log),
                                      new PlayerDeathEvent(),
                                      loadout: new ListHooks<ILoadoutProvider>(Loadout),
                                      spawnHooks: new ListHooks<IPlayerSpawnHook>(Resupply));
            rules.OnPlayerSpawned(PlayerRig.Player, Now);
        }

        /// <summary>The carried bar, as the live blob holds it.</summary>
        public float JarMeter => ((SsWeapon)Registry.Get(_weapon)!).State.ItemMeter;

        /// <summary>Sets the carried bar before a refill runs, so a fact can
        /// tell "kept" from "filled": the denial must leave this value.</summary>
        public void PrimeMeter(float meter) => ((SsWeapon)Registry.Get(_weapon)!).State.ItemMeter = meter;

        public IReadOnlyList<(string Weapon, ShotDamage Shot, float Charge)> Launches => Weapons.Effects.Projectiles;
    }

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

    /// <summary>The hooks over the named ones, the tests' own.</summary>
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;

        public void Each(string what, Action<T> call)
        {
            foreach (var h in hooks)
                call(h);
        }
    }
}
