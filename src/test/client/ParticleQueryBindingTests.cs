using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// IManagedParticlesClient's five query slots (lane c5-particles): what the
/// native IParticleSystemQuery thunk hands over arrives as the managed query's
/// arguments, its answers come back in the native layout, each call counts its
/// crossing, and a failure answers -1 (the thunk's "unanswered").
/// </summary>
public unsafe class ParticleQueryBindingTests
{
    internal sealed class NoBindings : IClientParticleBindings
    {
        public int Frame(in ParticleFrameHeader header, ReadOnlySpan<ParticleFrameIn> inputs, float aabbBuffer, Span<ParticleCommit> commits) => 0;
        public int DrawQuadCount(uint owner, int channel) => 0;
        public int DrawBegin(in ParticleDrawIn input, out ParticleDrawOut output, Span<ParticleQuadRaw> quads)
        {
            output = default;
            return 0;
        }
        public void DrawEnd(uint owner, int channel, ReadOnlySpan<float> keys) { }
        public void IncrementFrameCode() { }
        public void PostRender() { }
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

    private sealed class NoSparks : IClientImpactSparks
    {
        public int ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle) => 0;
    }

    private sealed class NoView : IParticleViewSource
    {
        public ParticleView Read() => throw new InvalidOperationException("the query facts never draw");
    }

    internal sealed class Water : IClientWaterSplashes
    {
        public readonly List<string> Calls = [];

        public void Splash(int kind, Vector origin, Vector normal, float scale, Vector? color, float lifetime, float alpha)
            => Calls.Add($"{kind} {origin} {normal} {scale} {(color is { } c ? c.ToString() : "none")} {lifetime} {alpha}");

        public (Vector Tint, float Luminosity) Tint(Vector color) => (color * 2, color.X + 0.5f);
    }

    private static readonly Water s_water = new();

    private sealed class Engine(bool inGame, Vector light) : IParticleQueryEngine
    {
        public Vector? AskedAt;

        public bool IsInGame() => inGame;

        public Vector LightForPoint(Vector position, bool clamp)
        {
            AskedAt = position;
            return light;
        }
    }

    private sealed class Throwing : IClientParticleQuery
    {
        public int CollisionGroupFromName(string name) => throw new InvalidOperationException("boom");
        public (byte R, byte G, byte B, byte A) LightingAtPoint(Vector origin) => throw new InvalidOperationException("boom");
        public bool ShouldTrace() => throw new InvalidOperationException("boom");
        public void RandomPointsOnControllingObjectHitBox(Vector controlPoint, bool hasObject, Span<Vector> points,
                                                          Span<int> hitBoxIndices, Span<Vector> relativeCoords)
            => throw new InvalidOperationException("boom");
        public int ControllingObjectHitBoxInfo(bool hasObject) => throw new InvalidOperationException("boom");
    }

    private static (ParticlesClientBinding Binding, Crossings Crossings) Make(IClientParticleQuery query)
    {
        var crossings = new Crossings();
        return (new ParticlesClientBinding(crossings, new RecordingLog(), new NoBindings(), query, new NoFlecks(),
                                            new NoSparks(), s_water, new RainTracerGeometry()), crossings);
    }

