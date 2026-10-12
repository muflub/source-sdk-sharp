//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.CompilerServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// <c>VMatrix</c>: sixteen floats, row-major (<c>m[row][col]</c>), as
/// <c>SetMatrixValue</c> takes it.
/// </summary>
[InlineArray(16)]
public struct VMatrix
{
    private float _e0;

    public float this[int row, int col]
    {
        readonly get => this[row * 4 + col];
        set => this[row * 4 + col] = value;
    }
}

/// <summary>
/// The mathlib builders the texture-transform proxies call
/// (mathlib/vmatrix.cpp, mathlib_base.cpp), spelled term for term: the order
/// of every float operation is stock's, and where stock goes through double
/// (<c>M_PI</c> is a double) so does this.
/// </summary>
public static class VMatrixMath
{
    private const double Pi = 3.14159265358979323846;

    /// <summary><c>MatrixSetIdentity</c>.</summary>
    public static VMatrix Identity()
    {
        var m = new VMatrix();
        m[0, 0] = 1f; m[1, 1] = 1f; m[2, 2] = 1f; m[3, 3] = 1f;
        return m;
    }

    /// <summary><c>MatrixBuildTranslation( dst, x, y, z )</c>.</summary>
    public static VMatrix Translation(float x, float y, float z)
    {
        VMatrix m = Identity();
        m[0, 3] = x;
        m[1, 3] = y;
        m[2, 3] = z;
        return m;
    }

    /// <summary><c>MatrixBuildScale( dst, x, y, z )</c>.</summary>
    public static VMatrix Scale(float x, float y, float z)
    {
        var m = new VMatrix();
        m[0, 0] = x;
        m[1, 1] = y;
        m[2, 2] = z;
        m[3, 3] = 1f;
        return m;
    }

    /// <summary>
    /// <c>MatrixBuildRotateZ</c>: <c>float radians = angle * ( M_PI / 180.0f )</c>
    /// is rounded to float, then <c>(float)sin( radians )</c>.
    /// </summary>
    public static VMatrix RotateZ(float angleDegrees, IProxyMath math)
    {
        float radians = (float)(angleDegrees * (Pi / 180.0f));
        math.SinCosOfFloat(radians, out float fSin, out float fCos);

        var m = new VMatrix();
        m[0, 0] = fCos; m[0, 1] = -fSin;
        m[1, 0] = fSin; m[1, 1] = fCos;
        m[2, 2] = 1f;
        m[3, 3] = 1f;
        return m;
    }

    /// <summary><c>MatrixMultiply( src1, src2, dst )</c>.</summary>
    public static VMatrix Multiply(in VMatrix s1, in VMatrix s2)
    {
        var d = new VMatrix();
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
                d[r, c] = s1[r, 0] * s2[0, c] + s1[r, 1] * s2[1, c] + s1[r, 2] * s2[2, c] + s1[r, 3] * s2[3, c];
        }
        return d;
    }

    /// <summary>
    /// <c>MatrixBuildRotationAboutAxis( VMatrix&amp;, axis, angle )</c>: the
    /// 3x4 builder (<c>float radians = angle * ( M_PI / 180.0 )</c>, then
    /// <c>sin( radians )</c> of that float), then the bottom row.
    /// </summary>
    public static VMatrix RotationAboutAxis(Vector axis, float angleDegrees, IProxyMath math)
    {
        float radians = (float)(angleDegrees * (Pi / 180.0));
        math.SinCosOfFloat(radians, out float fSin, out float fCos);

        float x = axis.X, y = axis.Y, z = axis.Z;
        float axisXSquared = x * x;
        float axisYSquared = y * y;
        float axisZSquared = z * z;

        var d = new VMatrix();
        d[0, 0] = axisXSquared + (1 - axisXSquared) * fCos;
        d[1, 0] = x * y * (1 - fCos) + z * fSin;
        d[2, 0] = z * x * (1 - fCos) - y * fSin;

        d[0, 1] = x * y * (1 - fCos) - z * fSin;
        d[1, 1] = axisYSquared + (1 - axisYSquared) * fCos;
        d[2, 1] = y * z * (1 - fCos) + x * fSin;

        d[0, 2] = z * x * (1 - fCos) + y * fSin;
        d[1, 2] = y * z * (1 - fCos) - x * fSin;
        d[2, 2] = axisZSquared + (1 - axisZSquared) * fCos;

        d[3, 3] = 1f;
        return d;
    }
}

/// <summary>
/// The C library calls whose float/double spelling the stock build decided.
/// A call that names <c>sin</c>, <c>exp</c> or <c>log</c> with a float
/// argument resolves to <c>sinf</c> (and the arithmetic around it stays float)
/// or to <c>sin( (double)x )</c> (and the arithmetic around it is double)
/// depending on which overloads its translation unit sees; the two differ in
/// the last bit. Each member is one call site's whole expression, so either
/// spelling is one implementation, and the recording (proxies-stock.tsv.gz) is
/// what says which one each site compiled to.
/// </summary>
public interface IProxyMath
{
    /// <summary>
    /// mathlib's <c>(float)sin( radians )</c> and <c>cos</c> with
    /// <c>float radians</c> (MatrixBuildRotateZ, MatrixBuildRotationAboutAxis).
    /// </summary>
    void SinCosOfFloat(float radians, out float sin, out float cos);

    /// <summary>mathproxy.cpp's <c>m_Scale.GetFloat() * exp( src + offset )</c>, assigned to a float.</summary>
    float Exponential(float scale, float exponent);

    /// <summary>vstdlib's <c>sqrtf( -2.0f * log( rsq ) / rsq )</c> (CGaussianRandomStream).</summary>
    float BoxMullerFactor(float rsq);
}

/// <summary>
/// The spelling each site compiled to. The recording proves two: the double
/// spelling of mathlib's trig misses 2 MatrixRotate binds and of vstdlib's
/// log 30 GaussianNoise draws. It cannot tell exp from expf on the three
/// recorded Exponential proxies (both spellings replay them), so that one is
/// read off the stock object: <c>nm -u mathproxy.cpp.o</c> imports
/// <c>expf</c> and no <c>exp</c> (debug, a9d8f9fd).
/// </summary>
public sealed class StockProxyMath : IProxyMath
{
    public void SinCosOfFloat(float radians, out float sin, out float cos)
    {
        sin = MathF.Sin(radians);
        cos = MathF.Cos(radians);
    }

    public float Exponential(float scale, float exponent) => scale * MathF.Exp(exponent);

    public float BoxMullerFactor(float rsq) => MathF.Sqrt(-2.0f * MathF.Log(rsq) / rsq);
}
