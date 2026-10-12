//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// c_tracer.cpp's geometry: a tracer's four view-space corners, from its
/// view-space start and delta, clipped against the near plane (z = 0) and
/// widened across the line of sight. Replayed against
/// <c>Fixtures/tracer-stock.tsv.gz</c> (lane c5-particles); the spark and
/// trail ports draw their stretched particles with it.
/// </summary>
public static class TracerGeometry
{
    /// <summary>
    /// <c>Tracer_ComputeVerts</c>: false when the tracer is wholly behind the
    /// eye, too short to clip, or seen end-on; else the corners in stock's
    /// order (start minus, start plus, end minus, end plus the half width).
    /// </summary>
    public static bool ComputeVerts(Vector start, Vector delta, float width, Span<Vector> verts)
    {
        if (Clip(start, delta, out Vector clippedStart, out Vector clippedDelta))
            return false;

        Vector normal = new(
            clippedDelta.Y * clippedStart.Z - clippedDelta.Z * clippedStart.Y,
            clippedDelta.Z * clippedStart.X - clippedDelta.X * clippedStart.Z,
            clippedDelta.X * clippedStart.Y - clippedDelta.Y * clippedStart.X);
        float sqLength = normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z;
        if (sqLength < 1e-3)
            return false;

        normal = normal * (0.5f * width / MathF.Sqrt(sqLength));
        verts[0] = clippedStart - normal;
        verts[1] = clippedStart + normal;
        verts[2] = verts[0] + clippedDelta;
        verts[3] = verts[1] + clippedDelta;
        return true;
    }

    /// <summary><c>ClipTracer</c>: true when nothing is left in front of the eye.</summary>
    private static bool Clip(Vector start, Vector delta, out Vector clippedStart, out Vector clippedDelta)
    {
        float dist1 = -start.Z;
        float dist2 = dist1 - delta.Z;
        clippedStart = default;
        clippedDelta = default;
        if (dist1 <= 0 && dist2 <= 0)
            return true;

        clippedStart = start;
        clippedDelta = delta;
        if (dist1 <= 0 || dist2 <= 0)
        {
            float fraction = dist2 - dist1;
            if (fraction < 1e-3 && fraction > -1e-3)
                return true;
            fraction = -dist1 / fraction;
            if (dist1 <= 0)
                clippedStart = new Vector(start.X + fraction * delta.X, start.Y + fraction * delta.Y, start.Z + fraction * delta.Z);
            else
                clippedDelta = delta * fraction;
        }
        return false;
    }
}

/// <summary>The tracer geometry as the rain's batched crossing asks for it (lane c5-particles).</summary>
public sealed class RainTracerGeometry : IClientTracerGeometry
{
    public bool ComputeVerts(Vector start, Vector delta, float width, Span<Vector> verts)
        => TracerGeometry.ComputeVerts(start, delta, width, verts);
}
