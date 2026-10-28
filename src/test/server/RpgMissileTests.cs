using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// rpg_missile as stock HL2MP's CMissile (shared/hl2mp/weapon_rpg.cpp): a
/// lofted, falling launch on w_missile_launch; ignition three tenths of a
/// second later (w_missile, Missile.Ignite, RPG_SPEED, the shooter's flash, a
/// rocket trail); steering for its shooter's switched-on laser dot; the blast
/// of the RPG's 150 out to 300; and the shoot-down that only missile defences
/// can cause.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons weapon_rpg,
/// 2026-10-25): stock's server played Missile.Ignite and this mod did not; its
/// rocket flew straight at 1500 from the muzzle and never turned.
/// </para>
/// </summary>
public class RpgMissileTests
{
    private static readonly EntityHandle Missile = new(0x0004_0c01u);
    private static readonly EntityHandle Shooter = new(0x0001_0001u);
    private static readonly EntityHandle Other = new(0x0002_0002u);
    private static readonly EntityHandle DotHandle = new(0x0004_0c02u);
    private static readonly Vector Start = new(0, 0, 64);

    private sealed class Effects : TempEntsDouble
    {
        public readonly List<string> Dispatched = [];

        public override void DispatchEffect(TeFilter filter, float delay, string name, in TeEffectData data)
            => Dispatched.Add(name);
    }

    private sealed class Screens : IScreenEffects
    {
        public readonly List<(EntityHandle Player, int R, int G, int B, int A, float Duration, float Hold, FadeFlags Flags)> Fades = [];

        public void Shake(Vector origin, float amplitude, float frequency, float duration, float radius,
                          ShakeCommand command = ShakeCommand.Start, bool airShake = false)
            => throw new NotSupportedException();

        public void Fade(EntityHandle player, int r, int g, int b, int a, float duration, float holdTime, FadeFlags flags)
            => Fades.Add((player, r, g, b, a, duration, holdTime, flags));
    }

    private sealed class Rig(ScriptedEntityRegistry registry, RecordingEngine engine, RecordingBlastSweep blast,
                             Effects temps, EntityMotion motion, Dictionary<EntityHandle, Vector> origins, Screens screens)
    {
        public Screens Screens { get; } = screens;
        public ScriptedEntityRegistry Registry { get; } = registry;
        public RecordingEngine Engine { get; } = engine;
        public RecordingBlastSweep Blast { get; } = blast;
        public Effects Temps { get; } = temps;
        public EntityMotion Motion { get; } = motion;
        public Dictionary<EntityHandle, Vector> Origins { get; } = origins;
        public float Now = 10f;

        public Rocket Rocket => (Rocket)Registry.Get(Missile)!;

        public void ThinkAt(float t)
        {
            Now = t;
            Registry.Think(Missile);
        }

        public LaserDot Dot(EntityHandle owner, Vector at, bool on = true)
        {
            Registry.Spawn(DotHandle, EntityNames.EnvLaserdot);
            var dot = (LaserDot)Registry.Get(DotHandle)!;
            dot.OwnedBy(owner);
            dot.SetLaserPosition(at, new Vector(-1, 0, 0));
            if (on)
                dot.TurnOn();
            else
                dot.TurnOff();
            return dot;
        }
    }

