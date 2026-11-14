using SourceSharp.Ai;

namespace TF2.Descent.Ai.Motion;

/// <summary>Yaw and pitch arithmetic for a body's facing, in degrees, engine-free.</summary>
public static class MotionAngles
{
    /// <summary><paramref name="degrees"/> in (-180, 180]; a non-finite angle is 0.</summary>
    public static float Normalise(float degrees)
    {
        if (!float.IsFinite(degrees))
            return 0f;
        degrees %= 360f;
        if (degrees > 180f)
            degrees -= 360f;
        else if (degrees <= -180f)
            degrees += 360f;
        return degrees;
    }

    /// <summary>The shortest turn from <paramref name="from"/> to <paramref name="to"/>, in (-180, 180].</summary>
    public static float Difference(float to, float from) => Normalise(to - from);

    /// <summary>The yaw of <paramref name="v"/> on the floor plane; <paramref name="otherwise"/> for a vector with no floor-plane length.</summary>
    public static float YawOf(AiVector v, float otherwise) =>
        v.Length2D > 1e-4f ? MathF.Atan2(v.Y, v.X) * (180f / MathF.PI) : otherwise;

    /// <summary>The upward elevation of <paramref name="v"/>: positive looking up (the opposite sign of the engine's pitch).</summary>
    public static float ElevationOf(AiVector v)
    {
        float flat = v.Length2D;
        return flat > 1e-4f || MathF.Abs(v.Z) > 1e-4f ? MathF.Atan2(v.Z, flat) * (180f / MathF.PI) : 0f;
    }

    /// <summary>
    /// <paramref name="current"/> turned towards <paramref name="target"/> by at most
    /// <paramref name="maxStep"/> degrees, the short way round; normalised.
    /// </summary>
    public static float Turn(float current, float target, float maxStep)
    {
        float d = Difference(target, current);
        if (MathF.Abs(d) <= maxStep)
            return Normalise(target);
        return Normalise(current + MathF.CopySign(maxStep, d));
    }
}
