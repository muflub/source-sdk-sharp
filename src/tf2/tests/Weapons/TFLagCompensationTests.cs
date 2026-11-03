using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Medigun;
using TF2.Server.Weapons;
using TF2.Shared;

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
        return (new TFLagCompensationTargets(convars, engine, engine, scripts,
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
        convars.Get<MpFriendlyFireConVar>()!.NotifyChanged("1", "0");

        Assert.Equal(Decision.Yes, rule.WantsLagCompensationOnEntity(Asked(Mate, yaw: 90f)));
    }
}
