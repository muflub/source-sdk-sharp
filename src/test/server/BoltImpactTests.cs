using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a bolt does when it lands, which is the other half of the weapon.
///
/// <para>
/// <c>CCrossbowBolt::BoltTouch</c> (game/server/hl2/crossbow.cpp:195) does
/// more than hurt what it hits: it says so out loud and leaves the bolt
/// visibly stuck. The body branch emits <c>Weapon_Crossbow.BoltHitBody</c> and
/// sticks the bolt behind the victim with a <c>"BoltImpact"</c> tempent at the
/// trace's end; the world branch emits <c>Weapon_Crossbow.BoltHitWorld</c> and
/// sticks the bolt in the surface with <c>"BoltImpact"</c> plus
/// <c>UTIL_ImpactTrace</c>'s <c>"Impact"</c>. A surface carrying the sky flag
/// gets neither, which is why a bolt shot into the sky makes no stick.
/// </para>
///
/// <para>
/// The client's <c>CrossbowBoltEffect</c> has been registered for that tempent
/// since it landed; the server never sent it, so the stuck bolt nobody saw
/// was paid for by an effect that was built and never used. These facts pin
/// the dispatch at the seam where it is observable: the fake
/// <see cref="ITempEnts"/> records what the game sends, and the engine double
/// logs the sounds, traces and impact effect it is asked for. The mover is
/// not involved - <c>OnTouch</c> is called directly, exactly as the mover's
/// stop calls it in play, and the base class's removal is what stock's
/// <c>SetTouch(NULL)</c> plus remove became.
/// </para>
/// </summary>
public class BoltImpactTests
{
    private const uint Owner = 3;

    /// <summary>Where the bolt came to rest: flush on a surface.</summary>
    private static readonly Vector Resting = new(64f, -32f, 50f);

    /// <summary>Fired due east, so "forward" is (1,0,0) without rounding.</summary>
    private static readonly Vector FiredVelocity = new(1500f, 0f, 0f);

    private static EntityHandle TheShooter => new(Owner);

    /// <summary>
    /// The bolt, spawned and fired the way the weapon does it, with the world
    /// answering what the impact will ask: the opaque line behind a victim,
    /// and the surface re-trace across the face it stopped on.
    /// </summary>
    private static (Bolt Bolt, RecordingEngine Engine, RecordingEffectDispatch Temps) ABolt(
        TraceResult? opaqueLine = null,
        SurfaceTraceResult? face = null,
        MoveType opaqueVictimMoveType = MoveType.None)
    {
        var services = Services(out var engine);

        engine.Ignores("Entities.SetModel")
              .Ignores("EntityPhysics.SetGravity")
              .Ignores("Entities.Remove")
              .Ignores("Entities.SetOwner")
              .Ignores("Entities.SetAngles")
              .Ignores("Entities.SetVelocity")
              // What the touch itself asks (is this a volume to fly through)
              // and who to credit: nothing this file is about.
              .Ignores("EntityPhysics.GetSolidFlags")
              .Ignores("EntityPhysics.GetCollisionGroup")
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", Resting)
              .Returns("Entities.GetAngles", Vector.Zero);

        // The default answers, for the branch a test is not exercising: the
        // opaque line reaches its full length (no stick), and the re-trace
        // misses (stock's absent surface, which takes the sky guard's silent
        // answer). A test overrides the one its name is about.
        engine.Returns("WorldQuery.TraceLine", new TraceResult(true, 1f, Vector.Zero, Vector.Zero, EntityHandle.None))
              .Returns("WorldQuery.TraceLineSurface", new SurfaceTraceResult(false, 1f, Vector.Zero, Vector.Zero,
                                                                              EntityHandle.None, -1, 0));
        if (opaqueLine is not null)
            engine.Returns("WorldQuery.TraceLine", opaqueLine.Value);
        if (face is not null)
            engine.Returns("WorldQuery.TraceLineSurface", face.Value);
        // The movetype is the half of stock's condition under test; the
        // state flags ride along unread.
        engine.Returns("Networking.EntityGetState", EntityState.None)
              .Returns("Networking.EntityGetState.moveType", opaqueVictimMoveType);

        var temps = (RecordingEffectDispatch)services.Resolve(typeof(ITempEnts))!;

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(Bolt).Assembly);
        registry.Services = services;

