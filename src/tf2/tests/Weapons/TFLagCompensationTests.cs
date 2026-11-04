using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Econ;
using TF2.Server.Medigun;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Medigun;
using TF2.Tests.Projectiles;
using TF2.Tests.Spy;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <c>CTFWeaponBaseMelee::Smack</c>'s lag compensation (tf_weaponbase_melee.cpp:711-712,
/// :788): the swing's traces see the other players where the swinger saw them.
/// The victim (slot 4) ran along +x and is at x=200 now; the swinger's command
/// was sent at tick 105, when the victim stood at x=150. The swing's line hits
/// the victim only if the victim stands at x=150 when the line is traced.
/// </summary>
public sealed class TFLagCompensatedSmackTests
{
    private sealed class Rig
    {
        public MeleeRig Melee { get; } = new();
        public LagWorld World { get; } = new();
        public LagSettings Settings { get; } = new();
        public PlayerCurrentCommands Commands { get; } = new();
        public LagCompensation Lag { get; }
        public LagPlayer Swinger { get; }
        public LagPlayer Victim { get; }

        public Rig()
        {
            Swinger = World.Add(3);
            Victim = World.Add(4);
            Assert.Equal((MeleeRig.Owner, MeleeRig.Victim), (Swinger.Handle, Victim.Handle));
            Lag = new LagCompensation(World, Settings, Commands, new RecordingLog());

            for (int tick = 100; tick <= 110; tick++)
            {
                World.At(tick);
                Victim.Origin = At(tick);
                Lag.RecordFrame();
            }

            Melee.Engine.ReturnsEachCall("WorldQuery.TraceLine",
                () => Victim.Origin.Equals(At(105))
                    ? new TraceResult(true, 0.5f, At(105), new Vector(-1f, 0f, 0f), MeleeRig.Victim)
                    : new TraceResult(false, 1f, default, default, EntityHandle.None));
            Melee.TargetIsPlayer = true;
        }

        public static Vector At(int tick) => new(100f + 10f * (tick - 100), 0f, 0f);

        /// <summary>The smack, in the command sent at tick 105, as the module runs it.</summary>
        public void Smack(ILagCompensation? lag)
        {
            var effects = new TFMeleeEffects(Melee.Engine, Melee.Engine, Melee.Engine, Melee.Damage, Melee.HitGroups,
                                             Melee.Log, lag);
            Swinger.Latency = 5 * LagWorld.Tick;
            var cmd = new UserCmd { TickCount = 105, CommandNumber = 7, Buttons = (int)InputButtons.Attack };
            using (Commands.Begin(MeleeRig.Owner, in cmd))
                effects.Melee(MeleeRig.Attack(Melee.AttackArms), WeaponFixtures.Parse("tf_weapon_bat"),
                              new ShotDamage(35f),
                              new SwingTrace(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false));
        }
    }

    [Fact]
    public void A_smack_hits_the_victim_where_the_swinger_saw_it_and_the_victim_is_put_back()
    {
        var rig = new Rig();

        rig.Smack(rig.Lag);

        MeleeHit hit = Assert.Single(rig.Melee.Damage.Hits);
        Assert.Equal(MeleeRig.Victim, hit.Victim);
        Assert.Equal(Rig.At(110), rig.Victim.Origin);
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void Without_the_compensation_the_same_smack_misses()
    {
        var rig = new Rig();

        rig.Smack(null);

        Assert.Empty(rig.Melee.Damage.Hits);
    }

    [Fact]
    public void With_unlag_off_the_same_smack_misses()
    {
        var rig = new Rig();
        rig.Settings.Unlag = false;

        rig.Smack(rig.Lag);

        Assert.Empty(rig.Melee.Damage.Hits);
        Assert.Equal(Rig.At(110), rig.Victim.Origin);
    }
}

/// <summary>
/// <c>CTFPlayer::WantsLagCompensationOnEntity</c> (server/tf/tf_player.cpp:20079-20154):
/// teammates only for a melee swing or a medic's patient, with friendly fire off.
/// </summary>
public sealed class TFLagCompensationTargetsTests
{
    private static readonly EntityHandle Shooter = new(0x0001_0001u);
    private static readonly EntityHandle Mate = new(0x0001_0002u);
    private static readonly EntityHandle Enemy = new(0x0001_0003u);

