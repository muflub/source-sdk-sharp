using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.TempEnts;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="BulletImpactEffect"/>, the "Impact" client effect: stock's
/// <c>ImpactCallback</c> (hl2/fx_hl2_impacts.cpp) over fx_impact.cpp's chain and
/// c_impact_effects.cpp's entry points, in the order shell_impact_effects.cpp's
/// <c>SourceSharp_ImpactCallback</c> ran them before lane M1-C moved it.
///
/// <para>
/// Every vector is distinct from every other, so a port that passed the
/// origin where stock passes the trace's end (or the start where it passes the
/// extended end) cannot agree with these by accident.
/// </para>
/// </summary>
public class BulletImpactEffectTests
{
    private static readonly Vector Start = new(0f, 0f, 0f);
    private static readonly Vector Origin = new(100f, 0f, 0f);
    private static readonly Vector TraceExt = new(108f, 0f, 0f);
    private static readonly Vector HitPoint = new(99f, 1f, 2f);
    private static readonly Vector Normal = new(0f, 1f, 0f);

    private const int ServerSurface = 13;
    private const int Decal = 42;

    private static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static bool Near(Vector a, Vector b) =>
        MathF.Abs(a.X - b.X) < 1e-4f && MathF.Abs(a.Y - b.Y) < 1e-4f && MathF.Abs(a.Z - b.Z) < 1e-4f;

    private static ShotTrace Hit(bool world = true, int hitbox = 0, int entity = 0, int flags = 0, int surface = 7,
                                 float fraction = 0.5f)
        => new(true, fraction, HitPoint, Normal, hitbox, entity, surface, flags, world);

    private static ShotTrace Miss => new(false, 1f, TraceExt, default, 0, -1, 0, 0, false);

    private sealed class FakeImpactEngine : IClientImpactEngine
    {
        public ShotTrace? Clip = Miss;
        public ShotTrace World = Hit();
        public (Vector, Vector) WorldLight = (new(0.25f, 0.5f, 1f), new(2f, 3f, 4f));
        public (Vector, Vector) PropLight = (new(1f, 1f, 1f), new(5f, 6f, 7f));

        public readonly List<(Vector Start, Vector End, int Entity)> Clips = [];
        public readonly List<(Vector Start, Vector End)> WorldTraces = [];
        public readonly List<(Vector Start, Vector End)> WorldLights = [];
        public readonly List<(Vector Start, Vector End, Vector Normal, int Prop)> PropLights = [];
        public readonly List<(int Flecks, int Dust)> Ledgers = [];
        public readonly List<(string Name, ImpactControlPoint[] Points, int Entity, float Scale, Vector Color)> Particles = [];

        public ShotTrace? ClipToEntity(Vector start, Vector end, int entityIndex)
        {
            Clips.Add((start, end, entityIndex));
            return Clip;
        }

        public ShotTrace TraceWorld(Vector start, Vector end)
        {
            WorldTraces.Add((start, end));
            return World;
        }

        public (Vector Diffuse, Vector Base) WorldLighting(Vector start, Vector end)
        {
            WorldLights.Add((start, end));
            return WorldLight;
        }

        public (Vector Diffuse, Vector Base) StaticPropLighting(Vector start, Vector end, Vector normal, int staticProp)
        {
            PropLights.Add((start, end, normal, staticProp));
            return PropLight;
        }

        public void Ledger(int flecks, int dust) => Ledgers.Add((flecks, dust));

        public bool ImpactParticle(string name, ReadOnlySpan<ImpactControlPoint> points, int entityIndex, float scale,
                                   Vector color)
        {
            Particles.Add((name, points.ToArray(), entityIndex, scale, color));
            return true;
        }
    }

    private sealed class FakeDecals : IClientDecals
    {
        public string? Translated;
        public int Index = Decal;
        public readonly List<(string Name, int Material)> Translations = [];
        public readonly List<string?> Lookups = [];

        public void Init() { }
        public void LevelInitPreEntity() { }
        public void Shutdown() { }

