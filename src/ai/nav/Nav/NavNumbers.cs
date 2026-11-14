namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// A game's body and movement numbers, as the derivation splits a
/// <c>.nav3d</c>'s jump links and classes its cells by (plan_hl2mp_ai §6.2):
/// the player hull's width, standing and crouched heights, the step, the
/// highest plain jump, the highest crouch jump, the highest safe drop, and the
/// highest rise a double jump reaches (0 for a game without one: no edge is a
/// <see cref="NavEdgeType.DoubleJump"/>), and whether the hull is wider than two voxels
/// (<see cref="WideHull"/>, TF2's 48 over 16 u voxels): such a box at a column's centre
/// reaches into the columns beside it, so the derivation stands it on a stair's step a step
/// up, and drops it past a ledge as the two-voxel body that clears the edge it leaves.
/// <see cref="CrouchOnlyCost"/> multiplies what a crouch-only place costs to cross (1, HL2MP's: as any
/// place), for a game whose players all but never duck to get round: TF2's.
/// A game's <see cref="INavProfile"/>
/// publishes them; <c>Stock</c> (Ai.Server's StockNavNumbers, read from the movement code) is
/// the core's default movement, HL2MP's (NavNumbersTests pins them).
/// <see cref="ExtraHulls"/> are more bodies the cells are classed for, beside the game's own:
/// none unless a game names some, and then nothing but their bits is added to the graph.
/// </summary>
public readonly record struct NavNumbers(
    float HullWidth,
    float StandHeight,
    float CrouchHeight,
    float StepHeight,
    float MaxJumpRise,
    float MaxCrouchJumpRise,
    float SafeDrop,
    float MaxDoubleJumpRise = 0f,
    bool WideHull = false,
    float CrouchOnlyCost = 1f)
{
    /// <summary>
    /// The extra hulls, in order: hull i is <see cref="ClearanceClass"/> 2 + i
    /// (<see cref="NavHulls.ClassOf"/>), admitted in a cell where its box stands. They add no cell
    /// and no edge: the places and the moves are the game's own body's. Part of a cached graph's
    /// name only when there are any, so a game without them keeps its graphs' names and hashes.
    /// </summary>
    public NavHulls ExtraHulls { get; init; }
}

/// <summary>A body the derivation classes cells for beside the game's own: a box <see cref="Width"/> across and <see cref="Height"/> tall.</summary>
public readonly record struct NavHull(float Width, float Height);

/// <summary>
/// An ordered list of at most <see cref="Max"/> extra hulls (<see cref="NavNumbers.ExtraHulls"/>),
/// compared by what it holds, so two numbers naming the same hulls are the same numbers; the
/// default is the empty list.
/// </summary>
public readonly struct NavHulls : IEquatable<NavHulls>
{
    /// <summary>The most hulls a list holds: the clearance classes past Stand and Crouch.</summary>
    public const int Max = 6;

    private readonly NavHull[]? _hulls;

    private NavHulls(NavHull[] hulls) => _hulls = hulls;

    /// <summary>The list of <paramref name="hulls"/>, in their order: each with a width and a height above zero, at most <see cref="Max"/> of them.</summary>
    public static NavHulls Of(params ReadOnlySpan<NavHull> hulls)
    {
        if (hulls.Length > Max)
            throw new ArgumentException($"{hulls.Length} extra hulls: a graph is classed for at most {Max}", nameof(hulls));
        foreach (NavHull h in hulls)
        {
            if (!(h.Width > 0f) || !(h.Height > 0f) || !float.IsFinite(h.Width) || !float.IsFinite(h.Height))
                throw new ArgumentException($"a hull has a width and a height above zero, not {h.Width} by {h.Height}", nameof(hulls));
        }

        return hulls.Length == 0 ? default : new NavHulls(hulls.ToArray());
    }

    public int Count => _hulls?.Length ?? 0;

    public NavHull this[int index] => _hulls is { } hulls ? hulls[index] : throw new ArgumentOutOfRangeException(nameof(index), index, "the list is empty");

    /// <summary>The class paths are planned by for extra hull <paramref name="hull"/> (0 the first): 2 + <paramref name="hull"/>.</summary>
    public static ClearanceClass ClassOf(int hull)
    {
        if ((uint)hull >= Max)
            throw new ArgumentOutOfRangeException(nameof(hull), hull, $"extra hulls run 0..{Max - 1}");
        return (ClearanceClass)(2 + hull);
    }

    public bool Equals(NavHulls other) => ((ReadOnlySpan<NavHull>)_hulls).SequenceEqual(other._hulls);

    public override bool Equals(object? obj) => obj is NavHulls other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (NavHull h in (ReadOnlySpan<NavHull>)_hulls)
            hash.Add(h);
        return hash.ToHashCode();
    }

    public static bool operator ==(NavHulls a, NavHulls b) => a.Equals(b);

    public static bool operator !=(NavHulls a, NavHulls b) => !a.Equals(b);

    public override string ToString() => $"[{string.Join(", ", (_hulls ?? []).Select(h => FormattableString.Invariant($"{h.Width}x{h.Height}")))}]";
}

/// <summary>What crossing each edge type adds to its distance (a jump's effort, a door's wait), in world units.</summary>
public readonly record struct NavEdgeCosts(float Jump, float CrouchJump, float Drop, float Ladder, float DoorWait, float DoubleJump = 48f)
{
    public static NavEdgeCosts Default => new(16f, 32f, 8f, 64f, 64f, 48f);

    /// <summary>What crossing an edge of <paramref name="type"/> adds to its distance.</summary>
    public float ExtraFor(NavEdgeType type) => type switch
    {
        NavEdgeType.Jump => Jump,
        NavEdgeType.CrouchJump => CrouchJump,
        NavEdgeType.Drop => Drop,
        NavEdgeType.Ladder => Ladder,
        NavEdgeType.Door => DoorWait,
        NavEdgeType.DoubleJump => DoubleJump,
        _ => 0f,
    };
}