    private sealed class Mediguns(int patient) : ITFMedigunWeapons
    {
        public void TickMedic(int ownerIndex, float now, float frametime) { }
        public void Holster(int ownerIndex, float now) { }
        public void Unholster(int ownerIndex, float now) { }
        public void RemoveMedigun(int ownerIndex, float now) { }
        public float ChargeLevel(int ownerIndex) => 0f;
        public void SetChargeLevel(int ownerIndex, float level) { }
        public int HealingTargetIndex(int ownerIndex) => patient;
        public bool IsReleasing(int ownerIndex) => false;
    }

    private static (TFLagCompensationTargets Rule, ConVarRegistry ConVars) Rule(string weapon, TFClass cls,
                                                                                 int patient = -1)
    {
        var engine = TestServices.Fake();
        engine.ReturnsEachCall("Players.GetTeam", () => engine.Calls[^1].Arg(0) is EntityHandle h && h == Enemy
                                                        ? (int)TFTeam.Blue : (int)TFTeam.Red);
        engine.Returns("Players.GetActiveWeapon", weapon);
        engine.ReturnsEachCall("Entities.GetIndex", () => engine.Calls[^1].Arg(0) is EntityHandle h ? h.EntryIndex : -1);
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(MpFriendlyFireConVar).Assembly);
        var scripts = new TFWeaponScripts(new RecordingLog(), WeaponFixtures.ShippedWithClasses());
        return (new TFLagCompensationTargets(convars.Declared<MpFriendlyFireConVar>(), engine, engine, scripts,
                                             new TF2.Tests.Projectiles.ProjectileRig.FixedClasses { Class = cls },
                                             new Mediguns(patient)), convars);
    }

    /// <summary>The target 1000 units off along +y while the shooter looks down +x: out of reach and cone.</summary>
    private static LagCompensationTarget Asked(EntityHandle target, float yaw = 0f)
        => new(Shooter, target, new UserCmd { Yaw = yaw }, Vector.Zero, new Vector(0f, 1000f, 0f), 320f, 1f);

    [Fact]
    public void A_teammate_is_not_compensated_for_a_gun()
    {
        var (rule, _) = Rule("tf_weapon_scattergun", TFClass.Scout);

        Assert.Equal(Decision.No, rule.WantsLagCompensationOnEntity(Asked(Mate, yaw: 90f)));
    }

    [Fact]
    public void An_enemy_in_the_cone_is_compensated_and_one_outside_is_not()
    {
        var (rule, _) = Rule("tf_weapon_scattergun", TFClass.Scout);

        Assert.Equal((Decision.Yes, Decision.No),
                     (rule.WantsLagCompensationOnEntity(Asked(Enemy, yaw: 90f)),
                      rule.WantsLagCompensationOnEntity(Asked(Enemy, yaw: 0f))));
    }

    [Fact]
    public void A_teammate_is_compensated_for_a_melee_swing_without_the_cone()
    {
        var (rule, _) = Rule("tf_weapon_shovel", TFClass.Soldier);

        Assert.Equal(Decision.Yes, rule.WantsLagCompensationOnEntity(Asked(Mate, yaw: 0f)));
    }

    [Fact]
    public void A_healing_medic_compensates_its_patient_and_no_other_teammate()
    {
        var (rule, _) = Rule("tf_weapon_medigun", TFClass.Medic, patient: Mate.EntryIndex);
        var (other, _) = Rule("tf_weapon_medigun", TFClass.Medic, patient: 9);

        Assert.Equal((Decision.Yes, Decision.No),
                     (rule.WantsLagCompensationOnEntity(Asked(Mate, yaw: 90f)),
                      other.WantsLagCompensationOnEntity(Asked(Mate, yaw: 90f))));
    }

    [Fact]
    public void With_friendly_fire_on_a_teammate_is_judged_by_the_reach_alone()
    {
        var (rule, convars) = Rule("tf_weapon_scattergun", TFClass.Scout);
        convars.Declared<MpFriendlyFireConVar>().NotifyChanged("1", "0");

        Assert.Equal(Decision.Yes, rule.WantsLagCompensationOnEntity(Asked(Mate, yaw: 90f)));
    }
}