        public int IndexForName(string? name)
        {
            Lookups.Add(name);
            return Index;
        }

        public string? NameForIndex(int index) => null;

        public string? TranslateForGameMaterial(string decalName, int gameMaterial)
        {
            Translations.Add((decalName, gameMaterial));
            return Translated;
        }
    }

    private sealed class FakeSprays : IClientImpactSprays
    {
        public int Made = 5;
        public readonly List<(Vector End, Vector Normal, Vector Color, int Material, int Scale, bool NoFlecks, float Throttle)> Debris = [];
        public readonly List<(Vector Origin, Vector Normal, Vector Color, float Scale, float Throttle)> Dusts = [];

        int IClientImpactSprays.Debris(Vector endPos, Vector normal, Vector color, int material, int scale, bool noFlecks, float throttle)
        {
            Debris.Add((endPos, normal, color, material, scale, noFlecks, throttle));
            return Made;
        }

        public int Dust(Vector origin, Vector normal, Vector color, float scale, float throttle)
        {
            Dusts.Add((origin, normal, color, scale, throttle));
            return Made;
        }
    }

    private sealed class FakeSparks : IClientImpactSparks
    {
        public readonly List<(Vector Origin, Vector ShotDir, Vector Normal, int Scale, float Throttle)> Calls = [];

