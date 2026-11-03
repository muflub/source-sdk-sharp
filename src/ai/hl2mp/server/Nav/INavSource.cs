namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>One free-space leaf of a <c>.nav3d</c>, as the derivation reads it.</summary>
public readonly record struct NavLeaf(
    float MinX,
    float MinY,
    float MaxX,
    float MaxY,
    float FloorZ,
    bool Walkable,
    bool Water,
    bool Ladder,
    float CostMultiplier);

/// <summary>What kind of dynamic obstacle a <c>.nav3d</c> names (map-tools format §10).</summary>
public enum NavObstacleKind : byte
{
    Door = 0,
    Mover,
    Toggle,
    Breakable,
    Physics,
    Prop,
}

/// <summary>A dynamic obstacle: the entity's name and its kind.</summary>
public readonly record struct NavObstacleInfo(string Name, NavObstacleKind Kind);

/// <summary>
/// What the graph derivation needs of a <c>.nav3d</c> (plan_hl2mp_ai §6.2),
/// shaped on map-tools' <c>Nav3dReader</c> as the plan describes it (§1.1:
/// <c>FindLeaf</c>, <c>Neighbours</c>, <c>Standable</c>, <c>StepUp</c>,
/// <c>Jumps</c>, obstacle leaves; neighbours and components derived at
/// <c>Open</c>). The binding reads the file only through this, so the
/// derivation and its facts need no reader; the adapter over the real
/// <c>Nav3dReader</c> lands with the <c>SourceSharp.MapFormats</c> package
/// (Phase 2 steps 2-3).
/// </summary>
public interface INavSource
{
    /// <summary>The header's step height (the file's 18 matches <c>sv_stepsize</c>).</summary>
    float StepHeight { get; }

    /// <summary>The header's jump height: the highest jump link the file writes (56, a crouch jump).</summary>
    float JumpHeight { get; }

    int LeafCount { get; }

    NavLeaf Leaf(int leaf);

    /// <summary>
    /// Whether a <paramref name="width"/> × <paramref name="height"/> player box
    /// stands on <paramref name="leaf"/> at <paramref name="z"/>, with the
    /// dynamic obstacles open (<paramref name="shut"/> false) or shut.
    /// </summary>
    bool Standable(int leaf, float z, float width, float height, bool shut);

    /// <summary>The leaves beside <paramref name="leaf"/>, into <paramref name="into"/>; answers how many.</summary>
    int Neighbours(int leaf, Span<int> into);

    /// <summary>The rise from <paramref name="from"/>'s floor to <paramref name="to"/>'s (negative going down).</summary>
    float StepUp(int from, int to);

    /// <summary>The leaves a jump link joins to <paramref name="leaf"/> (both directions are listed), into <paramref name="into"/>.</summary>
    int Jumps(int leaf, Span<int> into);

    int ObstacleCount { get; }

    NavObstacleInfo Obstacle(int obstacle);

    /// <summary>The leaves <paramref name="obstacle"/> occupies when open, into <paramref name="into"/>.</summary>
    int ObstacleLeaves(int obstacle, Span<int> into);
}
