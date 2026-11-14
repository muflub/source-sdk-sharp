using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>
/// The Short Circuit's orb (<c>CTFProjectile_MechanicalArmOrb</c>): the spawn
/// the trigger's ask becomes, the think's zap and eat, the annihilation and
/// the death blast.
/// </summary>
internal sealed class OrbRig
{
    public const string ShortCircuit = "tf_weapon_mechanical_arm";

    /// <summary>An enemy player at the rig's default reach.</summary>
    public static readonly EntityHandle Victim = new(0x0001_0000u | 7);

    /// <summary>The eaten enemy rocket's handle.</summary>
    public static readonly EntityHandle RocketHandle = new(0x0003_0000u | 11);

    /// <summary>The twin orb's owner, a Blue by the team answer.</summary>
    public static readonly EntityHandle OtherOwner = new(0x0001_0000u | 9);

    public ProjectileRig Base { get; } = new();
    public ProjectileRig.FlareConditions Conditions { get; } = new();
    public FakeStats Stats { get; } = new();

    /// <summary>Where each entity stands; the orb is the handle's entry.</summary>
    public readonly Dictionary<EntityHandle, Vector> Origins = new();

    public TFMechanicalArmOrb Orb { get; }

    public OrbRig()
    {
        var engine = Base.Engine;
        engine.ReturnsEachCall("Entities.GetOrigin",
            () => engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        engine.ReturnsEachCall("Entities.GetWorldSpaceCenter",
            () => engine.Calls[^1].Args[0] is EntityHandle h && Origins.TryGetValue(h, out var o) ? o : default(Vector));
        // Only the victim is a player, on the other team; the orb's owner is
        // the rig player, a Red by the rig's team answer.
        engine.ReturnsEachCall("Entities.IsPlayer",
            () => engine.Calls[^1].Args[0] is EntityHandle h && h == Victim);
        engine.ReturnsEachCall("Players.GetTeam",
            () => engine.Calls[^1].Args[0] is EntityHandle h && (h == Victim || h == OtherOwner) ? 3 : 2);
        // The twin's owner is index 4; everyone else is the rig player.
        engine.ReturnsEachCall("Players.GetHandle",
            () => engine.Calls[^1].Args[0] is int i && i == 4 ? OtherOwner : ProjectileRig.Player);
        engine.Returns("Players.IsAlive", true);
        engine.Returns("Precache.ParticleSystemIndex", 17);
        Origins[ProjectileRig.ProjectileHandle] = new Vector(0f, 0f, 64f);
        Origins[Victim] = new Vector(50f, 0f, 64f);

        Orb = OrbAt(ProjectileRig.ProjectileHandle);
        Orb.ArmedBy(3);
        Base.Directory.Instance = Orb;
    }

    /// <summary>Another orb, bound the same way, for the annihilation's pair.</summary>
    public TFMechanicalArmOrb OrbAt(EntityHandle handle)
    {
        var engine = Base.Engine;
        var orb = new TFMechanicalArmOrb(engine, Base.Motion, engine, engine, engine, engine, Base.Random,
                                         Conditions, Stats, Base.Scopes, Base.Directory, Base.Temps, engine,
                                         Base.Log)
        {
            Handle = handle,
            RegisteredKind = ScriptedEntityKind.Projectile,
        };
        orb.NetStates = NetStateBinding.AttachLocal(orb, orb.Handle, new NetStateFields(new RecordingLog()));
        orb.BindAreas(TestServices.Areas(engine));
        return orb;
    }

    /// <summary>The zap and eat think, at the rig clock.</summary>
    public void Think() => Orb.OnThink(TFMechanicalArmOrb.OrbThinkContext);

    /// <summary>Every damage the engine saw, the zap's among them.</summary>
    public IReadOnlyList<EngineCall> Hits => Base.Engine.AllOf("Damage.TakeDamage").ToList();

    public IReadOnlyList<EngineCall> Sounds(string name) =>
        Base.Engine.Calls.Where(c => c.Member == "Sound.EntityEmitSound"
                                     && c.Args[1] is string s && s == name).ToList();

    public int Particles => Base.Temps.Effects.Count(e => e.Name == "ParticleEffect");

    /// <summary>The seam's ask: the trigger's own shape, live.</summary>
    public void ShockAttack() => Base.Effects.ShockAttack(Attack(), Script(ShortCircuit));

    private static WeaponAttackContext Attack() => new(default, null!, null!, 1f, EffectsLive: true, null!,
        new WeaponCommand(1, 5, 3));

    private static TFWeaponScript Script(string name)
    {
        var files = WeaponFixtures.WithExtras(WeaponFixtures.ShippedWithClasses());
        return new TFWeaponScripts(new RecordingLog(), files).Get(name)
            ?? throw new InvalidOperationException($"no script {name}");
    }
}

public sealed class TFMechanicalArmOrbFacts
{
    [Fact]
    public void TheShockAttackSpawnsTheOrbAtTheMuzzle()
    {
        var rig = new OrbRig();

        rig.ShockAttack();

        // Stock's own spawn (tf_weapon_mechanical_arm.cpp:250-286): the orb
        // entity, born at the eye's 40 forward, 15 right and 10 down - the
        // right is -Y at yaw 0 - and brought to life on the rig's handle.
        EngineCall create = ProjectileRig.Must(rig.Base.Engine.Last("Entities.Create"), "Entities.Create");
        Assert.Equal(TFProjectileEntityNames.MechanicalArmOrb, create.ArgAs<string>(0));

        Vector muzzle = ProjectileRig.Must(rig.Base.Engine.Last("Entities.SetOrigin"), "Entities.SetOrigin")
            .ArgAs<Vector>(1);
        Assert.Equal(TFProjectileDefs.MechanicalArmMuzzleForward, muzzle.X, 0.5f);
        Assert.Equal(-TFProjectileDefs.MechanicalArmMuzzleRight, muzzle.Y, 0.5f);
        Assert.Equal(64f + TFProjectileDefs.MechanicalArmMuzzleUp, muzzle.Z, 0.5f);

        EngineCall spawned = ProjectileRig.Must(rig.Base.Engine.Last("Entities.Spawn"), "Entities.Spawn");
        Assert.Equal(ProjectileRig.ProjectileHandle, spawned.ArgAs<EntityHandle>(0));
    }

