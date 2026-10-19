using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

internal sealed unsafe class ClientPrecipitationSlots(PrecipitationClientArea? precipitationClient) : IClientPrecipitationEngine
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private PrecipitationClientArea PrecipitationClient => precipitationClient ?? throw ShimAreaGuard.NotAttached(nameof(PrecipitationClient));

    public bool TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer, out float fraction, out Vector endPos, out ushort surfaceFlags)
    {
        fraction = 1.0f;
        endPos = end;
        surfaceFlags = 0;
        if (precipitationClient is null)
            return false;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* o = stackalloc float[5];
        if (PrecipitationClient.PrecipitationTraceLine(s, e, unchecked((int)mask), ignorePlayer ? 1 : 0, o) < 5)
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
        if (precipitationClient is null)
            return false;
        float* s = stackalloc float[] { start.X, start.Y, start.Z };
        float* e = stackalloc float[] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[] { hullMin.X, hullMin.Y, hullMin.Z };
        float* hi = stackalloc float[] { hullMax.X, hullMax.Y, hullMax.Z };
        float* o = stackalloc float[6];
        if (PrecipitationClient.PrecipitationTraceVolume(owner.Value, s, e, lo, hi, o) < 6)
            return false;
        hit = o[0] != 0;
        fraction = o[1];
        startSolid = o[2] != 0;
        endPos = new Vector(o[3], o[4], o[5]);
        return true;
    }

    public int PointContents(in Vector position)
    {
        if (precipitationClient is null)
            return 0;
        float* p = stackalloc float[] { position.X, position.Y, position.Z };
        return PrecipitationClient.PrecipitationPointContents(p);
    }

    public bool InPvs(EntityHandle owner) => (precipitationClient is not null) && PrecipitationClient.PrecipitationInPvs(owner.Value) != 0;

    public PrecipitationPlayerRead? Player()
    {
        if (precipitationClient is null)
            return null;
        float* o = stackalloc float[12];
        if (PrecipitationClient.PrecipitationPlayer(o) == 0)
            return null;
        return new PrecipitationPlayerRead(new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]), o[6],
                                           new Vector(o[7], o[8], o[9]), o[10], (int)o[11]);
    }

    public (Vector Mins, Vector Maxs) WorldBounds()
    {
        if (precipitationClient is null)
            return default;
        float* o = stackalloc float[6];
        PrecipitationClient.PrecipitationWorldBounds(o);
        return (new Vector(o[0], o[1], o[2]), new Vector(o[3], o[4], o[5]));
    }

    public int ScreenWidth() => (precipitationClient is not null) ? PrecipitationClient.PrecipitationScreenWidth() : 0;
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
