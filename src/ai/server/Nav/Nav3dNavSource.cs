using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// <see cref="INavSource"/> over map-tools' <see cref="Nav3dReader"/>
/// (plan_hl2mp_ai §6.2, Phase 2 step 3): the player clip class, every
/// dynamic obstacle open, and a body's fit asked at the voxel's centre
/// (<c>StandableAtCentre</c>, <c>FitsAtCentre</c>; format §7.6), the
/// question <c>Nav3dWalker.Player</c> asks. The plain fit (origin anywhere
/// in the voxel) needs 48 units across for a 32-wide player and closes
/// dm_lockdown's narrow passages and ladders (§12.4).
/// </summary>
public sealed class Nav3dNavSource(Nav3dReader reader) : INavSource
{
    private const Nav3dClipClass Player = Nav3dClipClass.Player;

    // One obstacle shut at a time, the rest open: reused by StandsWithShut.
    private readonly bool[] _blocking = new bool[reader.ObstacleCount];

    public Nav3dReader Reader => reader;

    public float StepHeight => reader.StepHeight;

    public float JumpHeight => reader.JumpHeight;

    public float VoxelSize => reader.VoxelSize;

    public int LeafCount => reader.LeafCount;

    public NavLeaf Leaf(int leaf)
    {
        Nav3dLeaf l = reader.Leaf(leaf);
        (Vec3 mins, Vec3 maxs) = reader.LeafBounds(leaf);
        bool grounded = l.IsGrounded(Player);
        return new NavLeaf(mins.X, mins.Y, maxs.X, maxs.Y, mins.Z, maxs.Z, grounded ? l.FloorZ(Player) : mins.Z,
                           grounded && l.IsWalkable(Player), (l.Flags & Nav3dLeafFlags.Water) != 0,
                           (l.Flags & Nav3dLeafFlags.Ladder) != 0, l.CostMultiplier);
    }

    public bool Stands(int leaf, float width, float height) => reader.StandableAtCentre(leaf, width, height, Player);

    public bool Climbs(int leaf, float width, float height)
    {
        Nav3dLeaf l = reader.Leaf(leaf);
        return (l.Flags & Nav3dLeafFlags.Ladder) != 0 && reader.PassableAtCentre(leaf, l.ZLo, width, height, Player);
    }

    public bool FitsAt(int leaf, float originZ, float width, float height)
    {
        Nav3dLeaf l = reader.Leaf(leaf);
        (Vec3 mins, _) = reader.LeafBounds(leaf);
        int z = l.ZLo + (int)MathF.Floor((originZ - mins.Z) / reader.VoxelSize);
        return z >= l.ZLo && z <= l.ZHi && reader.FitsAtCentre(leaf, z, width, height, originZ, Player);
    }

    public bool FitsAbove(int leaf, float feet, float width, float height)
    {
        Nav3dLeaf l = reader.Leaf(leaf);
        (Vec3 mins, _) = reader.LeafBounds(leaf);
        int z = l.ZLo + (int)MathF.Ceiling((feet - mins.Z) / reader.VoxelSize - 1e-6f);
        return z >= l.ZLo && z <= l.ZHi && reader.FitsAtCentre(leaf, z, width, height, feet, Player);
    }

    public int ColumnLeaves(int leaf, int dx, int dy, Span<int> into)
    {
        (int cell, int x, int y) = reader.LeafColumn(leaf);
        int n = reader.CellVoxels;
        int gx = (cell % reader.Columns) * n + x + dx, gy = (cell / reader.Columns) * n + y + dy;
        if (gx < 0 || gy < 0 || gx >= reader.Columns * n || gy >= reader.Rows * n)
            return 0;
        (int first, int end) = reader.ColumnLeaves((gy / n) * reader.Columns + gx / n, gx % n, gy % n);
        if (end - first > into.Length)
            throw new InvalidDataException($"a column holds {end - first} leaves; the derivation reads at most {into.Length}");
        for (int l = first; l < end; l++)
            into[l - first] = l;
        return end - first;
    }

    public int Below(int leaf) => reader.LeafBelow(leaf);

    public int Above(int leaf) => leaf + 1 < reader.LeafCount && reader.LeafBelow(leaf + 1) == leaf ? leaf + 1 : -1;

    public int Jumps(int leaf, Span<NavJumpLink> into)
    {
        int count = 0;
        foreach (int j in reader.Jumps(leaf))
        {
            Nav3dJump jump = reader.Jump(j);
            if ((jump.ClassMask & 1) == 0)
                continue;
            if (count >= into.Length)
                throw new InvalidDataException($"leaf {leaf} has more than {into.Length} jump links");
            into[count++] = new NavJumpLink(jump.LeafA == (uint)leaf ? (int)jump.LeafB : (int)jump.LeafA, jump.Columns);
        }

        return count;
    }

    public int ObstacleCount => reader.ObstacleCount;

    public NavObstacleInfo Obstacle(int obstacle)
    {
        Nav3dObstacle o = reader.Obstacle(obstacle);
        var mins = new AiVector(o.Mins.X, o.Mins.Y, o.Mins.Z);
        var maxs = new AiVector(o.Maxs.X, o.Maxs.Y, o.Maxs.Z);
        string name = string.IsNullOrEmpty(o.Name) ? UnnamedName(o.ClassName, mins, maxs) : o.Name;
        return new NavObstacleInfo(name, (NavObstacleKind)Math.Clamp((int)o.Kind - 1, 0, (int)NavObstacleKind.Prop), o.ClassName, mins, maxs);
    }

    /// <summary>What an obstacle without a targetname is called: its classname and the centre the map gives it.</summary>
    public static string UnnamedName(string className, AiVector mins, AiVector maxs)
    {
        AiVector c = (mins + maxs) * 0.5f;
        return FormattableString.Invariant($"{className}@{c.X:F0},{c.Y:F0},{c.Z:F0}");
    }

    public int ObstacleLeaves(int obstacle, Span<int> into)
    {
        ReadOnlySpan<int> leaves = reader.ObstacleLeaves(obstacle);
        if (leaves.Length > into.Length)
            throw new InvalidDataException($"obstacle {obstacle} names {leaves.Length} leaves; the buffer holds {into.Length}");
        leaves.CopyTo(into);
        return leaves.Length;
    }

    public bool StandsWithShut(int leaf, int obstacle, float width, float height)
    {
        _blocking[obstacle] = true;
        try
        {
            return reader.StandableAtCentre(leaf, width, height, Player, _blocking);
        }
        finally
        {
            _blocking[obstacle] = false;
        }
    }
}
