using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ss_gib, the managed replacement for <c>CGib</c> — <c>plan_entity.md</c>
/// step 4a.
///
/// <para>
/// Every fact here is one that used to be a fact about C++: the whole of
/// <c>CGib::Spawn</c>, <c>CGib::BounceGibTouch</c> and <c>CGib::DieThink</c>
/// that this build could reach was the tail of <c>SourceSharp_ShootGib</c>, and
/// gib.cpp is out of server_csharp.vpc and off disk. What was a native
/// behaviour nobody could assert is now nine facts.
/// </para>
/// </summary>
public class GibTests
{
    /// <summary>
    /// One spawned gib, bound and ready to be told what it is.
    ///
    /// <para>
    /// The registry spawns it exactly as a map-placed entity is spawned, which
    /// is the same route <see cref="Gibs"/> takes at runtime: create, set the
    /// model, spawn, then <c>Throw</c>. What the test does by hand is only the
    /// part the strict double cannot do for it — hand back a handle.
    /// </para>
    /// </summary>
    private static (SsGib Gib, RecordingEngine Engine) Spawn()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(SsGib).Assembly);

        var services = TestServices.Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;

        engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        engine.Returns("EntityPhysics.GetMass", 10f);

        // The engine's random stream, pinned. A gib draws from it four times --
        // the lifetime jitter, two torque scales and the tumble -- and a test
        // whose subject is what it does with the answer must not also be a test
        // of what the answer was.
        engine.Returns("Host.RandomFloat", 1f);

        // A decal is placed by a trace, so the engine answers whether one landed.
        // Nothing here reads the answer; the count of CALLS is the subject.
        engine.Returns("Effects.BloodDecal", true);

        var handle = new EntityHandle(77u);

        registry.Spawn(handle, EntityNames.SsGib);
        return ((SsGib)registry.Get(handle)!, engine);
    }

    private static GibShot Shot(GibSimulation simulation = GibSimulation.Point,
                                BloodColor blood = BloodColor.Red,
                                SurfaceMaterial material = SurfaceMaterial.Silent,
                                float gravityScale = 0f,
                                float lifetime = 25f,
                                float scale = 0f,
                                int skin = 0)
        => new()
        {
            Origin = new Vector(0f, 0f, 0f),
            Angles = new Vector(0f, 0f, 0f),
            Direction = new Vector(1f, 0f, 0f),
            Speed = 200f,
            Simulation = (int)simulation,
            Lifetime = lifetime,
            AngularVelocity = 100f,
            Skin = skin,
            Scale = scale,
            GravityScale = gravityScale,
            Material = (int)material,
            StrictRemove = 0,
            BloodColor = (int)blood,
        };

    //-------------------------------------------------------------------------
    // What it is told, and what it does with it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A point gib is flown by the managed integrator rather than by the solver
    /// — stock's MOVETYPE_FLYGRAVITY with MOVECOLLIDE_FLY_BOUNCE.
    /// </summary>
    [Fact]
    public void APointGibIsThrownByTheIntegratorAndNotBySolver()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(), invisible: false);

        Assert.False(gib.SolverOwned);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityPhysics.InitPhysics");
    }

    /// <summary>
    /// A physics gib asks the solver for an object, and takes it as debris so it
    /// does not collide with players — <c>COLLISION_GROUP_DEBRIS</c>.
    /// </summary>
    [Fact]
    public void APhysicsGibAsksTheSolverAsDebris()
    {
        var (gib, engine) = Spawn();
        engine.Returns("EntityPhysics.InitPhysics", true);

        gib.Throw(Shot(GibSimulation.Physics), invisible: false);

        Assert.True(gib.SolverOwned);

        var init = Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.InitPhysics");
        Assert.Equal(true, init.Args[2]);   // debris
    }

    /// <summary>
    /// A model with no collision data does not leave a gib frozen in mid-air.
    ///
    /// <para>
    /// The bug this behaviour was written for is recorded at length in the
    /// function it came from: the move type was set unconditionally and only
    /// the push was guarded, so a gib with no physics object claimed
    /// MOVETYPE_VPHYSICS with nothing to move it — and MOVETYPE_VPHYSICS has an
    /// empty case in PhysicsSimulate, because the solver is supposed to be
    /// doing the moving. The gib hung in the air for its whole life.
    /// </para>
    /// </summary>
    [Fact]
    public void APhysicsGibTheSolverRefusesFallsUnderGamePhysicsInstead()
    {
        var (gib, engine) = Spawn();
        engine.Returns("EntityPhysics.InitPhysics", false);

        gib.Throw(Shot(GibSimulation.Physics), invisible: false);

        Assert.False(gib.SolverOwned);

        // It was still THROWN: the velocity went on, and the angular velocity a
        // tumbling chunk needs went on with it.
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetVelocity");
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetAngularVelocity");
    }

    /// <summary>
    /// Zero gravity scale means ONE, which is the engine's rule and not a guess:
    /// <c>CBaseEntity::PhysicsAddGravityMove</c> reads <c>if ( GetGravity() )
    /// ent_gravity = GetGravity(); else ent_gravity = 1.0;</c>.
    ///
    /// <para>
    /// It matters more here than it did in C++. The managed integrator reads a
    /// zero scale as <see cref="MotionKind.Fly"/> — no gravity at all — so a
    /// gib whose shooter left <c>gravityscale</c> unset would sail off in a
    /// straight line for ever instead of falling.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnsetGravityScaleFallsNormallyRatherThanFlyingFlat()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(gravityScale: 0f), invisible: false);

        var call = Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.SetGravity");
        Assert.Equal(1f, call.Args[1]);
    }

    /// <summary>And a scale the shooter DID set is the one that is used.</summary>
    [Fact]
    public void AGravityScaleTheShooterSetIsTheOneUsed()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(gravityScale: 0.25f), invisible: false);

        var call = Assert.Single(engine.Calls, c => c.Member == "EntityPhysics.SetGravity");
        Assert.Equal(0.25f, call.Args[1]);
    }

    /// <summary>
    /// A model scale of zero means one. <c>SourceSharp_ShootGib</c> made the
    /// same substitution, and a gib scaled to nothing is invisible rather than
    /// small.
    /// </summary>
    [Fact]
    public void AZeroModelScaleIsOne()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(scale: 0f), invisible: false);

        var call = Assert.Single(engine.Calls, c => c.Member == "EntityRender.SetModelScale");
        Assert.Equal(1f, call.Args[1]);
    }

    //-------------------------------------------------------------------------
    // What it does when it lands.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A bouncing gib leaves a blood decal, and only five of them —
    /// <c>CGib::m_cBloodDecals = 5</c>, "1 per bounce until none remain".
    /// </summary>
    [Fact]
    public void ABouncingGibSpendsFiveDecalsAndThenStops()
    {
        var (gib, engine) = Spawn();
        gib.Throw(Shot(blood: BloodColor.Red), invisible: false);

        for (int i = 0; i < SsGib.BloodDecalsPerGib + 3; i++)
            gib.OnTouch(EntityHandle.None);

        Assert.Equal(0, gib.DecalsLeft);
        Assert.Equal(SsGib.BloodDecalsPerGib,
                     engine.Calls.Count(c => c.Member == "Effects.BloodDecal"));
    }

    /// <summary>
    /// A gib told DONT_BLEED leaves nothing at all, and that is the value the
    /// blood-colour fix turns on: <see cref="BloodColor.None"/> is -1, not the 0
    /// a zeroed field reads as.
    /// </summary>
    [Fact]
    public void AGibToldNotToBleedLeavesNoDecal()
    {
        var (gib, engine) = Spawn();
        gib.Throw(Shot(blood: BloodColor.None), invisible: false);

        gib.OnTouch(EntityHandle.None);

        Assert.Equal(BloodColor.None, gib.Bleeds);
        Assert.Equal(SsGib.BloodDecalsPerGib, gib.DecalsLeft);
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Effects.BloodDecal");
    }

    /// <summary>
    /// A SOLVER-simulated gib leaves nothing and makes no noise, and that is
    /// stock's rule rather than a gap: <c>CGib::BounceGibTouch</c> opens with
    /// <c>if ( VPhysicsGetObject() ) return;</c>.
    /// </summary>
    [Fact]
    public void ASolverSimulatedGibNeitherBleedsNorSounds()
    {
        var (gib, engine) = Spawn();
        engine.Returns("EntityPhysics.InitPhysics", true);

        gib.Throw(Shot(GibSimulation.Physics, material: SurfaceMaterial.Wood), invisible: false);
        gib.OnTouch(EntityHandle.None);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Effects.BloodDecal");
        Assert.DoesNotContain(engine.Calls, c => c.Member == "Sound.EntityEmitSound");
    }

    //-------------------------------------------------------------------------
    // The impact sound, and the bug it was silently not making.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The material a shooter chose reaches the sound, which it did NOT before.
    ///
    /// <para>
    /// <c>GibShot.Material</c> carries a <see cref="SurfaceMaterial"/> — the
    /// ASCII letters the surfaceprop tables use, 'W' for wood.
    /// <c>SourceSharp_ShootGib</c> assigned that number straight into
    /// <c>CGib::m_material</c>, which is a <c>Materials</c> (func_break.h:
    /// matGlass = 0, matWood = 1, … matNone = 10), and
    /// <c>CBreakable::MaterialSound</c> was then asked for a sound for material
    /// 87. Its switch has no case for 87 and returns NULL, so the impact sound
    /// was dead for every gib in the game — and <c>m_material != matNone</c> was
    /// TRUE for 87, so the branch was entered every time and did nothing.
    /// </para>
    ///
    /// <para>
    /// Only symptom: a sound that does not play. Nothing could have found it
    /// except reading both ends, which is why the fact is pinned at both ends.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMaterialAShooterChoseNamesTheSoundItMakes()
    {
        Assert.Equal("Breakable.MatWood",  SsGib.MaterialSound(SurfaceMaterial.Wood));
        Assert.Equal("Breakable.MatGlass", SsGib.MaterialSound(SurfaceMaterial.Glass));
        Assert.Equal("Breakable.MatMetal", SsGib.MaterialSound(SurfaceMaterial.Metal));
        Assert.Equal("Breakable.MatFlesh", SsGib.MaterialSound(SurfaceMaterial.Flesh));
        Assert.Equal("Breakable.MatConcrete", SsGib.MaterialSound(SurfaceMaterial.Rocks));
        Assert.Equal("Breakable.Computer", SsGib.MaterialSound(SurfaceMaterial.Computer));
    }

    /// <summary>
    /// And the five an env_shooter can actually be set to all name a sound. The
    /// other direction of the same fact: a mapping that answered null for
    /// everything would satisfy the test above's absence half and nothing else.
    /// </summary>
    [Fact]
    public void EveryMaterialAnEnvShooterOffersMakesASound()
    {
        for (int shootSounds = 0; shootSounds <= 4; shootSounds++)
        {
            SurfaceMaterial material = EnvShooter.MaterialFor(shootSounds);

            Assert.True(SsGib.MaterialSound(material) != null,
                $"env_shooter shootsounds {shootSounds} ({material}) makes no sound. "
                + "That was true of every value for the whole life of the ported "
                + "shooter, because the letter was read as a func_break.h index.");
        }
    }

    /// <summary>Silence is a value and it makes no sound.</summary>
    [Fact]
    public void SilentMakesNoSound()
    {
        Assert.Null(SsGib.MaterialSound(SurfaceMaterial.Silent));

        var (gib, engine) = Spawn();
        gib.Throw(Shot(material: SurfaceMaterial.Silent), invisible: false);
        gib.OnTouch(EntityHandle.None);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Sound.EntityEmitSound");
    }

    //-------------------------------------------------------------------------
    // How it goes.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every gib arms its own removal. Stock leans on
    /// <c>CGib::WaitTillLand</c> → <c>SUB_FadeOut</c> for the ones that are not
    /// strict-remove; this hierarchy has no fade-out think, so a gib that armed
    /// nothing would live until the map changed.
    /// </summary>
    [Fact]
    public void AGibArmsItsOwnRemoval()
    {
        var (gib, _) = Spawn();

        gib.Throw(Shot(lifetime: 12f), invisible: false);

        Assert.True(gib.ThinkArmed > 0f, "no removal was armed");
        Assert.False(gib.Gone);

        gib.OnThink();
        Assert.True(gib.Gone);
    }

    /// <summary>
    /// The lifetime is jittered by ±5% from the ENGINE's random stream. A row of
    /// gibs that all vanished on the same frame reads as a bug rather than as
    /// debris.
    /// </summary>
    [Fact]
    public void TheLifetimeIsJitteredRatherThanExact()
    {
        var (gib, engine) = Spawn();
        engine.Returns("Host.RandomFloat", 0.95f);

        gib.Throw(Shot(lifetime: 20f), invisible: false);

        Assert.Equal(19f, gib.ThinkArmed, 3);
    }

    /// <summary>
    /// A sprite <c>shootmodel</c> throws an INVISIBLE gib rather than one the
    /// client refuses to draw.
    ///
    /// <para>
    /// A gib is drawn through the studio path, so a sprite model reached
    /// <c>case mod_sprite:</c> and the client said "Sprite model's not supported
    /// any more except in legacy temp ents" once per gib in the volley. Stock
    /// spawns the gib with an empty model for the same reason and hangs a real
    /// sprite off it; only the first half is here, and it is a gap rather than a
    /// bug — the gib is invisible on purpose, the way stock's is.
    /// </para>
    /// </summary>
    [Fact]
    public void ASpriteShootmodelThrowsAnInvisibleGib()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(), invisible: true);

        var call = Assert.Single(engine.Calls, c => c.Member == "EntityRender.SetVisible");
        Assert.Equal(false, call.Args[1]);
    }

    /// <summary>...and an ordinary model is not hidden.</summary>
    [Fact]
    public void AStudioShootmodelIsNotHidden()
    {
        var (gib, engine) = Spawn();

        gib.Throw(Shot(), invisible: false);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "EntityRender.SetVisible");
    }
}
