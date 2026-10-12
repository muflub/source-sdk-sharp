using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One CNewParticleEffect the client started, with its system's name.</summary>
public readonly record struct ClientParticleSystemRow(string Name, ParticleSystemInfo Info);

/// <summary>
/// The CNewParticleEffect instances the client started, read through
/// IShimParticlesClient (plan_client_novalve §4.4, the particles route; the
/// particles shell, shell_particles_client.cpp). Game thread.
/// </summary>
public interface IClientParticleSystems
{
    /// <summary>Every instance the particles shell watches, in the order it started them.</summary>
    IReadOnlyList<ClientParticleSystemRow> Read();

    /// <summary>Full reads of the particle manifest's definitions since the process started.</summary>
    int DefinitionReads();

    /// <summary>
    /// The particle query's calls that found no managed answer, per method:
    /// collision group, lighting, trace, hit-box points, hit-box info. Empty
    /// when the read is not wired.
    /// </summary>
    IReadOnlyList<int> QueryUnanswered();
}

/// <summary>The real one, over the client's shim areas.</summary>
internal sealed unsafe class ClientParticleSystems(EngineSide api) : IClientParticleSystems
{
    private const int NameBytes = 128;

    /// <summary>More than a frame can start; a count past it is a native bug, not a reason to allocate.</summary>
    private const int MaxRows = 4096;

    public IReadOnlyList<ClientParticleSystemRow> Read()
    {
        if (!api.ClientAttached)
            return [];
        int count = Math.Min(api.ClientShims.ParticleSystemsRead(-1, null, null, 0), MaxRows);
        var rows = new List<ClientParticleSystemRow>(Math.Max(count, 0));
        byte* name = stackalloc byte[NameBytes];
        ParticleSystemInfo info;
        for (int i = 0; i < count; i++)
        {
            name[0] = 0;
            info = default;
            if (api.ClientShims.ParticleSystemsRead(i, &info, name, NameBytes) <= i)
                break;
            rows.Add(new ClientParticleSystemRow(Text(name), info));
        }
        return rows;
    }

    public int DefinitionReads() => api.ClientAttached ? api.ClientShims.ParticleDefinitionReads() : -1;

    public IReadOnlyList<int> QueryUnanswered()
    {
        if (!api.ClientAttached)
            return [];
        int* counts = stackalloc int[8];
        int n = api.ClientShims.ParticleQueryUnanswered(counts, 8);
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

/// <summary>
/// The particles shell's starts for a dispatched particle effect (lane
/// c5-particles): a system's name by its string-table index, and a one-shot
/// started at a world point with the control points the dispatch named.
/// </summary>
public interface IClientParticleOneShots
{
    /// <summary><c>GetParticleSystemNameFromIndex</c>: the ParticleEffectNames table's string, null with no table.</summary>
    string? NameFromIndex(int index);

    /// <summary>
    /// <c>CNewParticleEffect::Create( NULL, name )</c>, its sort origin, the
    /// control points in order, and control point 0's orientation; false when
    /// no definition answers the name.
    /// </summary>
    bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points,
               Vector forward, Vector right, Vector up);
}

/// <summary>One control point a dispatch sets: its number and position.</summary>
public readonly record struct ParticleControlPoint(int Point, Vector Position);

/// <summary>The real one, over IShimParticlesClient.</summary>
internal sealed unsafe class ClientParticleOneShots(EngineSide api) : IClientParticleOneShots
{
    private const int NameBytes = 256;

    public string? NameFromIndex(int index)
    {
        if (!api.ClientAttached)
            return null;
        byte* name = stackalloc byte[NameBytes];
        int n = api.ClientShims.ParticleSystemNameFromIndex(index, name, NameBytes);
        return n < 0 ? null : Encoding.UTF8.GetString(name, Math.Min(n, NameBytes - 1));
    }

    public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points,
                      Vector forward, Vector right, Vector up)
    {
        if (!api.ClientAttached)
            return false;
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
            return api.ClientShims.ParticleSystemOneShot(p, sort, flat, points.Length, orient) != 0;
    }
}

/// <summary>A world trace's answer, as much of <c>trace_t</c> as the particle collision reads.</summary>
public struct ParticleTrace
{
    public float Fraction;
    public Vector EndPos;
    public Vector Normal;
    public float Dist;
    public bool AllSolid;
    public bool StartSolid;
}

/// <summary>
/// <c>UTIL_TraceLine( start, end, MASK_SOLID_BRUSHONLY, NULL,
/// COLLISION_GROUP_NONE )</c>: the only trace the particle collision makes.
/// </summary>
public interface IParticleWorldTrace
{
    ParticleTrace TraceLine(Vector start, Vector end);
}

/// <summary>What a masked line found: fraction, end, the plane's normal, and stock's startsolid / fractionleftsolid.</summary>
public readonly record struct MaskTrace(float Fraction, Vector End, Vector Normal, bool StartSolid, float FractionLeftSolid);

/// <summary><c>UTIL_TraceLine( start, end, mask, NULL, COLLISION_GROUP_NONE )</c> with the contents mask the caller names.</summary>
public interface IParticleMaskTrace
{
    /// <summary><c>CONTENTS_SOLID</c>.</summary>
    public const uint ContentsSolid = 0x1;

    /// <summary><c>MASK_WATER</c>: <c>CONTENTS_WATER | CONTENTS_MOVEABLE | CONTENTS_SLIME</c>.</summary>
    public const uint MaskWater = 0x20 | 0x4000 | 0x10;

    MaskTrace Trace(Vector start, Vector end, uint mask);
}