/// <summary>
/// The knife's backstab check (<c>TFSpyComponent.Swing</c>; tf_weapon_knife.cpp:192
/// Start, :225 Finish): the swing's trace sees the other players where the spy saw
/// them. The victim (entity 4) ran along +x and is at x=200 now; the spy's command
/// was sent at tick 105, when the victim stood at x=150. The trace names the victim
/// only while the session holds the rewind.
/// </summary>
public sealed class TFKnifeBackstabLagTests
{
    private static Vector At(int tick) => new(100f + 10f * (tick - 100), 0f, 0f);

    private sealed class Rig
    {
        public LagWorld World { get; } = new();
        public PlayerCurrentCommands Commands { get; } = new();
        public LagCompensation Lag { get; }
        public LagPlayer Swinger { get; }
        public LagPlayer Victim { get; }
        public SpyRig Spy { get; }
        public List<bool> Inside { get; } = [];
        public List<Vector> SeenAt { get; } = [];

        public Rig(bool withSession = true)
        {
            Swinger = World.Add(3);
            Victim = World.Add(4);
            Assert.Equal((SpyRig.Spy, SpyRig.Victim), (Swinger.Handle, Victim.Handle));
            Lag = new LagCompensation(World, new LagSettings(), Commands, new RecordingLog());
            Spy = new SpyRig(lag: withSession ? Lag : null);
            Swinger.Latency = 5 * LagWorld.Tick;

            Spy.Engine.ReturnsEachCall("WorldQuery.TraceLine", () =>
            {
                Inside.Add(Lag.IsCurrentlyDoingLagCompensation);
                SeenAt.Add(Victim.Origin);
                return Victim.Origin.Equals(At(105))
                    ? new TraceResult(true, 1f, new Vector(60f, 2f, 63f), default, SpyRig.Victim)
                    : new TraceResult(false, 1f, default, default, EntityHandle.None);
            });

            for (int tick = 100; tick <= 110; tick++)
            {
                World.At(tick);
                Victim.Origin = At(tick);
                Lag.RecordFrame();
            }
        }

        public bool Swing()
        {
            var cmd = new UserCmd { TickCount = 105, CommandNumber = 7, Buttons = (int)InputButtons.Attack };
            using (Commands.Begin(SpyRig.Spy, in cmd))
                return Spy.Spies.Swing(SpyRig.Spy.EntryIndex, 48f);
        }
    }

    [Fact]
    public void The_backstab_trace_names_the_victim_where_the_spy_saw_him()
    {
        var rig = new Rig();

        Assert.True(rig.Swing());

        Assert.True(Assert.Single(rig.Inside));                    // the trace ran inside the session
        Assert.Equal(At(105), Assert.Single(rig.SeenAt));          // judged at the rewound spot
        SpyHit hit = Assert.Single(rig.Spy.Damage.Hits);           // health 100 doubled
        Assert.Equal((SpyRig.Victim, 200f), (hit.Victim, hit.Damage));
        Assert.Equal(At(110), rig.Victim.Origin);                  // put back after
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void Without_the_compensation_the_same_swing_misses_the_running_victim()
    {
        var rig = new Rig(withSession: false);

        Assert.False(rig.Swing());

        Assert.False(Assert.Single(rig.Inside));
        Assert.Equal(At(110), Assert.Single(rig.SeenAt));          // the trace saw the current spot
        Assert.Empty(rig.Spy.Damage.Hits);
    }
}

/// <summary>
/// The flamethrower's per-tick flame (<c>TFFlameEffects::Flames</c>;
/// tf_weapon_flamethrower.cpp:808 Start, :934 Finish): the cone work runs with the
/// other players rewound. The pyro (entity 3) fired the command at tick 105; the
/// victim (entity 5) ran along +x and is at x=200 now, at x=150 when the pyro saw
/// him. The tick's reads fall inside the session.
/// </summary>
public sealed class TFFlameTickLagTests
{
    private static Vector At(int tick) => new(100f + 10f * (tick - 100), 0f, 0f);

