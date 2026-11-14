using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Player;

using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The timed refill's charge-type gate — stock reads the item's
/// <c>item_meter_charge_type</c> before its rate, and the clock-driven fill
/// walks only for TIME and COMBO
/// (<c>CTFPlayerShared::UpdateItemChargeMeters</c>,
/// <c>tf_player_shared.cpp:7881-7892</c>); SIMPLE and NONE leave the refill
/// to the item's own code. Both facts run the whole chain the hold facts
/// run: the kit, the bite, the holster, the idle commands.
/// </summary>
public sealed class TFItemMeterChargeGateFacts
{
    private const int Attack = (int)InputButtons.Attack;

    /// <summary>
    /// A TIME meter refills on the clock: the bitten sandvich's count is
    /// back after the hold, on the attribute's strength alone.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void ATimeMeterRefillsOnTheClock()
    {
        var world = GateWorld.Hold(chargeType: 1f);   // TIME
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, GateWorld.Lunchbox));
        int purse = world.Count;

        for (int k = 0; k < 200 && world.Eats < 1; k++)
            world.Command(Attack);

        Assert.Equal(1, world.Eats);
        Assert.Equal(purse - 1, world.Count);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(GateWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        for (int k = 0; k < 2200; k++)          // 33 s of hold, the timed window's thirty past
            world.Command(0);

        Assert.Equal(purse, world.Count);       // the refill walked
    }

    /// <summary>
    /// A SIMPLE meter does not: stock's timed walk skips it (:7882), and the
    /// count stays gone through the same hold the TIME arm spent. The pair's
    /// non-zero half is the arm above.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void ASimpleMeterDoesNotRefillOnTheClock()
    {
        var world = GateWorld.Hold(chargeType: 2f);   // SIMPLE
        world.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.Switch(PlayerRig.Player, GateWorld.Lunchbox));
        int purse = world.Count;

        for (int k = 0; k < 200 && world.Eats < 1; k++)
            world.Command(Attack);

        Assert.Equal(1, world.Eats);
        Assert.Equal(purse - 1, world.Count);

        Assert.Equal(SwitchOutcome.Switched, world.Arsenal.SwitchSlot(PlayerRig.Player, 0));
        Assert.NotEqual(GateWorld.Lunchbox, world.Arsenal.ActiveWeaponName(PlayerRig.Player));

        for (int k = 0; k < 2200; k++)          // the same hold, no refill under SIMPLE
            world.Command(0);

        Assert.Equal(purse - 1, world.Count);   // the timed walk refused, stock's :7882 skip
    }

    // ---------------------------------------------------------------------

    /// <summary>
    /// The hold rig's shape, the meter's own chain: the heavy's kit with
    /// the lunchbox in the secondary, its item naming the rate and one of
    /// the charge types through the owners seam.
    /// </summary>
    private sealed class GateWorld
    {
        public const string Lunchbox = "tf_weapon_lunchbox";
        private const int LunchboxDefindex = 42;

        /// <summary>The command step, the hold facts' cadence.</summary>
        internal const float Step = 0.015f;

        /// <summary>The kit: the sandwich's thirty under the named charge type.</summary>
        public static GateWorld Hold(float chargeType)
        {
            var item = new AttributeContainer();

            item.Set(AttributeValue.OfFloat(
                new AttributeDefinition(LunchboxDefindex, TFItemMeter.ChargeRateAttribute, TFItemMeter.ChargeRateAttribute,
                                        AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                        false, false, AttributeValueType.Default, MergedKeys.Block("meter")),
                30f));

            item.Set(AttributeValue.OfFloat(
                new AttributeDefinition(LunchboxDefindex + 1, TFItemMeter.ChargeTypeAttribute, TFItemMeter.ChargeTypeAttribute,
                                        AttributeDescriptionFormat.Or, AttributeEffectType.Neutral,
                                        false, false, AttributeValueType.Default, MergedKeys.Block("metertype")),
                chargeType));

            return new GateWorld(item);
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

        public GateWorld(IAttributeProvider item)
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
            Weapons.Owners.ByName[PlayerRig.Index] = new() { [Lunchbox] = item };

            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  behaviours: Weapons.Behaviours);

            var schema = ShippedSchema(log);
            var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), schema, log),
                                               schema, new NoClients(), log);
            inventory.OnClientActive(PlayerRig.Index);
            var held = inventory.Grant(PlayerRig.Index, LunchboxDefindex)
                       ?? throw new InvalidOperationException($"the shipped schema has no {LunchboxDefindex}");
            Assert.True(inventory.Equip(PlayerRig.Index, TFClass.HeavyWeapons, TFLoadoutSlot.Secondary, held.Id));

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

        /// <summary>The count the pool holds, the bar's own purse.</summary>
        public int Count => Arsenal.PoolOf(PlayerRig.Player, Grenades, out int count) ? count : 0;

        private const string Grenades = "TF_AMMO_GRENADES1";

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