/// <summary>The real one, over IShimParticlesClient::ParticlesWorldTrace and ParticlesTraceMask.</summary>
internal sealed unsafe class ClientParticleWorldTrace(EngineSide api) : IParticleWorldTrace, IParticleMaskTrace
{
    public MaskTrace Trace(Vector start, Vector end, uint mask)
    {
        if (!api.ClientAttached)
            return new MaskTrace(1, end, default, false, 0);
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[9];
        if (api.ClientShims.ParticlesTraceMask(s, e, mask, o, 9) < 9)
            return new MaskTrace(1, end, default, false, 0);
        return new MaskTrace(o[0], new Vector(o[1], o[2], o[3]), new Vector(o[4], o[5], o[6]), o[7] != 0, o[8]);
    }

    public ParticleTrace TraceLine(Vector start, Vector end)
    {
        if (!api.ClientAttached)
            return new ParticleTrace { Fraction = 1 };
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* o = stackalloc float[10];
        if (api.ClientShims.ParticlesWorldTrace(s, e, o, 10) < 10)
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
/// The view the particle manager is drawing (lane c5-particles): its
/// model-view matrix's three rows, row major (<c>ParticleMgr()-&gt;GetModelView()</c>),
/// <c>CurrentViewID()</c>, and the view's origin and forward.
/// </summary>
public readonly struct ParticleView
{
    private readonly float[] _m;

    public ParticleView(ReadOnlySpan<float> modelView12, int viewId, Vector origin = default, Vector forward = default)
    {
        _m = modelView12[..12].ToArray();
        ViewId = viewId;
        Origin = origin;
        Forward = forward;
    }

    public int ViewId { get; }

    /// <summary><c>CurrentViewOrigin()</c>.</summary>
    public Vector Origin { get; }

    /// <summary><c>CurrentViewForward()</c>.</summary>
    public Vector Forward { get; }

    public bool IsSet => _m is not null;

    /// <summary>One element of the matrix: row <paramref name="r"/> (0..2), column <paramref name="c"/> (0..3).</summary>
    public float this[int r, int c] => _m[r * 4 + c];

    /// <summary><c>TransformParticle</c> (particlemgr.h): a point into view space.</summary>
    public Vector TransformPoint(Vector v) => new(
        _m[0] * v.X + _m[1] * v.Y + _m[2] * v.Z + _m[3],
        _m[4] * v.X + _m[5] * v.Y + _m[6] * v.Z + _m[7],
        _m[8] * v.X + _m[9] * v.Y + _m[10] * v.Z + _m[11]);

    /// <summary><c>Vector3DMultiply</c> (vmatrix.cpp): a direction into view space.</summary>
    public Vector TransformDirection(Vector v) => new(
        _m[0] * v.X + _m[1] * v.Y + _m[2] * v.Z,
        _m[4] * v.X + _m[5] * v.Y + _m[6] * v.Z,
        _m[8] * v.X + _m[9] * v.Y + _m[10] * v.Z);
}

/// <summary>Reads the view the particle manager is drawing (IShimParticlesClient::ParticlesView).</summary>
public interface IParticleViewSource
{
    ParticleView Read();
}

/// <summary><c>PixelVisibility_FractionVisible</c> at a point through the caller's query handle.</summary>
public interface IParticlePixelVisibility
{
    float FractionVisible(Vector origin, ref int queryHandle);
}

/// <summary><c>g_pParticleSystemMgr-&gt;ParticleThrottleScaling()</c>, asked of the particle library: 1 unrestricted .. 0 fully.</summary>
public interface IParticleThrottle
{
    float ThrottleScaling();
}

/// <summary>
/// The real ones, over IShimParticlesClient's view and visibility slots; the
/// throttle is the particle library's own answer (plan_particles_so.md).
/// </summary>
internal sealed unsafe class ClientParticleViewSlots(EngineSide api, IParticleLibrary library)
    : IParticleViewSource, IParticlePixelVisibility, IParticleThrottle
{
    public float ThrottleScaling() => api.ClientAttached ? library.ThrottleScaling() : 1.0f;

    public ParticleView Read()
    {
        float* m = stackalloc float[12];
        new Span<float>(m, 12).Clear();
        float* of = stackalloc float[6];
        new Span<float>(of, 6).Clear();
        int viewId = 0;
        if (api.ClientAttached)
            api.ClientShims.ParticlesView(m, &viewId, of);
        return new ParticleView(new ReadOnlySpan<float>(m, 12), viewId, new Vector(of[0], of[1], of[2]), new Vector(of[3], of[4], of[5]));
    }

    public float FractionVisible(Vector origin, ref int queryHandle)
    {
        if (!api.ClientAttached)
            return 0.0f;
        float* o = stackalloc float[3] { origin.X, origin.Y, origin.Z };
        int handle = queryHandle;
        float visible = api.ClientShims.ParticlesPixelVisible(o, &handle);
        queryHandle = handle;
        return visible;
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

/// <summary>
/// The contents at a point inside a managed brush entity's own brush
/// (IShimParticlesClient::ParticlesBrushPointContents): the engine's point test
/// over the owner as stock's SOLID_BSP dust volume (lane c5-particles: the dust
/// motes' spawn test). 0 when nothing answers.
/// </summary>
public interface IClientBrushContents
{
    int PointContents(EntityHandle owner, Vector point);
}

/// <summary>The real one, over the client's shim areas.</summary>
internal sealed unsafe class ClientBrushContents(EngineSide api) : IClientBrushContents
{
    public int PointContents(EntityHandle owner, Vector point)
    {
        if (!api.ClientAttached)
            return 0;
        float* p = stackalloc float[3] { point.X, point.Y, point.Z };
        return api.ClientShims.ParticlesBrushPointContents(owner.Value, p);
    }
}
