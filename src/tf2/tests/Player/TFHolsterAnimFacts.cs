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
/// The holster animation's hold on a weapon switch — stock's
/// <c>Weapon_Switch</c> reads <c>holster_anim_time</c> off the outgoing
/// weapon's item: the first ask starts the anim and answers no, asks inside
/// the window answer no, and once it passes the switch walks
/// (<c>tf_player_shared.cpp:11798-11826</c>). The pair rides the real
/// command loop, the same one the hold facts run.
/// </summary>
public sealed class TFHolsterAnimFacts
{
    /// <summary>
    /// The holstered pair's first ask is Deferred — the anim's length is the
    /// item's own word — asks inside the window keep the sandwich in the
    /// hands, and the ask past the window's 0.8 s completes the switch.
    /// </summary>
    [Fact]
    public void TheHolsterAnimHoldsTheSwitchUntilItsWindowPasses()
    {
        var world = HolsterWorld.Hold(holsterSeconds: 0.8f);
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HolsterWorld.Lunchbox));
        Assert.Equal(SwitchOutcome.AlreadyHeld, world.Arsenal.SwitchSlot(PlayerRig.Player, 1));   // the sandwich is already in hand
        Assert.Equal(HolsterWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        // The first ask: the anim starts, the hands stay.
        Assert.Equal(SwitchOutcome.Deferred, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));

        // Mid-window asks answer the same — the window is 0.8 s, the step 0.015.
        world.Command(0);
        Assert.Equal(SwitchOutcome.Deferred, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.Equal(HolsterWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        // Past the window: the switch walks (the 55 steps add 0.825 s).
        for (int k = 0; k < 55; k++)
            world.Command(0);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(HolsterWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));
    }

    /// <summary>
    /// An outgoing item that names no holster time switches at once — the
    /// non-zero arm's control, the same rig with the word off the item.
    /// </summary>
    [Fact]
    public void AnItemWithoutAHolsterTimeSwitchesAtOnce()
    {
        var world = HolsterWorld.Hold(holsterSeconds: null);
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, HolsterWorld.Lunchbox));
        Assert.Equal(HolsterWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(HolsterWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// The heavy's kit, the sandwich in the secondary and its item's word
    /// under the owners seam: the switch chain the game runs, over the
    /// shipped schema.
    /// </summary>
    private sealed class HolsterWorld
    {
        public const string Lunchbox = "tf_weapon_lunchbox";
        private const int LunchboxDefindex = 42;

        /// <summary>The command step, the hold facts' cadence.</summary>
        internal const float Step = 0.015f;

        public static HolsterWorld Hold(float? holsterSeconds)
        {
            AttributeContainer? item = null;
            if (holsterSeconds is { } seconds)
            {
                item = new AttributeContainer();
                item.Set(AttributeValue.OfFloat(
                    new AttributeDefinition(LunchboxDefindex, TFWeaponBehaviour.HolsterAnimAttribute,
                                            TFWeaponBehaviour.HolsterAnimAttribute,
                                            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                            false, false, AttributeValueType.Default, MergedKeys.Block("holster")),
                    seconds));
            }
            return new HolsterWorld(item);
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

        public HolsterWorld(IAttributeProvider? item)
        {
            Rig = new PlayerRig().As(TFClass.HeavyWeapons);

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
            Weapons.Owners.Classes[PlayerRig.Index] = TFClass.HeavyWeapons;
            if (item is { } held)
                Weapons.Owners.ByName[PlayerRig.Index] = new() { [Lunchbox] = held };

            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  behaviours: Weapons.Behaviours);

            var schema = ShippedSchema(log);
            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), schema, log),
                                               schema, new NoClients(), log);
            inventory.OnClientActive(PlayerRig.Index);
            var given = inventory.Grant(PlayerRig.Index, LunchboxDefindex)
                        ?? throw new InvalidOperationException($"the shipped schema has no {LunchboxDefindex}");
            Assert.True(inventory.Equip(PlayerRig.Index, TFClass.HeavyWeapons, TFLoadoutSlot.Secondary, given.Id));

            var loadout = new TFLoadoutProvider(inventory, schema, Rig.Carriers, new TFPlayerItems(),
                                                Rig.Classes, catalog: Weapons.Catalog, carriers: Rig.Carriers);
            var conditions = new TFPlayerConditions(Rig.Carriers, Rig.Engine);
            Resupply = new TFPlayerResupply(Rig.Carriers, Rig.Classes, new ArsenalPlayers(Engine, Arsenal),
                                            Rig.Engine, conditions, loadout, Rig.Engine,
                                            Weapons.Owners, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(new RecordingLog(),
                                                                         WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses())),
                                            catalog: Weapons.Catalog, arsenal: Arsenal);
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
