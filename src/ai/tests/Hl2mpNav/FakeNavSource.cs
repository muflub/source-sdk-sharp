using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// A hand-built <see cref="INavSource"/>: one leaf per 16-unit column at
/// (x, y), with a floor and a ceiling, and jump links named by hand. A body
/// stands on a leaf when the gap from floor to ceiling holds its height;
/// columns beside each other are the leaves at the next x or y.
/// </summary>
internal sealed class FakeNavSource : INavSource
{
    private readonly List<(NavLeaf Leaf, int X, int Y, float Ceiling)> _leaves = new();
    private readonly List<List<NavJumpLink>> _jumps = new();
    private readonly List<(NavObstacleInfo Info, int[] Leaves)> _obstacles = new();

    public float StepHeight => 18f;

    public float JumpHeight => 56f;

    public float VoxelSize => 16f;

    public int LeafCount => _leaves.Count;

    /// <summary>A 16-unit leaf at column (x, y) with its floor and ceiling heights.</summary>
    public int Add(int x, int y, float floor, float ceiling = 1000f, bool walkable = true, bool water = false, bool ladder = false)
    {
        _leaves.Add((new NavLeaf(x * 16f, y * 16f, x * 16f + 16f, y * 16f + 16f, floor, ceiling, floor, walkable, water, ladder,
                                 water ? 2f : 1f), x, y, ceiling));
        _jumps.Add(new List<NavJumpLink>());
        return _leaves.Count - 1;
    }

    public void JumpLink(int a, int b)
    {
        int columns = Math.Max(Math.Abs(_leaves[a].X - _leaves[b].X), Math.Abs(_leaves[a].Y - _leaves[b].Y));
        _jumps[a].Add(new NavJumpLink(b, columns));
        _jumps[b].Add(new NavJumpLink(a, columns));
    }

    public int Door(string name, params int[] leaves) => Obstacle(name, NavObstacleKind.Door, leaves);

    public int Prop(string name, params int[] leaves) => Obstacle(name, NavObstacleKind.Physics, leaves);

    private int Obstacle(string name, NavObstacleKind kind, int[] leaves)
    {
        _obstacles.Add((new NavObstacleInfo(name, kind, kind == NavObstacleKind.Door ? "func_door" : "prop_physics", default, default),
                        leaves));
        return _obstacles.Count - 1;
    }

    public NavLeaf Leaf(int leaf) => _leaves[leaf].Leaf;

    public bool Stands(int leaf, float width, float height) => _leaves[leaf].Leaf.Walkable && _leaves[leaf].Ceiling - _leaves[leaf].Leaf.FloorZ >= height;

    public bool Climbs(int leaf, float width, float height) => _leaves[leaf].Leaf.Ladder && _leaves[leaf].Ceiling - _leaves[leaf].Leaf.BottomZ >= height;

    public bool FitsAbove(int leaf, float feet, float width, float height) => FitsAt(leaf, feet, width, height);

    public bool FitsAt(int leaf, float originZ, float width, float height)
        => originZ >= _leaves[leaf].Leaf.BottomZ && _leaves[leaf].Ceiling - originZ >= height;

    public int ColumnLeaves(int leaf, int dx, int dy, Span<int> into)
    {
        int x = _leaves[leaf].X + dx, y = _leaves[leaf].Y + dy, count = 0;
        foreach (int i in Enumerable.Range(0, _leaves.Count).Where(i => _leaves[i].X == x && _leaves[i].Y == y)
                     .OrderBy(i => _leaves[i].Leaf.BottomZ))
            into[count++] = i;
        return count;
    }

    public int Below(int leaf) => -1;

    public int Above(int leaf) => -1;

    public int Jumps(int leaf, Span<NavJumpLink> into)
    {
        for (int i = 0; i < _jumps[leaf].Count; i++)
            into[i] = _jumps[leaf][i];
        return _jumps[leaf].Count;
    }

    public int ObstacleCount => _obstacles.Count;

    public NavObstacleInfo Obstacle(int obstacle) => _obstacles[obstacle].Info;

    public int ObstacleLeaves(int obstacle, Span<int> into)
    {
        _obstacles[obstacle].Leaves.CopyTo(into);
        return _obstacles[obstacle].Leaves.Length;
    }

    public bool StandsWithShut(int leaf, int obstacle, float width, float height)
        => !_obstacles[obstacle].Leaves.Contains(leaf) && Stands(leaf, width, height);
}
