/*
 * A ROUND THAT LANDS LEAVES A MARK.
 *
 * THE BUG. `Arsenal.FireBullets` -- the port's whole bullet path -- traced,
 * applied damage and, for a player, bled. It never dispatched an impact effect,
 * so a shot into a wall, a floor or a crate left no decal, no flecks, no dust
 * and no impact sound. Lane `live-batch` measured it with the whole firing
 * chain asserted first: a crate at the aim point, `debug-api aim` naming
 * prop_physics, carried rounds 90 -> 63, sprite quads +2115 -- and both effect
 * ledgers flat, temp entities 58 -> 58, impact particles 0 -> 0.
 *
 * IT WAS NEVER A REGRESSION. `git log -S'ImpactEffect' --follow` over
 * Arsenal.cs names no commit at all, across the sixteen that have touched it.
 * The call was never there to lose, so this is the first test of it.
 *
 * AND IT WAS SILENT, which is what makes it the shape plan_entity.md §7 forbids
 * rather than merely a gap: no refusal printed, no counter moved, nothing to
 * read. A test is the only thing that notices.
 *
 * WHAT IS PINNED HERE is the SERVER half -- that a landing round asks, what it
 * asks with, and for what. The client half (something in this build claiming
 * the dispatch name, and resolving the entity by a spelling this client can
 * answer) is `src/sourcesharp/test/impact_dispatch_test.cpp`, and that it
 * actually draws is `tools/fx-impact-gate`, where the native impact ledger goes
 * 0 -> 41 particles on five rounds against 0 -> 0 before.
 *
 * DRIVEN THROUGH THE REAL MACHINE, not through a stub arms object.
 * `ServerWeaponArms` is a private nested class over a private `Carry`, so there
 * is no way to construct one -- and that is the right shape to test anyway:
 * what a live server does is `OnPlayerCommand` with the attack button set, and
 * everything between that and the trace is part of what could break.
 */

