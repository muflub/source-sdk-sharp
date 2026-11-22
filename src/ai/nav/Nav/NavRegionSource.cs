namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// <see cref="INavSource"/> over one region of another source: a set of its
/// leaves, renumbered from 0 in the order the other source numbers them, with
/// every answer about a leaf outside the set left out (plan_bots_teams_tf2_part_g
/// G.15, a jump map's graph a room at a time). <see cref="NavGraphFromNav3d.Derive"/>
/// runs on it unchanged and yields the graph of the region alone.
/// <list type="bullet">
/// <item><b>Leaves</b> are the other source's: a leaf's bounds, flags and fits
/// are asked of it under its own number.</item>
/// <item><b>Columns, neighbours and jump links</b> are the other source's, less
/// the leaves outside the region: a column beside answers only its leaves in the
/// region, a leaf touching from below or above outside it is none (-1), and a
/// jump link to a leaf outside it is not listed.</item>
/// <item><b>Obstacles</b> are all the other source's, in its order (a graph
/// obstacle is a file record whatever the region), each naming only its leaves
/// in the region.</item>
/// </list>
/// A region that holds every leaf answers as the other source does, leaf for leaf.
/// </summary>
public sealed class NavRegionSource : INavSource
{
    private readonly INavSource _inner;

    // The region's leaves, as the other source numbers them, ascending: leaf i here is _leaves[i] there.
    private readonly int[] _leaves;

    // Each obstacle's leaves in the region, renumbered.
    private readonly int[][] _obstacleLeaves;

    // When the region was made over a scratch: the other source's leaf -> this region's, -1 for none.
    private readonly NavRegionScratch? _scratch;

    private NavRegionSource(INavSource inner, int[] leaves, int[][] obstacleLeaves, NavRegionScratch? scratch = null)
    {
        _inner = inner;
        _leaves = leaves;
        _obstacleLeaves = obstacleLeaves;
        _scratch = scratch;
    }