        var handle = new EntityHandle(1);
        registry.Spawn(handle, EntityNames.SsBolt);
        var bolt = (Bolt)registry.Get(handle)!;
        bolt.FiredBy(TheShooter, FiredVelocity);

        return (bolt, engine, temps);
    }

    /// <summary>The opaque line stopping 64 units out: something behind the victim.</summary>
    private static TraceResult StoppedBehind => new(true, 0.5f, Resting + new Vector(64f, 0f, 0f),
                                                    new Vector(-1f, 0f, 0f), EntityHandle.None);

    /// <summary>The world branch's re-trace: the face is a tiled wall, still there.</summary>
    private static SurfaceTraceResult WallFace => new(true, 0.6667f, Resting, new Vector(-1f, 0f, 0f),
                                                      EntityHandle.None, 17, 0);

    /// <summary>The same face with SURF_SKY on it: the skybox.</summary>
    private static SurfaceTraceResult SkyFace => new(true, 0.6667f, Resting, new Vector(-1f, 0f, 0f),
                                                     EntityHandle.None, 17, SurfSky);

    /// <summary>bspflags.h, the one surface bit stock's world branch tests.</summary>
    private const int SurfSky = 0x0004;

    private static readonly EntityHandle Victim = new(7);

    [Fact]
    public void ABoltInAnEntitySaysSoAndSticksBehindIt()
    {
        // Stock's body branch: the hit-body cue, then the MASK_BLOCKLOS line
        // along forward*128 ignoring the victim, which stops - in brushwork
        // (no entity the engine can name), the only thing that holds a bolt -
        // so "BoltImpact" goes out at the trace's end with the forward
        // direction as its normal and the stuck flag set.
        var (bolt, engine, temps) = ABolt(opaqueLine: StoppedBehind);

        bolt.OnTouch(Victim);

        var sound = Assert.Single(engine.AllOf("Sound.EntityEmitSound"));
        Assert.Equal("Weapon_Crossbow.BoltHitBody", sound.ArgAs<string>(1));
        Assert.True(sound.ArgAs<EntityHandle>(0) == bolt.Handle,
                    "the cue is attached to the bolt, as stock's EmitSound on self is");
        var line = Assert.Single(engine.AllOf("WorldQuery.TraceLine"));
        Assert.True(line.ArgAs<TraceMask>(2) == TraceMask.BlockLos,
                    "MASK_BLOCKLOS, what the server's BoltTouch traces with");
        Assert.True(line.ArgAs<EntityHandle>(3) == Victim, "ignoring the victim");
        var stuck = Assert.Single(temps.Effects);
        Assert.Equal("BoltImpact", stuck.Name);
        Assert.Equal(TeFilter.Pvs(StoppedBehind.End), stuck.Filter);
        Assert.Equal(0f, stuck.Delay);
        Assert.Equal(StoppedBehind.End, stuck.Data.Origin);
        Assert.True(stuck.Data.Normal.Equals(new Vector(1f, 0f, 0f)),
                    $"stock's normal is forward, not velocity: {stuck.Data.Normal}");
        Assert.True(stuck.Data.EntIndex == 1, "the fraction != 1 flag, inside the guard this dispatch sits in");
    }

    [Fact]
    public void ABoltWithNothingBehindTheVictimMakesNoStick()
    {
        // The negative arm of the same line: the victim stood in the open and
        // the opaque trace ran its full 128 units, so there is no surface to
        // stick the bolt in and stock dispatches nothing. The sound still
        // plays - it is outside the guard.
        var (bolt, engine, temps) = ABolt(opaqueLine: new TraceResult(true, 1f, Vector.Zero, Vector.Zero, EntityHandle.None));

        bolt.OnTouch(Victim);

        Assert.Single(engine.AllOf("Sound.EntityEmitSound"));
        Assert.Empty(temps.Effects);
    }

    [Fact]
    public void ABoltInAMovingThingMakesNoStick()
    {
        // The other half of stock's condition - the line stops, but in a
        // MOVETYPE_WALK player rather than static architecture: you do not
        // stick a bolt in something that is walking around.
        var stoppedInPlayer = new TraceResult(true, 0.25f, Resting + new Vector(32f, 0f, 0f),
                                              new Vector(-1f, 0f, 0f), new EntityHandle(11));
        var (bolt, _, temps) = ABolt(opaqueLine: stoppedInPlayer, opaqueVictimMoveType: MoveType.Walk);

        bolt.OnTouch(Victim);

        Assert.Empty(temps.Effects);
    }

    [Fact]
    public void ABoltInAWallSticksWithBoltImpactAndTheImpactTrace()
    {
        var (bolt, engine, temps) = ABolt(face: WallFace);

        bolt.OnTouch(EntityHandle.None);

        var sound = Assert.Single(engine.AllOf("Sound.EntityEmitSound"));
        Assert.Equal("Weapon_Crossbow.BoltHitWorld", sound.ArgAs<string>(1));
        var stuck = Assert.Single(temps.Effects);
        Assert.Equal("BoltImpact", stuck.Name);
        Assert.Equal(TeFilter.Pvs(Resting), stuck.Filter);
        Assert.Equal(Resting, stuck.Data.Origin);
        Assert.True(stuck.Data.Normal.Equals(new Vector(1f, 0f, 0f)), $"normal: {stuck.Data.Normal}");
        Assert.Equal(0, stuck.Data.EntIndex);
        // UTIL_ImpactTrace's terminal dispatch — CBaseEntity::ImpactTrace's
        // "Impact" effect — is the managed port Arsenal's hitscan already
        // calls: ImpactEffect, with the backoff off the surface.
        var impact = Assert.Single(engine.AllOf("Effects.ImpactEffect"));
        Assert.Equal(Resting, impact.ArgAs<Vector>(1));
        Assert.Equal(Resting - new Vector(32f, 0f, 0f), impact.ArgAs<Vector>(0));
        Assert.Equal(17, impact.ArgAs<int>(2));
        Assert.Equal(DamageType.Bullet, impact.ArgAs<DamageType>(3));
        Assert.Equal(EntityHandle.None, impact.ArgAs<EntityHandle>(4));
    }

    [Fact]
    public void ABoltInTheSkyMakesNoStickAtAll()
    {
        // The SURF_SKY guard, stock's first condition on the world branch:
        // no sound, no BoltImpact, no impact trace. A bolt into the skybox is
        // removed without a word.
        var (bolt, engine, temps) = ABolt(face: SkyFace);

        bolt.OnTouch(EntityHandle.None);

        Assert.Empty(engine.AllOf("Sound.EntityEmitSound"));
        Assert.Empty(temps.Effects);
    }

    [Fact]
    public void ASurfaceThatVanishedUnderTheBoltIsSilent()
    {
        // The re-trace asking again what the bolt stopped on and finding
        // nothing - the breakable that the touch broke, say. Nothing can be
        // stuck in a face that is gone, so this is the sky guard's silent
        // answer rather than a dispatch at a position with no surface under
        // it.
        var (bolt, engine, temps) = ABolt(face: null);

        bolt.OnTouch(EntityHandle.None);

        Assert.Empty(engine.AllOf("Sound.EntityEmitSound"));
        Assert.Empty(temps.Effects);
    }

    [Fact]
    public void TheBoltStillHurtsWhatItHits()
    {
        // What was already true and must stay true through the dispatch work:
        // the hit hurts, credited to the shooter, whatever else is emitted.
        var (bolt, engine, _) = ABolt(opaqueLine: StoppedBehind);

        bolt.OnTouch(Victim);

        var damage = Assert.Single(engine.AllOf("Damage.Apply"));
        Assert.Equal(Victim, damage.ArgAs<EntityHandle>(0));
        Assert.Equal(TheShooter, damage.ArgAs<EntityHandle>(1));
    }
}
