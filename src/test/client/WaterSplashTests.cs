using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// fx_water.cpp's arms the recorded session did not reach (lane c5-particles;
/// watersplash-stock.tsv.gz holds the rest): the ripple on water, the slime
/// splash, the effect handlers, cl_show_splashes and the scale's clamp.
/// </summary>
public class WaterSplashTests
{
    private sealed class Draws : IParticleRandom
    {
        public readonly List<(float, float)> Floats = [];
        public float RandomFloat(float min, float max) { Floats.Add((min, max)); return min; }
        public int RandomInt(int min, int max) => min;
    }

    private sealed class Environment : ISplashEnvironment
    {
        public bool ShowSplashes { get; set; } = true;
        public (float, Vector, Vector) Water = (1, default, default);
        public int Sounds;
        public Vector LightForPoint(Vector position) => new(0.5f, 0.5f, 0.5f);
        public (float Fraction, Vector End, Vector Normal) WaterTrace(Vector start, Vector end) => Water;
        public void SplashSound(Vector origin) => Sounds++;
    }

    private sealed class Rig : IParticleViewSource, IParticleWorldTrace, IFxQuadSink, IClientParticleOneShots
    {
        public readonly List<(Vector Origin, float Start, float End, float Yaw, float DeltaYaw, string Material)> Quads = [];
        public readonly List<(string Name, Vector Origin, ParticleControlPoint[] Points)> Systems = [];
        public ParticleView Read() => throw new InvalidOperationException();
        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1 };
        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias, float startAlpha,
                            float endAlpha, float alphaBias, float yaw, float deltaYaw, Vector color, float lifeTime,
                            string material, FxQuadFlags flags) => Quads.Add((origin, startSize, endSize, yaw, deltaYaw, material));
        public string? NameFromIndex(int index) => null;
        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points, Vector forward,
                          Vector right, Vector up)
        {
            Systems.Add((name, sortOrigin, points.ToArray()));
            return true;
        }
    }

    private static (WaterSplashes, Rig, Environment) Make()
    {
        var rig = new Rig();
        var env = new Environment();
        return (new WaterSplashes(new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds()), rig,
                                  new ParticleViews(rig), env, rig, rig, new RandomSeeds(1)), rig, env);
    }

    [Fact]
    public void A_ripple_lies_on_the_water_the_trace_found()
    {
        var (fx, rig, env) = Make();
        env.Water = (0.5f, new Vector(10, 20, -4), new Vector(0, 0, 1));
        var draws = new Draws();
        Assert.True(fx.WaterRipple(new Vector(10, 20, 30), 2.0f, null, 1.5f, 0.75f, draws));
        var q = Assert.Single(rig.Quads);
        Assert.Equal(new Vector(10, 20, -3.5f), q.Origin);
        Assert.Equal((32.0f, 256.0f), (q.Start, q.End));
        Assert.Equal("effects/splashwake1", q.Material);
        Assert.Equal([(0f, 360f), (-16f, 16f)], draws.Floats);
    }

    [Fact]
    public void No_water_under_the_point_is_no_ripple_and_no_draw()
    {
        var (fx, rig, _) = Make();
        var draws = new Draws();
        Assert.False(fx.WaterRipple(new Vector(0, 0, 0), 1.0f, null, 1.5f, 1.0f, draws));
        Assert.Empty(rig.Quads);
        Assert.Empty(draws.Floats);
    }

    [Theory]
    [InlineData(1.0f, "slime_splash_01")]
    [InlineData(3.0f, "slime_splash_02")]
    [InlineData(4.0f, "slime_splash_03")]
    public void A_slime_splash_starts_the_system_its_size_names_and_sounds(float scale, string name)
    {
        var (fx, rig, env) = Make();
        fx.GunshotSlimeSplash(new Vector(1, 2, 3), new Vector(0, 0, 1), scale);
        var started = Assert.Single(rig.Systems);
        Assert.Equal(name, started.Name);
        Assert.Equal([new ParticleControlPoint(0, new Vector(1, 2, 3)), new ParticleControlPoint(1, new Vector(1, 2, 3))], started.Points);
        Assert.Equal(1, env.Sounds);
    }

    [Fact]
    public void The_splash_effects_choose_slime_by_the_flag()
    {
        var (fx, rig, env) = Make();
        var water = new WaterSplashEffect(fx, "gunshotsplash");
        water.Dispatch(new EffectData { Origin = new Vector(0, 0, 0), Scale = 8, Flags = WaterSplashEffect.InSlime });
        Assert.Single(rig.Systems);
        water.Dispatch(new EffectData { Origin = new Vector(0, 0, 0), Scale = 8 });
        Assert.Single(rig.Systems);
        Assert.Equal(2, fx.Splashes);
        Assert.Equal(2, env.Sounds);
    }

    [Fact]
    public void Splashes_are_off_with_cl_show_splashes()
    {
        var (fx, rig, env) = Make();
        env.ShowSplashes = false;
        SplashBurst burst = fx.GunshotSplash(default, new Vector(0, 0, 1), 8, new Draws());
        fx.GunshotSlimeSplash(default, new Vector(0, 0, 1), 8);
        Assert.Null(burst.Drops);
        Assert.Empty(rig.Systems);
        Assert.Equal(0, env.Sounds);
    }

    [Fact]
    public void A_big_splash_is_held_at_four_times_the_base_size()
    {
        var (fx, _, _) = Make();
        var draws = new Draws();
        fx.GunshotSplash(default, new Vector(0, 0, 1), 1000, draws);
        // The drops' speed range is 50..100 x scale x 3, the scale held at 4.
        Assert.Contains((600f, 1200f), draws.Floats);
    }
}
