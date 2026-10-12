using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The view the executor's draws are in (lane c5-particles): read through the
/// shim once per view key, not once per draw, and handed to the draw that
/// follows the binding's Draw.
/// </summary>
public class ParticleViewsTests
{
    private sealed class CountingSource : IParticleViewSource
    {
        public int Reads;
        public int ViewId = 3;

        public ParticleView Read()
        {
            Reads++;
            Span<float> m = stackalloc float[12] { 1, 0, 0, 10, 0, 1, 0, 20, 0, 0, 1, 30 };
            return new ParticleView(m, ViewId);
        }
    }

    [Fact]
    public void Draws_under_one_key_read_the_view_once()
    {
        var source = new CountingSource();
        var views = new ParticleViews(source) { Key = 7 };
        for (int i = 0; i < 50; i++)
            Assert.Equal(3, views.Current.ViewId);
        Assert.Equal(1, source.Reads);
        Assert.Equal(1, views.Reads);
    }

    [Fact]
    public void A_new_key_reads_the_view_again()
    {
        var source = new CountingSource();
        var views = new ParticleViews(source) { Key = 7 };
        _ = views.Current;
        views.Key = 8;
        source.ViewId = 5;
        Assert.Equal(5, views.Current.ViewId);
        _ = views.Current;
        Assert.Equal(2, source.Reads);
    }

    [Fact]
    public void Nothing_is_read_until_a_draw_needs_the_view()
    {
        var source = new CountingSource();
        var views = new ParticleViews(source);
        views.Key = 1;
        views.Key = 2;
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public void A_point_takes_the_translation_and_a_direction_does_not()
    {
        Span<float> m = stackalloc float[12] { 0, 1, 0, 10, 0, 0, 1, 20, 1, 0, 0, 30 };
        var view = new ParticleView(m, 0);
        Assert.Equal(new Vector(12, 23, 31), view.TransformPoint(new Vector(1, 2, 3)));
        Assert.Equal(new Vector(2, 3, 1), view.TransformDirection(new Vector(1, 2, 3)));
    }

    private sealed class KeySeeingSims(ParticleViews views) : IClientParticleSims
    {
        public int SeenKey;

        public int Emit(uint owner, int channel, ref ParticleEmit emit, Span<int> idsOut) => 0;

        public int Simulate(uint owner, int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut) => 0;

        public int Draw(uint owner, int channel, Span<ParticleQuadRaw> quadsOut)
        {
            SeenKey = views.Key;
            return 0;
        }
    }

    private sealed class NoQuery : IClientParticleQuery
    {
        public int CollisionGroupFromName(string name) => 0;
        public (byte R, byte G, byte B, byte A) LightingAtPoint(Vector origin) => default;
        public bool ShouldTrace() => true;
        public void RandomPointsOnControllingObjectHitBox(Vector controlPoint, bool hasObject, Span<Vector> points,
                                                          Span<int> hitBoxIndices, Span<Vector> relativeCoords) { }
        public int ControllingObjectHitBoxInfo(bool hasObject) => 0;
    }

    private sealed class NoSparks : IClientImpactSparks
    {
        public int ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle) => 0;
    }

    private sealed class NoFlecks : IClientImpactSprays
    {
        public readonly List<string> Calls = [];
        public int Debris(Vector endPos, Vector normal, Vector color, int material, int scale, bool noFlecks, float throttle)
        {
            Calls.Add($"debris {endPos} {normal} {color} {material} {scale} {noFlecks} {throttle}");
            return 7;
        }
        public int Dust(Vector origin, Vector normal, Vector color, float scale, float throttle)
        {
            Calls.Add($"dust {origin} {normal} {color} {scale} {throttle}");
            return 5;
        }
    }

    // The manager hands the sim's draw the view key native read for it (ParticlesViewKey).
    [Fact]
    public void The_manager_hands_the_draw_its_view_key()
    {
        var views = new ParticleViews(new CountingSource());
        var sims = new KeySeeingSims(views);
        var manager = new SourceSharp.Client.Effects.ClientParticleManager(() => sims, new FixedRandom(), views);
        manager.Add(1, 0, 0f, alwaysSimulate: true);
        manager.Binding(1, 0)!.AddParticle(0, 7, default);
        var input = new ParticleDrawIn { Owner = 1, ClientModeDraws = 1, DrawParticles = 1, ViewKey = unchecked((int)0x9e3779b9) };
        manager.DrawBegin(input, out _, new ParticleQuadRaw[4]);
        Assert.Equal(unchecked((int)0x9e3779b9), sims.SeenKey);
    }

    private sealed class FixedRandom : IUniformRandom
    {
        public float RandomFloat(float min, float max) => min;
        public int RandomInt(int min, int max) => max;
        public float RandomFloatExp(float min, float max, float exponent) => min;
    }

    [Fact]
    public void The_test_process_keeps_denormals_which_is_why_the_oracles_compare_them_as_zero()
    {
        // The instrument's other arm is the live read (cs_cl_fx_report on the
        // game thread); here, with the default MXCSR, both are off.
        var mode = new ParticleFloatMode();
        Assert.Equal((false, false), mode.Measure());
        mode.Sample();
        Assert.Equal((1L, 0L, 0L), (mode.Samples, mode.FlushToZero, mode.DenormalsAreZero));
    }
}
