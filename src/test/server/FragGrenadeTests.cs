using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// ss_grenade as stock's CGrenadeFrag (grenade_frag.cpp) on CBaseGrenade's
/// detonation (basegrenade_shared.cpp): it blips as it spawns and every
/// second after, every 0.3 s in its last 1.5, and when its timer is up it
/// goes off -- a fireball temp entity turned to the surface below, a blast
/// credited to the thrower, and BaseGrenade.Explode.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25):
/// stock's server played Grenade.Blip and BaseGrenade.Explode and this mod
/// neither, and its grenade blocked players.
/// </para>
/// </summary>
public class FragGrenadeTests
{
    private static readonly EntityHandle Thrown = new(0x0004_0901u);
    private static readonly EntityHandle Thrower = new(0x0001_0001u);
    private static readonly Vector Floor = new(100, 200, 0);

    private sealed class Explosions : TempEntsDouble
    {
        public readonly List<(Vector At, int Radius, int Magnitude, float Scale, Vector? Normal, byte Material)> Made = [];
        public readonly List<int> Models = [];

        public override void Explosion(TeFilter filter, float delay, Vector position, int modelIndex, float scale, int frameRate,
                                       int flags, int radius, int magnitude, Vector? normal = null, byte materialType = (byte)'C')
        {
            Made.Add((position, radius, magnitude, scale, normal, materialType));
            Models.Add(modelIndex);
        }
    }

    private sealed record Rig(ScriptedEntityRegistry Registry, RecordingEngine Engine, Explosions Temps,
                              RecordingBlastSweep Blast, Func<float> Clock, Action<float> SetClock);

