namespace SourceSharp.Ai;

/// <summary>
/// A place in the experience store (plan_bots_teams_tf2 Part G, G.3): a box of
/// the world <see cref="Size"/> units square and <see cref="Height"/> tall,
/// named by its whole-number coordinates. Places are bins, never nav cell ids:
/// the navigation is rebuilt and its cells renumbered, and what players did at
/// a place must outlive that.
/// </summary>
public readonly record struct WorldBin(int X, int Y, int Z)
{
    /// <summary>A bin's side in the ground plane.</summary>
    public const float Size = 64f;

    /// <summary>A bin's height: a standing player's, so two floors are two bins.</summary>
    public const float Height = 72f;

    private const int Bias = 1 << 20;
    private const long Mask = (1L << 21) - 1;

    /// <summary>The bin holding <paramref name="point"/>: each bin owns its low faces, so a point on a face belongs to the bin above it.</summary>
    public static WorldBin Of(AiVector point)
        => new((int)MathF.Floor(point.X / Size), (int)MathF.Floor(point.Y / Size), (int)MathF.Floor(point.Z / Height));

    /// <summary>The middle of the bin.</summary>
    public AiVector Centre => new((X + 0.5f) * Size, (Y + 0.5f) * Size, (Z + 0.5f) * Height);

    /// <summary>
    /// The bin as one number, for a store's key column: 21 bits a coordinate,
    /// which holds any bin within a million of the origin on each axis (a
    /// Source map ends 256 bins out).
    /// </summary>
    public long Packed => ((X + Bias) & Mask) | (((Y + Bias) & Mask) << 21) | (((Z + Bias) & Mask) << 42);

    /// <summary>The bin <see cref="Packed"/> named.</summary>
    public static WorldBin Unpack(long packed)
        => new((int)(packed & Mask) - Bias, (int)((packed >> 21) & Mask) - Bias, (int)((packed >> 42) & Mask) - Bias);
}

/// <summary>
/// The buckets a view direction is kept in (G.3's <c>look</c> table):
/// <see cref="YawCount"/> around, each centred on a multiple of 22.5 degrees,
/// and <see cref="PitchCount"/> up and down (up, level, down).
/// </summary>
public static class LookBuckets
{
    public const int YawCount = 16;

    public const int PitchCount = 3;

    /// <summary>Past this many degrees from level a view is looking up or down.</summary>
    public const float LevelDegrees = 20f;

    private const float YawStep = 360f / YawCount;

    /// <summary>The yaw bucket of <paramref name="yawDegrees"/> (any angle): bucket 0 is centred on 0.</summary>
    public static int Yaw(float yawDegrees)
    {
        float turned = (yawDegrees + YawStep * 0.5f) % 360f;
        if (turned < 0f)
            turned += 360f;
        return Math.Min(YawCount - 1, (int)(turned / YawStep));
    }

    /// <summary>The pitch bucket of <paramref name="pitchDegrees"/>, the engine's pitch (negative is up): 0 up, 1 level, 2 down.</summary>
    public static int Pitch(float pitchDegrees) => pitchDegrees < -LevelDegrees ? 0 : pitchDegrees > LevelDegrees ? 2 : 1;

    /// <summary>The yaw at the middle of <paramref name="bucket"/>, in [0, 360).</summary>
    public static float YawCentre(int bucket) => bucket * YawStep;

    /// <summary>A pitch in the middle of <paramref name="bucket"/>: 45 up, level, 45 down.</summary>
    public static float PitchCentre(int bucket) => bucket switch { 0 => -45f, 2 => 45f, _ => 0f };
}
