//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The mathlib and collision helpers the client-side effects use, spelt as
/// mathlib spells them, term for term and in the same order: the effect list's
/// meshes are compared against stock's recorded vertices bit for bit, so a
/// rearranged sum is a different answer. Pure functions; no state.
/// </summary>
internal static class FxMath
{
    /// <summary><c>CrossProduct( a, b, result )</c>.</summary>
    public static Vector Cross(in Vector a, in Vector b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary><c>DotProduct</c>.</summary>
    public static float Dot(in Vector a, in Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary><c>VectorMA( start, scale, direction, dest )</c>: the scale is a float parameter.</summary>
    public static Vector MA(in Vector start, float scale, in Vector direction)
        => new(start.X + scale * direction.X, start.Y + scale * direction.Y, start.Z + scale * direction.Z);

    /// <summary><c>a - b</c>, component by component.</summary>
    public static Vector Sub(in Vector a, in Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary><c>a + b</c>, component by component.</summary>
    public static Vector Add(in Vector a, in Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary><c>Vector::operator*( float )</c>.</summary>
    public static Vector Mul(in Vector a, float f) => new(a.X * f, a.Y * f, a.Z * f);

    /// <summary><c>VectorNormalize</c>, the SSE arm (MoveVec.Normalize): returns stock's value.</summary>
    public static float Normalize(ref Vector v) => MoveVec.Normalize(ref v);

    /// <summary><c>clamp( val, min, max )</c> as tier0's template writes it.</summary>
    public static float Clamp(float val, float min, float max)
    {
        if (max < min)
            return max;
        if (val < min)
            return min;
        if (val > max)
            return max;
        return val;
    }

    /// <summary><c>MAX( a, b )</c>: <c>(a &gt; b) ? a : b</c>.</summary>
    public static float Max(float a, float b) => a > b ? a : b;

    /// <summary><c>MIN( a, b )</c>: <c>(a &lt; b) ? a : b</c>.</summary>
    public static float Min(float a, float b) => a < b ? a : b;

    /// <summary><c>RemapVal</c> (mathlib.h).</summary>
    public static float RemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        return c + (d - c) * (val - a) / (b - a);
    }

    /// <summary><c>anglemod</c> (mathlib.h): to 16-bit angle units and back.</summary>
    public static float AngleMod(float a) => (360f / 65536) * ((int)(a * (65536f / 360.0f)) & 65535);

    /// <summary>
    /// <c>Bias( x, biasAmt )</c> (mathlib_base.cpp): <c>pow( x, log( biasAmt ) * -1.4427f )</c>.
    /// Stock's one-entry cache never stores its key, so it recomputes every time,
    /// which is what this does.
    /// </summary>
    public static float Bias(float x, float biasAmt)
    {
        float exponent = MathF.Log(biasAmt) * -1.4427f;
        return MathF.Pow(x, exponent);
    }

    /// <summary><c>VectorVectors( forward, right, up )</c> (mathlib_base.cpp).</summary>
    public static void VectorVectors(in Vector forward, out Vector right, out Vector up)
    {
        if (forward.X == 0 && forward.Y == 0)
        {
            right = new Vector(0f, -1f, 0f);
            up = new Vector(-forward.Z, 0f, 0f);
            return;
        }

        var tmp = new Vector(0f, 0f, 1.0f);
        right = Cross(forward, tmp);
        Normalize(ref right);
        up = Cross(right, forward);
        Normalize(ref up);
    }

    /// <summary><c>DEG2RAD</c>.</summary>
    public static float DegToRad(float degrees) => degrees * MoveVec.DegToRad;

    /// <summary>
    /// <c>CalcDistanceSqrToLineSegment( P, A, B )</c> (mathlib_base.cpp): the
    /// closest point's parameter clamped to the segment, then the squared distance.
    /// </summary>
    public static float DistanceSqrToLineSegment(in Vector p, in Vector a, in Vector b)
    {
        Vector dir = Sub(b, a);
        float div = Dot(dir, dir);
        float t = div < 0.00001f ? 0f : (Dot(dir, p) - Dot(dir, a)) / div;
        t = Clamp(t, 0f, 1f);
        var closest = new Vector(a.X + dir.X * t, a.Y + dir.Y * t, a.Z + dir.Z * t);
        Vector delta = Sub(p, closest);
        return delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
    }

    /// <summary>
    /// <c>IntersectRayWithRay( ray0, ray1, t, s )</c> (collisionutils.cpp), for two
    /// rays made by <c>Ray_t::Init( start, end )</c>: the parameters along each
    /// normalised direction. Parallel rays answer zero for both.
    /// </summary>
    public static void IntersectRayWithRay(in Vector start0, in Vector end0, in Vector start1, in Vector end1,
                                           out float t, out float s)
    {
        Vector v0 = Sub(end0, start0);
        Vector v1 = Sub(end1, start1);
        Normalize(ref v0);
        Normalize(ref v1);

        Vector v0xv1 = Cross(v0, v1);
        float lengthSq = v0xv1.X * v0xv1.X + v0xv1.Y * v0xv1.Y + v0xv1.Z * v0xv1.Z;
        if (lengthSq == 0.0f)
        {
            t = 0;
            s = 0;
            return;
        }

        Vector p1p0 = Sub(start1, start0);

        Vector axc = Cross(p1p0, v0xv1);
        axc = new Vector(-axc.X, -axc.Y, -axc.Z);
        float detT = Dot(axc, v1);

        axc = Cross(p1p0, v0xv1);
        axc = new Vector(-axc.X, -axc.Y, -axc.Z);
        float detS = Dot(axc, v0);

        t = detT / lengthSq;
        s = detS / lengthSq;
    }
}