    private sealed class Rig
    {
        public ManagerRig Flame { get; } = new();
        public LagWorld World { get; } = new();
        public PlayerCurrentCommands Commands { get; } = new();
        public LagCompensation Lag { get; }
        public LagPlayer Swinger { get; }
        public LagPlayer Victim { get; }
        public TFFlameEffects Effects { get; }
        public List<bool> Inside { get; } = [];
        public List<Vector> SeenAt { get; } = [];

        public Rig(bool withSession = true)
        {
            Swinger = World.Add(ManagerRig.Attacker.EntryIndex);
            Victim = World.Add(ManagerRig.Victim.EntryIndex);
            Assert.Equal((ManagerRig.Attacker, ManagerRig.Victim), (Swinger.Handle, Victim.Handle));
            World.MaxClients = ManagerRig.Victim.EntryIndex;     // the rig's victim sits at slot 5
            Lag = new LagCompensation(World, new LagSettings(), Commands, new RecordingLog());
            Effects = new TFFlameEffects(Flame.Engine, Flame.Engine, Flame.Directory, Flame.Engine, Flame.Engine,
                                         Flame.Conditions, Flame.Owners, Flame.Log,
                                         Flame.Convars.Declared<TFAirBlastCrayConVar>(),
                                         Flame.Convars.Declared<TFFlameThrowerBurstZvelocityConVar>(),
                                         Flame.Convars.Declared<TFPushbackScaleConVar>(),
                                         Flame.Convars.Declared<TFPushbackScaleVerticalConVar>(), Flame.Carriers,
                                         withSession ? Lag : null);
            Swinger.Latency = 5 * LagWorld.Tick;
            Flame.Directory.Instance = Flame.Manager;

            Flame.Engine.ReturnsEachCall("Entities.GetEyeAngles", () =>
            {
                Inside.Add(Lag.IsCurrentlyDoingLagCompensation);
                SeenAt.Add(Victim.Origin);
                return new Vector(0f, 0f, 0f);
            });

            for (int tick = 100; tick <= 110; tick++)
            {
                World.At(tick);
                Victim.Origin = At(tick);
                Lag.RecordFrame();
            }
        }

        public void Tick()
        {
            var cmd = new UserCmd { TickCount = 105, CommandNumber = 7, Buttons = (int)InputButtons.Attack };
            var attack = new WeaponAttackContext(default!, null!, null!, 1f, true, null!,
                                                 new WeaponCommand(1, 5, ManagerRig.Attacker.EntryIndex));
            using (Commands.Begin(ManagerRig.Attacker, in cmd))
                Effects.Flames(in attack, WeaponFixtures.Parse("tf_weapon_flamethrower"), new ShotDamage(13f));
        }
    }

    [Fact]
    public void The_flame_tick_runs_inside_the_session_with_the_players_rewound()
    {
        var rig = new Rig();

        rig.Tick();

        Assert.True(Assert.Single(rig.Inside));                    // the tick's reads fall inside
        Assert.Equal(At(105), Assert.Single(rig.SeenAt));          // the victim as the pyro saw him
        Assert.Single(rig.Flame.Manager.Points);                   // the flame point itself went in
        Assert.Equal(At(110), rig.Victim.Origin);                  // put back after
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void Without_the_compensation_the_same_tick_sees_the_current_spot()
    {
        var rig = new Rig(withSession: false);

        rig.Tick();

        Assert.False(Assert.Single(rig.Inside));
        Assert.Equal(At(110), Assert.Single(rig.SeenAt));
        Assert.Single(rig.Flame.Manager.Points);                   // the tick itself does not need the session
    }
}

/// <summary>
/// The medigun's target search (<c>TFMedigunWeapon.PrimaryAttack</c>;
/// tf_weapon_medigun.cpp:1774-1775 Start, :1799-1801 Finish, both gated on
/// <c>tf_medigun_lagcomp</c>, declared :86): with the flag on the aim traces see
/// the other players where the medic saw them. The patient (entity 3) ran along +x
/// and is at x=200 now; the medic's (entity 1) command was sent at tick 105, when
/// the patient stood at x=150. The beam takes him only from inside the session.
/// </summary>
public sealed class TFMedigunBeamLagTests
{
    private static readonly TFWeaponScript MedigunScript = WeaponFixtures.Parse("tf_weapon_medigun");

