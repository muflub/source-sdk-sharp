using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Tests.Buildings;
using TF2.Tests.Combat;
using TF2.Tests.Weapons;

using SourceSharp.Tests;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>The flame manager's world: the recording engine, the real scopes, one manager.</summary>
internal sealed class ManagerRig
{
    public static readonly EntityHandle Attacker = new(0x0001_0000u | 3);
    public static readonly EntityHandle Victim = new(0x0001_0000u | 5);
    public static readonly EntityHandle ManagerHandle = new(0x0006_0000u | 30);

    public RecordingEngine Engine { get; } = RecordingEngine.Default();
    public RecordingLog Log { get; } = new();
    public TFDamageScopes Scopes { get; } = new();
    public ScopedDamage Damage { get; }
    public DamageHitGroups HitGroups { get; } = new();
    public FakeConditions Conditions { get; } = new();
    public ProjectileRig.DirectoryView Directory { get; } = new();
    public FakeOwners Owners { get; } = new();

    /// <summary>The typed convars and the carrier registry the effects' ctor takes.</summary>
    public ConVarRegistry Convars { get; } = new(new StoredConVarBackend());
    public TFPlayerCarriers Carriers { get; } = new();

    public TFFlameManager Manager { get; }
    public TF2.Shared.TFFlameThrowerBoxSizeConVar BoxSize { get; }
    public TFFlameEffects Effects { get; }

    private int _teamAsk;

    public TFAfterburns Afterburns { get; }

    public ManagerRig()
    {
        Damage = new ScopedDamage(Scopes, HitGroups);
        // The teammate arm's class gate now runs on every flame (the manager's
        // owners are required): a victim of no recorded class, never the Sniper.
        Engine.Returns("Entities.GetIndex", 3);
        Convars.RegisterAll(typeof(TF2.Server.Medigun.TFPlayerHealing).Assembly);
        Afterburns = new TFAfterburns(Carriers, Conditions, Engine, Engine, Damage, Directory, Scopes,
                                      new CombatRig.HealingSpy(), Owners, new AttributeHooks(), Carriers);
        BoxSize = DeclaredConVars.Holding<TF2.Shared.TFFlameThrowerBoxSizeConVar>("12");
        Manager = new TFFlameManager(Engine, Damage, Engine, Engine, Conditions, Scopes, HitGroups, BoxSize,
                                     Owners, Directory);
        Manager.BindAreas(TestServices.Areas(Engine));
        Effects = new TFFlameEffects(Engine, Engine, Directory, Engine, Engine, Conditions, Owners, Log,
                                     Convars.Declared<TFAirBlastCrayConVar>(),
                                     Convars.Declared<TFFlameThrowerBurstZvelocityConVar>(),
                                     Convars.Declared<TFPushbackScaleConVar>(),
                                     Convars.Declared<TFPushbackScaleVerticalConVar>(), Carriers,
                                     new TF2.Shared.Econ.AttributeHooks(), new ProjectileRig.Resupply(), Engine,
                                     new TF2.Tests.Player.ComposedComponents.IdleLag(), Afterburns);

        Engine.Returns("Entities.Create", ManagerHandle);
        Engine.Returns("Players.GetHandle", Attacker);
        Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        Engine.Returns("Entities.GetVelocity", default(Vector));
        // The none handle is invalid here as it is live: a manager nobody gave an
        // attacker burns nothing, which a blanket true hid for a whole port.
        Engine.ReturnsEachCall("Entities.IsValid",
                               () => Engine.Calls[^1].Args[0] is EntityHandle h && h != EntityHandle.None);
        // The victim reads as a living player.
        Engine.Returns("Entities.IsPlayer", true);
        Engine.Returns("Players.IsAlive", true);
        // OnCollide asks the victim's team first, then the attacker's: Red
        // against Blue, so the default collide is an enemy.
        Engine.ReturnsEachCall("Players.GetTeam", () => _teamAsk++ % 2 == 0
            ? (int)TFTeam.Red : (int)TFTeam.Blue);
        Engine.Returns("WorldQuery.PointContents", default(Contents));
        // A clear path, unless a fact says otherwise.
        Engine.Returns("WorldQuery.TraceBox", Clear);
        Engine.Returns("Host.CurrentTime", 0f);
    }

    public static SurfaceTraceResult Clear { get; } =
        new(false, 1f, default, default, EntityHandle.None, 0, 0);

    /// <summary>A swept box stopped by <paramref name="who"/> half-way along.</summary>
    public static SurfaceTraceResult Hit(EntityHandle who) =>
        new(true, 0.5f, new Vector(150f, 0f, 0f), new Vector(0f, 1f, 0f), who, 0, 0);

