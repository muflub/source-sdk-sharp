//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>
/// The mathlib helpers the rope and beam code calls, each spelled as
/// vector.h / mathlib_base.cpp spell them, because the facts over them are
/// bit-exact. Pure functions: no state.
/// </summary>
public static class RopeMath
{
    /// <summary><c>VectorLerp</c>: <c>a + (b - a) * t</c>, per component.</summary>
    public static Vector Lerp(in Vector a, in Vector b, float t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>Component-wise product, <c>Vector::operator*( const Vector&amp; )</c>.</summary>
    public static Vector Mul(in Vector a, in Vector b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    /// <summary><c>Vector::operator/=( float )</c>: one reciprocal, three multiplies.</summary>
    public static Vector Div(in Vector v, float f)
    {
        float oo = 1.0f / f;
        return new Vector(v.X * oo, v.Y * oo, v.Z * oo);
    }

    /// <summary><c>VectorMin</c> (MIN is <c>a &lt; b ? a : b</c>).</summary>
    public static Vector Min(in Vector a, in Vector b)
        => new(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y, a.Z < b.Z ? a.Z : b.Z);

    /// <summary><c>VectorMax</c> (MAX is <c>a &gt; b ? a : b</c>).</summary>
    public static Vector Max(in Vector a, in Vector b)
        => new(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y, a.Z > b.Z ? a.Z : b.Z);

    /// <summary><c>AddPointToBounds</c>.</summary>
    public static void AddPointToBounds(in Vector v, ref Vector mins, ref Vector maxs)
    {
        if (v.X < mins.X) mins.X = v.X;
        if (v.X > maxs.X) maxs.X = v.X;
        if (v.Y < mins.Y) mins.Y = v.Y;
        if (v.Y > maxs.Y) maxs.Y = v.Y;
        if (v.Z < mins.Z) mins.Z = v.Z;
        if (v.Z > maxs.Z) maxs.Z = v.Z;
    }

    /// <summary><c>RemapVal</c> (mathlib.h): <c>C + (D - C) * (val - A) / (B - A)</c>.</summary>
    public static float RemapVal(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;

        return c + (d - c) * (val - a) / (b - a);
    }

    /// <summary><c>VectorsAreEqual</c> with a tolerance.</summary>
    public static bool AreEqual(in Vector a, in Vector b, float tolerance)
    {
        if (MathF.Abs(a.X - b.X) > tolerance)
            return false;
        if (MathF.Abs(a.Y - b.Y) > tolerance)
            return false;
        return MathF.Abs(a.Z - b.Z) <= tolerance;
    }

    /// <summary><c>CalcDistanceToLineSegment</c> (mathlib_base.cpp), through
    /// <c>CalcClosestPointToLineT</c> and <c>CalcClosestPointOnLineSegment</c>.</summary>
    public static float DistanceToLineSegment(in Vector p, in Vector a, in Vector b)
    {
        Vector dir = b - a;
        float div = dir.Dot(dir);
        float t = div < 0.00001f ? 0f : (dir.Dot(p) - dir.Dot(a)) / div;

        t = Math.Clamp(t, 0f, 1f);

        // Vector::MulAdd( a, dir, t ).
        var closest = new Vector(a.X + dir.X * t, a.Y + dir.Y * t, a.Z + dir.Z * t);

        return (p - closest).Length;
    }

    /// <summary><c>VectorNormalize</c>, the Intel arm (rsqrt and one
    /// Newton-Raphson step); returns stock's return value.</summary>
    public static float Normalize(ref Vector v) => MoveVec.Normalize(ref v);
}
