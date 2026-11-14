using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

using TF2.Tests.Player;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Thermal Thruster at the machine (<c>CTFRocketPack::ItemPostFrame</c>,
/// tf_weapon_rocketpack.cpp:752-841): the press pops its owner off the ground
/// on its own command, and the flight force lands at the launch delay — half
/// the item meter spent, the second charge refused at an empty bar and under
/// the standing cond without the item's air-launch attribute. Over the
/// shipped script's thruster row.
/// </summary>
public sealed class TFRocketPackFacts
{
    private const int Attack = (int)InputButtons.Attack;

    private static int _nextDef = 1;

    /// <summary>An item whose named hook holds one additive value.</summary>
    private static IAttributeProvider ItemWith(string hook, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    /// <summary>The thruster's world: the extras pack behind it, the aim the
    /// launch force reads, and the owner's condition bank.</summary>
    private static TFWeaponWorld Rig(out PlayerNetState carrier)
    {
        var world = new TFWeaponWorld(extras: true);
        world.Arms.AimAnswers = true;
        world.Owners.Conditions[TFWeaponWorld.Owner] = new TFConditions(authoritative: true);
        carrier = new PlayerNetState();
        carrier.BindLocal();
        world.Arms.Carrier = carrier;
        return world;
    }

    private static bool FromGround(WeaponNetState weapon) =>
        (weapon.BehaviourMode & TFRocketPackBehaviour.LaunchedFromGroundBit) != 0;

    /// <summary>The armed launch: pop on the press command, flight at the delay.</summary>
    private static void ArmAndFly(TFWeaponWorld world, WeaponNetState w, ViewModelNetState vm)
    {
        world.Run(w, vm, Attack, now: 10f);   // arms and pops (:827-830, :806-820)
        world.Run(w, vm, buttons: 0, now: 11.5f); // flies past the delay (:823)
    }

    [Fact]
    public void The_thruster_kind_wires_its_behaviour()
    {
        Assert.Equal(TFWeaponKind.RocketPack, TFWeaponKinds.Of(WeaponFixtures.ParseExtra("tf_weapon_rocketpack")));

        // And the rows the machine reads carry it: the world's catalogue has
        // the thruster's row, the behaviour key answering the launch shell.
        var world = new TFWeaponWorld(extras: true);
        Assert.Equal(TFWeaponKinds.Key(TFWeaponKind.RocketPack), world.Row("tf_weapon_rocketpack").Behaviour);
        Assert.NotNull(world.Behaviour("tf_weapon_rocketpack"));
    }

    [Fact]
    public void The_press_pops_then_launches_after_the_delay()
    {
        var world = Rig(out PlayerNetState carrier);
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;
        carrier.Velocity = new Vector(0f, 0f, -200f);

        // The press arms and pops on its own command: the fall cancelled and
        // 350 up (:493-502), the ground flag taken (:817), the parachute on
        // (:503), and the flight's deadline and press clock stamped
        // (:513, :820). The thruster's own shot is silenced (:752's replaced
        // ItemPostFrame): no bullet, no sound.
        world.Run(w, vm, Attack, now: 10f);
        Assert.Equal(new Vector(0f, 0f, 350f), carrier.Velocity);
        Assert.True(FromGround(w));
        Assert.Equal(10.65f, w.BehaviourTimeA, 2);
        Assert.Equal(11.2f, w.NextSecondaryAttack, 2);
        Assert.True(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.ParachuteActive));
        Assert.Empty(world.Arms.Bullets);
        Assert.Empty(world.Arms.Sounds);

        // At the delay the flight lands: along the eyes at 450 × 1.8 with the
        // grounded lift (:386, :425-431), the whole velocity under the
        // preserve-convars (:465-477), half the meter spent (:547), the
        // parachute off (:530) and the rocket-pack cond the landing reads (:455-457).
        world.Run(w, vm, buttons: 0, now: 11.5f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
        Assert.Equal(0.5f, w.ItemMeter);
        Assert.Equal(0f, w.BehaviourTimeA);
        Assert.True(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.Rocketpack));
        Assert.False(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.ParachuteActive));
    }

    [Fact]
    public void Two_charges_fill_the_launches_and_an_empty_bar_refuses_a_third()
    {
        var world = Rig(out PlayerNetState carrier);
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;

        ArmAndFly(world, w, vm);
        Assert.Equal(0.5f, w.ItemMeter);

        // Half a bar is still ready (:477's 50 of 100): with the item's
        // air-launch attribute (:198-201), the second charge pops once the
        // press clock has run (:193, :820), off the launched velocity.
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith(TFRocketPackBehaviour.AirLaunchAttribute, 1f);
        world.Run(w, vm, Attack, now: 13.5f);
        Assert.Equal(new Vector(810f, 0f, 665f), carrier.Velocity);
        world.Run(w, vm, buttons: 0, now: 14.8f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
        Assert.Equal(0f, w.ItemMeter);

        // An empty bar is not ready: the next press leaves the velocity and the
        // press clock exactly as the last flight left them (:186, :477).
        world.Run(w, vm, Attack, now: 16f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
        Assert.Equal(14.7f, w.NextSecondaryAttack, 2);
    }

    [Fact]
    public void An_airborne_launch_lifts_the_airborne_scale()
    {
        var world = Rig(out PlayerNetState carrier);
        world.Owners.Airborne = true;
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;

        // The FL_ONGROUND read at the pop (:817): off the ground, the flag off.
        world.Run(w, vm, Attack, now: 10f);
        Assert.False(FromGround(w));
        Assert.Equal(new Vector(0f, 0f, 350f), carrier.Velocity);

        // Less vertical force while airborne (:392): 0.25 of the base's up.
        world.Run(w, vm, buttons: 0, now: 11.5f);
        Assert.Equal(new Vector(810f, 0f, 112.5f), carrier.Velocity);
    }

    [Fact]
    public void A_second_launch_needs_the_items_air_launch_attribute()
    {
        var world = Rig(out PlayerNetState carrier);
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;

        ArmAndFly(world, w, vm);

        // The rocket-pack cond stands (:455): without the attribute a press in
        // its shade leaves everything where the flight left it (:196-202).
        world.Run(w, vm, Attack, now: 13.5f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
        Assert.Equal(11.2f, w.NextSecondaryAttack, 2);

        // The item's value opens the press again: the pop lands and the clock
        // stamps (:198-201). The button releases between the presses, the way
        // a held trigger is one press (:827's m_afButtonPressed).
        world.Owners.Items[TFWeaponWorld.Owner] = ItemWith(TFRocketPackBehaviour.AirLaunchAttribute, 1f);
        world.Run(w, vm, buttons: 0, now: 13.7f);
        world.Run(w, vm, Attack, now: 14f);
        Assert.Equal(new Vector(810f, 0f, 665f), carrier.Velocity);
        Assert.Equal(15.2f, w.NextSecondaryAttack, 2);
    }

    /// <summary>
    /// The press at the server's own arms: the flight force answers from the
    /// engine's eye reads, so at the launch delay the owner flies along the
    /// level aim — the horizontal of 810 with the grounded lift, not the pop's
    /// plain 350 up (the hop that survived on main had no horizontal at all).
    /// The eyes answer level, so the force is the row's whole scale.
    /// </summary>
    [Fact]
    public void The_server_press_launches_along_the_eyes()
    {
        var server = new ServerPress();

        Assert.True(server.Arsenal.GiveWeapon(PlayerRig.Player, "tf_weapon_rocketpack"),
                    "the thruster is taken and deploys itself");

        // The bar full, the way the resupply leaves it at spawn.
        server.WeaponState.ItemMeter = 1f;

        // The deploy's hold clears while the hands stand idle; the press after
        // it arms and pops on its own command (:806-820).
        for (int k = 0; k < 100 && server.WeaponState.NextSecondaryAttack > server.Now; k++)
            server.Command(0);

        server.Command(Attack);
        Assert.Equal(new Vector(0f, 0f, 350f), server.PlayerState.Velocity);

        // Past the launch delay (:513, the 0.65 of the clock): the flight
        // lands as the eyes answer — along them at the row's scales (:384-448).
        for (int k = 0; k < 50; k++)
            server.Command(0);

        Assert.Equal(new Vector(810f, 0f, 315f), server.PlayerState.Velocity);
    }

    [Fact]
    public void The_landing_clears_the_cond_for_a_plain_second_launch()
    {
        var world = Rig(out PlayerNetState carrier);
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;

        ArmAndFly(world, w, vm);

        // The landing (ConditionThink's ground arm, tf_player_shared.cpp
        // :3133-3161): grounded past the press clock, the flight over, the
        // rocket-pack cond goes — stock's :3151 — and the next press is a
        // fresh launch no air-launch item is needed for.
        world.Run(w, vm, buttons: 0, now: 12f);
        Assert.False(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.Rocketpack));

        // So the plain thruster's second launch pops and flies again, the
        // last half of the bar with it (:483-516, :521-548).
        world.Run(w, vm, Attack, now: 13f);
        Assert.Equal(new Vector(810f, 0f, 665f), carrier.Velocity);
        world.Run(w, vm, buttons: 0, now: 14.3f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
        Assert.Equal(0f, w.ItemMeter);
    }

    [Fact]
    public void The_server_second_launch_fires_after_the_landing()
    {
        var server = new ServerPress();
        server.Weapons.Owners.Conditions[TFWeaponWorld.Owner] = new TFConditions(authoritative: true);

        Assert.True(server.Arsenal.GiveWeapon(PlayerRig.Player, "tf_weapon_rocketpack"),
                    "the thruster is taken and deploys itself");
        server.WeaponState.ItemMeter = 1f;

        for (int k = 0; k < 100 && server.WeaponState.NextSecondaryAttack > server.Now; k++)
            server.Command(0);

        server.Command(Attack);
        Assert.Equal(new Vector(0f, 0f, 350f), server.PlayerState.Velocity);

        for (int k = 0; k < 50; k++)
            server.Command(0);

        Assert.Equal(new Vector(810f, 0f, 315f), server.PlayerState.Velocity);

        // Idle commands to past the press clock: on the ground the landing
        // clears the cond, and the plain second press pops again.
        for (int k = 0; k < 100 && server.WeaponState.NextSecondaryAttack >= server.Now; k++)
            server.Command(0);

        Assert.False(server.Weapons.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.Rocketpack));

        server.Command(Attack);
        Assert.Equal(new Vector(810f, 0f, 665f), server.PlayerState.Velocity);
    }

    [Fact]
    public void A_ground_touch_drops_the_parachute_before_the_landing()
    {
        var world = Rig(out PlayerNetState carrier);
        var (w, vm) = world.Armed("tf_weapon_rocketpack", clip: 2, reserve: 2);
        w.ItemMeter = 1f;

        world.Run(w, vm, Attack, now: 10f);
        Assert.True(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.ParachuteActive));

        // Grounded again before the flight's delay — the head-bonk fall
        // stock's arm stands for (ConditionThink's ground arm, :3130): the
        // parachute goes at the touch, the launch still lands at its own.
        world.Run(w, vm, buttons: 0, now: 10.3f);
        Assert.False(world.Owners.ConditionsOf(TFWeaponWorld.Owner)!.Has(TFCond.ParachuteActive));

        world.Run(w, vm, buttons: 0, now: 10.7f);
        Assert.Equal(new Vector(810f, 0f, 315f), carrier.Velocity);
    }

    /// <summary>
    /// One server's world wearing the thruster: the core arsenal carrying the
    /// TF behaviours over the extras catalogue, the carried pair pre-spawned
    /// for the first give, and the eyes scripted level — the same chain the
    /// game runs when a player presses on the server side.
    /// </summary>
    private sealed class ServerPress
    {
        /// <summary>The command step, the frag facts' cadence.</summary>
        private const float Step = 0.015f;

        public readonly TFWeaponWorld Weapons = new(extras: true);
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly RecordingLog Log = new();

        public readonly EntityHandle WeaponEntity = new(0x0500);
        private readonly EntityHandle _viewModel = new(0x0501);
        private readonly Queue<EntityHandle> _created;
        private int _command;

        /// <summary>The clock, the commands' own.</summary>
        public float Now;

        public ServerPress()
        {
            _created = new Queue<EntityHandle>([WeaponEntity, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", TFWeaponWorld.Owner)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", new Vector(0f, 0f, 64f))
                .Returns("Players.IsAlive", true)
                // Level eyes: the flight force reads them whole, along x. The
                // eye the server's aim answers from beside them.
                .Returns("Entities.GetEyeAngles", Vector.Zero)
                .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .ReturnsEachCall("Clock.Now", () => Now)
                .ReturnsEachCall("Entities.Create", () => _created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "fixture_player");
            Registry.Spawn(WeaponEntity, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            Arsenal = new Arsenal(Log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog,
                                  ammo: new AmmoCatalog(() => [new TFAmmoTypes()], Log),
                                  behaviours: Weapons.Behaviours);
        }

        /// <summary>The player's predicted state, the one the server's arms hand to behaviours.</summary>
        public PlayerNetState PlayerState => ((SsPlayer)Registry.Get(PlayerRig.Player)!).State;

        /// <summary>The carried weapon's state blob, the machine's own.</summary>
        public WeaponNetState WeaponState => ((SsWeapon)Registry.Get(WeaponEntity)!).State;

        public void Command(int buttons)
        {
            Now += Step;
            var cmd = new UserCmd { CommandNumber = ++_command, Buttons = buttons };
            Arsenal.OnPlayerCommand(PlayerRig.Player, in cmd, firstTime: true);
        }
    }
}
