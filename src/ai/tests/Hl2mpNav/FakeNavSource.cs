using SourceSharp.Ai.Hl2mp.Nav;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// A hand-built <see cref="INavSource"/>: leaves with a floor and a ceiling,
/// neighbours and jump links named by hand. A box stands on a leaf when the
/// gap from floor to ceiling holds its height.
/// </summary>
internal sealed class FakeNavSource : INavSource
{
    private readonly List<(NavLeaf Leaf, float Ceiling)> _leaves = new();
    private readonly List<List<int>> _neighbours = new();
    private readonly List<List<int>> _jumps = new();
    private readonly List<(NavObstacleInfo Info, int[] Leaves)> _obstacles = new();

    public float StepHeight => 18f;

    public float JumpHeight => 56f;

    public int LeafCount => _leaves.Count;

    /// <summary>A walkable 16-unit leaf at column (x, y) with its floor and ceiling heights.</summary>
    public int Add(int x, int y, float floor, float ceiling = 1000f, bool walkable = true, bool water = false, bool ladder = false)
    {
        _leaves.Add((new NavLeaf(x * 16f, y * 16f, x * 16f + 16f, y * 16f + 16f, floor, walkable, water, ladder, water ? 2f : 1f), ceiling));
        _neighbours.Add(new List<int>());
        _jumps.Add(new List<int>());
        return _leaves.Count - 1;
    }

    public void Beside(int a, int b)
    {
        _neighbours[a].Add(b);
        _neighbours[b].Add(a);
    }

    public void JumpLink(int a, int b)
    {
        _jumps[a].Add(b);
        _jumps[b].Add(a);
    }

    public int Door(string name, params int[] leaves)
    {
        _obstacles.Add((new NavObstacleInfo(name, NavObstacleKind.Door), leaves));
        return _obstacles.Count - 1;
    }

    public void Prop(string name, params int[] leaves) => _obstacles.Add((new NavObstacleInfo(name, NavObstacleKind.Physics), leaves));

    public NavLeaf Leaf(int leaf) => _leaves[leaf].Leaf;

    public bool Standable(int leaf, float z, float width, float height, bool shut) => _leaves[leaf].Ceiling - z >= height;

    public int Neighbours(int leaf, Span<int> into) => Copy(_neighbours[leaf], into);

    public float StepUp(int from, int to) => _leaves[to].Leaf.FloorZ - _leaves[from].Leaf.FloorZ;

    public int Jumps(int leaf, Span<int> into) => Copy(_jumps[leaf], into);

    public int ObstacleCount => _obstacles.Count;

    public NavObstacleInfo Obstacle(int obstacle) => _obstacles[obstacle].Info;

    public int ObstacleLeaves(int obstacle, Span<int> into) => Copy(_obstacles[obstacle].Leaves, into);

    private static int Copy(IReadOnlyList<int> from, Span<int> into)
    {
        for (int i = 0; i < from.Count; i++)
            into[i] = from[i];
        return from.Count;
    }
}
