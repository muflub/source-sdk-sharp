using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The arms of fx_sparks.cpp's routines the recorded session did not reach
/// (lane c5-particles; sparks-stock.tsv.gz holds the rest).
/// </summary>
public class SparkEffectsTests
{
    private sealed class Ranges : IParticleRandom
    {
        public readonly List<(float Min, float Max)> Floats = [];

        public float RandomFloat(float min, float max)
        {
            Floats.Add((min, max));
            return min;
        }

        public int RandomInt(int min, int max) => max;
    }

    private sealed class Quads : IFxQuadSink
    {
        public int Added;
        public readonly List<(Vector Origin, float StartSize, float EndSize, float Life, string Material)> Laid = [];

        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias, float startAlpha,
                            float endAlpha, float alphaBias, float yaw, float deltaYaw, Vector color, float lifeTime,
                            string material, FxQuadFlags flags)
        {
            Added++;
            Laid.Add((origin, startSize, endSize, lifeTime, material));
        }
    }

    private sealed class Environment : ISparkEnvironment
    {
        public float CurTime => 10;
        public float ThrottleScaling => 1;
        public bool DrawMetalSpark { get; set; } = true;
    }

    private sealed class Nothing : IParticleViewSource, IParticlePixelVisibility, IParticleWorldTrace
    {
        public ParticleView Read() => throw new InvalidOperationException();
        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1 };
    }

    private static (SparkEffects, Quads, Environment, RecordingParticleExecutor) Rig()
    {
        var executor = new RecordingParticleExecutor();
        var quads = new Quads();
        var env = new Environment();
        var nothing = new Nothing();
        return (new SparkEffects(new ClientParticleEffects(executor, new ParticleEffectKinds()), nothing,
                                 new ParticleViews(nothing), nothing, quads, env, new RandomSeeds(1)), quads, env, executor);
    }

    [Fact]
    public void A_big_metal_spark_batch_sends_every_third_spark_far()
    {
        var (sparks, _, _, _) = Rig();
        var random = new Ranges();
        SparkBurst burst = sparks.MetalSpark(new Vector(0, 0, 0), new Vector(0, 0, 1), new Vector(0, 0, 1), 2, 1.0f, random);

        // RandomInt answers its max: 8 * (2 * 2) = 32 sparks, each opening with its die time.
        var trail = Assert.IsType<TrailEmitter>(Assert.Single(burst.Emitters));
        Assert.Equal(32, trail.Pending.Count);
        var lives = random.Floats.Where(f => f is (0.15f, 0.25f) or (0.05f, 0.1f)).ToList();
        Assert.Equal(32, lives.Count);
        for (int i = 0; i < lives.Count; i++)
            Assert.Equal(i % 3 == 0 ? (0.15f, 0.25f) : (0.05f, 0.1f), lives[i]);
    }

    [Fact]
    public void A_scale_one_metal_spark_keeps_every_spark_short()
    {
        var (sparks, _, _, _) = Rig();
        var random = new Ranges();
        sparks.MetalSpark(new Vector(0, 0, 0), new Vector(0, 0, 1), new Vector(0, 0, 1), 1, 1.0f, random);
        Assert.DoesNotContain((0.15f, 0.25f), random.Floats);
        Assert.Contains((0.05f, 0.1f), random.Floats);
    }

    [Fact]
    public void Metal_sparks_draw_nothing_when_fx_drawmetalspark_is_off()
    {
        var (sparks, quads, env, executor) = Rig();
        env.DrawMetalSpark = false;
        sparks.MetalSpark(new Vector(0, 0, 0), new Vector(0, 0, 1), new Vector(0, 0, 1));
        Assert.Equal(0, quads.Added);
        Assert.Empty(executor.Calls);

        env.DrawMetalSpark = true;
        sparks.MetalSpark(new Vector(0, 0, 0), new Vector(0, 0, 1), new Vector(0, 0, 1));
        Assert.Equal(1, quads.Added);
        Assert.NotEmpty(executor.Calls);
    }

    [Fact]
    public void An_electric_spark_starts_two_trails_and_a_glow_of_two_sheets()
    {
        var (sparks, _, _, executor) = Rig();
        SparkBurst burst = sparks.ElectricSpark(new Vector(1, 2, 3), 1, 1, null, new Ranges());
        Assert.Equal(["trail", "trail", "sheet", "sheet"], burst.Emitters.Select(e => e is TrailEmitter ? "trail" : "sheet"));
        var sheets = burst.Emitters.OfType<SimpleSheet>().ToList();
        Assert.Same(sheets[0].Gate, sheets[1].Gate);
        Assert.Equal(10.2f, sheets[0].Gate!.DeathTime, 5);
        Assert.Equal(4, executor.Calls.Count(c => c.StartsWith("createat ", StringComparison.Ordinal)));
    }

    private sealed class Sequence(params float[] floats) : IParticleRandom
    {
        private readonly Queue<float> _floats = new(floats);
        public readonly List<string> Asked = [];

        public float RandomFloat(float min, float max)
        {
            Asked.Add($"f {min} {max}");
            return _floats.Count > 0 ? _floats.Dequeue() : min;
        }

        public int RandomInt(int min, int max)
        {
            Asked.Add($"i {min} {max}");
            return min;
        }
    }

    [Fact]
    public void An_impact_reflects_the_shot_off_the_surface()
    {
        // A shot down and forward onto a floor leaves up and forward.
        Vector r = SparkEffects.Reflect(new Vector(0.6f, 0, -0.8f), new Vector(0, 0, 1));
        Assert.Equal(0.6f, r.X, 6);
        Assert.Equal(0.0f, r.Y, 6);
        Assert.Equal(0.8f, r.Z, 6);
    }

    [Fact]
    public void An_impact_jitters_the_reflection_before_the_metal_spark_draws()
    {
        var (sparks, quads, _, _) = Rig();
        var random = new Sequence(0.1f, -0.2f, 0.05f);
        SparkBurst burst = sparks.ImpactMetalSpark(new Vector(0, 0, 0), new Vector(0.6f, 0, -0.8f), new Vector(0, 0, 1), 1, 1.0f, random);

        Assert.Equal(["f -0.2 0.2", "f -0.2 0.2", "f -0.2 0.2", "i 4 8"], random.Asked.Take(4));
        Assert.Equal(1, quads.Added);
        var trail = Assert.IsType<TrailEmitter>(Assert.Single(burst.Emitters));
        Assert.Equal(8, trail.Pending.Count);   // RandomInt answers 4: 4 * (1 * 2) at full throttle
    }

    [Fact]
    public void An_impact_metal_spark_is_off_with_fx_drawmetalspark()
    {
        var (sparks, quads, env, executor) = Rig();
        env.DrawMetalSpark = false;
        Assert.Equal(0, ((IClientImpactSparks)sparks).ImpactMetalSpark(default, new Vector(1, 0, 0), new Vector(0, 0, 1), 1, 1.0f));
        Assert.Equal(0, quads.Added);
        Assert.Empty(executor.Calls);
        env.DrawMetalSpark = true;
        Assert.True(((IClientImpactSparks)sparks).ImpactMetalSpark(default, new Vector(1, 0, 0), new Vector(0, 0, 1), 1, 1.0f) > 0);
    }

    // FX_Sparks and the stunstick's impact (weapon_stunstick.cpp), its one
    // caller in this build. The Ranges stream answers every float at its
    // minimum and every int at its maximum.

    private static SparkBurst StunstickSparks(SparkEffects sparks)
        => sparks.Sparks(new Vector(0, 0, 0), 1, 2, new Vector(0, 0, 1), 6, 64, 256,
                         SparkEffects.SparkMaterial, new Ranges());

    [Fact]
    public void FX_Sparks_at_magnitude_one_sends_two_big_sparks_and_eight_little_ones()
    {
        var (sparks, _, _, _) = Rig();
        SparkBurst burst = StunstickSparks(sparks);

        Assert.Equal(2, burst.Emitters.Count);
        var big = Assert.IsType<TrailEmitter>(burst.Emitters[0]);
        var little = Assert.IsType<TrailEmitter>(burst.Emitters[1]);

        // magnitude^2 * RandomFloat( 2, 4 ), then magnitude * RandomInt( 4, 8 ).
        Assert.Equal(2, big.Pending.Count);
        Assert.Equal(8, little.Pending.Count);
        Assert.Equal(SparkEffects.SparkMaterial, big.Material);
        Assert.Equal(400.0f, little.Collision.Gravity);
    }

    [Fact]
    public void FX_Sparks_big_sparks_leave_along_the_direction_at_twice_the_least_speed_with_no_spread()
    {
        var (sparks, _, _, _) = Rig();
        var big = Assert.IsType<TrailEmitter>(StunstickSparks(sparks).Emitters[0]);

        // spreadOfs = 0: no offset, and minSpeed * ( 2 - 0 ).
        Assert.Equal(new Vector(0, 0, 128), big.Pending[0].Velocity);
        Assert.Equal(6.0f, big.Pending[0].Width);
        Assert.Equal(1.0f, big.Pending[0].DieTime);
    }

    [Fact]
    public void FX_Sparks_little_sparks_are_a_quarter_the_width()
    {
        var (sparks, _, _, _) = Rig();
        var little = Assert.IsType<TrailEmitter>(StunstickSparks(sparks).Emitters[1]);

        Assert.Equal(1.5f, little.Pending[0].Width);
        Assert.Equal(0.3f, little.Pending[0].DieTime, 5);
    }

    [Fact]
    public void The_stunstick_impact_lays_one_glow_quad_and_throws_sparks()
    {
        var (sparks, quads, _, _) = Rig();
        SparkBurst burst = sparks.StunstickImpact(new Vector(1, 2, 3), new Vector(-1, 0, 0), new Ranges());

        var quad = Assert.Single(quads.Laid);
        Assert.Equal(new Vector(1, 2, 3), quad.Origin);
        Assert.Equal((16f, 32f, 0.1f), (quad.StartSize, quad.EndSize, quad.Life));
        Assert.Equal("sprites/light_glow02_add", quad.Material);
        Assert.Equal(2, burst.Emitters.Count);
    }

    [Fact]
    public void The_stunstick_impact_is_a_managed_client_effect_under_its_stock_name()
    {
        var (sparks, quads, _, _) = Rig();
        var handlers = new SourceSharp.Client.ClientEffectHandlers([new StunstickImpactEffect(sparks)]);

        Assert.True(handlers.TryDispatch("StunstickImpact",
                                         new EffectData { Origin = new Vector(0, 0, 0), Normal = new Vector(0, 0, 1) }));
        Assert.Equal(1, quads.Added);
    }
}
