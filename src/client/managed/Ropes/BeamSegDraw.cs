//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>
/// One vertex of a camera-facing strip, as the native writer
/// (src/client/native/beam_strip_vertex.h) reads it: position, r g b a, the two
/// texture coordinates, tangent S, tangent T.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BeamSegVertex
{
    public Vector Pos;
    public float R, G, B, A;
    public float U0, V0, U1, V1;
    public Vector TangentS;
    public Vector TangentT;
}

/// <summary>
/// tier2's <c>CBeamSegDraw</c>: a strip of <see cref="BeamSeg"/> turned to face
/// the camera, two vertices a segment.
/// </summary>
public static class BeamSegDraw
{
    /// <summary>Floats in a <see cref="BeamSegVertex"/>.</summary>
    public const int VertexFloats = 17;

    /// <summary>
    /// Expands <paramref name="segs"/> seen from <paramref name="camera"/> into
    /// <paramref name="vertices"/>: two a segment, or none for fewer than two
    /// segments. Returns the count written.
    /// </summary>
    /// <remarks>
    /// Each segment's two points lie across the strip along a normal
    /// perpendicular to both the strip and the line of sight. The normal is
    /// the one for the run to the next segment, averaged with the previous
    /// run's from the third segment on; the last segment takes the last run's
    /// unaveraged. The arithmetic is spelt in the order tier2.a's object does
    /// it, including <see cref="NormalizeFast"/>'s, so the result matches it
    /// bit for bit (beamsegdraw_oracle_test, BeamSegDrawOracleTests).
    /// </remarks>
    public static int Expand(ReadOnlySpan<BeamSeg> segs, in Vector camera, Span<BeamSegVertex> vertices)
    {
        int n = segs.Length;
        if (n < 2)
            return 0;
        if (vertices.Length < n * 2)
            throw new ArgumentException("two vertices a segment", nameof(vertices));

        Vector last = default;
        for (int i = 1; i < n; i++)
        {
            Vector normal = RunNormal(camera, segs[i - 1].Pos, segs[i].Pos);
            Vector across = normal;
            if (i > 1)
            {
                across = new Vector((normal.X + last.X) * 0.5f, (normal.Y + last.Y) * 0.5f, (normal.Z + last.Z) * 0.5f);
                NormalizeFast(ref across);
            }
            last = normal;
            Specify(segs[i - 1], camera, across, vertices.Slice((i - 1) * 2, 2));
        }
        Specify(segs[n - 1], camera, last, vertices.Slice((n - 1) * 2, 2));
        return n * 2;
    }

    // Perpendicular to the run from start to next and to the sight line to start.
    private static Vector RunNormal(in Vector camera, in Vector start, in Vector next)
    {
        float tx = start.X - next.X, ty = start.Y - next.Y, tz = start.Z - next.Z;
        float dx = start.X - camera.X, dy = start.Y - camera.Y, dz = start.Z - camera.Z;
        var n = new Vector(ty * dz - tz * dy, tz * dx - tx * dz, tx * dy - ty * dx);
        NormalizeFast(ref n);
        return n;
    }

    private static void Specify(in BeamSeg seg, in Vector camera, in Vector normal, Span<BeamSegVertex> two)
    {
        float dx = seg.Pos.X - camera.X, dy = seg.Pos.Y - camera.Y, dz = seg.Pos.Z - camera.Z;
        var tangent = new Vector(dy * normal.Z - dz * normal.Y, dz * normal.X - dx * normal.Z, dx * normal.Y - dy * normal.X);
        NormalizeFast(ref tangent);

        float half = seg.Width * 0.5f;
        float other = seg.Width * -0.5f;

        for (int side = 0; side < 2; side++)
        {
            float scale = side == 0 ? half : other;
            two[side] = new BeamSegVertex
            {
                Pos = new Vector(seg.Pos.X + scale * normal.X, seg.Pos.Y + scale * normal.Y, seg.Pos.Z + scale * normal.Z),
                R = seg.Color.X,
                G = seg.Color.Y,
                B = seg.Color.Z,
                A = seg.Alpha,
                U0 = side,
                V0 = seg.TexCoord,
                U1 = side,
                V1 = seg.TexCoord,
                TangentS = normal,
                TangentT = tangent,
            };
        }
    }

    /// <summary>
    /// <c>VectorNormalizeFast</c> as tier2.a's object computes it: the squared
    /// length summed as <c>(z*z + 1e-10) + (x*x + y*y)</c>, which is not
    /// <see cref="MoveVec.Normalize"/>'s order and rounds differently, then
    /// <see cref="MoveVec.RSqrt"/>.
    /// </summary>
    private static void NormalizeFast(ref Vector v)
    {
        float sqrlen = (v.Z * v.Z + 1.0e-10f) + (v.X * v.X + v.Y * v.Y);
        float inv = MoveVec.RSqrt(sqrlen);
        v = new Vector(v.X * inv, v.Y * inv, v.Z * inv);
    }
}