using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class WeaponImpactTests
{
    private const uint PlayerHandle = 0x0001_0001;

    private static EntityHandle Player => new(PlayerHandle);

    /// <summary>The crate the round lands in, on the parallel hierarchy.</summary>
    private static EntityHandle Crate => new(0x0002_0007);

    /// <summary>Where the trace says the round stopped.</summary>
    private static Vector Landed => new(64f, 0f, 64f);

    /// <summary>
    /// The surfaceprop the trace found. 13 is not a magic number for its own
    /// sake — it is what a live run read off dm_lockdown's floor, and any value
    /// that is neither 0 nor -1 makes the point: it has to arrive at the effect
    /// unchanged, because the client picks the decal and the impact sound from
    /// the material behind it.
    /// </summary>
    private const int TracedSurfaceProp = 13;

    private const int Attack = (int)InputButtons.Attack;

    /// <summary>
    /// An armed player standing at the origin, looking down +X, with a surface
    /// 64 units in front of them.
    ///
    /// <para>
    /// The SHOOT ORIGIN and the EYE ANGLES are stated because the spread sample
    /// is built from them and the impact's start point is stepped back along
    /// the result; a fixture that left them at zero would make "the start is
    /// behind the end" true by accident rather than by the code.
    /// </para>
    /// </summary>
    private static (Arsenal arsenal, RecordingEngine engine) Armed(
        bool hit = true, EntityHandle? subject = null, int surfaceProp = TracedSurfaceProp,
        string weapon = "weapon_smg1", HitGroup traced = HitGroup.Generic,
        DamageHitGroups? hitGroups = null, int surfaceFlags = 0,
        PredictionSuppression? prediction = null, IWeaponSounds? weaponSounds = null,
        Func<RecordingEngine, IEngineEffects>? effects = null)
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f))
            .Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f))
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            // The floor probe a weapon drop takes. Nothing in this file drops a
            // weapon, but the Arsenal reaches it on other paths and an
            // unanswered member is a different failure from the one under test.
            .Returns("WorldQuery.TraceLine",
                     new TraceResult(true, 0.5f, new Vector(0f, 0f, 8f),
                                     new Vector(0f, 0f, 1f), EntityHandle.None))
            .Returns("WorldQuery.TraceLineSurface",
                     new SurfaceTraceResult(hit, hit ? 0.001f : 1f, Landed,
                                            new Vector(-1f, 0f, 0f),
                                            subject ?? Crate,
                                            hit ? surfaceProp : -1,
                                            surfaceFlags, traced))
            // Dry land: where a round stops is not water.
            .Returns("WorldQuery.PointContents", Contents.Empty)
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine,
                                  effects?.Invoke(engine) ?? engine,
                                  engine, engine, registry, engine, hitGroups ?? new DamageHitGroups(),
                                  prediction: prediction, weaponSounds: weaponSounds);

        arsenal.GiveWeapon(Player, weapon);

        return (arsenal, engine);
    }

    /// <summary>
    /// One command with the attack button set — and THE CLOCK MOVED FIRST.
    ///
    /// <para>
    /// Not incidental. Deploying a weapon sets <c>NextPrimaryAttack</c> ahead
    /// of the current time, so a trigger pulled at the same instant the weapon
    /// was given is refused by the machine and NOTHING about the impact path is
    /// exercised — nine of these facts went red on a correct tree that way, all
    /// with the message they would print for the defect itself. A fixture whose
    /// silence has two causes cannot tell them apart.
    /// </para>
    /// </summary>
    private static void PullTheTrigger(Arsenal arsenal, RecordingEngine engine)
    {
        engine.Returns("Clock.Now", 30f);

        var cmd = new UserCmd { Buttons = Attack };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);
    }

    /// <summary>
    /// THE FIXTURE'S OWN PRECONDITION, asserted rather than assumed: the
    /// trigger really was pulled. Every fact below reads an ABSENCE or a value
    /// that only exists if a round was spent, and "the gun did not fire" would
    /// satisfy the absence and crash the value.
    /// </summary>
    private static void AssertARoundWasSpent(RecordingEngine engine)
    {
        Assert.True(engine.Called("Sound.EntityEmitSound"),
            "the weapon must actually have fired — a machine that refused the "
          + "trigger produces the same silence the defect did");
    }

    private static EngineCall? Impact(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Effects.ImpactEffect")
                       .Cast<EngineCall?>()
                       .FirstOrDefault();

    // =====================================================================
    // That it happens at all.
    // =====================================================================

    [Fact]
    public void ARoundThatLandsDispatchesAnImpactEffect()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is not null,
            "a round that landed must dispatch an impact effect. Stock's "
          + "CBaseEntity::FireBullets calls DoImpactEffect for every pellet "
          + "that lands (baseentity_shared.cpp:1986); this path called nothing, "
          + "and called it silently");
    }

    [Fact]
    public void ARoundThatLandsInNOTHINGDispatchesNoImpact()
    {
        var (arsenal, engine) = Armed(hit: false);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is null,
            "a trace that was never stopped has nothing to mark. This is what "
          + "keeps the clause above from being a check that cannot fail");
    }

    // =====================================================================
    // What it carries.
    // =====================================================================

    [Fact]
    public void TheImpactLandsWhereTheTraceSaysTheRoundStopped()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(Landed, Impact(engine)!.Value.ArgAs<Vector>(1));
    }

    [Fact]
    public void TheImpactCarriesTheSURFACEPROPTheTraceFound()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine)!.Value.ArgAs<int>(2) == TracedSurfaceProp,
            "the client chooses the decal AND the impact sound from the "
          + "material behind this number — it is what CBaseEntity::ImpactTrace "
          + "writes into its CEffectData. A server that named none would leave "
          + "a wooden crate taking a concrete decal and cement flecks");
    }

    [Fact]
    public void TheImpactCarriesTheAmmoRowsDamageType()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(AmmoCatalog.Stock().For("SMG1")!.Value.DamageType,
                     Impact(engine)!.Value.ArgAs<DamageType>(3));
    }

    [Fact]
    public void TheImpactNamesWhatWasHit()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.Equal(Crate, Impact(engine)!.Value.ArgAs<EntityHandle>(4));
    }

    [Fact]
    public void TheImpactStartsBEHINDTheLandingPointSoTheClientCanRetrace()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Vector start = Impact(engine)!.Value.ArgAs<Vector>(0);
        Vector end = Impact(engine)!.Value.ArgAs<Vector>(1);

        Assert.True((end - start).Length > 1f,
            "the two points must not collapse: the client re-traces "
          + "start->origin in fx_impact.cpp's Impact() to find the plane it "
          + "decals, and a zero-length ray finds nothing");

        Assert.True(start.X < end.X,
            "and the start is on the side the shot came FROM — the player is "
          + "at the origin looking down +X, so a start point in front of the "
          + "impact would put the re-trace inside the solid");
    }

    // =====================================================================
    // Which trace it is on, and who it is for.
    // =====================================================================

    [Fact]
    public void TheShotIsOnTheTraceThatAnswersASurface()
    {
        var (arsenal, engine) = Armed();

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(engine.Called("WorldQuery.TraceLineSurface"),
            "the narrow trace cannot answer a surfaceprop, and the surfaceprop "
          + "is what the whole impact is chosen from");
    }

    // =====================================================================
    // The push (plan_physics_managed A30): stock's FireBullets fills the
    // damage with CalculateBulletDamageForce, so what it hits is shoved.
    // =====================================================================

    [Fact]
    public void ARoundCarriesItsAmmosForceAlongTheShotToWhereItLanded()
    {
        var (arsenal, engine) = Armed();
        // The fixture ignores IsValid (false), so no other fact here reaches the
        // damage arm; this one must.
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;   // a valid player also animates; that is not this fact

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(Crate, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(Player, hit.ArgAs<EntityHandle>(1));

        Vector force = hit.ArgAs<Vector>(4);
        Assert.InRange(force.Length, 667.9f, 668.1f);   // SMG1's DamageForce x phys_pushscale 1
        Assert.True(force.X > 600f, "along the shot: the player looks down +X");
        Assert.Equal(Landed, hit.ArgAs<Vector>(5));

        Assert.False(engine.Called("Damage.Apply"), "the forceless form is not used as well");
    }

    // =====================================================================
    // The hitgroup (tf2 stage 0.8): the trace's tr.hitgroup reaches the
    // victim beside the damage call, as stock hands TraceAttack the trace.
    // =====================================================================

    /// <summary>
    /// Fires one round whose trace stopped on <paramref name="traced"/> at a
    /// valid victim, and answers the hitgroup that was open WHILE the damage
    /// call ran, and the one open after the trigger was released.
    /// </summary>
    private static (HitGroup During, HitGroup After, int Calls) ShootAt(HitGroup traced)
    {
        var hitGroups = new DamageHitGroups();
        var (arsenal, engine) = Armed(traced: traced, hitGroups: hitGroups);
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;   // a valid player also animates; that is not this fact

        HitGroup during = (HitGroup)(-1);
        engine.WhenCalled("Damage.ApplyAt", _ => during = hitGroups.Current);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        return (during, hitGroups.Current, engine.CountOf("Damage.ApplyAt"));
    }

    [Fact]
    public void ARoundCarriesTheTracesHitgroupIntoTheDamageCall()
    {
        var head = ShootAt(HitGroup.Head);
        var chest = ShootAt(HitGroup.Chest);

        Assert.True(head.Calls == 1 && chest.Calls == 1, "each round reached the damage call once");
        Assert.True(head.During == HitGroup.Head,
            $"a head trace is the head while the victim is hurt (read {head.During})");
        Assert.True(chest.During == HitGroup.Chest,
            $"and a chest trace the chest (read {chest.During}): the value is the trace's, not a constant");
    }

    [Fact]
    public void ARoundOnNoHitboxIsGenericAndTheScopeCloses()
    {
        var plain = ShootAt(HitGroup.Generic);
        var head = ShootAt(HitGroup.Head);

        Assert.True(plain.Calls == 1, "the round reached the damage call");
        Assert.True(plain.During == HitGroup.Generic,
            $"a trace that struck no hitbox hurts as Generic (read {plain.During})");
        Assert.True(head.After == HitGroup.Generic,
            "and the shot's hitgroup does not outlive its damage call: the next "
          + "hit, a blast or a fall, has no trace behind it");
    }

    [Fact]
    public void ASwingCarriesTheMeleeForceForItsDamage()
    {
        // CBaseHLBludgeonWeapon::Hit: CalculateMeleeDamageForce, damage x
        // ImpulseScale( 75, 4 ) along the swing, at the trace's end.
        var (arsenal, engine) = Armed(weapon: "weapon_crowbar");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        var struck = new Vector(40f, 0f, 64f);
        engine.Returns("WorldQuery.TraceLineSurface",
                       new SurfaceTraceResult(true, 0.5f, struck, new Vector(-1f, 0f, 0f), Crate,
                                              TracedSurfaceProp, 0));

        PullTheTrigger(arsenal, engine);

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(Crate, hit.ArgAs<EntityHandle>(0));
        float damage = hit.ArgAs<float>(2);
        Assert.True(damage > 0f, "the swing did damage");
        Vector force = hit.ArgAs<Vector>(4);
        Assert.InRange(force.X, damage * DamageForceRules.PerDamagePoint * 0.999f, damage * DamageForceRules.PerDamagePoint * 1.001f);
        Assert.Equal(struck, hit.ArgAs<Vector>(5));
    }

    /// <summary>
    /// A crowbar whose ray meets nothing, with the crate half a hull to the
    /// side: CBaseHL2MPBludgeonWeapon::Swing's hull catches it, and the swing
    /// lands where ChooseIntersectionPointAndActivity's doubled line meets it.
    /// </summary>
    private static (RecordingEngine Engine, int Hulls) SwingPast(Vector crateAt)
    {
        var (arsenal, engine) = Armed(hit: false, weapon: "weapon_crowbar");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;

        int lines = 0;
        var miss = new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None, -1, 0);
        var meets = new SurfaceTraceResult(true, 0.5f, new Vector(40f, 10f, 64f), new Vector(-1f, 0f, 0f), Crate,
                                           TracedSurfaceProp, 0);
        engine.ReturnsEachCall("WorldQuery.TraceLineSurface", () => ++lines == 1 ? miss : meets)
              .Returns("WorldQuery.TraceBox", new SurfaceTraceResult(true, 0.6f, new Vector(20f, 0f, 64f),
                                                                     new Vector(-1f, 0f, 0f), Crate, TracedSurfaceProp, 0))
              .ReturnsEachCall("Entities.GetOrigin",
                               () => engine.Last("Entities.GetOrigin")!.Value.ArgAs<EntityHandle>(0) == Crate
                                   ? crateAt : Vector.Zero);

        PullTheTrigger(arsenal, engine);
        return (engine, engine.CountOf("WorldQuery.TraceBox"));
    }

    [Fact]
    public void ANearMissTheSwingsHullCatchesIsAHit()
    {
        var (engine, hulls) = SwingPast(crateAt: new Vector(60f, 10f, 64f));

        Assert.True(hulls > 0, "the ray's miss tried the hull");
        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.ApplyAt");
        Assert.Equal(Crate, hit.ArgAs<EntityHandle>(0));
        Assert.Equal(new Vector(40f, 10f, 64f), hit.ArgAs<Vector>(5));
    }

    /// <summary>Swing's "YWB" test: a hull hit the swinger is not facing (here, behind them) is a miss.</summary>
    [Fact]
    public void AHullHitTheSwingerIsNotFacingIsAMiss()
    {
        var (engine, hulls) = SwingPast(crateAt: new Vector(-60f, 10f, 64f));

        Assert.True(hulls > 0, "the ray's miss tried the hull");
        Assert.False(engine.Called("Damage.ApplyAt"), "a crate behind the swinger was hit");
    }

    /// <summary>
    /// ImpactWater: a swing that meets nothing and ends under water splashes
    /// where it went in; the same swing ending in air does not.
    /// </summary>
    private static RecordingEngine SwingInto(Contents atTheEnd)
    {
        var (arsenal, engine) = Armed(hit: false, weapon: "weapon_crowbar");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        var surface = new Vector(30f, 0f, 50f);
        engine.Returns("WorldQuery.TraceBox", new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero,
                                                                     EntityHandle.None, -1, 0))
              .ReturnsEachCall("WorldQuery.PointContents",
                               () => engine.Last("WorldQuery.PointContents")!.Value.ArgAs<Vector>(0).X > 0f
                                   ? atTheEnd : Contents.Empty)
              .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.4f, surface, new Vector(0f, 0f, 1f),
                                                               EntityHandle.None));

        PullTheTrigger(arsenal, engine);
        return engine;
    }

    [Fact]
    public void AMissIntoWaterSplashesWhereItWentIn()
    {
        var splash = Assert.Single(SwingInto(Contents.Water).Calls, c => c.Member == "Effects.SplashEffect");

        Assert.Equal((new Vector(30f, 0f, 50f), 8f, false),
                     (splash.ArgAs<Vector>(0), splash.ArgAs<float>(1), splash.ArgAs<bool>(2)));
    }

    [Fact]
    public void AMissIntoSlimeSplashesAsSlime()
        => Assert.True(Assert.Single(SwingInto(Contents.Slime).Calls, c => c.Member == "Effects.SplashEffect")
                             .ArgAs<bool>(2));

    [Fact]
    public void AMissIntoAirMakesNoSplash()
        => Assert.False(SwingInto(Contents.Empty).Called("Effects.SplashEffect"));

    /// <summary>
    /// HandleShotImpactingWater: a round that ends under water splashes where
    /// it went in, the ammo's size, marks nothing below, and still hurts what
    /// it hit. Found against stock (2026-10-25): this mod's rounds marked a
    /// pool's floor and never splashed.
    /// </summary>
    private static (RecordingEngine Engine, RecordingEffectDispatch Temps) ShotInto(Contents liquid)
    {
        var (arsenal, engine) = Armed(weapon: "weapon_pistol");
        var temps = new RecordingEffectDispatch();
        arsenal.Temps = temps;
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        engine.Returns("WorldQuery.PointContents", liquid)
              .Returns("WorldQuery.TraceLine", new TraceResult(true, 0.05f, new Vector(32f, 0f, 60f),
                                                               new Vector(0f, 0f, 1f), EntityHandle.None));

        PullTheTrigger(arsenal, engine);
        return (engine, temps);
    }

    [Fact]
    public void ARoundIntoWaterSplashesWhereItWentInAndMarksNothingBelow()
    {
        var (engine, temps) = ShotInto(Contents.Water);

        var splash = Assert.Single(temps.Effects, e => e.Name == "gunshotsplash");
        Assert.Equal((new Vector(32f, 0f, 60f), 0), (splash.Data.Origin, splash.Data.Flags));
        Assert.InRange(splash.Data.Scale, 4f, 8f);
        Assert.Null(Impact(engine));
        Assert.True(engine.Called("Damage.ApplyAt"), "the round under water still hurt what it hit");
    }

    [Fact]
    public void ARoundIntoSlimeSplashesAsSlime()
        => Assert.Equal(1, Assert.Single(ShotInto(Contents.Slime).Temps.Effects, e => e.Name == "gunshotsplash").Data.Flags);

    [Fact]
    public void ARoundOnDryLandMakesNoSplash()
    {
        var (engine, temps) = ShotInto(Contents.Empty);

        Assert.DoesNotContain(temps.Effects, e => e.Name == "gunshotsplash");
        Assert.NotNull(Impact(engine));
    }

    [Fact]
    public void ABulletsForceIsTheAmmoForceAlongTheNormalisedDirection()
    {
        Vector force = DamageForceRules.BulletForce(new Vector(0f, 2f, 0f), 668f);

        Assert.Equal(new Vector(0f, 668f, 0f), force);
        Assert.Equal(new Vector(0f, 1336f, 0f), DamageForceRules.BulletForce(new Vector(0f, 2f, 0f), 668f, pushScale: 2f));
    }

    [Fact]
    public void AMeleeForceIsTheDamageTimesTheSeventyFiveKgFourInchImpulse()
    {
        Vector force = DamageForceRules.MeleeForce(new Vector(3f, 0f, 0f), damage: 25f);

        Assert.Equal(new Vector(25f * DamageForceRules.ImpulseScale(75f, 4f), 0f, 0f), force);
    }

    [Fact]
    public void ARoundIntoTheWORLDDispatchesAnImpactToo()
    {
        // No entity at all — which is what a shot into a wall or a floor is,
        // and it is the COMMON case. Stock dispatches for it: UTIL_ImpactTrace
        // resolves such a trace to worldspawn and CWorld::ImpactTrace answers.
        var (arsenal, engine) = Armed(subject: EntityHandle.None);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(Impact(engine) is not null,
            "an impact must not be gated on naming a subject. The damage arm "
          + "needs one; the mark a bullet leaves does not, and a wall is what "
          + "most rounds hit");
    }

    [Fact]
    public void ARoundIntoTheWORLDStillDamagesNothing()
    {
        var (arsenal, engine) = Armed(subject: EntityHandle.None);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.False(engine.Called("Damage.Apply"),
            "and the damage arm keeps its own guard — this is the other half "
          + "of the clause above, and without it 'ungate the impact' could be "
          + "read as 'ungate everything'");
    }

    // =====================================================================
    // Per pellet, not per pull.
    // =====================================================================

    [Fact]
    public void EveryPelletOfABuckshotLoadLeavesItsOwnMark()
    {
        var (arsenal, engine) = Armed();
        arsenal.GiveWeapon(Player, "weapon_shotgun");
        arsenal.Switch(Player, "weapon_shotgun");

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        int pellets = WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Pellets;
        int marks = engine.Calls.Count(c => c.Member == "Effects.ImpactEffect");

        Assert.True(marks == pellets,
            $"a shotgun that fired {pellets} pellet(s) into a surface left "
          + $"{marks} mark(s). One per pellet is stock — FireBullets' loop "
          + "calls DoImpactEffect inside it, not after it");
    }

    // =====================================================================
    // The predicting shooter's copies (stock's prediction rules), the
    // swing's impact, the sky, and the seed the pellets spread from.
    // =====================================================================

    private sealed class HostAtEmit(PredictionSuppression prediction) : IWeaponSounds
    {
        public readonly List<(string Sound, int Host)> Emitted = [];

        public PredictionSuppression Prediction => prediction;

        public void Emit(EntityHandle owner, string soundName) => Emitted.Add((soundName, prediction.Host));
    }

    private sealed class HostAtImpact(IEngineEffects inner, PredictionSuppression prediction) : EngineEffectsForwarder(inner)
    {
        public readonly List<int> Hosts = [];

        public override void ImpactEffect(Vector start, Vector end, int surfaceProp, DamageType damageType, EntityHandle entity)
        {
            Hosts.Add(prediction.Host);
            base.ImpactEffect(start, end, surfaceProp, damageType, entity);
        }
    }

    private static (Arsenal Arsenal, RecordingEngine Engine, HostAtEmit Sounds, HostAtImpact Effects) Predicted(
        string weaponsConVar = "1")
    {
        var clients = new PredictionSuppressionTests.Clients().Predicting(1, weapons: weaponsConVar);
        var prediction = new PredictionSuppression(clients);
        var sounds = new HostAtEmit(prediction);
        HostAtImpact? effects = null;
        var (arsenal, engine) = Armed(prediction: prediction, weaponSounds: sounds,
                                      effects: e => effects = new HostAtImpact(e, prediction));
        engine.Returns("Entities.GetIndex", 1);
        return (arsenal, engine, sounds, effects!);
    }

    [Fact]
    public void A_predicting_shooters_shot_sound_and_impact_go_out_with_it_as_the_suppress_host()
    {
        var (arsenal, engine, sounds, effects) = Predicted();

        PullTheTrigger(arsenal, engine);

        Assert.Equal(("Weapon_SMG1.Single", 1), Assert.Single(sounds.Emitted));
        Assert.NotEmpty(effects.Hosts);
        Assert.All(effects.Hosts, host => Assert.Equal(1, host));
        Assert.False(engine.Called("Sound.EntityEmitSound"), "the weapon sound is the prediction-rules one");
    }

    [Fact]
    public void A_shooter_who_does_not_predict_weapons_is_sent_every_copy()
    {
        var (arsenal, engine, sounds, effects) = Predicted(weaponsConVar: "0");

        PullTheTrigger(arsenal, engine);

        Assert.Equal(("Weapon_SMG1.Single", 0), Assert.Single(sounds.Emitted));
        Assert.NotEmpty(effects.Hosts);
        Assert.All(effects.Hosts, host => Assert.Equal(0, host));
    }

    [Fact]
    public void The_window_closes_with_the_command()
    {
        var (arsenal, engine, sounds, _) = Predicted();

        PullTheTrigger(arsenal, engine);

        Assert.Equal(1, Assert.Single(sounds.Emitted).Host);
        Assert.Equal(0, sounds.Prediction.Host);
    }

    [Fact]
    public void A_swing_that_lands_marks_what_it_struck_as_a_club()
    {
        var (arsenal, engine) = Armed(weapon: "weapon_crowbar");
        engine.Strict = false;

        PullTheTrigger(arsenal, engine);

        var impact = Impact(engine);
        Assert.True(impact is not null, "CBaseHLBludgeonWeapon::Hit's ImpactEffect: UTIL_ImpactTrace( DMG_CLUB )");
        Assert.Equal(Landed, impact!.Value.ArgAs<Vector>(1));
        Assert.Equal(TracedSurfaceProp, impact.Value.ArgAs<int>(2));
        Assert.Equal(DamageType.Club, impact.Value.ArgAs<DamageType>(3));
    }

    [Fact]
    public void A_swing_into_nothing_marks_nothing()
    {
        var (arsenal, engine) = Armed(hit: false, weapon: "weapon_crowbar");
        engine.Strict = false;

        PullTheTrigger(arsenal, engine);

        Assert.True(engine.Called("WorldQuery.TraceLineSurface"), "the swing traced (the positive arm)");
        Assert.Null(Impact(engine));
    }

    [Fact]
    public void A_round_into_the_sky_marks_nothing()
    {
        var (arsenal, engine) = Armed(surfaceFlags: 0x0004);

        PullTheTrigger(arsenal, engine);
        AssertARoundWasSpent(engine);

        Assert.True(engine.Called("WorldQuery.TraceLineSurface"));
        Assert.Null(Impact(engine));
    }

    private static List<Vector> PelletEnds(int commandNumber)
    {
        var (arsenal, engine) = Armed();
        arsenal.GiveWeapon(Player, "weapon_shotgun");
        arsenal.Switch(Player, "weapon_shotgun");
        engine.Returns("Clock.Now", 30f);

        var cmd = new UserCmd { Buttons = Attack, CommandNumber = commandNumber };
        arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        return engine.Calls.Where(c => c.Member == "WorldQuery.TraceLineSurface")
                           .Select(c => c.ArgAs<Vector>(1)).ToList();
    }

    [Fact]
    public void The_pellets_spread_from_the_commands_seed_as_the_predicting_client_spreads_them()
    {
        var first = PelletEnds(41);
        var again = PelletEnds(41);
        var other = PelletEnds(42);

        Assert.True(first.Count > 1, "a shotgun traced its pellets (the positive arm)");
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);

        // The direction is ShotSpread's, from the command's random seed.
        var aim = Angles.Vectors(Vector.Zero);
        float spread = ShotSpread.SpreadOf(WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Spread);
        Vector dir = ShotSpread.Apply(aim, spread, ShotSpread.FirstSeed(UserCmdCodec.RandomSeedFor(41)),
                                      new SeededUniformStream()).Normalized;
        Assert.Equal(new Vector(0f, 0f, 64f) + dir * 56756f, first[0]);
    }
}
