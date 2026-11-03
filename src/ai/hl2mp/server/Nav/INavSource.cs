namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// One free-space leaf of a <c>.nav3d</c>, as the derivation reads it: its
/// column's footprint, the run's bottom and top, the player's floor under it,
/// and its flags.
/// </summary>
public readonly record struct NavLeaf(
    float MinX,
    float MinY,
    float MaxX,
    float MaxY,
    float BottomZ,
    float TopZ,
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

/// <summary>A dynamic obstacle: the entity's name (or, unnamed, its classname and place), its kind and where the map puts it.</summary>
public readonly record struct NavObstacleInfo(string Name, NavObstacleKind Kind, string ClassName, AiVector Mins, AiVector Maxs);

/// <summary>A player-class jump link from one leaf: the leaf at its other end and how many columns apart the two are (1 a ledge, more a gap).</summary>
public readonly record struct NavJumpLink(int Leaf, int Columns);

/// <summary>
/// What the graph derivation needs of a <c>.nav3d</c> (plan_hl2mp_ai §6.2):
/// the questions map-tools' <c>Nav3dWalk</c> asks of its reader to find
/// which leaves a walking player reaches (format §7.6), answered for the
/// player clip class with every dynamic obstacle open. The adapter over the
/// real reader is <see cref="Nav3dNavSource"/>; the derivation is pure over
/// this, so its facts also run on a hand-built source.
/// </summary>
public interface INavSource
{
    /// <summary>The header's step height (the file's 18 matches <c>sv_stepsize</c>).</summary>
    float StepHeight { get; }

    /// <summary>The header's jump height: the highest jump link the file writes (56, a crouch jump).</summary>
    float JumpHeight { get; }

    /// <summary>The voxel's edge, in world units (a column's width).</summary>
    float VoxelSize { get; }

    int LeafCount { get; }

    NavLeaf Leaf(int leaf);

    /// <summary>
    /// Whether a <paramref name="width"/> × <paramref name="height"/> body
    /// stands on <paramref name="leaf"/>'s floor with its origin at the
    /// column's centre (the reader's <c>StandableAtCentre</c>): a walkable
    /// floor it fits on.
    /// </summary>
    bool Stands(int leaf, float width, float height);

    /// <summary>
    /// Whether <paramref name="leaf"/> is a place to climb: a ladder leaf the
    /// body fits in at the centre of its bottom voxel (<c>Nav3dWalk.IsClimb</c>).
    /// </summary>
    bool Climbs(int leaf, float width, float height);

    /// <summary>Whether the body fits at the centre of <paramref name="leaf"/>'s column with its feet at <paramref name="originZ"/>, inside the run.</summary>
    bool FitsAt(int leaf, float originZ, float width, float height);

    /// <summary>
    /// Whether the body fits at the centre of <paramref name="leaf"/>'s column
    /// with its feet at <paramref name="feet"/>, asked of the first voxel
    /// whose bottom is at or above the feet, as map-tools' <c>Nav3dWalk</c>
    /// asks the column a step crosses (MapFormats 1.0.2,
    /// <c>Nav3dWalk.Crosses</c>).
    /// </summary>
    bool FitsAbove(int leaf, float feet, float width, float height);

    /// <summary>The leaves of the column <paramref name="dx"/>, <paramref name="dy"/> columns from <paramref name="leaf"/>'s, low to high, into <paramref name="into"/>; answers how many.</summary>
    int ColumnLeaves(int leaf, int dx, int dy, Span<int> into);

    /// <summary>The leaf touching <paramref name="leaf"/> from below in its column, or -1.</summary>
    int Below(int leaf);

    /// <summary>The leaf touching <paramref name="leaf"/> from above in its column, or -1.</summary>
    int Above(int leaf);

    /// <summary>The leaves a player-class jump link joins to <paramref name="leaf"/> (both directions are listed), into <paramref name="into"/>.</summary>
    int Jumps(int leaf, Span<NavJumpLink> into);

    int ObstacleCount { get; }

    NavObstacleInfo Obstacle(int obstacle);

    /// <summary>The leaves <paramref name="obstacle"/>'s records name, into <paramref name="into"/>.</summary>
    int ObstacleLeaves(int obstacle, Span<int> into);

    /// <summary>
    /// <see cref="Stands"/> with <paramref name="obstacle"/> in the way (a
    /// shut door, a prop at its place) and every other obstacle open: a leaf
    /// that stands open but not shut is one the obstacle occupies.
    /// </summary>
    bool StandsWithShut(int leaf, int obstacle, float width, float height);
}
