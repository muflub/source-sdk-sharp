using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Every managed explosion names the entity it came out of.
///
/// <para>
/// This is the falloff, said at the only place it can be said. The traversal
/// works out a victim's damage from the distance between the blast and where the
/// trace TO that victim ended — and the trace is told to pass through the
/// CTakeDamageInfo's inflictor, which is how stock's CMissile and
/// CBreakableProp keep a blast from being stopped by the thing that produced
/// it. An entity on the parallel hierarchy cannot BE an inflictor
/// (m_hInflictor is an EHANDLE and holds a CBaseEntity or nothing), so the same
/// fact travels as the explosion's "ignored" handle instead.
/// </para>
///
/// <para>
/// Left unsaid, the first trace starts inside the exploding entity's own hull,
/// stops at fraction zero, and every victim in the radius is scored at distance
/// ZERO — full magnitude however far away. Measured live before the fix: one
/// explosive barrel on dm_lockdown, magnitude 120 and radius 256, dealt its
/// whole 120 to a player 252 units away, where the falloff gives 2.8.
/// </para>
///
/// <para>
/// So it is a rule about every caller rather than about one of them, and every
/// caller is asserted.
/// </para>
/// </summary>
public class ExplosionSelfIgnoreTests
{
    /// <summary>
    /// ExplosionDamage(origin, magnitude, radius, damageType, attacker, inflictor,
    /// ignored, damageForce).
    ///
    /// <para>
    /// This used to read Damage.Explosion, the one call that built a C++
    /// env_explosion. That entity is EnvExplosion.cs now, so the assertion moved
    /// DOWN a layer rather than being weakened: it observes the damage traversal
    /// the managed entity actually asks for, past the entity it now goes
    /// through. Magnitude, radius and the ignored handle happen to sit at the
    /// same argument positions as before.
    /// </para>
    /// </summary>
    /// <summary>
    /// The one blast this entity asked for.
    ///
    /// <para>
    /// It was <c>engine.Calls.Where( c =&gt; c.Member == "Damage.ExplosionDamage" )</c>
    /// until the shim wave's <c>policy-rules</c> lane moved the traversal into
    /// managed code. There is no ABI call to count any more — the falloff and
    /// the line of sight run in C# — so the REQUEST is recorded instead, by
    /// <see cref="RecordingBlastSweep"/>, and every fact below reads the same
    /// arguments off it. Positional <c>ArgAs</c> indices become named fields,
    /// which is the one thing about these facts that got better in the move.
    /// </para>
    /// </summary>
    private static RecordingBlastSweep.Request TheOnlyExplosion(ServiceContainer services)
    {
        var blast = (RecordingBlastSweep)services.Resolve(typeof(IBlastSweep))!;

        Assert.True(blast.Requests.Count == 1, "exactly one explosion was asked for");
        return blast.Requests[0];
    }

    /// <summary>
    /// What an explosion asks the engine for, now that making one is a call
    /// rather than an entity.
    ///
    /// <para>
    /// Stock built an env_explosion per bang and threw it away three tenths of
    /// a second later. The managed port shares the body instead, so a grenade
    /// creates nothing — which is why there is no entity to arrange here, only
    /// the four engine answers the blast reads.
    /// </para>
    /// </summary>
    private static void AnExplosionCanBeMade(RecordingEngine engine)
    {
        engine.Returns("Precache.PrecacheModel", 1)
              // Nothing underfoot: fraction 1 means the downward trace found no
              // ground, so the fireball is not nudged and the blast goes off
              // exactly where the entity stood.
              .Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              // Dry land. Stock only throws sparks when the blast went off IN
              // water, which reads backwards and is stock's own inversion.
              .Returns("WorldQuery.PointContents", Contents.Empty)
              .Ignores("Effects.PaintDecal")
              .Ignores("Effects.ExplosionEffect");
    }

