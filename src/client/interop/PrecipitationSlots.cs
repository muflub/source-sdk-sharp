using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The local player as native reads it for the weather.</summary>
public readonly record struct PrecipitationPlayerRead(Vector Origin, Vector Eye, float Yaw, Vector Center, float Fov, int DefaultFov);

/// <summary>
/// IShimPrecipitationClient: the engine reads the client weather makes
/// (shell_precipitation_client.cpp). Each is one engine call.
/// </summary>
public interface IClientPrecipitationEngine
{
    /// <summary><c>UTIL_TraceLine</c> through CTraceFilterSimple; the local player ignored when asked.</summary>
    bool TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer, out float fraction, out Vector endPos, out ushort surfaceFlags);

    /// <summary><c>UTIL_TraceModel</c> against the owner's brush.</summary>
    bool TraceVolume(EntityHandle owner, in Vector start, in Vector end, in Vector hullMin, in Vector hullMax,
                     out bool hit, out float fraction, out bool startSolid, out Vector endPos);

    /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
    int PointContents(in Vector position);

    /// <summary><c>IsRenderableInPVS</c> for the owner.</summary>
    bool InPvs(EntityHandle owner);

    /// <summary>The local player, or null when there is none.</summary>
    PrecipitationPlayerRead? Player();

    /// <summary>The world entity's bounds.</summary>
    (Vector Mins, Vector Maxs) WorldBounds();

    /// <summary><c>ScreenWidth()</c>.</summary>
    int ScreenWidth();
}

internal sealed unsafe class ClientPrecipitationSlots(EngineSide api) : IClientPrecipitationEngine
{
    public bool TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer, out float fraction, out Vector endPos, out ushort surfaceFlags)
    {
        fraction = 1.0f;
        endPos = end;
        surfaceFlags = 0;
        if (!api.ClientAttached)
            return false;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* o = stackalloc float[5];
        if (api.ClientShims.PrecipitationTraceLine(s, e, unchecked((int)mask), ignorePlayer ? 1 : 0, o) < 5)
            return false;
        fraction = o[0];
        endPos = new Vector(o[1], o[2], o[3]);
        surfaceFlags = (ushort)o[4];
        return true;
    }

    public bool TraceVolume(EntityHandle owner, in Vector start, in Vector end, in Vector hullMin, in Vector hullMax,
                            out bool hit, out float fraction, out bool startSolid, out Vector endPos)
    {
        hit = false;
        fraction = 1.0f;
        startSolid = false;
        endPos = end;
        if (!api.ClientAttached)
            return false;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[] { hullMin.X, hullMin.Y, hullMin.Z };
        float* hi = stackalloc float[] { hullMax.X, hullMax.Y, hullMax.Z };
        float* o = stackalloc float[6];
        if (api.ClientShims.PrecipitationTraceVolume(owner.Value, s, e, lo, hi, o) < 6)
            return false;
        hit = o[0] != 0;
        fraction = o[1];
        startSolid = o[2] != 0;
        endPos = new Vector(o[3], o[4], o[5]);
        return true;
    }

    public int PointContents(in Vector position)
    {
        if (!api.ClientAttached)
            return 0;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return api.ClientShims.PrecipitationPointContents(p);
    }

    public bool InPvs(EntityHandle owner) => api.ClientAttached && api.ClientShims.PrecipitationInPvs(owner.Value) != 0;

    public PrecipitationPlayerRead? Player()
    {
        if (!api.ClientAttached)
            return null;
        float* o = stackalloc float[12];
        if (api.ClientShims.PrecipitationPlayer(o) == 0)
            return null;
        return new PrecipitationPlayerRead(new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]), o[6],
                                           new Vector(o[7], o[8], o[9]), o[10], (int)o[11]);
    }

    public (Vector Mins, Vector Maxs) WorldBounds()
    {
        if (!api.ClientAttached)
            return default;
        float* o = stackalloc float[6];
        api.ClientShims.PrecipitationWorldBounds(o);
        return (new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]));
    }

    public int ScreenWidth() => api.ClientAttached ? api.ClientShims.PrecipitationScreenWidth() : 0;
}

/// <summary>What the render call lands on.</summary>
public interface IClientPrecipitationRenderer
{
    /// <summary>
    /// The plan for one render pass: <paramref name="view"/> is the world-to-view
    /// matrix (16, row major), the view forward and origin, the view id and the
    /// draw flags. Writes tracers (10 floats each) and splashes (6 each);
    /// answers the tracer count.
    /// </summary>
    int Render(ReadOnlySpan<float> view, Span<float> tracers, Span<float> splashes, out int splashCount);
}
