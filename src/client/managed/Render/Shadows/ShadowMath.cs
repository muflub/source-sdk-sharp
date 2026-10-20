//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.Shadows;

/// <summary>
/// The mathlib and collisionutils functions the shadow manager calls
/// (mathlib/vmatrix.cpp, mathlib/mathlib_base.cpp, public/collisionutils.cpp),
/// spelled term for term so every result is stock's to the bit: the order of
/// each float operation is stock's, a product stock widens to double
/// (QuaternionMatrix's first column, MatrixBuildPerspective's tangent) is
/// widened here, and where stock calls the float overload of a libm function
/// (tanf, atanf, atan2f, sinf, cosf) so does this. ShadowManagerOracleTests
/// holds the manager's every projection to the golden.
/// </summary>
public static class ShadowMath
{
    /// <summary>mathlib.h's DEG2RAD factor, <c>(float)(M_PI_F / 180.f)</c>.</summary>
    public const float DegToRad = (float)(Math.PI / 180.0);

    /// <summary>mathlib.h's RAD2DEG factor, <c>(float)(180.f / M_PI_F)</c>: a float quotient, one ulp from (float)(180 / M_PI).</summary>
    public const float RadToDeg = 180f / (float)Math.PI;

    // ---- the vector helpers ----

    public static float Dot(in Vector a, in Vector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary><c>CrossProduct( a, b, result )</c>.</summary>
    public static Vector Cross(in Vector a, in Vector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary><c>VectorMA( start, scale, direction, dest )</c>.</summary>
    public static Vector MA(in Vector start, float scale, in Vector direction) =>
        new(start.X + scale * direction.X, start.Y + scale * direction.Y, start.Z + scale * direction.Z);

    public static float At(in Vector v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    public static void Set(ref Vector v, int i, float value)
    {
        if (i == 0) v.X = value;
        else if (i == 1) v.Y = value;
        else v.Z = value;
    }

    /// <summary><c>VectorNormalize</c>, the Intel arm (rsqrtss and a Newton step).</summary>
    public static Vector Normalize(Vector v)
    {
        MoveVec.Normalize(ref v);
        return v;
    }

    /// <summary><c>AngleVectors( angles, &amp;forward, &amp;right, &amp;up )</c>.</summary>
    public static void AngleVectors(in QAngle angles, out Vector forward, out Vector right, out Vector up) =>
        MoveVec.AngleVectors(new Vector(angles.Pitch, angles.Yaw, angles.Roll), out forward, out right, out up);

    /// <summary><c>Vector2DMax</c>.</summary>
    public static float Max(float a, float b) => a > b ? a : b;

    // ---- matrix3x4_t ----

    public static float Get(in ShadowMatrix3x4 m, int row, int col) => m[row * 4 + col];

    /// <summary><c>AngleMatrix( angles, matrix )</c>.</summary>
    public static ShadowMatrix3x4 AngleMatrix(in QAngle angles)
    {
        float sy = MathF.Sin(angles.Yaw * DegToRad), cy = MathF.Cos(angles.Yaw * DegToRad);
        float sp = MathF.Sin(angles.Pitch * DegToRad), cp = MathF.Cos(angles.Pitch * DegToRad);
        float sr = MathF.Sin(angles.Roll * DegToRad), cr = MathF.Cos(angles.Roll * DegToRad);
        var m = new ShadowMatrix3x4();
        m[0] = cp * cy;
        m[4] = cp * sy;
        m[8] = -sp;
        float crcy = cr * cy;
        float crsy = cr * sy;
        float srcy = sr * cy;
        float srsy = sr * sy;
        m[1] = sp * srcy - crsy;
        m[5] = sp * srsy + crcy;
        m[9] = sr * cp;
        m[2] = sp * crcy + srsy;
        m[6] = sp * crsy - srcy;
        m[10] = cr * cp;
        m[3] = 0f;
        m[7] = 0f;
        m[11] = 0f;
        return m;
    }

    /// <summary><c>AngleIMatrix( angles, matrix )</c>: the transpose of AngleMatrix, computed as stock does.</summary>
    public static ShadowMatrix3x4 AngleIMatrix(in QAngle angles)
    {
        float sy = MathF.Sin(angles.Yaw * DegToRad), cy = MathF.Cos(angles.Yaw * DegToRad);
        float sp = MathF.Sin(angles.Pitch * DegToRad), cp = MathF.Cos(angles.Pitch * DegToRad);
        float sr = MathF.Sin(angles.Roll * DegToRad), cr = MathF.Cos(angles.Roll * DegToRad);
        var m = new ShadowMatrix3x4();
        m[0] = cp * cy;
        m[1] = cp * sy;
        m[2] = -sp;
        m[4] = sr * sp * cy + cr * -sy;
        m[5] = sr * sp * sy + cr * cy;
        m[6] = sr * cp;
        m[8] = cr * sp * cy + -sr * -sy;
        m[9] = cr * sp * sy + -sr * cy;
        m[10] = cr * cp;
        m[3] = 0f;
        m[7] = 0f;
        m[11] = 0f;
        return m;
    }

    /// <summary><c>VectorRotate( in, matrix, out )</c>: each row dotted with the vector.</summary>
    public static Vector Rotate(in Vector v, in ShadowMatrix3x4 m) =>
        new(v.X * m[0] + v.Y * m[1] + v.Z * m[2], v.X * m[4] + v.Y * m[5] + v.Z * m[6], v.X * m[8] + v.Y * m[9] + v.Z * m[10]);

    /// <summary><c>VectorTransform( in, matrix, out )</c>.</summary>
    public static Vector Transform(in Vector v, in ShadowMatrix3x4 m) =>
        new(v.X * m[0] + v.Y * m[1] + v.Z * m[2] + m[3], v.X * m[4] + v.Y * m[5] + v.Z * m[6] + m[7],
            v.X * m[8] + v.Y * m[9] + v.Z * m[10] + m[11]);

    /// <summary>
    /// <c>ConcatTransforms( in1, in2, out )</c>, its SSE body: each output row
    /// is a0*b0 + (a1*b1 + a2*b2), then the row's translation added under a
    /// mask (so a rotation term gets + 0.0f, which turns -0 into +0).
    /// </summary>
    public static ShadowMatrix3x4 ConcatTransforms(in ShadowMatrix3x4 a, in ShadowMatrix3x4 b)
    {
        var o = new ShadowMatrix3x4();
        for (int r = 0; r < 3; r++)
        {
            float a0 = a[r * 4], a1 = a[r * 4 + 1], a2 = a[r * 4 + 2];
            for (int c = 0; c < 4; c++)
            {
                float v = a0 * b[c] + (a1 * b[4 + c] + a2 * b[8 + c]);
                v += c == 3 ? a[r * 4 + 3] : 0f;
                o[r * 4 + c] = v;
            }
        }
        return o;
    }

    /// <summary><c>MatrixInvert( in, out )</c> for a rotation and translation.</summary>
    public static ShadowMatrix3x4 MatrixInvert(in ShadowMatrix3x4 i)
    {
        var o = new ShadowMatrix3x4();
        o[0] = i[0]; o[1] = i[4]; o[2] = i[8];
        o[4] = i[1]; o[5] = i[5]; o[6] = i[9];
        o[8] = i[2]; o[9] = i[6]; o[10] = i[10];
        float t0 = i[3], t1 = i[7], t2 = i[11];
        o[3] = -(t0 * o[0] + t1 * o[1] + t2 * o[2]);
        o[7] = -(t0 * o[4] + t1 * o[5] + t2 * o[6]);
        o[11] = -(t0 * o[8] + t1 * o[9] + t2 * o[10]);
        return o;
    }

    /// <summary><c>TransformAABB( transform, minsIn, maxsIn, minsOut, maxsOut )</c>.</summary>
    public static void TransformAABB(in ShadowMatrix3x4 m, in Vector minsIn, in Vector maxsIn, out Vector minsOut, out Vector maxsOut)
    {
        Vector localCenter = new(minsIn.X + maxsIn.X, minsIn.Y + maxsIn.Y, minsIn.Z + maxsIn.Z);
        localCenter = new(localCenter.X * 0.5f, localCenter.Y * 0.5f, localCenter.Z * 0.5f);
        Vector localExtents = new(maxsIn.X - localCenter.X, maxsIn.Y - localCenter.Y, maxsIn.Z - localCenter.Z);
        Vector worldCenter = Transform(localCenter, m);
        Vector worldExtents = new(
            MathF.Abs(localExtents.X * m[0]) + MathF.Abs(localExtents.Y * m[1]) + MathF.Abs(localExtents.Z * m[2]),
            MathF.Abs(localExtents.X * m[4]) + MathF.Abs(localExtents.Y * m[5]) + MathF.Abs(localExtents.Z * m[6]),
            MathF.Abs(localExtents.X * m[8]) + MathF.Abs(localExtents.Y * m[9]) + MathF.Abs(localExtents.Z * m[10]));
        minsOut = new(worldCenter.X - worldExtents.X, worldCenter.Y - worldExtents.Y, worldCenter.Z - worldExtents.Z);
        maxsOut = new(worldCenter.X + worldExtents.X, worldCenter.Y + worldExtents.Y, worldCenter.Z + worldExtents.Z);
    }

    /// <summary>
    /// <c>QuaternionMatrix( q, matrix )</c>: its first column is computed in
    /// double (stock's literals there are 1.0 and 2.0), the rest in float.
    /// </summary>
    public static ShadowMatrix3x4 QuaternionMatrix(float x, float y, float z, float w)
    {
        var m = new ShadowMatrix3x4();
        m[0] = (float)(1.0 - 2.0 * y * y - 2.0 * z * z);
        m[4] = (float)(2.0 * x * y + 2.0 * w * z);
        m[8] = (float)(2.0 * x * z - 2.0 * w * y);
        m[1] = 2.0f * x * y - 2.0f * w * z;
        m[5] = 1.0f - 2.0f * x * x - 2.0f * z * z;
        m[9] = 2.0f * y * z + 2.0f * w * x;
        m[2] = 2.0f * x * z + 2.0f * w * y;
        m[6] = 2.0f * y * z - 2.0f * w * x;
        m[10] = 1.0f - 2.0f * x * x - 2.0f * y * y;
        m[3] = 0f;
        m[7] = 0f;
        m[11] = 0f;
        return m;
    }

    /// <summary><c>MatrixAngles( matrix, angles )</c>: atan2f, RAD2DEG.</summary>
    public static QAngle MatrixAngles(in ShadowMatrix3x4 m)
    {
        float f0 = m[0], f1 = m[4], f2 = m[8];
        float l0 = m[1], l1 = m[5], l2 = m[9];
        float u2 = m[10];
        float xyDist = MathF.Sqrt(f0 * f0 + f1 * f1);
        float pitch, yaw, roll;
        if (xyDist > 0.001f)
        {
            yaw = MathF.Atan2(f1, f0) * RadToDeg;
            pitch = MathF.Atan2(-f2, xyDist) * RadToDeg;
            roll = MathF.Atan2(l2, u2) * RadToDeg;
        }
        else
        {
            yaw = MathF.Atan2(-l0, l1) * RadToDeg;
            pitch = MathF.Atan2(-f2, xyDist) * RadToDeg;
            roll = 0;
        }
        return new QAngle(pitch, yaw, roll);
    }

    /// <summary><c>QuaternionAngles( q, angles )</c>: through the matrix, as stock does.</summary>
    public static QAngle QuaternionAngles(float x, float y, float z, float w) => MatrixAngles(QuaternionMatrix(x, y, z, w));

    // ---- VMatrix ----

    public static float Get(in ShadowMatrix m, int row, int col) => m[row * 4 + col];

    public static ShadowMatrix Identity()
    {
        var m = new ShadowMatrix();
        m[0] = 1f; m[5] = 1f; m[10] = 1f; m[15] = 1f;
        return m;
    }

    /// <summary><c>VMatrix( matrix3x4 )</c>: the three rows and a bottom row of 0, 0, 0, 1.</summary>
    public static ShadowMatrix FromMatrix3x4(in ShadowMatrix3x4 m3)
    {
        var m = new ShadowMatrix();
        for (int i = 0; i < 12; i++)
            m[i] = m3[i];
        m[12] = 0f; m[13] = 0f; m[14] = 0f; m[15] = 1f;
        return m;
    }

    /// <summary><c>MatrixBuildScale( dst, x, y, z )</c>.</summary>
    public static ShadowMatrix Scale(float x, float y, float z)
    {
        var m = new ShadowMatrix();
        m[0] = x; m[5] = y; m[10] = z; m[15] = 1f;
        return m;
    }

    /// <summary><c>MatrixMultiply( src1, src2, dst )</c>.</summary>
    public static ShadowMatrix Multiply(in ShadowMatrix s1, in ShadowMatrix s2)
    {
        var d = new ShadowMatrix();
        for (int r = 0; r < 4; r++)
        {
            for (int c = 0; c < 4; c++)
                d[r * 4 + c] = s1[r * 4] * s2[c] + s1[r * 4 + 1] * s2[4 + c] + s1[r * 4 + 2] * s2[8 + c] + s1[r * 4 + 3] * s2[12 + c];
        }
        return d;
    }

    /// <summary><c>VMatrix::Transpose()</c> and <c>MatrixTranspose</c>.</summary>
    public static ShadowMatrix Transpose(in ShadowMatrix s)
    {
        var d = new ShadowMatrix();
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                d[r * 4 + c] = s[c * 4 + r];
        return d;
    }

    /// <summary><c>VMatrix::SetBasisVectors( forward, left, up )</c>: the columns 0-2 of the 3x3.</summary>
    public static void SetBasisVectors(ref ShadowMatrix m, in Vector x, in Vector y, in Vector z)
    {
        m[0] = x.X; m[4] = x.Y; m[8] = x.Z;
        m[1] = y.X; m[5] = y.Y; m[9] = y.Z;
        m[2] = z.X; m[6] = z.Y; m[10] = z.Z;
    }

    /// <summary><c>VMatrix::SetTranslation</c>.</summary>
    public static void SetTranslation(ref ShadowMatrix m, in Vector t)
    {
        m[3] = t.X; m[7] = t.Y; m[11] = t.Z;
    }

    /// <summary><c>Vector3DMultiply( matrix, v, dst )</c>: the 3x3 only.</summary>
    public static Vector Multiply3x3(in ShadowMatrix m, in Vector v) =>
        new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z, m[4] * v.X + m[5] * v.Y + m[6] * v.Z, m[8] * v.X + m[9] * v.Y + m[10] * v.Z);

    /// <summary><c>Vector3DMultiplyPosition( matrix, v, dst )</c>.</summary>
    public static Vector MultiplyPosition(in ShadowMatrix m, in Vector v) =>
        new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z + m[3], m[4] * v.X + m[5] * v.Y + m[6] * v.Z + m[7],
            m[8] * v.X + m[9] * v.Y + m[10] * v.Z + m[11]);

    /// <summary><c>Vector3DMultiplyPositionProjective( matrix, v, dst )</c>.</summary>
    public static Vector MultiplyPositionProjective(in ShadowMatrix m, in Vector v)
    {
        float w = m[12] * v.X + m[13] * v.Y + m[14] * v.Z + m[15];
        if (w != 0.0f)
            w = 1.0f / w;
        Vector d = new(m[0] * v.X + m[1] * v.Y + m[2] * v.Z + m[3], m[4] * v.X + m[5] * v.Y + m[6] * v.Z + m[7],
                       m[8] * v.X + m[9] * v.Y + m[10] * v.Z + m[11]);
        return new Vector(d.X * w, d.Y * w, d.Z * w);
    }

    /// <summary>
    /// <c>MatrixInverseGeneral( src, dst )</c>: Gauss-Jordan with partial
    /// pivoting over the augmented 4x8. False, and dst untouched, when a pivot
    /// is too small.
    /// </summary>
    public static bool InverseGeneral(in ShadowMatrix src, ref ShadowMatrix dst)
    {
        Span<float> mat = stackalloc float[32];
        Span<int> rowMap = stackalloc int[4];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
                mat[i * 8 + j] = src[i * 4 + j];
            mat[i * 8 + 4] = 0.0f;
            mat[i * 8 + 5] = 0.0f;
            mat[i * 8 + 6] = 0.0f;
            mat[i * 8 + 7] = 0.0f;
            mat[i * 8 + i + 4] = 1.0f;
            rowMap[i] = i;
        }

        for (int iRow = 0; iRow < 4; iRow++)
        {
            float fLargest = 0.00001f;
            int iLargest = -1;
            for (int iTest = iRow; iTest < 4; iTest++)
            {
                float fTest = MathF.Abs(mat[rowMap[iTest] * 8 + iRow]);
                if (fTest > fLargest)
                {
                    iLargest = iTest;
                    fLargest = fTest;
                }
            }

            if (iLargest == -1)
                return false;

            (rowMap[iLargest], rowMap[iRow]) = (rowMap[iRow], rowMap[iLargest]);

            int pRow = rowMap[iRow] * 8;
            float mul = 1.0f / mat[pRow + iRow];
            for (int j = 0; j < 8; j++)
                mat[pRow + j] *= mul;
            mat[pRow + iRow] = 1.0f;

            for (int i = 0; i < 4; i++)
            {
                if (i == iRow)
                    continue;
                int pScaleRow = rowMap[i] * 8;
                mul = -mat[pScaleRow + iRow];
                for (int j = 0; j < 8; j++)
                    mat[pScaleRow + j] += mat[pRow + j] * mul;
                mat[pScaleRow + iRow] = 0.0f;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            int pIn = rowMap[i] * 8 + 4;
            for (int j = 0; j < 4; j++)
                dst[i * 4 + j] = mat[pIn + j];
        }
        return true;
    }

    /// <summary>
    /// <c>MatrixBuildPerspective( dst, fovX, fovY, zNear, zFar )</c>: its width
    /// and height go through double (<c>M_PI</c> is a double, so is the tan).
    /// </summary>
    public static ShadowMatrix BuildPerspective(float fovX, float fovY, float zNear, float zFar)
    {
        float width = (float)(2 * zNear * Math.Tan(fovX * (Math.PI / 180.0f) * 0.5f));
        float height = (float)(2 * zNear * Math.Tan(fovY * (Math.PI / 180.0f) * 0.5f));
        var dst = new ShadowMatrix();
        dst[0] = 2.0F * zNear / width;
        dst[5] = 2.0F * zNear / height;
        dst[10] = -zFar / (zNear - zFar);
        dst[14] = 1.0f;
        dst[11] = zNear * zFar / (zNear - zFar);

        ShadowMatrix negateXY = Identity();
        negateXY[0] = -1.0f;
        negateXY[5] = -1.0f;
        dst = Multiply(negateXY, dst);

        ShadowMatrix addW = Identity();
        addW[3] = 1.0f;
        addW[7] = 1.0f;
        addW[11] = 0.0f;
        dst = Multiply(addW, dst);

        ShadowMatrix scaleHalf = Identity();
        scaleHalf[0] = 0.5f;
        scaleHalf[5] = 0.5f;
        return Multiply(scaleHalf, dst);
    }

    /// <summary>
    /// <c>CalculateAABBFromProjectionMatrix( worldToVolume, &amp;mins, &amp;maxs )</c>:
    /// the unit volume's eight corners through the inverse, bounded from
    /// ClearBounds' 99999.
    /// </summary>
    public static void AABBFromProjectionMatrix(in ShadowMatrix worldToVolume, out Vector mins, out Vector maxs)
    {
        ShadowMatrix volumeToWorld = default;
        InverseGeneral(worldToVolume, ref volumeToWorld);
        mins = new Vector(99999, 99999, 99999);
        maxs = new Vector(-99999, -99999, -99999);
        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector world = MultiplyPositionProjective(volumeToWorld, new Vector(x, y, z));
                    AddPointToBounds(world, ref mins, ref maxs);
                }
            }
        }
    }

    /// <summary><c>AddPointToBounds</c>.</summary>
    public static void AddPointToBounds(in Vector v, ref Vector mins, ref Vector maxs)
    {
        for (int i = 0; i < 3; i++)
        {
            float val = At(v, i);
            if (val < At(mins, i))
                Set(ref mins, i, val);
            if (val > At(maxs, i))
                Set(ref maxs, i, val);
        }
    }

    // ---- the frustum ----

    /// <summary><c>SignbitsForPlane</c>.</summary>
    public static byte SignBits(in Vector normal)
    {
        int bits = 0;
        for (int j = 0; j < 3; j++)
        {
            if (At(normal, j) < 0)
                bits |= 1 << j;
        }
        return (byte)bits;
    }

    /// <summary><c>Frustum_t::SetPlane</c>.</summary>
    public static ShadowPlane Plane(int type, in Vector normal, float dist) =>
        new() { Normal = normal, Dist = dist, Type = (byte)type, SignBits = SignBits(normal) };

    // mathlib.h's FRUSTUM_* and PLANE_ANYZ.
    private const int FrustumRight = 0, FrustumLeft = 1, FrustumTop = 2, FrustumBottom = 3, FrustumNearZ = 4, FrustumFarZ = 5;
    private const int PlaneAnyZ = 5;

    /// <summary><c>CalcFovY( fovX, aspect )</c>: tanf and atanf.</summary>
    public static float CalcFovY(float fovX, float aspect)
    {
        if (fovX < 1 || fovX > 179)
            fovX = 90;
        float val = MathF.Atan(MathF.Tan(fovX * DegToRad * 0.5f) / aspect);
        return val * RadToDeg * 2.0f;
    }

    /// <summary><c>GeneratePerspectiveFrustum( origin, angles, zNear, zFar, fovX, aspect, frustum )</c>.</summary>
    public static ShadowFrustum PerspectiveFrustum(in Vector origin, in QAngle angles, float zNear, float zFar, float fovX, float aspect)
    {
        AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
        float fovY = CalcFovY(fovX, aspect);
        var frustum = new ShadowFrustum();
        float intercept = Dot(origin, forward);
        frustum[FrustumFarZ] = Plane(PlaneAnyZ, new Vector(-forward.X, -forward.Y, -forward.Z), -zFar - intercept);
        frustum[FrustumNearZ] = Plane(PlaneAnyZ, forward, zNear + intercept);

        fovX *= 0.5f;
        fovY *= 0.5f;
        float tanX = MathF.Tan(fovX * DegToRad);
        float tanY = MathF.Tan(fovY * DegToRad);

        Vector normalPos = MA(right, tanX, forward);
        Vector normalNeg = MA(normalPos, -2.0f, right);
        normalPos = Normalize(normalPos);
        normalNeg = Normalize(normalNeg);
        frustum[FrustumLeft] = Plane(PlaneAnyZ, normalPos, Dot(normalPos, origin));
        frustum[FrustumRight] = Plane(PlaneAnyZ, normalNeg, Dot(normalNeg, origin));

        normalPos = MA(up, tanY, forward);
        normalNeg = MA(normalPos, -2.0f, up);
        normalPos = Normalize(normalPos);
        normalNeg = Normalize(normalNeg);
        frustum[FrustumBottom] = Plane(PlaneAnyZ, normalPos, Dot(normalPos, origin));
        frustum[FrustumTop] = Plane(PlaneAnyZ, normalNeg, Dot(normalNeg, origin));
        return frustum;
    }

    /// <summary><c>BoxOnPlaneSide( mins, maxs, plane )</c>: 1 in front, 2 behind, 3 across.</summary>
    public static int BoxOnPlaneSide(in Vector emins, in Vector emaxs, in ShadowPlane p)
    {
        if (p.Type < 3)
        {
            if (p.Dist <= At(emins, p.Type))
                return 1;
            if (p.Dist >= At(emaxs, p.Type))
                return 2;
            return 3;
        }

        float n0 = p.Normal.X, n1 = p.Normal.Y, n2 = p.Normal.Z;
        float dist1, dist2;
        switch (p.SignBits)
        {
            case 0:
                dist1 = n0 * emaxs.X + n1 * emaxs.Y + n2 * emaxs.Z;
                dist2 = n0 * emins.X + n1 * emins.Y + n2 * emins.Z;
                break;
            case 1:
                dist1 = n0 * emins.X + n1 * emaxs.Y + n2 * emaxs.Z;
                dist2 = n0 * emaxs.X + n1 * emins.Y + n2 * emins.Z;
                break;
            case 2:
                dist1 = n0 * emaxs.X + n1 * emins.Y + n2 * emaxs.Z;
                dist2 = n0 * emins.X + n1 * emaxs.Y + n2 * emins.Z;
                break;
            case 3:
                dist1 = n0 * emins.X + n1 * emins.Y + n2 * emaxs.Z;
                dist2 = n0 * emaxs.X + n1 * emaxs.Y + n2 * emins.Z;
                break;
            case 4:
                dist1 = n0 * emaxs.X + n1 * emaxs.Y + n2 * emins.Z;
                dist2 = n0 * emins.X + n1 * emins.Y + n2 * emaxs.Z;
                break;
            case 5:
                dist1 = n0 * emins.X + n1 * emaxs.Y + n2 * emins.Z;
                dist2 = n0 * emaxs.X + n1 * emins.Y + n2 * emaxs.Z;
                break;
            case 6:
                dist1 = n0 * emaxs.X + n1 * emins.Y + n2 * emins.Z;
                dist2 = n0 * emins.X + n1 * emaxs.Y + n2 * emaxs.Z;
                break;
            case 7:
                dist1 = n0 * emins.X + n1 * emins.Y + n2 * emins.Z;
                dist2 = n0 * emaxs.X + n1 * emaxs.Y + n2 * emaxs.Z;
                break;
            default:
                dist1 = dist2 = 0;
                break;
        }

        int sides = 0;
        if (dist1 >= p.Dist)
            sides = 1;
        if (dist2 < p.Dist)
            sides |= 2;
        return sides;
    }

    /// <summary><c>R_CullBox( mins, maxs, frustum )</c>: right, left, top, bottom, near, far.</summary>
    public static bool CullBox(in Vector mins, in Vector maxs, in ShadowFrustum frustum) =>
        BoxOnPlaneSide(mins, maxs, frustum[FrustumRight]) == 2 || BoxOnPlaneSide(mins, maxs, frustum[FrustumLeft]) == 2 ||
        BoxOnPlaneSide(mins, maxs, frustum[FrustumTop]) == 2 || BoxOnPlaneSide(mins, maxs, frustum[FrustumBottom]) == 2 ||
        BoxOnPlaneSide(mins, maxs, frustum[FrustumNearZ]) == 2 || BoxOnPlaneSide(mins, maxs, frustum[FrustumFarZ]) == 2;

    // ---- collisionutils ----

    /// <summary><c>IsBoxIntersectingSphere( boxMin, boxMax, center, radius )</c>.</summary>
    public static bool IsBoxIntersectingSphere(in Vector boxMin, in Vector boxMax, in Vector center, float radius)
    {
        float dmin = 0.0f;
        float flDelta;
        for (int i = 0; i < 3; i++)
        {
            float c = At(center, i);
            if (c < At(boxMin, i))
            {
                flDelta = c - At(boxMin, i);
                dmin += flDelta * flDelta;
            }
            else if (c > At(boxMax, i))
            {
                flDelta = At(boxMax, i) - c;
                dmin += flDelta * flDelta;
            }
        }
        return dmin < radius * radius;
    }

    /// <summary><c>IsSphereIntersectingSphere( center1, radius1, center2, radius2 )</c>.</summary>
    public static bool IsSphereIntersectingSphere(in Vector center1, float radius1, in Vector center2, float radius2)
    {
        Vector delta = new(center2.X - center1.X, center2.Y - center1.Y, center2.Z - center1.Z);
        float distSq = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
        float radiusSum = radius1 + radius2;
        return distSq <= radiusSum * radiusSum;
    }

    private static uint Bits(float f) => unchecked((uint)BitConverter.SingleToInt32Bits(f));

    private static Vector Column(in ShadowMatrix3x4 m, int col) => new(m[col], m[4 + col], m[8 + col]);

    private static Vector Row(in ShadowMatrix3x4 m, int row) => new(m[row * 4], m[row * 4 + 1], m[row * 4 + 2]);

    private static float RowDot(in ShadowMatrix3x4 m, int row, in Vector v) => m[row * 4] * v.X + m[row * 4 + 1] * v.Y + m[row * 4 + 2] * v.Z;

    private static float ColumnDot(in ShadowMatrix3x4 m, int col, in Vector v) => m[col] * v.X + m[4 + col] * v.Y + m[8 + col] * v.Z;

    /// <summary>
    /// <c>ComputeSeparatingPlane( org1, angles1, min1, max1, org2, angles2,
    /// min2, max2, tolerance, &amp;plane )</c>: the fifteen axes of two
    /// oriented boxes, each projection compared by its bits as stock compares
    /// them. The plane's normal only; false when the boxes overlap.
    /// </summary>
    public static bool ComputeSeparatingPlane(in Vector org1, in QAngle angles1, in Vector min1, in Vector max1, in Vector org2,
                                              in QAngle angles2, in Vector min2, in Vector max2, float tolerance, out Vector normal)
    {
        // ComputeCenterIMatrix( org1, angles1, min1, max1, worldToBox1 )
        Vector c1 = new(min1.X + max1.X, min1.Y + max1.Y, min1.Z + max1.Z);
        c1 = new Vector(c1.X * -0.5f, c1.Y * -0.5f, c1.Z * -0.5f);
        ShadowMatrix3x4 worldToBox1 = AngleIMatrix(angles1);
        Vector localOrigin = Rotate(org1, worldToBox1);
        c1 = new Vector(c1.X - localOrigin.X, c1.Y - localOrigin.Y, c1.Z - localOrigin.Z);
        worldToBox1[3] = c1.X; worldToBox1[7] = c1.Y; worldToBox1[11] = c1.Z;

        // ComputeCenterMatrix( org2, angles2, min2, max2, box2ToWorld )
        Vector c2 = new(min2.X + max2.X, min2.Y + max2.Y, min2.Z + max2.Z);
        c2 = new Vector(c2.X * 0.5f, c2.Y * 0.5f, c2.Z * 0.5f);
        ShadowMatrix3x4 box2ToWorld = AngleMatrix(angles2);
        Vector worldCentroid = Rotate(c2, box2ToWorld);
        worldCentroid = new Vector(worldCentroid.X + org2.X, worldCentroid.Y + org2.Y, worldCentroid.Z + org2.Z);
        box2ToWorld[3] = worldCentroid.X; box2ToWorld[7] = worldCentroid.Y; box2ToWorld[11] = worldCentroid.Z;

        Vector box1Size = new(max1.X - min1.X, max1.Y - min1.Y, max1.Z - min1.Z);
        Vector box2Size = new(max2.X - min2.X, max2.Y - min2.Y, max2.Z - min2.Z);
        box1Size = new Vector(box1Size.X * 0.5f, box1Size.Y * 0.5f, box1Size.Z * 0.5f);
        box2Size = new Vector(box2Size.X * 0.5f, box2Size.Y * 0.5f, box2Size.Z * 0.5f);

        ShadowMatrix3x4 b = ConcatTransforms(worldToBox1, box2ToWorld);
        Vector o = Column(b, 3);
        var a = new ShadowMatrix3x4();
        for (int i = 0; i < 12; i++)
            a[i] = (i & 3) == 3 ? 0f : MathF.Abs(b[i]);

        float sum, proj;

        // The faces of box 1.
        for (int k = 0; k < 3; k++)
        {
            sum = At(box1Size, k) + RowDot(a, k, box2Size);
            proj = MathF.Abs(At(o, k)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Row(worldToBox1, k);
                return true;
            }
        }

        // The faces of box 2.
        for (int k = 0; k < 3; k++)
        {
            sum = At(box2Size, k) + ColumnDot(a, k, box1Size);
            proj = MathF.Abs(ColumnDot(b, k, o)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Column(box2ToWorld, k);
                return true;
            }
        }

        float A(int r, int c) => a[r * 4 + c];
        float B(int r, int c) => b[r * 4 + c];

        // The nine edge pairs, as stock unrolled them.
        if (A(0, 0) < 1.0f - 1e-3f)
        {
            sum = box1Size.Y * A(2, 0) + box1Size.Z * A(1, 0) + box2Size.Y * A(0, 2) + box2Size.Z * A(0, 1);
            proj = MathF.Abs(-o.Y * B(2, 0) + o.Z * B(1, 0)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 0), Column(box2ToWorld, 0));
                return true;
            }
        }
        if (A(0, 1) < 1.0f - 1e-3f)
        {
            sum = box1Size.Y * A(2, 1) + box1Size.Z * A(1, 1) + box2Size.X * A(0, 2) + box2Size.Z * A(0, 0);
            proj = MathF.Abs(-o.Y * B(2, 1) + o.Z * B(1, 1)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 0), Column(box2ToWorld, 1));
                return true;
            }
        }
        if (A(0, 2) < 1.0f - 1e-3f)
        {
            sum = box1Size.Y * A(2, 2) + box1Size.Z * A(1, 2) + box2Size.X * A(0, 1) + box2Size.Y * A(0, 0);
            proj = MathF.Abs(-o.Y * B(2, 2) + o.Z * B(1, 2)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 0), Column(box2ToWorld, 2));
                return true;
            }
        }
        if (A(1, 0) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(2, 0) + box1Size.Z * A(0, 0) + box2Size.Y * A(1, 2) + box2Size.Z * A(1, 1);
            proj = MathF.Abs(o.X * B(2, 0) - o.Z * B(0, 0)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 1), Column(box2ToWorld, 0));
                return true;
            }
        }
        if (A(1, 1) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(2, 1) + box1Size.Z * A(0, 1) + box2Size.X * A(1, 2) + box2Size.Z * A(1, 0);
            proj = MathF.Abs(o.X * B(2, 1) - o.Z * B(0, 1)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 1), Column(box2ToWorld, 1));
                return true;
            }
        }
        if (A(1, 2) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(2, 2) + box1Size.Z * A(0, 2) + box2Size.X * A(1, 1) + box2Size.Y * A(1, 0);
            proj = MathF.Abs(o.X * B(2, 2) - o.Z * B(0, 2)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 1), Column(box2ToWorld, 2));
                return true;
            }
        }
        if (A(2, 0) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(1, 0) + box1Size.Y * A(0, 0) + box2Size.Y * A(2, 2) + box2Size.Z * A(2, 1);
            proj = MathF.Abs(-o.X * B(1, 0) + o.Y * B(0, 0)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 2), Column(box2ToWorld, 0));
                return true;
            }
        }
        if (A(2, 1) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(1, 1) + box1Size.Y * A(0, 1) + box2Size.X * A(2, 2) + box2Size.Z * A(2, 0);
            proj = MathF.Abs(-o.X * B(1, 1) + o.Y * B(0, 1)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 2), Column(box2ToWorld, 1));
                return true;
            }
        }
        if (A(2, 2) < 1.0f - 1e-3f)
        {
            sum = box1Size.X * A(1, 2) + box1Size.Y * A(0, 2) + box2Size.X * A(2, 1) + box2Size.Y * A(2, 0);
            proj = MathF.Abs(-o.X * B(1, 2) + o.Y * B(0, 2)) + tolerance;
            if (Bits(proj) > Bits(sum))
            {
                normal = Cross(Row(worldToBox1, 2), Column(box2ToWorld, 2));
                return true;
            }
        }
        normal = default;
        return false;
    }
}
