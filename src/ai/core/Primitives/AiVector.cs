namespace SourceSharp.Ai;

/// <summary>
/// A point or direction in world units. The core's own vector: the engine's
/// <c>Vector</c> lives in SourceSharp.Abi, which the core may not reference
/// (plan_hl2mp_ai §1.2), so a binding translates at the boundary.
/// </summary>
public readonly record struct AiVector(float X, float Y, float Z)
{
    public static AiVector Zero => default;

    public float LengthSquared => X * X + Y * Y + Z * Z;

    public float Length => MathF.Sqrt(LengthSquared);

    /// <summary>The length in the ground plane, ignoring height.</summary>
    public float Length2D => MathF.Sqrt(X * X + Y * Y);

    public float Dot(AiVector o) => X * o.X + Y * o.Y + Z * o.Z;

    public float DistanceTo(AiVector o) => (this - o).Length;

    /// <summary>This vector scaled to length 1, or zero when it has none.</summary>
    public AiVector Normalised()
    {
        float len = Length;
        return len > 0f ? this * (1f / len) : Zero;
    }

    public static AiVector operator +(AiVector a, AiVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static AiVector operator -(AiVector a, AiVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static AiVector operator -(AiVector a) => new(-a.X, -a.Y, -a.Z);

    public static AiVector operator *(AiVector a, float s) => new(a.X * s, a.Y * s, a.Z * s);
}

/// <summary>View angles in degrees, as a usercmd carries them.</summary>
public readonly record struct AiAngles(float Pitch, float Yaw, float Roll);