    private static Vector At(int tick) => new(100f + 10f * (tick - 100), 0f, 0f);

    private sealed class Rig
    {
        public MedigunRig Med { get; } = new();
        public LagWorld World { get; } = new();
        public PlayerCurrentCommands Commands { get; } = new();
        public LagCompensation Lag { get; }
        public LagPlayer Swinger { get; }
        public LagPlayer Victim { get; }
        public TFMedigunWeapon Weapon { get; }
        public List<bool> Inside { get; } = [];
        public List<Vector> SeenAt { get; } = [];

        public Rig(bool withSession = true)
        {
            Swinger = World.Add(1);
            Victim = World.Add(3);
            Assert.Equal((MedigunRig.HandleFor(1), MedigunRig.HandleFor(3)), (Swinger.Handle, Victim.Handle));
            Lag = new LagCompensation(World, new LagSettings(), Commands, new RecordingLog());
            Med.Give(1, 100, 100);
            Med.Give(3, 100, 125);
            Weapon = Med.NewWeapon(withSession ? Lag : null);
            Swinger.Latency = 5 * LagWorld.Tick;

            Med.Engine.ReturnsEachCall("WorldQuery.TraceLine", () =>
            {
                Inside.Add(Lag.IsCurrentlyDoingLagCompensation);
                SeenAt.Add(Victim.Origin);
                return Victim.Origin.Equals(At(105))
                    ? new TraceResult(true, 1f, default, default, MedigunRig.HandleFor(3))
                    : new TraceResult(false, 1f, default, default, EntityHandle.None);
            });

            for (int tick = 100; tick <= 110; tick++)
            {
                World.At(tick);
                Victim.Origin = At(tick);
                Lag.RecordFrame();
            }
        }

        public void Heal()
        {
            var weapon = new WeaponNetState();
            weapon.BindLocal();
            var view = new ViewModelNetState();
            view.BindLocal();
            var attack = new WeaponAttackContext(default, weapon, view, 10f, true, default!,
                                                 new WeaponCommand(0, 9, 1));
            var cmd = new UserCmd { TickCount = 105, CommandNumber = 7, Buttons = (int)InputButtons.Attack };
            using (Commands.Begin(MedigunRig.HandleFor(1), in cmd))
                Weapon.Heal(in attack, MedigunScript, held: true);
        }
    }

    [Fact]
    public void The_beam_takes_the_patient_where_the_medic_saw_him()
    {
        var rig = new Rig();

        rig.Heal();

        Assert.Equal(3, rig.Weapon.HealingTargetIndex(1));         // the rewound patient was in reach
        Assert.True(rig.Inside[0]);                                // both aim traces ran inside the session
        Assert.Equal(At(105), rig.SeenAt[0]);                      // judged at the rewound spot
        Assert.Equal(At(110), rig.Victim.Origin);                  // put back after
        Assert.False(rig.Lag.IsCurrentlyDoingLagCompensation);
    }

    [Fact]
    public void Without_the_compensation_the_same_beam_misses_the_running_patient()
    {
        var rig = new Rig(withSession: false);

        rig.Heal();

        Assert.Equal(-1, rig.Weapon.HealingTargetIndex(1));
        Assert.False(rig.Inside[0]);
        Assert.Equal(At(110), rig.SeenAt[0]);                      // the traces saw the current spot
    }

    [Fact]
    public void With_tf_medigun_lagcomp_off_the_beam_refuses_even_a_session_ready_medic()
    {
        var rig = new Rig();                                         // the lag service is wired
        rig.Med.Set<TFMedigunLagCompConVar>("0");                    // stock's gate: off

        rig.Heal();

        Assert.Equal(-1, rig.Weapon.HealingTargetIndex(1));          // no session: the current spot, a miss
        Assert.False(rig.Inside[0]);
        Assert.Equal(At(110), rig.SeenAt[0]);
    }
}