    /// <summary>Leave the manager without an attacker, as nothing but the weapon's path sets one.</summary>
    public bool Unattributed { get; set; }

    /// <summary>Everyone on one team, so a collide reaches no damage.</summary>
    public void SameTeams() => Engine.Returns("Players.GetTeam", (int)TFTeam.Red);

    /// <summary>
    /// Arms the manager from the shell's deal, adds the first point at time
    /// zero and thinks one tick at <paramref name="now"/>; the point, when it
    /// lived through the tick.
    /// </summary>
    public TFFlameManager.PointView Flame(float now, SurfaceTraceResult trace)
    {
        if (!Unattributed)
            Manager.SetAttacker(Attacker);

        Manager.UpdateDamage(WeaponFixtures.Parse("tf_weapon_flamethrower"),
                             new ShotDamage(TFFlamethrowerBehaviour.DamagePerTick),
                             TFFlameManagerDefs.BurnFrequency, attackerIsPyro: false);
        Manager.AddPoint(default, new Vector(1f, 0f, 0f), default);
        Engine.Returns("Host.CurrentTime", now);
        Engine.Returns("WorldQuery.TraceBox", trace);
        Manager.OnThink();
        return Manager.Points.FirstOrDefault();
    }

    /// <summary>One more think of the same manager at <paramref name="now"/>.</summary>
    public void Again(float now, SurfaceTraceResult trace)
    {
        Engine.Returns("Host.CurrentTime", now);
        Engine.Returns("WorldQuery.TraceBox", trace);
        Manager.OnThink();
    }
}

