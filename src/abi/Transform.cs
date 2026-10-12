//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// A place and a facing, as one thing you can compose and invert.
///
/// <para>
/// Source's <c>VMatrix</c>, restricted to what it is ever used as here: a
/// rotation and a translation, row-major, with the translation in column 3 and
/// an implicit bottom row of (0,0,0,1). Twelve floats rather than sixteen —
/// every operation below preserves that bottom row, so storing it would be
/// storing a constant and inviting somebody to write to it.
/// </para>
///
/// <para>
/// WHY THIS IS IN C# AT ALL. <c>point_template</c> records each of its sources
/// as a transform in the template's own space and puts a copy back into the
/// world somewhere else; that is the whole of what a template DOES, and it was
/// four lines of mathlib in the middle of a C++ entity. It is arithmetic with
/// no engine state in it, which makes it exactly the kind of thing that belongs
/// on this side of the boundary — and exactly the kind of thing that is
/// silently wrong if nobody checks it, since a sign error puts a spawned crate
/// somewhere plausible rather than nowhere.
/// </para>
///
/// <para>
/// So it is checked against the arithmetic it replaces rather than against
/// itself: <c>sourcesharp/test/template_transform_cases.txt</c> holds a table of
/// placements taken from the real <c>VMatrix</c>, a gtest keeps that table
/// honest about native, and <c>TemplateTransformTests</c> asserts this class
/// reproduces it.
/// </para>
/// </summary>
public readonly struct Transform
{
    // Row-major, [row][column], laid out as VMatrix's first three rows.
    public readonly float M00, M01, M02, M03;
    public readonly float M10, M11, M12, M13;
    public readonly float M20, M21, M22, M23;

    public Transform(
        float m00, float m01, float m02, float m03,
        float m10, float m11, float m12, float m13,
        float m20, float m21, float m22, float m23)
    {
        M00 = m00; M01 = m01; M02 = m02; M03 = m03;
        M10 = m10; M11 = m11; M12 = m12; M13 = m13;
        M20 = m20; M21 = m21; M22 = m22; M23 = m23;
    }

    /// <summary>Nowhere, facing along +X. What composing with changes nothing.</summary>
    public static readonly Transform Identity = new(
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f);

    /// <summary>
    /// Source's <c>VMatrix::SetupMatrixOrgAngles</c>: the transform that takes a
    /// point in an entity's own space to the world.
    ///
    /// <para>
    /// The rotation is (yaw * pitch) * roll, in that order, which is the order
    /// every Source angle is composed in — writing it in any other order gives a
    /// matrix that is right for a facing with only one non-zero component and
    /// wrong for every other, which is the shape of bug that survives testing.
    /// </para>
    /// </summary>
    public static Transform FromOriginAngles(Vector origin, QAngle angles)
    {
        float pitch = angles.Pitch * MathF.PI / 180f;
        float yaw   = angles.Yaw   * MathF.PI / 180f;
        float roll  = angles.Roll  * MathF.PI / 180f;

        float sp = MathF.Sin(pitch), cp = MathF.Cos(pitch);
        float sy = MathF.Sin(yaw),   cy = MathF.Cos(yaw);
        float sr = MathF.Sin(roll),  cr = MathF.Cos(roll);

        return new Transform(
            cp * cy,                     sr * sp * cy + cr * -sy,   cr * sp * cy + -sr * -sy,  origin.X,
            cp * sy,                     sr * sp * sy + cr * cy,    cr * sp * sy + -sr * cy,   origin.Y,
            -sp,                         sr * cp,                   cr * cp,                   origin.Z);
    }

    /// <summary>Where this transform puts the origin — column 3.</summary>
    public Vector Translation => new(M03, M13, M23);

    /// <summary>
    /// Source's <c>VectorTransform</c>: where a point in this frame ends up in
    /// the one the transform is expressed in.
    ///
    /// <para>
    /// Rotate AND translate, which is what distinguishes it from
    /// <c>VectorRotate</c> — a point moves with the frame's origin, a direction
    /// does not. The two are one line apart and a caller that picks the wrong
    /// one gets an answer that is right at the origin and drifts with distance,
    /// which is exactly the shape of bug that survives a test placed at zero.
    /// </para>
    /// </summary>
    public Vector TransformPoint(Vector point) => new(
        M00 * point.X + M01 * point.Y + M02 * point.Z + M03,
        M10 * point.X + M11 * point.Y + M12 * point.Z + M13,
        M20 * point.X + M21 * point.Y + M22 * point.Z + M23);

    /// <summary>
    /// Source's <c>MatrixInverseTR</c>. Valid only because this is a rotation
    /// and a translation and nothing else: the inverse of the rotation is its
    /// transpose, so no general inversion is needed and none is done.
    /// </summary>
    public Transform InverseTR()
    {
        // Transposed upper 3x3.
        float d00 = M00, d01 = M10, d02 = M20;
        float d10 = M01, d11 = M11, d12 = M21;
        float d20 = M02, d21 = M12, d22 = M22;

        // The old translation, negated and rotated into the new frame.
        float tx = -M03, ty = -M13, tz = -M23;

        return new Transform(
            d00, d01, d02, d00 * tx + d01 * ty + d02 * tz,
            d10, d11, d12, d10 * tx + d11 * ty + d12 * tz,
            d20, d21, d22, d20 * tx + d21 * ty + d22 * tz);
    }

    /// <summary>
    /// Source's <c>MatrixMultiply</c>: apply <paramref name="b"/> first, then
    /// <paramref name="a"/>.
    ///
    /// <para>
    /// The fourth column of <paramref name="a"/> survives into the result
    /// because <paramref name="b"/>'s implicit bottom row is (0,0,0,1) — which
    /// is the term the twelve-float form would lose if the bottom row were
    /// anything else. It never is; nothing here builds a matrix that is not a
    /// rotation and a translation.
    /// </para>
    /// </summary>
    public static Transform operator *(Transform a, Transform b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03,

        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13,

        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
        a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23);

    /// <summary>
    /// Source's <c>MatrixToAngles</c>: the pitch, yaw and roll this transform's
    /// rotation describes.
    ///
    /// <para>
    /// The second branch is not a rounding guard. When the facing is very nearly
    /// straight up or straight down, yaw and roll describe the SAME rotation and
    /// atan2 of the forward axis is a coin toss between them; stock reads the
    /// yaw off the left axis instead and calls the roll zero, and a copy that
    /// did not would give a spawned entity a random spin about its own axis
    /// whenever a mapper pointed a template at the floor.
    /// </para>
    /// </summary>
    public QAngle ToAngles()
    {
        // The basis vectors are the COLUMNS: forward is column 0, left is
        // column 1, and only the z of up (column 2) is needed.
        float forwardX = M00, forwardY = M10, forwardZ = M20;
        float leftX = M01, leftY = M11, leftZ = M21;
        float upZ = M22;

        float xyDist = MathF.Sqrt(forwardX * forwardX + forwardY * forwardY);

        const float radToDeg = 180f / MathF.PI;

        if (xyDist > 0.001f)
        {
            return new QAngle(
                MathF.Atan2(-forwardZ, xyDist) * radToDeg,
                MathF.Atan2(forwardY, forwardX) * radToDeg,
                MathF.Atan2(leftZ, upZ) * radToDeg);
        }

        return new QAngle(
            MathF.Atan2(-forwardZ, xyDist) * radToDeg,
            MathF.Atan2(-leftX, leftY) * radToDeg,
            0f);
    }

    /// <summary>
    /// Where a source entity sits in a template's own space — the number a
    /// template stores instead of the entity.
    /// </summary>
    public static Transform Relative(Vector templateOrigin, QAngle templateAngles,
                                     Vector entityOrigin, QAngle entityAngles)
        => FromOriginAngles(templateOrigin, templateAngles).InverseTR()
         * FromOriginAngles(entityOrigin, entityAngles);

    /// <summary>
    /// Where that source goes when the template is instanced somewhere else —
    /// the other half, and the reason <see cref="Relative"/> is stored at all.
    /// </summary>
    public (Vector Origin, QAngle Angles) Placed(Vector instanceOrigin, QAngle instanceAngles)
    {
        Transform world = FromOriginAngles(instanceOrigin, instanceAngles) * this;
        return (world.Translation, world.ToAngles());
    }
}
