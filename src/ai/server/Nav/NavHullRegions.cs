using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Server.Nav;

/// <summary>
/// The regions of connected free space in a hull navigation (plan_bots_teams_tf2_part_g
/// G.15): two boxes are in one region when a chain of boxes joins them, each touching the
/// next across a side face or from above or below (<see cref="NavHullReader.Neighbours"/>),
/// whether or not a body stands in them. Air joins what the walk graph does not: the
/// platforms of a jump map's room are one region, and two rooms sealed from each other are
/// two. Regions are numbered in the order of their lowest box.
/// </summary>
public sealed class NavHullRegions
{
    private readonly int[] _regionOfBox;

    // Region r's boxes are _boxes[_start[r].._start[r + 1]), ascending.
    private readonly int[] _start;
    private readonly int[] _boxes;

    private NavHullRegions(int[] regionOfBox, int[] start, int[] boxes)
    {
        _regionOfBox = regionOfBox;
        _start = start;
        _boxes = boxes;
    }

    /// <summary>The regions of <paramref name="reader"/>'s boxes.</summary>
    public static NavHullRegions Of(NavHullReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int boxes = reader.BoxCount;
        var parent = new int[boxes];
        for (int b = 0; b < boxes; b++)
            parent[b] = b;
        for (int b = 0; b < boxes; b++)
        {
            foreach (NavHullNeighbour n in reader.Neighbours(b))
            {
                int x = Root(parent, b), y = Root(parent, n.Box);
                if (x != y)
                    parent[Math.Max(x, y)] = Math.Min(x, y);
            }
        }

        // A root is its region's lowest box, so numbering roots in box order numbers regions by their lowest box.
        var regionOfBox = new int[boxes];
        int regions = 0;
        for (int b = 0; b < boxes; b++)
        {
            int root = Root(parent, b);
            regionOfBox[b] = root == b ? regions++ : regionOfBox[root];
        }

        var start = new int[regions + 1];
        for (int b = 0; b < boxes; b++)
            start[regionOfBox[b] + 1]++;
        for (int r = 0; r < regions; r++)
            start[r + 1] += start[r];
        var at = (int[])start.Clone();
        var list = new int[boxes];
        for (int b = 0; b < boxes; b++)
            list[at[regionOfBox[b]]++] = b;
        return new NavHullRegions(regionOfBox, start, list);
    }

    private static int Root(int[] parent, int b)
    {
        while (parent[b] != b)
        {
            parent[b] = parent[parent[b]];
            b = parent[b];
        }

        return b;
    }

    public int Count => _start.Length - 1;

    /// <summary>The region <paramref name="box"/> is in.</summary>
    public int RegionOf(int box) => _regionOfBox[box];

    /// <summary>Region <paramref name="region"/>'s boxes, ascending.</summary>
    public ReadOnlySpan<int> Boxes(int region) => _boxes.AsSpan(_start[region], _start[region + 1] - _start[region]);

    /// <summary>Region <paramref name="region"/>'s leaves as <paramref name="source"/> numbers them: each of its boxes' columns.</summary>
    public int[] Leaves(NavHullNavSource source, int region)
    {
        ArgumentNullException.ThrowIfNull(source);
        var leaves = new List<int>();
        foreach (int box in Boxes(region))
        {
            NavHullBox b = source.Reader.Box(box);
            int first = source.LeafOf(box, b.X0, b.Y0), count = (b.X1 - b.X0) * (b.Y1 - b.Y0);
            for (int i = 0; i < count; i++)
                leaves.Add(first + i);
        }

        return [.. leaves];
    }
}