        public int ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle)
        {
            Calls.Add((origin, shotDir, normal, scale, throttle));
            return 1;
        }
    }

    private sealed class HalfThrottle : IParticleThrottle
    {
        public float ThrottleScaling() => 0.5f;
    }

    private sealed class FakeOneShots : IClientParticleOneShots
    {
        public readonly List<(string Name, Vector Sort, ParticleControlPoint[] Points, Vector Forward)> Starts = [];

        public string? NameFromIndex(int index) => null;

        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points,
                          Vector forward, Vector right, Vector up)
        {
            Starts.Add((name, sortOrigin, points.ToArray(), forward));
            return true;
        }
    }

    private sealed class FakeSurfaces : IFwdPhysicsSurfaceProps
    {
        public readonly Dictionary<int, string> Sounds = new() { [ServerSurface] = "Bullet.Concrete", [7] = "Bullet.Trace" };
        public readonly List<int> Asked = [];

        public int ParseSurfaceData(string fileName, string text) => 0;
        public int SurfacePropCount() => 0;
        public string? PropName(int index) => null;
        public int SurfaceIndex(string name) => -1;
        public int GameMaterial(int surfaceProps) => 0;
        public MoveSurface Surface(int surfaceProps) => default;
        public string? ScrapeSound(int surfaceProps, bool smooth) => null;

        public string? BulletImpactSound(int surfaceProps)
        {
            Asked.Add(surfaceProps);
            return Sounds.TryGetValue(surfaceProps, out string? name) ? name : null;
        }
    }

    private sealed class FakeSounds : IClientEntitySounds
    {
        public readonly List<(EntityHandle Entity, string Name, Vector? Origin)> Emits = [];

        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null) => Emits.Add((entity, soundName, origin));

        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    private sealed class Harness
    {
        public readonly FakeImpactEngine Engine = new();
        public readonly RecordingEngine Fake = TestServices.Fake();
        public readonly FakeDecals Decals = new();
        public readonly FakeSprays Sprays = new();
        public readonly FakeSparks Sparks = new();
        public readonly FakeOneShots OneShots = new();
        public readonly FakeSurfaces Surfaces = new();
        public readonly FakeSounds Sounds = new();
        public readonly RecordingLog Log = new();
        public bool DrawDebris = true, DrawDust = true, DrawFlecks = true, NewEffects;
        public bool DecalsOn = true, ModelDecalsOn = true;
        public readonly BulletImpactEffect Effect;

        public Harness(char material = 'C')
        {
            Material(material);
            // The world, as SourceSharp_ClientEntityModelType answers it: a brush model.
            Fake.Returns("WorldQuery.ClientEntityModelType", (ModelType?)ModelType.Brush);
            Effect = new BulletImpactEffect(Engine, Fake, Fake, Decals, Sprays, Sparks, new HalfThrottle(), OneShots,
                                            Surfaces, Sounds,
                                            new BulletImpactSettings(() => DrawDebris, () => DrawDust, () => DrawFlecks,
                                                                     () => NewEffects),
                                            new DecalSettings(() => DecalsOn, () => ModelDecalsOn),
                                            Log);
        }

        public Harness Material(char material)
        {
            Fake.Returns("WorldQuery.SurfacePropGameMaterial", (SurfaceMaterial)material);
            return this;
        }

        public void Shoot(int entity = 0, int hitbox = 0, int surface = ServerSurface)
            => Effect.Dispatch(new EffectData
            {
                Origin = Origin, Start = Start, EntIndex = entity, HitBox = hitbox, SurfaceProp = surface,
                DamageType = 2, Scale = 1f,
            });
    }

    // =====================================================================
    // The trace.
    // =====================================================================

    [Fact]
    public void TheEffectAnswersToTheImpactName()
        => Assert.Equal("Impact", new Harness().Effect.EffectName);

    [Fact]
    public void TheTraceRunsFromTheStartToEightUnitsPastTheImpact()
    {
        var h = new Harness();
        h.Shoot(entity: 5);

        Assert.Single(h.Engine.Clips);
        Assert.True(Same(h.Engine.Clips[0].Start, Start), "from the shot's start");
        Assert.True(Same(h.Engine.Clips[0].End, TraceExt), $"to the impact plus 8 along the shot, not {h.Engine.Clips[0].End}");
        Assert.Equal(5, h.Engine.Clips[0].Entity);
    }

    [Fact]
    public void AClipThatHitsIsTheTraceAndTheWorldIsNotTraced()
    {
        var h = new Harness();
        h.Engine.Clip = Hit(world: false, entity: 5);
        h.Shoot(entity: 5);

        Assert.Empty(h.Engine.WorldTraces);
    }

    [Fact]
    public void AClipThatMissesFallsBackToTheWorldTrace()
    {
        var h = new Harness();
        h.Engine.Clip = Miss;
        h.Shoot(entity: 5);

        Assert.Single(h.Engine.WorldTraces);
        Assert.True(Same(h.Engine.WorldTraces[0].End, TraceExt));
    }

    [Fact]
    public void AnEntityThisClientDoesNotHaveIsTracedThroughTheWorld()
    {
        var h = new Harness();
        h.Engine.Clip = null;
        h.Shoot(entity: 5);

        Assert.Single(h.Engine.WorldTraces);
    }

    // =====================================================================
    // The decal.
    // =====================================================================

    [Fact]
    public void TheDecalIsImpactConcreteTranslatedForTheMaterial()
    {
        var h = new Harness('W');
        h.Decals.Translated = "Impact.Wood";
        h.Shoot();

        Assert.Equal(("Impact.Concrete", (int)'W'), h.Decals.Translations.Single());
        Assert.Equal("Impact.Wood", h.Decals.Lookups.Single());
    }

    [Fact]
    public void ADecalTheMaterialDoesNotTranslateKeepsItsOwnName()
    {
        var h = new Harness();
        h.Decals.Translated = null;
        h.Shoot();

        Assert.Equal("Impact.Concrete", h.Decals.Lookups.Single());
    }

    [Fact]
    public void ADecalNameWithNoIndexDrawsNoDecal()
    {
        var h = new Harness();
        h.Decals.Index = -1;
        h.Shoot();

        Assert.Equal(0, h.Fake.CountOf("Effects.ClientDecalShoot") + h.Fake.CountOf("Effects.ClientDecalStaticProp")
                        + h.Fake.CountOf("Effects.ClientDecalStudioModel"));
    }

    [Fact]
    public void AHitboxOnTheWorldIsAStaticPropDecalOnPropHitboxMinusOne()
    {
        var h = new Harness();
        h.Shoot(entity: 0, hitbox: 3);

        var call = h.Fake.Last("Effects.ClientDecalStaticProp")!.Value;
        Assert.True(Same(call.ArgAs<Vector>(0), Start) && Same(call.ArgAs<Vector>(1), TraceExt), "along the extended ray");
        Assert.Equal(2, call.ArgAs<int>(2));
        Assert.Equal(Decal, call.ArgAs<int>(3));
    }

    [Fact]
    public void AStudioModelTakesTheDecalAlongTheRay()
    {
        var h = new Harness();
        h.Fake.Returns("WorldQuery.ClientEntityModelType", (ModelType?)ModelType.Studio);
        h.Shoot(entity: 5);

        var call = h.Fake.Last("Effects.ClientDecalStudioModel")!.Value;
        Assert.Equal(5, call.ArgAs<int>(0));
        Assert.True(Same(call.ArgAs<Vector>(1), Start)
                    && Same(call.ArgAs<Vector>(2), ClientEntityDecal.StudioRayDelta(Start, TraceExt)),
                    "AddStudioDecal's ray: from the start along the extended segment, lengthened a tenth");
        Assert.Equal(Decal, call.ArgAs<int>(3));
    }

    [Fact]
    public void RDecalsOffLeavesNoDecalOnTheWorld()
    {
        var h = new Harness();
        h.DecalsOn = false;
        h.Shoot(entity: 0);

        Assert.Equal(0, h.Fake.CountOf("Effects.ClientDecalShoot"));
        Assert.Single(h.Sounds.Emits);
    }

    [Fact]
    public void RDrawModelDecalsOffLeavesNoDecalOnAModel()
    {
        var h = new Harness();
        h.ModelDecalsOn = false;
        h.Fake.Returns("WorldQuery.ClientEntityModelType", (ModelType?)ModelType.Studio);
        h.Shoot(entity: 5);

        Assert.Equal(0, h.Fake.CountOf("Effects.ClientDecalStudioModel"));
    }

    [Fact]
    public void TheWorldTakesTheDecalAtThePointTheTraceFound()
    {
        var h = new Harness();
        h.Shoot(entity: 0);

        var call = h.Fake.Last("Effects.ClientDecalShoot")!.Value;
        Assert.Equal(Decal, call.ArgAs<int>(0));
        Assert.Equal(0, call.ArgAs<int>(1));
        Assert.True(Same(call.ArgAs<Vector>(2), HitPoint), "the trace's end, not the server's origin");
    }

    [Fact]
    public void AnEntityThisClientDoesNotHaveDecalsForNoEntity()
    {
        var h = new Harness();
        h.Engine.Clip = null;
        h.Shoot(entity: 5);

        Assert.Equal(-1, h.Fake.Last("Effects.ClientDecalShoot")!.Value.ArgAs<int>(1));
    }

    // =====================================================================
    // The custom effect.
    // =====================================================================

    [Fact]
    public void ConcreteThrowsDebrisInTheWorldsGammaCorrectedLight()
    {
        var h = new Harness('C');
        h.Shoot();

        var debris = h.Sprays.Debris.Single();
        Assert.True(Same(debris.End, HitPoint) && Same(debris.Normal, Normal), "at the trace's end, off its normal");
        var expected = new Vector(MathF.Pow(0.25f, 1f / 2.2f) * 2f, MathF.Pow(0.5f, 1f / 2.2f) * 3f, 4f);
        Assert.True(Near(debris.Color, expected), $"pow( diffuse, 1/2.2 ) * base, not {debris.Color}");
        Assert.Equal((int)'C', debris.Material);
        Assert.Equal((1, false, 0.5f), (debris.Scale, debris.NoFlecks, debris.Throttle));
    }

    [Fact]
    public void TheWorldsLightIsReadAlongTheTracePushedOnATenth()
    {
        var h = new Harness('C');
        h.Shoot();

        var light = h.Engine.WorldLights.Single();
        Assert.True(Same(light.Start, Start));
        Assert.True(Near(light.End, new Vector(99f * 1.1f, 1.1f, 2.2f)), $"start + ( end - start ) * 1.1, not {light.End}");
    }

    [Fact]
    public void AStaticPropsColourIsItsOwnLightingOnPropHitboxMinusOne()
    {
        var h = new Harness('C');
        h.Engine.World = Hit(world: true, hitbox: 4);
        h.Shoot();

        var light = h.Engine.PropLights.Single();
        Assert.Equal(3, light.Prop);
        Assert.True(Same(light.End, HitPoint) && Same(light.Normal, Normal));
        Assert.Empty(h.Engine.WorldLights);
    }

    [Fact]
    public void AnEntitysSurfaceIsWhiteAndSaysSo()
    {
        var h = new Harness('C');
        h.Engine.World = Hit(world: false, entity: 9);
        h.Shoot();

        Assert.True(Same(h.Sprays.Debris.Single().Color, new Vector(255f, 255f, 255f)));
        Assert.Contains("Couldn't find surface in GetColorForSurface()", h.Log.Text);
    }

    [Fact]
    public void DebrisCountsIntoTheFlecksLedger()
    {
        var h = new Harness('C');
        h.Shoot();

        Assert.Equal((5, 0), h.Engine.Ledgers.Single());
        Assert.Equal(5, h.Effect.Flecks);
    }

    [Fact]
    public void FxDrawImpactDebrisOffThrowsNoDebris()
    {
        var h = new Harness('C') { DrawDebris = false };
        h.Shoot();

        Assert.Empty(h.Sprays.Debris);
    }

    [Fact]
    public void RDrawFlecksOffThrowsDebrisWithoutFlecks()
    {
        var h = new Harness('C') { DrawFlecks = false };
        h.Shoot();

        Assert.True(h.Sprays.Debris.Single().NoFlecks);
    }

    [Fact]
    public void DirtRaisesDustAtTheServersOriginAndCountsIt()
    {
        var h = new Harness('D');
        h.Shoot();

        var dust = h.Sprays.Dusts.Single();
        Assert.True(Same(dust.Origin, Origin), "FX_DustImpact is handed the effect's origin");
        Assert.True(Same(dust.Normal, Normal));
        Assert.Equal((1f, 0.5f), (dust.Scale, dust.Throttle));
        Assert.Equal((0, 5), h.Engine.Ledgers.Single());
    }

    [Fact]
    public void FxDrawImpactDustOffRaisesNoDust()
    {
        var h = new Harness('D') { DrawDust = false };
        h.Shoot();

        Assert.Empty(h.Sprays.Dusts);
    }

    [Fact]
    public void MetalSparksAlongTheShotOffTheSurface()
    {
        var h = new Harness('M');
        h.Shoot();

        var spark = h.Sparks.Calls.Single();
        Assert.True(Same(spark.Origin, Origin) && Same(spark.Normal, Normal));
        Assert.True(Near(spark.ShotDir, new Vector(1f, 0f, 0f)), "the normalised shot");
        Assert.Equal((1, 0.5f), (spark.Scale, spark.Throttle));
    }

    [Fact]
    public void AComputerSparksOneUnitOffTheSurface()
    {
        var h = new Harness('P');
        h.Shoot();

        var call = h.Fake.Last("Effects.ClientSparks")!.Value;
        Assert.True(Same(call.ArgAs<Vector>(0), new Vector(100f, 1f, 0f)), "origin + normal");
        Assert.Equal((1, 1), (call.ArgAs<int>(1), call.ArgAs<int>(2)));
        Assert.Null(call.Arg(3));
    }

    [Fact]
    public void AWarpShieldDispatchesItsParticleBackAlongTheShot()
    {
        var h = new Harness('Z');
        h.Shoot();

        var start = h.OneShots.Starts.Single();
        Assert.Equal("warp_shield_impact", start.Name);
        Assert.True(Same(start.Sort, Origin));
        Assert.Equal(new[] { new ParticleControlPoint(0, Origin), new ParticleControlPoint(1, Origin) }, start.Points);
        Assert.True(Near(start.Forward, new Vector(-1f, 0f, 0f)), $"facing back up the shot, not {start.Forward}");
    }

    [Fact]
    public void TheNewEffectsOrientTheirThreeControlPointsOnTheSurface()
    {
        var h = new Harness('C') { NewEffects = true };
        h.Engine.World = Hit(entity: 0);
        h.Shoot();

        var particle = h.Engine.Particles.Single();
        Assert.Equal("impact_concrete", particle.Name);
        Assert.All(particle.Points, p => Assert.True(Same(p.Position, HitPoint), "at the trace's end"));
        Assert.True(Same(particle.Points[0].Forward, Normal), "0 along the surface normal");
        Assert.True(Near(particle.Points[1].Forward, new Vector(1f, 0f, 0f)), "1 along the shot reflected (it grazes)");
        Assert.True(Near(particle.Points[2].Forward, new Vector(-1f, 0f, 0f)), "2 back along the shot");
        // VectorVectors( (0,1,0) ): right = forward x up = (1,0,0), negated.
        Assert.True(Near(particle.Points[0].Right, new Vector(-1f, 0f, 0f)), $"right negated, not {particle.Points[0].Right}");
        Assert.Equal((0, 1f), (particle.Entity, particle.Scale));
    }

    [Fact]
    public void TheSkyTakesNoEffectButStillTakesTheDecalAndTheSound()
    {
        var h = new Harness('C');
        h.Engine.World = Hit(flags: 0x0004);
        h.Shoot();

        Assert.Empty(h.Sprays.Debris);
        Assert.Equal(1, h.Fake.CountOf("Effects.ClientDecalShoot"));
        Assert.Single(h.Sounds.Emits);
    }

    [Fact]
    public void AMissedTraceTakesNoEffect()
    {
        var h = new Harness('C');
        h.Engine.World = Miss;
        h.Shoot();

        Assert.Empty(h.Sprays.Debris);
    }

    // =====================================================================
    // The sound.
    // =====================================================================

    [Fact]
    public void TheSoundIsTheServersSurfacesBulletImpactAtTheTracesEnd()
    {
        var h = new Harness();
        h.Shoot(surface: ServerSurface);

        var emit = h.Sounds.Emits.Single();
        Assert.Equal("Bullet.Concrete", emit.Name);
        Assert.Equal(EntityHandle.None, emit.Entity);
        Assert.True(Same(emit.Origin!.Value, HitPoint));
    }

    [Fact]
    public void AServerSurfacepropOfZeroPlaysTheTracesSurface()
    {
        var h = new Harness();
        h.Shoot(surface: 0);

        Assert.Equal(7, h.Surfaces.Asked.Single());
        Assert.Equal("Bullet.Trace", h.Sounds.Emits.Single().Name);
    }

    [Fact]
    public void AMissedTraceSoundsAtTheServersOrigin()
    {
        var h = new Harness();
        h.Engine.World = Miss;
        h.Shoot();

        Assert.True(Same(h.Sounds.Emits.Single().Origin!.Value, Origin));
    }

    [Fact]
    public void ASurfaceWithNoBulletImpactSoundIsSilent()
    {
        var h = new Harness();
        h.Surfaces.Sounds.Clear();
        h.Shoot();

        Assert.Empty(h.Sounds.Emits);
    }

    // =====================================================================
    // The ragdoll sweep.
    // =====================================================================

    [Fact]
    public void TheRagdollSweepIsRefusedAloudOnceAcrossImpacts()
    {
        var h = new Harness();
        h.Shoot();
        h.Shoot();

        Assert.Single(h.Log.Lines, l => l.Warning && l.Text.Contains("FX_AffectRagdolls"));
        Assert.Equal(2, h.Effect.Impacts);
    }
}