    /// <summary>
    /// The region of <paramref name="scratch"/>'s source that holds <paramref name="leaves"/>, as
    /// <see cref="Over(INavSource, IEnumerable{int})"/> answers it, with its lookups in
    /// <paramref name="scratch"/>: a leaf's number here is read from an array, not searched, and the
    /// obstacles' leaves are the ones the scratch read once. The scratch serves one region at a
    /// time: <see cref="Release"/> it before the next.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scratch serves another region.</exception>
    public static NavRegionSource Over(NavRegionScratch scratch, IEnumerable<int> leaves)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentNullException.ThrowIfNull(leaves);
        if (scratch.InUse)
            throw new InvalidOperationException("the scratch serves another region: release it first");
        INavSource inner = scratch.Source;
        int[] sorted = [.. leaves];
        Array.Sort(sorted);
        int distinct = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            if (distinct == 0 || sorted[i] != sorted[distinct - 1])
                sorted[distinct++] = sorted[i];
        }

        Array.Resize(ref sorted, distinct);
        if (sorted.Length > 0 && (sorted[0] < 0 || sorted[^1] >= inner.LeafCount))
        {
            int bad = sorted[0] < 0 ? sorted[0] : sorted[^1];
            throw new ArgumentOutOfRangeException(nameof(leaves), bad, $"the source has {inner.LeafCount} leaves");
        }

        int[] local = scratch.Local;
        for (int i = 0; i < sorted.Length; i++)
            local[sorted[i]] = i;
        scratch.InUse = true;
        var obstacles = new int[scratch.ObstacleInner.Length][];
        for (int o = 0; o < obstacles.Length; o++)
        {
            var mine = new List<int>();
            foreach (int leaf in scratch.ObstacleInner[o])
            {
                if (local[leaf] >= 0)
                    mine.Add(local[leaf]);
            }

            obstacles[o] = [.. mine];
        }

        return new NavRegionSource(inner, sorted, obstacles, scratch);
    }

    /// <summary>Gives the scratch this region was made over back for the next region; the region answers nothing after.</summary>
    public void Release()
    {
        if (_scratch is null || !_scratch.InUse)
            return;
        foreach (int leaf in _leaves)
            _scratch.Local[leaf] = -1;
        _scratch.InUse = false;
    }

    /// <summary>
    /// The region of <paramref name="inner"/> that holds <paramref name="leaves"/> (in any order,
    /// repeats allowed): numbers them and reads which of each obstacle's leaves are in it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A leaf is not one of <paramref name="inner"/>'s.</exception>
    public static NavRegionSource Over(INavSource inner, IEnumerable<int> leaves)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(leaves);
        int[] sorted = [.. leaves];
        Array.Sort(sorted);
        int distinct = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            if (distinct == 0 || sorted[i] != sorted[distinct - 1])
                sorted[distinct++] = sorted[i];
        }

        Array.Resize(ref sorted, distinct);
        if (sorted.Length > 0 && (sorted[0] < 0 || sorted[^1] >= inner.LeafCount))
        {
            int bad = sorted[0] < 0 ? sorted[0] : sorted[^1];
            throw new ArgumentOutOfRangeException(nameof(leaves), bad, $"the source has {inner.LeafCount} leaves");
        }

        var obstacles = new int[inner.ObstacleCount][];
        if (obstacles.Length > 0)
        {
            // An obstacle may name any of the other source's leaves: read each into room for all of them.
            var all = new int[Math.Max(1, inner.LeafCount)];
            for (int o = 0; o < obstacles.Length; o++)
            {
                int count = inner.ObstacleLeaves(o, all);
                var mine = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int at = Array.BinarySearch(sorted, all[i]);
                    if (at >= 0)
                        mine.Add(at);
                }

                obstacles[o] = [.. mine];
            }
        }

        return new NavRegionSource(inner, sorted, obstacles);
    }

    /// <summary>The other source's number for this region's <paramref name="leaf"/>.</summary>
    public int InnerLeaf(int leaf) => _leaves[leaf];

    /// <summary>This region's number for the other source's <paramref name="inner"/>, or -1 when the region does not hold it.</summary>
    public int LeafOf(int inner)
    {
        if (_scratch is not null)
            return _scratch.Local[inner];
        int at = Array.BinarySearch(_leaves, inner);
        return at >= 0 ? at : -1;
    }

    public float StepHeight => _inner.StepHeight;

    public float JumpHeight => _inner.JumpHeight;

    public float VoxelSize => _inner.VoxelSize;

    public int LeafCount => _leaves.Length;

    public NavLeaf Leaf(int leaf) => _inner.Leaf(_leaves[leaf]);

    public bool Stands(int leaf, float width, float height) => _inner.Stands(_leaves[leaf], width, height);

    public bool Climbs(int leaf, float width, float height) => _inner.Climbs(_leaves[leaf], width, height);

    public bool FitsAt(int leaf, float originZ, float width, float height) => _inner.FitsAt(_leaves[leaf], originZ, width, height);

    public bool FitsAbove(int leaf, float feet, float width, float height) => _inner.FitsAbove(_leaves[leaf], feet, width, height);

    public int ColumnLeaves(int leaf, int dx, int dy, Span<int> into)
    {
        // The other source's answer, then kept in place where the region holds it: a kept leaf is never ahead of the one read.
        int count = _inner.ColumnLeaves(_leaves[leaf], dx, dy, into), kept = 0;
        for (int i = 0; i < count; i++)
        {
            int mine = LeafOf(into[i]);
            if (mine >= 0)
                into[kept++] = mine;
        }

        return kept;
    }

    public int Below(int leaf)
    {
        int other = _inner.Below(_leaves[leaf]);
        return other < 0 ? -1 : LeafOf(other);
    }

    public int Above(int leaf)
    {
        int other = _inner.Above(_leaves[leaf]);
        return other < 0 ? -1 : LeafOf(other);
    }

    public int Jumps(int leaf, Span<NavJumpLink> into)
    {
        int count = _inner.Jumps(_leaves[leaf], into), kept = 0;
        for (int i = 0; i < count; i++)
        {
            int mine = LeafOf(into[i].Leaf);
            if (mine >= 0)
                into[kept++] = into[i] with { Leaf = mine };
        }

        return kept;
    }

    public int ObstacleCount => _obstacleLeaves.Length;

    public NavObstacleInfo Obstacle(int obstacle) => _inner.Obstacle(obstacle);

    public int ObstacleLeaves(int obstacle, Span<int> into)
    {
        int[] leaves = _obstacleLeaves[obstacle];
        if (leaves.Length > into.Length)
            throw new InvalidDataException($"obstacle {obstacle} names {leaves.Length} leaves of the region; the buffer holds {into.Length}");
        leaves.CopyTo(into);
        return leaves.Length;
    }

    public bool StandsWithShut(int leaf, int obstacle, float width, float height) => _inner.StandsWithShut(_leaves[leaf], obstacle, width, height);
}

/// <summary>
/// What many regions of one source share when they are made in turn
/// (<see cref="NavRegionSource.Over(NavRegionScratch, IEnumerable{int})"/>): an array from the
/// source's leaf to the region's, and each obstacle's leaves, read once.
/// </summary>
public sealed class NavRegionScratch
{
    private NavRegionScratch(INavSource source, int[] local, int[][] obstacleInner)
    {
        Source = source;
        Local = local;
        ObstacleInner = obstacleInner;
    }

    /// <summary>The scratch for regions of <paramref name="source"/>.</summary>
    public static NavRegionScratch For(INavSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var local = new int[source.LeafCount];
        Array.Fill(local, -1);
        var obstacles = new int[source.ObstacleCount][];
        if (obstacles.Length > 0)
        {
            var all = new int[Math.Max(1, source.LeafCount)];
            for (int o = 0; o < obstacles.Length; o++)
                obstacles[o] = all.AsSpan(0, source.ObstacleLeaves(o, all)).ToArray();
        }

        return new NavRegionScratch(source, local, obstacles);
    }

    /// <summary>Another scratch for regions of the same source, made at the same time as this one's (another thread's): its own array, the obstacles' leaves shared.</summary>
    public NavRegionScratch Fork()
    {
        var local = new int[Local.Length];
        Array.Fill(local, -1);
        return new NavRegionScratch(Source, local, ObstacleInner);
    }

    internal INavSource Source { get; }

    // The source's leaf -> the region's in use, -1 for none.
    internal int[] Local { get; }

    internal int[][] ObstacleInner { get; }

    internal bool InUse { get; set; }
}