    [Fact]
    public void A_collision_group_name_crosses_as_utf8_and_its_group_comes_back()
    {
        var (binding, crossings) = Make(new ParticleSystemQuery(new Engine(true, default)));
        byte[] name = "debris\0"u8.ToArray();
        fixed (byte* p = name)
            Assert.Equal((int)EngineCollisionGroup.Debris, binding.QueryCollisionGroup(p));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientParticleQueryCollisionGroup));
    }

    [Fact]
    public void The_light_is_asked_at_the_origin_native_passed_and_written_as_four_bytes()
    {
        var engine = new Engine(true, new Vector(0.0204874687f, 0.0422973633f, 0.0643514022f));
        var (binding, crossings) = Make(new ParticleSystemQuery(engine));
        float* origin = stackalloc float[3] { -3893.09f, 1541.9f, 64.83f };
        byte* rgba = stackalloc byte[4] { 9, 9, 9, 9 };

        Assert.Equal(1, binding.QueryLighting(origin, rgba));
        Assert.Equal(new Vector(-3893.09f, 1541.9f, 64.83f), engine.AskedAt);
        // A recorded stock answer (particlequery-stock.tsv): 5 10 16 0.
        Assert.Equal(new byte[] { 5, 10, 16, 0 }, new[] { rgba[0], rgba[1], rgba[2], rgba[3] });
        Assert.Equal(1, crossings.Read(CrossingOp.ClientParticleQueryLighting));
    }

    [Fact]
    public void The_trace_answer_is_one_in_game_and_zero_out_of_it()
    {
        float* start = stackalloc float[3];
        float* end = stackalloc float[3];
        Assert.Equal(1, Make(new ParticleSystemQuery(new Engine(true, default))).Binding.QueryTrace(start, end, 0x200400b, 1, 0));
        Assert.Equal(0, Make(new ParticleSystemQuery(new Engine(false, default))).Binding.QueryTrace(start, end, 0x200400b, 1, 0));
    }

    [Fact]
    public void Hit_box_points_fill_every_buffer_native_handed_over_and_answer_the_count()
    {
        var (binding, crossings) = Make(new ParticleSystemQuery(new Engine(true, default)));
        float* cp = stackalloc float[3] { 1, 2, 3 };
        float* points = stackalloc float[6] { -1, -1, -1, -1, -1, -1 };
        int* indices = stackalloc int[2] { 5, 5 };
        float* relative = stackalloc float[6] { 7, 7, 7, 7, 7, 7 };

        Assert.Equal(2, binding.QueryHitBoxPoints(1, cp, 0, 2, points, indices, relative));
        Assert.Equal(new float[] { 1, 2, 3, 1, 2, 3 }, new ReadOnlySpan<float>(points, 6).ToArray());
        Assert.Equal(new[] { 0, 0 }, new ReadOnlySpan<int>(indices, 2).ToArray());
        Assert.Equal(new float[6], new ReadOnlySpan<float>(relative, 6).ToArray());
        Assert.Equal(1, crossings.Read(CrossingOp.ClientParticleQueryHitBoxPoints));

        // The optional buffers may be absent, as the library may pass NULL.
        Assert.Equal(2, binding.QueryHitBoxPoints(1, cp, 0, 2, points, null, null));
    }

    [Fact]
    public void A_throwing_query_answers_minus_one_so_native_counts_it_unanswered()
    {
        var (binding, crossings) = Make(new Throwing());
        byte[] name = "NONE\0"u8.ToArray();
        float* v = stackalloc float[6];
        byte* rgba = stackalloc byte[4];
        fixed (byte* p = name)
            Assert.Equal(-1, binding.QueryCollisionGroup(p));
        Assert.Equal(-1, binding.QueryLighting(v, rgba));
        Assert.Equal(-1, binding.QueryTrace(v, v, 0, 0, 0));
        Assert.Equal(-1, binding.QueryHitBoxPoints(0, v, 0, 1, v + 3, null, null));
        Assert.Equal(-1, binding.QueryHitBoxInfo(0, 0, 128));
        Assert.Equal(1, crossings.Read(CrossingOp.ClientParticleQueryHitBoxInfo));
    }

    [Fact]
    public void An_impact_crosses_its_debris_and_dust_once_each()
    {
        var sprays = new NoFlecks();
        var crossings = new Crossings();
        var binding = new ParticlesClientBinding(crossings, new RecordingLog(), new NoBindings(), new NoQueryAtAll(), sprays,
                                                 new NoSparks(), new Water(), new RainTracerGeometry());
        var o = new Vector(1, 2, 3);
        var n = new Vector(0, 0, 1);
        var c = new Vector(0.5f, 0.25f, 1);
        Assert.Equal(7, binding.ImpactDebris(&o, &n, &c, 'C', 2, 1, 0.5f));
        Assert.Equal(5, binding.ImpactDust(&o, &n, &c, 1.5f, 0.75f));
        Assert.Equal(-1, binding.ImpactDebris(&o, null, &c, 'C', 1, 0, 1));
        Assert.Equal(-1, binding.ImpactDust(null, &n, &c, 1, 1));
        Assert.Equal([$"debris {o} {n} {c} 67 2 True 0.5", $"dust {o} {n} {c} 1.5 0.75"], sprays.Calls);
        Assert.Equal(2, crossings.Read(CrossingOp.ClientParticlesImpactDebris));
        Assert.Equal(2, crossings.Read(CrossingOp.ClientParticlesImpactDust));
    }

    [Fact]
    public void A_splash_crosses_its_kind_place_and_optional_colour()
    {
        var water = new Water();
        var crossings = new Crossings();
        var binding = new ParticlesClientBinding(crossings, new RecordingLog(), new NoBindings(), new NoQueryAtAll(), new NoFlecks(),
                                                 new NoSparks(), water, new RainTracerGeometry());
        var o = new Vector(1, 2, 3);
        var n = new Vector(0, 0, 1);
        var c = new Vector(0.5f, 0.25f, 1);
        Assert.Equal(1, binding.WaterSplash(0, &o, &n, 8, null, 0, 0));
        Assert.Equal(1, binding.WaterSplash(2, &o, null, 1.5f, &c, 1.5f, 0.75f));
        Assert.Equal(1, binding.WaterSplash(2, &o, null, 0.25f, null, 1, 1));
        Assert.Equal(-1, binding.WaterSplash(3, &o, &n, 1, null, 0, 0));
        Assert.Equal(-1, binding.WaterSplash(1, &o, null, 1, null, 0, 0));
        Assert.Equal([$"0 {o} {n} 8 none 0 0", $"2 {o} {default(Vector)} 1.5 {c} 1.5 0.75", $"2 {o} {default(Vector)} 0.25 none 1 1"], water.Calls);
        Assert.Equal(5, crossings.Read(CrossingOp.ClientParticlesWaterSplash));
    }

    [Fact]
    public void A_splash_tint_writes_only_what_was_asked()
    {
        var water = new Water();
        var crossings = new Crossings();
        var binding = new ParticlesClientBinding(crossings, new RecordingLog(), new NoBindings(), new NoQueryAtAll(), new NoFlecks(),
                                                 new NoSparks(), water, new RainTracerGeometry());
        var color = new Vector(1, 2, 3);
        Vector tint = default;
        float lum = 0;
        Assert.Equal(1, binding.SplashTint(&color, &tint, &lum));
        Assert.Equal((new Vector(2, 4, 6), 1.5f), (tint, lum));
        Assert.Equal(1, binding.SplashTint(&color, null, &lum));
        Assert.Equal(1, binding.SplashTint(&color, &tint, null));
        Assert.Equal(-1, binding.SplashTint(null, &tint, &lum));
        Assert.Equal(4, crossings.Read(CrossingOp.ClientParticlesSplashTint));
    }

    [Fact]
    public void A_batch_of_tracers_crosses_once_and_answers_each_ones_corners()
    {
        var crossings = new Crossings();
        var binding = new ParticlesClientBinding(crossings, new RecordingLog(), new NoBindings(), new NoQueryAtAll(), new NoFlecks(),
                                                 new NoSparks(), new Water(), new RainTracerGeometry());
        // In front of the eye; wholly behind it; in front again.
        float[] input = [1, 2, -50, 0, -10, -5, 0.5f,
                         0, 0, 10, 0, 0, 5, 1,
                         -3, 1, -20, 2, 0, -1, 2];
        var verts = new Vector[12];
        var valid = new byte[3];
        fixed (float* i = input)
        fixed (Vector* v = verts)
        fixed (byte* ok = valid)
            Assert.Equal(3, binding.TracerQuads(3, i, v, ok));
        Assert.Equal([1, 0, 1], valid);
        for (int k = 0; k < 3; k += 2)
        {
            var want = new Vector[4];
            Assert.True(TracerGeometry.ComputeVerts(new Vector(input[k * 7], input[k * 7 + 1], input[k * 7 + 2]),
                                                    new Vector(input[k * 7 + 3], input[k * 7 + 4], input[k * 7 + 5]), input[k * 7 + 6], want));
            Assert.Equal(want, verts[(k * 4)..(k * 4 + 4)]);
        }
        Assert.Equal(1, crossings.Read(CrossingOp.ClientParticlesTracerQuads));
        Assert.Equal(-1, binding.TracerQuads(1, null, null, null));
    }

    private sealed class NoQueryAtAll : IClientParticleQuery
    {
        public int CollisionGroupFromName(string name) => 0;
        public (byte R, byte G, byte B, byte A) LightingAtPoint(Vector origin) => default;
        public bool ShouldTrace() => false;
        public void RandomPointsOnControllingObjectHitBox(Vector controlPoint, bool hasObject, Span<Vector> points,
                                                         Span<int> hitBoxIndices, Span<Vector> relativeCoords) { }
        public int ControllingObjectHitBoxInfo(bool hasObject) => 0;
    }

    //-------------------------------------------------------------------------
    // ManifestFiles (C9 KeyValues K1): a manifest's file list, read in C#,
    // back NUL-separated in one BitBlock.
    //-------------------------------------------------------------------------

    private static ParticlesClientBinding WithManifests(Func<bool, string, List<string>?> files)
        => new(new Crossings(), new RecordingLog(), new NoBindings(), new ParticleSystemQuery(new Engine(true, default)),
               new NoFlecks(), new NoSparks(), s_water, new RainTracerGeometry(), files);

    [Fact]
    public unsafe void A_manifests_files_come_back_NUL_separated_in_one_block()
    {
        bool? askedMap = null;
        string? askedName = null;
        var binding = WithManifests((map, name) =>
        {
            askedMap = map;
            askedName = name;
            return new List<string> { "particles/a.pcf", "!particles/b.pcf" };
        });

        byte[] block = new byte[BitBlock.HeaderBytes + 64];
        byte[] name = System.Text.Encoding.UTF8.GetBytes("dm_lockdown\0");
        fixed (byte* b = block)
        fixed (byte* n = name)
            Assert.Equal(2, binding.ManifestFiles(1, n, b, block.Length));

        Assert.True(askedMap);
        Assert.Equal("dm_lockdown", askedName);
        string payload = System.Text.Encoding.UTF8.GetString(block, BitBlock.HeaderBytes, BitBlock.Bits(block) / 8);
        Assert.Equal("particles/a.pcf\0!particles/b.pcf\0", payload);
    }

    [Fact]
    public unsafe void No_manifest_answers_minus_one_and_a_short_block_minus_two()
    {
        byte[] block = new byte[BitBlock.HeaderBytes + 8];
        fixed (byte* b = block)
        {
            Assert.Equal(-1, WithManifests((_, _) => null).ManifestFiles(1, null, b, block.Length));
            Assert.Equal(-2, WithManifests((_, _) => new List<string> { "particles/too_long_for_eight.pcf" })
                .ManifestFiles(0, null, b, block.Length));
            Assert.Equal(0, WithManifests((_, _) => new List<string>()).ManifestFiles(0, null, b, block.Length));
            Assert.Equal(0, BitBlock.Bits(block));
        }
    }
}
