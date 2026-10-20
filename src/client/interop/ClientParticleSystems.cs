using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The real one, over the client's shim areas.</summary>
internal sealed unsafe class ClientParticleSystems(IParticlesClientArea particlesClient) : IClientParticleSystems
{
    private const int NameBytes = 128;

    /// <summary>More than a frame can start; a count past it is a native bug, not a reason to allocate.</summary>
    private const int MaxRows = 4096;

    public IReadOnlyList<ClientParticleSystemRow> Read()
    {
        int count = Math.Min(particlesClient.ParticleSystemsRead(-1, null, null, 0), MaxRows);
        var rows = new List<ClientParticleSystemRow>(Math.Max(count, 0));
        byte* name = stackalloc byte[NameBytes];
        ParticleSystemInfo info;
        for (int i = 0; i < count; i++)
        {
            name[0] = 0;
            info = default;
            if (particlesClient.ParticleSystemsRead(i, &info, name, NameBytes) <= i)
                break;
            rows.Add(new ClientParticleSystemRow(Text(name), info));
        }
        return rows;
    }

    public int DefinitionReads() => particlesClient.ParticleDefinitionReads();

    public IReadOnlyList<int> QueryUnanswered()
    {
        int* counts = stackalloc int[8];
        int n = particlesClient.ParticleQueryUnanswered(counts, 8);
        var list = new int[Math.Clamp(n, 0, 8)];
        for (int i = 0; i < list.Length; i++)
            list[i] = counts[i];
        return list;
    }

    private static string Text(byte* bytes)
    {
        int n = 0;
        while (n < NameBytes && bytes[n] != 0)
            n++;
        return Encoding.UTF8.GetString(bytes, n);
    }
}

/// <summary>The real one, over IShimParticlesClient.</summary>
internal sealed unsafe class ClientParticleOneShots(IParticlesClientArea particlesClient) : IClientParticleOneShots
{
    private const int NameBytes = 256;

    public string? NameFromIndex(int index)
    {
        byte* name = stackalloc byte[NameBytes];
        int n = particlesClient.ParticleSystemNameFromIndex(index, name, NameBytes);
        return n < 0 ? null : Encoding.UTF8.GetString(name, Math.Min(n, NameBytes - 1));
    }

    public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points,
                      Vector forward, Vector right, Vector up)
    {
        byte[] bytes = Vtable.Utf8(name);
        float* sort = stackalloc float[3] { sortOrigin.X, sortOrigin.Y, sortOrigin.Z };
        float* flat = stackalloc float[Math.Max(points.Length, 1) * 4];
        for (int i = 0; i < points.Length; i++)
        {
            flat[i * 4] = points[i].Point;
            flat[i * 4 + 1] = points[i].Position.X;
            flat[i * 4 + 2] = points[i].Position.Y;
            flat[i * 4 + 3] = points[i].Position.Z;
        }
        float* orient = stackalloc float[9] { forward.X, forward.Y, forward.Z, right.X, right.Y, right.Z, up.X, up.Y, up.Z };
        fixed (byte* p = bytes)
            return particlesClient.ParticleSystemOneShot(p, sort, flat, points.Length, orient) != 0;
    }
}

/// <summary>The real one, over IShimParticlesClient::ParticlesWorldTrace and ParticlesTraceMask.</summary>
internal sealed unsafe class ClientParticleWorldTrace(IParticlesClientArea particlesClient) : IParticleWorldTrace, IParticleMaskTrace
{
    public MaskTrace Trace(Vector start, Vector end, uint mask)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[9];
        if (particlesClient.ParticlesTraceMask(s, e, mask, o, 9) < 9)
            return new MaskTrace(1, end, default, false, 0);
        return new MaskTrace(o[0], new Vector(o[1], o[2], o[3]), new Vector(o[4], o[5], o[6]), o[7] != 0, o[8]);
    }

    public ParticleTrace TraceLine(Vector start, Vector end)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[10];
        if (particlesClient.ParticlesWorldTrace(s, e, o, 10) < 10)
            return new ParticleTrace { Fraction = 1 };
        return new ParticleTrace
        {
            Fraction = o[0], EndPos = new Vector(o[1], o[2], o[3]), Normal = new Vector(o[4], o[5], o[6]),
            Dist = o[7], AllSolid = o[8] != 0, StartSolid = o[9] != 0,
        };
    }
}