    /// <summary>A missile fired from <see cref="Start"/> facing +X, with each entity's origin and angles kept.</summary>
    private static Rig Fired(bool grace = false, SurfaceTraceResult? ahead = null)
    {
        var temps = new Effects();
        var services = Services(out var engine);
        services.AddInstance<ITempEnts>(temps);
        var screens = new Screens();
        services.AddInstance<IScreenEffects>(screens);
        var blast = (RecordingBlastSweep)services.Resolve(typeof(IBlastSweep))!;
        var motion = (EntityMotion)services.Resolve(typeof(EntityMotion))!;
        var origins = new Dictionary<EntityHandle, Vector> { [Missile] = Start };
        var angles = new Dictionary<EntityHandle, Vector>();
        Rig? rig = null;
        uint made = 0x0004_0d00u;

        engine.Strict = false;
        engine.ReturnsEachCall("Clock.Now", () => rig?.Now ?? 10f)
              .Returns("Entities.IsValid", true)
              .WhenCalled("Entities.SetOrigin", c => origins[(EntityHandle)c.Args[0]!] = (Vector)c.Args[1]!)
              .ReturnsEachCall("Entities.GetOrigin", () => origins.GetValueOrDefault(Arg(engine, "Entities.GetOrigin")))
              .WhenCalled("Entities.SetAngles", c => angles[(EntityHandle)c.Args[0]!] = (Vector)c.Args[1]!)
              .ReturnsEachCall("Entities.GetAngles", () => angles.GetValueOrDefault(Arg(engine, "Entities.GetAngles")))
              .Returns("Entities.GetWorldSpaceCenter", Start)
              .Returns("WorldQuery.TraceLineSurface",
                       ahead ?? new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None, 0, 0))
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Returns("WorldQuery.PointContents", Contents.Empty)
              .ReturnsEachCall("Entities.Create", () => new EntityHandle(++made));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Rocket).Assembly);
        registry.Services = services;
        rig = new Rig(registry, engine, blast, temps, motion, origins, screens);

        registry.Spawn(Missile, EntityNames.RpgMissile);
        rig.Rocket.FiredFromLauncher(Shooter, Vector.Zero, grace);
        return rig;
    }

    private static EntityHandle Arg(RecordingEngine engine, string member)
        => (EntityHandle)engine.Calls.Last(c => c.Member == member).Args[0]!;

    private static IEnumerable<string> Models(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "EntityRender.SetModel" && (EntityHandle)c.Args[0]! == Missile)
                       .Select(c => (string)c.Args[1]!);

    private static bool Emitted(RecordingEngine engine, string sound)
        => engine.Calls.Any(c => c.Member == "Sound.EntityEmitSound" && (EntityHandle)c.Args[0]! == Missile
                                 && (string)c.Args[1]! == sound);

    private static void AssertNear(Vector expected, Vector actual, float tolerance = 0.01f)
        => Assert.True((expected - actual).Length <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void ItLeavesOnTheLaunchModelLoftedAndFalling()
    {
        var rig = Fired();

        Assert.Equal(new[] { Rocket.LaunchModel }, Models(rig.Engine));
        AssertNear(new Vector(300, 0, 128), rig.Motion.VelocityOf(Missile));
        Assert.Equal(MotionKind.Toss, rig.Motion.KindOf(Missile));
    }

    [Fact]
    public void ItKeepsFacingTheAimWhileLofted()
    {
        var rig = Fired();

        var set = rig.Engine.Calls.Last(c => c.Member == "Entities.SetAngles" && (EntityHandle)c.Args[0]! == Missile);
        Assert.Equal(Vector.Zero, (Vector)set.Args[1]!);
    }

    [Fact]
    public void ItIgnitesThreeTenthsOfASecondAfterItLeaves()
    {
        var rig = Fired();

        Assert.Equal(Rocket.Stage.Ignite, rig.Rocket.NextStage);
        Assert.Equal(0.3f, rig.Rocket.NextThinkDelay(), 3);
    }

    [Fact]
    public void IgnitionSwapsTheModelAndFliesFlatAtRpgSpeed()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        Assert.Equal(new[] { Rocket.LaunchModel, Rocket.FlyingModel }, Models(rig.Engine));
        AssertNear(new Vector(1500, 0, 0), rig.Motion.VelocityOf(Missile));
        Assert.Equal(MotionKind.Fly, rig.Motion.KindOf(Missile));
        Assert.Equal(Rocket.Stage.Seek, rig.Rocket.NextStage);
    }

    /// <summary>
    /// The launch flies as CMissile::Spawn's eight-unit box, and the ignited
    /// missile as the point IgniteThink's UTIL_SetSize( vec3_origin ) leaves.
    /// </summary>
    [Fact]
    public void ItSweepsItsBoxUntilIgnitionAndAPointAfter()
    {
        var rig = Fired();
        Assert.Equal(new Vector(4f, 4f, 4f), rig.Motion.HullOf(Missile));

        rig.ThinkAt(10.3f);

        Assert.Equal(MotionKind.Fly, rig.Motion.KindOf(Missile));
        Assert.Equal(Vector.Zero, rig.Motion.HullOf(Missile));
    }

    [Fact]
    public void IgnitionPlaysMissileIgnite()
    {
        var rig = Fired();
        Assert.False(Emitted(rig.Engine, Rocket.IgniteSound), "not before it ignites");

        rig.ThinkAt(10.3f);

        Assert.True(Emitted(rig.Engine, Rocket.IgniteSound));
    }

    [Fact]
    public void IgnitionFlashesTheShootersScreen()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        Assert.Equal(new[] { (Shooter, 255, 225, 205, 64, 0.1f, 0f, FadeFlags.In) }, rig.Screens.Fades);
    }

    [Fact]
    public void IgnitionTrailsARocketTrailParentedToIt()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.Create" && (string)c.Args[0]! == EntityNames.EnvRockettrail);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Entities.SetParent" && (EntityHandle)c.Args[1]! == Missile);
    }

    [Fact]
    public void TheRocketTrailIsNotSolidAsFollowEntityLeavesIt()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        var parented = rig.Engine.Calls.Last(c => c.Member == "Entities.SetParent" && (EntityHandle)c.Args[1]! == Missile);
        Assert.Contains(rig.Engine.Calls, c => c.Member == "EntityPhysics.SetNotSolid"
                                               && (EntityHandle)c.Args[0]! == (EntityHandle)parented.Args[0]! && (bool)c.Args[1]!);
    }

    [Fact]
    public void TheTrailCarriesCreateSmokeTrailsNumbers()
    {
        var state = new RocketTrailNetState();
        state.BindLocal();
        Rocket.ConfigureTrail(state, 2);

        Assert.Equal((0.2f, 100f, 0.5f, 8f, 32f, 4f, 2f, 16f, 2),
                     (state.Opacity, state.SpawnRate, state.ParticleLifetime, state.StartSize, state.EndSize,
                      state.SpawnRadius, state.MinSpeed, state.MaxSpeed, state.Attachment));
        Assert.Equal((new Vector(0.65f, 0.65f, 0.65f), Vector.Zero), (state.StartColor, state.EndColor));
    }

    [Fact]
    public void AClearShotPassesThroughPlayersUntilIgnition()
    {
        var rig = Fired(grace: true);
        Assert.True(rig.Motion.PassesCharacters(Missile));

        rig.ThinkAt(10.3f);

        Assert.False(rig.Motion.PassesCharacters(Missile));
    }

    [Fact]
    public void ABlockedShotHasNoGracePeriod()
    {
        var rig = Fired(grace: false);

        Assert.False(rig.Motion.PassesCharacters(Missile));
    }

    [Fact]
    public void ItSteersAnEighthOfTheWayTowardItsShootersDot()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);
        // A dot far off to the side, so the missile chases the dot itself.
        rig.Dot(Shooter, new Vector(0, 5000, 64));
        rig.Origins[Missile] = new Vector(2000, 0, 64);

        rig.ThinkAt(10.4f);

        Vector chase = new(10, 5000, 64);
        Vector expected = Rocket.Homing(new Vector(2000, 0, 64), new Vector(1500, 0, 0),
                                        Rocket.ActualDotPosition(new Vector(2000, 0, 64), Start, chase, false),
                                        Rocket.HomingSpeed) * 1500f;
        AssertNear(expected, rig.Motion.VelocityOf(Missile));
        Assert.True(rig.Motion.VelocityOf(Missile).Y > 0f, "it turned toward the dot");
    }

    [Fact]
    public void ADotThatIsOffIsNotFollowed()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);
        rig.Dot(Shooter, new Vector(0, 5000, 64), on: false);
        rig.Origins[Missile] = new Vector(2000, 0, 64);

        rig.ThinkAt(10.4f);

        AssertNear(new Vector(1500, 0, 0), rig.Motion.VelocityOf(Missile));
    }

    [Fact]
    public void AnotherPlayersDotIsNotFollowed()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);
        rig.Dot(Other, new Vector(0, 5000, 64));
        rig.Origins[Missile] = new Vector(2000, 0, 64);

        rig.ThinkAt(10.4f);

        AssertNear(new Vector(1500, 0, 0), rig.Motion.VelocityOf(Missile));
    }

    [Fact]
    public void NearTheBeamItRidesTheLineTwoHundredFiftySixUnitsAhead()
    {
        // Shooter at the origin, the dot 2000 out along +X, the missile 100 off the line at x = 500.
        Vector at = Rocket.ActualDotPosition(new Vector(500, 100, 0), Vector.Zero, new Vector(2000, 0, 0), false);

        AssertNear(new Vector(756, 0, 0), at);
    }

    [Fact]
    public void PastTheDotAndFarFromItItChasesTheDot()
    {
        Vector at = Rocket.ActualDotPosition(new Vector(3000, 1000, 0), Vector.Zero, new Vector(2000, 0, 0), false);

        Assert.Equal(new Vector(2000, 0, 0), at);
    }

    [Fact]
    public void ADotOnSomethingThatCanBeHurtIsChasedDirectly()
    {
        Vector at = Rocket.ActualDotPosition(new Vector(500, 100, 0), Vector.Zero, new Vector(2000, 0, 0), true);

        Assert.Equal(new Vector(2000, 0, 0), at);
    }

    [Fact]
    public void TheChasePositionIsTenUnitsIntoTheFace()
    {
        Assert.Equal(new Vector(110, 0, 0), LaserDot.ChasePositionFor(new Vector(100, 0, 0), new Vector(-1, 0, 0)));
    }

    [Fact]
    public void ItGoesOffWithTheRpgsDamageOutToTwiceThat()
    {
        var rig = Fired();
        rig.Rocket.OnTouch(EntityHandle.None);

        var blast = Assert.Single(rig.Blast.Requests);
        Assert.Equal((150f, 300f, Shooter, Missile), (blast.Magnitude, blast.Radius, blast.Attacker, blast.Inflictor));
        Assert.Equal(1, rig.Registry.Directory.PendingRemovalCount);
    }

    [Fact]
    public void AgainstTheSkyItFliesOffWithoutABang()
    {
        var rig = Fired(ahead: new SurfaceTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, EntityHandle.None, 0, 0x0004));
        rig.Rocket.OnTouch(EntityHandle.None);

        Assert.Empty(rig.Blast.Requests);
        Assert.Equal(1, rig.Registry.Directory.PendingRemovalCount);
    }

    [Fact]
    public void GoingOffStopsMissileIgnite()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);
        rig.Rocket.OnTouch(EntityHandle.None);

        Assert.Contains(rig.Engine.Calls, c => c.Member == "Sound.EntityStopSound" && (EntityHandle)c.Args[0]! == Missile
                                               && (string)c.Args[1]! == Rocket.IgniteSound);
    }

    [Fact]
    public void ItsLauncherWaitsOnItUntilItGoesOff()
    {
        var rig = Fired();
        Assert.True(rig.Rocket.LauncherWaits);

        rig.Rocket.OnTouch(EntityHandle.None);

        Assert.False(rig.Rocket.LauncherWaits);
    }

    [Fact]
    public void BulletsAndBlastsDoNotHurtIt()
    {
        var rig = Fired();

        Assert.Equal(0, rig.Rocket.OnDamaged(Other, 50f, DamageType.Bullet | DamageType.Blast));
        Assert.Equal(Rocket.MaxHealth, rig.Rocket.MissileHealth);
    }

    [Fact]
    public void MissileDefenceShootsItDownIntoAnAuger()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        rig.Rocket.OnDamaged(Other, 25f, Rocket.MissileDefense);

        Assert.Equal(Rocket.Stage.Auger, rig.Rocket.NextStage);
        Assert.False(rig.Rocket.LauncherWaits, "the RPG may reload at once");
        Assert.Equal(new[] { Rocket.ShotDownEffect }, rig.Temps.Dispatched);
    }

    [Fact]
    public void ABlowThatLeavesItAboveItsAugerHealthOnlyHurtsIt()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);

        rig.Rocket.OnDamaged(Other, 10f, Rocket.MissileDefense);

        Assert.Equal(Rocket.Stage.Seek, rig.Rocket.NextStage);
        Assert.True(rig.Rocket.LauncherWaits);
    }

    [Fact]
    public void AnAugeringMissileGoesOffAfterASecondAndAHalf()
    {
        var rig = Fired();
        rig.ThinkAt(10.3f);
        rig.Rocket.OnDamaged(Other, 25f, Rocket.MissileDefense);

        rig.ThinkAt(10.35f);
        Assert.Empty(rig.Blast.Requests);
        AssertNear(new Vector(1000, 0, 0), new Vector(rig.Motion.VelocityOf(Missile).Length, 0, 0), 0.5f);

        rig.ThinkAt(11.9f);
        Assert.Single(rig.Blast.Requests);
    }
}
