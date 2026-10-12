//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
//
// The mathlib mirror for the bone_setup.cpp port. Every body here is a line
// copy of its stock original, with the operand ORDER kept: the native build
// compiles -march=core2 (no FMA contraction) and the CLR does not contract
// either, so mirroring the expression order is what makes the managed result
// bit-identical to the native one, not merely close. Where stock's literal
// types force a double promotion (the `0.5f * M_PI` product in the slerp's
// antipodal arm), the promotion is reproduced exactly -- rounding it at a
// different moment is an ulp of disagreement per call. (A bare INT literal
// like `2` promotes INT-to-FLOAT, so those products stay float; FLT_EPSILON
// is glibc's F-suffixed float macro, 2^-23.)
//
// Two mirrors are NOT bit-exact and say so at the definition:
//   * VectorNormalize, whose stock x86 body is an rsqrtss with one Newton
//     step; the scalar 1/sqrtf form is used and the difference is last-ulp.
//   * the trig, where glibc sinf/cosf and the CLR's MathF are each
//     correctly-rounded-quality but not guaranteed identical. Facts derive
//     their expectations from THESE functions, so they are self-consistent;
//     only goldens recorded off the native build carry a tolerance.
//
// Quaternion storage is System.Numerics (x,y,z,w, stock's order); its own
// operators are deliberately unused -- Quaternion.Multiply's summation order
// is not stock's, and this file exists for the summation order.
//

using Vector = System.Numerics.Vector3;

using System;
using System.Numerics;

namespace SourceSharp.Shared.Bones;