public class TFFlameManagerFacts
{
    [Fact]
    public void A_manager_with_no_attacker_burns_nothing()
    {
        var rig = new ManagerRig { Unattributed = true };

        rig.Flame(0.1f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheDamageFallsToThreeQuartersAtHalfTheLife()
    {
        // 13 × RemapValClamped(0.15, 0, 0.6 × 0.5, 1, 0.5) = 9.75
        // (tf_flame.cpp:526-528, the shipped time curve).
        var rig = new ManagerRig();
        rig.Flame(0.15f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Equal(9.75f, Assert.Single(rig.Damage.Hits).Damage, 2f);
    }

    [Fact]
    public void TheDamageFallsToHalfAtTheScalesCap()
    {
        // The cap is the life × tf_flame_min_damage_scale_time_cap (0.5):
        // 0.3 s into a 0.6 s life the curve lands on its 0.5 floor.
        var rig = new ManagerRig();
        rig.Flame(0.3f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Equal(6.5f, Assert.Single(rig.Damage.Hits).Damage, 2f);
    }

    [Fact]
    public void AVictimBurnsAtMostOncePerBurnFrequency()
    {
        // The gate is tf_flamethrower_burn_frequency 0.075
        // (tf_flame.cpp:555-563): a tick inside it burns nothing, a tick that
        // waited it out burns again — at the heat index's half (:534-543,
        // :689-698), 13 × 0.85 × 0.5.
        var rig = new ManagerRig();
        rig.Flame(0.015f, ManagerRig.Hit(ManagerRig.Victim));
        rig.Again(0.03f, ManagerRig.Hit(ManagerRig.Victim));
        Assert.Single(rig.Damage.Hits);

        rig.Again(0.09f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Equal(2, rig.Damage.Hits.Count);
        Assert.Equal(5.525f, rig.Damage.Hits[1].Damage, 3f);
    }

    [Fact]
    public void APointDiesAtItsLifeAndTheManagerWithIt()
    {
        // Expiry is now > spawn + life (tf_point_manager.cpp:259-262): alive
        // at the life itself, gone a hair past — and the manager, whose last
        // point it was, removes itself (:290-304).
        var rig = new ManagerRig();
        TFFlameManager.PointView point = rig.Flame(0.015f, ManagerRig.Clear);

        rig.Engine.Returns("Host.CurrentTime", point.SpawnTime + point.LifeTime);
        rig.Manager.OnThink();
        Assert.Equal(1, rig.Manager.PointCount);
        Assert.Equal(0, rig.Engine.CountOf("Entities.Remove"));

        rig.Engine.Returns("Host.CurrentTime", point.SpawnTime + point.LifeTime + 0.001f);
        rig.Manager.OnThink();
        Assert.Equal(0, rig.Manager.PointCount);
        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
    }

    [Fact]
    public void APointSittingInWaterDiesWithoutBurning()
    {
        // Update's water check (tf_point_manager.cpp:264-269), before any
        // collide: the flame dies entering water, as the old flame entity did.
        var rig = new ManagerRig();
        rig.Engine.Returns("WorldQuery.PointContents", Contents.Water);

        rig.Flame(0.015f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void APointSittingInSlimeDiesToo()
    {
        var rig = new ManagerRig();
        rig.Engine.Returns("WorldQuery.PointContents", Contents.Slime);

        rig.Flame(0.015f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Equal(1, rig.Engine.CountOf("Entities.Remove"));
        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void ATeammateIsNotABurnTarget()
    {
        // The teammate arm is stock's sniper arrow-lighting (:704-719), which
        // the seam cannot reach: a teammate burns nothing, but the burn-list
        // write still closes (:809-817).
        var rig = new ManagerRig();
        rig.SameTeams();

        rig.Flame(0.015f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.Empty(rig.Damage.Hits);
    }

    [Fact]
    public void TheFlameIgnitesThroughItsOwnDamageBits()
    {
        // The flamethrower's own bits carry DMG_IGNITE, and the kill's custom
        // kind is TF_DMG_CUSTOM_BURNING (:780).
        var rig = new ManagerRig();
        rig.Flame(0.015f, ManagerRig.Hit(ManagerRig.Victim));

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(ManagerRig.Victim, hit.Victim);
        Assert.True((hit.Type & TFDamageBits.Ignite) != 0);
        Assert.Equal(TFDamageCustom.Burning, hit.Custom);
    }

    [Fact]
    public void APyroAttackerDebuffsTheirVictimForTwoSeconds()
    {
        // The pyro arm (:756-773): the victim takes TF_COND_HEALING_DEBUFF
        // for 2 s from the attacker.
        var rig = new ManagerRig();
        rig.Manager.SetAttacker(ManagerRig.Attacker);
        rig.Manager.UpdateDamage(WeaponFixtures.Parse("tf_weapon_flamethrower"),
                                 new ShotDamage(TFFlamethrowerBehaviour.DamagePerTick),
                                 TFFlameManagerDefs.BurnFrequency, attackerIsPyro: true);
        rig.Manager.AddPoint(default, new Vector(1f, 0f, 0f), default);
        rig.Engine.Returns("Host.CurrentTime", 0.015f);
        rig.Engine.Returns("WorldQuery.TraceBox", ManagerRig.Hit(ManagerRig.Victim));
        rig.Manager.OnThink();

        Assert.Equal([(ManagerRig.Victim, TFCond.HealingDebuff, 2f)], rig.Conditions.Added);
    }

    [Fact]
    public void TheCollisionBoxIsHalfTheConvarEachWay()
    {
        var rig = new ManagerRig();
        rig.BoxSize.Hold("30");
        rig.Manager.AddPoint(default, new Vector(1f, 0f, 0f), default);
        rig.Engine.Returns("Host.CurrentTime", 0.015f);
        rig.Manager.OnThink();

        // The swept box's corners: tf_flamethrower_boxsize each way from the
        // path (CTFFlameManager::GetRadius, tf_flame.cpp:1209).
        var trace = rig.Engine.Last("WorldQuery.TraceBox");
        Assert.NotNull(trace);
        Assert.Equal(new Vector(-30f, -30f, -30f), trace!.ArgAs<Vector>(2));
        Assert.Equal(new Vector(30f, 30f, 30f), trace!.ArgAs<Vector>(3));
    }
}

public class TFFlameEffectsFacts
{
    private static WeaponAttackContext Attack(int owner) => new(default!, null!, null!, 1f, true,
        null!, new WeaponCommand(1, 5, owner));

    private static TFWeaponScript Script() => WeaponFixtures.Parse("tf_weapon_flamethrower");

    /// <summary>
    /// A flame fired through the weapon burns as its owner (<c>CTFFlameManager::Create</c>
    /// sets <c>m_hAttacker</c>). The manager had no attacker from the port until a
    /// two-player boot showed 1.5 s of flame costing an enemy nothing.
    /// </summary>
    [Fact]
    public void A_flame_from_the_weapon_burns_as_its_owner()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Owners.Classes[3] = TFClass.Pyro;

        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));
        rig.Engine.Returns("Host.CurrentTime", 0.1f);
        rig.Engine.Returns("WorldQuery.TraceBox", ManagerRig.Hit(ManagerRig.Victim));
        rig.Manager.OnThink();

        var hit = Assert.Single(rig.Damage.Hits);
        Assert.Equal(ManagerRig.Victim, hit.Victim);
        Assert.Equal(ManagerRig.Attacker, hit.Attacker);
    }

    [Fact]
    public void TheTickAsksAManagerForAFlamePointAtTheMuzzle()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Owners.Classes[3] = TFClass.Pyro;

        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));

        var created = rig.Engine.Last("Entities.Create");
        Assert.NotNull(created);
        Assert.Equal(TFFlameManagerDefs.ClassName, created!.ArgAs<string>(0));

        // The muzzle: the eye (0, 0, 64) plus the offset's forward 40 and
        // right 5 along the straight aim (:2111-2128); at yaw 0 AngleVectors'
        // right is -Y.
        var point = Assert.Single(rig.Manager.Points);
        Assert.Equal(new Vector(40f, -5f, 64f), point.Position);

        // FlameSpeed 2450 along the aim: the spread's draws move the cross
        // axes (right (0,-1,0), up (0,0,1) here), never the forward one.
        Assert.Equal(2450f, point.Velocity.X, 2f);
        Assert.InRange(point.Velocity.Length, 2450f, 2456f);
    }

    [Fact]
    public void TheManagerIsMadeOncePerOwnerAndReusedWhileItLives()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;

        rig.Effects.Flames(Attack(3), Script(), default);
        rig.Effects.Flames(Attack(3), Script(), default);

        Assert.Equal(1, rig.Engine.CountOf("Entities.Create"));
        Assert.Equal(2, rig.Manager.PointCount);
    }

    /// <summary>
    /// <c>set_flamethrower_back_crit</c> (<c>tf_weapon_flamethrower.cpp:875-</c>
    /// 876, <c>tf_flame.cpp:730-745</c>): the item's exactly-one flag turns the
    /// behind arm on. One tick of clear sky moves the point along its aim; the
    /// collide on the next asks the flame's travel against the victim's facing
    /// and, past the behind's 0.8, burns critical.
    /// </summary>
    [Fact]
    public void AFlameReachingFromBehindCritsWithTheAttribute()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Owners.Items[Attack(3).Command.OwnerIndex] =
            ItemWith("set_flamethrower_back_crit", AttributeDescriptionFormat.Additive, 1f);
        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));

        rig.Again(0.015f, ManagerRig.Clear);
        rig.Engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        rig.Again(0.09f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.True(Assert.Single(rig.Damage.Hits).Critical);
    }

    /// <summary>
    /// A victim close enough to meet the flame on its first tick still crits: the
    /// point hasn't moved yet, so the travel is to the contact, not to its position.
    /// </summary>
    [Fact]
    public void AFlameMeetingTheVictimOnItsFirstTickCritsFromBehind()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Owners.Items[Attack(3).Command.OwnerIndex] =
            ItemWith("set_flamethrower_back_crit", AttributeDescriptionFormat.Additive, 1f);
        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));

        rig.Engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        rig.Again(0.015f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.True(Assert.Single(rig.Damage.Hits).Critical);
    }

    /// <summary>
    /// The same collide on a victim facing the flame (yaw 180 against the
    /// flame's +X travel, the dot's −1) is a hit in front: no crit, flag or
    /// no flag.
    /// </summary>
    [Fact]
    public void AFlameInTheFrontNeverCrits()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Owners.Items[Attack(3).Command.OwnerIndex] =
            ItemWith("set_flamethrower_back_crit", AttributeDescriptionFormat.Additive, 1f);
        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));

        rig.Again(0.015f, ManagerRig.Clear);
        rig.Engine.Returns("Entities.GetAngles", new Vector(0f, 180f, 0f));
        rig.Again(0.09f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.False(Assert.Single(rig.Damage.Hits).Critical);
    }

    /// <summary>Behind geometry with no item on the gun answers no crit: the
    /// flag arm gates on the attribute's exactly one.</summary>
    [Fact]
    public void BehindWithoutTheAttributeDoesNotCrit()
    {
        var rig = new ManagerRig();
        rig.Directory.Instance = rig.Manager;
        rig.Effects.Flames(Attack(3), Script(), new ShotDamage(13f));

        rig.Again(0.015f, ManagerRig.Clear);
        rig.Engine.Returns("Entities.GetAngles", new Vector(0f, 0f, 0f));
        rig.Again(0.09f, ManagerRig.Hit(ManagerRig.Victim));

        Assert.False(Assert.Single(rig.Damage.Hits).Critical);
    }

    private static int _nextDef = 5500;

    /// <summary>An item holding one attribute under the named hook class.</summary>
    private static IAttributeProvider ItemWith(string hook, AttributeDescriptionFormat format, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_nextDef++, "attr " + _nextDef, hook, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }
}