/// <summary>
/// The impact sprays (lane c5-particles): shell_impact_effects.cpp's
/// FX_DebrisFlecks and FX_DustImpact, asked once per impact with what only the
/// engine knows (the trace's surface colour, the particle throttle).
/// </summary>
public interface IClientImpactSprays
{
    /// <summary><c>FX_DebrisFlecks</c>: the flecks and the dust behind them. The particles started.</summary>
    int Debris(Vector endPos, Vector normal, Vector color, int material, int scale, bool noFlecks, float throttle);

    /// <summary><c>FX_DustImpact</c>. The particles started.</summary>
    int Dust(Vector origin, Vector normal, Vector color, float scale, float throttle);
}

/// <summary>
/// An impact's metal spark (lane c5-particles): ApplyImpactChoice's metal arm,
/// the shot reflected off the surface and jittered, then FX_MetalSpark; asked
/// once per metal impact by shell_impact_effects.cpp.
/// </summary>
/// <summary><c>Tracer_ComputeVerts</c>: a view-space tracer's four corners, false when nothing of it draws.</summary>
public interface IClientTracerGeometry
{
    bool ComputeVerts(Vector start, Vector delta, float width, Span<Vector> verts);
}

public interface IClientWaterSplashes
{
    /// <summary>
    /// fx_water's three routines native code still calls: 0 <c>FX_GunshotSplash</c>,
    /// 1 <c>FX_GunshotSlimeSplash</c>, 2 <c>FX_WaterRipple</c> (its colour optional).
    /// </summary>
    void Splash(int kind, Vector origin, Vector normal, float scale, Vector? color, float lifetime, float alpha);

    /// <summary><c>UTIL_GetNormalizedColorTintAndLuminosity</c>.</summary>
    (Vector Tint, float Luminosity) Tint(Vector color);
}

public interface IClientImpactSparks
{
    /// <summary>The sparks started.</summary>
    int ImpactMetalSpark(Vector origin, Vector shotDir, Vector normal, int scale, float throttle);
}

/// <summary>
/// Building a <see cref="ParticleView"/> and reading points through it.
///
/// <para>
/// Here rather than on the view since plan_shim_areas_split phase 4: the view
/// is in a seam's signature (<see cref="IParticleViewSource.Read"/>), so it
/// lives with the seam in an interface assembly, which holds only data.
/// </para>
/// </summary>
public static class ParticleViewReading
{
    /// <summary>A view over a copy of the first twelve floats of <paramref name="modelView12"/>.</summary>
    public static ParticleView From(ReadOnlySpan<float> modelView12, int viewId, Vector origin = default, Vector forward = default)
        => new() { ModelView = modelView12[..12].ToArray(), ViewId = viewId, Origin = origin, Forward = forward };

    /// <summary>Whether the view holds a matrix: false for <c>default</c>.</summary>
    public static bool IsSet(this in ParticleView view) => view.ModelView is not null;

    /// <summary><c>TransformParticle</c> (particlemgr.h): a point into view space.</summary>
    public static Vector TransformPoint(this in ParticleView view, Vector v)
    {
        float[] m = view.ModelView!;
        return new(
            m[0] * v.X + m[1] * v.Y + m[2] * v.Z + m[3],
            m[4] * v.X + m[5] * v.Y + m[6] * v.Z + m[7],
            m[8] * v.X + m[9] * v.Y + m[10] * v.Z + m[11]);
    }

    /// <summary><c>Vector3DMultiply</c> (vmatrix.cpp): a direction into view space.</summary>
    public static Vector TransformDirection(this in ParticleView view, Vector v)
    {
        float[] m = view.ModelView!;
        return new(
            m[0] * v.X + m[1] * v.Y + m[2] * v.Z,
            m[4] * v.X + m[5] * v.Y + m[6] * v.Z,
            m[8] * v.X + m[9] * v.Y + m[10] * v.Z);
    }
}

