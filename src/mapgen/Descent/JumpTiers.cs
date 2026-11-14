namespace SourceSharp.MapGen.Descent;

/// <summary>A jump tier (plan_descent_mine_levels.md §4.1): who can reach a ledge or cross a gap.</summary>
public enum JumpTier
{
    /// <summary>Everyone, walking: a step.</summary>
    T0,

    /// <summary>Everyone, a crouch-jump.</summary>
    T1,

    /// <summary>The Scout's double jump.</summary>
    S2,

    /// <summary>The Scout's triple jump (Atomizer or an <c>air_dash_count</c> item).</summary>
    S3,

    /// <summary>The Soldier's rocket jump without crouching; any sticky jump.</summary>
    B1,

    /// <summary>The Soldier's crouched rocket jump.</summary>
    B2,

    /// <summary>The Demoman's sticky jump.</summary>
    B3,
}

/// <summary>
/// One row of the tier table: the ledge heights (above the floor a player
/// jumps from) and the gaps (edge to edge) the tier is built at, each inside
/// what its class clears with a margin and above what the class below it
/// reaches.
/// </summary>
/// <param name="Who">Who reaches it, as the plan's table says.</param>
public sealed record JumpTierSpec(JumpTier Tier, string Who, int LedgeMin, int LedgeMax, int GapMin, int GapMax)
{
    /// <summary>How high the air must be clear above a ledge at <paramref name="ledge"/>: the hull plus a margin over it.</summary>
    public static int ClearAbove(int ledge) => ledge + JumpTable.LedgeClearance;

    /// <summary>
    /// A ledge height of this tier on the navigation grid: the multiples of
    /// <paramref name="voxel"/> inside the tier's range, picked by
    /// <paramref name="t"/> from 0 (the lowest) to 1 (the highest). Every
    /// room height is a whole number of voxels, and so is every ledge.
    /// </summary>
    public int Ledge(float t = 0.5f, int voxel = RoomKit.DefaultNavVoxel) => Pick(LedgeMin, LedgeMax, t, voxel, "ledge");

    /// <summary>A gap of this tier, edge to edge, picked as <see cref="Ledge"/> picks a height.</summary>
    public int Gap(float t = 0.5f, int step = RoomKit.DefaultNavVoxel) => Pick(GapMin, GapMax, t, step, "gap");

    private int Pick(int min, int max, float t, int step, string what)
    {
        if (t is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(t), t, "a pick is a fraction from 0 to 1");
        int first = (min + step - 1) / step * step, last = max / step * step;
        if (last < first)
            throw new InvalidOperationException($"tier {Tier} has no {what} on a {step}-unit grid between {min} and {max}");
        int count = (last - first) / step;
        return first + (int)MathF.Round(t * count) * step;
    }
}

/// <summary>
/// The jump-tier table (plan §4.1, task A5), measured in Phase 0 (§1.3):
/// every shape generator takes its ledges and gaps from here, never from a
/// literal.
/// </summary>
public static class JumpTable
{
    /// <summary>The clear air a ledge needs above it: TF2's 82-unit standing hull and a 32 margin.</summary>
    public const int LedgeClearance = 114;

    /// <summary>A rocket-jump room's clear air over half its floor: the crouched jump's 509 and the ducked hull.</summary>
    public const int RocketClearAir = 576;

    /// <summary>A rocket-jump room's straight run.</summary>
    public const int RocketRun = 768;

    /// <summary>A sticky spot's clear air: shaft rooms only.</summary>
    public const int StickyClearAir = 1104;

    /// <summary>The seven tiers, in order.</summary>
    public static IReadOnlyList<JumpTierSpec> Tiers { get; } =
    [
        new(JumpTier.T0, "everyone, walking", 0, 18, 0, 0),
        new(JumpTier.T1, "everyone, crouch-jump", 19, 64, 0, 160),
        new(JumpTier.S2, "Scout, double jump", 88, 104, 400, 512),
        new(JumpTier.S3, "Scout, triple jump", 136, 144, 704, 768),
        new(JumpTier.B1, "Soldier, rocket jump uncrouched; any sticky", 192, 256, 0, 448),
        new(JumpTier.B2, "Soldier, crouched rocket jump", 320, 448, 0, 640),
        new(JumpTier.B3, "Demoman, sticky jump", 640, 768, 0, 1024),
    ];

    /// <summary>A tier's row.</summary>
    public static JumpTierSpec Of(JumpTier tier) => Tiers[(int)tier];

    /// <summary>The highest ledge everyone reaches: the critical path stays at or under it (M8).</summary>
    public static int CriticalPathReach => Of(JumpTier.T1).LedgeMax;
}