    private static Rig Spawned()
    {
        float now = 10f;
        uint made = 0x0004_0a00u;
        var temps = new Explosions();
        var blast = new RecordingBlastSweep();
        var services = Services(out var engine);
        services.AddInstance<ITempEnts>(temps);
        services.AddInstance<IBlastSweep>(blast);
        engine.Strict = false;
        engine.ReturnsEachCall("Clock.Now", () => now)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetOrigin", Floor + new Vector(0, 0, 4))
              .Returns("WorldQuery.TraceLineSurface",
                       new SurfaceTraceResult(true, 0.5f, Floor, new Vector(0, 0, 1), EntityHandle.None, 7, 0))
              .Returns("WorldQuery.SurfacePropGameMaterial", SurfaceMaterial.Metal)
              .Returns("Entities.LookupAttachmentAny", 2)
              .ReturnsEachCall("Entities.Create", () => new EntityHandle(++made));

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(Grenade).Assembly);
        registry.Services = services;
        registry.Spawn(Thrown, EntityNames.SsGrenade);
        return new Rig(registry, engine, temps, blast, () => now, t => now = t);
    }

    private static Grenade Of(Rig rig) => (Grenade)rig.Registry.Get(Thrown)!;

    /// <summary>
    /// Thinks every eighth of a second -- near DelayThink's 0.1 and exact in
    /// binary, so no time lands a hair either side of a deadline.
    /// </summary>
    private static void ThinkUntil(Rig rig, float until)
    {
        for (float t = rig.Clock() + 0.125f; t <= until; t += 0.125f)
        {
            rig.SetClock(t);
            rig.Registry.Think(Thrown);
        }
    }

    private static int Blips(RecordingEngine engine)
        => engine.Calls.Count(c => c.Member == "Sound.EntityEmitSound" && (string)c.Args[1]! == "Grenade.Blip");

    /// <summary>The entity a Create for <paramref name="className"/> made, in creation order.</summary>
    private static EntityHandle Made(RecordingEngine engine, string className)
    {
        int nth = 0;
        foreach (var call in engine.Calls.Where(c => c.Member == "Entities.Create"))
        {
            nth++;
            if ((string)call.Args[0]! == className)
                return new EntityHandle(0x0004_0a00u + (uint)nth);
        }
        return EntityHandle.None;
    }

    private static string? Key(RecordingEngine engine, EntityHandle of, string key)
        => engine.Calls.LastOrDefault(c => c.Member == "Entities.SetKeyValue" && (EntityHandle)c.Args[0]! == of
                                           && (string)c.Args[1]! == key).Args?[2] as string;

    [Fact]
    public void ARedGlowFollowsItsFuse()
    {
        var engine = Spawned().Engine;
        EntityHandle glow = Made(engine, EntityNames.EnvSprite);

        Assert.True(glow != EntityHandle.None, "no glow was made");
        Assert.Equal(("sprites/redglow1.vmt", "0.2", "3", "200", "14"),
                     (Key(engine, glow, "model"), Key(engine, glow, "scale"), Key(engine, glow, "rendermode"),
                      Key(engine, glow, "renderamt"), Key(engine, glow, "renderfx")));
        var parent = engine.Calls.Last(c => c.Member == "Entities.SetParent" && (EntityHandle)c.Args[0]! == glow);
        Assert.Equal((Thrown, 2), ((EntityHandle)parent.Args[1]!, (int)parent.Args[2]!));
    }

    [Fact]
    public void ARedTrailFollowsItsFuse()
    {
        var engine = Spawned().Engine;
        EntityHandle trail = Made(engine, EntityNames.EnvSpritetrail);

        Assert.True(trail != EntityHandle.None, "no trail was made");
        Assert.Equal(("sprites/bluelaser1.vmt", "255 0 0", "8", "1", "0.5"),
                     (Key(engine, trail, "spritename"), Key(engine, trail, "rendercolor"), Key(engine, trail, "startwidth"),
                      Key(engine, trail, "endwidth"), Key(engine, trail, "lifetime")));
        Assert.Contains(engine.Calls, c => c.Member == "Entities.SetParent" && (EntityHandle)c.Args[0]! == trail
                                           && (EntityHandle)c.Args[1]! == Thrown);
    }

    [Fact]
    public void ASecondTimerKeepsTheEffectsItHas()
    {
        var rig = Spawned();
        Of(rig).ThrownBy(Thrower, 2.5f);

        Assert.Equal(2, rig.Engine.Calls.Count(c => c.Member == "Entities.Create"));
    }

    [Fact]
    public void TheEffectsGoWithIt()
    {
        var rig = Spawned();
        EntityHandle glow = Made(rig.Engine, EntityNames.EnvSprite), trail = Made(rig.Engine, EntityNames.EnvSpritetrail);

        Of(rig).OnRemoved();

        var removed = rig.Engine.Calls.Where(c => c.Member == "Entities.Remove").Select(c => (EntityHandle)c.Args[0]!).ToList();
        Assert.Contains(glow, removed);
        Assert.Contains(trail, removed);
    }

    [Fact]
    public void ItBlipsAsItSpawns()
    {
        Assert.Equal(1, Blips(Spawned().Engine));
    }

    [Fact]
    public void PlayersPassThroughIt()
    {
        var rig = Spawned();

        Assert.Equal(CollisionGroup.Weapon, (CollisionGroup)rig.Engine.Last("EntityPhysics.SetCollisionGroup")!.Value.Args[1]!);
        Assert.Equal(SolidFlags.NotStandable, (SolidFlags)rig.Engine.Last("Entities.SetSolidity")!.Value.Args[2]!);
    }

    [Fact]
    public void ItBlipsEverySecondThenFasterForItsLastSecondAndAHalf()
    {
        var rig = Spawned();
        Of(rig).ThrownBy(Thrower, 2.5f);

        ThinkUntil(rig, 11.0f);
        Assert.Equal(1, Blips(rig.Engine));    // the spawn's; the next is due after 11

        ThinkUntil(rig, 12.5f);
        // 11.125, then at least 0.3 s apart: 11.5, 11.875, 12.25.
        Assert.Equal(5, Blips(rig.Engine));
    }

    [Fact]
    public void ItGoesOffWhenItsTimeIsUpAndNotBefore()
    {
        var rig = Spawned();
        Of(rig).ThrownBy(Thrower, 2.5f);

        ThinkUntil(rig, 12.5f);
        Assert.Empty(rig.Blast.Requests);

        ThinkUntil(rig, 12.625f);
        var blast = Assert.Single(rig.Blast.Requests);
        Assert.Equal((150f, 250f, DamageType.Blast), (blast.Magnitude, blast.Radius, blast.DamageType));
        Assert.Equal((Thrower, Thrown), (blast.Attacker, blast.Inflictor));
        Assert.Contains(rig.Engine.Calls, c => c.Member == "Sound.EntityEmitSound" && (string)c.Args[1]! == "BaseGrenade.Explode");
        Assert.Equal(1, rig.Registry.Directory.PendingRemovalCount);
    }

    // CBaseGrenade::Explode: !( contents & MASK_WATER ) ? g_sModelIndexFireball
    // : g_sModelIndexWExplosion, and HL2MP never precaches the second, so 0.
    [Theory]
    [InlineData(Contents.Empty, 37)]
    [InlineData(Contents.Water, 0)]
    [InlineData(Contents.Slime, 0)]
    [InlineData(Contents.Moveable, 0)]
    public void InWaterTheExplosionHasNoSpriteAndInAirTheFireball(Contents contents, int model)
    {
        var rig = Spawned();
        rig.Engine.Returns("WorldQuery.PointContents", contents)
                  .Returns("Precache.PrecacheModel", 37);
        Of(rig).ThrownBy(Thrower, 2.5f);

        ThinkUntil(rig, 12.625f);

        Assert.Equal(model, Assert.Single(rig.Temps.Models));
    }

    [Fact]
    public void TheFireballIsTurnedToTheSurfaceBelowInItsMaterial()
    {
        var rig = Spawned();
        Of(rig).ThrownBy(Thrower, 2.5f);

        ThinkUntil(rig, 12.625f);

        var made = Assert.Single(rig.Temps.Made);
        Assert.Equal(Floor + new Vector(0, 0, 0.6f), made.At);
        Assert.Equal((250, 150, 7.5f), (made.Radius, made.Magnitude, made.Scale));
        Assert.Equal(new Vector(0, 0, 1), made.Normal);
        Assert.Equal((byte)'M', made.Material);
    }
}