    [Fact]
    public void TheThinkZapsTheEnemyInReach()
    {
        var rig = new OrbRig();
        rig.Base.Engine.FillsSphere(OrbRig.Victim);

        rig.Think();

        // The zap (CheckForPlayers, tf_weapon_mechanical_arm.cpp:727-804):
        // fifteen shock over the plasma custom, the hit particle and the
        // charged-death sound once - and the owner in the square of eighty
        // takes the same zap, the punish :793-800 keeps.
        Assert.Equal(2, rig.Hits.Count);
        Assert.Equal(OrbRig.Victim, rig.Hits[0].ArgAs<EntityHandle>(0));
        Assert.Equal(15f, rig.Hits[0].ArgAs<float>(2), 0.01f);
        Assert.Equal(DamageType.Shock, rig.Hits[0].ArgAs<DamageType>(3));
        Assert.Equal(ProjectileRig.Player, rig.Hits[1].ArgAs<EntityHandle>(0));
        Assert.Equal(2, rig.Particles);
        Assert.Single(rig.Sounds(TFMechanicalArmOrb.ZapSound));
    }

    [Fact]
    public void TheRefireHoldsTheZapForTheTenth()
    {
        var rig = new OrbRig();
        rig.Base.Engine.FillsSphere(OrbRig.Victim);

        rig.Think();
        rig.Think();

        // The gate's stamp (m_flOrbNextAttackTime, :803): the second think,
        // the refire unfired, adds no zap - still the first think's pair.
        Assert.Equal(2, rig.Hits.Count);
    }

    [Fact]
    public void TheThinkEatsTheEnemyProjectileInTheReach()
    {
        var rig = new OrbRig();
        var rocket = rig.Base.Rocket();
        rocket.Handle = OrbRig.RocketHandle;
        rocket.State.TeamNumber = 3;
        rig.Base.Directory.Holds(OrbRig.RocketHandle, rocket);
        rig.Origins[OrbRig.RocketHandle] = new Vector(60f, 0f, 64f);
        rig.Base.Engine.FillsSphere(OrbRig.RocketHandle);

        rig.Think();

        // The eat (CheckForProjectiles, :809-876): the enemy rocket gone, its
        // sound on the orb, and the eater's side two bonus points.
        Assert.Equal(1, rig.Base.Engine.CountOf("Entities.Remove"));
        Assert.Single(rig.Sounds(TFMechanicalArmOrb.EatSound));
        (EntityHandle Player, TFStat Stat, int Amount) paid = Assert.Single(rig.Stats.Paid);
        Assert.Equal(ProjectileRig.Player, paid.Player);
        Assert.Equal(TFStat.BonusPoints, paid.Stat);
        Assert.Equal(2, paid.Amount);
    }

    [Fact]
    public void TwoOrbsAnnihilate()
    {
        var rig = new OrbRig();
        var otherHandle = new EntityHandle(0x0003_0000u | 12);
        var other = rig.OrbAt(otherHandle);
        other.ArmedBy(4);
        rig.Base.Directory.Holds(otherHandle, other);
        rig.Base.Engine.FillsSphere(otherHandle);

        rig.Think();

        // The annihilation (:852-858): both orbs die the same death, the
        // blast sound twice - once on each.
        Assert.Equal(2, rig.Sounds(TFMechanicalArmOrb.BlastSound).Count);
    }

    [Fact]
    public void TheDeathBlastZapsWideAndGone()
    {
        var rig = new OrbRig();
        rig.Base.Engine.FillsSphere(OrbRig.Victim);

        rig.Orb.ExplodeAndRemove();

        // ExplodeAndRemove (:673-696): the blast particle and sound, one
        // last wide zap - sixteen asked, the victim and the owner in the
        // punish's square answered - and the removal.
        Assert.Equal(3, rig.Particles);
        Assert.Single(rig.Sounds(TFMechanicalArmOrb.BlastSound));
        Assert.Equal(2, rig.Hits.Count);
    }

    [Fact]
    public void APlayerTouchPassesThrough()
    {
        var rig = new OrbRig();

        // Bodies are flown through (:651-656): no spend, no blast.
        rig.Orb.OnTouch(OrbRig.Victim);

        Assert.Equal(0, rig.Particles);
        Assert.Equal(0, rig.Base.Engine.CountOf("Sound.EntityEmitSound"));
    }
}
