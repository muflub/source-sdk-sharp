using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>FX_AntlionImpact</c>'s port (ShardEffects.AntlionImpact, lane c5-particles).
/// STOCK-DERIVED, NOT RECORDED: the effect returns before it draws anything
/// unless the hit entity's bounding radius is known, which this ABI cannot
/// say, so no session can reach it. Every expectation here is read off stock's
/// c_impact_effects.cpp in the pristine clone: the draws in its order and
/// ranges, the spawn and dust offsets along the shot, the fixed lives, the
/// colours and the collision set-up.
/// </summary>
public class AntlionImpactStockTests
{
    /// <summary>Answers each draw with the low end and keeps the ranges asked, in order.</summary>
    private sealed class Draws : IParticleRandom, IParticleWorldTrace, IParticleViewSource, IParticlePixelVisibility,
        IClientComputeLighting, IFxRoutineEnvironment
    {
        public readonly List<string> Asked = [];
        public readonly List<(string Script, Vector Origin)> Sounds = [];

        public float RandomFloat(float min, float max)
        {
            Asked.Add($"f {min} {max}");
            return min;
        }

        public int RandomInt(int min, int max)
        {
            Asked.Add($"i {min} {max}");
            return max;
        }

        public ParticleTrace TraceLine(Vector start, Vector end) => new() { Fraction = 1, EndPos = end };
        public ParticleView Read() => throw new InvalidOperationException();
        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();
        public Vector ComputeLighting(in Vector pos, Span<Vector> boxColors) => default;
        public void Sound(string script, Vector origin) => Sounds.Add((script, origin));
        public void ImpactOverlay(Vector position, Vector normal, float r, float g, float b) { }
    }

    private static (ShardEffects Fx, Draws Draws) Make()
    {
        var d = new Draws();
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        return (new ShardEffects(effects, new ParticleViews(d), d, d, d, new FxTestRig(), new RandomSeeds(1), d), d);
    }

    // The shot comes from +x: shotDir = normalize( start - end ) = (1, 0, 0).
    private static readonly Vector Start = new(100, 0, 0), End = new(0, 0, 0);

    [Fact]
    public void Without_the_entitys_bounds_nothing_is_drawn_and_nothing_drawn_from_the_stream()
    {
        var (fx, d) = Make();
        Assert.Null(fx.AntlionImpact(Start, End, null, d));
        Assert.Empty(d.Asked);
        Assert.Empty(d.Sounds);
    }

    [Fact]
    public void Stocks_draws_in_stocks_order_and_ranges()
    {
        var (fx, d) = Make();
        Assert.NotNull(fx.AntlionImpact(Start, End, 16.0f, d));

        // numFlecks = RandomInt( 8, 16 ); answered 16.
        var want = new List<string> { "i 8 16" };
        for (int i = 0; i < 16; i++)
        {
            // material pair, life 3.0f (fixed, no draw), three spray draws in
            // [-flAngularSpray, flAngularSpray] = [-1, 1], size RandomInt( 1, 6 ),
            // speed RandomFloat( FLECK_MIN_SPEED 64, flMaxSpeed 256 ),
            // m_vAngles.Random( 0, 360 ), angular speed RandomFloat( -800, 800 ).
            want.AddRange(["i 0 1", "f -1 1", "f -1 1", "f -1 1", "i 1 6", "f 64 256", "f 0 360", "f 0 360", "f 0 360", "f -800 800"]);
        }
        for (int i = 0; i < 4; i++)
        {
            // die 1.0f (fixed), spray [-0.8, 0.8] x3, size RandomInt( 8, 16 ),
            // speed RandomFloat( 4, 64 ), alpha RandomInt( 32, 64 ),
            // roll RandomFloat( 0, 2*M_PI ), roll delta RandomFloat( -0.5, 0.5 ),
            // colour ramp RandomFloat( 0.5, 1.0 ).
            want.AddRange(["f -0.8 0.8", "f -0.8 0.8", "f -0.8 0.8", "i 8 16", "f 4 64", "i 32 64",
                           $"f 0 {(float)(2.0 * Math.PI)}", "f -0.5 0.5", "f 0.5 1"]);
        }
        Assert.Equal(want, d.Asked);
    }

    [Fact]
    public void Stocks_offsets_lives_colours_and_collision()
    {
        var (fx, d) = Make();
        ShardEffects.Made made = fx.AntlionImpact(Start, End, 16.0f, d)!;

        // spawnOffset = endpos + shotDir * 2; collision Setup( spawnOffset, &shotDir, 1.0f, 8.0f, 256.0f, FLECK_GRAVITY 800, FLECK_DAMPEN 0.3 ).
        Assert.Equal((new Vector(1, 0, 0), 1.0f, 8.0f, 256.0f, 800.0f, 0.3f),
                     (made.Direction, made.Spread, made.MinSpeed, made.MaxSpeed, made.Gravity, made.Dampen));
        Assert.Equal(16, made.Shards.Made.Count);
        foreach (Particle3D f in made.Shards.Made)
        {
            Assert.Equal(new Vector(2, 0, 0), f.Position);
            Assert.Equal(3.0f, f.LifeRemaining);
            // dir = shotDir + (-1, -1, -1) (each spray its low end), times the speed's low end 64.
            Assert.Equal(new Vector(0, -1, -1) * 64.0f, f.Velocity);
            Assert.Equal((byte)6, f.Size);
            Assert.Equal((255, 255, 255, 128, 128, 128), ((int)f.FrontR, (int)f.FrontG, (int)f.FrontB, (int)f.BackR, (int)f.BackG, (int)f.BackB));
        }
        Assert.All(made.Shards.Sheets, s => Assert.Contains(s.Material, new[] { "effects/fleck_antlion1", "effects/fleck_antlion2" }));

        // The dust: four puffs of g_Mat_DustPuff[0] at endpos + shotDir * 4, a second's life,
        // end size four times the start, colour MIN( 1, (1, 0.9, 0.75) * ramp ) * 255 at ramp 0.5.
        SimpleSheet dust = Assert.Single(made.Dust);
        Assert.Equal("particle/particle_smokegrenade", dust.Material);
        Assert.Equal(4, dust.Pending.Count);
        foreach (SimpleParticle p in dust.Pending)
        {
            Assert.Equal(new Vector(4, 0, 0), p.Position);
            Assert.Equal((0.0f, 1.0f), (p.Lifetime, p.DieTime));
            Assert.Equal((16, 64, 64, 0), ((int)p.StartSize, (int)p.EndSize, (int)p.StartAlpha, (int)p.EndAlpha));
            Assert.Equal(((int)(0.5f * 255.0f), (int)(0.45f * 255.0f), (int)(0.375f * 255.0f)), ((int)p.R, (int)p.G, (int)p.B));
        }

        Assert.Equal([("FX_AntlionImpact.ShellImpact", End)], d.Sounds);
    }
}