/// <summary>
/// Stock's <c>RadianEuler</c>: three radians, stored x,y,z like stock so the
/// byte readers and the conversions read the same layout the C++ does.
/// </summary>
public readonly struct RadianEuler
{
    public RadianEuler(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The stock layout twin: mstudio{bone,linearbone}_t::rot is a
    /// Vector3 that CalcBoneQuaternion takes as a <c>RadianEuler &amp;</c>.</summary>
    public RadianEuler(Vector v)
    {
        X = v.X;
        Y = v.Y;
        Z = v.Z;
    }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
}

/// <summary>
/// Stock's <c>QAngle</c> (pitch, yaw, roll in degrees). <c>SourceSharp.Abi</c>
/// has a twin for the engine ABI; this one belongs to the shared bones
/// namespace so the math mirror does not lean on the interop assembly.
/// </summary>
public readonly struct BoneAngle
{
    public BoneAngle(float pitch, float yaw, float roll)
    {
        Pitch = pitch;
        Yaw = yaw;
        Roll = roll;
    }

    public float Pitch { get; }
    public float Yaw { get; }
    public float Roll { get; }
}

/// <summary>
/// Stock's <c>matrix3x4_t</c>: three rows of four floats, row-major exactly
/// as <c>m_flMatVal[3][4]</c> lays out, column 3 the translation. A bone's
/// world matrix is one of these; the trace cluster reads their columns.
/// </summary>
public struct BoneMatrix3x4
{
    /// <summary>Row 0 (x-axis / x-basis and its translation).</summary>
    public Vector4 Row0;

    /// <summary>Row 1 (y-axis / y-basis and its translation).</summary>
    public Vector4 Row1;

    /// <summary>Row 2 (z-axis / z-basis and its translation).</summary>
    public Vector4 Row2;

    /// <summary>Stock <c>matrix[0][3]</c> etc.: the translation column.</summary>
    public Vector Position
    {
        readonly get => new(Row0.W, Row1.W, Row2.W);
        set
        {
            Row0.W = value.X;
            Row1.W = value.Y;
            Row2.W = value.Z;
        }
    }

    /// <summary>
    /// Identity: rotation rows exactly as stock's <c>IsQuaternionIdentity</c>-
    /// compatible matrix, translation zero. Stock zero-then-overwrites rather
    /// than clearing whole rows in most places; helpers here fill all twelve
    /// floats at once, which is why each constructor below states the rows.
    /// </summary>
    public static BoneMatrix3x4 Identity =>
        new()
        {
            Row0 = new Vector4(1f, 0f, 0f, 0f),
            Row1 = new Vector4(0f, 1f, 0f, 0f),
            Row2 = new Vector4(0f, 0f, 1f, 0f),
        };

    /// <summary>All twelve floats, row-major (<c>m[r*4+c]</c> in C++).</summary>
    public void GetElements(Span<float> twelve)
    {
        Row0.CopyTo(twelve);
        Row1.CopyTo(twelve[4..]);
        Row2.CopyTo(twelve[8..]);
    }

    /// <summary>From twelve floats, row-major (<c>m[r*4+c]</c> in C++).</summary>
    public static BoneMatrix3x4 FromElements(ReadOnlySpan<float> twelve) =>
        new()
        {
            Row0 = new Vector4(twelve[0], twelve[1], twelve[2], twelve[3]),
            Row1 = new Vector4(twelve[4], twelve[5], twelve[6], twelve[7]),
            Row2 = new Vector4(twelve[8], twelve[9], twelve[10], twelve[11]),
        };
}

/// <summary>
/// The pure mathlib surface the bone port consumes. No state, no clock, no
/// convar: every input arrives as a parameter.
/// </summary>
public static class BoneSetupMath
{
    /// <summary>Stock <c>DEG2RAD(x)</c>: x * (float)(M_PI_F/180.f), the float
    /// quotient (M_PI_F is itself the float π), not the double one.</summary>
    public const float DegToRad = (float)((float)Math.PI / 180.0f);

    /// <summary>Stock <c>RAD2DEG(x)</c> (mathlib.h:298):
    /// <c>(float)(x) * (float)(180.f / M_PI_F)</c> — both literals are FLOAT
    /// (<c>M_PI_F</c> is <c>(float)(M_PI)</c>, mathlib.h:294), so the product
    /// is the float quotient rounded once. (180.0/double π rounded once gives
    /// the same float here, but the expression is mirrored as written.)</summary>
    public const float RadToDeg = 180.0f / (float)Math.PI;

    /// <summary>glibc's <c>FLT_EPSILON</c>: 2^-23, defined with the F suffix,
    /// so stock's uses of it stay in float.</summary>
    public const float FltEpsilon = 1.1920928955078125e-07f;

    // -----------------------------------------------------------------
    // Small vector ops (vector.h), order verbatim
    // -----------------------------------------------------------------

    /// <summary>vector.h's <c>DotProduct3 / DotProduct</c>: (x0*y0 + x1*y1) + x2*y2.</summary>
    public static float Dot(Vector a, Vector b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>mathlib.h's <c>Lerp(flPercent, A, B) = A + (B - A) * flPercent</c>.</summary>
    public static float Lerp(float s, float a, float b) =>
        a + (b - a) * s;

    /// <summary>vector.h's <c>VectorLerp</c>, component by component.</summary>
    public static Vector VectorLerp(Vector src1, Vector src2, float t) =>
        new(
            src1.X + (src2.X - src1.X) * t,
            src1.Y + (src2.Y - src1.Y) * t,
            src1.Z + (src2.Z - src1.Z) * t);

    /// <summary>vector.h's <c>VectorScale(p, scale)</c>.</summary>
    public static Vector VectorScale(Vector p, float scale) =>
        new(p.X * scale, p.Y * scale, p.Z * scale);

    /// <summary>vector.h's <c>VectorMA(v1, v2scale, v2)</c>: v1 + v2 * v2scale.</summary>
    public static Vector VectorMA(Vector v1, float v2scale, Vector v2) =>
        new(
            v1.X + v2.X * v2scale,
            v1.Y + v2.Y * v2scale,
            v1.Z + v2.Z * v2scale);

    /// <summary>
    /// vector.h's <c>VectorNormalize</c>. Stock's x86 body is rsqrtss with
    /// one Newton step over (len² + 1e-10f); that approximation is NOT
    /// reproducible bit-for-bit outside x86 asm, so this is the scalar
    /// 1/sqrtf form. Last-ulp divergence, accepted and named in the file
    /// header. Zero length leaves the vector unchanged, as stock does.
    /// </summary>
    public static Vector VectorNormalize(Vector v)
    {
        float len2 = Dot(v, v);
        if (len2 == 0f)
        {
            return v;
        }

        float inv = 1.0f / MathF.Sqrt(len2);
        return new Vector(v.X * inv, v.Y * inv, v.Z * inv);
    }

    /// <summary>
    /// mathlib.h's <c>SimpleSpline</c>: <c>3 * valueSquared - 2 * valueSquared
    /// * value</c>. The 3 and 2 are INT literals (int-to-float promotion, not
    /// double), and the subtraction is left-associative: (3*vs) - ((2*vs)*value).
    /// </summary>
    public static float SimpleSpline(float value)
    {
        float valueSquared = value * value;
        return 3.0f * valueSquared - 2.0f * valueSquared * value;
    }

    // -----------------------------------------------------------------
    // Trig (math_pfns.h's SinCos is sinf/cosf in our build)
    // -----------------------------------------------------------------

    /// <summary>math_pfns.h's <c>FastSinCos</c> = sinf/cosf of the raw argument.
    /// Public (promotion ruling 2026-08, needs-query.md NQ-11) so
    /// <c>BoneSetupQuery.VectorYawRotate</c> takes the compiled sincosf pair stock
    /// calls instead of a second private copy of the two calls.</summary>
    public static (float Sin, float Cos) SinCos(float radians) =>
        (MathF.Sin(radians), MathF.Cos(radians));

    // -----------------------------------------------------------------
    // Angle conversions (mathlib_base.cpp)
    // -----------------------------------------------------------------

    /// <summary>
    /// mathlib_base.cpp's <c>AngleQuaternion(RadianEuler)</c>, non-X360 arm:
    /// half-angle sines/cosines of (z, y, x) in that call order, then the
    /// four products exactly as grouped.
    /// </summary>
    public static Quaternion AngleQuaternion(RadianEuler angles)
    {
        (float sy, float cy) = SinCos(angles.Z * 0.5f);
        (float sp, float cp) = SinCos(angles.Y * 0.5f);
        (float sr, float cr) = SinCos(angles.X * 0.5f);

        float srXcp = sr * cp, crXsp = cr * sp;
        float x = srXcp * cy - crXsp * sy;   // X
        float y = crXsp * cy + srXcp * sy;   // Y

        float crXcp = cr * cp, srXsp = sr * sp;
        float z = crXcp * sy - srXsp * cy;   // Z
        float w = crXcp * cy + srXsp * sy;   // W (real component)

        return new Quaternion(x, y, z, w);
    }

    /// <summary>
    /// mathlib_base.cpp's <c>AngleQuaternion(QAngle)</c>, non-X360 arm: the
    /// yaw/pitch/roll half-angle arms, then the same four products.
    /// </summary>
    public static Quaternion AngleQuaternion(BoneAngle angles)
    {
        (float sy, float cy) = SinCos(DegToRad * angles.Yaw * 0.5f);
        (float sp, float cp) = SinCos(DegToRad * angles.Pitch * 0.5f);
        (float sr, float cr) = SinCos(DegToRad * angles.Roll * 0.5f);

        float srXcp = sr * cp, crXsp = cr * sp;
        float x = srXcp * cy - crXsp * sy;   // X
        float y = crXsp * cy + srXcp * sy;   // Y

        float crXcp = cr * cp, srXsp = sr * sp;
        float z = crXcp * sy - srXsp * cy;   // Z
        float w = crXcp * cy + srXsp * sy;   // W (real component)

        return new Quaternion(x, y, z, w);
    }

    /// <summary>
    /// mathlib_base.cpp's <c>AngleMatrix(QAngle)</c>, non-X360 arm: matrix =
    /// (YAW * PITCH) * ROLL, translation column zeroed by stock, caller fills
    /// column 3 (<see cref="AngleMatrix(BoneAngle, Vector)"/>).
    /// </summary>
    public static BoneMatrix3x4 AngleMatrix(BoneAngle angles)
    {
        (float sy, float cy) = SinCos(DegToRad * angles.Yaw);
        (float sp, float cp) = SinCos(DegToRad * angles.Pitch);
        (float sr, float cr) = SinCos(DegToRad * angles.Roll);

        // matrix = (YAW * PITCH) * ROLL. Stock writes matrix[r][c]: column 0
        // is (cp*cy, cp*sy, -sp) — the forward vector — so the ROWS below
        // carry the transposed-looking grouping the source's [row][col]
        // assignments give them.
        var m = new BoneMatrix3x4();
        float crcy = cr * cy;
        float crsy = cr * sy;
        float srcy = sr * cy;
        float srsy = sr * sy;
        m.Row0.X = cp * cy;
        m.Row0.Y = sp * srcy - crsy;
        m.Row0.Z = sp * crcy + srsy;

        m.Row1.X = cp * sy;
        m.Row1.Y = sp * srsy + crcy;
        m.Row1.Z = sp * crsy - srcy;

        m.Row2.X = -sp;
        m.Row2.Y = sr * cp;
        m.Row2.Z = cr * cp;

        m.Row0.W = 0.0f;
        m.Row1.W = 0.0f;
        m.Row2.W = 0.0f;
        return m;
    }

    /// <summary>mathlib_base.cpp's <c>AngleMatrix(QAngle, position)</c>: the
    /// rotation above, then <c>MatrixSetColumn(position, 3)</c>.</summary>
    public static BoneMatrix3x4 AngleMatrix(BoneAngle angles, Vector position)
    {
        BoneMatrix3x4 m = AngleMatrix(angles);
        m.Position = position;
        return m;
    }

    /// <summary>mathlib_base.cpp's <c>AngleMatrix(RadianEuler)</c>: stock
    /// re-expresses (x,y,z) radians as the QAngle (y,z,x) degrees, then runs
    /// the yaw/pitch/roll arm above.</summary>
    public static BoneMatrix3x4 AngleMatrix(RadianEuler angles) =>
        AngleMatrix(new BoneAngle(RadToDeg * angles.Y, RadToDeg * angles.Z, RadToDeg * angles.X));

    /// <summary>mathlib_base.cpp's <c>AngleMatrix(RadianEuler, position)</c>.</summary>
    public static BoneMatrix3x4 AngleMatrix(RadianEuler angles, Vector position) =>
        AngleMatrix(new BoneAngle(RadToDeg * angles.Y, RadToDeg * angles.Z, RadToDeg * angles.X), position);

    /// <summary>
    /// mathlib_base.cpp's <c>MatrixAngles(matrix, float *angles)</c>: the
    /// forward/left/up arm and the gimbal-lock arm, atan2f/sqrtf form, each
    /// result scaled by the RAD2DEG float constant.
    /// </summary>
    public static BoneAngle MatrixAngles(in BoneMatrix3x4 m)
    {
        float fwdX = m.Row0.X, fwdY = m.Row1.X, fwdZ = m.Row2.X;
        float leftX = m.Row0.Y, leftY = m.Row1.Y, leftZ = m.Row2.Y;
        float upZ = m.Row2.Z;

        float xyDist = MathF.Sqrt(fwdX * fwdX + fwdY * fwdY);

        float pitch, yaw, roll;
        if (xyDist > 0.001f)
        {
            yaw = RadToDeg * MathF.Atan2(fwdY, fwdX);
            pitch = RadToDeg * MathF.Atan2(-fwdZ, xyDist);
            roll = RadToDeg * MathF.Atan2(leftZ, upZ);
        }
        else
        {
            yaw = RadToDeg * MathF.Atan2(-leftX, leftY);
            pitch = RadToDeg * MathF.Atan2(-fwdZ, xyDist);
            roll = 0;
        }

        return new BoneAngle(pitch, yaw, roll);
    }

    /// <summary>
    /// mathlib.h's <c>MatrixAngles(matrix, RadianEuler&amp;)</c> (854-859):
    /// the float* arm writes DEGREES into <c>&amp;angles.x</c> verbatim —
    /// <c>(angles[0], angles[1], angles[2])</c> land in <c>(x, y, z)</c>, so
    /// <c>(x, y, z) = (pitch, yaw, roll)</c> degrees — and the wrapper then
    /// re-initialises through the DEG2RAD macro (mathlib.h:302):
    /// <c>angles.Init( DEG2RAD(angles.z), DEG2RAD(angles.x),
    /// DEG2RAD(angles.y) )</c>: each component multiplied by the float
    /// constant <c>(float)(M_PI_F / 180.f)</c> (== <see cref="DegToRad"/>)
    /// and reordered (z, x, y) into (x, y, z), i.e. the RadianEuler carries
    /// (roll, pitch, yaw) radians — the convention
    /// <c>AngleMatrix(RadianEuler)</c> reads back. The WorldSpaceSlerp
    /// target-matrix route (bone_setup.cpp:1410) passes through this
    /// wrapper; ruling in docs/plans/needs-blend.md NB-06.
    /// </summary>
    public static RadianEuler MatrixAnglesRadian(in BoneMatrix3x4 m)
    {
        BoneAngle a = MatrixAngles(m); // (pitch, yaw, roll) degrees = angles[0..2]
        return new RadianEuler(DegToRad * a.Roll, DegToRad * a.Pitch, DegToRad * a.Yaw);
    }

    /// <summary>
    /// mathlib_base.cpp:2191's <c>QuaternionAngles(q, RadianEuler&amp;)</c> —
    /// the conversion behind stock's implicit <c>RadianEuler(Quaternion)</c>
    /// ctor (vector.h:1736-1739, "evil auto type promotion"): build the
    /// rotation with <c>QuaternionMatrix</c>, then read it back through
    /// <c>MatrixAngles(matrix, RadianEuler&amp;)</c> (mathlib.h:854-859).
    /// This is the first hop of the WorldSpaceSlerp target-matrix route
    /// (bone_setup.cpp:1410); ruling in docs/plans/needs-blend.md NB-06.
    /// </summary>
    public static RadianEuler QuaternionAngles(Quaternion q) =>
        MatrixAnglesRadian(QuaternionMatrix(q));

    /// <summary>
    /// mathlib_base.cpp's <c>MatrixAngles(matrix, Quaternion&amp;, Vector&amp;)</c>:
    /// the four trace arms, then <c>QuaternionNormalize</c>, then the position
    /// column. The raw (unnormalised) quat the arms produce is handed to
    /// <see cref="QuaternionNormalize"/> exactly as stock's float
    /// fields held them.
    /// </summary>
    public static (Quaternion Q, Vector Pos) MatrixAnglesQuaternion(in BoneMatrix3x4 m)
    {
        float trace = m.Row0.X + m.Row1.Y + m.Row2.Z + 1.0f;
        Quaternion q;
        if (trace > 1.0f + FltEpsilon)
        {
            q = new Quaternion(
                m.Row2.Y - m.Row1.Z,
                m.Row0.Z - m.Row2.X,
                m.Row1.X - m.Row0.Y,
                trace);
        }
        else if (m.Row0.X > m.Row1.Y && m.Row0.X > m.Row2.Z)
        {
            trace = 1.0f + m.Row0.X - m.Row1.Y - m.Row2.Z;
            q = new Quaternion(
                trace,
                m.Row1.X + m.Row0.Y,
                m.Row0.Z + m.Row2.X,
                m.Row2.Y - m.Row1.Z);
        }
        else if (m.Row1.Y > m.Row2.Z)
        {
            trace = 1.0f + m.Row1.Y - m.Row0.X - m.Row2.Z;
            q = new Quaternion(
                m.Row0.Y + m.Row1.X,
                trace,
                m.Row2.Y + m.Row1.Z,
                m.Row0.Z - m.Row2.X);
        }
        else
        {
            trace = 1.0f + m.Row2.Z - m.Row0.X - m.Row1.Y;
            q = new Quaternion(
                m.Row0.Z + m.Row2.X,
                m.Row2.Y + m.Row1.Z,
                trace,
                m.Row1.X - m.Row0.Y);
        }

        QuaternionNormalize(ref q);
        return (q, m.Position);
    }

    // -----------------------------------------------------------------
    // Matrix × vector (mathlib_base.cpp)
    // -----------------------------------------------------------------

    /// <summary>mathlib_base.cpp's <c>VectorTransform</c>: rotate, then the
    /// translation column, per row as <c>dot + col3</c>.</summary>
    public static Vector VectorTransform(Vector in1, in BoneMatrix3x4 in2) =>
        new(
            Dot(in1, new Vector(in2.Row0.X, in2.Row0.Y, in2.Row0.Z)) + in2.Row0.W,
            Dot(in1, new Vector(in2.Row1.X, in2.Row1.Y, in2.Row1.Z)) + in2.Row1.W,
            Dot(in1, new Vector(in2.Row2.X, in2.Row2.Y, in2.Row2.Z)) + in2.Row2.W);

    /// <summary><c>VectorRotate</c> (float* form, mathlib_base.cpp:286): the
    /// rotation only, no translation column.</summary>
    public static Vector VectorRotate(Vector in1, in BoneMatrix3x4 in2) =>
        new(
            Dot(in1, new Vector(in2.Row0.X, in2.Row0.Y, in2.Row0.Z)),
            Dot(in1, new Vector(in2.Row1.X, in2.Row1.Y, in2.Row1.Z)),
            Dot(in1, new Vector(in2.Row2.X, in2.Row2.Y, in2.Row2.Z)));

    /// <summary><c>VectorIRotate</c> (mathlib_base.cpp:313): rotate by the
    /// transpose — column-major summation, stock's exact order.</summary>
    public static Vector VectorIRotate(Vector in1, in BoneMatrix3x4 in2) =>
        new(
            in1.X * in2.Row0.X + in1.Y * in2.Row1.X + in1.Z * in2.Row2.X,
            in1.X * in2.Row0.Y + in1.Y * in2.Row1.Y + in1.Z * in2.Row2.Y,
            in1.X * in2.Row0.Z + in1.Y * in2.Row1.Z + in1.Z * in2.Row2.Z);

    /// <summary><c>VectorITransform</c> (mathlib_base.cpp:270): subtract the
    /// translation, then rotate by the transpose (stock's <c>in1t</c>
    /// temporary order).</summary>
    public static Vector VectorITransform(Vector in1, in BoneMatrix3x4 in2)
    {
        Vector in1t = new(
            in1.X - in2.Row0.W,
            in1.Y - in2.Row1.W,
            in1.Z - in2.Row2.W);
        return new(
            in1t.X * in2.Row0.X + in1t.Y * in2.Row1.X + in1t.Z * in2.Row2.X,
            in1t.X * in2.Row0.Y + in1t.Y * in2.Row1.Y + in1t.Z * in2.Row2.Y,
            in1t.X * in2.Row0.Z + in1t.Y * in2.Row1.Z + in1t.Z * in2.Row2.Z);
    }

    /// <summary>vector.h:1213's <c>CrossProduct</c>.</summary>
    public static Vector CrossProduct(Vector a, Vector b) =>
        new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    /// <summary>vector.h's <c>VectorLength</c>: <c>sqrtf(x*x+y*y+z*z)</c>.</summary>
    public static float VectorLength(Vector v) =>
            MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary>vector.h's <c>VectorLengthSqr</c>.</summary>
    public static float VectorLengthSqr(Vector v) =>
            v.X * v.X + v.Y * v.Y + v.Z * v.Z;

    public static BoneMatrix3x4 ConcatTransforms(in BoneMatrix3x4 in1, in BoneMatrix3x4 in2)
    {
        // Row r of out = a0*rowB0 + (a1*rowB1 + a2*rowB2), and column 3 gets
        // in1's translation added last (stock's masked AddSIMD).
        (Vector4 r0, float w0) = ConcatRow(in1.Row0, in2);
        (Vector4 r1, float w1) = ConcatRow(in1.Row1, in2);
        (Vector4 r2, float w2) = ConcatRow(in1.Row2, in2);
        return new BoneMatrix3x4 { Row0 = r0, Row1 = r1, Row2 = r2 };
    }

    private static (Vector4, float) ConcatRow(Vector4 a, in BoneMatrix3x4 b)
    {
        // out[r][c] = a.X*b0[c] + (a.Y*b1[c] + a.Z*b2[c]); column 3 adds a.W
        // as the in1 translation (stock's masked last-add).
        // The c<3 lanes get stock's masked AddSIMD too: the mask leaves
        // +0.0 there, so the add is a no-op except that it turns a -0.0 sum
        // into +0.0, exactly as the native SIMD does.
        float o0 = (a.X * b.Row0.X + (a.Y * b.Row1.X + a.Z * b.Row2.X)) + 0.0f;
        float o1 = (a.X * b.Row0.Y + (a.Y * b.Row1.Y + a.Z * b.Row2.Y)) + 0.0f;
        float o2 = (a.X * b.Row0.Z + (a.Y * b.Row1.Z + a.Z * b.Row2.Z)) + 0.0f;
        float o3 = (a.X * b.Row0.W + (a.Y * b.Row1.W + a.Z * b.Row2.W)) + a.W;
        return (new Vector4(o0, o1, o2, o3), o3);
    }

    // -----------------------------------------------------------------
    // Matrix × matrix (mathlib_base.cpp)
    // -----------------------------------------------------------------


    /// <summary>mathlib_base.cpp's <c>MatrixInvert</c>: transpose the basis,
    /// then the translation is minus its own dot with each new row.</summary>
    public static BoneMatrix3x4 MatrixInvert(in BoneMatrix3x4 in1)
    {
        var outm = new BoneMatrix3x4
        {
            Row0 = new Vector4(in1.Row0.X, in1.Row1.X, in1.Row2.X, 0f),
            Row1 = new Vector4(in1.Row0.Y, in1.Row1.Y, in1.Row2.Y, 0f),
            Row2 = new Vector4(in1.Row0.Z, in1.Row1.Z, in1.Row2.Z, 0f),
        };

        var tmp = new Vector(in1.Row0.W, in1.Row1.W, in1.Row2.W);
        outm.Row0.W = -Dot(tmp, new Vector(outm.Row0.X, outm.Row0.Y, outm.Row0.Z));
        outm.Row1.W = -Dot(tmp, new Vector(outm.Row1.X, outm.Row1.Y, outm.Row1.Z));
        outm.Row2.W = -Dot(tmp, new Vector(outm.Row2.X, outm.Row2.Y, outm.Row2.Z));
        return outm;
    }

    // -----------------------------------------------------------------
    // Quaternions (mathlib_base.cpp)
    // -----------------------------------------------------------------

    /// <summary>mathlib_base.cpp's <c>QuaternionDotProduct</c>.</summary>
    public static float QuaternionDotProduct(Quaternion p, Quaternion q) =>
        p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionNormalize</c>. Zero radius returns the
    /// input untouched (the assert-only guard stock compiles to that) and
    /// reports 0; the scaling multiplies w, z, y, x in stock's order.
    /// </summary>
    public static float QuaternionNormalize(ref Quaternion q)
    {
        float radius = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (radius != 0f)
        {
            radius = MathF.Sqrt(radius);
            float iradius = 1.0f / radius;
            q.W *= iradius;
            q.Z *= iradius;
            q.Y *= iradius;
            q.X *= iradius;
        }

        return radius;
    }

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionAlign</c>: q and −q are the same
    /// rotation; pick the one nearer p by the ±  sums-of-squares test.
    /// </summary>
    public static Quaternion QuaternionAlign(Quaternion p, Quaternion q)
    {
        float a = 0f, b = 0f;
        // The accumulation order of the stock loop is per-component first:
        // a += (p[i]-q[i])*(p[i]-q[i]) for i 0..3 as four separate adds.
        a += (p.X - q.X) * (p.X - q.X);
        b += (p.X + q.X) * (p.X + q.X);
        a += (p.Y - q.Y) * (p.Y - q.Y);
        b += (p.Y + q.Y) * (p.Y + q.Y);
        a += (p.Z - q.Z) * (p.Z - q.Z);
        b += (p.Z + q.Z) * (p.Z + q.Z);
        a += (p.W - q.W) * (p.W - q.W);
        b += (p.W + q.W) * (p.W + q.W);

        if (a > b)
        {
            return new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        }

        return q;
    }

    /// <summary>mathlib_base.cpp's <c>QuaternionBlendNoAlign</c>: the lerp of
    /// the four fields, then normalize. 0 returns p, 1 returns q.</summary>
    public static Quaternion QuaternionBlendNoAlign(Quaternion p, Quaternion q, float t)
    {
        float sclp = 1.0f - t;
        float sclq = t;
        Quaternion qt = new(
            sclp * p.X + sclq * q.X,
            sclp * p.Y + sclq * q.Y,
            sclp * p.Z + sclq * q.Z,
            sclp * p.W + sclq * q.W);
        QuaternionNormalize(ref qt);
        return qt;
    }

    /// <summary>mathlib_base.cpp's <c>QuaternionBlend</c>: align, then the
    /// no-align blend.</summary>
    public static Quaternion QuaternionBlend(Quaternion p, Quaternion q, float t) =>
        QuaternionBlendNoAlign(p, QuaternionAlign(p, q), t);

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionSlerp</c>: align, then the no-align
    /// slerp below.
    /// </summary>
    public static Quaternion QuaternionSlerp(Quaternion p, Quaternion q, float t) =>
        QuaternionSlerpNoAlign(p, QuaternionAlign(p, q), t);

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionSlerpNoAlign</c>. The aligned arm's
    /// <c>acos</c>/<c>sin</c> take float arguments, so clang emits
    /// acosf/sinf: every product, quotient and compare is float (confirmed
    /// against the core2 disassembly: no cvt in this body). The antipodal
    /// arm's <c>0.5f * M_PI</c> IS a double product and its <c>sin</c> a
    /// double call — reproduced. The antipodal arm keeps stock's THREE-only
    /// loop: <c>qt[3]</c> is left holding q.z (Valve's code, reproduced not
    /// fixed).
    /// </summary>
    public static Quaternion QuaternionSlerpNoAlign(Quaternion p, Quaternion q, float t)
    {
        float cosom = p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;

        if ((1.0f + cosom) > 0.000001f)
        {
            float sclp, sclq;
            if ((1.0f - cosom) > 0.000001f)
            {
                float omega = MathF.Acos(cosom);
                float sinom = MathF.Sin(omega);
                sclp = MathF.Sin((1.0f - t) * omega) / sinom;
                sclq = MathF.Sin(t * omega) / sinom;
            }
            else
            {
                sclp = 1.0f - t;
                sclq = t;
            }

            return new Quaternion(
                sclp * p.X + sclq * q.X,
                sclp * p.Y + sclq * q.Y,
                sclp * p.Z + sclq * q.Z,
                sclp * p.W + sclq * q.W);
        }
        else
        {
            // The antipodal case: no shortest arc; sweep through a
            // perpendicular of q.
            float px = -q.Y, py = q.X, pz = -q.W, pw = q.Z;
            float sclp = (float)Math.Sin((1.0f - t) * (0.5 * Math.PI));
            float sclq = (float)Math.Sin(t * (0.5 * Math.PI));

            // THREE and not four: stock leaves index 3 at its pre-loop
            // value (q.z carried on the perpendicular), bug and all.
            return new Quaternion(
                sclp * p.X + sclq * px,
                sclp * p.Y + sclq * py,
                sclp * p.Z + sclq * pz,
                pw);
        }
    }

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionScale</c>, the #else (compiled) arm.
    /// Float-argument calls, so clang emits the float overloads throughout
    /// (sqrtf, asinf, sinf, addss/divss — no cvts in the disassembly);
    /// FLT_EPSILON is the float macro. <c>min(sinom, 1.f)</c> is the
    /// <c>min</c> template, exact compare.
    /// </summary>
    public static Quaternion QuaternionScale(Quaternion p, float t)
    {
        float sinom = MathF.Sqrt(Dot(new Vector(p.X, p.Y, p.Z), new Vector(p.X, p.Y, p.Z)));
        sinom = MathF.Min(sinom, 1.0f);

        float sinsom = MathF.Sin(MathF.Asin(sinom) * t);

        t = sinsom / (sinom + FltEpsilon);

        float qx = p.X * t;
        float qy = p.Y * t;
        float qz = p.Z * t;

        // rescale rotation
        float r = 1.0f - sinsom * sinsom;
        if (r < 0.0f)
        {
            r = 0.0f;
        }

        r = MathF.Sqrt(r);

        float qw = p.W < 0 ? -r : r;
        return new Quaternion(qx, qy, qz, qw);
    }

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionMult</c> (qt = p * q): align q to p
    /// first, then the four component expressions with their exact sign and
    /// summation order. (System.Numerics' own multiply groups differently;
    /// this is why the port calls here, never <c>Quaternion.Multiply</c>.)
    /// </summary>
    public static Quaternion QuaternionMult(Quaternion p, Quaternion q)
    {
        Quaternion q2 = QuaternionAlign(p, q);
        return new Quaternion(
            p.X * q2.W + p.Y * q2.Z - p.Z * q2.Y + p.W * q2.X,
            -p.X * q2.Z + p.Y * q2.W + p.Z * q2.X + p.W * q2.Y,
            p.X * q2.Y - p.Y * q2.X + p.Z * q2.W + p.W * q2.Z,
            -p.X * q2.X - p.Y * q2.Y - p.Z * q2.Z + p.W * q2.W);
    }

    /// <summary>mathlib_base.cpp's <c>QuaternionAdd</c>: align q, add fields
    /// (no renormalise, as stock).</summary>
    public static Quaternion QuaternionAdd(Quaternion p, Quaternion q)
    {
        Quaternion q2 = QuaternionAlign(p, q);
        return new Quaternion(p.X + q2.X, p.Y + q2.Y, p.Z + q2.Z, p.W + q2.W);
    }

    /// <summary>
    /// mathlib_base.cpp's <c>QuaternionMatrix(q, matrix)</c>. The compiled
    /// (#if 1) block mixes double literals on the first row's first two
    /// entries with float ones on the rest — every expression is mirrored
    /// with the promotion it has in stock, first row included.
    /// </summary>
    public static BoneMatrix3x4 QuaternionMatrix(Quaternion q) =>
        new()
        {
            Row0 = new Vector4(
                (float)(1.0 - 2.0 * (double)q.Y * (double)q.Y - 2.0 * (double)q.Z * (double)q.Z),
                (float)(2.0f * q.X * q.Y - 2.0f * q.W * q.Z),
                (float)(2.0f * q.X * q.Z + 2.0f * q.W * q.Y),
                0.0f),
            Row1 = new Vector4(
                (float)(2.0 * (double)q.X * (double)q.Y + 2.0 * (double)q.W * (double)q.Z),
                (float)(1.0f - 2.0f * q.X * q.X - 2.0f * q.Z * q.Z),
                (float)(2.0f * q.Y * q.Z - 2.0f * q.W * q.X),
                0.0f),
            Row2 = new Vector4(
                (float)(2.0 * (double)q.X * (double)q.Z - 2.0 * (double)q.W * (double)q.Y),
                (float)(2.0f * q.Y * q.Z + 2.0f * q.W * q.X),
                (float)(1.0f - 2.0f * q.X * q.X - 2.0f * q.Y * q.Y),
                0.0f),
        };

    /// <summary>mathlib_base.cpp's <c>QuaternionMatrix(q, pos, matrix)</c>:
    /// the rotation above, then the position into column 3.</summary>
    public static BoneMatrix3x4 QuaternionMatrix(Quaternion q, Vector pos)
    {
        BoneMatrix3x4 m = QuaternionMatrix(q);
        m.Position = pos;
        return m;
    }

    // -----------------------------------------------------------------
    // bone_setup.cpp's own quaternion helpers (file-local in stock,
    // exported here for the blend cluster)
    // -----------------------------------------------------------------

    /// <summary>bone_setup.cpp's <c>QuaternionSM(s, p, q)</c>: qt = normalize(
    /// scale(p, s) * q). The SIMD trio around it is _X360-only and not
    /// compiled; the scalar arm above is the only one that exists here.</summary>
    public static Quaternion QuaternionSM(float s, Quaternion p, Quaternion q)
    {
        Quaternion p1 = QuaternionScale(p, s);
        Quaternion q1 = QuaternionMult(p1, q);
        QuaternionNormalize(ref q1);
        return q1;
    }

    /// <summary>bone_setup.cpp's <c>QuaternionMA(p, s, q)</c>: qt = normalize(
    /// p * scale(q, s)).</summary>
    public static Quaternion QuaternionMA(Quaternion p, float s, Quaternion q)
    {
        Quaternion q1 = QuaternionScale(q, s);
        Quaternion p1 = QuaternionMult(p, q1);
        QuaternionNormalize(ref p1);
        return p1;
    }

    /// <summary>bone_setup.cpp's <c>QuaternionAccumulate(p, s, q)</c>:
    /// qt = p + s * align(p, q), with NO renormalise — the accumulated error
    /// stays unnormalised on purpose; callers that need a unit quat
    /// normalise explicitly.</summary>
    public static Quaternion QuaternionAccumulate(Quaternion p, float s, Quaternion q)
    {
        Quaternion q2 = QuaternionAlign(p, q);
        return new Quaternion(
            p.X + s * q2.X,
            p.Y + s * q2.Y,
            p.Z + s * q2.Z,
            p.W + s * q2.W);
    }

    /// <summary>
    /// mathlib_base.cpp:1982's <c>QuaternionAxisAngle(q, axis, angle)</c>:
    /// <c>angle = RAD2DEG(2 * acos(q.w))</c>. The compiled game is all
    /// float: the macro (mathlib.h:295-299, "Inlined to prevent floats from
    /// being autopromoted to doubles") casts <c>(float)(x)</c> BEFORE the
    /// multiply by the FLOAT constant <c>(float)(180.f / M_PI_F)</c> — the
    /// fork's own mathlib_base.o disassembles to <c>movss</c> q.w →
    /// <c>call acosf</c> → <c>mulss</c> 0x42652ee0 (57.2958f) →
    /// <c>comiss 180.0f</c> → <c>subss 360.0f</c>. A double product
    /// (<c>(180.0/π) * 2acos</c> rounded once) differs by ~4.7e-6 and can
    /// flip the &gt;180 fold near the boundary; the chain below mirrors the
    /// compiled order operand for operand. The &gt;180 arm folds to
    /// (-180, 180]. Axis is the raw vector normalised (a zero axis stays
    /// zero: <see cref="VectorNormalize"/> keeps stock's len2==0
    /// early-out).
    /// </summary>
    public static (Vector Axis, float Angle) QuaternionAxisAngle(Quaternion q)
    {
        // (float)(2 * acos(q.w)) — acos takes the DOUBLE overload on a
        // float q.w, times int 2, then the macro rounds once to float —
        // then the single float multiply by the RAD2DEG constant.
        float angle = (float)(2 * Math.Acos((double)q.W)) * RadToDeg;
        if (angle > 180)
        {
            angle -= 360;
        }

        Vector axis = VectorNormalize(new Vector(q.X, q.Y, q.Z));
        return (axis, angle);
    }

    /// <summary>
    /// mathlib_base.cpp:3542's <c>RotationDeltaAxisAngle(src, dest, axis,
    /// angle)</c>: <c>out = normalize(mult(angleQuaternion(dest),
    /// scale(angleQuaternion(src), -1)))</c> through
    /// <see cref="QuaternionAxisAngle"/>. The -1 scale goes through the
    /// compiled <see cref="QuaternionScale"/> arm — asin/sin, not a sign
    /// flip — exactly as stock.
    /// </summary>
    public static (Vector Axis, float Angle) RotationDeltaAxisAngle(BoneAngle srcAngles, BoneAngle destAngles)
    {
        Quaternion srcQuat = AngleQuaternion(srcAngles);
        Quaternion destQuat = AngleQuaternion(destAngles);
        Quaternion srcQuatInv = QuaternionScale(srcQuat, -1.0f);
        Quaternion outQ = QuaternionMult(destQuat, srcQuatInv);
        QuaternionNormalize(ref outQ);
        return QuaternionAxisAngle(outQ);
    }

    // -----------------------------------------------------------------
    // Splines (mathlib_base.cpp + mathlib.h)
    // -----------------------------------------------------------------

    /// <summary>
    /// mathlib_base.cpp's scalar <c>Hermite_Spline(p1,p2,d1,d2,t)</c>. The
    /// basis is the shared <see cref="HermiteBasis"/> rule: b3's bare <c>2</c>
    /// is an INT literal, so that one polynomial runs in double.
    /// </summary>
    public static float HermiteSpline(float p1, float p2, float d1, float d2, float t)
    {
        (float b1, float b2, float b3, float b4) = HermiteBasis(t);
        float output = p1 * b1;
        output += p2 * b2;
        output += d1 * b3;
        output += d2 * b4;
        return output;
    }

    /// <summary>mathlib_base.cpp's <c>Hermite_Spline(p0,p1,p2,t)</c>, scalar:
    /// the slopes are the segment differences.</summary>
    public static float HermiteSpline(float p0, float p1, float p2, float t) =>
        HermiteSpline(p1, p2, p1 - p0, p2 - p1, t);

    /// <summary>
    /// mathlib_base.cpp's <c>Vector</c> Hermite pair:
    /// <c>VectorScale</c> then three <c>VectorMA</c>s — each accumulate is
    /// (accumulate + derivative*scale), the association VectorMA gives it.
    /// </summary>
    public static Vector HermiteSpline(Vector p1, Vector p2, Vector d1, Vector d2, float t)
    {
        (float b1, float b2, float b3, float b4) = HermiteBasis(t);
        Vector output = VectorScale(p1, b1);
        output = VectorMA(output, b2, p2);
        output = VectorMA(output, b3, d1);
        output = VectorMA(output, b4, d2);
        return output;
    }

    /// <summary>mathlib_base.cpp's three-point <c>Vector</c> Hermite: slopes
    /// p1−p0 and p2−p1, then the four-point arm.</summary>
    public static Vector HermiteSpline(Vector p0, Vector p1, Vector p2, float t) =>
        HermiteSpline(p1, p2, p1 - p0, p2 - p1, t);

    /// <summary>
    /// mathlib_base.cpp's <c>Quaternion</c> Hermite ("cheap, hacked version"):
    /// align q0 and q1 to q2, run the scalar spline on each of the four
    /// fields, normalize.
    /// </summary>
    public static Quaternion HermiteSpline(Quaternion q0, Quaternion q1, Quaternion q2, float t)
    {
        Quaternion q0a = QuaternionAlign(q2, q0);
        Quaternion q1a = QuaternionAlign(q2, q1);

        Quaternion output = new(
            HermiteSpline(q0a.X, q1a.X, q2.X, t),
            HermiteSpline(q0a.Y, q1a.Y, q2.Y, t),
            HermiteSpline(q0a.Z, q1a.Z, q2.Z, t),
            HermiteSpline(q0a.W, q1a.W, q2.W, t));

        QuaternionNormalize(ref output);
        return output;
    }

    /// <summary>
    /// mathlib_base.cpp's <c>Hermite_SplineBasis</c>. Literal types are
    /// preserved per coefficient: b1 and b2 use float literals; b3's <c>2</c>
    /// is an INT literal, which promotes to float (NOT double), so the whole
    /// (tCube − 2*tSqr + t) polynomial stays float, left-associative.
    /// </summary>
    public static (float B1, float B2, float B3, float B4) HermiteBasis(float t)
    {
        float tSqr = t * t;
        float tCube = t * tSqr;

        float b1 = 2.0f * tCube - 3.0f * tSqr + 1.0f;
        float b2 = 1.0f - b1;
        float b3 = tCube - 2.0f * tSqr + t;
        float b4 = tCube - tSqr;
        return (b1, b2, b3, b4);
    }
}
