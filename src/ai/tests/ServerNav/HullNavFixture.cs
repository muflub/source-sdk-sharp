using SourceSharp.MapFormats.Geometry;
using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Writes a real <c>.navhull</c> with map-tools' own <see cref="NavHullWriter"/>
/// from boxes named by hand, for the facts that read one through the real
/// reader: 16-unit voxels, the grid's low corner at the origin, the widths 32
/// and 48. A box is added to a cell (cells in rising order) with a key; a key
/// is its flags, its floor and a ceiling per width (and, under a sloped brush, its overhang record).
/// </summary>
internal sealed class HullNavFixture(int cellsX, int cellsY, int cellVoxels, int columnVoxels)
{
    public const float Voxel = 16f;

    private readonly List<NavHullBox> _boxes = new();
    private readonly List<int> _cellOfBox = new();
    private readonly List<NavHullKey> _keys = new();
    private readonly List<NavHullShut> _shuts = new();
    private readonly List<NavHullOverhang> _overhangs = new();
    private readonly List<Nav3dObstacle> _obstacles = new();
    private readonly List<NavHullJump> _jumps = new();

    public IReadOnlyList<float> Widths { get; init; } = [32f, 48f];

    public Nav3dBspBinding? Bsp { get; set; }

    /// <summary>A key: what a box's voxels are, the floor under its bottom, and the ceiling a 32-wide and a 48-wide body meet.</summary>
    public uint Key(NavHullFlags flags, float floor, float ceiling32, float ceiling48)
    {
        _keys.Add(new NavHullKey(flags, 256, floor, Ceilings(ceiling32, ceiling48), (uint)_shuts.Count, 0, (uint)_overhangs.Count, 0));
        return (uint)(_keys.Count - 1);
    }

    /// <summary>
    /// A key as <see cref="Key"/> under a sloped brush: every width passes under it with its top at
    /// most <paramref name="under"/> and stands clear over it with its feet at least <paramref name="over"/>.
    /// </summary>
    public uint KeyUnderOverhang(NavHullFlags flags, float floor, float ceiling32, float ceiling48, double under, double over)
    {
        _keys.Add(new NavHullKey(flags, 256, floor, Ceilings(ceiling32, ceiling48), (uint)_shuts.Count, 0, (uint)_overhangs.Count, 1));
        _overhangs.Add(new NavHullOverhang([.. Widths.Select(_ => under)], [.. Widths.Select(_ => over)]));
        return (uint)(_keys.Count - 1);
    }

    /// <summary>A key as <see cref="Key"/>, whose ceilings with <paramref name="obstacle"/> shut are the two given.</summary>
    public uint KeyShutBy(NavHullFlags flags, float floor, float ceiling32, float ceiling48, int obstacle, float shut32, float shut48)
    {
        _keys.Add(new NavHullKey(flags, 256, floor, Ceilings(ceiling32, ceiling48), (uint)_shuts.Count, 1, (uint)_overhangs.Count, 0));
        _shuts.Add(new NavHullShut((uint)obstacle, Ceilings(shut32, shut48)));
        return (uint)(_keys.Count - 1);
    }

    private float[] Ceilings(float ceiling32, float ceiling48)
        => [.. Widths.Select(w => w == 32f ? ceiling32 : w == 48f ? ceiling48 : float.NegativeInfinity)];

    /// <summary>A box over columns [x0, x1) × [y0, y1) of <paramref name="cell"/>, <paramref name="height"/> voxels up from voxel <paramref name="zLo"/>; answers its index.</summary>
    public int Box(int cell, int x0, int y0, int x1, int y1, int zLo, int height, uint key)
    {
        if (_cellOfBox.Count > 0 && cell < _cellOfBox[^1])
            throw new ArgumentException("boxes are added cell by cell, in rising order");
        _boxes.Add(new NavHullBox((byte)x0, (byte)y0, (byte)x1, (byte)y1, (ushort)zLo, (ushort)height, key));
        _cellOfBox.Add(cell);
        return _boxes.Count - 1;
    }

    public int Obstacle(string? name, string className, Nav3dObstacleKind kind, Vec3 mins, Vec3 maxs)
    {
        _obstacles.Add(new Nav3dObstacle(name, className, 0, -1, kind, mins, maxs));
        return _obstacles.Count - 1;
    }

    /// <summary>A jump link from column (ax, ay) of box <paramref name="a"/> to column (bx, by) of box <paramref name="b"/>, the higher-numbered.</summary>
    public void Jump(int a, int ax, int ay, int b, int bx, int by, float rise, Nav3dDirection direction, int columns = 1)
        => _jumps.Add(new NavHullJump((uint)a, (uint)b, (byte)ax, (byte)ay, (byte)bx, (byte)by, rise, direction, (byte)columns));

    public NavHullLevel Level()
    {
        int cells = cellsX * cellsY;
        var starts = new uint[cells + 1];
        foreach (int cell in _cellOfBox)
            starts[cell + 1]++;
        for (int c = 0; c < cells; c++)
            starts[c + 1] += starts[c];
        return new NavHullLevel
        {
            VoxelSize = Voxel,
            CellVoxels = cellVoxels,
            Columns = cellsX,
            Rows = cellsY,
            Origin = new Vec3(0, 0, 0),
            ColumnVoxels = columnVoxels,
            FloorNormalZ = 0.7f,
            StepHeight = 18f,
            JumpHeight = 56f,
            JumpDistance = 96f,
            Widths = Widths,
            CellStarts = starts,
            Boxes = [.. _boxes],
            Keys = _keys,
            Shuts = _shuts,
            Overhangs = _overhangs,
            Obstacles = _obstacles,
            Jumps = _jumps,
            Bsp = Bsp,
        };
    }

    public byte[] Write() => NavHullWriter.Write(Level());

    public NavHullReader Open() => NavHullReader.Open(Write());
}

/// <summary>The hull navigations the loader's facts read.</summary>
internal static class HullNavFixtures
{
    /// <summary>
    /// Two cells of four columns a side, each one floor box at z 16 under a ceiling at 144: 32
    /// places a body stands, bound to <paramref name="bsp"/> when one is given.
    /// </summary>
    public static HullNavFixture Floor(Nav3dBspBinding? bsp = null)
    {
        var f = new HullNavFixture(2, 1, 4, 16) { Bsp = bsp };
        uint floor = f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 144f, 144f);
        f.Box(0, 0, 0, 4, 4, 1, 8, floor);
        f.Box(1, 0, 0, 4, 4, 1, 8, floor);
        return f;
    }
}