/// <summary>
/// The real ones, over the particles area's view slot; the throttle is the
/// particle library's own answer (plan_particles_so.md). The glows' pixel
/// visibility is managed's (C7, ParticlePixelVisibility).
/// </summary>
internal sealed unsafe class ClientParticleViewSlots(IParticlesClientArea particlesClient, IParticleLibrary library)
    : IParticleViewSource, IParticleThrottle
{
    public float ThrottleScaling() => library.ThrottleScaling();

    public ParticleView Read()
    {
        float* m = stackalloc float[12];
        new Span<float>(m, 12).Clear();
        float* of = stackalloc float[6];
        new Span<float>(of, 6).Clear();
        int viewId = 0;
        particlesClient.ParticlesView(m, &viewId, of);
        return ParticleViewReading.From(new ReadOnlySpan<float>(m, 12), viewId, new Vector(of[0], of[1], of[2]), new Vector(of[3], of[4], of[5]));
    }
}

/// <summary>
/// The view each executor draw is in, read once per view (lane c5-particles):
/// native hands every <c>IManagedParticlesClient::Draw</c> a key of the view it
/// is drawing (its model-view matrix and view id); the first draw that needs
/// the view under a new key reads it, the rest of that view's draws reuse it.
/// A frame costs one read per view drawn, however many effects draw.
/// </summary>
public sealed class ParticleViews(IParticleViewSource source)
{
    /// <summary>The FP mode the executor's simulate calls run under (sampled per call).</summary>
    public ParticleFloatMode FloatMode { get; } = new();

    private int _key;
    private int _readKey;
    private bool _read;
    private ParticleView _view;

    /// <summary>Views read since start (the crossings this costs).</summary>
    public int Reads { get; private set; }

    /// <summary>The key of the view being drawn now (set by the binding before each draw).</summary>
    public int Key
    {
        get => _key;
        set => _key = value;
    }

    /// <summary>The view being drawn: read when the key changed since the last read.</summary>
    public ParticleView Current
    {
        get
        {
            if (!_read || _readKey != _key)
            {
                _view = source.Read();
                _readKey = _key;
                _read = true;
                Reads++;
            }
            return _view;
        }
    }
}

/// <summary>
/// The floating-point mode the managed particle steps run under, measured on
/// the thread that runs them (lane c5-particles). Stock's particle code ran
/// under the engine's MXCSR flush-to-zero (FTZ: a denormal RESULT is zero)
/// and denormals-are-zero (DAZ: a denormal INPUT is read as zero); the oracle
/// facts compare a denormal as its signed zero on that ground. Managed code
/// called on the engine's thread inherits that thread's MXCSR, so the game's
/// steps flush as stock's did; this is the live evidence.
/// </summary>
public sealed class ParticleFloatMode
{
    // Instance fields, read through a method the JIT may not inline, so the
    // products are computed at run time under the thread's MXCSR, not folded.
    private float _minNormal = BitConverter.Int32BitsToSingle(0x00800000);
    private float _half = 0.5f;
    private float _minDenormal = BitConverter.Int32BitsToSingle(1);
    private float _two = 2.0f;

    /// <summary>Samples taken, and how many found FTZ and DAZ on.</summary>
    public long Samples { get; private set; }
    public long FlushToZero { get; private set; }
    public long DenormalsAreZero { get; private set; }

    /// <summary>One measurement: (a denormal result flushed, a denormal input read as zero).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public (bool Ftz, bool Daz) Measure()
    {
        float result = _minNormal * _half;      // 2^-127: a denormal unless FTZ flushes it
        float input = _minDenormal * _two;      // 2^-148 unless DAZ reads the input as 0
        return (result == 0.0f, input == 0.0f);
    }

    public void Sample()
    {
        var (ftz, daz) = Measure();
        Samples++;
        if (ftz)
            FlushToZero++;
        if (daz)
            DenormalsAreZero++;
    }
}

/// <summary>The real one, over the client's shim areas.</summary>
internal sealed unsafe class ClientBrushContents(IParticlesClientArea particlesClient) : IClientBrushContents
{
    public int PointContents(EntityHandle owner, Vector point)
    {
        float* p = stackalloc float[3] { point.X, point.Y, point.Z };
        return particlesClient.ParticlesBrushPointContents(owner.Value, p);
    }
}
