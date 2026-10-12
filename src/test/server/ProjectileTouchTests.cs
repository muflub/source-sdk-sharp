using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a flying projectile goes off on, and what it flies through.
///
/// <para>
/// This exists because the rule was missing and the cost was not subtle. Stock
/// opens both <c>CMissile::MissileTouch</c> and <c>CCrossbowBolt::BoltTouch</c>
/// by refusing to detonate on a trigger; the port kept the touch and dropped the
/// refusal. dm_lockdown wraps its spawn points in trigger_multiple, so an RPG
/// fired from a spawn detonated twenty-two units in front of the shooter's own
/// eye — 159 damage at 40 units, plus whatever the blast then set off — which
/// read from inside the game as "the RPG kills whoever fires it".
/// </para>
/// </summary>
public class ProjectileTouchTests
{
    /// <summary>
    /// A rocket, spawned through the registry the way the game spawns one, with
    /// the engine double answering only what it needs.
    /// </summary>
    private static (Rocket rocket, RecordingEngine engine, RecordingBlastSweep blast) ARocket()
    {
        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");
        var blast = (RecordingBlastSweep)services.Resolve(typeof(IBlastSweep))!;

        engine.Returns("Entities.GetOrigin", new Vector(10f, 20f, 30f))
              // What a projectile does on the way past, and not what this is about.
              .Ignores("Entities.SetModel")
              .Ignores("EntityPhysics.SetGravity")
              .Ignores("Entities.Remove")
              // What the blast itself reads. Making an explosion stopped being
              // one call when env_explosion became C#: the body is shared with
              // the map's entity now and asks for these four directly. Fraction
              // 1 is no ground underfoot; Contents.Empty is dry land, which is
              // the branch stock throws NO sparks on.
              .Returns("Precache.PrecacheModel", 1)
              .Returns("Precache.PrecacheDecal", 2)
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Returns("WorldQuery.PointContents", Contents.Empty)
              .Ignores("Effects.PaintDecal")
              .Ignores("Effects.ExplosionEffect");

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(Rocket).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(1);
        registry.Spawn(handle, EntityNames.SsRocket);

        var rocket = (Rocket)registry.Get(handle)!;

        // Fired, because a rocket in play always has been -- and because the
        // first thing OnTouch does is compare the toucher with the shooter, so
        // an unfired rocket would answer "that is me" to a great many things.
        engine.Ignores("Entities.SetOwner").Ignores("Entities.SetAngles").Ignores("Entities.SetVelocity");
        rocket.FiredBy(TheShooter, new Vector(0f, 1500f, 0f));

        return (rocket, engine, blast);
    }

    /// <summary>Whoever fired it. Any handle that is not one of the touchers below.</summary>
    private static EntityHandle TheShooter => new(3);

    /// <summary>Something the rocket has run into, described only by its collision.</summary>
    private static EntityHandle AToucher(RecordingEngine engine, SolidFlags flags, CollisionGroup group)
        => AToucher(new EntityHandle(7), engine, flags, group);

    private static EntityHandle AToucher(EntityHandle handle, RecordingEngine engine,
                                         SolidFlags flags, CollisionGroup group)
    {
        engine.Returns("EntityPhysics.GetSolidFlags", flags)
              .Returns("EntityPhysics.GetCollisionGroup", group);

        return handle;
    }

    [Fact]
    public void ATriggerVolumeIsFlownThroughRatherThanDetonatedOn()
    {
        var (rocket, engine, blast) = ARocket();

        rocket.OnTouch(AToucher(engine, SolidFlags.Trigger, CollisionGroup.None));

        Assert.True(blast.Requests.Count == 0,
                    "a rocket does not go off inside a trigger volume");
    }

    [Fact]
    public void AVolumetricBrushIsAlsoFlownThrough()
    {
        var (rocket, engine, blast) = ARocket();

        // The other half of stock's test: FSOLID_VOLUME_CONTENTS, which is how
        // water and the like are spelt.
        rocket.OnTouch(AToucher(engine, SolidFlags.VolumeContents, CollisionGroup.None));

        Assert.True(blast.Requests.Count == 0,
                    "a volumetric brush is a volume to fly through as well");
    }

    [Fact]
    public void ADroppedWeaponIsStillHit()
    {
        var (rocket, engine, blast) = ARocket();

        // Stock's own exception, in its own words: "Don't touch triggers (but DO
        // hit weapons)". A weapon on the floor is a trigger, because that is how
        // it notices a foot.
        rocket.OnTouch(AToucher(engine, SolidFlags.Trigger, CollisionGroup.Weapon));

        Assert.True(blast.Requests.Count == 1,
                    "a weapon lying on the floor is a trigger the rocket still hits");
    }

    [Fact]
    public void ASolidEntityIsHit()
    {
        var (rocket, engine, blast) = ARocket();

        rocket.OnTouch(AToucher(engine, SolidFlags.None, CollisionGroup.None));

        Assert.True(blast.Requests.Count == 1,
                    "anything that is not a volume stops the rocket");
    }

    [Fact]
    public void AHandleThatResolvesToNothingIsHit()
    {
        var (rocket, engine, blast) = ARocket();

        // What a projectile that struck the world is handed where the shell had
        // no entity to name. It reports no flags, which is "not a volume", which
        // is stop -- the safe answer for anything a rocket cannot identify.
        rocket.OnTouch(AToucher(EntityHandle.None, engine, SolidFlags.None, CollisionGroup.None));

        Assert.True(blast.Requests.Count == 1,
                    "a rocket stops on anything it cannot identify");
    }

    [Fact]
    public void TheShooterIsNeverHit()
    {
        var (rocket, engine, blast) = ARocket();

        rocket.OnTouch(TheShooter);

        Assert.True(blast.Requests.Count == 0,
                    "a projectile never goes off on whoever fired it");
    }

    [Fact]
    public void OneTouchIsOneExplosion()
    {
        var (rocket, engine, blast) = ARocket();

        rocket.OnTouch(AToucher(engine, SolidFlags.None, CollisionGroup.None));
        rocket.OnTouch(AToucher(engine, SolidFlags.None, CollisionGroup.None));

        Assert.True(blast.Requests.Count == 1,
                    "a rocket that touches a wall and a floor in one tick explodes once");
    }
}