    private static (ScriptedEntity entity, RecordingEngine engine, ServiceContainer services)
        Spawned(string className, params string[] alsoIgnore)
    {
        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");

        engine.Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f))
              .Ignores("Entities.SetModel")
              .Ignores("EntityPhysics.SetGravity")
              .Ignores("Entities.Remove");

        foreach (var member in alsoIgnore)
            engine.Ignores(member);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Rocket).Assembly);
        registry.Services = services;

        AnExplosionCanBeMade(engine);

        var handle = new EntityHandle(11);
        registry.Spawn(handle, className);

        return (registry.Get(handle)!, engine, services);
    }

    /// <summary>
    /// What the projectile ran into: not a volume, so it stops on it. A distinct
    /// handle from the shooter, or OnTouch's first guard swallows it.
    /// </summary>
    private static EntityHandle AWall(RecordingEngine engine)
    {
        engine.Returns("EntityPhysics.GetSolidFlags", SolidFlags.None)
              .Returns("EntityPhysics.GetCollisionGroup", CollisionGroup.None);

        return new EntityHandle(7);
    }

    [Fact]
    public void ARocketNamesItself()
    {
        var (entity, engine, services) = Spawned(EntityNames.SsRocket);

        entity.OnTouch(AWall(engine));

        Assert.True(TheOnlyExplosion(services).Ignored == entity.Handle,
                    "a rocket's blast is not stopped by the rocket");
    }

    /// <summary>
    /// The rocket's own numbers, against stock's.
    ///
    /// <para>
    /// The radius was 300 here where stock's <c>CMissile::EXPLOSION_RADIUS</c> is
    /// 200, and it is not a free fifty per cent: the traversal's falloff is
    /// magnitude/radius per unit, so a wider blast is a HARDER one at every
    /// distance as well as a longer one. At 100 units 300 gave 133 where 200
    /// gives 100, and at 250 it still gave 33 where stock had already stopped.
    /// On a map with explosive barrels the extra hundred units is what reaches
    /// the next barrel, and the chain comes back to whoever fired.
    /// </para>
    /// </summary>
    /// <summary>
    /// The thing that exploded is the blast's inflictor -- stock's
    /// CTakeDamageInfo( this, GetOwnerEntity(), ... ) -- so a hit is felt FROM
    /// it, while the owner keeps the credit (lane player-msgs).
    /// </summary>
    [Theory]
    [InlineData(EntityNames.SsRocket)]
    [InlineData(EntityNames.SsEnergyball)]
    public void A_projectile_is_its_blasts_inflictor(string className)
    {
        var (entity, engine, services) = Spawned(className, "Damage.Apply");

        entity.OnTouch(AWall(engine));

        Assert.Equal(entity.Handle, TheOnlyExplosion(services).Inflictor);
    }

    [Fact]
    public void ARocketCarriesStocksNumbers()
    {
        var (entity, engine, services) = Spawned(EntityNames.SsRocket);

        entity.OnTouch(AWall(engine));

        var bang = TheOnlyExplosion(services);

        Assert.True(bang.Magnitude == 200f,
                    "stock's CMissile::Spawn calls SetDamage( 200.0f )");

        Assert.True(bang.Radius == 200f,
                    "stock's CMissile::EXPLOSION_RADIUS is 200");
    }

    [Fact]
    public void AnEnergyBallNamesItself()
    {
        var (entity, engine, services) = Spawned(EntityNames.SsEnergyball, "Damage.Apply");

        entity.OnTouch(AWall(engine));

        Assert.True(TheOnlyExplosion(services).Ignored == entity.Handle,
                    "an energy ball's blast is not stopped by the ball");
    }

    [Fact]
    public void ABreakingPropNamesItself()
    {
        var services = Services(out var engine);
        engine.Ignores("Entities.IsValid");

        engine.Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f))
              // An explosive barrel. The numbers are the MODEL's, read through
              // prop data rather than written in the map, which is why they are
              // stubbed here — a barrel is not given a health key by any map.
              .Returns("EntityRender.GetModelName", "models/props_c17/oildrum001_explosive.mdl")
              .Returns("EntityRender.GetModelKeyValueText",
                       "mdlkeyvalue { prop_data { health 30 explosive_damage 120 explosive_radius 256 } }")
              .Ignores("Entities.SetModel")
              .Ignores("Entities.Remove")
              .Ignores("Entities.SetNextThink");

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = services;

        AnExplosionCanBeMade(engine);

        var handle = new EntityHandle(12);
        registry.Spawn(handle, EntityNames.PropPhysics);

        registry.Input(handle, "Break", "", 0f, EntityHandle.None);

        var bang = TheOnlyExplosion(services);

        Assert.True(bang.Magnitude == 120f && bang.Radius == 256f,
                    "the barrel bangs with the model's own magnitude and radius");

        Assert.True(bang.Ignored == handle,
                    "a breaking prop's blast is not stopped by the prop");
    }
}
